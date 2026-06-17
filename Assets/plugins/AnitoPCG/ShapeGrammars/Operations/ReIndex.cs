using System;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine; // Included if you want to use the Tooltip attribute

namespace Gbe.ShapeGrammar
{
    using Vector3 = System.Numerics.Vector3;

    [Serializable]
    public class ReIndex : Operation
    {
        [Tooltip("Shifts the starting vertex (Index 0). Supports positive values (forward) and negative values (backward).")]
        public int ShiftIndexCount { get; set; } = 0;

        [Tooltip("Reverses the vertex arrangement array to flip face normals/winding direction.")]
        public bool ReverseWindingOrder { get; set; } = false;

        public override List<Shape> Apply(Shape shape)
        {
            if (shape.Vertices == null || shape.Vertices.Count == 0)
                return new List<Shape> { shape };

            List<Vector3> adjustedVertices = new List<Vector3>(shape.Vertices);
            int count = adjustedVertices.Count;

            // 1. Process Index Cycle Shifts (Rotates the starting corner point)
            if (ShiftIndexCount != 0 && count > 1)
            {
                // THE FIX: Bulletproof wrapping math that handles negative and positive shift sizes flawlessly
                int effectiveShift = ((ShiftIndexCount % count) + count) % count;

                if (effectiveShift != 0)
                {
                    List<Vector3> shifted = new List<Vector3>(count);
                    for (int i = 0; i < count; i++)
                    {
                        shifted.Add(adjustedVertices[(i + effectiveShift) % count]);
                    }
                    adjustedVertices = shifted;
                }
            }

            // 2. Process Winding Reversals (Flips front vs back face orientation alignments)
            if (ReverseWindingOrder && count > 1)
            {
                // To safely reverse a polygon winding without losing the active start index positioning:
                // Keep element 0 pinned, and reverse the remaining elements trailing after it.
                Vector3 originPoint = adjustedVertices[0];
                adjustedVertices.RemoveAt(0);
                adjustedVertices.Reverse();
                adjustedVertices.Insert(0, originPoint);
            }

            Shape processedShape = new Shape(adjustedVertices);
            processedShape.Data = new Dictionary<string, List<int>>(shape.Data);
            return new List<Shape> { processedShape };
        }
    }
}