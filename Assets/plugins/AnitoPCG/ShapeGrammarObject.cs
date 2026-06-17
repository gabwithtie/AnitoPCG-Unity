using UnityEngine;
using System.Collections.Generic;
using System.Numerics;

using SysVector3 = System.Numerics.Vector3;
using System;

namespace Gbe.ShapeGrammar
{
    [ExecuteInEditMode]
    public class ShapeGrammarObject : MonoBehaviour
    {
        [Header("Pipeline Generation Graph Assets")]
        public GrammarGraphAsset graphAsset;
        public SubstitutionSchemeAsset substitutionAsset;

        [Header("Instantiation Settings")]
        public Transform prefabsInstantiationRoot;

        [HideInInspector]
        public List<UnityEngine.Vector3> inputVertices = new List<UnityEngine.Vector3>()
        {
            new UnityEngine.Vector3(-2, 0, -2),
            new UnityEngine.Vector3(-2, 0, 2),
            new UnityEngine.Vector3(2, 0, 2),
            new UnityEngine.Vector3(2, 0, -2)
        };

        [HideInInspector]
        public List<Shape> generatedTriangles = new List<Shape>();
        private RuntimeGraphTree tree = new RuntimeGraphTree();

        [ContextMenu("Execute Full Generation & Substitution")]
        public void ExecuteGrammarChain()
        {
            if (graphAsset == null)
            {
                Debug.LogWarning($"[ShapeGrammarObject] Please assign a valid GrammarGraphAsset before running.", this);
                return;
            }

            // =================================================================
            // STEP 1: RUN GEOMETRY GENERATION GRAPH (WITH VALUE BINDINGS MAPPED)
            // =================================================================

            // 1. Convert input canvas vertex vectors into world coordinate space
            List<SysVector3> sysVertices = new List<SysVector3>();
            foreach (var v in inputVertices)
            {
                UnityEngine.Vector3 worldPos = transform.TransformPoint(v);
                sysVertices.Add(new SysVector3(worldPos.x, worldPos.y, worldPos.z));
            }

            // Create our starting seed shape polygon
            Shape initialShape = new Shape(sysVertices);
            List<Shape> seedList = new List<Shape> { initialShape };

            // 2. Sort the serialized nodes topologically so dependencies execute in order
            List<IStep> sortedExecutionList = SortStepsTopologically(graphAsset.serializedSteps);

            // 3. Rebuild execution lookup maps and purge stale historical branch cache states
            Dictionary<string, IStep> allStepsLookup = new Dictionary<string, IStep>();
            foreach (var step in sortedExecutionList)
            {
                if (step.branchCache != null) step.branchCache.Clear();
                allStepsLookup[step.guid] = step;
            }

            // 4. Process each step sequentially down the sorted dependency pipeline
            foreach (IStep step in sortedExecutionList)
            {
                // Skip the master visual output node; it acts as an exit collector sink
                if (step.isMasterNode) continue;

                // CRITICAL TRIGGER: Inject math results into properties right before running
                GrammarEngineUtility.ResolveValueBindings(step, allStepsLookup);

                Operation op = step.GetOperation();
                if (op != null)
                {
                    // Gather geometry from upstream steps using serialized GUID wires
                    List<Shape> inputShapes = GatherUpstreamShapesFromGuids(step, seedList, allStepsLookup);
                    List<Shape> outputShapes = new List<Shape>();

                    // Apply operational manipulations (e.g. SubdivideQuad, Triangulate, Math operators)
                    foreach (var shape in inputShapes)
                    {
                        var results = op.Apply(shape);
                        if (results != null) outputShapes.AddRange(results);
                    }

                    // Store calculation arrays into local execution caches for downstream readers
                    GrammarEngineUtility.StoreDownstreamShapes(step, outputShapes);
                }
            }

            // 5. Extract final reporting results to hand over to substitution steps
            generatedTriangles = new List<Shape>();
            if (sortedExecutionList.Count > 0)
            {
                // Harvest from the explicit Master/Final Output node if it exists, otherwise fall back to the tail node
                var masterNode = sortedExecutionList.Find(s => s.isMasterNode);
                IStep finalReportingStep = masterNode ?? sortedExecutionList[sortedExecutionList.Count - 1];

                generatedTriangles = GatherUpstreamShapesFromGuids(finalReportingStep, seedList, allStepsLookup);
            }

            // =================================================================
            // STEP 2: RUN SUBSTITUTION SCHEME ASSET SYSTEM PIPELINE 
            // =================================================================
            if (substitutionAsset != null)
            {
                ClearInstantiatedPrefabs();
                ProcessSubstitutionGraph(generatedTriangles);
            }
        }

        // =================================================================
        // PIPELINE HELPER UTILITIES
        // =================================================================

        /// <summary>
        /// Directs graph compilation by sorting steps based on their wire connection paths (beforeGuids).
        /// </summary>
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

                if (step.beforeGuids != null)
                {
                    foreach (var beforeGuid in step.beforeGuids)
                    {
                        if (stepMap.TryGetValue(beforeGuid, out var prereq))
                        {
                            Visit(prereq);
                        }
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

        /// <summary>
        /// Inspects serialized wiring data matrices to pull shape collections out of upstream caches.
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

        private void ProcessSubstitutionGraph(List<Shape> shapes)
        {
            if (substitutionAsset.serializedSteps == null || substitutionAsset.serializedSteps.Count == 0) return;

            // Find the master entry point
            var masterInput = substitutionAsset.serializedSteps.Find(s => s.isMasterInput);
            if (masterInput == null) return;

            // Maps to track active routing states
            var nodeQueue = new Queue<ISubStep>();
            var shapesAtNode = new Dictionary<string, List<Shape>>();

            nodeQueue.Enqueue(masterInput);
            shapesAtNode[masterInput.guid] = new List<Shape>(shapes);

            var finalTerminalMaps = new Dictionary<GameObject, List<Shape>>();

            // Route data forward across branches
            while (nodeQueue.Count > 0)
            {
                var currentNode = nodeQueue.Dequeue();
                if (!shapesAtNode.TryGetValue(currentNode.guid, out var currentShapes) || currentShapes.Count == 0)
                    continue;

                if (currentNode is PrefabVectorMapStep finalMapNode)
                {
                    // Terminal branch node identified: Compile assignments data matrix 
                    var partialMap = finalMapNode.CompilePrefabAssignments(currentShapes);
                    foreach (var pair in partialMap)
                    {
                        if (!finalTerminalMaps.ContainsKey(pair.Key)) finalTerminalMaps[pair.Key] = new List<Shape>();
                        finalTerminalMaps[pair.Key].AddRange(pair.Value);
                    }
                    continue;
                }

                // Call evaluation to split lists matching branching definitions
                var routingDataOutputs = currentNode.Evaluate(currentShapes);
                foreach (var branch in routingDataOutputs)
                {
                    string targetNodeGuid = branch.Key;
                    List<Shape> routedShapes = branch.Value;

                    if (routedShapes == null || routedShapes.Count == 0) continue;

                    if (!shapesAtNode.ContainsKey(targetNodeGuid))
                    {
                        shapesAtNode[targetNodeGuid] = new List<Shape>();
                        var nextStepTarget = substitutionAsset.serializedSteps.Find(s => s.guid == targetNodeGuid);
                        if (nextStepTarget != null) nodeQueue.Enqueue(nextStepTarget);
                    }

                    shapesAtNode[targetNodeGuid].AddRange(routedShapes);
                }
            }

            // Step 3: Instantiation Loop (3-Vertex Coordinate Space Rotation Alignment and Scaling)
            Transform parentTransform = prefabsInstantiationRoot != null ? prefabsInstantiationRoot : this.transform;
            foreach (var assignment in finalTerminalMaps)
            {
                GameObject prefabBlueprint = assignment.Key;
                List<Shape> targetPlacements = assignment.Value;

                foreach (var placementShape in targetPlacements)
                {
                    // Safety check: Ensure we have at least 3 vertices to compute a 2D/3D plane basis matrix
                    if (placementShape.Vertices.Count < 3) continue;

                    // 1. Map System.Numerics positions to Unity space vectors
                    SysVector3 v0 = placementShape.Vertices[0];
                    SysVector3 v1 = placementShape.Vertices[1];
                    SysVector3 v2 = placementShape.Vertices[placementShape.Vertices.Count - 1];

                    UnityEngine.Vector3 p0 = new UnityEngine.Vector3(v0.X, v0.Y, v0.Z);
                    UnityEngine.Vector3 p1 = new UnityEngine.Vector3(v1.X, v1.Y, v1.Z);
                    UnityEngine.Vector3 p2 = new UnityEngine.Vector3(v2.X, v2.Y, v2.Z);

                    // 2. Compute Base Vectors representing Edge Spans
                    UnityEngine.Vector3 edgeX = (p1 - p0); // Vector mapping width dimension
                    UnityEngine.Vector3 edgeTemp = p2 - p0;

                    // 3. Construct Orthogonal Local Basis Axes via Vector Cross Products
                    UnityEngine.Vector3 localRight = edgeX;

                    // Face normal represents our absolute local Up Vector tracking plane tilts
                    UnityEngine.Vector3 localUp = edgeTemp;

                    // Forward vector aligns orthogonal to right and up to lock in the orientation matrix
                    UnityEngine.Vector3 localForward = -UnityEngine.Vector3.Cross(localRight, localUp).normalized;

                    // Base spawn target positions first
                    UnityEngine.Vector3 spawnPos = p0;

                    // 5. Construct Final Structural Orientation Matrix
                    // If vectors are valid, use LookRotation passing your calculated forward and upward components
                    UnityEngine.Quaternion spawnRot = UnityEngine.Quaternion.identity;
                    if (localForward.sqrMagnitude > 0.001f && localUp.sqrMagnitude > 0.001f)
                    {
                        spawnRot = UnityEngine.Quaternion.LookRotation(localForward, localUp);
                    }

                    // Configure local scale transformations matching structural geometric metrics
                    // Assumes your blueprint prefab asset is natively a 1m x 1m x 1m primitive cube/mesh bound
                    UnityEngine.Vector3 spawnScale = new UnityEngine.Vector3(localRight.magnitude, localUp.magnitude, 1.0f);

#if UNITY_EDITOR
                    GameObject spawnedObj = UnityEditor.PrefabUtility.InstantiatePrefab(prefabBlueprint, parentTransform) as GameObject;
                    if (spawnedObj != null)
                    {
                        spawnedObj.transform.position = spawnPos;
                        spawnedObj.transform.rotation = spawnRot;
                        spawnedObj.transform.localScale = spawnScale;
                    }
#else
        GameObject spawnedObj = Instantiate(prefabBlueprint, spawnPos, spawnRot, parentTransform);
        spawnedObj.transform.localScale = spawnScale;
#endif
                }
            }
        }

        public void ClearInstantiatedPrefabs()
        {
            Transform root = prefabsInstantiationRoot != null ? prefabsInstantiationRoot : this.transform;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(root.GetChild(i).gameObject);
            }
        }

        public void ExecuteCompiledGraphPipeline(List<IStep> sortedExecutionList)
        {
            // 1. Clear out old historical data states across your scene run
            Dictionary<string, IStep> allStepsLookup = new Dictionary<string, IStep>();
            foreach (var step in sortedExecutionList)
            {
                if (step.branchCache != null) step.branchCache.Clear();
                allStepsLookup[step.guid] = step;
            }

            // 2. Build the initial "Seed Shape" from your Unity Inspector's input vertices coordinates
            List<System.Numerics.Vector3> initialPoints = new List<System.Numerics.Vector3>();
            foreach (UnityEngine.Vector3 v in inputVertices)
            {
                initialPoints.Add(new System.Numerics.Vector3(v.x, v.y, v.z));
            }

            // Create the master starting canvas polygon
            Shape seedShape = new Shape(initialPoints);
            List<Shape> seedList = new List<Shape> { seedShape };

            // 3. Process each step sequentially along your topologically sorted execution list
            foreach (IStep step in sortedExecutionList)
            {
                if (step.isMasterNode) continue;

                // Inject dynamic math properties immediately before running operations
                GrammarEngineUtility.ResolveValueBindings(step, allStepsLookup);

                Operation op = step.GetOperation();
                if (op != null)
                {
                    // --- UPDATED: Pass the seed shape list into the utility gatherer ---
                    List<Shape> inputShapes = GrammarEngineUtility.GatherUpstreamShapes(step, seedList);
                    List<Shape> outputShapes = new List<Shape>();

                    // Run geometry manipulations
                    foreach (var shape in inputShapes)
                    {
                        var results = op.Apply(shape);
                        if (results != null) outputShapes.AddRange(results);
                    }

                    // --- UPDATED: Cache output data fields so downstream slots read them cleanly ---
                    GrammarEngineUtility.StoreDownstreamShapes(step, outputShapes);
                }
            }

            // 4. Expose final output data out to your scene graph view visualization routines!
            // Simply fetch the shapes cached by the last node(s) in the chain to draw or instantiate them.
            if (sortedExecutionList.Count > 0)
            {
                IStep finalStep = sortedExecutionList[sortedExecutionList.Count - 1];
                if (finalStep.branchCache != null && finalStep.branchCache.TryGetValue(0, out List<Shape> finalShapes))
                {
                    this.generatedTriangles = finalShapes; // Update display loops
                }
            }
        }

        private void Reset()
        {
            inputVertices = new List<UnityEngine.Vector3>()
            {
                new UnityEngine.Vector3(-2, 0, -2),
                new UnityEngine.Vector3(-2, 0, 2),
                new UnityEngine.Vector3(2, 0, 2),
                new UnityEngine.Vector3(2, 0, -2)
            };
            generatedTriangles.Clear();
        }
    }
}