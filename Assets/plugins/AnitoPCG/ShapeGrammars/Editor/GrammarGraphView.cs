using Gbe.ShapeGrammar;
using Gbe.ShapeGrammar.Editor;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gbe.ShapeGrammar.Editor
{
    public class GrammarGraphView : GraphView
    {
        public GrammarGraphView()
        {
            // Add grid background and zooming/dragging capabilities
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            // Right-click grid to open a creation context menu
            RegisterCallback<ContextualMenuPopulateEvent>(PopulateContextMenu);
        }

        // Dictates which ports are allowed to connect to each other
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatiblePorts = new List<Port>();
            ports.ForEach((port) =>
            {
                if (startPort != port &&
                    startPort.node != port.node &&
                    startPort.direction != port.direction &&
                    startPort.portType == port.portType) // <-- ENFORCE MATCHING DATA TYPES
                {
                    compatiblePorts.Add(port);
                }
            });
            return compatiblePorts;
        }

        private void PopulateContextMenu(ContextualMenuPopulateEvent evt)
        {
            Vector2 mousePos = evt.localMousePosition;
            Vector2 graphMousePos = contentViewContainer.WorldToLocal(mousePos);

            // 1. Locate all assemblies to scan for your custom operations
            // Scanning AppDomain ensures we find operations regardless of which folder/assembly they sit in
            var operationTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly => assembly.GetTypes())
                .Where(type => type.IsClass
                    && !type.IsAbstract
                    && typeof(Operation).IsAssignableFrom(type)
                    && type.GetConstructor(Type.EmptyTypes) != null
                ); // Ensures it has a parameterless constructor

            // 2. Loop through every discovered concrete operation type
            foreach (Type opType in operationTypes)
            {
                // Clean up the name for the UI menu (e.g., "TranslateOperation" becomes "Translate")
                string menuName = opType.Name;
                if (menuName.EndsWith("Operation"))
                {
                    menuName = menuName.Substring(0, menuName.LastIndexOf("Operation"));
                }

                // 3. Append the action dynamically to the right-click dropdown menu
                evt.menu.AppendAction($"Add Operation/{menuName}", (action) =>
                {
                    // Crucial Step: Because Step<T> is generic, we construct the specific generic type at runtime
                    // This transforms typeof(Step<>) into typeof(Step<YourDiscoveredOperation>)
                    Type concreteStepType = typeof(Step<>).MakeGenericType(opType);

                    // Dynamically instantiate the newly created closed generic type
                    IStep mockStep = (IStep)Activator.CreateInstance(concreteStepType);
                    mockStep.uiPosition = graphMousePos;

                    // Pass the constructed step into your node drawing engine
                    GrammarEditorWindow.RecordPreUserEdit();
                    CreateNodeWindow(mockStep);
                    GrammarEditorWindow.SaveChangesToAsset();
                });
            }
        }

        public static string GetPrettyName(Type type)
        {
            if (!type.IsGenericType)
            {
                return type.Name;
            }

            // Split the name at the backtick to remove '1, '2, etc.
            string baseName = type.Name.Split('`')[0];

            // Get the friendly names of all generic arguments
            var genericArgs = type.GetGenericArguments().Select(t => GetPrettyName(t));

            return $"{baseName}<{string.Join(", ", genericArgs)}>";
        }

        public GrammarNode CreateNodeWindow(IStep step)
        {
            // Generates a visual node based on an existing saved runtime instance
            var node = new GrammarNode(GetPrettyName(step.GetType()), step);
            node.NodeId = step.guid;
            node.SetPosition(new Rect(step.uiPosition, Vector2.zero));
            AddElement(node);
            return node;
        }
    }
}