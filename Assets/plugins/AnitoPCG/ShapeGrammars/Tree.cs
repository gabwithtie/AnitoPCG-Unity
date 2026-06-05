using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class TreeParameters
    {
        public Dictionary<string, IParameter> parameters = new Dictionary<string, IParameter>();

        public void SetFromVector(List<IParameter> paramsList)
        {
            foreach (IParameter source in paramsList)
            {
                if (parameters.TryGetValue(source.id, out IParameter target))
                {
                    if (target.parameterType == source.parameterType)
                    {
                        // Clean C# pattern matching instead of explicit switch/casting blocks
                        switch (source.parameterType)
                        {
                            case ParameterType.INT:
                                if (source is Parameter<int> srcInt && target is Parameter<int> tgtInt)
                                    tgtInt.Set(srcInt.value);
                                break;
                            case ParameterType.FLOAT:
                                if (source is Parameter<float> srcFloat && target is Parameter<float> tgtFloat)
                                    tgtFloat.Set(srcFloat.value);
                                break;
                        }
                    }
                    else
                    {
                        Debug.LogError($"Parameter type mismatch for parameter: {source.id}");
                    }
                }
                else
                {
                    Debug.LogError($"Parameter not found: {source.id}");
                }
            }
        }
    }

    public class ApplyParams
    {
        public int targetStage = -1;
        public int currentStage = 0;
        public List<Shape> outStageShapes = null;
        public Shape initial;
        public Tree owningTree = null;
        public SpatialDependencyRegistry registry;
        public TreeParameters parametersCaptured;
        public bool isMainThreadDryRun = false;
    }

    public abstract class Tree : MonoBehaviour
    {
        public AsyncOperationDispatcher asyncDispatcher = new AsyncOperationDispatcher();
        public TreeParameters parameters = new TreeParameters();
        public SpatialDependencyRegistry registry;

        [Header("Research Metadata & Thread Sync")]
        public string treeId;
        public string groupType;

        public abstract IStep GetRootStep();

        public void RegisterToRegistry(SpatialDependencyRegistry registryInstance, Shape initialShape)
        {
            registryInstance.RegisterTree(this, initialShape);
            registry = registryInstance;
        }

        // Core Graph Execution Driver
        public List<List<Shape>> Apply(IStep step, ApplyParams paramsPack)
        {
            if (asyncDispatcher.IsRunning && asyncDispatcher.IsStopping())
            {
                return new List<List<Shape>>();
            }

            List<List<Shape>> upstreamBranches = new List<List<Shape>>();

            // 1. Gather Dependencies & Pull Structural Branches
            if (step.before.Count == 0)
            {
                upstreamBranches.Add(new List<Shape> { paramsPack.initial });
            }
            else
            {
                foreach (IStep dependency in step.before)
                {
                    var depBranches = Apply(dependency, paramsPack);

                    if (asyncDispatcher.IsRunning && asyncDispatcher.IsStopping())
                    {
                        return new List<List<Shape>>();
                    }
                    upstreamBranches.AddRange(depBranches);
                }
            }

            // Early Truncation
            if (paramsPack.targetStage != -1 && paramsPack.currentStage > paramsPack.targetStage)
            {
                return new List<List<Shape>>();
            }

            // 2. Cross-Stream Sync Point (Crucial for Boolean Intersections)
            if (step.flattenInputs && upstreamBranches.Count > 0)
            {
                List<Shape> flatStream = new List<Shape>();
                foreach (var branch in upstreamBranches)
                {
                    flatStream.AddRange(branch);
                }
                upstreamBranches.Clear();
                upstreamBranches.Add(flatStream);
            }

            // 3. Sync Global Parameters
            var operationalParams = step.GetOperation().parameters;
            foreach (var param in paramsPack.parametersCaptured.parameters)
            {
                operationalParams[param.Value.id] = param.Value;
            }

            // 4. Parameter Cache Invalidation
            bool paramsDirty = false;
            foreach (var kvp in operationalParams)
            {
                if (kvp.Value != null && kvp.Value.isDirty)
                {
                    paramsDirty = true;
                    break;
                }
            }
            if (paramsDirty)
            {
                step.branchCache.Clear();
            }

            step.GetOperation().currentParams = paramsPack;

            List<List<Shape>> finalOutputBranches = new List<List<Shape>>();
            foreach (List<Shape> branchInput in upstreamBranches)
            {
                if (asyncDispatcher.IsStopping()) return new List<List<Shape>>();
                if (branchInput.Count == 0) continue;

                // ==========================================
                // PHASE 1: FRAMEWORK-LEVEL DEPENDENCY RESOLUTION
                // ==========================================
                List<Shape> resolvedDependencies = new List<Shape>();

                if (step.dependencies.Count > 0 && registry != null && !paramsPack.isMainThreadDryRun)
                {
                    List<Task<List<Shape>>> dependencyTasks = new List<Task<List<Shape>>>();

                    foreach (var dep in step.dependencies)
                    {
                        var matchingTasks = registry.GetStageDependencies(this, dep.dependencyType, dep.targetStage);
                        dependencyTasks.AddRange(matchingTasks);
                    }

                    foreach (var task in dependencyTasks)
                    {
                        if (asyncDispatcher.IsStopping()) return new List<List<Shape>>();

                        if (asyncDispatcher.IsRunning)
                        {
                            asyncDispatcher.Pause();
                            asyncDispatcher.MarkYieldPoint(() => task.IsCompleted);
                        }

                        // Safely blocks worker thread/task without spinning CPU cycles
                        task.Wait();

                        if (asyncDispatcher.IsRunning)
                        {
                            asyncDispatcher.Unpause();
                        }

                        resolvedDependencies.AddRange(task.Result);
                    }
                }

                // ==========================================
                // PHASE 2: CACHE EVALUATION & EXECUTION
                // ==========================================
                int branchHash = Shape.GetVectorShapeHash(branchInput);
                List<Shape> branchOutput;

                bool bypassCache = step.isVolatile || step.dependencies.Count > 0 || paramsPack.isMainThreadDryRun;

                if (!bypassCache && step.branchCache.TryGetValue(branchHash, out var cachedShapes))
                {
                    branchOutput = cachedShapes;
                }
                else
                {
                    branchOutput = step.GetOperation().ApplySet(branchInput, resolvedDependencies);

                    if (!bypassCache)
                    {
                        step.branchCache[branchHash] = branchOutput;
                    }
                }

                // 6. Branch Splitting / Routing
                if (step.forEach)
                {
                    foreach (var shape in branchOutput)
                    {
                        finalOutputBranches.Add(new List<Shape> { shape });
                    }
                }
                else
                {
                    finalOutputBranches.Add(branchOutput);
                }

                if (asyncDispatcher.IsRunning && !paramsPack.isMainThreadDryRun)
                {
                    asyncDispatcher.MarkYieldPoint();
                }
            }

            // 7. Checkpoint Flag & Publishing Processing
            if (step.isStageCheckpoint)
            {
                List<Shape> flatCheckpointOutput = new List<Shape>();
                int stageIndex = paramsPack.currentStage;
                foreach (var branch in finalOutputBranches)
                {
                    flatCheckpointOutput.AddRange(branch);
                }

                if (!paramsPack.isMainThreadDryRun)
                {
                    asyncDispatcher.FulfillStage(stageIndex, flatCheckpointOutput);
                    if (asyncDispatcher.IsRunning)
                    {
                        asyncDispatcher.PublishState(flatCheckpointOutput);
                    }
                }

                if (paramsPack.targetStage != -1 && paramsPack.currentStage == paramsPack.targetStage)
                {
                    if (paramsPack.outStageShapes != null)
                    {
                        paramsPack.outStageShapes.Clear();
                        paramsPack.outStageShapes.AddRange(flatCheckpointOutput);
                    }
                }

                paramsPack.currentStage++;
            }

            asyncDispatcher.IncrementProgress();

            if (asyncDispatcher.IsRunning && !paramsPack.isMainThreadDryRun)
            {
                asyncDispatcher.MarkYieldPoint();
            }

            return finalOutputBranches;
        }

        public List<Shape> ApplySynchronous(Shape initialShape, int stage, bool isDryRun = false)
        {
            IStep root = GetRootStep();

            List<Shape> stageShapes = new List<Shape>();
            ApplyParams paramsPack = new ApplyParams
            {
                targetStage = stage,
                currentStage = 0,
                outStageShapes = stageShapes,
                owningTree = this,
                registry = this.registry,
                initial = initialShape,
                parametersCaptured = this.parameters,
                isMainThreadDryRun = isDryRun
            };

            var finalBranches = Apply(root, paramsPack);

            if (stage != -1 && stage < paramsPack.currentStage)
            {
                return stageShapes;
            }

            List<Shape> flatFinalOutput = new List<Shape>();
            foreach (var branch in finalBranches)
            {
                flatFinalOutput.AddRange(branch);
            }

            return flatFinalOutput;
        }

        public void PrepareForAsync()
        {
            asyncDispatcher.RequestStop();
            asyncDispatcher.ResetStagePromises();
        }

        public void ApplyAsync(Shape initialShape)
        {
            asyncDispatcher.Fire(() =>
            {
                var flatFinalOutput = ApplySynchronous(initialShape, -1, false);

                asyncDispatcher.MarkComplete();
                asyncDispatcher.FulfilAllBlank();

                if (!asyncDispatcher.IsStopping())
                {
                    asyncDispatcher.PublishState(flatFinalOutput);
                }
            });

            foreach (var kvp in parameters.parameters)
            {
                kvp.Value.isDirty = false;
            }
        }
    }
}