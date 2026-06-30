using System;
using System.Collections.Generic;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class PublishSpatialDependency : Operation
    {
        // Outlines the explicit channel/name for this dependency slot
        public string DependencyName { get; set; } = "DefaultDependency";

        public override List<Shape> Apply(Shape shape)
        {
            return new List<Shape> { shape };
        }

        public override List<Shape> ApplySet(List<Shape> shapes)
        {
            string treeId = SpatialGraphRegistry.CurrentTreeId;
            if (!string.IsNullOrEmpty(treeId) && shapes != null && shapes.Count > 0)
            {
                SpatialGraphRegistry.RegisterDependency(treeId, DependencyName, shapes);
            }
            return shapes; // Transparent pass-through
        }
    }
}