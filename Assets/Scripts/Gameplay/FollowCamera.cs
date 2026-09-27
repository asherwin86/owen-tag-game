using UnityEngine;

namespace TagGame.Gameplay
{
    /// <summary>
    /// Simple third-person follow camera: stays a fixed distance behind and
    /// above the target, looking at it. Attach to Main Camera and assign the
    /// Player's transform as the target.
    /// </summary>
    public class FollowCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 4f, -7f);
        [SerializeField] private float followSpeed = 8f;
        [SerializeField] private float lookAtHeightOffset = 1f;

        public void SetTarget(Transform newTarget) => target = newTarget;

        private void LateUpdate()
        {
            if (target == null) return;

            Vector3 desiredPosition = target.position + offset;
            transform.position = Vector3.Lerp(transform.position, desiredPosition, followSpeed * Time.deltaTime);
            transform.LookAt(target.position + Vector3.up * lookAtHeightOffset);
        }
    }
}
