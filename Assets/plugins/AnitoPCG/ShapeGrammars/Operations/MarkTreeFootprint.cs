using System;
using System.Collections.Generic;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class MarkTreeFootprint : Operation
    {
        public override List<Shape> Apply(Shape shape)
        {
            return new List<Shape> { shape };
        }

        public override List<Shape> ApplySet(List<Shape> shapes)
        {
            string treeId = SpatialGraphRegistry.CurrentTreeId;
            if (!string.IsNullOrEmpty(treeId) && shapes != null && shapes.Count > 0)
            {
                SpatialGraphRegistry.RegisterFootprint(treeId, shapes);
            }
            return shapes; // Transparent pass-through
        }
    }
}