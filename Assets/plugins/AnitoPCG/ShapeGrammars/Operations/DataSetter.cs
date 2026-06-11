using System;
using System.Collections.Generic;

namespace Gbe.ShapeGrammar
{
    public class DataSetter : Operation
    {
        public string MetadataKey { get; set; } = "DropFirst";
        public int ValueToSet { get; set; } = 1;

        public override List<Shape> Apply(Shape shape)
        {
            // Clone the shape to prevent mutating historical timeline nodes
            Shape modifiedShape = new Shape(new List<System.Numerics.Vector3>(shape.Vertices));
            modifiedShape.Data = new Dictionary<string, List<int>>(shape.Data);

            // Assign the tracking flag value
            modifiedShape.SetDataSingle(MetadataKey, ValueToSet);

            return new List<Shape> { modifiedShape };
        }
    }
}