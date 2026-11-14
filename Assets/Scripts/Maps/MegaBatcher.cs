using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TagGame.Maps
{
    /// <summary>
    /// Builds thousands of boxes as a few big meshes (one per 250 m chunk and material) plus box colliders,
    /// so a 2 km city still runs in a web browser.
    /// </summary>
    internal class MegaBatcher
    {
        private const float Chunk = 250f;

        private class Part
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector3> n = new List<Vector3>();
            public readonly List<Vector2> uv = new List<Vector2>();
            public readonly List<int> t = new List<int>();
        }

        private readonly Dictionary<long, Part> _parts = new Dictionary<long, Part>();
        private readonly Dictionary<long, Material> _partMat = new Dictionary<long, Material>();
        private readonly Dictionary<Material, int> _matId = new Dictionary<Material, int>();
        private readonly List<Material> _mats = new List<Material>();
        private readonly Dictionary<int, GameObject> _colliders = new Dictionary<int, GameObject>();
        private readonly Transform _root;
        private readonly MapGrid _grid;

        public MegaBatcher(Transform root, MapGrid grid) { _root = root; _grid = grid; }

        private static int CK(float v) => Mathf.FloorToInt(v / Chunk) + 64;

        private Part P(int kx, int kz, Material m)
        {
            if (!_matId.TryGetValue(m, out int id)) { id = _mats.Count; _mats.Add(m); _matId[m] = id; }
            long key = ((long)kx << 40) | ((long)kz << 20) | (long)id;
            if (!_parts.TryGetValue(key, out var p)) { p = new Part(); _parts[key] = p; _partMat[key] = m; }
            return p;
        }

        private static void Quad(Part p, Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector3 normal, float u, float v)
        {
            int b = p.v.Count;
            p.v.Add(bl); p.v.Add(br); p.v.Add(tr); p.v.Add(tl);
            p.n.Add(normal); p.n.Add(normal); p.n.Add(normal); p.n.Add(normal);
            p.uv.Add(new Vector2(0f, 0f)); p.uv.Add(new Vector2(u, 0f)); p.uv.Add(new Vector2(u, v)); p.uv.Add(new Vector2(0f, v));
            p.t.Add(b); p.t.Add(b + 3); p.t.Add(b + 2);
            p.t.Add(b); p.t.Add(b + 2); p.t.Add(b + 1);
        }

        /// <summary>A box from y0 up to y0+h centred on (cx, cz). Blocks the bots' walk grid when it stands on the ground.</summary>
        public void Box(float cx, float y0, float cz, float sx, float h, float sz, Material side, Material top, bool collide = true, bool bottom = false, float tileU = 4f, float tileV = 3.5f, bool gridBlock = true)
        {
            if (sx <= 0.01f || sz <= 0.01f || h <= 0.001f) return;
            int kx = CK(cx), kz = CK(cz);
            float x0 = cx - sx * 0.5f, x1 = cx + sx * 0.5f, z0 = cz - sz * 0.5f, z1 = cz + sz * 0.5f, y1 = y0 + h;
            var ps = P(kx, kz, side);
            float us = sx / tileU, ud = sz / tileU, vh = h / tileV;
            Quad(ps, new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.back, us, vh);
            Quad(ps, new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1), Vector3.forward, us, vh);
            Quad(ps, new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.right, ud, vh);
            Quad(ps, new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1), Vector3.left, ud, vh);
            var pt = P(kx, kz, top);
            Quad(pt, new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.up, us, sz / tileU);
            if (bottom) Quad(pt, new Vector3(x1, y0, z0), new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), Vector3.down, us, sz / tileU);
            if (collide)
            {
                int ck = kx * 1000 + kz;
                if (!_colliders.TryGetValue(ck, out var go))
                {
                    go = new GameObject("Colliders_" + kx + "_" + kz);
                    go.transform.SetParent(_root, false);
                    _colliders[ck] = go;
                }
                var bc = go.AddComponent<BoxCollider>();
                bc.center = new Vector3(cx, y0 + h * 0.5f, cz);
                bc.size = new Vector3(sx, h, sz);
                if (_grid != null && y0 < 0.3f && h > 0.4f) _grid.BlockRect(cx, cz, sx, sz, 0.8f);
            }
        }

        /// <summary>A flat patch on the ground (roads, grass, courts). No collider.</summary>
        public void Patch(float cx, float cz, float sx, float sz, Material m, float y = 0.01f)
        {
            int kx = CK(cx), kz = CK(cz);
            var p = P(kx, kz, m);
            float x0 = cx - sx * 0.5f, x1 = cx + sx * 0.5f, z0 = cz - sz * 0.5f, z1 = cz + sz * 0.5f;
            Quad(p, new Vector3(x0, y, z0), new Vector3(x1, y, z0), new Vector3(x1, y, z1), new Vector3(x0, y, z1), Vector3.up, sx / 4f, sz / 4f);
        }

        /// <summary>Turns everything added so far into meshes.</summary>
        public List<Mesh> Flush(bool shadows)
        {
            var made = new List<Mesh>();
            foreach (var kv in _parts)
            {
                var p = kv.Value;
                if (p.v.Count == 0) continue;
                var go = new GameObject("MegaChunk");
                go.transform.SetParent(_root, false);
                var mesh = new Mesh();
                if (p.v.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(p.v); mesh.SetNormals(p.n); mesh.SetUVs(0, p.uv); mesh.SetTriangles(p.t, 0);
                mesh.RecalculateBounds();
                made.Add(mesh);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = _partMat[kv.Key];
                mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                mr.receiveShadows = true;
            }
            _parts.Clear();
            _partMat.Clear();
            return made;
        }
    }
}
