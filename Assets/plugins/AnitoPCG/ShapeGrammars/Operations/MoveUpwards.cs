using System;
using System.Collections.Generic;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class MoveUpwards : Operation
    {
        public float OffsetHeight { get; set; } = 1;

        public override List<Shape> Apply(Shape shape)
        {
            var modifiedShape = new Shape
            {
                Vertices = new List<System.Numerics.Vector3>(shape.Vertices),
                Data = new Dictionary<string, List<int>>(shape.Data)
            };

            for (int i = 0; i < modifiedShape.Vertices.Count; i++)
            {
                var vertex = modifiedShape.Vertices[i];

                vertex.Y += OffsetHeight;

                modifiedShape.Vertices[i] = vertex;
            }

            return new List<Shape> { modifiedShape };
        }
    }
}