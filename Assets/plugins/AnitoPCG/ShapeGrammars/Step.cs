using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class PropertyBinding
    {
        public string sourceStepGuid;      // Which upstream node computed the value
        public string outputVariableName;  // e.g., "FinalSegmentCount"
        public string targetPropertyName;  // e.g., "MaxDistance" or "ShiftIndexCount"
    }

    [Serializable]
    public struct StepDependency
    {
        public string dependencyType;
        public int targetStage;
    }

    [Serializable]
    public struct OperationOverride
    {
        public string fieldName;
        public float value_single;
        public Vector3 value_3;
        public string value_string;
    }

    [Serializable]
    public abstract class IStep
    {
        public bool isMasterNode = false;

        // Persistence & UI Layout Metadata
        public string guid = Guid.NewGuid().ToString();
        public Vector2 uiPosition;
        public List<string> beforeGuids = new List<string>();

        // Execution Edges
        [NonSerialized] public List<IStep> before = new List<IStep>();

        public bool forEach = false;
        public bool flattenInputs = false;
        public bool isVolatile = false;
        public bool isStageCheckpoint = false;

        public List<PropertyBinding> valueBindings = new List<PropertyBinding>();
        public List<OperationOverride> overrides = new List<OperationOverride>();

        [NonSerialized] public Dictionary<int, List<Shape>> branchCache = new Dictionary<int, List<Shape>>();

        public abstract Operation GetOperation();

        public void HydrateSavedOverrides()
        {
            if (this.overrides == null || this.overrides.Count == 0)
                return;

            Operation operationalInstance = this.GetOperation();
            if (operationalInstance == null) return;

            Type opType = operationalInstance.GetType();

            foreach (var ovr in this.overrides)
            {
                PropertyInfo prop = opType.GetProperty(ovr.fieldName, BindingFlags.Public | BindingFlags.Instance);
                FieldInfo field = opType.GetField(ovr.fieldName, BindingFlags.Public | BindingFlags.Instance);

                Type targetType = prop != null ? prop.PropertyType : (field != null ? field.FieldType : null);
                if (targetType == null) continue;

                object calculatedValue = null;

                // 2. Safely extract control references by querying down the element hierarchy
                if (targetType == typeof(float))
                {
                    calculatedValue = ovr.value_single;
                }
                else if (targetType == typeof(int))
                {
                    int intVal = (int)ovr.value_single;
                    calculatedValue = intVal;
                }
                else if (targetType == typeof(bool))
                {
                    bool boolVal = ovr.value_single > 0.5f;
                    calculatedValue = boolVal;
                }
                else if (targetType == typeof(string))
                {
                    calculatedValue = ovr.value_string;
                }
                else if (targetType == typeof(System.Numerics.Vector3))
                {
                    calculatedValue = new System.Numerics.Vector3(ovr.value_3.x, ovr.value_3.y, ovr.value_3.z);
                }
                else if (targetType == typeof(UnityEngine.Vector3))
                {
                    calculatedValue = PlayHookyToUnityVector(ovr.value_3);
                }

                // 3. Re-inject restored value parameters into backend engine structures
                if (calculatedValue != null)
                {
                    if (prop != null) prop.SetValue(operationalInstance, calculatedValue);
                    else if (field != null) field.SetValue(operationalInstance, calculatedValue);
                }
            }
        }

        private UnityEngine.Vector3 PlayHookyToUnityVector(UnityEngine.Vector3 incoming) => incoming;
    }

    [Serializable]
    public class Step<T> : IStep where T : Operation, new()
    {
        [NonSerialized] public T operation = new T();
        public override Operation GetOperation() => operation;
        public Step(T customOperation) => operation = customOperation;
        public Step() { }
    }
}