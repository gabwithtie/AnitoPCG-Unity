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
            _graphView.Clear();

            // 1. Instantiate all nodes first
            foreach (var step in asset.serializedSteps)
            {
                _graphView.CreateNode(step);
            }

            // 2. Rebuild edges from the serialized map
            foreach (var edgeData in asset.serializedEdges)
            {
                var src = _graphView.nodes.ToList().Cast<SubstitutionNode>().FirstOrDefault(n => n.NodeId == edgeData.sourceNodeGuid);
                var tgt = _graphView.nodes.ToList().Cast<SubstitutionNode>().FirstOrDefault(n => n.NodeId == edgeData.targetNodeGuid);

                if (src != null && tgt != null)
                {
                    // Use FindPortByName to get the correct visual port
                    var outPort = src.Query<Port>(name: edgeData.sourcePortName).First();
                    var inPort = tgt.Query<Port>(name: edgeData.targetPortName).First();

                    _graphView.AddElement(outPort.ConnectTo(inPort));
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

            // Inside your Save method in SubstitutionEditorWindow:
            _targetAsset.serializedEdges.Clear();

            foreach (var edge in _graphView.edges.ToList())
            {
                var outputNode = edge.output.node as SubstitutionNode;
                var inputNode = edge.input.node as SubstitutionNode;

                if (outputNode != null && inputNode != null)
                {
                    _targetAsset.serializedEdges.Add(new SubstitutionEdge
                    {
                        sourceNodeGuid = outputNode.NodeId,
                        sourcePortName = edge.output.portName,
                        targetNodeGuid = inputNode.NodeId,
                        targetPortName = edge.input.portName
                    });
                }
            }

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