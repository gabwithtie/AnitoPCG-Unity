using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class SeparateEdges : Operation
    {
        public string IndexTag { get; set; } = "e";

        public override List<Shape> Apply(Shape shape)
        {
            return new List<Shape>() { shape };
        }

        public override List<Shape> ApplySet(List<Shape> shapes)
        {
            List<Shape> output = new List<Shape>();
            int edge_counter = 0;

            foreach (Shape shape in shapes)
            {
                int this_shape_start = edge_counter;

                if (shape.Vertices.Count < 2)
                {
                    Fail(); // Call base operation logging warning
                    return output;
                }

                // Create segment strings between sequential corners
                for (int i = 1; i < shape.Vertices.Count; i++)
                {
                    edge_counter++;

                    Shape newShape = new Shape(new List<Vector3> { shape.Vertices[i], shape.Vertices[i - 1] });
                    newShape.Data = new Dictionary<string, List<int>>(shape.Data); // Value clone lookup dictionary
                    newShape.Data[IndexTag] = new List<int>() { edge_counter };
                    
                    output.Add(newShape);
                }

                // Tie the final loop line segment wrapping back to vertex index 0
                Shape closingShape = new Shape(new List<Vector3> { shape.Vertices[0], shape.Vertices[shape.Vertices.Count - 1] });
                closingShape.Data = new Dictionary<string, List<int>>(shape.Data);
                closingShape.Data[IndexTag] = new List<int>() { this_shape_start };
                output.Add(closingShape);

                edge_counter++;
            }

            return output;
        }
    }
}