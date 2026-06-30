using System;
using System.Collections.Generic;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class QuerySpatialDependencies : Operation
    {
        // The targeted channel/name identifier to search for across intersecting trees
        public string DependencyName { get; set; } = "DefaultDependency";

        public override List<Shape> Apply(Shape shape)
        {
            return ApplySet(new List<Shape> { shape });
        }

        public override List<Shape> ApplySet(List<Shape> shapes)
        {
            string treeId = SpatialGraphRegistry.CurrentTreeId;
            if (string.IsNullOrEmpty(treeId))
            {
                return new List<Shape>();
            }

            // Target queries specifically down the named pipeline channel
            return SpatialGraphRegistry.QueryIntersectingDependencies(treeId, DependencyName);
        }
    }
}