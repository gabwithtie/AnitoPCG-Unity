using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gbe.ShapeGrammar.Editor
{
    public class SubstitutionNode : Node
    {
        public string NodeId;
        public ISubStep RuntimeStep;
        public Port InputPort;  // Located on the RIGHT

        public SubstitutionNode(string titleName, ISubStep step)
        {
            title = titleName;
            RuntimeStep = step;
            NodeId = step.guid;
            style.width = StyleKeyword.Auto;
            style.minWidth = 240;

            // --- REVERSED PORTS CONFIGURATION ---
            if (step.isMasterInput)
            {
                title = "Master Input Stack";
                titleContainer.style.backgroundColor = new Color(0.15f, 0.3f, 0.4f); // Cyan scheme
                capabilities &= ~Capabilities.Deletable;

                // Master Input outputs data to the LEFT
                Port outPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
                outPort.portName = "Output Geometry Pipeline";
                inputContainer.Add(outPort); // Using left container for output display
            }
            else
            {
                // Normal nodes receive data from the RIGHT
                InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Single, typeof(bool));
                InputPort.portName = "From Matrix Engine";
                outputContainer.Add(InputPort); // Using right container for input display

                GenerateDynamicParameterUI();
            }

            RefreshPorts();
            RefreshExpandedState();
        }

        private void GenerateDynamicParameterUI()
        {
            extensionContainer.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f, 0.9f);
            extensionContainer.style.paddingLeft = 8;
            extensionContainer.style.paddingRight = 8;

            // Generate customized programmatic port mappings based on specific branching targets
            if (RuntimeStep is BooleanBranchStep boolStep)
            {
                var keyField = new TextField("Metadata Key") { value = boolStep.dataKey };
                keyField.RegisterValueChangedCallback(evt => boolStep.dataKey = evt.newValue);
                extensionContainer.Add(keyField);

                // Add explicit local branching output ports (visually on the left)
                AddCustomLeftOutputPort("True Path (1)", "true");
                AddCustomLeftOutputPort("False Path (0)", "false");
            }
            else if (RuntimeStep is IndexBranchStep idxStep)
            {
                var keyField = new TextField("Metadata Key") { value = idxStep.dataKey };
                keyField.RegisterValueChangedCallback(evt => idxStep.dataKey = evt.newValue);
                extensionContainer.Add(keyField);

                // Create a standard label for tracking array assignments
                var descLabel = new Label("Target Indices Map:");
                descLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                extensionContainer.Add(descLabel);

                for (int i = 0; i < idxStep.targetIndices.Count; i++)
                {
                    int localIdx = i;
                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;

                    var intField = new IntegerField($"Branch Slot [{localIdx}]") { value = idxStep.targetIndices[localIdx] };
                    intField.RegisterValueChangedCallback(evt => idxStep.targetIndices[localIdx] = evt.newValue);
                    row.Add(intField);

                    extensionContainer.Add(row);
                    AddCustomLeftOutputPort($"Index Match ({idxStep.targetIndices[localIdx]})", $"idx_{localIdx}");
                }
            }
            else if (RuntimeStep is PrefabVectorMapStep prefabStep)
            {
                var keyField = new TextField("Metadata Key") { value = prefabStep.dataKey };
                keyField.RegisterValueChangedCallback(evt => prefabStep.dataKey = evt.newValue);
                extensionContainer.Add(keyField);

                var clampToggle = new Toggle("Clamp Out Of Bounds") { value = prefabStep.clampIndexOutOfBounds };
                clampToggle.RegisterValueChangedCallback(evt => prefabStep.clampIndexOutOfBounds = evt.newValue);
                extensionContainer.Add(clampToggle);

                // --- ADD THE DYNAMIC FALLBACK UI ELEMENT ---
                var fallbackField = new ObjectField("Fallback Prefab")
                {
                    objectType = typeof(GameObject),
                    value = prefabStep.fallbackPrefab
                };
                fallbackField.RegisterValueChangedCallback(evt => prefabStep.fallbackPrefab = evt.newValue as GameObject);
                // Give it a subtle visual distinction so the user knows it's the global safety container
                fallbackField.style.marginTop = 4;
                fallbackField.style.marginBottom = 8;
                extensionContainer.Add(fallbackField);

                // Drawer UI for tracking project GameObject elements assigned to the node asset layout
                var header = new Label("Prefab Slots Vector:");
                header.style.unityFontStyleAndWeight = FontStyle.Bold;
                extensionContainer.Add(header);

                var objectListContainer = new VisualElement();
                RebuildPrefabListUI(objectListContainer, prefabStep);
                extensionContainer.Add(objectListContainer);

                var addBtn = new Button(() => {
                    prefabStep.prefabVector.Add(null);
                    RebuildPrefabListUI(objectListContainer, prefabStep);
                })
                { text = "Add New Prefab Slot Element" };
                extensionContainer.Add(addBtn);
            }

            expanded = true;
        }

        private void RebuildPrefabListUI(VisualElement container, PrefabVectorMapStep step)
        {
            container.Clear();
            for (int i = 0; i < step.prefabVector.Count; i++)
            {
                int index = i;
                var row = new VisualElement();
                row.style.flexDirection = FlexDirection.Row;

                var objField = new ObjectField($"Element [{index}]")
                {
                    objectType = typeof(GameObject),
                    value = step.prefabVector[index]
                };
                objField.RegisterValueChangedCallback(evt => step.prefabVector[index] = evt.newValue as GameObject);
                objField.style.flexGrow = 1f;
                row.Add(objField);

                var remBtn = new Button(() => {
                    step.prefabVector.RemoveAt(index);
                    RebuildPrefabListUI(container, step);
                })
                { text = "X" };
                row.Add(remBtn);

                container.Add(row);
            }
        }

        private void AddCustomLeftOutputPort(string name, string localId)
        {
            Port outputPort = InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
            outputPort.portName = name;
            outputPort.viewDataKey = localId; // Retain a safe reference tag key identification mapping entry
            inputContainer.Add(outputPort); // Placed on the LEFT container side
        }
    }
}