using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UnityEngine;
using SysVector3 = System.Numerics.Vector3;

namespace Gbe.ShapeGrammar
{
    [ExecuteInEditMode]
    [SelectionBase] // <--- FORCES PARENT SELECTION WHEN CLICKING CHILDREN
    public class ShapeGrammarObject : MonoBehaviour
    {
        [Header("Pipeline Generation Graph Assets")]
        public GrammarGraphAsset graphAsset;
        public SubstitutionSchemeAsset substitutionAsset;


        [Header("Scene Visualization Toggles")] // <--- ADD THESE TOGGLES
        public bool showAxiomLines = true;
        public bool showWireframes = true;

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

        private void OnValidate()
        {
            tree.SetOnEvaluate(OnDoneNewEvaluation);
        }

        public RuntimeGraphTree GetTree()
        {
            return tree;
        }

        public List<System.Numerics.Vector3> InitializeEvaluation()
        {
            // Convert scene coordinates to pipeline coordinates
            List<System.Numerics.Vector3> sysVertices = inputVertices
                .Select(v => {
                    var world = transform.TransformPoint(v);
                    return new System.Numerics.Vector3(world.x, world.y, world.z);
                }).ToList();

            tree.InitializeFromAsset(graphAsset);

            return sysVertices;
        }

        private void OnDoneNewEvaluation(List<Shape> generatedTriangles)
        {
            // Substitution Pipeline
            if (substitutionAsset != null)
            {
                ClearInstantiatedPrefabs();
                ProcessSubstitutionGraph(generatedTriangles);
            }
        }

        private void ProcessSubstitutionGraph(List<Shape> shapes)
        {
            if (substitutionAsset == null || substitutionAsset.serializedSteps == null || substitutionAsset.serializedSteps.Count == 0) return;

            // Maps to track which prefab gets assigned to which geometric shapes
            var finalTerminalMaps = new Dictionary<GameObject, List<Shape>>();

            // Step 1: Flat Lookup Resolution Pass
            foreach (var shape in shapes)
            {
                GameObject assignedPrefab = null;

                foreach (var step in substitutionAsset.serializedSteps)
                {
                    // Verify all required flags for this step exist in the shape's metadata keys
                    bool flagMatch = true;
                    if (step.requiredFlags != null && step.requiredFlags.Count > 0)
                    {
                        foreach (var flag in step.requiredFlags)
                        {
                            if (!shape.Data.ContainsKey(flag))
                            {
                                flagMatch = false;
                                break;
                            }
                        }
                    }

                    // If the shape satisfies this step's criteria, determine which prefab index to extract
                    if (flagMatch)
                    {
                        if (step.indexedPrefab == null || step.indexedPrefab.Count == 0)
                            continue;

                        int index = 0;

                        // Extract index value using indexerFlag if provided
                        if (!string.IsNullOrEmpty(step.indexerFlag) && shape.Data.TryGetValue(step.indexerFlag, out var values) && values.Count > 0)
                        {
                            index = values[0];
                            if (index < 0) index = 0;

                            // Handle index out of bounds bounds configurations
                            if (index >= step.indexedPrefab.Count)
                            {
                                index = step.clampIndex ? step.indexedPrefab.Count - 1 : index % step.indexedPrefab.Count;
                            }
                        }

                        assignedPrefab = step.indexedPrefab[index];
                        break; // First match wins (simulating an ordered fallback table)
                    }
                }

                // If a matching configuration rule mapped a valid blueprint asset, queue it for instantiation
                if (assignedPrefab != null)
                {
                    if (!finalTerminalMaps.ContainsKey(assignedPrefab))
                        finalTerminalMaps[assignedPrefab] = new List<Shape>();

                    finalTerminalMaps[assignedPrefab].Add(shape);
                }
            }

            // Step 2: Instantiation Loop (3-Vertex Coordinate Space Rotation Alignment and Scaling)
            Transform parentTransform = this.transform;
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
                    UnityEngine.Quaternion spawnRot = UnityEngine.Quaternion.identity;
                    if (localForward.sqrMagnitude > 0.001f && localUp.sqrMagnitude > 0.001f)
                    {
                        spawnRot = UnityEngine.Quaternion.LookRotation(localForward, localUp);
                    }

                    // Configure local scale transformations matching structural geometric metrics
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
            Transform root = this.transform;
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