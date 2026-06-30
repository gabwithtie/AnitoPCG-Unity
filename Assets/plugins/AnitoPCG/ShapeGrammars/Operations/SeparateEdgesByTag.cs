using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class SeparateEdgesByTag : Operation
    {
        // The runtime data key containing the list of target indices (e.g., "corner_edge")
        public string TargetTag { get; set; } = "corner_edge";

        // The metadata destination key for the tracking identity tag mapping
        public string IndexTag { get; set; } = "e";

        public SeparateEdgesByTag() { }

        public override List<Shape> Apply(Shape shape)
        {
            List<Shape> output = new List<Shape>();

            // Ensure we have a valid polygon face to split
            if (shape == null || shape.Vertices.Count < 2)
            {
                return output;
            }

            // Return early if the parent shape doesn't contain the requested filtering tag
            if (shape.Data == null || !shape.Data.TryGetValue(TargetTag, out List<int> trackingIndices) || trackingIndices.Count == 0)
            {
                return output;
            }

            int vertexCount = shape.Vertices.Count;

            // --- Generic Index-Driven Topology Slicing ---
            // In the architecture's structural looping conventions (from SeparateEdges):
            // Loop sequential sub-edges (from i = 1 to vertexCount - 1)
            for (int i = 1; i < vertexCount; i++)
            {
                // In your architecture, sub-edge 'i' is the segment: shape.Vertices[i] -> shape.Vertices[i - 1]
                // If this sequential sub-edge index matches any element in the user's custom tracking tag array...
                if (trackingIndices.Contains(i))
                {
                    Shape newEdge = new Shape(new List<Vector3> { shape.Vertices[i], shape.Vertices[i - 1] });

                    // Value clone parent dictionaries to secure tracing arrays
                    newEdge.Data = new Dictionary<string, List<int>>(shape.Data);

                    // Assign the matching tracker ID directly down the line
                    newEdge.Data[IndexTag] = new List<int>() { i };
                    output.Add(newEdge);
                }
            }

            // Check the closing loop sub-edge wrapping back around (Sub-Edge Index 0)
            if (trackingIndices.Contains(0))
            {
                Shape closingEdge = new Shape(new List<Vector3> { shape.Vertices[0], shape.Vertices[vertexCount - 1] });
                closingEdge.Data = new Dictionary<string, List<int>>(shape.Data);
                closingEdge.Data[IndexTag] = new List<int>() { 0 };
                output.Add(closingEdge);
            }

            return output;
        }

        public override List<Shape> ApplySet(List<Shape> shapes)
        {
            if (shapes == null || shapes.Count == 0) return shapes;

            List<Shape> outputs = new List<Shape>();
            foreach (var shape in shapes)
            {
                outputs.AddRange(Apply(shape));
            }
            return outputs;
        }
    }
}