using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class ScatterPointsOnEdges : Operation
    {
        // Density defines the number of points generated per unit of edge length.
        // For example: A value of 2.0 means 2 points per meter (spacing of 0.5m).
        public float Density { get; set; } = 1.0f;

        // Randomization acts as a jitter multiplier (typically between 0.0 and 1.0).
        // It offsets points forward or backward along the edge to break uniform distribution grids.
        public float Randomization { get; set; } = 0.0f;

        // A static seed value keeps generation deterministic during real-time updates.
        public int Seed { get; set; } = 1337;

        // Toggle this if you are processing open paths (like road centerlines) 
        // to prevent generating a closing edge segment back to vertex 0.
        public bool ClosedLoop { get; set; } = true;

        public ScatterPointsOnEdges() { }

        public override List<Shape> Apply(Shape shape)
        {
            List<Shape> results = new List<Shape>();

            // Ensure the shape has enough vertices to define an edge sequence
            if (shape == null || shape.Vertices.Count < 2 || Density <= 0.0001f)
            {
                return results;
            }

            // Initialize random states per shape using the specified seed 
            // to ensure stability across graph evaluations
            Random rand = new Random(Seed);
            int numVertices = shape.Vertices.Count;

            // Determine total target edges based on whether the shape forms a closed perimeter loop
            int edgeCount = ClosedLoop ? numVertices : numVertices - 1;

            for (int i = 0; i < edgeCount; i++)
            {
                Vector3 vStart = shape.Vertices[i];
                Vector3 vEnd = shape.Vertices[(i + 1) % numVertices];

                Vector3 delta = vEnd - vStart;
                float length = delta.Length();

                // Skip zero-length or degenerate micro-edges
                if (length < 0.0001f) continue;

                Vector3 dir = delta / length;

                // Calculate nominal step distance between points based on density (Spacing = 1 / Density)
                float spacing = 1.0f / Density;

                float travel = 0f;
                while (travel <= length)
                {
                    float t = travel;

                    // Apply randomized longitudinal jitter along the edge vector
                    if (Randomization > 0f)
                    {
                        // Generates a value between -1.0 and 1.0
                        float noiseFactor = (float)rand.NextDouble() * 2f - 1f;

                        // Scale jitter distance relative to maximum spacing to maintain relative order
                        float jitter = noiseFactor * (Randomization * spacing * 0.5f);
                        t += jitter;
                    }

                    // CRITICAL: Clamp the evaluated position to keep vertices perfectly bound 
                    // to the edge line segment, preventing them from bleeding past corners.
                    t = Math.Clamp(t, 0f, length);

                    Vector3 pointPos = vStart + dir * t;

                    // Reconstruct as a lightweight single-vertex Point shape
                    Shape pointShape = new Shape(new List<Vector3> { pointPos });

                    // Maintain downstream tracking compatibility by cloning parent metadata dictionaries
                    pointShape.Data = new Dictionary<string, List<int>>(shape.Data);

                    // Inject a helper tag capturing which index edge this point originated from
                    pointShape.SetDataSingle("parent_edge_index", i);
                    pointShape.SetDataSingle("is_scattered_point", 1);

                    results.Add(pointShape);

                    travel += spacing;

                    // Standard infinite loop safety cutoff
                    if (spacing <= 0.0001f) break;
                }
            }

            return results;
        }

        public override List<Shape> ApplySet(List<Shape> shapes)
        {
            if (shapes == null || shapes.Count == 0) return shapes;

            List<Shape> outputs = new List<Shape>();

            // Isolate per-shape contexts to guarantee clean data integrity loops
            foreach (var shape in shapes)
            {
                outputs.AddRange(Apply(shape));
            }

            return outputs;
        }
    }
}