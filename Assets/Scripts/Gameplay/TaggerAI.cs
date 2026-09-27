using UnityEngine;

namespace TagGame.Gameplay
{
    /// <summary>
    /// Practice-mode AI tagger. Chases the nearest un-tagged runner directly
    /// (no NavMesh dependency, so it works in an empty test scene). Swap the
    /// movement block for a NavMeshAgent.SetDestination call if your level
    /// has obstacles and a baked NavMesh.
    /// </summary>
    public class TaggerAI : MonoBehaviour
    {
        [SerializeField] private float chaseSpeed = 5.5f;
        [SerializeField] private float turnSpeedDegrees = 540f;
        [SerializeField] private float tagRadius = 1.1f;

        private Transform _target;
        private TagGameManager _manager;

        public void Initialize(TagGameManager manager, Transform initialTarget)
        {
            _manager = manager;
            _target = initialTarget;
        }

        public void SetTarget(Transform target) => _target = target;

        private void Update()
        {
            if (_target == null || _manager == null || !_manager.RoundInProgress)
            {
                return;
            }

            Vector3 toTarget = _target.position - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            if (distance <= tagRadius)
            {
                _manager.OnTaggerReachedPlayer(_target);
                return;
            }

            Vector3 dir = toTarget.normalized;
            transform.position += dir * chaseSpeed * Time.deltaTime;

            if (dir.sqrMagnitude > 0.001f)
            {
                Quaternion targetRot = Quaternion.LookRotation(dir, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRot, turnSpeedDegrees * Time.deltaTime);
            }
        }
    }
}
