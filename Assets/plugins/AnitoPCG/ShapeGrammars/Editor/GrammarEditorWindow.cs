using UnityEditor;
using UnityEditor.Callbacks; // CRITICAL: Gives access to [OnOpenAsset]
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace Gbe.ShapeGrammar.Editor
{
    public class GrammarEditorWindow : EditorWindow
    {
        private GrammarGraphView _graphView;
        private GrammarGraphAsset _targetAsset;

        public static void RecordPreUserEdit()
        {
            var _targetAsset = EditorWindow.GetWindow<GrammarEditorWindow>()._targetAsset;
            Undo.RecordObject(_targetAsset, "User Change");
        }

        // This intercepts whenever any asset inside your Project window is double-clicked
        [OnOpenAsset]
        public static bool OnOpenAssetHandler(int instanceID, int line)
        {
            // Attempt to cast the clicked object to your custom Shape Grammar Asset type
            var asset = EditorUtility.EntityIdToObject(instanceID) as GrammarGraphAsset;
            if (asset != null)
            {
                var window = GetWindow<GrammarEditorWindow>();
                window.titleContent = new GUIContent($"Graph: {asset.name}");
                window.LoadTargetAsset(asset);
                return true; // Tells Unity we successfully handled opening this specific asset
            }
            return false; // Defer to standard behavior for other asset types (materials, textures, etc.)
        }

        private void OnEnable()
        {
            void ConstructGraphView()
            {
                GraphViewChange OnGraphChange(GraphViewChange change)
                {
                    if (change.edgesToCreate != null)
                    {
                        foreach (var edge in change.edgesToCreate)
                        {
                            if (edge.input.node is GrammarNode node)
                                EditorApplication.delayCall += () => node.RefreshFieldStates(); // Delay ensures 'connected' state registers
                        }
                    }
                    if (change.elementsToRemove != null)
                    {
                        foreach (var element in change.elementsToRemove)
                        {
                            if (element is Edge edge && edge.input.node is GrammarNode node)
                                EditorApplication.delayCall += () => node.RefreshFieldStates();
                        }
                    }
                    return change;
                }

                _graphView = new GrammarGraphView
                {
                    name = "Grammar Graph"
                };
                _graphView.StretchToParentSize();
                _graphView.graphViewChanged -= OnGraphChange;
                _graphView.graphViewChanged += OnGraphChange;
                rootVisualElement.Add(_graphView);
            }

            void GenerateToolbar()
            {
                var toolbar = new IMGUIContainer(() =>
                {
                    GUILayout.BeginHorizontal(EditorStyles.toolbar);

                    GUILayout.Space(10);
                    GUILayout.Label(_targetAsset != null ? $"Editing: {_targetAsset.name}" : "No Asset Loaded (Double-click a GrammarGraphAsset to edit)");

                    GUILayout.FlexibleSpace();
                    GUILayout.EndHorizontal();
                });

                rootVisualElement.Add(toolbar);
            }

            ConstructGraphView();
            GenerateToolbar();

            void OnKeyDown(KeyDownEvent evt)
            {
                if (evt.ctrlKey && evt.keyCode == KeyCode.S)
                {
                    SaveChangesToAsset(becauseOfChange: false);
                    evt.StopPropagation(); // Prevent event from bubbling
                }
            }

            rootVisualElement.RegisterCallback<KeyDownEvent>(OnKeyDown);

            // Handle assembly reloads gracefully if an asset was being edited
            if (_targetAsset != null)
            {
                LoadTargetAsset(_targetAsset);
            }

            Undo.undoRedoPerformed += OnUndoRedo;
        }
        private void OnUndoRedo()
        {
            // 1. If the asset was changed, we need to force a full UI rebuild
            if (_targetAsset != null)
            {
                // Clear the current view
                _graphView.DeleteElements(_graphView.graphElements.ToList());

                // Reload from the (now modified) asset
                LoadTargetAsset(_targetAsset);
            }
        }

        private void OnDisable()
        {
            if (_graphView != null)
            {
                rootVisualElement.Remove(_graphView);
            }

            Undo.undoRedoPerformed -= OnUndoRedo;
        }

        // Wipes the visual canvas and recreates node windows based on the Asset data
        public void LoadTargetAsset(GrammarGraphAsset asset)
        {
            _targetAsset = asset;

            if (_graphView == null) return;

            var elementsToDelete = new List<GraphElement>(_graphView.graphElements);
            foreach (var elem in elementsToDelete) _graphView.RemoveElement(elem);

            if (_targetAsset == null) return;

            // --- ENFORCE MASTER NODE EXISTENCE ---
            bool hasMaster = false;
            foreach (var step in _targetAsset.serializedSteps)
            {
                if (step.isMasterNode) hasMaster = true;
            }

            if (!hasMaster)
            {
                var masterStep = new Step<FinalOutputOperation>
                {
                    uiPosition = new Vector2(600, 300), // Default center-right placement
                    isMasterNode = true,
                    guid = System.Guid.NewGuid().ToString()
                };
                _targetAsset.serializedSteps.Add(masterStep);
                EditorUtility.SetDirty(_targetAsset);
                AssetDatabase.SaveAssets();
            }

            // Reconstruct nodes visually as before
            var spawnedNodes = new Dictionary<string, GrammarNode>();

            // --- INSIDE GrammarEditorWindow.cs -> LoadTargetAsset() ---

            // Phase 1: Re-instantiate node layout windows from serialized collection
            foreach (var step in _targetAsset.serializedSteps)
            {
                var nodeWindow = _graphView.CreateNodeWindow(step);

                // ADD THIS CALLBACK: Flags the asset modification register immediately on adjustments
                nodeWindow.OnParameterChanged = () =>
                {
                    if (_targetAsset != null)
                    {
                        // Set dirty tells Unity that data changed and needs to be rewritten on the next Save
                        EditorUtility.SetDirty(_targetAsset);
                    }
                };

                spawnedNodes[step.guid] = nodeWindow;
            }

            // Phase 2: Reconstruct exact port-to-port wire connections from asset database
            foreach (var edgeData in _targetAsset.serializedEdges)
            {
                if (spawnedNodes.TryGetValue(edgeData.sourceNodeGuid, out var sourceNode) &&
                    spawnedNodes.TryGetValue(edgeData.targetNodeGuid, out var targetNode))
                {
                    Port outPort = FindPortByName(sourceNode, edgeData.sourcePortName, Direction.Output);
                    Port inPort = FindPortByName(targetNode, edgeData.targetPortName, Direction.Input);

                    if (outPort != null && inPort != null)
                    {
                        var edge = outPort.ConnectTo(inPort);
                        _graphView.AddElement(edge);
                    }
                }
            }

            // Put this at the very end of LoadTargetAsset()
            foreach (var element in _graphView.graphElements)
            {
                if (element is GrammarNode node)
                {
                    node.RefreshFieldStates();
                }
            }
        }

        // Compiles the visual node positions/links and updates the ScriptableObject database on disk
        public static void SaveChangesToAsset(bool becauseOfChange = true)
        {
            var _targetAsset = EditorWindow.GetWindow<GrammarEditorWindow>()._targetAsset;
            var _graphView = EditorWindow.GetWindow<GrammarEditorWindow>()._graphView;

            if (_targetAsset == null) return;

            if (becauseOfChange)
            {
                // Enable CTR+Z Undo tracking compatibility
                Undo.RecordObject(_targetAsset, "Update Grammar Graph Layout");
            }

            _targetAsset.serializedSteps.Clear();
            _targetAsset.serializedEdges.Clear();

            // 1. Gather Nodes and Reset Connections
            foreach (var element in _graphView.graphElements)
            {
                if (element is GrammarNode node)
                {
                    node.RuntimeStep.guid = node.NodeId;
                    node.RuntimeStep.uiPosition = node.GetPosition().position;
                    node.RuntimeStep.beforeGuids.Clear();
                    node.RuntimeStep.valueBindings.Clear(); // CRITICAL: Reset data bindings!
                    _targetAsset.serializedSteps.Add(node.RuntimeStep);
                }
            }

            // 2. Gather Wires and Separate by Data Type
            foreach (var element in _graphView.graphElements)
            {
                if (element is Edge edge)
                {
                    var src = edge.output.node as GrammarNode;
                    var tgt = edge.input.node as GrammarNode;
                    if (src != null && tgt != null)
                    {
                        // Is this a structural execution flow link? (Bool / White wire)
                        if (edge.output.portType == typeof(bool) && edge.input.portType == typeof(bool))
                        {
                            tgt.RuntimeStep.beforeGuids.Add(src.NodeId);
                        }
                        // Is this a Value/Math parameter link? (Float/Int / Yellow wire)
                        else if (edge.output.portType == typeof(float) || edge.input.portType == typeof(float))
                        {
                            tgt.RuntimeStep.valueBindings.Add(new PropertyBinding
                            {
                                sourceStepGuid = src.NodeId,
                                outputVariableName = edge.output.portName,
                                targetPropertyName = edge.input.portName
                            });
                        }

                        // SAVE THE PRECISE PORT ROUTING DETAILS FOR UI REBUILDING
                        _targetAsset.serializedEdges.Add(new SerializableEdge
                        {
                            sourceNodeGuid = src.NodeId,
                            sourcePortName = edge.output.portName,
                            targetNodeGuid = tgt.NodeId,
                            targetPortName = edge.input.portName
                        });
                    }
                }
            }

            EditorUtility.SetDirty(_targetAsset);
            AssetDatabase.SaveAssets();

            Debug.Log($"<color=green><b>[Saved]</b></color> Successfully compiled layout states into file asset: {_targetAsset.name}");
        }

        private Port FindPortByName(GrammarNode node, string portName, Direction direction)
        {
            // Query recursively across all input/output containers for a matching portName
            var ports = node.Query<Port>().ToList();
            foreach (var port in ports)
            {
                if (port.direction == direction && port.portName == portName)
                {
                    return port;
                }
            }
            return null;
        }
    }
}