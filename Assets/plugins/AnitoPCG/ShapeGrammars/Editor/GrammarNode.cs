using System;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
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

        public Action OnParameterChanged;

        public GrammarNode(string titleName, IStep step)
        {
            title = titleName;
            RuntimeStep = step;
            NodeId = step.guid;

            style.width = StyleKeyword.Auto;
            style.minWidth = 240;

            Operation operationalInstance = RuntimeStep.GetOperation();
            bool isPureMathNode = operationalInstance != null && operationalInstance.GetType().Namespace.Contains("Gbe.ShapeGrammar") && operationalInstance.GetType().Name.StartsWith("Math") || operationalInstance is FloatValueNode;

            // 1. Create standard geometry Input Port
            InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            InputPort.portName = "In";

            if (isPureMathNode)
            {
                // Style adjustments for standalone math logic nodes
                InputPort.portName = "Flow Link";
                InputPort.portColor = new Color(0.4f, 0.4f, 0.4f); // Neutral dark grey flow line
            }
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

                if (isPureMathNode)
                {
                    OutputPort.portName = "Flow Out";
                    OutputPort.portColor = new Color(0.4f, 0.4f, 0.4f);
                }
                outputContainer.Add(OutputPort);

                GenerateDynamicParameterUI();
            }

            // Give math nodes a distinct purple header tint in the layout canvas window
            if (isPureMathNode)
            {
                titleContainer.style.backgroundColor = new Color(0.32f, 0.2f, 0.45f);
            }

            // --- COMPILE-TIME OUTPUT PORT GENERATION ---
            if (operationalInstance != null)
            {
                List<string> registeredOutputs = operationalInstance.GetOutputRegistry();
                if (registeredOutputs != null)
                {
                    foreach (string outputKey in registeredOutputs)
                    {
                        if (string.IsNullOrEmpty(outputKey)) continue;

                        Port valueOutPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(float));
                        valueOutPort.portName = outputKey;
                        valueOutPort.portColor = Color.yellow; // Yellow connection links
                        outputContainer.Add(valueOutPort);
                    }
                }
            }

            RefreshPorts();
            RefreshExpandedState();
        }

        public void RefreshFieldStates()
        {
            // Find all input ports on this node
            var inputPorts = this.Query<Port>().ToList();

            foreach (var port in inputPorts)
            {
                // Check if it's one of our custom yellow scalar value ports
                if (port.direction == Direction.Input && (port.portType == typeof(float) || port.portType == typeof(int)))
                {
                    var customRow = port.parent;
                    if (customRow != null)
                    {
                        // Find the slider/input field next to the port
                        var fieldControl = customRow.Q<FloatField>() as VisualElement ?? customRow.Q<IntegerField>() as VisualElement;
                        if (fieldControl != null)
                        {
                            bool isDriven = port.connected;

                            // Disable the UI element if a wire is connected
                            fieldControl.SetEnabled(!isDriven);

                            // Highlight the label yellow to show it is currently being driven by an upstream node
                            var label = customRow.Q<Label>();
                            if (label != null)
                            {
                                label.style.color = isDriven ? Color.yellow : new StyleColor(StyleKeyword.Null);
                            }
                        }
                    }
                }
            }
        }

        private void GenerateDynamicParameterUI()
        {
            Operation operationalInstance = RuntimeStep.GetOperation();
            if (operationalInstance == null) return;

            Type opType = operationalInstance.GetType();

            extensionContainer.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 0.85f);
            extensionContainer.style.paddingLeft = 6;
            extensionContainer.style.paddingRight = 6;
            extensionContainer.style.paddingTop = 5;
            extensionContainer.style.paddingBottom = 5;

            // Generate fields for properties
            var properties = opType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (var prop in properties)
            {
                if (!prop.CanRead || !prop.CanWrite || prop.GetSetMethod() == null || !prop.GetSetMethod().IsPublic)
                    continue;

                if (prop.Name == "ComputedOutputs") continue;

                VisualElement UIField = CreateFieldFromType(
                    prop.PropertyType,
                    prop.Name,
                    ObjectNames.NicifyVariableName(prop.Name),
                    () => prop.GetValue(operationalInstance),
                    (newVal) => prop.SetValue(operationalInstance, newVal)
                );

                if (UIField != null) extensionContainer.Add(UIField);
            }

            // Generate fields for raw public variables
            var fields = opType.GetFields(BindingFlags.Public | BindingFlags.Instance);
            foreach (var field in fields)
            {
                if (field.Name == "ComputedOutputs") continue;

                VisualElement UIField = CreateFieldFromType(
                    field.FieldType,
                    field.Name,
                    ObjectNames.NicifyVariableName(field.Name),
                    () => field.GetValue(operationalInstance),
                    (newVal) => field.SetValue(operationalInstance, newVal)
                );

                if (UIField != null) extensionContainer.Add(UIField);
            }

            expanded = true;
            HydrateSavedOverrides(); // Restore overrides from serialization safely
        }

        private VisualElement CreateFieldFromType(Type type, string rawFieldName, string label, Func<object> getter, Action<object> setter)
        {
            VisualElement fieldElement = null;
            VisualElement coreInputControl = null;

            // Instantiate corresponding GraphView controls
            if (type == typeof(float))
            {
                var field = new FloatField(label) { value = (float)getter() };
                field.RegisterValueChangedCallback(evt => { setter(evt.newValue); ApplyOverrideData(rawFieldName, singleValue: evt.newValue); OnParameterChanged?.Invoke(); });
                coreInputControl = field;
                fieldElement = field;
            }
            else if (type == typeof(int))
            {
                var field = new IntegerField(label) { value = (int)getter() };
                field.RegisterValueChangedCallback(evt => { setter(evt.newValue); ApplyOverrideData(rawFieldName, singleValue: (float)evt.newValue); OnParameterChanged?.Invoke(); });
                coreInputControl = field;
                fieldElement = field;
            }
            else if (type == typeof(bool))
            {
                var field = new Toggle(label) { value = (bool)getter() };
                field.RegisterValueChangedCallback(evt => { setter(evt.newValue); ApplyOverrideData(rawFieldName, singleValue: evt.newValue ? 1f : 0f); OnParameterChanged?.Invoke(); });
                coreInputControl = field;
                fieldElement = field;
            }
            else if (type == typeof(string))
            {
                var field = new TextField(label) { value = (string)getter() };
                field.RegisterValueChangedCallback(evt => { setter(evt.newValue); ApplyOverrideData(rawFieldName, stringValue: evt.newValue); OnParameterChanged?.Invoke(); });
                coreInputControl = field;
                fieldElement = field;
            }
            else if (type == typeof(System.Numerics.Vector3))
            {
                System.Numerics.Vector3 sysVec = (System.Numerics.Vector3)getter();
                UnityEngine.Vector3 unityVec = new UnityEngine.Vector3(sysVec.X, sysVec.Y, sysVec.Z);

                var field = new Vector3Field(label) { value = unityVec };
                field.RegisterValueChangedCallback(evt =>
                {
                    UnityEngine.Vector3 v = evt.newValue;
                    setter(new System.Numerics.Vector3(v.x, v.y, v.z));
                    ApplyOverrideData(rawFieldName, vectorValue: v);
                    OnParameterChanged?.Invoke();
                });
                coreInputControl = field;
                fieldElement = field;
            }
            else if (type == typeof(UnityEngine.Vector3))
            {
                var field = new Vector3Field(label) { value = (UnityEngine.Vector3)getter() };
                field.RegisterValueChangedCallback(evt => { setter(evt.newValue); ApplyOverrideData(rawFieldName, vectorValue: evt.newValue); OnParameterChanged?.Invoke(); });
                coreInputControl = field;
                fieldElement = field;
            }

            if (fieldElement != null)
            {
                fieldElement.name = rawFieldName;

                // --- ADD VALUE INPUT PORTS FOR SCALAR VARIABLES ---
                // If the field type is a standard float or integer, we nest an input value port 
                // on its immediate left side so the property can be dynamically driven by an output wire.
                if (type == typeof(float) || type == typeof(int))
                {
                    var customRow = new VisualElement();
                    customRow.style.flexDirection = FlexDirection.Row;
                    customRow.style.alignItems = Align.Center;
                    customRow.name = rawFieldName; // Set name on container so Hydrate can find it easily

                    Port valueInPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(float));
                    valueInPort.portName = rawFieldName;
                    valueInPort.portColor = Color.yellow;
                    valueInPort.style.marginRight = 2;

                    // Remove explicit label from control to lay out flat next to our port nicely
                    if (coreInputControl is TextInputBaseField<float> tibf) tibf.label = string.Empty;
                    if (coreInputControl is TextInputBaseField<int> tibi) tibi.label = string.Empty;

                    // Create a separate label text mesh element
                    var sideLabel = new Label(label);
                    sideLabel.style.width = 100;
                    sideLabel.style.unityTextAlign = TextAnchor.MiddleLeft;

                    customRow.Add(valueInPort);
                    customRow.Add(sideLabel);

                    coreInputControl.style.flexGrow = 1f;
                    customRow.Add(coreInputControl);

                    return customRow;
                }
            }

            return fieldElement;
        }

        private void ApplyOverrideData(string fieldName, float singleValue = 0f, UnityEngine.Vector3 vectorValue = default, string stringValue = "")
        {
            if (RuntimeStep == null) return;

            RuntimeStep.overrides.RemoveAll(o => o.fieldName == fieldName);

            OperationOverride newOverride = new OperationOverride
            {
                fieldName = fieldName,
                value_single = singleValue,
                value_3 = vectorValue,
                value_string = stringValue
            };

            RuntimeStep.overrides.Add(newOverride);
        }

        private void HydrateSavedOverrides()
        {
            if (RuntimeStep == null || RuntimeStep.overrides == null || RuntimeStep.overrides.Count == 0)
                return;

            Operation operationalInstance = RuntimeStep.GetOperation();
            if (operationalInstance == null) return;

            Type opType = operationalInstance.GetType();

            foreach (var ovr in RuntimeStep.overrides)
            {
                // 1. Find the parent row container or raw field element by name
                VisualElement uiField = extensionContainer.Q(ovr.fieldName);
                if (uiField == null) continue;

                PropertyInfo prop = opType.GetProperty(ovr.fieldName, BindingFlags.Public | BindingFlags.Instance);
                FieldInfo field = opType.GetField(ovr.fieldName, BindingFlags.Public | BindingFlags.Instance);

                Type targetType = prop != null ? prop.PropertyType : (field != null ? field.FieldType : null);
                if (targetType == null) continue;

                object calculatedValue = null;

                // 2. Safely extract control references by querying down the element hierarchy
                if (targetType == typeof(float))
                {
                    calculatedValue = ovr.value_single;
                    // THE FIX: Query recursively (.Q) so it finds the FloatField even inside a custom row wrapper
                    var targetControl = uiField is FloatField ? uiField as FloatField : uiField.Q<FloatField>();
                    if (targetControl != null) targetControl.SetValueWithoutNotify(ovr.value_single);
                }
                else if (targetType == typeof(int))
                {
                    int intVal = (int)ovr.value_single;
                    calculatedValue = intVal;
                    var targetControl = uiField is IntegerField ? uiField as IntegerField : uiField.Q<IntegerField>();
                    if (targetControl != null) targetControl.SetValueWithoutNotify(intVal);
                }
                else if (targetType == typeof(bool))
                {
                    bool boolVal = ovr.value_single > 0.5f;
                    calculatedValue = boolVal;
                    var targetControl = uiField is Toggle ? uiField as Toggle : uiField.Q<Toggle>();
                    if (targetControl != null) targetControl.SetValueWithoutNotify(boolVal);
                }
                else if (targetType == typeof(string))
                {
                    calculatedValue = ovr.value_string;
                    var targetControl = uiField is TextField ? uiField as TextField : uiField.Q<TextField>();
                    if (targetControl != null) targetControl.SetValueWithoutNotify(ovr.value_string);
                }
                else if (targetType == typeof(System.Numerics.Vector3))
                {
                    calculatedValue = new System.Numerics.Vector3(ovr.value_3.x, ovr.value_3.y, ovr.value_3.z);
                    var targetControl = uiField is Vector3Field ? uiField as Vector3Field : uiField.Q<Vector3Field>();
                    if (targetControl != null) targetControl.SetValueWithoutNotify(ovr.value_3);
                }
                else if (targetType == typeof(UnityEngine.Vector3))
                {
                    calculatedValue = PlayHookyToUnityVector(ovr.value_3);
                    var targetControl = uiField is Vector3Field ? uiField as Vector3Field : uiField.Q<Vector3Field>();
                    if (targetControl != null) targetControl.SetValueWithoutNotify(ovr.value_3);
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
}