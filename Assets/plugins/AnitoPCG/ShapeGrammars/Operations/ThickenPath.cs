using System;
using System.Collections.Generic;
using System.Numerics;
using Clipper2Lib;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class ThickenPath : Operation
    {
        // The total horizontal width of the generated road footprint
        public float RoadWidth { get; set; } = 4.0f;

        // Controls how corners are handled (Miter, Round, Bevel)
        public JoinType JoinTypeSetting { get; set; } = JoinType.Miter;

        // Controls the ends of the road (Square or Butt)
        public EndType EndTypeSetting { get; set; } = EndType.Square;

        public ThickenPath() { }

        public override List<Shape> Apply(Shape shape)
        {
            if (shape == null || shape.Vertices.Count < 2) return new List<Shape> { shape };

            List<Shape> results = new List<Shape>();

            // 1. Build a robust 2D projection plane down the spine of the open path
            Vector3 origin = shape.Vertices[0];
            Vector3 xAxis = Vector3.Normalize(shape.Vertices[1] - origin);

            Vector3 upVector = Vector3.UnitY;
            if (Math.Abs(Vector3.Dot(xAxis, upVector)) > 0.98f)
            {
                upVector = Vector3.UnitZ;
            }

            Vector3 yAxis = Vector3.Normalize(Vector3.Cross(upVector, xAxis));
            Vector3 normal = Vector3.Normalize(Vector3.Cross(xAxis, yAxis));

            Vector2 WorldToLocal(Vector3 worldPos)
            {
                Vector3 local3D = worldPos - origin;
                return new Vector2(Vector3.Dot(local3D, xAxis), Vector3.Dot(local3D, yAxis));
            }

            Vector3 LocalToWorld(Vector2 localPos)
            {
                return origin + (xAxis * localPos.X) + (yAxis * localPos.Y);
            }

            PathD openPath = new PathD();
            foreach (var v in shape.Vertices)
            {
                Vector2 local = WorldToLocal(v);
                openPath.Add(new PointD(local.X, local.Y));
            }

            PathsD pathContainer = new PathsD { openPath };

            // 2. Execute Clipper Offset via the PathsD-compatible helper function
            double offsetDistance = RoadWidth / 2.0;

            // FIX: Using Clipper.InflatePaths removes the manual ClipperOffset instantiation, 
            // natively returning a clean PathsD without type-casting errors.
            PathsD solutionPaths = Clipper.InflatePaths(
                pathContainer,
                offsetDistance,
                JoinTypeSetting,
                EndTypeSetting,
                2.0 // Miter limit fallback
            );

            // 3. Process outputs back to 3D with standard geometry sanitation
            Dictionary<string, List<int>> baseMetadata = shape.Data != null ? new Dictionary<string, List<int>>(shape.Data) : new Dictionary<string, List<int>>();

            foreach (PathD path in solutionPaths)
            {
                List<Vector2> localOutputs = new List<Vector2>();
                foreach (PointD pt in path)
                {
                    localOutputs.Add(new Vector2((float)pt.x, (float)pt.y));
                }

                // SANITATION A: Deduplicate adjacent micro-edges
                List<Vector2> uniqueVerts = new List<Vector2>();
                foreach (var v in localOutputs)
                {
                    if (uniqueVerts.Count == 0 || Vector2.DistanceSquared(uniqueVerts[uniqueVerts.Count - 1], v) > 0.00001f)
                    {
                        uniqueVerts.Add(v);
                    }
                }
                if (uniqueVerts.Count > 1 && Vector2.DistanceSquared(uniqueVerts[0], uniqueVerts[uniqueVerts.Count - 1]) < 0.00001f)
                {
                    uniqueVerts.RemoveAt(uniqueVerts.Count - 1);
                }

                if (uniqueVerts.Count < 3) continue;

                // SANITATION B: Clean out flat collinear edge spikes
                List<Vector2> nonCollinearVerts = new List<Vector2>();
                int m = uniqueVerts.Count;
                for (int i = 0; i < m; i++)
                {
                    Vector2 prev = uniqueVerts[(i - 1 + m) % m];
                    Vector2 curr = uniqueVerts[i];
                    Vector2 next = uniqueVerts[(i + 1) % m];

                    Vector2 d1 = curr - prev;
                    Vector2 d2 = next - curr;

                    if (d1.LengthSquared() > 0.00001f) d1 = Vector2.Normalize(d1);
                    if (d2.LengthSquared() > 0.00001f) d2 = Vector2.Normalize(d2);

                    float cross = d1.X * d2.Y - d1.Y * d2.X;
                    if (Math.Abs(cross) > 0.001f)
                    {
                        nonCollinearVerts.Add(curr);
                    }
                }

                if (nonCollinearVerts.Count < 3) continue;

                // Transform outline back into 3D space
                List<Vector3> worldVertices = new List<Vector3>();
                foreach (Vector2 lv in nonCollinearVerts)
                {
                    worldVertices.Add(LocalToWorld(lv));
                }

                Shape roadFootprint = new Shape(worldVertices);
                roadFootprint.Data = new Dictionary<string, List<int>>(baseMetadata);

                results.Add(roadFootprint);
            }

            return results;
        }

        public override List<Shape> ApplySet(List<Shape> shapes)
        {
            if (shapes == null || shapes.Count == 0) return shapes;

            List<Shape> outputs = new List<Shape>();
            foreach (var shape in shapes)
            {
                outputs.AddRange(Apply(shape));
            }
            return outputs;
        }
    }
}