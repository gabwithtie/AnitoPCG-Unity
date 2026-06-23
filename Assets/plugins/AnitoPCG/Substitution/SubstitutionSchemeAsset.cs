using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gbe.ShapeGrammar
{
    [Serializable]
    public struct SubstitutionStep
    {
        public List<string> requiredFlags;
        public bool clampIndex; // loops back to 0 on overflow if false
        public string indexerFlag; // defaults to index 0 if indexerFlag does not exist in shape data
        public List<GameObject> indexedPrefab; // Should always have 1 element because the first element is always the fallback
    }

    [CreateAssetMenu(fileName = "NewSubstitutionScheme", menuName = "Shape Grammar/Substitution Scheme")]
    public class SubstitutionSchemeAsset : ScriptableObject
    {
        public List<SubstitutionStep> serializedSteps = new();
    }
}