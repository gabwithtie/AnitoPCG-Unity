using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class TreeParameters
    {
        public Dictionary<string, IParameter> parameters = new Dictionary<string, IParameter>();

        public void SetFromVector(List<IParameter> paramsList)
        {
            foreach (IParameter source in paramsList)
            {
                if (parameters.TryGetValue(source.id, out IParameter target))
                {
                    if (target.parameterType == source.parameterType)
                    {
                        // Clean C# pattern matching instead of explicit switch/casting blocks
                        switch (source.parameterType)
                        {
                            case ParameterType.INT:
                                if (source is Parameter<int> srcInt && target is Parameter<int> tgtInt)
                                    tgtInt.Set(srcInt.value);
                                break;
                            case ParameterType.FLOAT:
                                if (source is Parameter<float> srcFloat && target is Parameter<float> tgtFloat)
                                    tgtFloat.Set(srcFloat.value);
                                break;
                        }
                    }
                    else
                    {
                        Debug.LogError($"Parameter type mismatch for parameter: {source.id}");
                    }
                }
                else
                {
                    Debug.LogError($"Parameter not found: {source.id}");
                }
            }
        }
    }

    public class ApplyParams
    {
        public int targetStage = -1;
        public int currentStage = 0;
        public List<Shape> outStageShapes = null;
        public Shape initial;
        public Tree owningTree = null;
        public TreeParameters parametersCaptured;
        public bool isMainThreadDryRun = false;
    }

    public abstract class Tree
    {
        List<Shape> evaluationCache;
        Action<List<Shape>> OnEvaluate;

        public abstract IStep GetRootStep();
        public List<Shape> Evaluate(List<System.Numerics.Vector3> seedVertices, bool finalPass = true)
        {
            evaluationCache = EvaluateImplementation(seedVertices, finalPass);

            if(OnEvaluate != null) OnEvaluate.Invoke(evaluationCache);

            return evaluationCache;
        }

        public void SetOnEvaluate(Action<List<Shape>> _func)
        {
            OnEvaluate = _func;
        }

        protected abstract List<Shape> EvaluateImplementation(List<System.Numerics.Vector3> seedVertices, bool finalPass = true);
    }
}