using System.Collections.Generic;
using UnityEngine;

namespace Gbe.ShapeGrammar
{
    [CreateAssetMenu(fileName = "NewSubstitutionScheme", menuName = "Shape Grammar/Substitution Scheme")]
    public class SubstitutionSchemeAsset : ScriptableObject
    {
        [SerializeReference]
        public List<ISubStep> serializedSteps = new List<ISubStep>();
    }
}