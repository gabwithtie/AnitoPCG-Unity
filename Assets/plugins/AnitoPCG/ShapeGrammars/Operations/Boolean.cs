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

            // 1. Establish 2D Local Plane
            Shape refShape = subjects[0];
            if (refShape.Vertices.Count < 3) return shapes;

            Vector3 origin = refShape.Vertices[0];
            Vector3 xAxis = Vector3.Normalize(refShape.Vertices[1] - origin);

            Vector3 normal = Vector3.Zero;
            int n = refShape.Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 curr = refShape.Vertices[i];
                Vector3 next = refShape.Vertices[(i + 1) % n];
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

            PathsD subjectPaths = new PathsD();
            foreach (var sub in subjects)
            {
                PathD path = new PathD();
                foreach (var v in sub.Vertices) path.Add(new PointD(WorldToLocal(v).X, WorldToLocal(v).Y));
                subjectPaths.Add(path);
            }

            PathsD cutterPaths = new PathsD();
            foreach (var cut in cutters)
            {
                PathD path = new PathD();
                foreach (var v in cut.Vertices) path.Add(new PointD(WorldToLocal(v).X, WorldToLocal(v).Y));
                cutterPaths.Add(path);
            }

            // 2. Execute Clipper using PolyTree to capture Hole Hierarchies
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

                    // Iterate using .Count and cast the base PolyPath down to PolyPathD
                    for (int i = 0; i < node.Count; i++)
                    {
                        PolyPathD childHole = (PolyPathD)node[i];

                        if (childHole.Polygon != null && childHole.Polygon.Count > 0)
                        {
                            currentOuter = MergeHoleWithSlit(currentOuter, childHole.Polygon);
                        }

                        // Any shape inside a hole is an island (a new solid outer shape)
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
                    // Root node is invisible, jump straight to its children (the actual shapes)
                    for (int i = 0; i < node.Count; i++)
                    {
                        PolyPathD child = (PolyPathD)node[i];
                        ProcessPolyNode(child);
                    }
                }
            }

            ProcessPolyNode(solutionTree);

            // 3. Convert back to 3D Shapes
            List<Shape> outputShapes = new List<Shape>();
            Dictionary<string, List<int>> baseMetadata = refShape.Data != null ? new Dictionary<string, List<int>>(refShape.Data) : new Dictionary<string, List<int>>();

            foreach (PathD path in resolvedPaths)
            {
                if (path.Count < 3) continue;

                List<Vector3> worldVertices = new List<Vector3>();
                foreach (PointD pt in path) worldVertices.Add(LocalToWorld(new Vector2((float)pt.x, (float)pt.y))); // Depending on Clipper2 wrapper version, it might be pt.X and pt.Y

                Shape newShape = new Shape(worldVertices);
                newShape.Data = new Dictionary<string, List<int>>(baseMetadata);
                if (newShape.Data.ContainsKey(CutterDataKey)) newShape.Data.Remove(CutterDataKey);

                outputShapes.Add(newShape);
            }

            return outputShapes;
        }

        // Helper: Slices a zero-width cut from the outer edge to the hole
        private PathD MergeHoleWithSlit(PathD outer, PathD hole)
        {
            if (outer.Count == 0 || hole.Count == 0) return outer;

            int bestOuterIdx = 0;
            int bestHoleIdx = 0;
            double minSqDist = double.MaxValue;

            // Find the closest pair of vertices between the outer ring and the hole
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

            // 1. Walk the outer edge up to the closest vertex
            for (int i = 0; i <= bestOuterIdx; i++) merged.Add(outer[i]);

            // 2. Cross the slit and walk the entire inner hole
            for (int j = 0; j <= hole.Count; j++)
            {
                int idx = (bestHoleIdx + j) % hole.Count;
                merged.Add(hole[idx]);
            }

            // 3. Cross the slit back to the exact same outer vertex
            merged.Add(outer[bestOuterIdx]);

            // 4. Finish walking the rest of the outer edge
            for (int i = bestOuterIdx + 1; i < outer.Count; i++) merged.Add(outer[i]);

            return merged;
        }
    }
}