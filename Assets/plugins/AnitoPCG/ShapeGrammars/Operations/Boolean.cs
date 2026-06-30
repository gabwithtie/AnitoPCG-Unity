using System;
using System.Collections.Generic;
using System.Numerics;
using Clipper2Lib;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class Boolean : Operation
    {
        public string CutterDataKey { get; set; } = "is_cutter";
        public bool InvertOperation { get; set; } = false;
        public ClipType OperationType { get; set; } = ClipType.Difference;

        public Boolean() { }

        public override List<Shape> Apply(Shape shape)
        {
            return new List<Shape> { shape };
        }

        public override List<Shape> ApplySet(List<Shape> shapes)
        {
            if (shapes == null || shapes.Count == 0) return shapes;

            List<Shape> subjects = new List<Shape>();
            List<Shape> cutters = new List<Shape>();

            foreach (var shape in shapes)
            {
                if (shape.Data != null && shape.Data.TryGetValue(CutterDataKey, out var values) && values.Count > 0 && values[0] == 1)
                {
                    cutters.Add(shape);
                }
                else
                {
                    subjects.Add(shape);
                }
            }

            if (InvertOperation)
            {
                var temp = subjects;
                subjects = cutters;
                cutters = temp;
            }

            if (subjects.Count == 0) return OperationType == ClipType.Union ? cutters : new List<Shape>();
            if (cutters.Count == 0) return subjects;

            List<Shape> outputShapes = new List<Shape>();

            // FIX: Process each subject individually to maintain isolated ID groups and unique planes
            foreach (var subject in subjects)
            {
                if (subject.Vertices.Count < 3)
                {
                    if (OperationType != ClipType.Intersection)
                    {
                        outputShapes.Add(subject);
                    }
                    continue;
                }

                // 1. Establish 2D Local Plane specific to THIS subject shape
                Vector3 origin = subject.Vertices[0];
                Vector3 xAxis = Vector3.Normalize(subject.Vertices[1] - origin);

                Vector3 normal = Vector3.Zero;
                int n = subject.Vertices.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector3 curr = subject.Vertices[i];
                    Vector3 next = subject.Vertices[(i + 1) % n];
                    normal.X += (curr.Y - next.Y) * (curr.Z + next.Z);
                    normal.Y += (curr.Z - next.Z) * (curr.X + next.X);
                    normal.Z += (curr.X - next.X) * (curr.Y + next.Y);
                }
                normal = Vector3.Normalize(normal);
                Vector3 yAxis = Vector3.Normalize(Vector3.Cross(normal, xAxis));

                Vector2 WorldToLocal(Vector3 worldPos)
                {
                    Vector3 local3D = worldPos - origin;
                    return new Vector2(Vector3.Dot(local3D, xAxis), Vector3.Dot(local3D, yAxis));
                }

                Vector3 LocalToWorld(Vector2 localPos)
                {
                    return origin + (xAxis * localPos.X) + (yAxis * localPos.Y);
                }

                // Capture original winding layout orientation for this explicit shape
                List<Vector2> refLocalVerts = new List<Vector2>();
                foreach (var v in subject.Vertices)
                {
                    refLocalVerts.Add(WorldToLocal(v));
                }
                float originalArea = 0f;
                for (int i = 0; i < refLocalVerts.Count; i++)
                {
                    Vector2 curr = refLocalVerts[i];
                    Vector2 next = refLocalVerts[(i + 1) % refLocalVerts.Count];
                    originalArea += (curr.X * next.Y - next.X * curr.Y);
                }
                bool originalIsCCW = originalArea >= 0f;

                PathsD subjectPaths = new PathsD();
                PathD subPath = new PathD();
                foreach (var v in subject.Vertices) subPath.Add(new PointD(WorldToLocal(v).X, WorldToLocal(v).Y));
                subjectPaths.Add(subPath);

                PathsD cutterPaths = new PathsD();
                foreach (var cut in cutters)
                {
                    PathD path = new PathD();
                    foreach (var v in cut.Vertices) path.Add(new PointD(WorldToLocal(v).X, WorldToLocal(v).Y));
                    cutterPaths.Add(path);
                }

                // 2. Execute Clipper using PolyTree to isolate hole hierarchies
                ClipperD clipper = new ClipperD();
                clipper.AddSubject(subjectPaths);
                clipper.AddClip(cutterPaths);

                PolyTreeD solutionTree = new PolyTreeD();
                clipper.Execute(OperationType, FillRule.EvenOdd, solutionTree);

                List<PathD> resolvedPaths = new List<PathD>();

                void ProcessPolyNode(PolyPathD node)
                {
                    if (node.Polygon != null && node.Polygon.Count > 0)
                    {
                        PathD currentOuter = new PathD(node.Polygon);

                        for (int i = 0; i < node.Count; i++)
                        {
                            PolyPathD childHole = (PolyPathD)node[i];

                            if (childHole.Polygon != null && childHole.Polygon.Count > 0)
                            {
                                currentOuter = MergeHoleWithSlit(currentOuter, childHole.Polygon);
                            }

                            for (int j = 0; j < childHole.Count; j++)
                            {
                                PolyPathD island = (PolyPathD)childHole[j];
                                ProcessPolyNode(island);
                            }
                        }
                        resolvedPaths.Add(currentOuter);
                    }
                    else
                    {
                        for (int i = 0; i < node.Count; i++)
                        {
                            PolyPathD child = (PolyPathD)node[i];
                            ProcessPolyNode(child);
                        }
                    }
                }

                ProcessPolyNode(solutionTree);

                // 3. Reconstruct back to 3D using the active subject's metadata dictionary
                Dictionary<string, List<int>> baseMetadata = subject.Data != null ? new Dictionary<string, List<int>>(subject.Data) : new Dictionary<string, List<int>>();

                foreach (PathD path in resolvedPaths)
                {
                    // SANITATION A: Map to local Vector2 space for precise floating-point corrections
                    List<Vector2> localOutputs = new List<Vector2>();
                    foreach (PointD pt in path)
                    {
                        localOutputs.Add(new Vector2((float)pt.x, (float)pt.y));
                    }

                    // SANITATION B: Deduplicate sequential duplicate vertices (prevents NaN vector logic downstream)
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

                    // SANITATION C: Eliminate flat collinear edge spikes
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

                    // SANITATION D: Enforce matching winding alignment to secure correct facade normals
                    float outputArea = 0f;
                    for (int i = 0; i < nonCollinearVerts.Count; i++)
                    {
                        Vector2 curr = nonCollinearVerts[i];
                        Vector2 next = nonCollinearVerts[(i + 1) % nonCollinearVerts.Count];
                        outputArea += (curr.X * next.Y - next.X * curr.Y);
                    }
                    bool outputIsCCW = outputArea >= 0f;

                    if (originalIsCCW != outputIsCCW)
                    {
                        nonCollinearVerts.Reverse();
                    }

                    // Transform clean coordinates back to 3D world space
                    List<Vector3> worldVertices = new List<Vector3>();
                    foreach (Vector2 lv in nonCollinearVerts)
                    {
                        worldVertices.Add(LocalToWorld(lv));
                    }

                    Shape newShape = new Shape(worldVertices);
                    newShape.Data = new Dictionary<string, List<int>>(baseMetadata);
                    if (newShape.Data.ContainsKey(CutterDataKey)) newShape.Data.Remove(CutterDataKey);

                    outputShapes.Add(newShape);
                }
            }

            return outputShapes;
        }

        private PathD MergeHoleWithSlit(PathD outer, PathD hole)
        {
            if (outer.Count == 0 || hole.Count == 0) return outer;

            int bestOuterIdx = 0;
            int bestHoleIdx = 0;
            double minSqDist = double.MaxValue;

            for (int i = 0; i < outer.Count; i++)
            {
                for (int j = 0; j < hole.Count; j++)
                {
                    double dx = outer[i].x - hole[j].x;
                    double dy = outer[i].y - hole[j].y;
                    double sqDist = dx * dx + dy * dy;

                    if (sqDist < minSqDist)
                    {
                        minSqDist = sqDist;
                        bestOuterIdx = i;
                        bestHoleIdx = j;
                    }
                }
            }

            PathD merged = new PathD();

            for (int i = 0; i <= bestOuterIdx; i++) merged.Add(outer[i]);

            for (int j = 0; j <= hole.Count; j++)
            {
                int idx = (bestHoleIdx + j) % hole.Count;
                merged.Add(hole[idx]);
            }

            merged.Add(outer[bestOuterIdx]);

            for (int i = bestOuterIdx + 1; i < outer.Count; i++) merged.Add(outer[i]);

            return merged;
        }
    }
}