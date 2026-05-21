using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    public class Triangulate : Operation
    {
        // Toggle to automatically detect winding and prevent "inside-out" triangulation
        public bool AutoFixWinding { get; set; } = true;

        public Triangulate()
        {
        }

        public override List<Shape> Apply(Shape shape)
        {
            List<Shape> triangles = new List<Shape>();
            int n = shape.Vertices.Count;
            if (n < 3) return triangles;

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

            // In C#, LengthSquared() replaces SqrMagnitude()
            if (normal.LengthSquared() < 0.000001f) return triangles; // Degenerate polygon
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

            // 3. Calculate 2D Signed Area to determine actual winding
            float signedArea = 0.0f;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                signedArea += (poly2D[i].X * poly2D[j].Y - poly2D[j].X * poly2D[i].Y);
            }

            // If signedArea is negative, the polygon is defined in Clockwise order (Inside-out)
            bool isCW = signedArea < 0.0f;

            // 4. Setup working indices, forcing CCW traversal to prevent outside triangulation
            List<int> currentIndices = new List<int>(n);
            for (int i = 0; i < n; ++i)
            {
                if (AutoFixWinding && isCW)
                {
                    currentIndices.Add(n - 1 - i); // Reverse the working list to CCW
                }
                else
                {
                    currentIndices.Add(i);
                }
            }

            // 5. 2D Ear Clipping Loop
            int attempts = 0;
            int maxAttempts = n * 2; // Infinite loop failsafe

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
                        triangles.Add(CreateTriangle(shape, a, b, c));
                        currentIndices.RemoveAt(i); // Equivalent to erase
                        earFound = true;
                        break;
                    }
                }

                if (!earFound) break;
                attempts++;
            }

            // Add the final remaining triangle
            if (currentIndices.Count == 3)
            {
                triangles.Add(CreateTriangle(shape, currentIndices[0], currentIndices[1], currentIndices[2]));
            }

            return triangles;
        }

        private Shape CreateTriangle(Shape original, int aIdx, int bIdx, int cIdx)
        {
            // Outputs the triangle using the original 3D spatial geometry
            Shape tri = new Shape(new List<Vector3> { original.Vertices[aIdx], original.Vertices[bIdx], original.Vertices[cIdx] });

            // Shallow copy/reference allocation matching C++ assignment
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
                tri.Data["edge"] = edgeIndices; // insert_or_assign logic
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

            // 2D Cross product (determines convex vs reflex)
            // Because we forced CCW traversal above, an ear MUST have a positive cross product.
            float cross = (B.X - A.X) * (C.Y - B.Y) - (B.Y - A.Y) * (C.X - B.X);

            // If it's <= 0, it's reflex or collinear (pointing outward into the void)
            if (cross <= 0.0001f) return false;

            // Point in triangle check
            foreach (int idx in indices)
            {
                if (idx == a || idx == b || idx == c) continue;
                if (IsPointInTriangle2D(A, B, C, poly[idx])) return false;
            }
            return true;
        }
    }
}