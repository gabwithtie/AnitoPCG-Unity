using System.Collections.Generic;
using UnityEngine;

namespace Gbe.ShapeGrammar
{
    [CreateAssetMenu(fileName = "NewGrammarGraph", menuName = "Shape Grammar/Graph Asset")]
    public class GrammarGraphAsset : ScriptableObject
    {
        // [SerializeReference] preserves distinct polymorphism profiles across different operation steps
        [SerializeReference]
        public List<IStep> serializedSteps = new List<IStep>();
    }
}