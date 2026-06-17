using System;
using System.Collections.Generic;
using System.Reflection;

namespace Gbe.ShapeGrammar
{
    public static class GrammarEngineUtility
    {
        /// <summary>
        /// Pulls values from completed upstream ComputedOutputs and dynamically injects them
        /// into the target fields/properties of the current step's operation right before execution.
        /// </summary>
        public static void ResolveValueBindings(IStep currentStep, Dictionary<string, IStep> allStepsLookup)
        {
            if (currentStep == null || currentStep.valueBindings == null || currentStep.valueBindings.Count == 0)
                return;

            Operation currentOp = currentStep.GetOperation();
            if (currentOp == null) return;

            Type currentOpType = currentOp.GetType();

            foreach (PropertyBinding binding in currentStep.valueBindings)
            {
                // 1. Locate the upstream source node that contains our data payload
                if (!allStepsLookup.TryGetValue(binding.sourceStepGuid, out IStep sourceStep))
                {
                    Console.WriteLine($"[GrammarEngine] Upstream source step {binding.sourceStepGuid} not found in execution cache.");
                    continue;
                }

                Operation sourceOp = sourceStep.GetOperation();
                if (sourceOp == null) continue;

                // 2. Fetch the calculated result from the upstream node's computed runtime dictionary
                float runtimeValue = 0f;
                if (sourceOp.ComputedOutputs.TryGetValue(binding.outputVariableName, out float foundValue))
                {
                    runtimeValue = foundValue;
                }
                else
                {
                    // Fallback: If it's a pure property node and wasn't manually written to ComputedOutputs, read it reflectively
                    PropertyInfo srcProp = sourceOp.GetType().GetProperty(binding.outputVariableName, BindingFlags.Public | BindingFlags.Instance);
                    if (srcProp != null)
                    {
                        runtimeValue = Convert.ToSingle(srcProp.GetValue(sourceOp));
                    }
                    else
                    {
                        Console.WriteLine($"[GrammarEngine] Output variable '{binding.outputVariableName}' could not be resolved on node {sourceStep.GetType().Name}.");
                        continue;
                    }
                }

                // 3. Find the parameter on our current operation that we need to overwrite
                PropertyInfo targetProp = currentOpType.GetProperty(binding.targetPropertyName, BindingFlags.Public | BindingFlags.Instance);
                FieldInfo targetField = currentOpType.GetField(binding.targetPropertyName, BindingFlags.Public | BindingFlags.Instance);

                Type destinationType = targetProp != null ? targetProp.PropertyType : (targetField != null ? targetField.FieldType : null);
                if (destinationType == null)
                {
                    Console.WriteLine($"[GrammarEngine] Target field/property '{binding.targetPropertyName}' does not exist on {currentOpType.Name}.");
                    continue;
                }

                // 4. Safely convert types (e.g. converting float metrics to int segments or boolean toggles)
                object convertedValue = ConvertScalarValue(runtimeValue, destinationType);

                // 5. Inject the data straight into the current runtime execution instance
                if (targetProp != null && targetProp.CanWrite)
                {
                    targetProp.SetValue(currentOp, convertedValue);
                }
                else if (targetField != null)
                {
                    targetField.SetValue(currentOp, convertedValue);
                }
            }
        }

        private static object ConvertScalarValue(float value, Type targetType)
        {
            if (targetType == typeof(float)) return value;
            if (targetType == typeof(int)) return (int)Math.Round(value);
            if (targetType == typeof(bool)) return value > 0.5f; // Threshold check mapping bit flags
            return Convert.ChangeType(value, targetType);
        }

        /// <summary>
        /// Gathers all evaluated shapes from the caches of all connected upstream nodes.
        /// If this is a root node (no inputs), it returns the initial seed geometry.
        /// </summary>
        public static List<Shape> GatherUpstreamShapes(IStep currentStep, List<Shape> seedShapes)
        {
            List<Shape> inputShapes = new List<Shape>();

            // 1. If this node has no incoming geometry connections, it acts as a generator/root node.
            // We feed it the initial canvas shape (e.g., compiled from your inputVertices).
            if (currentStep.before == null || currentStep.before.Count == 0)
            {
                if (seedShapes != null)
                {
                    inputShapes.AddRange(seedShapes);
                }
                return inputShapes;
            }

            // 2. Otherwise, look through every upstream step connected to this node
            foreach (IStep upstreamStep in currentStep.before)
            {
                if (upstreamStep == null || upstreamStep.branchCache == null)
                    continue;

                // Accumulate geometry output data from all active structural cache channels
                foreach (var cacheBranch in upstreamStep.branchCache)
                {
                    if (cacheBranch.Value != null)
                    {
                        inputShapes.AddRange(cacheBranch.Value);
                    }
                }
            }

            return inputShapes;
        }

        /// <summary>
        /// Stores the newly generated shapes into the step's local execution cache 
        /// so downstream nodes can collect them.
        /// </summary>
        public static void StoreDownstreamShapes(IStep currentStep, List<Shape> outputShapes, int branchIndex = 0)
        {
            if (currentStep == null) return;

            // Ensure the non-serialized runtime dictionary is instantiated
            if (currentStep.branchCache == null)
            {
                currentStep.branchCache = new Dictionary<int, List<Shape>>();
            }

            // Store the output shapes array into the specified port/branch channel
            currentStep.branchCache[branchIndex] = new List<Shape>(outputShapes);
        }
    }
}