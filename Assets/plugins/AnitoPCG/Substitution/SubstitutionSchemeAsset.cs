using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public class SubstitutionEdge
    {
        public string sourceNodeGuid;
        public string sourcePortName;
        public string targetNodeGuid;
        public string targetPortName;
    }

    // Inside SubstitutionSchemeAsset:

    [CreateAssetMenu(fileName = "NewSubstitutionScheme", menuName = "Shape Grammar/Substitution Scheme")]
    public class SubstitutionSchemeAsset : ScriptableObject
    {
        [SerializeReference]
        public List<ISubStep> serializedSteps = new List<ISubStep>();
        public List<SubstitutionEdge> serializedEdges = new List<SubstitutionEdge>();
    }
}