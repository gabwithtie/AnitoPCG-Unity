using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    public class MergeForFacade : Operation
    {
        public enum Layout { XY, YX, nYX }
        public enum Alignment { center, left, right, top, bottom }

        public int facadeSegmentsX = 1;
        public int facadeSegmentsY = 1;
        public Layout layout = Layout.XY;
        public string metadataKey = "facade";

        // --- NEW: Grouping Tag ---
        [UnityEngine.Tooltip("If set, groups shapes by this metadata key before merging. Useful for processing multiple separate walls simultaneously.")]
        public string groupingTag = "";


        public override List<Shape> Apply(Shape shape)
        {
            return new List<Shape> { shape };
        }

        public override List<Shape> ApplySet(List<Shape> shapes)
        {
            // Early exit conditions
            if (shapes == null || shapes.Count == 0) return shapes;
            if (facadeSegmentsX <= 1 && facadeSegmentsY <= 1) return shapes;

            // If no grouping tag is provided, process all shapes as a single batch
            if (string.IsNullOrEmpty(groupingTag))
            {
                return ProcessGroup(shapes);
            }

            // --- NEW: Group shapes by metadata tag ---
            Dictionary<int, List<Shape>> groupedShapes = new Dictionary<int, List<Shape>>();
            List<Shape> ungroupedShapes = new List<Shape>(); // Shapes that lack the tag pass through

            foreach (var shape in shapes)
            {
                // Ensure shape data exists and contains our grouping key
                if (shape.Data != null && shape.Data.ContainsKey(groupingTag) && shape.Data[groupingTag].Count > 0)
                {
                    int groupVal = shape.Data[groupingTag][0];
                    if (!groupedShapes.ContainsKey(groupVal))
                    {
                        groupedShapes[groupVal] = new List<Shape>();
                    }
                    groupedShapes[groupVal].Add(shape);
                }
                else
                {
                    ungroupedShapes.Add(shape);
                }
            }

            List<Shape> finalOutput = new List<Shape>();

            // Process each isolated group independently
            foreach (var group in groupedShapes.Values)
            {
                finalOutput.AddRange(ProcessGroup(group));
            }

            // Add any shapes that didn't have the tag back into the stream unharmed
            finalOutput.AddRange(ungroupedShapes);

            return finalOutput;
        }

        // --- EXTRACTED CORE LOGIC ---
        private List<Shape> ProcessGroup(List<Shape> shapes)
        {
            if (shapes == null || shapes.Count == 0) return shapes;

            // 1. Establish Local 2D Coordinate System from the first shape
            Vector3 origin = shapes[0].Vertices[0];
            Vector3 edgeU = Vector3.Normalize(shapes[0].Vertices[1] - origin);

            // Newell's method to find robust normal for the plane
            Vector3 normal = Vector3.Zero;
            int n = shapes[0].Vertices.Count;
            for (int i = 0; i < n; i++)
            {
                Vector3 curr = shapes[0].Vertices[i];
                Vector3 next = shapes[0].Vertices[(i + 1) % n];
                normal.X += (curr.Y - next.Y) * (curr.Z + next.Z);
                normal.Y += (curr.Z - next.Z) * (curr.X + next.X);
                normal.Z += (curr.X - next.X) * (curr.Y + next.Y);
            }
            normal = Vector3.Normalize(normal);
            Vector3 edgeV = Vector3.Normalize(Vector3.Cross(normal, edgeU));

            // Projection functions
            Vector2 WorldToLocal(Vector3 p)
            {
                Vector3 vec = p - origin;
                return new Vector2(Vector3.Dot(vec, edgeU), Vector3.Dot(vec, edgeV));
            }

            Vector3 LocalToWorld(Vector2 p)
            {
                return origin + (edgeU * p.X) + (edgeV * p.Y);
            }

            List<ShapeGridData> gridData = new List<ShapeGridData>();
            List<float> u_coords = new List<float>();
            List<float> v_coords = new List<float>();

            // 2. Project centroids to 2D to deduce the grid
            foreach (var shape in shapes)
            {
                // Abort and return original if not all inputs are quads
                if (shape.Vertices.Count != 4) return shapes;

                Vector2 centroid = Vector2.Zero;
                foreach (var v in shape.Vertices)
                {
                    Vector2 localV = WorldToLocal(v);
                    centroid.X += localV.X;
                    centroid.Y += localV.Y;
                }
                centroid.X /= 4.0f;
                centroid.Y /= 4.0f;

                // ApplyLayout (Swap X/Y if needed)
                if (layout == Layout.YX || layout == Layout.nYX)
                {
                    float temp = centroid.X;
                    centroid.X = centroid.Y;
                    centroid.Y = temp;
                }

                gridData.Add(new ShapeGridData { shape = shape, centroid = centroid });
                u_coords.Add(centroid.X);
                v_coords.Add(centroid.Y);
            }

            // Sort and deduplicate coordinates (using a tolerance to group rows/cols)
            u_coords = UniqueTolerant(u_coords, 0.05f);
            v_coords = UniqueTolerant(v_coords, 0.05f);

            int totalCols = u_coords.Count;
            int totalRows = v_coords.Count;

            // 3. Fallback check: If requested size exceeds available grid, return original
            if (facadeSegmentsX > totalCols || facadeSegmentsY > totalRows)
            {
                return shapes;
            }

            // 4. Calculate bounds for the center target block
            int startCol = (totalCols - facadeSegmentsX) / 2;
            int startRow = 0;
            int endCol = startCol + facadeSegmentsX - 1;
            int endRow = startRow + facadeSegmentsY - 1;

            List<Shape> output = new List<Shape>();
            List<Vector2> mergedUVVertices = new List<Vector2>();
            Shape metadataSource = shapes[0];
            bool metadataSourced = false;

            // 5. Partition and Merge
            foreach (var data in gridData)
            {
                // Map centroids back to row/col integer indices
                for (int c = 0; c < totalCols; ++c)
                {
                    if (Math.Abs(data.centroid.X - u_coords[c]) < 0.05f) data.col = c;
                }
                for (int r = 0; r < totalRows; ++r)
                {
                    int final_r = r;

                    if (layout == Layout.nYX)
                    {
                        final_r = totalRows - 1 - r; // Invert row order for nYX layout
                    }

                    if (Math.Abs(data.centroid.Y - v_coords[r]) < 0.05f) data.row = final_r;
                }

                // Check if the shape falls inside our target center facade
                if (data.col >= startCol && data.col <= endCol &&
                    data.row >= startRow && data.row <= endRow)
                {
                    // Collect all vertices to form the new bounding box
                    foreach (var v in data.shape.Vertices)
                    {
                        mergedUVVertices.Add(WorldToLocal(v));
                    }

                    // Grab metadata from the first merged quad
                    if (!metadataSourced)
                    {
                        metadataSource = data.shape;
                        metadataSourced = true;
                    }
                }
                else
                {
                    // Shape is outside the facade bounds, pass it through unharmed
                    output.Add(data.shape);
                }
            }

            // 6. Synthesize the new Facade Quad from the collected UV extremes
            if (mergedUVVertices.Count > 0)
            {
                float minU = mergedUVVertices.Min(v => v.X);
                float maxU = mergedUVVertices.Max(v => v.X);
                float minV = mergedUVVertices.Min(v => v.Y);
                float maxV = mergedUVVertices.Max(v => v.Y);

                // Reconstruct quad in CCW winding order (Bottom-Left, Bottom-Right, Top-Right, Top-Left)
                List<Vector3> mergedVerts = new List<Vector3>
                {
                    LocalToWorld(new Vector2(minU, minV)),
                    LocalToWorld(new Vector2(maxU, minV)),
                    LocalToWorld(new Vector2(maxU, maxV)),
                    LocalToWorld(new Vector2(minU, maxV))
                };

                Shape mergedShape = new Shape(mergedVerts);

                // Copy dictionary metadata if available and set facade tag
                if (metadataSource.Data != null)
                {
                    mergedShape.Data = new Dictionary<string, List<int>>(metadataSource.Data);
                }
                mergedShape.SetDataSingle(metadataKey, 1);

                output.Add(mergedShape);
            }

            return output;
        }

        // Helper structure for grid sorting
        private class ShapeGridData
        {
            public Shape shape;
            public Vector2 centroid;
            public int col = -1;
            public int row = -1;
        }

        // Helper function translating the C++ unique/tolerant lambda
        private List<float> UniqueTolerant(List<float> coords, float tol)
        {
            coords.Sort();
            List<float> result = new List<float>();

            if (coords.Count == 0) return result;

            result.Add(coords[0]);
            for (int i = 1; i < coords.Count; i++)
            {
                if (Math.Abs(coords[i] - result.Last()) >= tol)
                {
                    result.Add(coords[i]);
                }
            }
            return result;
        }
    }
}