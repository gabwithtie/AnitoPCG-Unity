using Gbe.ShapeGrammar;
using System;
using System.Collections.Generic;

public class RuntimeGraphTree : Tree
{
    private IStep _resolvedRootStep;

    public void InitializeFromAsset(GrammarGraphAsset asset)
    {
        if (asset == null || asset.serializedSteps.Count == 0) return;

        var stepLookup = new Dictionary<string, IStep>();
        foreach (var step in asset.serializedSteps)
        {
            step.before.Clear();
            step.branchCache = new Dictionary<int, List<Shape>>();
            stepLookup[step.guid] = step;
        }

        foreach (var step in asset.serializedSteps)
        {
            foreach (var parentGuid in step.beforeGuids)
            {
                if (stepLookup.TryGetValue(parentGuid, out IStep parentStep))
                {
                    step.before.Add(parentStep);
                }
            }

            // 1. RESOLVE DRIVEN PROPERTIES (Reflection Injection)
            foreach (var binding in step.valueBindings)
            {
                // Locate the upstream step matching the GUID
                if (!stepLookup.TryGetValue(binding.sourceStepGuid, out IStep upstreamStep))
                {
                    continue;
                }

                Operation upstreamOp = upstreamStep.GetOperation();
                if (upstreamOp != null && upstreamOp.ComputedOutputs.TryGetValue(binding.outputVariableName, out float computedValue))
                {
                    // Use C# Reflection to inject the value straight into the target property
                    var targetProp = step.GetOperation().GetType().GetProperty(binding.targetPropertyName);
                    if (targetProp != null && targetProp.CanWrite)
                    {
                        // Safely convert types (e.g., float to int if target is an Integer)
                        object convertedValue = Convert.ChangeType(computedValue, targetProp.PropertyType);
                        targetProp.SetValue(step.GetOperation(), convertedValue);
                    }
                }
            }
        }

        // --- EXPLICIT LOOKUP ---
        // No more guessing or checking dangling nodes!
        _resolvedRootStep = asset.serializedSteps.Find(s => s.isMasterNode);

        // Fallback safety net if an older asset doesn't have one
        if (_resolvedRootStep == null)
        {
            _resolvedRootStep = asset.serializedSteps[asset.serializedSteps.Count - 1];
        }
    }

    public override IStep GetRootStep() => _resolvedRootStep;
}