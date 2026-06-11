using UnityEngine;
using System.Collections.Generic;
using System.Numerics;

// Alias to avoid conflicting with UnityEngine.Vector3
using SysVector3 = System.Numerics.Vector3;
using System;

namespace Gbe.ShapeGrammar
{
    [ExecuteInEditMode] // Allows it to update or render while inside the editor
    public class ShapeGrammarObject : MonoBehaviour
    {
        [Header("Grammar Configuration Asset")]
        public GrammarGraphAsset graphAsset;

        [Header("Path Settings")]
        public bool autoFixWinding = true;

        // One GameObject Component = One Path
        [HideInInspector]
        public List<UnityEngine.Vector3> inputVertices = new List<UnityEngine.Vector3>()
        {
            new UnityEngine.Vector3(-2, 0, -2),
            new UnityEngine.Vector3(-2, 0, 2),
            new UnityEngine.Vector3(2, 0, 2),
            new UnityEngine.Vector3(2, 0, -2)
        };

        // Holds the internal evaluation state for this specific component
        [HideInInspector]
        public List<Shape> generatedTriangles = new List<Shape>();

        // Dynamically driven evaluation layer proxy
        private RuntimeGraphTree tree = new RuntimeGraphTree();

        public void ExecuteGrammarChain()
        {
            if (graphAsset == null)
            {
                Debug.LogWarning($"[ShapeGrammarObject] Please assign a valid GrammarGraphAsset to {gameObject.name} before executing.", this);
                return;
            }

            // 1. Gather initial coordinates from your interactive viewport handles
            List<SysVector3> sysVertices = new List<SysVector3>();
            foreach (var v in inputVertices)
            {
                UnityEngine.Vector3 worldPos = transform.TransformPoint(v);
                sysVertices.Add(new SysVector3(worldPos.x, worldPos.y, worldPos.z));
            }

            Shape initialShape = new Shape(sysVertices);
            SpatialDependencyRegistry mockRegistry = new SpatialDependencyRegistry();

            // Reconstruct and compile execution chains using asset layout parameters
            tree.InitializeFromAsset(graphAsset);
            tree.RegisterToRegistry(mockRegistry, initialShape);

            // 2. Execute Synchronously 
            generatedTriangles = tree.ApplySynchronous(initialShape, stage: 0);

            Debug.Log($"[ShapeGrammarObject] Finished evaluating graph: {graphAsset.name}. Result shapes count: {generatedTriangles.Count}");
        }

        // Reset to default shape values when component is added
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