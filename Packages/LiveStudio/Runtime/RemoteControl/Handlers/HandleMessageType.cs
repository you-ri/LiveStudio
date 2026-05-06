using UnityEngine;

namespace Lilium.LiveStudio
{

    [System.Serializable]
    public class InputActionInfo
    {
        public string name;
        public string[] bindings;
        public string[] bindingDisplayNames;
        public bool isEnabled;
        public string actionType;
    }

    [System.Serializable]
    public class ExpressionWeightInfo
    {
        public string name;
        public float weight;
    }


}