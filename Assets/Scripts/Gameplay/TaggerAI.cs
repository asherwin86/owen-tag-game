using System.Collections.Generic;
using UnityEngine;
using TagGame.Maps;

namespace TagGame.Gameplay
{
    /// <summary>
    /// Practice-mode bot. Plays pass-the-tag against everyone else (you and the other bots):
    /// when it is "it" it hunts the nearest runner, otherwise it runs away from whoever is "it".
    /// Uses A* around walls on the big maps. Each bot has its own speed and start delay.
    /// </summary>
    public class TaggerAI : MonoBehaviour
    {
        [SerializeField] private float chaseSpeed = 5.5f;
        [SerializeField] private float turnSpeedDegrees = 540f;
        [SerializeField] private float tagRadius = 1.2f;

        public string DisplayName { get; private set; } = "Tiger";

        private TagGameManager _manager;
        private float _speed, _delay, _repathAt, _wanderAt;
        private List<Vector3> _path;
        private int _pi;
        private Vector3 _wander;
        private Transform _lastTarget;

        public void Initialize(TagGameManager manager, string displayName)
        {
            _manager = manager;
            DisplayName = displayName;
            _speed = chaseSpeed * Random.Range(0.78f, 1.0f);
            _delay = Random.Range(0.5f, 2.0f);
            _repathAt = Time.time + Random.value * 0.5f;
            _wander = transform.position;
        }

        private void Update()
        {
            if (_manager == null || !_manager.RoundInProgress) return;
            if (_delay > 0f) { _delay -= Time.deltaTime; return; }
            if (_manager.IsFrozen(transform)) return;

            var maps = MapManager.I;
            var grid = maps != null ? maps.Grid : null;
            Vector3 pos = transform.position;
            Vector3 dir;
            float speed;

            if (_manager.IsIt(transform))
            {
                var target = _manager.NearestRunnerFor(transform);
                if (target == null) return;
                Vector3 to = target.position - pos; to.y = 0f;
                if (to.magnitude <= tagRadius) { _manager.TryTag(transform, target); return; }

                Vector3 aim = target.position;
                if (grid != null && !grid.Los(pos, target.position))
                {
                    if (_path == null || target != _lastTarget || Time.time >= _repathAt)
                    {
                        _path = grid.FindPath(pos, target.position);
                        _pi = 0;
                        _lastTarget = target;
                        _repathAt = Time.time + Random.Range(0.45f, 0.8f);
                    }
                    if (_path != null)
                    {
                        while (_pi < _path.Count - 1 && new Vector3(_path[_pi].x - pos.x, 0f, _path[_pi].z - pos.z).sqrMagnitude < 0.6f) _pi++;
                        if (_pi < _path.Count) aim = _path[_pi];
                    }
                }
                else _path = null;
                dir = aim - pos;
                speed = _speed * 1.12f;
            }
            else
            {
                _path = null;
                Transform it = _manager.ItTransform;
                Vector3 away = it != null ? pos - it.position : Vector3.zero;
                away.y = 0f;
                if (it != null && away.magnitude < 20f)
                {
                    dir = BestFlee(pos, away.normalized, grid);
                    speed = _speed * 0.92f;
                }
                else
                {
                    // nobody close: amble to random spots
                    if (Time.time >= _wanderAt || (_wander - pos).sqrMagnitude < 2f)
                    {
                        float lim = maps != null ? maps.PlayLimit : 14f;
                        Vector3 cand = pos + new Vector3(Random.Range(-12f, 12f), 0f, Random.Range(-12f, 12f));
                        cand.x = Mathf.Clamp(cand.x, -lim, lim); cand.z = Mathf.Clamp(cand.z, -lim, lim);
                        if (grid == null || !grid.IsBlocked(cand.x, cand.z)) _wander = cand;
                        _wanderAt = Time.time + Random.Range(2f, 5f);
                    }
                    dir = BestFlee(pos, (_wander - pos).normalized, grid);
                    speed = _speed * 0.45f;
                }
            }

            dir.y = 0f;
            if (dir.sqrMagnitude < 0.0001f) return;
            dir.Normalize();
            Vector3 np = pos + dir * speed * Time.deltaTime;
            float limit = maps != null ? maps.PlayLimit : 14f;
            np.x = Mathf.Clamp(np.x, -limit, limit); np.z = Mathf.Clamp(np.z, -limit, limit);
            transform.position = np;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir, Vector3.up), turnSpeedDegrees * Time.deltaTime);
        }

        /// <summary>Pick the direction closest to 'want' that doesn't run into a wall.</summary>
        private static Vector3 BestFlee(Vector3 pos, Vector3 want, MapGrid grid)
        {
            if (grid == null) return want;
            float[] turns = { 0f, 40f, -40f, 80f, -80f, 120f, -120f, 160f };
            foreach (float t in turns)
            {
                Vector3 d = Quaternion.Euler(0f, t, 0f) * want;
                Vector3 p1 = pos + d * 1.5f, p2 = pos + d * 3f;
                if (!grid.IsBlocked(p1.x, p1.z) && !grid.IsBlocked(p2.x, p2.z)) return d;
            }
            return -want;
        }
    }
}
