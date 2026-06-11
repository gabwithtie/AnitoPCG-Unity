using System;
using System.Collections.Generic;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public abstract class Operation
    {
        public Dictionary<string, float> ComputedOutputs { get; } = new Dictionary<string, float>();
        public virtual List<string> GetOutputRegistry()
        {
            return new List<string>();
        }

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

        public virtual List<Shape> ApplySet(List<Shape> shapes, List<Shape> dependencies)
        {
            return ApplySet(shapes);
        }
    }
}