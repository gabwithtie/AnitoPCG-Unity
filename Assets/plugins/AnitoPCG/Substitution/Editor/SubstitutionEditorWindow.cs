using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;
using System.Linq;

namespace Gbe.ShapeGrammar.Editor
{
    public class SubstitutionEditorWindow : EditorWindow
    {
        private SubstitutionGraphView _graphView;
        private SubstitutionSchemeAsset _targetAsset;

        [MenuItem("Tools/Substitution Scheme Editor")]
        public static void OpenWindow()
        {
            var window = GetWindow<SubstitutionEditorWindow>();
            window.titleContent = new GUIContent("Substitution Engine");
        }

        [OnOpenAsset]
        public static bool OnOpenAssetHandler(int instanceID, int line)
        {
            var asset = EditorUtility.EntityIdToObject(instanceID) as SubstitutionSchemeAsset;
            if (asset != null)
            {
                var window = GetWindow<SubstitutionEditorWindow>();
                window.titleContent = new GUIContent($"Substitution: {asset.name}");
                window.LoadTargetAsset(asset);
                return true;
            }
            return false;
        }

        private void OnEnable()
        {
            _graphView = new SubstitutionGraphView { name = "Substitution Canvas View" };
            _graphView.StretchToParentSize();
            rootVisualElement.Add(_graphView);
            GenerateToolbar();

            if (_targetAsset != null) LoadTargetAsset(_targetAsset);
        }

        private void OnDisable() => rootVisualElement.Remove(_graphView);

        public void LoadTargetAsset(SubstitutionSchemeAsset asset)
        {
            _targetAsset = asset;
            if (_graphView == null || _targetAsset == null) return;

            var elementsToDelete = new List<GraphElement>(_graphView.graphElements);
            foreach (var elem in elementsToDelete) _graphView.RemoveElement(elem);

            // Enforce Master Entry Node Existence
            if (!_targetAsset.serializedSteps.Any(s => s.isMasterInput))
            {
                _targetAsset.serializedSteps.Add(new MasterInputStep { isMasterInput = true, uiTitle = "Master Input Stack", uiPosition = new Vector2(800, 300) });
                EditorUtility.SetDirty(_targetAsset);
                AssetDatabase.SaveAssets();
            }

            var nodeMap = new Dictionary<string, SubstitutionNode>();
            foreach (var step in _targetAsset.serializedSteps)
            {
                var uiNode = _graphView.CreateNode(step);
                nodeMap[step.guid] = uiNode;
            }

            // Draw right-to-left wiring channels
            foreach (var step in _targetAsset.serializedSteps)
            {
                if (nodeMap.TryGetValue(step.guid, out var currentNode) && !string.IsNullOrEmpty(step.sourceNodeGuid))
                {
                    if (nodeMap.TryGetValue(step.sourceNodeGuid, out var sourceNode))
                    {
                        // Look inside the left output ports collection container for matching target linkage
                        Port outputPort = sourceNode.inputContainer.Children().OfType<Port>().FirstOrDefault();

                        if (step is BooleanBranchStep || step is IndexBranchStep)
                        {
                            // If this node is a child target, look up which custom port ID inside the parent targeted it
                            foreach (var parentStep in _targetAsset.serializedSteps)
                            {
                                if (parentStep is BooleanBranchStep b && b.trueBranchGuid == step.guid) outputPort = sourceNode.inputContainer.Children().OfType<Port>().FirstOrDefault(p => p.viewDataKey == "true");
                                if (parentStep is BooleanBranchStep b2 && b2.falseBranchGuid == step.guid) outputPort = sourceNode.inputContainer.Children().OfType<Port>().FirstOrDefault(p => p.viewDataKey == "false");
                                if (parentStep is IndexBranchStep idx)
                                {
                                    int matchIdx = idx.outputBranchGuids.IndexOf(step.guid);
                                    if (matchIdx >= 0) outputPort = sourceNode.inputContainer.Children().OfType<Port>().FirstOrDefault(p => p.viewDataKey == $"idx_{matchIdx}");
                                }
                            }
                        }

                        if (outputPort != null && currentNode.InputPort != null)
                        {
                            var edge = outputPort.ConnectTo(currentNode.InputPort);
                            _graphView.AddElement(edge);
                        }
                    }
                }
            }
        }

        private void GenerateToolbar()
        {
            rootVisualElement.Add(new IMGUIContainer(() => {
                GUILayout.BeginHorizontal(EditorStyles.toolbar);
                if (GUILayout.Button("Save Substitution Graph Schema", EditorStyles.toolbarButton) && _targetAsset != null) SaveChangesToAsset();
                GUILayout.Label(_targetAsset != null ? $" Target Database: {_targetAsset.name}" : " No Active Target Configuration Matrix Asset Selected");
                GUILayout.EndHorizontal();
            }));
        }

        private void SaveChangesToAsset()
        {
            if (_targetAsset == null) return;

            // 1. Enable standard Ctrl+Z Undo tracking compatibility
            Undo.RecordObject(_targetAsset, "Update Substitution Hierarchy Asset States");

            // 2. Extract a snapshot list of the runtime steps currently on the canvas
            var activeSteps = new List<ISubStep>();
            foreach (var element in _graphView.graphElements)
            {
                if (element is SubstitutionNode uiNode && uiNode.RuntimeStep != null)
                {
                    var step = uiNode.RuntimeStep;

                    // Clear prior link bindings to calculate them clean from current wires
                    step.sourceNodeGuid = null;
                    if (step is BooleanBranchStep b) { b.trueBranchGuid = null; b.falseBranchGuid = null; }
                    if (step is IndexBranchStep idx) idx.outputBranchGuids = idx.targetIndices.Select(_ => string.Empty).ToList();

                    activeSteps.Add(step);
                }
            }

            // --- Inside SubstitutionEditorWindow.cs -> SaveChangesToAsset() ---
            // Locate the loop that reads your graph Edge wires, and add this check:

            foreach (var element in _graphView.graphElements)
            {
                if (element is Edge edge)
                {
                    var outPortNode = edge.output.node as SubstitutionNode; // Left side
                    var inPortNode = edge.input.node as SubstitutionNode;   // Right side

                    if (outPortNode != null && inPortNode != null)
                    {
                        inPortNode.RuntimeStep.sourceNodeGuid = outPortNode.NodeId;

                        var parentStep = outPortNode.RuntimeStep;

                        // --- ADD THIS EXPLICIT CHECK ---
                        if (parentStep is MasterInputStep master)
                        {
                            master.targetNodeGuid = inPortNode.NodeId;
                        }
                        else if (parentStep is BooleanBranchStep b)
                        {
                            if (edge.output.viewDataKey == "true") b.trueBranchGuid = inPortNode.NodeId;
                            if (edge.output.viewDataKey == "false") b.falseBranchGuid = inPortNode.NodeId;
                        }
                        else if (parentStep is IndexBranchStep idx)
                        {
                            string key = edge.output.viewDataKey;
                            if (key.StartsWith("idx_") && int.TryParse(key.Substring(4), out int listIndex))
                            {
                                if (listIndex >= 0 && listIndex < idx.outputBranchGuids.Count)
                                    idx.outputBranchGuids[listIndex] = inPortNode.NodeId;
                            }
                        }
                    }
                }
            }

            // 4. THE FIX: Clear and overwrite the container completely to force serialization
            _targetAsset.serializedSteps.Clear();
            _targetAsset.serializedSteps.AddRange(activeSteps);

            // 5. Hard flush modifications directly to your Project's Asset Database asset file
            EditorUtility.SetDirty(_targetAsset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh(); // Forces Unity to update the Project window view state

            Debug.Log("<color=cyan><b>[Saved]</b></color> Successfully compiled Right-To-Left Substitution Scheme configurations to disk.");
        }
    }
}