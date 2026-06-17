using System;
using System.Collections.Generic;
using System.Numerics;
using UnityEngine;

namespace Gbe.ShapeGrammar
{
    using Vector3 = System.Numerics.Vector3;
    using Vector2 = System.Numerics.Vector2;

    [Serializable]
    public class Triangulate : Operation
    {
        // Toggle to automatically detect winding and prevent "inside-out" triangulation
        public bool AutoFixWinding { get; set; } = true;

        [Tooltip("Splits all evaluated triangles into perfect right triangles by dropping an altitude line from their largest angle.")]
        public bool SplitIntoRightTriangles { get; set; } = false;

        public Triangulate()
        {
        }

        public override List<Shape> Apply(Shape shape)
        {
            List<Shape> baseTriangles = new List<Shape>();
            int n = shape.Vertices.Count;
            if (n < 3) return baseTriangles;

            // 1. Calculate Newell's Normal to find the 3D plane
            Vector3 normal = Vector3.Zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 curr = shape.Vertices[i];
                Vector3 next = shape.Vertices[(i + 1) % n];
                normal.X += (curr.Y - next.Y) * (curr.Z + next.Z);
                normal.Y += (curr.Z - next.Z) * (curr.X + next.X);
                normal.Z += (curr.X - next.X) * (curr.Y + next.Y);
            }

            if (normal.LengthSquared() < 0.000001f) return baseTriangles;
            normal = Vector3.Normalize(normal);

            // 2. Project to 2D for bulletproof math
            Vector3 origin = shape.Vertices[0];
            Vector3 edgeA = Vector3.Normalize(shape.Vertices[1] - origin);
            Vector3 edgeB = Vector3.Normalize(Vector3.Cross(normal, edgeA));

            List<Vector2> poly2D = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                Vector3 vec = shape.Vertices[i] - origin;
                poly2D.Add(new Vector2(Vector3.Dot(vec, edgeA), Vector3.Dot(vec, edgeB)));
            }

            // 3. Calculate 2D Signed Area
            float signedArea = 0.0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                signedArea += (poly2D[i].X * poly2D[j].Y - poly2D[j].X * poly2D[i].Y);
            }

            bool isCW = signedArea < 0.0f;

            // 4. Setup working indices
            List<int> currentIndices = new List<int>(n);
            for (int i = 0; i < n; ++i)
            {
                if (AutoFixWinding && isCW)
                {
                    currentIndices.Add(n - 1 - i);
                }
                else
                {
                    currentIndices.Add(i);
                }
            }

            // 5. 2D Ear Clipping Loop
            int attempts = 0;
            int maxAttempts = n * 2;

            while (currentIndices.Count > 3 && attempts < maxAttempts)
            {
                bool earFound = false;
                for (int i = 0; i < currentIndices.Count; ++i)
                {
                    int prevIdx = (i + currentIndices.Count - 1) % currentIndices.Count;
                    int nextIdx = (i + 1) % currentIndices.Count;

                    int a = currentIndices[prevIdx];
                    int b = currentIndices[i];
                    int c = currentIndices[nextIdx];

                    if (IsEar2D(a, b, c, poly2D, currentIndices))
                    {
                        baseTriangles.Add(CreateTriangle(shape, a, b, c));
                        currentIndices.RemoveAt(i);
                        earFound = true;
                        break;
                    }
                }

                if (!earFound) break;
                attempts++;
            }

            if (currentIndices.Count == 3)
            {
                baseTriangles.Add(CreateTriangle(shape, currentIndices[0], currentIndices[1], currentIndices[2]));
            }

            // --- SUBDIVIDE INTO DUAL RIGHT TRIANGLES MODULE ---
            if (!SplitIntoRightTriangles)
            {
                return baseTriangles;
            }

            List<Shape> rightTriangles = new List<Shape>();

            foreach (var tri in baseTriangles)
            {
                if (tri.Vertices.Count != 3) continue;

                Vector3 p0 = tri.Vertices[0];
                Vector3 p1 = tri.Vertices[1];
                Vector3 p2 = tri.Vertices[2];

                Vector3 e01 = p1 - p0;
                Vector3 e12 = p2 - p1;
                Vector3 e20 = p0 - p2;

                float len01Sq = e01.LengthSquared();
                float len12Sq = e12.LengthSquared();
                float len20Sq = e20.LengthSquared();

                Vector3 pivotVertex;
                Vector3 baseStart;
                Vector3 baseEnd;

                if (len01Sq >= len12Sq && len01Sq >= len20Sq)
                {
                    pivotVertex = p2; baseStart = p0; baseEnd = p1;
                }
                else if (len12Sq >= len01Sq && len12Sq >= len20Sq)
                {
                    pivotVertex = p0; baseStart = p1; baseEnd = p2;
                }
                else
                {
                    pivotVertex = p1; baseStart = p2; baseEnd = p0;
                }

                Vector3 baseVector = baseEnd - baseStart;
                float baseLengthSq = baseVector.LengthSquared();

                if (baseLengthSq < 0.000001f)
                {
                    rightTriangles.Add(tri);
                    continue;
                }

                float t = Vector3.Dot(pivotVertex - baseStart, baseVector) / baseLengthSq;
                t = Math.Clamp(t, 0.001f, 0.999f);

                Vector3 altitudeIntersectionPoint = baseStart + (baseVector * t);

                // --- GUARANTEED TOPOLOGY MAPPING ---
                // Index 0: altitudeIntersectionPoint (The absolute 90-degree corner)
                // Index 1: The width edge anchor
                // Index 2: The height/depth edge anchor (The pivot peak)

                // Right Triangle 1 Layout: [Right-Angle, Base-Start Corner, Top Pivot Peak]
                Shape rt1 = new Shape(new List<Vector3> { altitudeIntersectionPoint, baseStart, pivotVertex });
                rt1.Data = new Dictionary<string, List<int>>(tri.Data);

                // Right Triangle 2 Layout: [Right-Angle, Base-End Corner, Top Pivot Peak]
                Shape rt2 = new Shape(new List<Vector3> { altitudeIntersectionPoint, baseEnd, pivotVertex });
                rt2.Data = new Dictionary<string, List<int>>(tri.Data);

                rightTriangles.Add(rt1);
                rightTriangles.Add(rt2);
            }

            return rightTriangles;
        }

        private Shape CreateTriangle(Shape original, int aIdx, int bIdx, int cIdx)
        {
            Shape tri = new Shape(new List<Vector3> { original.Vertices[aIdx], original.Vertices[bIdx], original.Vertices[cIdx] });
            tri.Data = new Dictionary<string, List<int>>(original.Data);

            List<int> edgeIndices = new List<int>();
            int[] triIndices = { aIdx, bIdx, cIdx };
            int n = original.Vertices.Count;

            for (int i = 0; i < 3; ++i)
            {
                int startOrig = triIndices[i];
                int endOrig = triIndices[(i + 1) % 3];

                if (endOrig == (startOrig + 1) % n || startOrig == (endOrig + 1) % n)
                {
                    if (edgeIndices.Count == 0 || edgeIndices[edgeIndices.Count - 1] != i)
                    {
                        edgeIndices.Add(i);
                    }
                    edgeIndices.Add((i + 1) % 3);
                }
            }

            if (edgeIndices.Count > 0)
            {
                tri.Data["edge"] = edgeIndices;
            }
            return tri;
        }

        private bool IsPointInTriangle2D(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            Vector2 v0 = new Vector2(c.X - a.X, c.Y - a.Y);
            Vector2 v1 = new Vector2(b.X - a.X, b.Y - a.Y);
            Vector2 v2 = new Vector2(p.X - a.X, p.Y - a.Y);

            float d00 = v0.X * v0.X + v0.Y * v0.Y;
            float d01 = v0.X * v1.X + v0.Y * v1.Y;
            float d02 = v0.X * v2.X + v0.Y * v2.Y;
            float d11 = v1.X * v1.X + v1.Y * v1.Y;
            float d12 = v1.X * v2.X + v1.Y * v2.Y;

            float denom = d00 * d11 - d01 * d01;
            if (Math.Abs(denom) < 0.000001f) return false;

            float invDenom = 1.0f / denom;
            float u = (d11 * d02 - d01 * d12) * invDenom;
            float v = (d00 * d12 - d01 * d02) * invDenom;

            return (u >= -0.001f) && (v >= -0.001f) && (u + v <= 1.001f);
        }

        private bool IsEar2D(int a, int b, int c, List<Vector2> poly, List<int> indices)
        {
            Vector2 A = poly[a], B = poly[b], C = poly[c];
            float cross = (B.X - A.X) * (C.Y - B.Y) - (B.Y - A.Y) * (C.X - B.X);
            if (cross <= 0.0001f) return false;

            foreach (int idx in indices)
            {
                if (idx == a || idx == b || idx == c) continue;
                if (IsPointInTriangle2D(A, B, C, poly[idx])) return false;
            }
            return true;
        }
    }
}