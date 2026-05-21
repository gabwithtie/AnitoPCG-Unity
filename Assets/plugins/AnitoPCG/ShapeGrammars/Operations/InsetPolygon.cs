using Clipper2Lib; // Requires the Clipper2 C# library
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    public class InsetPolygon : Operation
    {
        public enum Mode { Inner, Border }

        public float InsetAmount { get; set; } = 0.2f;
        public float InsetHeight { get; set; } = 0.0f;
        public Mode OperationMode { get; set; } = Mode.Inner;
        public JoinType JoinTypeSetting { get; set; } = JoinType.Miter;
        public float MiterLimit { get; set; } = 2.0f;

        public InsetPolygon(float amount = 0.2f, Mode mode = Mode.Inner)
        {
            InsetAmount = amount;
            OperationMode = mode;
        }

        public override List<Shape> Apply(Shape shape)
        {
            // Poly/Inset require at least a triangle footprint to operate
            if (shape.Vertices.Count < 3) return new List<Shape> { shape };

            // --- 1. Establish Local 2D Coordinate System using Plane Math ---
            Vector3 origin = shape.Vertices[0];
            Vector3 edgeA = Vector3.Normalize(shape.Vertices[1] - origin);

            // Compute Newell's Normal for the polygon's 3D orientation plane
            Vector3 normal = Vector3.Zero;
            int n = shape.Vertices.Count;
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

            // Lambda projection transformations mappings
            Vector2 WorldToLocal(Vector3 p)
            {
                Vector3 vec = p - origin;
                return new Vector2(Vector3.Dot(vec, edgeA), Vector3.Dot(vec, edgeB));
            }

            Vector3 LocalToWorld(Vector2 p)
            {
                return origin + (edgeA * p.X) + (edgeB * p.Y);
            }

            // --- 2. Project to 2D and Scale for Clipper Integer Space ---
            const double scale = 100000.0;
            Path64 subj = new Path64();
            foreach (var v in shape.Vertices)
            {
                Vector2 p2d = WorldToLocal(v);
                subj.Add(new Point64((long)Math.Round(p2d.X * scale), (long)Math.Round(p2d.Y * scale)));
            }

            // --- 3. Perform Offset (Negative delta value converts inflate operation into Inset) ---
            Paths64 pathsSource = new Paths64 { subj };
            Paths64 solution = Clipper.InflatePaths(
                pathsSource,
                -InsetAmount * scale,
                JoinTypeSetting,
                EndType.Polygon,
                MiterLimit
            );

            if (solution.Count == 0) return new List<Shape>();

            List<Shape> results = new List<Shape>();

            // --- 4. Generate Output Shapes based on Selected Mode ---
            if (OperationMode == Mode.Inner)
            {
                foreach (var path in solution)
                {
                    List<Vector3> verts = new List<Vector3>();
                    foreach (var pt in path)
                    {
                        // Reproject back to 3D and apply the vertical height offset down the plane normal
                        Vector3 pos = LocalToWorld(new Vector2((float)(pt.X / scale), (float)(pt.Y / scale)));
                        pos += -normal * InsetHeight;
                        verts.Add(pos);
                    }
                    Shape s = new Shape(verts);
                    s.Data = new Dictionary<string, List<int>>(shape.Data);
                    results.Add(s);
                }
                return results;
            }
            else
            {
                // BORDER MODE: Create structural sloped quads connecting Outer bounds to Inner inset path
                Path64 innerPath = solution[0];
                int numOuter = shape.Vertices.Count;

                for (int i = 0; i < numOuter; i++)
                {
                    Vector3 v0 = shape.Vertices[i];
                    Vector3 v1 = shape.Vertices[(i + 1) % numOuter];

                    // Local function helper to grab pixel-closest point on Clipper's output path 
                    Vector3 GetNearestInner(Vector3 worldPos)
                    {
                        Vector2 p2d = WorldToLocal(worldPos);
                        Point64 p64 = new Point64((long)Math.Round(p2d.X * scale), (long)Math.Round(p2d.Y * scale));

                        Point64 closest = innerPath[0];
                        double minDist = double.MaxValue;
                        foreach (var pt in innerPath)
                        {
                            double d = Math.Pow(pt.X - p64.X, 2) + Math.Pow(pt.Y - p64.Y, 2);
                            if (d < minDist)
                            {
                                minDist = d;
                                closest = pt;
                            }
                        }

                        Vector3 outPos = LocalToWorld(new Vector2((float)(closest.X / scale), (float)(closest.Y / scale)));
                        return outPos + (-normal * InsetHeight);
                    }

                    Vector3 v2 = GetNearestInner(v1); // Inner Right
                    Vector3 v3 = GetNearestInner(v0); // Inner Left

                    List<Vector3> quadVerts = new List<Vector3> { v0, v1, v2, v3 };

                    // Basic cleanup logic filtering to drop degenerate/duplicate spatial overlaps
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
                        Shape s = new Shape(cleanVerts);
                        s.Data = new Dictionary<string, List<int>>(shape.Data);
                        s.SetDataSingle("is_border", 1);
                        results.Add(s);
                    }
                }
                return results;
            }
        }
    }
}