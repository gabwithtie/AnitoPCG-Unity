using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    public class SubdivideQuad : Operation
    {
        public float MaxWidth { get; set; } = 1.0f;
        public int EdgeBasis { get; set; } = 0;
        public int FinalSegmentCount { get; private set; } = 0;

        public override List<Shape> Apply(Shape shape)
        {
            // This operation specifically expects a Quad (4 vertices)
            if (shape.Vertices.Count != 4)
            {
                Fail();
                return new List<Shape>();
            }

            int markedSide = EdgeBasis;

            if (markedSide == -1) return new List<Shape> { shape }; // No valid marked boundary matching corners found

            // Step 2: Define Rails based on the marked side
            int i0 = markedSide;
            int i1 = (markedSide + 1) % 4;
            int i2 = (markedSide + 2) % 4;
            int i3 = (markedSide + 3) % 4;

            // Rail A: i2 -> i3 | Rail B: i1 -> i0
            Vector3 railA_start = shape.Vertices[i2];
            Vector3 railA_end = shape.Vertices[i3];
            Vector3 railB_start = shape.Vertices[i1];
            Vector3 railB_end = shape.Vertices[i0];

            float distA = (railA_end - railA_start).Length(); // C# uses .Length() over C++ .Magnitude()
            float distB = (railB_end - railB_start).Length();
            float avgDist = (distA + distB) * 0.5f;

            // Step 3: Calculate balanced number of step structural segment slices
            int numSegments = Math.Max(1, (int)Math.Ceiling(avgDist / MaxWidth));

            List<Shape> quads = new List<Shape>();
            for (int i = 0; i < numSegments; ++i)
            {
                float tStart = (float)i / numSegments;
                float tEnd = (float)(i + 1) / numSegments;

                // Bilinear interpolation tracking coordinates across target bounds
                Vector3 p0 = railA_start + (railA_end - railA_start) * tStart; // Bottom Left
                Vector3 p1 = railB_start + (railB_end - railB_start) * tStart; // Bottom Right
                Vector3 p2 = railB_start + (railB_end - railB_start) * tEnd;   // Top Right
                Vector3 p3 = railA_start + (railA_end - railA_start) * tEnd;   // Top Left

                Shape subQuad = new Shape(new List<Vector3> { p0, p1, p2, p3 });
                subQuad.Data = new Dictionary<string, List<int>>(shape.Data);

                // Update edge index flags for downstream cascade operations
                subQuad.Data["edge"] = new List<int> { 2, 3 };

                quads.Add(subQuad);
            }

            FinalSegmentCount = numSegments;
            return quads;
        }
    }
}