using System.Collections.Generic;
using UnityEngine;
using TagGame.Gameplay;
using TagGame.Maps;
using TagGame.UI;

namespace TagGame.Juice
{
    /// <summary>
    /// Speed boosts lying around each map: glowing spinning gems. Run through one for 5 seconds of extra speed
    /// and unlimited sprint. They come back 15 seconds after being taken. Same spots on every machine.
    /// </summary>
    public class Pickups : MonoBehaviour
    {
        private class Gem { public Transform t; public Vector3 home; public float backAt; }

        private static readonly int[] PerMap = { 3, 10, 14 };
        private readonly List<Gem> _gems = new List<Gem>();
        private PlayerController _player;
        private Material _mat;
        private Transform _root;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<Pickups>() != null) return;
            if (FindFirstObjectByType<PlayerController>() == null) return;
            new GameObject("Pickups").AddComponent<Pickups>();
        }

        private void Start()
        {
            _player = FindFirstObjectByType<PlayerController>();
            var r = _player != null ? _player.GetComponent<Renderer>() : null;
            if (r != null && r.sharedMaterial != null)
            {
                _mat = new Material(r.sharedMaterial) { color = new Color(1f, 0.82f, 0.15f) };
                if (_mat.HasProperty("_EmissionColor")) { _mat.EnableKeyword("_EMISSION"); _mat.SetColor("_EmissionColor", new Color(1f, 0.7f, 0.1f) * 1.6f); }
                Fx.Init(r.sharedMaterial);
            }
            if (MapManager.I != null) { MapManager.I.OnMapChanged += Build; Build(MapManager.I.Current); }
        }

        private void OnDestroy() { if (MapManager.I != null) MapManager.I.OnMapChanged -= Build; }

        private void Build(int map)
        {
            if (_root != null) Destroy(_root.gameObject);
            _gems.Clear();
            if (_mat == null) return;
            _root = new GameObject("Pickups_" + map).transform;
            var grid = MapManager.I != null ? MapManager.I.Grid : null;
            float lim = MapManager.I != null ? MapManager.I.PlayLimit : 14f;
            var rng = new System.Random(4242 + map * 97);
            int want = PerMap[Mathf.Clamp(map, 0, PerMap.Length - 1)], tries = 0;
            while (_gems.Count < want && tries++ < 2000)
            {
                float x = (float)(rng.NextDouble() * 2 - 1) * (lim - 2f), z = (float)(rng.NextDouble() * 2 - 1) * (lim - 2f);
                float minR = map == 0 ? 5f : 16f;
                if (x * x + z * z < minR * minR) continue;
                if (grid != null && (grid.IsBlocked(x, z) || grid.IsBlocked(x + 1f, z) || grid.IsBlocked(x - 1f, z) || grid.IsBlocked(x, z + 1f) || grid.IsBlocked(x, z - 1f))) continue;
                bool close = false;
                foreach (var g in _gems) if ((g.home - new Vector3(x, 0f, z)).sqrMagnitude < 100f) { close = true; break; }
                if (close) continue;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
                go.name = "SpeedGem";
                go.transform.SetParent(_root, false);
                go.transform.localScale = Vector3.one * 0.55f;
                var rend = go.GetComponent<Renderer>();
                rend.sharedMaterial = _mat;
                var gem = new Gem { t = go.transform, home = new Vector3(x, 1.1f, z) };
                go.transform.position = gem.home;
                _gems.Add(gem);
                MapBlips.Register(go.transform, new Color(1f, 0.82f, 0.15f), 0.6f);
            }
        }

        private void Update()
        {
            if (_player == null || !_player.isActiveAndEnabled) return;
            Vector3 pp = _player.transform.position;
            foreach (var g in _gems)
            {
                if (g.t == null) continue;
                if (!g.t.gameObject.activeSelf)
                {
                    if (Time.time >= g.backAt) g.t.gameObject.SetActive(true);
                    continue;
                }
                g.t.position = g.home + Vector3.up * Mathf.Sin(Time.time * 2.5f + g.home.x) * 0.18f;
                g.t.rotation = Quaternion.Euler(45f, Time.time * 140f, 45f);
                Vector3 d = g.home - pp; d.y = 0f;
                if (d.sqrMagnitude < 1.4f * 1.4f)
                {
                    g.t.gameObject.SetActive(false);
                    g.backAt = Time.time + 15f;
                    _player.Boost(1.35f, 5f);
                    GameAudio.Play("pickup");
                    Fx.Burst(g.home - Vector3.up * 0.6f, new Color(1f, 0.82f, 0.15f));
                    TagPopup.Show("Speed boost!", new Color(0.95f, 0.75f, 0.1f));
                }
            }
        }
    }
}
