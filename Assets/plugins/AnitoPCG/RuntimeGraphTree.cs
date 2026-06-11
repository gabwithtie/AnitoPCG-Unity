using Gbe.ShapeGrammar;
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