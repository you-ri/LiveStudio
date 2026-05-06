using UnityEngine;

namespace Lilium.LiveStudio
{
    [CreateAssetMenu(fileName = "VRMAvatarSetupSettings", menuName = "Live Studio/VRM Avatar Setup Settings")]
    public class VRMAvatarSetupSettings : ScriptableObject
    {
       public GameObject CharacterProviderPrefab => _characterProviderPrefab;

        [SerializeField]
        private GameObject _characterProviderPrefab;

        public GameObject armsPostRigPrefab => _postRigArmPrefab;

        [SerializeField]
        private GameObject _postRigArmPrefab;

    }
}