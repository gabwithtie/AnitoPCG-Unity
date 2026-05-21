using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    public class SeparateEdges : Operation
    {
        public override List<Shape> Apply(Shape shape)
        {
            List<Shape> output = new List<Shape>();

            if (shape.Vertices.Count < 2)
            {
                Fail(); // Call base operation logging warning
                return output;
            }

            // Create segment strings between sequential corners
            for (int i = 1; i < shape.Vertices.Count; i++)
            {
                Shape newShape = new Shape(new List<Vector3> { shape.Vertices[i], shape.Vertices[i - 1] });
                newShape.Data = new Dictionary<string, List<int>>(shape.Data); // Value clone lookup dictionary
                output.Add(newShape);
            }

            // Tie the final loop line segment wrapping back to vertex index 0
            Shape closingShape = new Shape(new List<Vector3> { shape.Vertices[0], shape.Vertices[shape.Vertices.Count - 1] });
            closingShape.Data = new Dictionary<string, List<int>>(shape.Data);
            output.Add(closingShape);

            return output;
        }
    }
}