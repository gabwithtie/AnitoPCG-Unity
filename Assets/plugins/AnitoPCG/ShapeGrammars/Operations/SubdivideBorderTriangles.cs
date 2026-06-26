using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class SubdivideBorderTriangles : Operation
    {
        public float MaxWidth { get; set; } = 1.0f;
        public string RoofPartTag { get; set; } = "roof_part";

        public override List<Shape> Apply(Shape shape)
        {
            int n = shape.Vertices.Count;
            if (n < 3 || n > 4 || MaxWidth <= 0.0001f) return new List<Shape> { shape };

            bool isQuad = (n == 4);
            Vector3 vBL = shape.Vertices[0];
            Vector3 vBR = shape.Vertices[1];
            Vector3 vTR = isQuad ? shape.Vertices[2] : shape.Vertices[2];
            Vector3 vTL = isQuad ? shape.Vertices[3] : shape.Vertices[2];

            // 1. Compute the parent polygon's normal to guarantee consistent winding/orientation
            Vector3 originalNormal = Vector3.Zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 curr = shape.Vertices[i];
                Vector3 next = shape.Vertices[(i + 1) % n];
                originalNormal.X += (curr.Y - next.Y) * (curr.Z + next.Z);
                originalNormal.Y += (curr.Z - next.Z) * (curr.X + next.X);
                originalNormal.Z += (curr.X - next.X) * (curr.Y + next.Y);
            }
            if (originalNormal.LengthSquared() > 0.000001f)
                originalNormal = Vector3.Normalize(originalNormal);
            else
                originalNormal = Vector3.UnitY;

            // 2. Establish Local Coordinate System based on the Bottom Edge
            Vector3 edgeX = (vBR - vBL);
            float bottomLen = edgeX.Length();
            if (bottomLen < 0.0001f) return new List<Shape> { shape };
            Vector3 dirX = edgeX / bottomLen;

            // 3. Project Top Vertices onto the Bottom Line to isolate wings from the core
            float offL = Vector3.Dot(vTL - vBL, dirX);
            float offR = Vector3.Dot(vTR - vBL, dirX);

            Vector3 GetPos(float x, bool isTop)
            {
                if (!isTop)
                {
                    return vBL + (dirX * x);
                }
                else
                {
                    float topSpan = offR - offL;
                    if (Math.Abs(topSpan) < 0.0001f) return vTL;
                    float t = (x - offL) / topSpan;
                    return vTL + (vTR - vTL) * t;
                }
            }

            List<Shape> output = new List<Shape>();
            Dictionary<string, List<int>> baseData = shape.Data != null ? new Dictionary<string, List<int>>(shape.Data) : new Dictionary<string, List<int>>();

            // 4. Generate Left Wing (Guaranteed Right Triangle)
            if (Math.Abs(offL) > 0.0001f)
            {
                Vector3 bL_proj = GetPos(offL, false); // Exact 90-degree intersection
                AddRightTriangle(bL_proj, vTL, vBL, originalNormal, baseData, 1, output);
            }

            // 5. Generate Right Wing (Guaranteed Right Triangle)
            if (Math.Abs(offR - bottomLen) > 0.0001f)
            {
                Vector3 bR_proj = GetPos(offR, false); // Exact 90-degree intersection
                AddRightTriangle(bR_proj, vTR, vBR, -originalNormal, baseData, 2, output);
            }

            // 6. Subdivide the Core into Right Triangles
            float coreWidth = Math.Abs(offR - offL);
            if (coreWidth > 0.0001f)
            {
                int numCols = (int)Math.Ceiling(coreWidth / MaxWidth);
                for (int i = 0; i < numCols; i++)
                {
                    float x0 = offL + (offR - offL) * ((float)i / numCols);
                    float x1 = offL + (offR - offL) * ((float)(i + 1) / numCols);

                    Vector3 b0 = GetPos(x0, false);
                    Vector3 b1 = GetPos(x1, false);
                    Vector3 t0 = GetPos(x0, true);
                    Vector3 t1 = GetPos(x1, true);

                    // Project t0 horizontally across the column width to sample the vertical alignment line of b1
                    Vector3 t0_proj = t0 + dirX * (x1 - x0);

                    // This decomposes the skewed column quad into exactly 3 perfect right triangles:
                    // Tri 1: Bottom Right Right-Triangle (90 deg at b1)
                    AddRightTriangle(b1, b0, t0_proj, originalNormal, baseData, 1, output);

                    // Tri 2: Top Left Right-Triangle (90 deg at t0)
                    AddRightTriangle(t0, t0_proj, b0, originalNormal, baseData, 0, output);
                }
            }

            return output;
        }

        /// <summary>
        /// Creates a right triangle shape ensuring the 90-degree vertex is strictly at index 0,
        /// while dynamically arranging indices 1 and 2 to match the parent plane's facing normal.
        /// </summary>
        private void AddRightTriangle(Vector3 rightAngleVertex, Vector3 v1, Vector3 v2, Vector3 parentNormal,
                                      Dictionary<string, List<int>> baseData, int roofPart, List<Shape> outputList)
        {
            // Test cross product configuration to check winding direction against the parent face
            Vector3 crossTest = Vector3.Cross(v1 - rightAngleVertex, v2 - rightAngleVertex);

            List<Vector3> arrangedVertices;
            if (Vector3.Dot(crossTest, parentNormal) >= 0f)
            {
                arrangedVertices = new List<Vector3> { rightAngleVertex, v1, v2 };
            }
            else
            {
                // Swap index 1 and 2 to correct the normal direction, keeping the right angle at index 0
                arrangedVertices = new List<Vector3> { rightAngleVertex, v2, v1 };
            }

            Shape rightTri = new Shape(arrangedVertices);
            rightTri.Data = new Dictionary<string, List<int>>(baseData);
            rightTri.SetDataSingle(RoofPartTag, roofPart);
            outputList.Add(rightTri);
        }
    }
}