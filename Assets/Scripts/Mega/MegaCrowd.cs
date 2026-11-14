using UnityEngine;

namespace TagGame.Mega
{
    /// <summary>
    /// The huge crowd for Mega Mode's biggest sizes (up to 1,000,000 players). Everyone far from you lives here as a
    /// few numbers in big arrays: they walk the streets, turn at crossings, pass the tag around and earn safe time.
    /// When one of them walks into the zone around you (and there is room), they become a full bot you can see and
    /// play with; full bots that wander far away come back into the crowd. Updated a slice at a time so it stays cheap.
    /// </summary>
    public class MegaCrowd
    {
        public int Count { get; private set; }
        public int Its { get; private set; }
        /// <summary>How many crowd players had more safe time than you at the last full sweep.</summary>
        public int RankAbove { get; private set; }

        private float[] _along, _safe, _last, _lane;
        private byte[] _axis, _color, _flags;   // flags: 1 = IT, 2 = going backwards
        private short[] _k;                     // which street (100 m lines from the region's centre)
        private int[] _id;
        private int _cursor, _aboveAcc;
        private uint _rng = 2463534242u;

        private const float Speed = 5.4f;
        /// <summary>The crowd lives in a square region of the world (the country you're in, or 10 km of the Super Mega World).</summary>
        public double RCX, RCZ;
        public float RH = 5000f;
        private int KMax => Mathf.Max(1, Mathf.FloorToInt(RH / 100f) - 1);
        private float AMax => RH - 50f;
        private static float Street(int k) => 100f * k;
        private Vector3 RegionToScene => new Vector3((float)(RCX - MegaWorld.OriginX), 0f, (float)(RCZ - MegaWorld.OriginZ));

        private float Rand() { _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5; return (_rng & 0xFFFFFF) / 16777216f; }

        public void Init(int n, int its, int firstId, double rcx, double rcz, float rh)
        {
            Count = n; Its = 0;
            RCX = rcx; RCZ = rcz; RH = rh;
            _along = new float[n]; _safe = new float[n]; _last = new float[n]; _lane = new float[n];
            _axis = new byte[n]; _k = new short[n]; _color = new byte[n]; _flags = new byte[n]; _id = new int[n];
            float now = Time.time;
            for (int i = 0; i < n; i++)
            {
                Place(i);
                _color[i] = (byte)(Rand() * 8f);
                _flags[i] = (byte)(Rand() < 0.5f ? 2 : 0);
                _last[i] = now;
                _id[i] = firstId + i;
            }
            for (int t = 0; t < its && t < n; t++) { int j = (int)(Rand() * n); if ((_flags[j] & 1) == 0) { _flags[j] |= 1; Its++; } }
            _cursor = 0; _aboveAcc = 0; RankAbove = 0;
        }

        private void Place(int i)
        {
            _axis[i] = (byte)(Rand() < 0.5f ? 0 : 1);
            _k[i] = (short)Mathf.RoundToInt((Rand() * 2f - 1f) * KMax);
            _along[i] = (Rand() * 2f - 1f) * AMax;
            _lane[i] = Rand() * 8f - 4f;
        }

        /// <summary>Moves the whole crowd to a new region (you flew to another country). Scores are kept.</summary>
        public void Scatter(double rcx, double rcz, float rh)
        {
            RCX = rcx; RCZ = rcz; RH = rh;
            for (int i = 0; i < Count; i++) Place(i);
        }

        /// <summary>Keeps the number of ITs in the crowd at target.</summary>
        public void SetIts(int target)
        {
            int guard = 0;
            while (Its < target && Count > 0 && guard++ < 1000) { int j = (int)(Rand() * Count); if ((_flags[j] & 1) == 0) { _flags[j] |= 1; Its++; } }
            if (Its > target)
                for (int i = 0; i < Count && Its > target; i++)
                    if ((_flags[i] & 1) != 0) { _flags[i] = (byte)(_flags[i] & 2); Its--; }
        }

        public void Clear() { Count = 0; Its = 0; _along = null; _safe = null; _last = null; _lane = null; _axis = null; _k = null; _color = null; _flags = null; _id = null; }

        public Vector3 PosOf(int i)
        {
            float line = Street(_k[i]) + _lane[i];
            Vector3 r = _axis[i] == 0 ? new Vector3(_along[i], 0f, line) : new Vector3(line, 0f, _along[i]);
            return r + RegionToScene;
        }

        /// <summary>A full bot walking away from you goes back into the crowd.</summary>
        public void Add(Vector3 p, float safe, bool it, int color, int id)
        {
            if (_along == null) return;
            if (Count >= _along.Length) Grow();
            int i = Count++;
            p -= RegionToScene;
            int kx = Mathf.Clamp(Mathf.RoundToInt(p.x / 100f), -KMax, KMax), kz = Mathf.Clamp(Mathf.RoundToInt(p.z / 100f), -KMax, KMax);
            float dx = Mathf.Abs(p.x - Street(kx)), dz = Mathf.Abs(p.z - Street(kz));
            if (dz < dx) { _axis[i] = 0; _k[i] = (short)kz; _along[i] = Mathf.Clamp(p.x, -AMax, AMax); }
            else { _axis[i] = 1; _k[i] = (short)kx; _along[i] = Mathf.Clamp(p.z, -AMax, AMax); }
            _lane[i] = Rand() * 8f - 4f;
            _safe[i] = safe; _last[i] = Time.time; _color[i] = (byte)color; _id[i] = id;
            _flags[i] = (byte)((it ? 1 : 0) | (Rand() < 0.5f ? 2 : 0));
            if (it) Its++;
        }

        private void Grow()
        {
            int n = Mathf.Max(16, _along.Length * 3 / 2);
            System.Array.Resize(ref _along, n); System.Array.Resize(ref _safe, n); System.Array.Resize(ref _last, n); System.Array.Resize(ref _lane, n);
            System.Array.Resize(ref _axis, n); System.Array.Resize(ref _k, n); System.Array.Resize(ref _color, n); System.Array.Resize(ref _flags, n); System.Array.Resize(ref _id, n);
        }

        private void RemoveAt(int i)
        {
            if ((_flags[i] & 1) != 0) Its--;
            int last = --Count;
            _along[i] = _along[last]; _safe[i] = _safe[last]; _last[i] = _last[last]; _lane[i] = _lane[last];
            _axis[i] = _axis[last]; _k[i] = _k[last]; _color[i] = _color[last]; _flags[i] = _flags[last]; _id[i] = _id[last];
        }

        public delegate bool PromoteFn(Vector3 pos, float safe, bool it, int color, int id);

        /// <summary>Moves a slice of the crowd forward in time. Anyone inside radius r of the centre is offered to promote().</summary>
        public void Tick(int budget, Vector3 centre, float r, float mySafe, PromoteFn promote)
        {
            if (Count == 0) return;
            float now = Time.time, r2 = r * r;
            int n = Mathf.Min(budget, Count);
            for (int s = 0; s < n && Count > 0; s++)
            {
                if (_cursor >= Count)
                {
                    _cursor = 0;
                    RankAbove = _aboveAcc; _aboveAcc = 0;
                }
                int i = _cursor;
                float dt = Mathf.Min(now - _last[i], 2f);
                _last[i] = now;
                bool it = (_flags[i] & 1) != 0;
                if (!it) _safe[i] += dt;
                if (_safe[i] > mySafe) _aboveAcc++;

                // walk the street, sometimes turning at a crossing
                float dir = (_flags[i] & 2) != 0 ? -1f : 1f;
                float spd = Speed * (0.85f + (_id[i] % 7) * 0.05f) * (it ? 1.1f : 1f);
                float a0 = _along[i], a1 = a0 + dir * spd * dt;
                int m0 = Mathf.FloorToInt(a0 / 100f), m1 = Mathf.FloorToInt(a1 / 100f);
                if (m0 != m1 && Rand() < 0.35f)
                {
                    int cross = Mathf.Clamp(dir > 0f ? m1 : m0, -KMax, KMax);
                    float oldLine = Street(_k[i]);
                    _axis[i] = (byte)(1 - _axis[i]);
                    _k[i] = (short)cross;
                    a1 = oldLine;
                    _flags[i] = (byte)((_flags[i] & 1) | (Rand() < 0.5f ? 2 : 0));
                }
                float am = AMax;
                if (a1 > am) { a1 = am; _flags[i] |= 2; }
                else if (a1 < -am) { a1 = -am; _flags[i] = (byte)(_flags[i] & 1); }
                _along[i] = a1;

                // IT passes the tag on now and then (to someone else in the crowd)
                if (it && Rand() < 0.04f * dt)
                {
                    int j = (int)(Rand() * Count);
                    if ((_flags[j] & 1) == 0) { _flags[j] |= 1; _flags[i] = (byte)(_flags[i] & 2); }
                }

                Vector3 p = PosOf(i);
                float dx = p.x - centre.x, dz = p.z - centre.z;
                if (dx * dx + dz * dz < r2 && promote(p, _safe[i], (_flags[i] & 1) != 0, _color[i], _id[i]))
                {
                    RemoveAt(i);
                    continue;   // the last one moved into slot i; look at it next
                }
                _cursor++;
            }
        }

        /// <summary>Best scores in the crowd (for the results screen).</summary>
        public void Top(int howMany, System.Collections.Generic.List<KeyValuePair> into)
        {
            for (int i = 0; i < Count; i++)
            {
                float s = _safe[i];
                if (into.Count >= howMany && s <= into[into.Count - 1].Safe) continue;
                into.Add(new KeyValuePair { Id = _id[i], Safe = s });
                into.Sort((a, b) => b.Safe.CompareTo(a.Safe));
                if (into.Count > howMany) into.RemoveAt(into.Count - 1);
            }
        }

        public struct KeyValuePair { public int Id; public float Safe; }
    }
}
