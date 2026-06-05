using UnityEngine;

namespace Gbe.ShapeGrammar
{
    public enum ParameterType { INT, FLOAT }

    public interface IParameter
    {
        string id { get; }
        ParameterType parameterType { get; }
        bool isDirty { get; set; }
    }

    public class Parameter<T> : IParameter
    {
        public string id { get; set; }
        public ParameterType parameterType { get; set; }
        public bool isDirty { get; set; }
        public T value;
        public void Set(T val) { value = val; isDirty = true; }
    }
}
