using System;
using System.Collections.Generic;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class DataFilter : Operation
    {
        public string MetadataKey { get; set; } = "i";
        public int TargetValue { get; set; } = 0;
        public bool InvertFilter { get; set; } = false;

        public override List<Shape> Apply(Shape shape)
        {
            bool hasTargetTag = shape.Data.TryGetValue(MetadataKey, out var values) &&
                                values.Count > 0 &&
                                values[0] == TargetValue;

            // Xor logic to handle clean standard or inverted pass-through conditions
            if (hasTargetTag ^ InvertFilter)
            {
                return new List<Shape> { shape };
            }

            return new List<Shape>(); // Drops shape from this output path branch
        }
    }
}