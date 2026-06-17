using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public abstract class ISubStep
    {
        public string guid = Guid.NewGuid().ToString();
        public string uiTitle = "Substitution Node";
        public Vector2 uiPosition;
        public bool isMasterInput = false;

        // Change: Nodes now track the GUIDs of nodes connected to their output ports
        public List<string> outputBranchGuids = new List<string>();

        // Existing input connection
        public string sourceNodeGuid;

        public abstract Dictionary<string, List<Shape>> Evaluate(List<Shape> incomingShapes);
    }

    [Serializable]
    public class MasterInputStep : ISubStep
    {
        // Stores the GUID of whatever node is plugged into the Master Input's left port
        public string targetNodeGuid;

        public override Dictionary<string, List<Shape>> Evaluate(List<Shape> incomingShapes)
        {
            var output = new Dictionary<string, List<Shape>>();
            if (!string.IsNullOrEmpty(targetNodeGuid))
            {
                output[targetNodeGuid] = incomingShapes; // Route raw geometry directly down the wire
            }
            return output;
        }
    }

    // 2. BRANCH BASED ON 0-OR-1 (BOOLEAN/FLAG CONDITIONAL)
    [Serializable]
    public class BooleanBranchStep : ISubStep
    {
        public string dataKey = "DropFirst"; // Metadata index target
        public string trueBranchGuid;
        public string falseBranchGuid;

        public override Dictionary<string, List<Shape>> Evaluate(List<Shape> incomingShapes)
        {
            var results = new Dictionary<string, List<Shape>>();
            var trueList = new List<Shape>();
            var falseList = new List<Shape>();

            foreach (var shape in incomingShapes)
            {
                // Accessing metadata dictionaries inherited from base Shape properties
                if (shape.Data.TryGetValue(dataKey, out var values) && values.Count > 0 && values[0] != 0)
                {
                    trueList.Add(shape);
                }
                else
                {
                    falseList.Add(shape);
                }
            }

            if (!string.IsNullOrEmpty(trueBranchGuid)) results[trueBranchGuid] = trueList;
            if (!string.IsNullOrEmpty(falseBranchGuid)) results[falseBranchGuid] = falseList;
            return results;
        }
    }

    // 3. BRANCH BASED ON AN EXACT INDEX (0...X MATCHING)
    [Serializable]
    public class IndexBranchStep : ISubStep
    {
        public string dataKey = "i"; // Target key matching your RepeatAlongPath indices
        public List<int> targetIndices = new List<int> { 0, 1, 2 };
        public List<string> outputBranchGuids = new List<string>(); // Matches size of targetIndices

        public override Dictionary<string, List<Shape>> Evaluate(List<Shape> incomingShapes)
        {
            var results = new Dictionary<string, List<Shape>>();

            foreach (var shape in incomingShapes)
            {
                if (shape.Data.TryGetValue(dataKey, out var values) && values.Count > 0)
                {
                    int currentIdx = values[0];
                    int listPosition = targetIndices.IndexOf(currentIdx);

                    if (listPosition >= 0 && listPosition < outputBranchGuids.Count)
                    {
                        string targetGuid = outputBranchGuids[listPosition];
                        if (!results.ContainsKey(targetGuid)) results[targetGuid] = new List<Shape>();
                        results[targetGuid].Add(shape);
                    }
                }
            }
            return results;
        }
    }

    // 4. MAP AN INDEX ARRAY DIRECTLY TO A PREFAB VECTOR
    [Serializable]
    public class PrefabVectorMapStep : ISubStep
    {
        public string dataKey = "i";
        public List<GameObject> prefabVector = new List<GameObject>();
        public GameObject fallbackPrefab = null;
        [Tooltip("What to do if index is larger than the prefab vector size?")]
        public bool clampIndexOutOfBounds = true;

        // Associated instantiations runtime map output
        public Dictionary<GameObject, List<Shape>> CompilePrefabAssignments(List<Shape> incomingShapes)
        {
            var assignments = new Dictionary<GameObject, List<Shape>>();
            if (prefabVector.Count == 0) return assignments;

            foreach (var shape in incomingShapes)
            {
                GameObject prefabToAssign = fallbackPrefab;

                if (shape.Data.TryGetValue(dataKey, out var values) && values.Count > 0)
                {
                    int index = values[0];

                    if (index < 0) index = 0;
                    if (index >= prefabVector.Count)
                    {
                        index = clampIndexOutOfBounds ? prefabVector.Count - 1 : index % prefabVector.Count;
                    }

                    prefabToAssign = prefabVector[index];
                }

                if (!assignments.ContainsKey(prefabToAssign)) assignments[prefabToAssign] = new List<Shape>();
                assignments[prefabToAssign].Add(shape);

            }
            return assignments;
        }

        public override Dictionary<string, List<Shape>> Evaluate(List<Shape> incomingShapes)
        {
            return new Dictionary<string, List<Shape>>(); // Terminal Node: End of internal flow execution.
        }
    }
}