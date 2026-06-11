using System;
using System.Collections.Generic;

namespace Gbe.ShapeGrammar
{
    public class Pass : Operation
    {
        public override List<Shape> Apply(Shape shape)
        {
            // Direct optimization pass: simply forward the shape reference along the execution pipeline
            return new List<Shape> { shape };
        }
    }
}