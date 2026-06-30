using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class EdgesToRightTriangles : Operation
    {
        // The thickness/orthogonal offset depth of the right triangle profile
        public float ProfileDepth { get; set; } = 0.5f;

        // Custom index tag tracking to maintain compatibility with SeparateEdges mapping
        public string IndexTag { get; set; } = "e";

        public EdgesToRightTriangles() { }

        public override List<Shape> Apply(Shape shape)
        {
            List<Shape> results = new List<Shape>();

            // This operation strictly expects line segments/edges (exactly 2 vertices)
            if (shape == null || shape.Vertices.Count != 2)
            {
                // Pass non-edge shapes through safely without altering data flow
                return new List<Shape> { shape };
            }

            Vector3 v0 = shape.Vertices[0]; // Base Start
            Vector3 v1 = shape.Vertices[1]; // Base End

            Vector3 edgeVec = v1 - v0;
            float edgeLength = edgeVec.Length();

            if (edgeLength < 0.0001f) return results;

            Vector3 edgeDir = edgeVec / edgeLength;

            // 1. Establish an upright/orthogonal normal vector relative to the line segment direction
            Vector3 upVector = Vector3.UnitY;
            if (Math.Abs(Vector3.Dot(edgeDir, upVector)) > 0.98f)
            {
                upVector = Vector3.UnitZ; // Avoid gimbal lock for vertical edges
            }

            // Cross product gives us a perfect orthogonal vector extending out from the edge spine
            Vector3 perpendicularAxis = Vector3.Normalize(Vector3.Cross(edgeDir, upVector));

            // 2. Compute the third vertex (v2) to complete a right triangle
            // Making v1 the right angle corner (perpendicular extrusion extends out from v1)
            Vector3 v2 = v1 + (perpendicularAxis * ProfileDepth);

            // Assemble the complete 3-vertex right triangle loop profile
            List<Vector3> triangleVertices = new List<Vector3> { v1, v2, v0 };

            Shape profileShape = new Shape(triangleVertices);

            // Clone and forward all tracking metadata down the pipe network seamlessly
            profileShape.Data = new Dictionary<string, List<int>>(shape.Data);
            profileShape.SetDataSingle("is_pipe_profile", 1);

            results.Add(profileShape);
            return results;
        }

        public override List<Shape> ApplySet(List<Shape> shapes)
        {
            if (shapes == null || shapes.Count == 0) return shapes;

            List<Shape> outputs = new List<Shape>();

            // Isolate individual edges to protect ID dictionaries and coordinate transforms
            foreach (var shape in shapes)
            {
                outputs.AddRange(Apply(shape));
            }

            return outputs;
        }
    }
}