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

            // Step 1: Run Geometry Generation Graph
            List<SysVector3> sysVertices = new List<SysVector3>();
            foreach (var v in inputVertices)
            {
                UnityEngine.Vector3 worldPos = transform.TransformPoint(v);
                sysVertices.Add(new SysVector3(worldPos.x, worldPos.y, worldPos.z));
            }

            Shape initialShape = new Shape(sysVertices);
            SpatialDependencyRegistry mockRegistry = new SpatialDependencyRegistry();

            tree.InitializeFromAsset(graphAsset);
            tree.RegisterToRegistry(mockRegistry, initialShape);
            generatedTriangles = tree.ApplySynchronous(initialShape, stage: 0);

            // Step 2: Run Substitution Scheme Asset System Pipeline 
            if (substitutionAsset != null)
            {
                ClearInstantiatedPrefabs();
                ProcessSubstitutionGraph(generatedTriangles);
            }
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