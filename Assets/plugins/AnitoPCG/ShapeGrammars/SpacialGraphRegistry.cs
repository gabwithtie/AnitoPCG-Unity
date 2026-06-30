using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    public static class SpatialGraphRegistry
    {
        [ThreadStatic]
        public static string CurrentTreeId;

        // FIREWALL: Prevents double-registration across execution phases
        public static bool IsRegistrationPass { get; set; } = false;

        private static readonly Dictionary<string, Bounds2D> _treeFootprints = new Dictionary<string, Bounds2D>();

        // Key 1: TreeId, Key 2: DependencyName, Value: List of Shapes
        private static readonly Dictionary<string, Dictionary<string, List<Shape>>> _treeDependencies =
            new Dictionary<string, Dictionary<string, List<Shape>>>();


        public static void GenerateScene(List<Tree> allTreesInScene, List<List<System.Numerics.Vector3>> treeSeeds)
        {
            IsRegistrationPass = true;

            // 1. Reset the global spatial matrix entirely before starting a new frame
            SpatialGraphRegistry.ClearAll();

            // 2. PHASE 1: Build layouts and register footprints/dependencies for ALL trees
            for (int i = 0; i < allTreesInScene.Count; i++)
            {
                // finalPass = false tells the tree to only execute up to its footprint/publishing nodes
                allTreesInScene[i].Evaluate(treeSeeds[i], finalPass: false);
            }

            IsRegistrationPass = false;
            // At this specific moment, the registry is completely filled with EVERY tree's 
            // flat X-Z bounding boxes and published data channels.

            // 3. PHASE 2: Complete the generation loops. Queries will now successfully find neighbors.
            for (int i = 0; i < allTreesInScene.Count; i++)
            {
                // finalPass = true calculates the master sink and processes any spatial queries
                List<Shape> finalGeometry = allTreesInScene[i].Evaluate(treeSeeds[i], finalPass: true);
            }
        }

        public struct Bounds2D
        {
            public float MinX, MaxX;
            public float MinZ, MaxZ;

            public bool Intersects(Bounds2D other)
            {
                return !(MaxX < other.MinX || MinX > other.MaxX ||
                         MaxZ < other.MinZ || MinZ > other.MaxZ);
            }
        }

        public static void ClearAll()
        {
            _treeFootprints.Clear();
            _treeDependencies.Clear();
        }

        public static void RegisterFootprint(string treeId, List<Shape> shapes)
        {
            if (!IsRegistrationPass)
                return;

            if (shapes == null || shapes.Count == 0) return;
            _treeFootprints[treeId] = CalculateXZBounds(shapes);
        }

        public static void RegisterDependency(string treeId, string dependencyName, List<Shape> shapes)
        {
            if (!IsRegistrationPass)
                return;

            if (shapes == null || shapes.Count == 0 || string.IsNullOrEmpty(dependencyName)) return;

            if (!_treeDependencies.TryGetValue(treeId, out var namedDependencies))
            {
                namedDependencies = new Dictionary<string, List<Shape>>();
                _treeDependencies[treeId] = namedDependencies;
            }

            if (!namedDependencies.TryGetValue(dependencyName, out var shapeList))
            {
                shapeList = new List<Shape>();
                namedDependencies[dependencyName] = shapeList;
            }

            shapeList.AddRange(shapes);
        }

        public static List<Shape> QueryIntersectingDependencies(string currentTreeId, string dependencyName)
        {
            List<Shape> gatheredDependencies = new List<Shape>();
            if (string.IsNullOrEmpty(dependencyName) || !_treeFootprints.TryGetValue(currentTreeId, out Bounds2D currentFootprint))
            {
                return gatheredDependencies;
            }

            foreach (var kvp in _treeFootprints)
            {
                string otherTreeId = kvp.Key;
                if (otherTreeId == currentTreeId) continue; // Skip self

                // Spatial check on flattened X-Z plane
                if (currentFootprint.Intersects(kvp.Value))
                {
                    // Check if this intersecting tree has published data under the requested dependency name
                    if (_treeDependencies.TryGetValue(otherTreeId, out var namedDependencies))
                    {
                        if (namedDependencies.TryGetValue(dependencyName, out List<Shape> sharedShapes))
                        {
                            foreach (var shape in sharedShapes)
                            {
                                Shape clonedShape = new Shape(new List<Vector3>(shape.Vertices));
                                if (shape.Data != null)
                                    clonedShape.Data = new Dictionary<string, List<int>>(shape.Data);
                                gatheredDependencies.Add(clonedShape);
                            }
                        }
                    }
                }
            }

            return gatheredDependencies;
        }

        private static Bounds2D CalculateXZBounds(List<Shape> shapes)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            foreach (var shape in shapes)
            {
                foreach (var vertex in shape.Vertices)
                {
                    if (vertex.X < minX) minX = vertex.X;
                    if (vertex.X > maxX) maxX = vertex.X;
                    if (vertex.Z < minZ) minZ = vertex.Z;
                    if (vertex.Z > maxZ) maxZ = vertex.Z;
                }
            }

            return new Bounds2D { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ };
        }
    }
}