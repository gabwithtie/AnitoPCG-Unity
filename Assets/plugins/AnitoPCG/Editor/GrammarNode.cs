using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements; // CRITICAL: Gives access to FloatField, Vector3Field, etc.
using UnityEngine;
using UnityEngine.UIElements;

namespace Gbe.ShapeGrammar.Editor
{
    public class GrammarNode : Node
    {
        public string NodeId;
        public IStep RuntimeStep;
        public Port InputPort;
        public Port OutputPort;

        // Triggers whenever a user adjusts a value slider or checkbox in the node
        public Action OnParameterChanged;

        public GrammarNode(string titleName, IStep step)
        {
            title = titleName;
            RuntimeStep = step;
            NodeId = step.guid;

            // Expand width slightly to fit property fields comfortably
            style.width = StyleKeyword.Auto;
            style.minWidth = 240; // Sets a professional, clean default base layout width

            // 1. Create Input Port
            InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            InputPort.portName = "In";
            inputContainer.Add(InputPort);

            // 2. Setup Nodes Conditionally
            if (step.isMasterNode)
            {
                title = "Final Output";
                titleContainer.style.backgroundColor = new Color(0.15f, 0.4f, 0.25f);
                capabilities &= ~Capabilities.Deletable;
            }
            else
            {
                OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
                OutputPort.portName = "Out";
                outputContainer.Add(OutputPort);

                // SCALE LAYER: Automatically discover and draw variable fields
                GenerateDynamicParameterUI();
            }

            // Inside GrammarNode.cs constructor:
            // Draw a distinct value out port for any available computed metrics
            if (RuntimeStep.GetOperation() != null)
            {
                foreach (var key in RuntimeStep.GetOperation().ComputedOutputs.Keys)
                {
                    Port valueOutPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(float));
                    valueOutPort.portName = key;
                    // Style it a distinct color (like Yellow) so users don't mix it up with Shape geometry wires
                    valueOutPort.portColor = Color.yellow;
                    outputContainer.Add(valueOutPort);
                }
            }

            RefreshPorts();
            RefreshExpandedState();
        }

        private void GenerateDynamicParameterUI()
        {
            Operation operationalInstance = RuntimeStep.GetOperation();
            if (operationalInstance == null) return;

            Type opType = operationalInstance.GetType();

            // Apply distinct dark-panel styling to the node's built-in collapsible lower container
            extensionContainer.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 0.85f);
            extensionContainer.style.paddingLeft = 6;
            extensionContainer.style.paddingRight = 6;
            extensionContainer.style.paddingTop = 5;
            extensionContainer.style.paddingBottom = 5;

            // Phase A: Handle standard properties { get; set; } (e.g., RepeatAlongPath parameters)
            var properties = opType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                // Ensure the property has both a public getter and a public setter
                if (!prop.CanRead || !prop.CanWrite || prop.GetSetMethod() == null || !prop.GetSetMethod().IsPublic)
                    continue;

                VisualElement UIField = CreateFieldFromType(
                    prop.PropertyType,
                    ObjectNames.NicifyVariableName(prop.Name), // Auto-formats "StartPos" to "Start Pos"
                    () => prop.GetValue(operationalInstance),
                    (newVal) => prop.SetValue(operationalInstance, newVal)
                );

                if (UIField != null) extensionContainer.Add(UIField);
            }

            // Phase B: Handle raw public variables/fields (fallback support)
            var fields = opType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                VisualElement UIField = CreateFieldFromType(
                    field.FieldType,
                    ObjectNames.NicifyVariableName(field.Name),
                    () => field.GetValue(operationalInstance),
                    (newVal) => field.SetValue(operationalInstance, newVal)
                );

                if (UIField != null) extensionContainer.Add(UIField);
            }

            // Enforce open panel states on initial spawning
            expanded = true;
        }

        private VisualElement CreateFieldFromType(Type type, string label, Func<object> getter, Action<object> setter)
        {
            if (type == typeof(float))
            {
                var field = new FloatField(label) { value = (float)getter() };
                field.RegisterValueChangedCallback(evt => { setter(evt.newValue); OnParameterChanged?.Invoke(); });
                return field;
            }
            if (type == typeof(int))
            {
                var field = new IntegerField(label) { value = (int)getter() };
                field.RegisterValueChangedCallback(evt => { setter(evt.newValue); OnParameterChanged?.Invoke(); });
                return field;
            }
            if (type == typeof(bool))
            {
                var field = new Toggle(label) { value = (bool)getter() };
                field.RegisterValueChangedCallback(evt => { setter(evt.newValue); OnParameterChanged?.Invoke(); });
                return field;
            }
            if (type == typeof(string))
            {
                var field = new TextField(label) { value = (string)getter() };
                field.RegisterValueChangedCallback(evt => { setter(evt.newValue); OnParameterChanged?.Invoke(); });
                return field;
            }

            // BRIDGE SYSTEM: Handle System.Numerics.Vector3 translation natively
            if (type == typeof(System.Numerics.Vector3))
            {
                System.Numerics.Vector3 sysVec = (System.Numerics.Vector3)getter();
                UnityEngine.Vector3 unityVec = new UnityEngine.Vector3(sysVec.X, sysVec.Y, sysVec.Z);

                var field = new Vector3Field(label) { value = unityVec };
                field.RegisterValueChangedCallback(evt =>
                {
                    UnityEngine.Vector3 v = evt.newValue;
                    setter(new System.Numerics.Vector3(v.x, v.y, v.z));
                    OnParameterChanged?.Invoke();
                });
                return field;
            }
            if (type == typeof(UnityEngine.Vector3))
            {
                var field = new Vector3Field(label) { value = (UnityEngine.Vector3)getter() };
                field.RegisterValueChangedCallback(evt => { setter(evt.newValue); OnParameterChanged?.Invoke(); });
                return field;
            }

            return null; // Ignore unmapped or complex reference types gracefully
        }
    }
}