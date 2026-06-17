using System.Collections.Generic;
using UnityEngine;

namespace Gbe.ShapeGrammar
{
    [System.Serializable]
    public class SerializableEdge
    {
        public string sourceNodeGuid;
        public string sourcePortName;
        public string targetNodeGuid;
        public string targetPortName;
    }

    [CreateAssetMenu(fileName = "NewGrammarGraph", menuName = "Shape Grammar/Graph Asset")]
    public class GrammarGraphAsset : ScriptableObject
    {
        // [SerializeReference] preserves distinct polymorphism profiles across different operation steps
        [SerializeReference]
        public List<IStep> serializedSteps = new List<IStep>();
        public List<SerializableEdge> serializedEdges = new List<SerializableEdge>();
    }
}