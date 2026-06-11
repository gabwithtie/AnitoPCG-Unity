// Define this pass-through operation somewhere in your project:
using System.Collections.Generic;

namespace Gbe.ShapeGrammar
{
    public class FinalOutputOperation : Operation
    {
        public override List<Shape> Apply(Shape shape)
        {
            return new List<Shape>() { shape };
        }

        public override List<Shape> ApplySet(List<Shape> inputs, List<Shape> dependencies)
        {
            // A simple pass-through: It takes whatever geometry flows into it
            // and returns it as the final scene output.
            return inputs;
        }
    }
}