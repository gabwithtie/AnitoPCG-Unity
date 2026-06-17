using Gbe.ShapeGrammar;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Gbe.ShapeGrammar
{
    public class RuntimeGraphTree : Tree
    {
        private IStep _resolvedRootStep;
        private List<IStep> _sortedExecutionList;
        private Dictionary<string, IStep> _stepLookup;

        /// <summary>
        /// Reads the asset, builds lookups, and correctly sorts the nodes topologically 
        /// so that all dependencies (both geometry and math values) are executed in the correct order.
        /// </summary>
        public void InitializeFromAsset(GrammarGraphAsset asset)
        {
            if (asset == null || asset.serializedSteps.Count == 0) return;

            // 1. Create lookup maps and clear structural caches
            _stepLookup = new Dictionary<string, IStep>();
            foreach (var step in asset.serializedSteps)
            {
                step.before.Clear();
                step.branchCache = new Dictionary<int, List<Shape>>();
                _stepLookup[step.guid] = step;
            }

            // 2. Topologically sort the graph based on both Geometry AND Value dependencies
            _sortedExecutionList = SortStepsTopologically(asset.serializedSteps);

            // 3. Build fast structural edges for geometry flow
            foreach (var step in _sortedExecutionList)
            {
                foreach (var parentGuid in step.beforeGuids)
                {
                    if (_stepLookup.TryGetValue(parentGuid, out IStep parentStep))
                    {
                        step.before.Add(parentStep);
                    }
                }
            }

            // Explicit root lookup
            _resolvedRootStep = asset.serializedSteps.Find(s => s.isMasterNode)
                                ?? asset.serializedSteps.LastOrDefault();
        }

        /// <summary>
        /// Executes the grammar graph using a safe two-pass system:
        /// Pass 1 resolves all math and dynamic parameter bindings.
        /// Pass 2 generates and routes all geometry down the tree.
        /// </summary>
        public List<Shape> Evaluate(List<System.Numerics.Vector3> seedVertices)
        {
            if (_sortedExecutionList == null || _sortedExecutionList.Count == 0)
                return new List<Shape>();

            // 1. INITIALIZE CACHES
            foreach (var step in _sortedExecutionList)
            {
                if (step.branchCache != null) step.branchCache.Clear();
                // Clear operation computed outputs to ensure a fresh calculation frame
                step.GetOperation()?.ComputedOutputs?.Clear();
            }

            // 2. PASS 1: RESOLVE MATH & VALUE DEPENDENCIES
            Shape dummyMathShape = new Shape(); // Math operations don't need real geometry

            foreach (IStep step in _sortedExecutionList)
            {
                // Inject bound values from upstream (guaranteed to be ready because of topological sort)
                GrammarEngineUtility.ResolveValueBindings(step, _stepLookup);

                Operation op = step.GetOperation();
                if (op != null)
                {
                    bool isMathNode = op.GetType().Name.Contains("Math") || op.GetType().Name.Contains("Value");
                    if (isMathNode)
                    {
                        // Trigger pure math nodes so their ComputedOutputs are populated for downstream readers
                        op.Apply(dummyMathShape);
                    }
                }
            }

            // 3. PASS 2: GEOMETRY GENERATION
            Shape initialShape = new Shape(seedVertices);
            List<Shape> seedList = new List<Shape> { initialShape };

            foreach (IStep step in _sortedExecutionList)
            {
                if (step.isMasterNode) continue;

                Operation op = step.GetOperation();
                if (op != null)
                {
                    bool isMathNode = op.GetType().Name.Contains("Math") || op.GetType().Name.Contains("Value");
                    if (!isMathNode)
                    {
                        List<Shape> inputShapes = GatherUpstreamShapesFromGuids(step, seedList, _stepLookup);
                        List<Shape> outputShapes = new List<Shape>();

                        foreach (var shape in inputShapes)
                        {
                            var results = op.Apply(shape);
                            if (results != null) outputShapes.AddRange(results);
                        }

                        GrammarEngineUtility.StoreDownstreamShapes(step, outputShapes);
                    }
                }
            }

            // 4. EXTRACT FINAL RESULT
            return _resolvedRootStep != null
                ? GatherUpstreamShapesFromGuids(_resolvedRootStep, seedList, _stepLookup)
                : new List<Shape>();
        }

        public override IStep GetRootStep() => _resolvedRootStep;

        // =================================================================
        // PIPELINE HELPER UTILITIES
        // =================================================================

        private List<IStep> SortStepsTopologically(List<IStep> steps)
        {
            List<IStep> sorted = new List<IStep>();
            HashSet<string> visited = new HashSet<string>();
            HashSet<string> visiting = new HashSet<string>();

            Dictionary<string, IStep> stepMap = new Dictionary<string, IStep>();
            foreach (var s in steps)
            {
                if (s != null && !string.IsNullOrEmpty(s.guid))
                    stepMap[s.guid] = s;
            }

            void Visit(IStep step)
            {
                if (visited.Contains(step.guid)) return;
                if (visiting.Contains(step.guid)) return; // Prevents circular infinite loops safely

                visiting.Add(step.guid);

                // 1. Dependency Check: Geometry Data Flow
                if (step.beforeGuids != null)
                {
                    foreach (var beforeGuid in step.beforeGuids)
                    {
                        if (stepMap.TryGetValue(beforeGuid, out var prereq))
                            Visit(prereq);
                    }
                }

                // 2. CRITICAL DEPENDENCY CHECK: Math/Value Data Flow
                if (step.valueBindings != null)
                {
                    foreach (var binding in step.valueBindings)
                    {
                        if (!string.IsNullOrEmpty(binding.sourceStepGuid) && stepMap.TryGetValue(binding.sourceStepGuid, out var prereq))
                            Visit(prereq);
                    }
                }

                visiting.Remove(step.guid);
                visited.Add(step.guid);
                sorted.Add(step);
            }

            foreach (var s in steps)
            {
                if (s != null) Visit(s);
            }

            return sorted;
        }

        private List<Shape> GatherUpstreamShapesFromGuids(IStep currentStep, List<Shape> seedShapes, Dictionary<string, IStep> allStepsLookup)
        {
            List<Shape> inputShapes = new List<Shape>();

            // Root nodes with no connections behind them ingest the baseline shape data
            if (currentStep.beforeGuids == null || currentStep.beforeGuids.Count == 0)
            {
                if (seedShapes != null) inputShapes.AddRange(seedShapes);
                return inputShapes;
            }

            // Otherwise, harvest shapes across all connected input ports
            foreach (string upstreamGuid in currentStep.beforeGuids)
            {
                if (allStepsLookup.TryGetValue(upstreamGuid, out IStep upstreamStep))
                {
                    if (upstreamStep == null || upstreamStep.branchCache == null) continue;

                    foreach (var cacheBranch in upstreamStep.branchCache)
                    {
                        if (cacheBranch.Value != null)
                        {
                            inputShapes.AddRange(cacheBranch.Value);
                        }
                    }
                }
            }

            return inputShapes;
        }
    }
}