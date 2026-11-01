using System.Collections.Generic;
using UnityEngine;

namespace TagGame.Juice
{
    /// <summary>Little puffs of dust and tag bursts, made from pooled spheres that grow and shrink.</summary>
    public class Fx : MonoBehaviour
    {
        private class Puff { public Transform t; public Renderer r; public Vector3 vel; public float born, life, size; }

        private static Fx _inst;
        private Material _base;
        private readonly Dictionary<int, Material> _mats = new Dictionary<int, Material>();
        private readonly List<Puff> _live = new List<Puff>();
        private readonly Stack<Puff> _pool = new Stack<Puff>();

        /// <summary>Give Fx a material it can copy (the player's), so it survives WebGL shader stripping.</summary>
        public static void Init(Material baseMat)
        {
            if (_inst == null)
            {
                var go = new GameObject("Fx");
                _inst = go.AddComponent<Fx>();
            }
            if (baseMat != null) _inst._base = baseMat;
        }

        public static void Dust(Vector3 pos) { Spawn(pos + Vector3.up * 0.1f, new Color(0.86f, 0.8f, 0.68f), new Vector3(Random.Range(-0.6f, 0.6f), 0.6f, Random.Range(-0.6f, 0.6f)), 0.45f, 0.35f); }

        public static void Burst(Vector3 pos, Color c)
        {
            for (int i = 0; i < 14; i++)
            {
                float a = i / 14f * Mathf.PI * 2f;
                Spawn(pos + Vector3.up * 1f, i % 2 == 0 ? c : Color.white, new Vector3(Mathf.Cos(a) * 4f, Random.Range(1.5f, 4f), Mathf.Sin(a) * 4f), 0.6f, 0.3f);
            }
        }

        private static void Spawn(Vector3 pos, Color c, Vector3 vel, float life, float size)
        {
            if (_inst == null || _inst._base == null) return;
            _inst.DoSpawn(pos, c, vel, life, size);
        }

        private void DoSpawn(Vector3 pos, Color c, Vector3 vel, float life, float size)
        {
            if (_live.Count > 160) return;
            Puff p = _pool.Count > 0 ? _pool.Pop() : null;
            if (p == null)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
                go.transform.SetParent(transform, false);
                p = new Puff { t = go.transform, r = go.GetComponent<Renderer>() };
                p.r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                p.r.receiveShadows = false;
            }
            int key = ((int)(c.r * 255f) << 16) | ((int)(c.g * 255f) << 8) | (int)(c.b * 255f);
            if (!_mats.TryGetValue(key, out var m)) { m = new Material(_base) { color = c }; _mats[key] = m; }
            p.r.sharedMaterial = m;
            p.t.position = pos; p.vel = vel; p.born = Time.time; p.life = life; p.size = size;
            p.t.localScale = Vector3.one * 0.01f;
            p.t.gameObject.SetActive(true);
            _live.Add(p);
        }

        private void Update()
        {
            for (int i = _live.Count - 1; i >= 0; i--)
            {
                var p = _live[i];
                float k = (Time.time - p.born) / p.life;
                if (k >= 1f) { p.t.gameObject.SetActive(false); _live.RemoveAt(i); _pool.Push(p); continue; }
                p.vel += Vector3.down * 6f * Time.deltaTime;
                p.t.position += p.vel * Time.deltaTime;
                float s = p.size * Mathf.Sin(Mathf.Clamp01(k) * Mathf.PI) * 1.4f;
                p.t.localScale = Vector3.one * Mathf.Max(0.01f, s);
            }
        }
    }
}
