using Gbe.ShapeGrammar;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Gbe.ShapeGrammar
{
    public class RuntimeGraphTree : Tree
    {
        public string TreeId { get; set; } = Guid.NewGuid().ToString();

        private IStep _resolvedRootStep;
        private Dictionary<string, IStep> _stepLookup;

        /// <summary>
        /// Reads the asset and builds lookups and quick geometry flow links.
        /// Topological pre-sorting is omitted in favor of runtime cached recursion.
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


                //Reload operation values
                step.HydrateSavedOverrides();
            }

            // 2. Build fast structural edges for geometry flow
            foreach (var step in asset.serializedSteps)
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
        /// Executes the grammar graph dynamically using a cached recursive strategy.
        /// Supports a two-pass lifecycle to resolve cross-tree spatial dependencies.
        /// </summary>
        override protected List<Shape> EvaluateImplementation(List<System.Numerics.Vector3> seedVertices, bool finalPass = true)
        {
            if (_stepLookup == null || _stepLookup.Count == 0 || _resolvedRootStep == null)
                return new List<Shape>();

            // 1. Set the global context for the static registry
            SpatialGraphRegistry.CurrentTreeId = this.TreeId;

            // 2. Clear caches ONLY on the initial registration pass
            if (!finalPass)
            {
                foreach (var step in _stepLookup.Values)
                {
                    if (step.branchCache != null) step.branchCache.Clear();
                    step.GetOperation()?.ComputedOutputs?.Clear();
                }
            }

            HashSet<string> evaluatedSteps = new HashSet<string>();
            HashSet<string> evaluatingSteps = new HashSet<string>();

            Shape dummyMathShape = new Shape();
            Shape initialShape = new Shape(seedVertices);
            List<Shape> seedList = new List<Shape> { initialShape };

            if (!finalPass)
            {
                // PASS 1: Find the footprint/publish nodes and pull them backwards 
                // This stops evaluation early, registering data without evaluating queries.
                var spatialSteps = _stepLookup.Values.Where(s =>
                    s.GetOperation() is MarkTreeFootprint ||
                    s.GetOperation() is PublishSpatialDependency).ToList();

                foreach (var step in spatialSteps)
                {
                    EvaluateStep(step, seedList, evaluatedSteps, evaluatingSteps, dummyMathShape);
                }

                return new List<Shape>(); // Pass 1 only registers metadata
            }
            else
            {
                // PASS 2: Evaluate the entire graph normally from the Master Sink Root.
                // It will instantly reuse the cached geometry generated during Pass 1.
                EvaluateStep(_resolvedRootStep, seedList, evaluatedSteps, evaluatingSteps, dummyMathShape);

                // Clean context tracking hook
                SpatialGraphRegistry.CurrentTreeId = null;

                return GatherUpstreamShapesFromGuids(_resolvedRootStep, seedList, _stepLookup);
            }
        }

        /// <summary>
        /// Recursive helper function that guarantees all dependency nodes (Value & Geometry)
        /// are fully generated and cached before evaluating the requested step.
        /// </summary>
        private void EvaluateStep(IStep step, List<Shape> seedList, HashSet<string> evaluatedSteps, HashSet<string> evaluatingSteps, Shape dummyMathShape)
        {
            if (step == null || string.IsNullOrEmpty(step.guid)) return;

            // Memoization check: skip if this node was already computed by another execution branch
            if (evaluatedSteps.Contains(step.guid)) return;

            // Cyclic graph protection
            if (evaluatingSteps.Contains(step.guid))
            {
                UnityEngine.Debug.LogWarning($"[RuntimeGraphTree] Circular dependency detected at step GUID: {step.guid}. Breaking recursion.");
                return;
            }

            evaluatingSteps.Add(step.guid);

            // PASS A: Evaluate Prerequisite Math/Value Steps first (Value Bindings)
            if (step.valueBindings != null)
            {
                foreach (var binding in step.valueBindings)
                {
                    if (!string.IsNullOrEmpty(binding.sourceStepGuid) && _stepLookup.TryGetValue(binding.sourceStepGuid, out var prereq))
                    {
                        EvaluateStep(prereq, seedList, evaluatedSteps, evaluatingSteps, dummyMathShape);
                    }
                }
            }

            // PASS B: Evaluate Upstream Geometry Parent Steps (Execution Wires)
            if (step.beforeGuids != null)
            {
                foreach (var beforeGuid in step.beforeGuids)
                {
                    if (_stepLookup.TryGetValue(beforeGuid, out var prereq))
                    {
                        EvaluateStep(prereq, seedList, evaluatedSteps, evaluatingSteps, dummyMathShape);
                    }
                }
            }

            // Now that all prerequisites are guaranteed to be ready, perform value injection
            GrammarEngineUtility.ResolveValueBindings(step, _stepLookup);

            // PASS C: Execute this specific node's operations
            if (!step.isMasterNode)
            {
                Operation op = step.GetOperation();
                if (op != null)
                {
                    bool isMathNode = op.GetType().Name.Contains("Math") || op.GetType().Name.Contains("Value");

                    // Always gather upstream shapes so inline math nodes pass geometry flow through seamlessly
                    List<Shape> inputShapes = GatherUpstreamShapesFromGuids(step, seedList, _stepLookup);

                    if (isMathNode)
                    {
                        // Provide the live shape context if available so math nodes can read live geometry metadata/attributes
                        Shape contextualShape = (inputShapes != null && inputShapes.Count > 0) ? inputShapes[0] : dummyMathShape;
                        op.Apply(contextualShape);
                    }
                    else
                    {
                        // Standard geometry execution
                        List<Shape> outputShapes = new List<Shape>();

                        var results = op.ApplySet(inputShapes);
                        if (results != null) outputShapes.AddRange(results);

                        GrammarEngineUtility.StoreDownstreamShapes(step, outputShapes);
                    }
                }
            }

            // Mark this node clean
            evaluatingSteps.Remove(step.guid);
            evaluatedSteps.Add(step.guid);
        }

        public override IStep GetRootStep() => _resolvedRootStep;

        // =================================================================
        // PIPELINE HELPER UTILITIES
        // =================================================================

        /// <summary>
        /// Gathers incoming shapes from upstream execution dependencies. 
        /// Seamlessly tunnels through inline math/value nodes to preserve continuous geometry data flow.
        /// </summary>
        private List<Shape> GatherUpstreamShapesFromGuids(IStep currentStep, List<Shape> seedShapes, Dictionary<string, IStep> allStepsLookup)
        {
            List<Shape> inputShapes = new List<Shape>();

            // Root nodes with no connections behind them ingest the baseline shape data
            if (currentStep.beforeGuids == null || currentStep.beforeGuids.Count == 0)
            {
                if (seedShapes != null) inputShapes.AddRange(seedShapes);
                return inputShapes;
            }

            // Otherwise, harvest shapes across all connected input execution ports
            foreach (string upstreamGuid in currentStep.beforeGuids)
            {
                if (allStepsLookup.TryGetValue(upstreamGuid, out IStep upstreamStep))
                {
                    if (upstreamStep == null) continue;

                    // Detect if the upstream connection point belongs to a math/value node
                    Operation op = upstreamStep.GetOperation();
                    bool isMathNode = op != null && (op.GetType().Name.Contains("Math") || op.GetType().Name.Contains("Value"));

                    if (isMathNode)
                    {
                        // FIX: Math nodes act as a pass-through transparent bridge for geometry.
                        // Recursively fetch the geometry running into the back of this math node.
                        inputShapes.AddRange(GatherUpstreamShapesFromGuids(upstreamStep, seedShapes, allStepsLookup));
                    }
                    else if (upstreamStep.branchCache != null)
                    {
                        // Standard geometry node: harvest its cached evaluated shapes
                        foreach (var cacheBranch in upstreamStep.branchCache)
                        {
                            if (cacheBranch.Value != null)
                            {
                                inputShapes.AddRange(cacheBranch.Value);
                            }
                        }
                    }
                }
            }

            return inputShapes;
        }
    }
}