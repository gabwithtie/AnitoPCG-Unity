using System;
using System.Collections.Generic;

namespace Gbe.ShapeGrammar
{
    public abstract class Operation
    {
        protected void Fail()
        {
            Console.Error.WriteLine("Operation failed to apply!");
        }

        // Equivalent to the pure virtual method = 0;
        public abstract List<Shape> Apply(Shape shape);

        // Equivalent to the base virtual method
        public virtual List<Shape> ApplySet(List<Shape> shapes)
        {
            List<Shape> output = new List<Shape>();

            foreach (var shape in shapes)
            {
                List<Shape> subOutput = Apply(shape);
                output.AddRange(subOutput);
            }

            return output;
        }
    }
}