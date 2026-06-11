using UnityEngine;

namespace Gbe.ShapeGrammar
{
    public class SimpleTestTree : Gbe.ShapeGrammar.Tree
    {
        private IStep _rootStep;

        public void BuildHardcodedGraph()
        {
            // Step 1: Initial transformation node
            var step1 = new Step<ExtrudeEdges>();
            step1.forEach = false;
            step1.flattenInputs = false;

            // Step 2: Main branch sequence checkpoint node
            var step2 = new Step<ExtrudeEdges>();
            step2.isStageCheckpoint = true; // Marks a stage increment boundary

            // Link the graph topology (Step 2 executes AFTER Step 1 finishes)
            step2.before.Add(step1);

            // In our structural compiler, the bottom-most execution target acts as the root
            _rootStep = step2;

            Debug.Log("[SimpleTestTree] Graph compiled successfully. (Step1 -> Step2 [Checkpoint])");
        }

        public override IStep GetRootStep()
        {
            if (_rootStep == null)
            {
                BuildHardcodedGraph();
            }
            return _rootStep;
        }
    }
}