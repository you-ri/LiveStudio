using UnityEngine;

namespace Lilium.LiveStudio
{
    public class ChairController : MonoBehaviour
    {
        public GameObject character;

        public float interpolationSpeed = 5f;

        public float spaceAngle = 5f;

        private Vector3 targetRotation;

        void Start()
        {
            if (character == null)
            {
                Debug.LogError("[LiveStudio] Character is not assigned in ChairController.");
                return;
            }
            targetRotation = character.transform.rotation.eulerAngles;
        }

        void Update()
        {
            if (character != null)
            {
                // Rotate the chair to face the character
                var characterRotation = character.transform.rotation.eulerAngles;
                characterRotation.z = 0;
                characterRotation.x = 0; // Keep the chair upright

                if (Mathf.Abs(Mathf.DeltaAngle(transform.rotation.eulerAngles.y, characterRotation.y)) > spaceAngle)
                {
                    targetRotation = characterRotation;
                }
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.Euler(characterRotation), Time.deltaTime * interpolationSpeed);
            }
        }
    }
}
