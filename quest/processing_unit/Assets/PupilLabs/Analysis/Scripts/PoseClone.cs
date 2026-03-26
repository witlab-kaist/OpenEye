using UnityEngine;

namespace PupilLabs
{
    public class PoseClone : MonoBehaviour
    {
        [SerializeField]
        private Transform target;
        [SerializeField]
        private bool clonePosition;
        [SerializeField]
        private bool cloneRotation;

        void LateUpdate()
        {
            if (clonePosition)
            {
                transform.position = target.position;
            }
            if (cloneRotation)
            {
                transform.rotation = target.rotation;
            }
        }
    }
}
