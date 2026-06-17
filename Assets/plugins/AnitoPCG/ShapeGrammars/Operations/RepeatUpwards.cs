using System;
using System.Collections.Generic;
using System.Numerics;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class RepeatUpwards : Operation
    {
        public float Height { get; set; } = 3.0f;
        public float MaxDistance { get; set; } = 1.0f;
        public bool DropFirst { get; set; } = false;
        public bool DropLast { get; set; } = false;
        public float FinalDistancePerSegment { get; private set; } = 0f;
        public string IdToStoreIndex { get; set; } = "i";

        private Vector3 pathVector;
        private float totalDistance;
        private int numSegments;
        private int numShapes;


        public override List<Shape> Apply(Shape shape)
        {
            List<Shape> output = new List<Shape>();

            pathVector = new Vector3(0, 1, 0) * Height;
            totalDistance = pathVector.Length(); // .Length() replaces .Magnitude() in System.Numerics

            // Calculate intervals intervals
            numSegments = Math.Max(1, (int)Math.Ceiling(totalDistance / MaxDistance));
            numShapes = numSegments + 1;

            FinalDistancePerSegment = totalDistance / numSegments;

            for (int i = 0; i < numShapes; ++i)
            {
                // Check if we should skip based on dropFirst or dropLast
                if (i == 0 && DropFirst) continue;
                if (i == numShapes - 1 && DropLast) continue;

                // Handle safety divide division checks if there is only 1 shape
                float t = (numShapes > 1) ? (float)i / (numShapes - 1) : 0f;
                Vector3 translation = Vector3.Zero + (pathVector * t);

                List<Vector3> newVerts = new List<Vector3>();
                foreach (var v in shape.Vertices)
                {
                    newVerts.Add(v + translation);
                }

                Shape duplicate = new Shape(newVerts);
                duplicate.Data = new Dictionary<string, List<int>>(shape.Data);
                duplicate.SetDataSingle(IdToStoreIndex, i); // Track structural array stack indexing tags
                output.Add(duplicate);
            }

            return output;
        }
    }
}