using System;
using System.Collections.Generic;
using UnityEngine;

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
        public List<StepDependency> dependencies = new List<StepDependency>();
        public List<OperationOverride> overrides = new List<OperationOverride>();

        [NonSerialized] public Dictionary<int, List<Shape>> branchCache = new Dictionary<int, List<Shape>>();

        public abstract Operation GetOperation();
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