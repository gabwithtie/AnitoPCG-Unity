using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class PointsToRightTriangles : Operation
    {
        // Baseline dimensions for the right triangle
        public float BaseWidth { get; set; } = 1.0f;
        public float BaseHeight { get; set; } = 1.5f;

        // A single float property that defines the maximum random deviation percentage (0.0 to 1.0+)
        // for both width and height independently.
        public float DimensionRandomization { get; set; } = 0.0f;

        // If true, rotates the triangle around its origin point on a random 360-degree heading.
        public bool RandomizeRotation { get; set; } = false;

        // Static seed value keeps generations deterministic during graph re-evaluation
        public int Seed { get; set; } = 4242;

        public PointsToRightTriangles() { }

        public override List<Shape> Apply(Shape shape)
        {
            List<Shape> results = new List<Shape>();

            // This operation strictly processes point shapes (single vertex spatial anchors)
            if (shape == null || shape.Vertices.Count != 1)
            {
                // Pass non-point shapes through untouched to avoid swallowing data
                return new List<Shape> { shape };
            }

            Vector3 origin = shape.Vertices[0];

            // Setup a robust pseudo-random engine seeded specifically to this point's coordinate space
            // adding the master Seed ensures consistency across geometry refreshes.
            int localSeed = Seed ^ origin.GetHashCode();
            Random rand = new Random(localSeed);

            // 1. Compute randomized dimensions
            float width = BaseWidth;
            float height = BaseHeight;

            if (DimensionRandomization > 0.0f)
            {
                // Multipliers will range between (1 - Randomization) and (1 + Randomization)
                float widthMultiplier = 1.0f + ((float)rand.NextDouble() * 2f - 1f) * DimensionRandomization;
                float heightMultiplier = 1.0f + ((float)rand.NextDouble() * 2f - 1f) * DimensionRandomization;

                width = Math.Max(0.001f, width * widthMultiplier);
                height = Math.Max(0.001f, height * heightMultiplier);
            }

            // 2. Establish local directional axes (Assuming +Y is Upwards)
            Vector3 localUp = Vector3.UnitY;
            Vector3 localRight = Vector3.UnitX;

            // Handle optional 360-degree yaw rotation around the upward orientation vector
            if (RandomizeRotation)
            {
                float randomAngle = (float)(rand.NextDouble() * 2.0 * Math.PI);
                float cos = (float)Math.Cos(randomAngle);
                float sin = (float)Math.Sin(randomAngle);

                // Rotate the local right horizontal vector around the vertical up axis
                localRight = new Vector3(cos, 0f, -sin);
            }

            // 3. Construct the Right Triangle vertices relative to the anchor origin
            // Vertex 0: The Anchor Origin (Right Angle corner)
            Vector3 v0 = origin;

            // Vertex 1: Out along the base width axis
            Vector3 v1 = origin + (localRight * width);

            // Vertex 2: Straight upwards along the vertical axis from the origin
            Vector3 v2 = origin + (localUp * height);

            // Assemble into a clockwise-ordered polygon face
            List<Vector3> triangleVertices = new List<Vector3> { v0, v1, v2 };

            Shape triangleShape = new Shape(triangleVertices);

            // Clone parent dictionary data arrays safely to lock downstream ID tracing compatibility
            triangleShape.Data = new Dictionary<string, List<int>>(shape.Data);
            triangleShape.SetDataSingle("is_generated_triangle", 1);

            results.Add(triangleShape);
            return results;
        }

        public override List<Shape> ApplySet(List<Shape> shapes)
        {
            if (shapes == null || shapes.Count == 0) return shapes;

            List<Shape> outputs = new List<Shape>();

            // Isolate individual context loops per point shape to uphold ApplySet architecture specifications
            foreach (var shape in shapes)
            {
                outputs.AddRange(Apply(shape));
            }

            return outputs;
        }
    }
}