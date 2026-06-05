using UnityEngine;
using System.Collections.Generic;
using System.Numerics;

// Alias to avoid conflicting with UnityEngine.Vector3
using SysVector3 = System.Numerics.Vector3;

namespace Gbe.ShapeGrammar
{
    [ExecuteInEditMode] // Allows it to update or render while inside the editor
    public class ShapeGrammarPath : MonoBehaviour
    {
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

        public void ExecuteGrammarChain()
        {
            // 1. Gather initial coordinates from your interactive viewport handles
            List<SysVector3> sysVertices = new List<SysVector3>();
            foreach (var v in inputVertices)
            {
                UnityEngine.Vector3 worldPos = transform.TransformPoint(v);
                sysVertices.Add(new SysVector3(worldPos.x, worldPos.y, worldPos.z));
            }

            Shape initialShape = new Shape(sysVertices);

            generatedTriangles = new List<Shape>() { initialShape };
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