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

        [MenuItem("Tools/Shape Grammar Editor")]
        public static void OpenWindow()
        {
            var window = GetWindow<GrammarEditorWindow>();
            window.titleContent = new GUIContent("Grammar Graph");
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
            ConstructGraphView();
            GenerateToolbar();

            // Handle assembly reloads gracefully if an asset was being edited
            if (_targetAsset != null)
            {
                LoadTargetAsset(_targetAsset);
            }
        }

        private void OnDisable()
        {
            if (_graphView != null)
            {
                rootVisualElement.Remove(_graphView);
            }
        }

        private void ConstructGraphView()
        {
            _graphView = new GrammarGraphView
            {
                name = "Grammar Graph"
            };
            _graphView.StretchToParentSize();
            rootVisualElement.Add(_graphView);
        }

        private void GenerateToolbar()
        {
            var toolbar = new IMGUIContainer(() =>
            {
                GUILayout.BeginHorizontal(EditorStyles.toolbar);

                if (GUILayout.Button("Save Active Graph Asset", EditorStyles.toolbarButton))
                {
                    SaveChangesToAsset();
                }

                GUILayout.Space(10);
                GUILayout.Label(_targetAsset != null ? $"Editing: {_targetAsset.name}" : "No Asset Loaded (Double-click a GrammarGraphAsset to edit)");

                GUILayout.FlexibleSpace();
                GUILayout.EndHorizontal();
            });

            rootVisualElement.Add(toolbar);
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
        }

        // Compiles the visual node positions/links and updates the ScriptableObject database on disk
        private void SaveChangesToAsset()
        {
            if (_targetAsset == null) return;

            // Enable CTR+Z Undo tracking compatibility
            Undo.RecordObject(_targetAsset, "Update Grammar Graph Layout");

            _targetAsset.serializedSteps.Clear();

            // 1. Gather all active node transformations and structural states
            foreach (var element in _graphView.graphElements)
            {
                if (element is GrammarNode node)
                {
                    node.RuntimeStep.guid = node.NodeId;
                    node.RuntimeStep.uiPosition = node.GetPosition().position;
                    node.RuntimeStep.beforeGuids.Clear();
                    _targetAsset.serializedSteps.Add(node.RuntimeStep);
                }
            }

            // 2. Gather structural wire connections across all nodes
            _targetAsset.serializedEdges.Clear();

            foreach (var element in _graphView.graphElements)
            {
                if (element is Edge edge)
                {
                    var src = edge.output.node as GrammarNode;
                    var tgt = edge.input.node as GrammarNode;
                    if (src != null && tgt != null)
                    {
                        // Keep your topological flow ordering list
                        tgt.RuntimeStep.beforeGuids.Add(src.NodeId);

                        // SAVE THE PRECISE PORT ROUTING DETAILS
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

            // Flush modifications straight to asset database serialization
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