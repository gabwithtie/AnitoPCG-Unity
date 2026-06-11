using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Gbe.ShapeGrammar.Editor
{
    public class SubstitutionGraphView : GraphView
    {
        public SubstitutionGraphView()
        {
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);
            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());

            var grid = new GridBackground();
            Insert(0, grid);
            grid.StretchToParentSize();

            RegisterCallback<ContextualMenuPopulateEvent>(PopulateContextMenu);
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            var compatiblePorts = new List<Port>();
            ports.ForEach((port) =>
            {
                if (startPort != port && startPort.node != port.node && startPort.direction != port.direction)
                {
                    compatiblePorts.Add(port);
                }
            });
            return compatiblePorts;
        }

        private void PopulateContextMenu(ContextualMenuPopulateEvent evt)
        {
            Vector2 graphMousePos = contentViewContainer.WorldToLocal(evt.localMousePosition);

            evt.menu.AppendAction("Add Condition/Boolean Branch", (action) => CreateNode(new BooleanBranchStep { uiTitle = "Boolean Condition Splitter", uiPosition = graphMousePos }));
            evt.menu.AppendAction("Add Condition/Index Router", (action) => CreateNode(new IndexBranchStep { uiTitle = "Index Sequence Router", uiPosition = graphMousePos }));
            evt.menu.AppendAction("Add Terminals/Prefab Vector Map", (action) => CreateNode(new PrefabVectorMapStep { uiTitle = "Prefab Array Substituted Entity", uiPosition = graphMousePos }));
        }

        public SubstitutionNode CreateNode(ISubStep step)
        {
            var node = new SubstitutionNode(step.uiTitle, step);
            node.SetPosition(new Rect(step.uiPosition, Vector2.zero));
            AddElement(node);
            return node;
        }
    }
}