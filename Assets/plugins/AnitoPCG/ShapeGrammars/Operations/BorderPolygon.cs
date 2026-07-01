using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class BorderPolygon : Operation
    {
        public float InsetAmount { get; set; } = 0.2f;
        public float InsetHeight { get; set; } = 0.0f;
        public float MiterLimit { get; set; } = 2.0f;

        public string OuterEdgeTag { get; set; } = "outer_edge";
        public string InnerEdgeTag { get; set; } = "inner_edge";

        // --- NEW PROPERTY: Custom Tag for Lateral Corner Connectors ---
        public string CornerEdgeTag { get; set; } = "corner_edge";

        public override List<Shape> Apply(Shape shape)
        {
            if (shape.Vertices.Count < 3) return new List<Shape> { shape };

            int n = shape.Vertices.Count;

            // --- 1. Establish Local 2D Coordinate System using Plane Math ---
            Vector3 origin = shape.Vertices[0];
            Vector3 edgeA = Vector3.Normalize(shape.Vertices[1] - origin);

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
            Vector3 edgeB = Vector3.Normalize(Vector3.Cross(normal, edgeA));

            Vector2 WorldToLocal(Vector3 p)
            {
                Vector3 vec = p - origin;
                return new Vector2(Vector3.Dot(vec, edgeA), Vector3.Dot(vec, edgeB));
            }

            Vector3 LocalToWorld(Vector2 p)
            {
                return origin + (edgeA * p.X) + (edgeB * p.Y);
            }

            // --- 2. Project Vertices to 2D and Determine Polygon Winding ---
            List<Vector2> localVerts = new List<Vector2>(n);
            for (int i = 0; i < n; i++)
            {
                localVerts.Add(WorldToLocal(shape.Vertices[i]));
            }

            float signedArea = 0f;
            for (int i = 0; i < n; i++)
            {
                Vector2 curr = localVerts[i];
                Vector2 next = localVerts[(i + 1) % n];
                signedArea += (curr.X * next.Y - next.X * curr.Y);
            }
            bool isCCW = signedArea >= 0f;

            // --- 3. Compute Inward Edge Normals and Offset Lines ---
            Vector2[] edgeNormals = new Vector2[n];
            Vector2[] offsetLineStart = new Vector2[n];
            Vector2[] offsetLineEnd = new Vector2[n];

            for (int i = 0; i < n; i++)
            {
                Vector2 curr = localVerts[i];
                Vector2 next = localVerts[(i + 1) % n];
                Vector2 edgeDir = next - curr;
                float len = edgeDir.Length();

                if (len > 0.00001f) edgeDir /= len;

                if (isCCW)
                    edgeNormals[i] = new Vector2(-edgeDir.Y, edgeDir.X);
                else
                    edgeNormals[i] = new Vector2(edgeDir.Y, -edgeDir.X);

                offsetLineStart[i] = curr + edgeNormals[i] * InsetAmount;
                offsetLineEnd[i] = next + edgeNormals[i] * InsetAmount;
            }

            // --- 4. Intersect Adjacent Lines ---
            Vector2[] innerVerts2D = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                int prevEdge = (i - 1 + n) % n;
                int currEdge = i;

                Vector2 fallback = localVerts[i] + edgeNormals[currEdge] * InsetAmount;

                innerVerts2D[i] = FindLineIntersection(
                    offsetLineStart[prevEdge], offsetLineEnd[prevEdge],
                    offsetLineStart[currEdge], offsetLineEnd[currEdge],
                    fallback
                );

                Vector2 offsetVector = innerVerts2D[i] - localVerts[i];
                float maxDist = MiterLimit * InsetAmount;
                if (offsetVector.LengthSquared() > maxDist * maxDist)
                {
                    innerVerts2D[i] = localVerts[i] + Vector2.Normalize(offsetVector) * maxDist;
                }
            }

            // --- 5. Convert Inner Vertices back to 3D ---
            List<Vector3> innerVerts3D = new List<Vector3>(n);
            for (int i = 0; i < n; i++)
            {
                Vector3 pos = LocalToWorld(innerVerts2D[i]);
                pos += -normal * InsetHeight;
                innerVerts3D.Add(pos);
            }

            // --- 6. Construct Layout Outputs ---
            List<Shape> results = new List<Shape>();

            for (int i = 0; i < n; i++)
            {
                Vector3 v0 = shape.Vertices[i];               // Outer Left
                Vector3 v1 = shape.Vertices[(i + 1) % n];     // Outer Right
                Vector3 v2 = innerVerts3D[(i + 1) % n];       // Inner Right
                Vector3 v3 = innerVerts3D[i];                 // Inner Left

                List<Vector3> quadVerts = new List<Vector3> { v0, v1, v2, v3 };

                List<Vector3> cleanVerts = new List<Vector3>();
                foreach (var v in quadVerts)
                {
                    if (cleanVerts.Count == 0 || (cleanVerts[cleanVerts.Count - 1] - v).LengthSquared() > 0.0001f)
                    {
                        cleanVerts.Add(v);
                    }
                }

                if (cleanVerts.Count >= 3)
                {
                    Shape edgeQuad = new Shape(cleanVerts);
                    edgeQuad.Data = new Dictionary<string, List<int>>(shape.Data);
                    edgeQuad.SetDataSingle("is_border", 1);

                    int matchingEdgeIndex = i + 1; // 1-based structural matching indices

                    if (!string.IsNullOrEmpty(OuterEdgeTag))
                    {
                        edgeQuad.SetDataSingle(OuterEdgeTag, 1);
                    }

                    if (!string.IsNullOrEmpty(InnerEdgeTag) && cleanVerts.Count == 4)
                    {
                        edgeQuad.SetDataSingle(InnerEdgeTag, 3);
                    }

                    // --- NEW LOGIC: Inject index-tags to isolate corner-edges later ---
                    if (!string.IsNullOrEmpty(CornerEdgeTag))
                    {
                        edgeQuad.Data.Add(CornerEdgeTag , new List<int>() { 0, 2 });
                    }

                    results.Add(edgeQuad);
                }
            }
            return results;
        }

        private Vector2 FindLineIntersection(Vector2 a1, Vector2 b1, Vector2 a2, Vector2 b2, Vector2 fallback)
        {
            float d = (a1.X - b1.X) * (a2.Y - b2.Y) - (a1.Y - b1.Y) * (a2.X - b2.X);
            if (Math.Abs(d) < 0.00001f) return fallback;

            float t = ((a1.X - a2.X) * (a2.Y - b2.Y) - (a1.Y - a2.Y) * (a2.X - b2.X)) / d;
            return new Vector2(a1.X + t * (b1.X - a1.X), a1.Y + t * (b1.Y - a1.Y));
        }
    }
}