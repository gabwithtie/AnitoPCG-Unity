using System;
using System.Collections.Generic;

namespace Gbe.ShapeGrammar
{
    // =================================================================
    // 1. STATIC LITERAL VALUE NODE
    // =================================================================
    [Serializable]
    public class FloatValueNode : Operation
    {
        public float Value { get; set; } = 1.0f;

        public override List<string> GetOutputRegistry() => new List<string> { "Result" };

        public override List<Shape> Apply(Shape shape)
        {
            ComputedOutputs["Result"] = Value;
            return new List<Shape> { shape }; // Pass-through geometry
        }
    }

    // =================================================================
    // 2. ADDITION / SUBTRACTION NODE
    // =================================================================
    [Serializable]
    public class MathAdd : Operation
    {
        public float InputA { get; set; } = 0.0f;
        public float InputB { get; set; } = 0.0f;

        public override List<string> GetOutputRegistry() => new List<string> { "Result" };

        public override List<Shape> Apply(Shape shape)
        {
            ComputedOutputs["Result"] = InputA + InputB;
            return new List<Shape> { shape };
        }
    }

    // =================================================================
    // 3. MULTIPLICATION NODE
    // =================================================================
    [Serializable]
    public class MathMultiply : Operation
    {
        public float InputA { get; set; } = 1.0f;
        public float InputB { get; set; } = 1.0f;

        public override List<string> GetOutputRegistry() => new List<string> { "Result" };

        public override List<Shape> Apply(Shape shape)
        {
            ComputedOutputs["Result"] = InputA * InputB;
            return new List<Shape> { shape };
        }
    }

    // =================================================================
    // 4. DIVISION NODE
    // =================================================================
    [Serializable]
    public class MathDivide : Operation
    {
        public float InputA { get; set; } = 1.0f;
        public float InputB { get; set; } = 1.0f;

        public override List<string> GetOutputRegistry() => new List<string> { "Result" };

        public override List<Shape> Apply(Shape shape)
        {
            if (Math.Abs(InputB) < 0.00001f)
            {
                ComputedOutputs["Result"] = 0.0f; // Division-by-zero protection fallback
            }
            else
            {
                ComputedOutputs["Result"] = InputA / InputB;
            }
            return new List<Shape> { shape };
        }
    }

    // =================================================================
    // 5. MODULO NODE
    // =================================================================
    [Serializable]
    public class MathModulo : Operation
    {
        public float InputA { get; set; } = 1.0f;
        public float InputB { get; set; } = 1.0f;

        public override List<string> GetOutputRegistry() => new List<string> { "Result" };

        public override List<Shape> Apply(Shape shape)
        {
            if (Math.Abs(InputB) < 0.00001f)
            {
                ComputedOutputs["Result"] = 0.0f;
            }
            else
            {
                ComputedOutputs["Result"] = InputA % InputB;
            }
            return new List<Shape> { shape };
        }
    }

    // =================================================================
    // 6. FLOATING POINT ROUNDING / UTILITIES
    // =================================================================
    [Serializable]
    public class MathRound : Operation
    {
        public float InputValue { get; set; } = 0.0f;
        public override List<string> GetOutputRegistry() => new List<string> { "Result" };
        public override List<Shape> Apply(Shape shape)
        {
            ComputedOutputs["Result"] = (float)Math.Round(InputValue);
            return new List<Shape> { shape };
        }
    }

    [Serializable]
    public class MathCeil : Operation
    {
        public float InputValue { get; set; } = 0.0f;
        public override List<string> GetOutputRegistry() => new List<string> { "Result" };
        public override List<Shape> Apply(Shape shape)
        {
            ComputedOutputs["Result"] = (float)Math.Ceiling(InputValue);
            return new List<Shape> { shape };
        }
    }

    [Serializable]
    public class MathFloor : Operation
    {
        public float InputValue { get; set; } = 0.0f;
        public override List<string> GetOutputRegistry() => new List<string> { "Result" };
        public override List<Shape> Apply(Shape shape)
        {
            ComputedOutputs["Result"] = (float)Math.Floor(InputValue);
            return new List<Shape> { shape };
        }
    }
}