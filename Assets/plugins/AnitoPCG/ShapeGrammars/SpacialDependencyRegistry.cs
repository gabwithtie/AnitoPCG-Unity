using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Gbe.ShapeGrammar
{
    public class SpatialDependencyRegistry
    {
        public void RegisterTree(Tree tree, Shape initialShape) { }
        public List<Task<List<Shape>>> GetStageDependencies(Tree tree, string type, int stage) => new List<Task<List<Shape>>>();
    }
}