using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    public class ExtrudeEdges : Operation
    {
        public float Height { get; set; } = 3.0f;

        public override List<Shape> Apply(Shape shape)
        {
            List<Shape> output = new List<Shape>();

            // Extrude requires a line segment (exactly 2 vertices)
            if (shape.Vertices.Count != 2)
            {
                Fail();
                return output;
            }

            // Extract the original bottom line vertices
            Vector3 b0 = shape.Vertices[0];
            Vector3 b1 = shape.Vertices[1];

            // Extrude straight up along the Y axis to get the top vertices
            Vector3 t1 = b1 + new Vector3(0, Height, 0);
            Vector3 t0 = b0 + new Vector3(0, Height, 0);

            // Form a 4-vertex Quad (counter-clockwise or matching your winding order)
            // Order: Bottom-Left, Bottom-Right, Top-Right, Top-Left
            Shape wallQuad = new Shape(new List<Vector3> { b0, b1, t1, t0 });

            // Pass forward the original metadata dictionary
            wallQuad.Data = new Dictionary<string, List<int>>(shape.Data);

            // CRITICAL: Explicitly tag the newly extruded top edge (indices 2 to 3) 
            // so that SubdivideQuad knows which wall boundary rail to follow!
            wallQuad.Data["edge"] = new List<int> { 2, 3 };

            output.Add(wallQuad);
            return output;
        }
    }
}