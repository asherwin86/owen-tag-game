using System.Collections.Generic;
using UnityEngine;

namespace TagGame.Maps
{
    /// <summary>
    /// Flat grid of "blocked" cells built from the map's solid boxes. The AI taggers use it
    /// to path-find around buildings (A*) and to check if they can run straight at you.
    /// </summary>
    public class MapGrid
    {
        public readonly float Half;
        public readonly float Cell;
        public readonly int N;
        private readonly bool[] _blocked;

        // A* scratch (reused between searches, so repathing doesn't allocate much)
        private readonly float[] _g;
        private readonly int[] _parent;
        private readonly int[] _stamp;
        private int _curStamp;
        private readonly List<int> _heapIdx = new List<int>();
        private readonly List<float> _heapF = new List<float>();

        public MapGrid(float half, float cell)
        {
            Half = half; Cell = cell;
            N = Mathf.CeilToInt(half * 2f / cell);
            _blocked = new bool[N * N];
            _g = new float[N * N];
            _parent = new int[N * N];
            _stamp = new int[N * N];
        }

        public void BlockRect(float cx, float cz, float sx, float sz, float inflate)
        {
            int x0 = ToCell(cx - sx * 0.5f - inflate), x1 = ToCell(cx + sx * 0.5f + inflate);
            int z0 = ToCell(cz - sz * 0.5f - inflate), z1 = ToCell(cz + sz * 0.5f + inflate);
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                    if (x >= 0 && z >= 0 && x < N && z < N) _blocked[z * N + x] = true;
        }

        private int ToCell(float w) { return Mathf.FloorToInt((w + Half) / Cell); }

        public bool IsBlocked(float x, float z)
        {
            int cx = ToCell(x), cz = ToCell(z);
            if (cx < 0 || cz < 0 || cx >= N || cz >= N) return true;
            return _blocked[cz * N + cx];
        }

        /// <summary>True if nothing solid is in the way on the straight line between the two points.</summary>
        public bool Los(Vector3 a, Vector3 b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            float len = Mathf.Sqrt(dx * dx + dz * dz);
            int steps = Mathf.Max(1, Mathf.CeilToInt(len / (Cell * 0.5f)));
            for (int i = 1; i < steps; i++)
            {
                float t = i / (float)steps;
                if (IsBlocked(a.x + dx * t, a.z + dz * t)) return false;
            }
            return true;
        }

        private Vector3 CellCenter(int idx, float y)
        {
            int x = idx % N, z = idx / N;
            return new Vector3(-Half + (x + 0.5f) * Cell, y, -Half + (z + 0.5f) * Cell);
        }

        private int NearestFree(Vector3 p)
        {
            int cx = Mathf.Clamp(ToCell(p.x), 0, N - 1), cz = Mathf.Clamp(ToCell(p.z), 0, N - 1);
            if (!_blocked[cz * N + cx]) return cz * N + cx;
            for (int r = 1; r < 12; r++)
                for (int dz = -r; dz <= r; dz++)
                    for (int dx = -r; dx <= r; dx++)
                    {
                        if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r) continue;
                        int x = cx + dx, z = cz + dz;
                        if (x < 0 || z < 0 || x >= N || z >= N) continue;
                        if (!_blocked[z * N + x]) return z * N + x;
                    }
            return -1;
        }

        private static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] DZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        /// <summary>A* from a to b. Returns waypoints (world positions) or null if there is no way.</summary>
        public List<Vector3> FindPath(Vector3 a, Vector3 b)
        {
            int s = NearestFree(a), e = NearestFree(b);
            if (s < 0 || e < 0) return null;
            _curStamp++;
            _heapIdx.Clear(); _heapF.Clear();
            int ex = e % N, ez = e / N;
            _g[s] = 0f; _parent[s] = -1; _stamp[s] = _curStamp;
            Push(s, Heur(s % N, s / N, ex, ez));
            bool found = false;
            int guard = 0;
            while (_heapIdx.Count > 0 && guard++ < 200000)
            {
                int cur = Pop();
                if (cur == e) { found = true; break; }
                int cx = cur % N, cz = cur / N;
                float gc = _g[cur];
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k], nz = cz + DZ[k];
                    if (nx < 0 || nz < 0 || nx >= N || nz >= N) continue;
                    int ni = nz * N + nx;
                    if (_blocked[ni]) continue;
                    if (k >= 4 && (_blocked[cz * N + nx] || _blocked[nz * N + cx])) continue; // no cutting corners
                    float ng = gc + (k < 4 ? 1f : 1.4142f);
                    if (_stamp[ni] == _curStamp && ng >= _g[ni]) continue;
                    _stamp[ni] = _curStamp; _g[ni] = ng; _parent[ni] = cur;
                    Push(ni, ng + 1.15f * Heur(nx, nz, ex, ez));
                }
            }
            if (!found) return null;
            var rev = new List<Vector3>();
            for (int c = e; c >= 0; c = _parent[c]) rev.Add(CellCenter(c, a.y));
            rev.Reverse();
            // skip the first node (we're already there)
            if (rev.Count > 1) rev.RemoveAt(0);
            return rev;
        }

        private static float Heur(int x, int z, int ex, int ez)
        {
            float dx = Mathf.Abs(x - ex), dz = Mathf.Abs(z - ez);
            return (dx + dz) + (1.4142f - 2f) * Mathf.Min(dx, dz);
        }

        // --- tiny binary min-heap on (f, idx)
        private void Push(int idx, float f)
        {
            _heapIdx.Add(idx); _heapF.Add(f);
            int i = _heapIdx.Count - 1;
            while (i > 0)
            {
                int p = (i - 1) >> 1;
                if (_heapF[p] <= _heapF[i]) break;
                Swap(p, i); i = p;
            }
        }
        private int Pop()
        {
            int top = _heapIdx[0];
            int last = _heapIdx.Count - 1;
            _heapIdx[0] = _heapIdx[last]; _heapF[0] = _heapF[last];
            _heapIdx.RemoveAt(last); _heapF.RemoveAt(last);
            int i = 0, n = _heapIdx.Count;
            while (true)
            {
                int l = i * 2 + 1, r = l + 1, m = i;
                if (l < n && _heapF[l] < _heapF[m]) m = l;
                if (r < n && _heapF[r] < _heapF[m]) m = r;
                if (m == i) break;
                Swap(m, i); i = m;
            }
            return top;
        }
        private void Swap(int a, int b)
        {
            int ti = _heapIdx[a]; _heapIdx[a] = _heapIdx[b]; _heapIdx[b] = ti;
            float tf = _heapF[a]; _heapF[a] = _heapF[b]; _heapF[b] = tf;
        }
    }
}
