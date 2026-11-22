using UnityEngine;

namespace BoxDefence
{
    /// <summary>Copies another transform's position and rotation (not its scale), after it has moved.</summary>
    [DefaultExecutionOrder(200)]
    public class FollowTransform : MonoBehaviour
    {
        public Transform target;
        void LateUpdate()
        {
            if (target) transform.SetPositionAndRotation(target.position, target.rotation);
        }
    }
}
