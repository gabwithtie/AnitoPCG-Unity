using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using UnityEngine;

namespace Gbe.ShapeGrammar
{
    using Vector3 = System.Numerics.Vector3;
    using Vector2 = System.Numerics.Vector2;

    [Serializable]
    public class Triangulate : Operation
    {
        public bool AutoFixWinding { get; set; } = true;
        public bool SplitIntoRightTriangles { get; set; } = false;

        public override List<Shape> Apply(Shape shape)
        {
            List<Shape> baseTriangles = new List<Shape>();
            int n = shape.Vertices.Count;
            if (n < 3) return baseTriangles;

            // 1. Calculate Newell's Normal and 2D Plane
            Vector3 normal = Vector3.Zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 curr = shape.Vertices[i];
                Vector3 next = shape.Vertices[(i + 1) % n];
                normal.X += (curr.Y - next.Y) * (curr.Z + next.Z);
                normal.Y += (curr.Z - next.Z) * (curr.X + next.X);
                normal.Z += (curr.X - next.X) * (curr.Y + next.Y);
            }
            normal = Vector3.Normalize(normal);

            Vector3 xAxis = Vector3.Normalize(shape.Vertices[1] - shape.Vertices[0]);
            Vector3 yAxis = Vector3.Normalize(Vector3.Cross(normal, xAxis));
            Vector3 origin = shape.Vertices[0];

            List<Vector2> poly2D = new List<Vector2>();
            List<int> indices = new List<int>();

            for (int i = 0; i < n; i++)
            {
                Vector3 local = shape.Vertices[i] - origin;
                poly2D.Add(new Vector2(Vector3.Dot(local, xAxis), Vector3.Dot(local, yAxis)));
                indices.Add(i);
            }

            // Ensure CCW Winding
            if (AutoFixWinding && GetSignedArea(poly2D) < 0) indices.Reverse();

            // 2. EAR CLIPPING LOOP
            int failSafe = indices.Count * 2;
            while (indices.Count > 3 && failSafe-- > 0)
            {
                bool earFound = false;
                for (int i = 0; i < indices.Count; i++)
                {
                    int prev = (i == 0) ? indices.Count - 1 : i - 1;
                    int next = (i == indices.Count - 1) ? 0 : i + 1;

                    if (IsEar(poly2D, indices, prev, i, next))
                    {
                        List<Vector3> triVerts = new List<Vector3>
                        {
                            shape.Vertices[indices[prev]],
                            shape.Vertices[indices[i]],
                            shape.Vertices[indices[next]]
                        };

                        Shape tri = new Shape(triVerts);
                        if (shape.Data != null) tri.Data = new Dictionary<string, List<int>>(shape.Data);
                        baseTriangles.Add(tri);

                        indices.RemoveAt(i);
                        earFound = true;
                        break;
                    }
                }

                // If the failsafe trips, break out to prevent unity from freezing
                if (!earFound) break;
            }

            // Add final triangle
            if (indices.Count == 3)
            {
                List<Vector3> triVerts = new List<Vector3>
                {
                    shape.Vertices[indices[0]],
                    shape.Vertices[indices[1]],
                    shape.Vertices[indices[2]]
                };
                Shape tri = new Shape(triVerts);
                if (shape.Data != null) tri.Data = new Dictionary<string, List<int>>(shape.Data);
                baseTriangles.Add(tri);
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

        private float GetSignedArea(List<Vector2> poly)
        {
            float area = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector2 curr = poly[i];
                Vector2 next = poly[(i + 1) % poly.Count];
                area += (curr.X * next.Y) - (next.X * curr.Y);
            }
            return area * 0.5f;
        }

        private bool IsEar(List<Vector2> poly, List<int> indices, int prevNode, int currNode, int nextNode)
        {
            Vector2 a = poly[indices[prevNode]];
            Vector2 b = poly[indices[currNode]];
            Vector2 c = poly[indices[nextNode]];

            // 1. Must be strictly convex. 
            // A strict epsilon > 0.0001f prevents flat, collapsed slit edges from being parsed as ears.
            float cross = (b.X - a.X) * (c.Y - b.Y) - (b.Y - a.Y) * (c.X - b.X);
            if (cross <= 0.0001f) return false;

            // 2. Point in Triangle test with Keyhole Tolerance
            for (int i = 0; i < indices.Count; i++)
            {
                if (i == prevNode || i == currNode || i == nextNode) continue;

                Vector2 p = poly[indices[i]];

                // THE FIX: Skip this vertex if it physically occupies the exact same spot 
                // as one of the ear's vertices. This prevents the coincident vertices of a boolean slit 
                // from registering as being "inside" the triangle.
                if (Vector2.DistanceSquared(p, a) < 0.0001f ||
                    Vector2.DistanceSquared(p, b) < 0.0001f ||
                    Vector2.DistanceSquared(p, c) < 0.0001f)
                {
                    continue;
                }

                if (IsPointInTriangle2D(a, b, c, p)) return false;
            }

            return true;
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

            // Allow standard > 0 tolerance inclusion 
            return (u >= 0) && (v >= 0) && (u + v <= 1);
        }
    }
}