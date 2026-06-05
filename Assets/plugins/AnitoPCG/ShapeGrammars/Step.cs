using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public struct StepDependency
    {
        public string dependencyType;
        public int targetStage;
    }

    [Serializable]
    public abstract class IStep
    {
        // [SerializeReference] allows Unity to natively serialize polymorphic graph edges
        [SerializeReference] public List<IStep> before = new List<IStep>();

        public bool forEach = false;
        public bool flattenInputs = false;
        public bool isVolatile = false;
        public bool isStageCheckpoint = false;

        public List<StepDependency> dependencies = new List<StepDependency>();
        public Dictionary<int, List<Shape>> branchCache = new Dictionary<int, List<Shape>>();

        public abstract Operation GetOperation();
    }

    public class Step<T> : IStep where T : Operation, new()
    {
        public T operation = new T();
        public override Operation GetOperation() => operation;
        public Step(T customOperation) => operation = customOperation;
        public Step() { }
    }
}