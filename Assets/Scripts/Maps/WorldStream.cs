using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TagGame.Mega;

namespace TagGame.Maps
{
    /// <summary>
    /// Builds the World around the camera: detailed 500 m chunks close by (with colliders, a walk grid for the bots,
    /// items and buildings), and a low-detail 2 km skyline further out. Unloads what's far away. Keeps the scene's
    /// origin near you (moving everything in 500 m steps) so a 100 km world stays sharp and smooth.
    /// </summary>
    public class WorldStream : MonoBehaviour, IWalk
    {
        public static WorldStream I { get; private set; }

        public float DetailRadius = 900f, LodRadius = 7000f;
        public bool DetailOn = true;
        /// <summary>True when the detailed chunks around the focus are all built.</summary>
        public bool Ready { get; private set; }
        public System.Func<Vector3> Focus;   // where to build around (the player, or the plane)

        private MapManager _maps;
        private readonly Dictionary<long, ChunkRecord> _chunks = new Dictionary<long, ChunkRecord>();
        private class Lod { public Transform Root; public List<Mesh> Meshes = new List<Mesh>(); public int Country, TI, TJ; public bool Dirty; }
        private readonly Dictionary<long, Lod> _lods = new Dictionary<long, Lod>();
        private IEnumerator _building;
        private ChunkRecord _buildingRec;
        private readonly System.Diagnostics.Stopwatch _sw = new System.Diagnostics.Stopwatch();
        private float _scanAt;
        private readonly List<long> _tmpKeys = new List<long>();

        public void Init(MapManager maps)
        {
            I = this;
            _maps = maps;
            MegaWorld.Walk = this;
            MegaWorld.OnShift += OnShift;
        }

        private void OnDestroy()
        {
            MegaWorld.OnShift -= OnShift;
            if (I == this) I = null;
            if (MegaWorld.Walk == (IWalk)this) MegaWorld.Walk = null;
            foreach (var c in _chunks.Values) Unload(c, false);
            _chunks.Clear();
            foreach (var l in _lods.Values) DropLod(l);
            _lods.Clear();
        }

        private static long Key(int country, int ci, int cj) => ((long)country << 40) | ((long)(ci + 1000) << 20) | (long)(cj + 1000);

        private Vector3 FocusPos()
        {
            if (Focus != null) return Focus();
            var cam = Camera.main;
            return cam != null ? cam.transform.position : Vector3.zero;
        }

        // ------------------------------------------------------------ walk grid for bots (IWalk)

        private ChunkRecord ChunkAt(float x, float z)
        {
            double wx = MegaWorld.WX(x), wz = MegaWorld.WZ(z);
            int ci = MegaWorld.CountryAt(wx, wz);
            if (ci < 0) return null;
            var c = MegaWorld.Countries[ci];
            int a = (int)System.Math.Floor((wx - (c.CX - c.Half)) / MegaWorld.ChunkSize), b = (int)System.Math.Floor((wz - (c.CZ - c.Half)) / MegaWorld.ChunkSize);
            return _chunks.TryGetValue(Key(ci, a, b), out var r) && r.Done ? r : null;
        }

        public bool IsBlocked(float x, float z)
        {
            var r = ChunkAt(x, z);
            return r == null || r.Grid.IsBlocked(x, z);
        }

        public bool Los(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / 1f));
            for (int i = 1; i < steps; i++)
            {
                float t = i / (float)steps;
                if (IsBlocked(a.x + dx * t, a.z + dz * t)) return false;
            }
            return true;
        }

        public List<Vector3> FindPath(Vector3 a, Vector3 b, int maxNodes = 200000)
        {
            var ra = ChunkAt(a.x, a.z);
            if (ra == null || ra != ChunkAt(b.x, b.z)) return null;
            return ra.Grid.FindPath(a, b, maxNodes);
        }

        // ------------------------------------------------------------ streaming

        /// <summary>Builds everything near the focus right now (used behind the loading screen).</summary>
        public void BuildNowAround(Vector3 p)
        {
            Scan(p);
            int guard = 0;
            while (guard++ < 400)
            {
                if (_building == null) { if (!StartNext(p)) break; }
                while (_building != null && _building.MoveNext()) { }
                if (_buildingRec != null) ChunkFinished(_buildingRec);
                _building = null; _buildingRec = null;
            }
            foreach (var l in _lods.Values) if (l.Dirty) RebuildLod(l);
            Ready = true;
        }

        private void Update()
        {
            Vector3 p = FocusPos();
            // keep the origin close: everything moves in 500 m steps
            if (_building == null && (Mathf.Abs(p.x) > 1500f || Mathf.Abs(p.z) > 1500f)) { MegaWorld.ShiftOrigin(p); p = FocusPos(); }

            if (Time.unscaledTime >= _scanAt) { _scanAt = Time.unscaledTime + 0.4f; Scan(p); }

            // build a bit each frame, within a time budget
            _sw.Reset(); _sw.Start();
            while (_sw.ElapsedMilliseconds < 7)
            {
                if (_building == null && !StartNext(p)) break;
                if (!_building.MoveNext()) { ChunkFinished(_buildingRec); _building = null; _buildingRec = null; }
            }
            // refresh one stale skyline tile per frame
            if (_building == null)
                foreach (var l in _lods.Values) if (l.Dirty) { RebuildLod(l); break; }
            Ready = _building == null && !AnyMissing(p);
        }

        private bool AnyMissing(Vector3 p)
        {
            if (!DetailOn) return false;
            bool missing = false;
            ForChunksNear(p, DetailRadius * 0.6f, (ci, a, b, d) => { if (!_chunks.ContainsKey(Key(ci, a, b))) missing = true; });
            return missing;
        }

        private delegate void ChunkFn(int country, int ci, int cj, float dist);

        private void ForChunksNear(Vector3 p, float r, ChunkFn f)
        {
            double wx = MegaWorld.WX(p.x), wz = MegaWorld.WZ(p.z);
            for (int k = 0; k < MegaWorld.Countries.Length; k++)
            {
                var c = MegaWorld.Countries[k];
                double x0 = c.CX - c.Half, z0 = c.CZ - c.Half;
                int n = Mathf.RoundToInt(c.Half * 2f / MegaWorld.ChunkSize);
                int a0 = (int)System.Math.Floor((wx - r - x0) / MegaWorld.ChunkSize), a1 = (int)System.Math.Floor((wx + r - x0) / MegaWorld.ChunkSize);
                int b0 = (int)System.Math.Floor((wz - r - z0) / MegaWorld.ChunkSize), b1 = (int)System.Math.Floor((wz + r - z0) / MegaWorld.ChunkSize);
                a0 = Mathf.Max(a0, 0); b0 = Mathf.Max(b0, 0); a1 = Mathf.Min(a1, n - 1); b1 = Mathf.Min(b1, n - 1);
                for (int a = a0; a <= a1; a++)
                    for (int b = b0; b <= b1; b++)
                    {
                        double cx = x0 + (a + 0.5) * MegaWorld.ChunkSize, cz = z0 + (b + 0.5) * MegaWorld.ChunkSize;
                        float d = (float)System.Math.Sqrt((cx - wx) * (cx - wx) + (cz - wz) * (cz - wz));
                        if (d <= r) f(k, a, b, d);
                    }
            }
        }

        private void Scan(Vector3 p)
        {
            // unload far detail
            _tmpKeys.Clear();
            double wx = MegaWorld.WX(p.x), wz = MegaWorld.WZ(p.z);
            float keep = DetailOn ? DetailRadius + 450f : 400f;
            foreach (var kv in _chunks)
            {
                var r = kv.Value;
                if (r == _buildingRec) continue;
                var c = MegaWorld.Countries[r.Country];
                double cx = c.CX - c.Half + (r.CI + 0.5) * MegaWorld.ChunkSize, cz = c.CZ - c.Half + (r.CJ + 0.5) * MegaWorld.ChunkSize;
                if (System.Math.Sqrt((cx - wx) * (cx - wx) + (cz - wz) * (cz - wz)) > keep) _tmpKeys.Add(kv.Key);
            }
            foreach (var k in _tmpKeys) { Unload(_chunks[k], true); _chunks.Remove(k); }

            // skyline tiles: load near, drop far
            _tmpKeys.Clear();
            foreach (var kv in _lods)
            {
                var l = kv.Value; var c = MegaWorld.Countries[l.Country];
                double cx = c.CX - c.Half + (l.TI + 0.5) * 2000.0, cz = c.CZ - c.Half + (l.TJ + 0.5) * 2000.0;
                if (System.Math.Sqrt((cx - wx) * (cx - wx) + (cz - wz) * (cz - wz)) > LodRadius + 2500f) _tmpKeys.Add(kv.Key);
            }
            foreach (var k in _tmpKeys) { DropLod(_lods[k]); _lods.Remove(k); }
            for (int k = 0; k < MegaWorld.Countries.Length; k++)
            {
                var c = MegaWorld.Countries[k];
                int n = Mathf.CeilToInt(c.Half * 2f / 2000f);
                double x0 = c.CX - c.Half, z0 = c.CZ - c.Half;
                int a0 = Mathf.Max(0, (int)System.Math.Floor((wx - LodRadius - x0) / 2000.0)), a1 = Mathf.Min(n - 1, (int)System.Math.Floor((wx + LodRadius - x0) / 2000.0));
                int b0 = Mathf.Max(0, (int)System.Math.Floor((wz - LodRadius - z0) / 2000.0)), b1 = Mathf.Min(n - 1, (int)System.Math.Floor((wz + LodRadius - z0) / 2000.0));
                for (int a = a0; a <= a1; a++)
                    for (int b = b0; b <= b1; b++)
                    {
                        long key = Key(k + 16, a, b);
                        if (_lods.ContainsKey(key)) continue;
                        double cx = x0 + (a + 0.5) * 2000.0, cz = z0 + (b + 0.5) * 2000.0;
                        if (System.Math.Sqrt((cx - wx) * (cx - wx) + (cz - wz) * (cz - wz)) > LodRadius + 1500f) continue;
                        var l = new Lod { Country = k, TI = a, TJ = b, Dirty = true };
                        l.Root = new GameObject("Skyline_" + c.Name + "_" + a + "_" + b).transform;
                        l.Root.SetParent(transform, false);
                        _lods[key] = l;
                    }
            }
        }

        /// <summary>Starts building the nearest missing detailed chunk. False if nothing is missing.</summary>
        private bool StartNext(Vector3 p)
        {
            if (!DetailOn) return false;
            int bc = -1, ba = 0, bb = 0; float bd = float.MaxValue;
            ForChunksNear(p, DetailRadius, (ci, a, b, d) =>
            {
                if (d < bd && !_chunks.ContainsKey(Key(ci, a, b))) { bd = d; bc = ci; ba = a; bb = b; }
            });
            if (bc < 0) return false;
            var rec = new ChunkRecord { Country = bc, CI = ba, CJ = bb };
            _chunks[Key(bc, ba, bb)] = rec;
            _buildingRec = rec;
            _building = _maps.BuildChunk(rec);
            MarkLodDirty(bc, ba, bb);
            return true;
        }

        private void MarkLodDirty(int country, int ci, int cj)
        {
            if (_lods.TryGetValue(Key(country + 16, ci / 4, cj / 4), out var l)) l.Dirty = true;
        }

        private void Unload(ChunkRecord r, bool markLod)
        {
            if (r.Root != null) Destroy(r.Root.gameObject);
            foreach (var m in r.Meshes) if (m != null) Destroy(m);
            foreach (var b in r.Buildings) MegaWorld.Buildings.Remove(b);
            foreach (var it in r.Items) MegaItems.Remove(it);
            if (WorldTravel.I != null) WorldTravel.I.StandsGone(r);
            if (markLod) MarkLodDirty(r.Country, r.CI, r.CJ);
        }

        private void DropLod(Lod l)
        {
            if (l.Root != null) Destroy(l.Root.gameObject);
            foreach (var m in l.Meshes) if (m != null) Destroy(m);
            l.Meshes.Clear();
        }

        private void RebuildLod(Lod l)
        {
            l.Dirty = false;
            foreach (Transform ch in l.Root) Destroy(ch.gameObject);
            foreach (var m in l.Meshes) if (m != null) Destroy(m);
            l.Meshes.Clear();
            int country = l.Country;
            _maps.BuildLodTile(country, l.TI, l.TJ, l.Root, l.Meshes, (ci, cj) => _chunks.TryGetValue(Key(country, ci, cj), out var r) && r.Done);
        }

        /// <summary>Chunks that just finished building also need their skyline tile refreshed.</summary>
        public void ChunkFinished(ChunkRecord r)
        {
            MarkLodDirty(r.Country, r.CI, r.CJ);
            if (r.PlaneStands.Count > 0 && WorldTravel.I != null) WorldTravel.I.StandsReady(r);
        }

        /// <summary>How tall the city is under a point (for planes), from the layout, even where nothing is built.</summary>
        public static float GroundClearance(Vector3 p)
        {
            double wx = MegaWorld.WX(p.x), wz = MegaWorld.WZ(p.z);
            int k = MegaWorld.CountryAt(wx, wz);
            if (k < 0) return MegaWorld.SeaLevel;
            var c = MegaWorld.Countries[k];
            float lx = (float)(wx - c.CX), lz = (float)(wz - c.CZ);
            int ci = Mathf.FloorToInt((lx + c.Half) / MegaWorld.ChunkSize), cj = Mathf.FloorToInt((lz + c.Half) / MegaWorld.ChunkSize);
            var kind = MapManager.KindOf(c, ci, cj);
            float inX = lx + c.Half - ci * MegaWorld.ChunkSize, inZ = lz + c.Half - cj * MegaWorld.ChunkSize;
            int bi = Mathf.Clamp(Mathf.FloorToInt(inX / 100f), 0, 4), bj = Mathf.Clamp(Mathf.FloorToInt(inZ / 100f), 0, 4);
            float bx = inX - bi * 100f, bz = inZ - bj * 100f;
            if (bx < 10f || bx > 90f || bz < 10f || bz > 90f) return 0f;   // over a street
            return MapManager.BlockHeight(c, kind, lx, lz, bi, bj, ci, cj);
        }

        private void OnShift(Vector3 d)
        {
            foreach (var r in _chunks.Values)
            {
                if (r.Root != null) r.Root.position -= d;
                if (r.Grid != null) { r.Grid.OX -= d.x; r.Grid.OZ -= d.z; }
                for (int i = 0; i < r.PlaneStands.Count; i++) { var s = r.PlaneStands[i]; s.position -= d; r.PlaneStands[i] = s; }
            }
            foreach (var l in _lods.Values) if (l.Root != null) l.Root.position -= d;
        }
    }
}
