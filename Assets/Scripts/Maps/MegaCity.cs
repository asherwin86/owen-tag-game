using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TagGame.Mega;

namespace TagGame.Maps
{
    /// <summary>What one streamed 500 m chunk of the world made, so it can be cleaned up again.</summary>
    public class ChunkRecord
    {
        public int Country, CI, CJ;
        public Transform Root;
        public MapGrid Grid;
        public readonly List<MegaWorld.Building> Buildings = new List<MegaWorld.Building>();
        public readonly List<FloorItem> Items = new List<FloorItem>();
        public readonly List<Mesh> Meshes = new List<Mesh>();
        public readonly List<Pose> PlaneStands = new List<Pose>();
        public bool Done;
    }

    /// <summary>
    /// The World for Mega Mode: walled island countries on a sea. Three 10 km cities (Mega City, Harbor City, Sky City)
    /// and the 100 km Super Mega World. Far too big to build at once, so it is built in 500 m chunks around you
    /// (WorldStream), with a cheap low-detail skyline further out. Every chunk is made from its own seed, so it's
    /// the same city every time. Streets run along every 100 m line; blocks are 80 m.
    /// </summary>
    public partial class MapManager
    {
        private const float BlockSize = MegaWorld.BlockSize;
        private MegaBatcher _mb;
        private Rng _mr;
        private Transform _buildRoot;
        private ChunkRecord _rec;
        private readonly List<Vector3> _spots = new List<Vector3>();
        private bool _worldMats;
        private Material _mConcrete, _mFloor, _mRoof, _mGrass, _mPath, _mSidewalk, _mLine, _mTrunk, _mLeaf, _mPine, _mGlass, _mWall, _mMetal, _mField, _mCourt, _mSand, _mAsphalt, _mTarmac, _mWhite, _mCrop, _mBark, _mRoofRed, _mLod, _mLodRoof;
        private Material[] _mLodWalls;
        private static readonly Color[] MegaPalette =
        {
            new Color(0.75f, 0.55f, 0.45f), new Color(0.6f, 0.65f, 0.75f), new Color(0.85f, 0.8f, 0.65f), new Color(0.55f, 0.7f, 0.6f),
            new Color(0.8f, 0.6f, 0.7f), new Color(0.7f, 0.7f, 0.72f), new Color(0.5f, 0.55f, 0.65f), new Color(0.82f, 0.7f, 0.5f),
        };
        private static readonly Color[] GlassPalette =
        {
            new Color(0.45f, 0.6f, 0.75f), new Color(0.35f, 0.5f, 0.6f), new Color(0.6f, 0.7f, 0.8f), new Color(0.4f, 0.55f, 0.55f),
        };

        private Transform BuildRoot() => _buildRoot != null ? _buildRoot : Root();

        private void BuildMega()
        {
            MegaWorld.Clear();
            Grid = null;
            WorldMaterials();
            var root = Root();
            var ws = root.gameObject.AddComponent<WorldStream>();
            ws.Init(this);
        }

        private void WorldMaterials()
        {
            if (_worldMats && _mConcrete != null) return;
            _worldMats = true;
            _mConcrete = Tint(new Color(0.62f, 0.62f, 0.6f));
            _mFloor = Tint(new Color(0.78f, 0.74f, 0.66f));
            _mRoof = Tint(new Color(0.42f, 0.42f, 0.45f));
            _mGrass = Tint(new Color(0.3f, 0.56f, 0.26f), 0.02f);
            _mPath = Tint(new Color(0.78f, 0.74f, 0.62f), 0.02f);
            _mSidewalk = Tint(new Color(0.66f, 0.66f, 0.68f), 0.05f);
            _mLine = Tint(new Color(0.92f, 0.8f, 0.2f), 0.02f);
            _mTrunk = Tint(new Color(0.4f, 0.28f, 0.18f));
            _mLeaf = Tint(new Color(0.2f, 0.5f, 0.22f));
            _mPine = Tint(new Color(0.12f, 0.36f, 0.2f));
            _mGlass = Tint(new Color(0.45f, 0.65f, 0.8f), 0.85f);
            _mWall = Tint(new Color(0.88f, 0.86f, 0.8f));
            _mMetal = Tint(new Color(0.55f, 0.58f, 0.62f), 0.6f);
            _mField = Tint(new Color(0.35f, 0.68f, 0.3f), 0.02f);
            _mCourt = Tint(new Color(0.82f, 0.55f, 0.32f), 0.05f);
            _mSand = Tint(new Color(0.9f, 0.82f, 0.62f), 0.02f);
            _mAsphalt = Tint(new Color(0.22f, 0.22f, 0.24f), 0.15f);
            _mTarmac = Tint(new Color(0.3f, 0.3f, 0.32f), 0.1f);
            _mWhite = Tint(new Color(0.95f, 0.95f, 0.95f), 0.1f);
            _mCrop = Tint(new Color(0.75f, 0.68f, 0.3f), 0.02f);
            _mBark = Tint(new Color(0.55f, 0.2f, 0.15f));
            _mRoofRed = Tint(new Color(0.55f, 0.25f, 0.2f));
            _mLod = Tint(new Color(0.3f, 0.31f, 0.33f), 0.05f);
            _mLodRoof = Tint(new Color(0.4f, 0.4f, 0.43f), 0.05f);
            _mLodWalls = new Material[MegaPalette.Length];
            for (int i = 0; i < MegaPalette.Length; i++) _mLodWalls[i] = Windowed(MegaPalette[i]);
        }

        // ================================================================== layout (same answer every time)

        public enum ChunkKind { City, Lake, Park, Airport, Forest, Farm, Town }

        private static uint Hash(uint seed, int a, int b, int c = 0)
        {
            uint h = seed * 0x9E3779B1u ^ (uint)a * 0x85EBCA77u ^ (uint)b * 0xC2B2AE3Du ^ (uint)c * 0x27D4EB2Fu;
            h ^= h >> 15; h *= 0x2C1B3C6Du; h ^= h >> 12; h *= 0x297A2D39u; h ^= h >> 15;
            return h == 0 ? 1u : h;
        }
        private static float Hash01(uint seed, int a, int b, int c = 0) => (Hash(seed, a, b, c) & 0xFFFFFF) / 16777216f;

        /// <summary>How built-up this spot is, 0 (countryside) .. 1 (downtown).</summary>
        public static float Density(MegaWorld.Country c, float lx, float lz)
        {
            if (c.Style == 3)
            {
                float u = Mathf.PerlinNoise(lx / 9000f + c.Seed * 0.013f, lz / 9000f + 7.1f);
                float v = Mathf.PerlinNoise(lx / 2500f + 3.3f, lz / 2500f + c.Seed * 0.007f);
                return Mathf.Clamp01((u * 0.75f + v * 0.35f - 0.42f) * 2.2f);
            }
            float r = Mathf.Sqrt(lx * lx + lz * lz) / c.Half;
            float n = Mathf.PerlinNoise(lx / 1800f + c.Seed * 0.01f, lz / 1800f + 1.7f);
            return Mathf.Clamp01(1.05f - r * 0.9f + (n - 0.5f) * 0.5f);
        }

        /// <summary>What a chunk is. ci/cj count chunks from the country's -x/-z edge.</summary>
        public static ChunkKind KindOf(MegaWorld.Country c, int ci, int cj)
        {
            float lx = -c.Half + (ci + 0.5f) * MegaWorld.ChunkSize, lz = -c.Half + (cj + 0.5f) * MegaWorld.ChunkSize;
            foreach (var a in c.Airports)
                if (Mathf.Abs(lx - a.x) < 1500f && Mathf.Abs(lz - a.y) < 250f) return ChunkKind.Airport;
            foreach (var a in c.Airports)   // keep the land around airports dry
                if (Mathf.Abs(lx - a.x) < 2500f && Mathf.Abs(lz - a.y) < 1000f) return ChunkKind.City;
            float water = Mathf.PerlinNoise(lx / 3200f + c.Seed * 0.021f, lz / 3200f + 5.5f);
            float thr = c.Style == 1 ? 0.6f : (c.Style == 3 ? 0.66f : 0.72f);
            if (water > thr) return ChunkKind.Lake;
            float h = Hash01(c.Seed, ci, cj, 7);
            float d = Density(c, lx, lz);
            if (c.Style == 3 && d < 0.12f)
            {
                float f = Mathf.PerlinNoise(lx / 4000f + 11.2f, lz / 4000f + c.Seed * 0.003f);
                return f > 0.5f ? ChunkKind.Forest : ChunkKind.Farm;
            }
            if (h < (c.Style == 3 ? 0.08f : 0.06f)) return ChunkKind.Park;
            if (c.Style == 3 && d < 0.35f) return ChunkKind.Town;
            return ChunkKind.City;
        }

        /// <summary>Height of the tallest thing on a block (for the skyline and for planes not flying through cities).</summary>
        public static float BlockHeight(MegaWorld.Country c, ChunkKind k, float lx, float lz, int bi, int bj, int ci, int cj)
        {
            if (k == ChunkKind.Lake || k == ChunkKind.Farm || k == ChunkKind.Airport) return 0f;
            if (k == ChunkKind.Park || k == ChunkKind.Forest) return 9f;
            if (k == ChunkKind.Town) return 7f;
            float d = Density(c, lx, lz);
            float h = Hash01(c.Seed, ci * 5 + bi, cj * 5 + bj, 3);
            float tall = c.Style == 2 ? 1.6f : 1f;
            if (d > 0.7f) return (40f + h * 90f) * tall;
            if (d > 0.4f) return (14f + h * 26f) * tall;
            return 7f;
        }

        // ================================================================== a detailed chunk

        /// <summary>Builds one 500 m chunk, a few blocks at a time (yield between them so the game keeps running).</summary>
        internal IEnumerator BuildChunk(ChunkRecord rec)
        {
            WorldMaterials();
            var c = MegaWorld.Countries[rec.Country];
            float lx0 = -c.Half + rec.CI * MegaWorld.ChunkSize, lz0 = -c.Half + rec.CJ * MegaWorld.ChunkSize;   // country-relative corner
            Vector3 o = MegaWorld.ToLocal(c.CX + lx0, c.CZ + lz0);   // scene position of the corner
            float x0 = o.x, z0 = o.z, S = MegaWorld.ChunkSize;
            var kind = KindOf(c, rec.CI, rec.CJ);

            rec.Root = new GameObject("Chunk_" + c.Name + "_" + rec.CI + "_" + rec.CJ).transform;
            rec.Root.SetParent(Root(), false);
            rec.Grid = new MapGrid(S * 0.5f + 4f, 2f, x0 + S * 0.5f, z0 + S * 0.5f);
            _buildRoot = rec.Root; _rec = rec;
            _mb = new MegaBatcher(rec.Root, rec.Grid);
            _mr = new Rng(Hash(c.Seed, rec.CI, rec.CJ, 1));
            _spots.Clear();
            int maxCi = Mathf.RoundToInt(c.Half * 2f / S) - 1;

            // ground (with a hole for a lake), and the sea wall on the country's edge
            var holes = new List<Rect>();
            if (kind == ChunkKind.Lake) holes.Add(Rect.MinMaxRect(x0 + 30f, z0 + 30f, x0 + S - 30f, z0 + S - 30f));
            Material groundM = kind == ChunkKind.Farm || kind == ChunkKind.Forest || kind == ChunkKind.Park ? _mGrass : _mAsphalt;
            SlabWithHoles(Rect.MinMaxRect(x0, z0, x0 + S, z0 + S), holes, -1f, 1f, groundM, false);
            var wallM = Tint(new Color(0.55f, 0.53f, 0.5f));
            if (rec.CI == 0) _mb.Box(x0 - 1f, -1f, z0 + S * 0.5f, 2f, 11f, S, wallM, wallM);
            if (rec.CI == maxCi) _mb.Box(x0 + S + 1f, -1f, z0 + S * 0.5f, 2f, 11f, S, wallM, wallM);
            if (rec.CJ == 0) _mb.Box(x0 + S * 0.5f, -1f, z0 - 1f, S, 11f, 2f, wallM, wallM);
            if (rec.CJ == maxCi) _mb.Box(x0 + S * 0.5f, -1f, z0 + S + 1f, S, 11f, 2f, wallM, wallM);
            yield return null;

            switch (kind)
            {
                case ChunkKind.Lake: LakeChunk(x0, z0, S); break;
                case ChunkKind.Park: ParkChunk(x0, z0, S); break;
                case ChunkKind.Airport: AirportChunk(c, rec, lx0, lz0, x0, z0, S); break;
                case ChunkKind.Forest: ForestChunk(x0, z0, S); break;
                case ChunkKind.Farm: FarmChunk(x0, z0, S); break;
                default:
                    {
                        // street centre lines
                        for (int k = 0; k <= 5; k++)
                        {
                            _mb.Patch(x0 + k * 100f, z0 + S * 0.5f, 0.35f, S, _mLine, 0.015f);
                            _mb.Patch(x0 + S * 0.5f, z0 + k * 100f, S, 0.35f, _mLine, 0.015f);
                        }
                        var used = new bool[5, 5];
                        float dC = Density(c, lx0 + S * 0.5f, lz0 + S * 0.5f);
                        bool town = kind == ChunkKind.Town;
                        // a school campus (2x2 blocks) in many chunks
                        if (_mr.Chance(town ? 0.2f : 0.32f))
                        {
                            int bi = _mr.Int(0, 4), bj = _mr.Int(0, 4);
                            for (int a = bi; a < bi + 2; a++) for (int b = bj; b < bj + 2; b++) used[a, b] = true;
                            BuildSchoolCampus(x0 + 100f * (bi + 1), z0 + 100f * (bj + 1), "School " + (Hash(c.Seed, rec.CI, rec.CJ, 9) % 900 + 100));
                            yield return null;
                        }
                        if (!town && _mr.Chance(0.08f))
                        {
                            int bi = _mr.Int(0, 4), bj = _mr.Int(0, 4);
                            if (!used[bi, bj] && !used[bi + 1, bj] && !used[bi, bj + 1] && !used[bi + 1, bj + 1])
                            {
                                for (int a = bi; a < bi + 2; a++) for (int b = bj; b < bj + 2; b++) used[a, b] = true;
                                BuildMall(x0 + 100f * (bi + 1), z0 + 100f * (bj + 1));
                                yield return null;
                            }
                        }
                        int towers = town ? 0 : (dC > 0.6f ? 2 : (dC > 0.35f ? 1 : 0));
                        for (int t = 0; t < towers; t++)
                        {
                            int bi = _mr.Int(0, 5), bj = _mr.Int(0, 5);
                            if (used[bi, bj]) continue;
                            used[bi, bj] = true;
                            float cx = x0 + 50f + 100f * bi, cz = z0 + 50f + 100f * bj;
                            _mb.Patch(cx, cz, BlockSize + 4f, BlockSize + 4f, _mSidewalk, 0.02f);
                            Color col = MegaPalette[_mr.Int(0, MegaPalette.Length)];
                            EnterBuilding(cx, cz, 50f, 40f, _mr.Int(c.Style == 2 ? 5 : 3, c.Style == 2 ? 9 : 6), 4.5f, col, "Tower", false, true);
                            yield return null;
                        }
                        for (int bi = 0; bi < 5; bi++)
                        {
                            for (int bj = 0; bj < 5; bj++)
                            {
                                if (used[bi, bj]) continue;
                                float cx = x0 + 50f + 100f * bi, cz = z0 + 50f + 100f * bj;
                                float d = Density(c, lx0 + 50f + 100f * bi, lz0 + 50f + 100f * bj);
                                _mb.Patch(cx, cz, BlockSize + 4f, BlockSize + 4f, _mSidewalk, 0.02f);
                                float bh = BlockHeight(c, kind, lx0 + 50f + 100f * bi, lz0 + 50f + 100f * bj, bi, bj, rec.CI, rec.CJ);
                                if (town || d < 0.4f) Houses(cx, cz);
                                else if (d < 0.7f) Apartments(cx, cz, bh);
                                else Downtown(cx, cz, bh, c.Style);
                                if (_mr.Chance(0.5f)) _spots.Add(new Vector3(cx + (_mr.Chance(0.5f) ? -1 : 1) * 42f, 0f, cz + (_mr.Chance(0.5f) ? -1 : 1) * 42f));
                            }
                            yield return null;
                        }
                        StreetLife(x0, z0, S, town ? 30 : 70);
                        break;
                    }
            }
            yield return null;

            var meshes = _mb.Flush(true);
            rec.Meshes.AddRange(meshes);
            foreach (var p in _spots) rec.Items.Add(MegaItems.Spawn(MegaItems.RandomType(), p, true));
            _spots.Clear();
            _buildRoot = null; _rec = null; _mb = null;
            rec.Done = true;
        }

        // ---------------------------------------------------------------- districts

        private void Downtown(float cx, float cz, float maxH, int style)
        {
            for (int qx = 0; qx < 2; qx++)
                for (int qz = 0; qz < 2; qz++)
                {
                    float lx = cx + (qx == 0 ? -20f : 20f), lz = cz + (qz == 0 ? -20f : 20f);
                    float sx = _mr.Range(26f, 34f), sz = _mr.Range(26f, 34f), h = _mr.Range(maxH * 0.45f, maxH);
                    bool glass = _mr.Chance(style == 2 ? 0.6f : 0.35f);
                    Color col = glass ? GlassPalette[_mr.Int(0, GlassPalette.Length)] : MegaPalette[_mr.Int(0, MegaPalette.Length)];
                    var side = Windowed(col);
                    _mb.Box(lx, 0f, lz, sx, h, sz, side, _mRoof);
                    if (_mr.Chance(0.45f)) _mb.Box(lx, h, lz, sx * 0.55f, _mr.Range(6f, 22f), sz * 0.55f, Windowed(col * 0.9f), _mRoof);
                    if (_mr.Chance(0.3f)) _mb.Box(lx + sx * 0.2f, h, lz, 1.2f, _mr.Range(8f, 18f), 1.2f, _mMetal, _mMetal, false);   // antenna
                }
        }

        private void Apartments(float cx, float cz, float maxH)
        {
            if (_mr.Chance(0.1f)) { MiniPark(cx, cz); return; }
            for (int qx = 0; qx < 2; qx++)
                for (int qz = 0; qz < 2; qz++)
                {
                    float lx = cx + (qx == 0 ? -20f : 20f), lz = cz + (qz == 0 ? -20f : 20f);
                    float sx = _mr.Range(22f, 32f), sz = _mr.Range(22f, 32f), h = _mr.Range(maxH * 0.5f, maxH);
                    Color col = MegaPalette[_mr.Int(0, MegaPalette.Length)];
                    _mb.Box(lx, 0f, lz, sx, h, sz, Windowed(col), _mRoof);
                    _mb.Box(lx, h, lz, sx + 0.6f, 0.8f, sz + 0.6f, _mRoof, _mRoof);
                }
        }

        private void Houses(float cx, float cz)
        {
            if (_mr.Chance(0.1f)) { MiniPark(cx, cz); return; }
            _mb.Patch(cx, cz, BlockSize - 2f, BlockSize - 2f, _mGrass, 0.025f);
            for (int qx = 0; qx < 3; qx++)
                for (int qz = 0; qz < 3; qz++)
                {
                    float lx = cx + (qx - 1) * 26f, lz = cz + (qz - 1) * 26f;
                    if (qx == 1 && qz == 1) { Tree(lx, lz); Tree(lx + 6f, lz - 5f); continue; }
                    Color col = MegaPalette[_mr.Int(0, MegaPalette.Length)];
                    float w = _mr.Range(11f, 15f), d = _mr.Range(10f, 13f), h = _mr.Range(5f, 7.5f);
                    _mb.Box(lx, 0f, lz, w, h, d, Windowed(col), _mRoofRed);
                    _mb.Box(lx, h, lz, w * 0.75f, 1.4f, d * 0.75f, _mRoofRed, _mRoofRed);
                    _mb.Box(lx, h + 1.4f, lz, w * 0.45f, 1.0f, d * 0.45f, _mRoofRed, _mRoofRed);
                    if (_mr.Chance(0.5f)) Tree(lx + w * 0.5f + 3f, lz + d * 0.5f + 2f);
                }
        }

        private void MiniPark(float cx, float cz)
        {
            _mb.Patch(cx, cz, BlockSize - 2f, BlockSize - 2f, _mGrass, 0.025f);
            _mb.Patch(cx, cz, 4f, BlockSize - 2f, _mPath, 0.03f);
            for (int t = 0; t < 10; t++) Tree(cx + _mr.Range(-36f, 36f), cz + _mr.Range(-36f, 36f));
            if (_mr.Chance(0.5f)) MakeJumpPad(new Vector3(cx + 10f, 0f, cz));
            _spots.Add(new Vector3(cx + 6f, 0f, cz));
        }

        private void Tree(float x, float z)
        {
            float s = _mr.Range(0.9f, 1.5f);
            _mb.Box(x, 0f, z, 0.5f * s, 2.4f * s, 0.5f * s, _mTrunk, _mTrunk);
            _mb.Box(x, 2.2f * s, z, 3.0f * s, 2.2f * s, 3.0f * s, _mLeaf, _mLeaf, false);
            _mb.Box(x, 4.2f * s, z, 2.0f * s, 1.4f * s, 2.0f * s, _mLeaf, _mLeaf, false);
        }

        private void Pine(float x, float z)
        {
            float s = _mr.Range(0.9f, 1.8f);
            _mb.Box(x, 0f, z, 0.45f * s, 2f * s, 0.45f * s, _mTrunk, _mTrunk);
            _mb.Box(x, 1.6f * s, z, 3.4f * s, 1.8f * s, 3.4f * s, _mPine, _mPine, false);
            _mb.Box(x, 3.2f * s, z, 2.4f * s, 1.8f * s, 2.4f * s, _mPine, _mPine, false);
            _mb.Box(x, 4.8f * s, z, 1.3f * s, 1.8f * s, 1.3f * s, _mPine, _mPine, false);
        }

        private void LakeChunk(float x0, float z0, float S)
        {
            // sandy beach ring, a lake bed you can wade on, a pier and trees round the shore
            _mb.Patch(x0 + S * 0.5f, z0 + 25f, S - 40f, 12f, _mSand, 0.02f);
            _mb.Patch(x0 + S * 0.5f, z0 + S - 25f, S - 40f, 12f, _mSand, 0.02f);
            _mb.Patch(x0 + 25f, z0 + S * 0.5f, 12f, S - 40f, _mSand, 0.02f);
            _mb.Patch(x0 + S - 25f, z0 + S * 0.5f, 12f, S - 40f, _mSand, 0.02f);
            _mb.Box(x0 + S * 0.5f, -4f, z0 + S * 0.5f, S - 60f, 1.4f, S - 60f, _mSand, _mSand, true, false, 4f, 3.5f, false);
            _rec.Grid.BlockRect(x0 + S * 0.5f, z0 + S * 0.5f, S - 60f, S - 60f, 0f);
            float px = x0 + S * 0.5f + _mr.Range(-120f, 120f);
            _mb.Box(px, -2.6f, z0 + 30f + 30f, 4f, 2.75f, 60f, _mTrunk, Tint(new Color(0.6f, 0.45f, 0.3f)), true, false, 4f, 3.5f, false);
            for (int t = 0; t < 30; t++)
            {
                float a = _mr.Range(0f, Mathf.PI * 2f);
                float side = _mr.Int(0, 4);
                float u = _mr.Range(40f, S - 40f);
                float x = side == 0 ? x0 + u : side == 1 ? x0 + u : side == 2 ? x0 + 18f : x0 + S - 18f;
                float z = side == 0 ? z0 + 16f : side == 1 ? z0 + S - 16f : z0 + u;
                if (side >= 2) z = z0 + u;
                Tree(x, z);
            }
            _spots.Add(new Vector3(px, 0.15f, z0 + 85f));
            _spots.Add(new Vector3(x0 + 25f, 0f, z0 + S * 0.5f));
        }

        private void ParkChunk(float x0, float z0, float S)
        {
            float cx = x0 + S * 0.5f, cz = z0 + S * 0.5f;
            _mb.Patch(cx, cz, S - 20f, 8f, _mPath, 0.03f);
            _mb.Patch(cx, cz, 8f, S - 20f, _mPath, 0.03f);
            _mb.Patch(cx, cz, 40f, 40f, _mPath, 0.032f);
            _mb.Box(cx, 0f, cz, 8f, 0.8f, 8f, _mConcrete, Tint(new Color(0.3f, 0.55f, 0.75f), 0.9f));
            _mb.Box(cx, 0.8f, cz, 1.2f, 2.5f, 1.2f, _mConcrete, _mConcrete);
            for (int t = 0; t < 110; t++)
            {
                float x = _mr.Range(x0 + 14f, x0 + S - 14f), z = _mr.Range(z0 + 14f, z0 + S - 14f);
                if (Mathf.Abs(x - cx) < 8f || Mathf.Abs(z - cz) < 8f) continue;
                if (_mr.Chance(0.3f)) Pine(x, z); else Tree(x, z);
            }
            for (int k = 0; k < 6; k++) MakeJumpPad(new Vector3(cx + _mr.Range(-180f, 180f), 0f, cz + _mr.Range(-180f, 180f)));
            MakeVending(new Vector3(cx + 14f, 0f, cz - 16f), 0f);
            MakeVending(new Vector3(cx - 14f, 0f, cz + 16f), 180f);
            // playground
            float gx = cx + 90f, gz = cz + 90f;
            _mb.Patch(gx, gz, 30f, 30f, _mSand, 0.03f);
            _mb.Box(gx - 6f, 0f, gz, 1f, 3f, 1f, _mBark, _mBark);
            _mb.Box(gx + 6f, 0f, gz, 1f, 3f, 1f, _mBark, _mBark);
            _mb.Box(gx, 3f, gz, 13f, 0.5f, 1f, _mBark, _mBark);
            _mb.Box(gx, 0f, gz - 8f, 4f, 1.5f, 4f, Tint(new Color(0.95f, 0.3f, 0.3f)), Tint(new Color(0.95f, 0.8f, 0.2f)));
            for (int k = 0; k < 12; k++) _spots.Add(new Vector3(cx + _mr.Range(-200f, 200f), 0f, cz + _mr.Range(-200f, 200f)));
        }

        private void ForestChunk(float x0, float z0, float S)
        {
            float cx = x0 + S * 0.5f, cz = z0 + S * 0.5f;
            _mb.Patch(cx, cz, 6f, S, _mPath, 0.03f);
            for (int t = 0; t < 260; t++)
            {
                float x = _mr.Range(x0 + 8f, x0 + S - 8f), z = _mr.Range(z0 + 8f, z0 + S - 8f);
                if (Mathf.Abs(x - cx) < 5f) continue;
                if (_mr.Chance(0.7f)) Pine(x, z); else Tree(x, z);
            }
            // a cabin
            _mb.Box(cx + 30f, 0f, cz, 10f, 4f, 8f, _mBark, _mRoofRed);
            for (int k = 0; k < 5; k++) _spots.Add(new Vector3(cx + _mr.Range(-2f, 2f), 0f, cz + _mr.Range(-220f, 220f)));
        }

        private void FarmChunk(float x0, float z0, float S)
        {
            for (int i = 0; i < 4; i++)
                for (int j = 0; j < 4; j++)
                {
                    float cx = x0 + 62.5f + i * 125f, cz = z0 + 62.5f + j * 125f;
                    _mb.Patch(cx, cz, 115f, 115f, _mr.Chance(0.5f) ? _mCrop : _mField, 0.025f);
                }
            float bx = x0 + S * 0.5f + _mr.Range(-100f, 100f), bz = z0 + S * 0.5f + _mr.Range(-100f, 100f);
            _mb.Box(bx, 0f, bz, 18f, 8f, 12f, _mBark, _mRoofRed);
            _mb.Box(bx + 14f, 0f, bz, 5f, 14f, 5f, _mMetal, _mMetal);
            for (int k = 0; k < 20; k++) Tree(x0 + _mr.Range(5f, S - 5f), z0 + (_mr.Chance(0.5f) ? 4f : S - 4f));
            for (int k = 0; k < 4; k++) _spots.Add(new Vector3(bx + _mr.Range(-30f, 30f), 0f, bz - 12f));
        }

        private void AirportChunk(MegaWorld.Country c, ChunkRecord rec, float lx0, float lz0, float x0, float z0, float S)
        {
            // which airport, and where its runway is in this chunk
            Vector2 ap = c.Airports[0]; float bd = float.MaxValue;
            foreach (var a in c.Airports) { float d = Mathf.Abs(lx0 + 250f - a.x) + Mathf.Abs(lz0 + 250f - a.y); if (d < bd) { bd = d; ap = a; } }
            float cxLocal = x0 + (ap.x - lx0), runwayZ = z0 + (ap.y - lz0);   // scene coords of the airport centre
            _mb.Patch(x0 + S * 0.5f, z0 + S * 0.5f, S, S - 2f, _mTarmac, 0.02f);
            float rx0 = Mathf.Max(x0, cxLocal - 1400f), rx1 = Mathf.Min(x0 + S, cxLocal + 1400f);
            if (rx1 > rx0)
            {
                _mb.Patch((rx0 + rx1) * 0.5f, runwayZ, rx1 - rx0, 60f, _mAsphalt, 0.03f);
                for (float x = rx0; x < rx1; x += 40f) _mb.Patch(x + 10f, runwayZ, 20f, 1.2f, _mWhite, 0.035f);
                _mb.Patch((rx0 + rx1) * 0.5f, runwayZ - 28f, rx1 - rx0, 0.8f, _mWhite, 0.035f);
                _mb.Patch((rx0 + rx1) * 0.5f, runwayZ + 28f, rx1 - rx0, 0.8f, _mWhite, 0.035f);
                _mb.Patch((rx0 + rx1) * 0.5f, runwayZ + 120f, rx1 - rx0, 25f, _mAsphalt, 0.03f);   // taxiway
                for (float x = rx0 + 25f; x < rx1; x += 50f)
                {
                    _mb.Box(x, 0f, runwayZ - 34f, 0.5f, 0.5f, 0.5f, _mWhite, Tint(new Color(1f, 0.8f, 0.3f), 0.9f), false);
                    _mb.Box(x, 0f, runwayZ + 34f, 0.5f, 0.5f, 0.5f, _mWhite, Tint(new Color(1f, 0.8f, 0.3f), 0.9f), false);
                }
            }
            bool centre = cxLocal >= x0 && cxLocal < x0 + S;
            if (centre)
            {
                // terminal (you can go in), control tower, hangars and plane stands
                float tx = cxLocal + 250f, tz = runwayZ + 190f;
                EnterBuilding(tx, tz, 140f, 44f, 2, 6f, new Color(0.85f, 0.88f, 0.92f), "Airport terminal", false, true, new Rect(tx - 20f, tz - 12f, 40f, 20f));
                _mb.Box(cxLocal + 420f, 0f, runwayZ + 175f, 8f, 34f, 8f, _mConcrete, _mConcrete);
                _mb.Box(cxLocal + 420f, 34f, runwayZ + 175f, 14f, 6f, 14f, _mGlass, _mRoof);
                MegaWorld.Spawn = new Vector3(tx, 1.1f, tz + 32f);
                for (int k = 0; k < 4; k++)
                {
                    var stand = new Vector3(cxLocal + 60f + k * 40f, 0f, runwayZ + 150f);
                    rec.PlaneStands.Add(new Pose(stand, Quaternion.Euler(0f, 180f, 0f)));
                }
                for (int k = 0; k < 6; k++) _spots.Add(new Vector3(tx + _mr.Range(-60f, 60f), 0f, tz + 30f));
            }
            else if (_mr.Chance(0.6f))
            {
                float hx = x0 + _mr.Range(100f, 400f);
                _mb.Box(hx, 0f, runwayZ + 200f, 60f, 14f, 40f, _mMetal, _mMetal);
                _spots.Add(new Vector3(hx, 0f, runwayZ + 175f));
            }
        }

        private void BuildSchoolCampus(float cx, float cz, string name)
        {
            float H = 85f;
            _mb.Patch(cx, cz, H * 2f, H * 2f, _mPath, 0.025f);
            var fence = Tint(new Color(0.6f, 0.62f, 0.66f));
            FenceX(cz - H, cx - H, cx + H, fence); FenceX(cz + H, cx - H, cx + H, fence);
            FenceZ(cx - H, cz - H, cz + H, fence); FenceZ(cx + H, cz - H, cz + H, fence);
            Color brick = new Color(0.72f, 0.4f, 0.32f);
            EnterBuilding(cx, cz + 40f, 110f, 36f, 2, 4.5f, brick, name, true, false);
            EnterBuilding(cx + 45f, cz - 40f, 54f, 42f, 1, 7f, new Color(0.65f, 0.38f, 0.3f), name + " gym", false, true);
            _mb.Patch(cx - 35f, cz - 40f, 80f, 60f, _mField, 0.03f);
            _mb.Box(cx - 74f, 0f, cz - 40f, 0.4f, 2.6f, 7f, _mWall, _mWall);
            _mb.Box(cx + 4f, 0f, cz - 40f, 0.4f, 2.6f, 7f, _mWall, _mWall);
            _mb.Patch(cx + 45f, cz - 40f, 30f, 18f, _mCourt, 0.035f);
            for (int k = 0; k < 4; k++) _spots.Add(new Vector3(cx + _mr.Range(-70f, 70f), 0f, cz + _mr.Range(-70f, 5f)));
        }

        private void FenceX(float z, float x0, float x1, Material m)
        {
            float mid = (x0 + x1) * 0.5f;
            _mb.Box((x0 + mid - 5f) * 0.5f, 0f, z, (mid - 5f) - x0, 2.2f, 0.3f, m, m);
            _mb.Box((mid + 5f + x1) * 0.5f, 0f, z, x1 - (mid + 5f), 2.2f, 0.3f, m, m);
        }

        private void FenceZ(float x, float z0, float z1, Material m)
        {
            float mid = (z0 + z1) * 0.5f;
            _mb.Box(x, 0f, (z0 + mid - 5f) * 0.5f, 0.3f, 2.2f, (mid - 5f) - z0, m, m);
            _mb.Box(x, 0f, (mid + 5f + z1) * 0.5f, 0.3f, 2.2f, z1 - (mid + 5f), m, m);
        }

        private void BuildMall(float cx, float cz)
        {
            _mb.Patch(cx, cz, 170f, 170f, _mSidewalk, 0.025f);
            var b = EnterBuilding(cx, cz, 140f, 100f, 2, 6f, new Color(0.9f, 0.88f, 0.82f), "Mall", false, true, new Rect(cx - 18f, cz - 14f, 36f, 26f));
            for (int k = 0; k < 6; k++)
            {
                Color sc = MegaPalette[k % MegaPalette.Length];
                _mb.Box(cx - 52f + k * 20f, 0f, cz - 42f, 14f, 3.2f, 6f, Tint(sc), _mRoof);
                _spots.Add(new Vector3(cx - 52f + k * 20f, 0f, cz - 36f));
                _spots.Add(new Vector3(cx - 52f + k * 20f, 6f, cz - 36f));
            }
            float rise = 6f, run = 10.4f, len = Mathf.Sqrt(rise * rise + run * run), ang = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            for (int e = 0; e < 2; e++)
            {
                float ex = cx - 6f + e * 4.2f, ez = cz - 14f + run * 0.5f;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = e == 0 ? "EscalatorUp" : "EscalatorDown";
                go.transform.SetParent(BuildRoot(), false);
                go.transform.position = new Vector3(ex, rise * 0.5f - 0.1f, ez);
                go.transform.rotation = Quaternion.Euler(ang, 0f, 0f);
                go.transform.localScale = new Vector3(2.4f, 0.25f, len);
                go.GetComponent<Renderer>().sharedMaterial = _mMetal;
                if (go.GetComponent<Collider>() == null) go.AddComponent<BoxCollider>();
                var esc = go.AddComponent<Escalator>();
                esc.HalfSize = new Vector3(1.2f, 0.2f, len * 0.5f);
                esc.Direction = e == 0 ? -go.transform.forward : go.transform.forward;
                b.Escalators.Add(new Vector3(ex, 0f, ez));
            }
            _spots.Add(new Vector3(cx, 6f, cz + 20f));
            _spots.Add(new Vector3(cx + 30f, 0f, cz + 20f));
        }

        private void StreetLife(float x0, float z0, float S, int cars)
        {
            for (int k = 0; k < cars; k++)
            {
                bool alongX = _mr.Chance(0.5f);
                float street = (alongX ? z0 : x0) + 100f * _mr.Int(1, 5);
                float along = (alongX ? x0 : z0) + _mr.Range(5f, S - 5f);
                float off = _mr.Chance(0.5f) ? -6.5f : 6.5f;
                float x = alongX ? along : street + off, z = alongX ? street + off : along;
                Color cc = Color.HSVToRGB(_mr.Value(), _mr.Range(0.2f, 0.7f), _mr.Range(0.4f, 0.95f));
                var m = Tint(new Color(Mathf.Round(cc.r * 4f) / 4f, Mathf.Round(cc.g * 4f) / 4f, Mathf.Round(cc.b * 4f) / 4f), 0.7f);
                _mb.Box(x, 0.2f, z, alongX ? 4.4f : 1.9f, 1.1f, alongX ? 1.9f : 4.4f, m, m);
                _mb.Box(x, 1.3f, z, alongX ? 2.4f : 1.6f, 0.65f, alongX ? 1.6f : 2.4f, _mGlass, m, false);
                if (k % 8 == 0) _spots.Add(new Vector3(x + (alongX ? 0f : 2.5f), 0f, z + (alongX ? 2.5f : 0f)));
            }
            for (int i = 0; i < 5; i++)
                for (int j = 0; j < 5; j++)
                {
                    float x = x0 + 100f * i + 92f, z = z0 + 100f * j + 92f;
                    _mb.Box(x, 0f, z, 0.25f, 6f, 0.25f, _mMetal, _mMetal);
                    _mb.Box(x - 1f, 6f, z - 1f, 2.2f, 0.25f, 0.5f, _mMetal, Tint(new Color(1f, 0.95f, 0.8f), 0.9f), false);
                    if ((i + j) % 4 == 0) MakeVending(new Vector3(x - 2f, 0f, z - 2.5f), 225f);
                }
        }

        // ================================================================== the low-detail skyline

        /// <summary>A 2 km tile of simple boxes (one per block) for everything far away. Skips chunks built in detail.</summary>
        internal void BuildLodTile(int country, int ti, int tj, Transform root, List<Mesh> meshes, System.Func<int, int, bool> skip)
        {
            WorldMaterials();
            var c = MegaWorld.Countries[country];
            var mb = new MegaBatcher(root, null);
            int perTile = 4, maxC = Mathf.RoundToInt(c.Half * 2f / MegaWorld.ChunkSize);
            for (int a = 0; a < perTile; a++)
                for (int b = 0; b < perTile; b++)
                {
                    int ci = ti * perTile + a, cj = tj * perTile + b;
                    if (ci < 0 || cj < 0 || ci >= maxC || cj >= maxC) continue;
                    if (skip != null && skip(ci, cj)) continue;
                    var kind = KindOf(c, ci, cj);
                    float lx0 = -c.Half + ci * MegaWorld.ChunkSize, lz0 = -c.Half + cj * MegaWorld.ChunkSize;
                    Vector3 o = MegaWorld.ToLocal(c.CX + lx0, c.CZ + lz0);
                    if (kind == ChunkKind.Lake)
                    {
                        // only the beach ring
                        mb.Patch(o.x + 250f, o.z + 15f, 500f, 30f, _mSand, -0.05f);
                        mb.Patch(o.x + 250f, o.z + 485f, 500f, 30f, _mSand, -0.05f);
                        mb.Patch(o.x + 15f, o.z + 250f, 30f, 440f, _mSand, -0.05f);
                        mb.Patch(o.x + 485f, o.z + 250f, 30f, 440f, _mSand, -0.05f);
                        continue;
                    }
                    Material g = kind == ChunkKind.Forest || kind == ChunkKind.Park || kind == ChunkKind.Farm ? _mGrass : (kind == ChunkKind.Airport ? _mTarmac : _mLod);
                    mb.Patch(o.x + 250f, o.z + 250f, 500f, 500f, g, -0.05f);
                    if (ci == 0 || cj == 0 || ci == maxC - 1 || cj == maxC - 1)
                    {
                        if (ci == 0) mb.Box(o.x - 1f, 0f, o.z + 250f, 2f, 10f, 500f, _mConcrete, _mConcrete, false);
                        if (ci == maxC - 1) mb.Box(o.x + 501f, 0f, o.z + 250f, 2f, 10f, 500f, _mConcrete, _mConcrete, false);
                        if (cj == 0) mb.Box(o.x + 250f, 0f, o.z - 1f, 500f, 10f, 2f, _mConcrete, _mConcrete, false);
                        if (cj == maxC - 1) mb.Box(o.x + 250f, 0f, o.z + 501f, 500f, 10f, 2f, _mConcrete, _mConcrete, false);
                    }
                    if (kind == ChunkKind.Forest || kind == ChunkKind.Park)
                    {
                        for (int k = 0; k < 10; k++)
                        {
                            float h01 = Hash01(c.Seed, ci, cj, 50 + k), h2 = Hash01(c.Seed, ci, cj, 80 + k);
                            mb.Box(o.x + 40f + h01 * 420f, 0f, o.z + 40f + h2 * 420f, 40f, 8f, 40f, _mPine, _mPine, false);
                        }
                        continue;
                    }
                    if (kind == ChunkKind.Farm || kind == ChunkKind.Airport) continue;
                    for (int bi = 0; bi < 5; bi++)
                        for (int bj = 0; bj < 5; bj++)
                        {
                            float h = BlockHeight(c, kind, lx0 + 50f + 100f * bi, lz0 + 50f + 100f * bj, bi, bj, ci, cj);
                            if (h <= 0f) continue;
                            int col = (int)(Hash(c.Seed, ci * 5 + bi, cj * 5 + bj, 5) % (uint)_mLodWalls.Length);
                            mb.Box(o.x + 50f + 100f * bi, 0f, o.z + 50f + 100f * bj, h > 12f ? 60f : 74f, h, h > 12f ? 60f : 74f, h > 12f ? _mLodWalls[col] : _mRoofRed, _mLodRoof, false, false, 8f, 7f);
                        }
                }
            meshes.AddRange(mb.Flush(false));
        }

        // ---------------------------------------------------------------- interactive bits

        private void MakeJumpPad(Vector3 p)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "JumpPad";
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.transform.SetParent(BuildRoot(), false);
            go.transform.position = p + Vector3.up * 0.06f;
            go.transform.localScale = new Vector3(2.8f, 0.12f, 2.8f);
            go.GetComponent<Renderer>().sharedMaterial = Tint(new Color(0.2f, 0.85f, 0.95f), 0.6f);
            go.AddComponent<JumpPad>();
        }

        private void MakeVending(Vector3 p, float yaw)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Vending";
            go.transform.SetParent(BuildRoot(), false);
            go.transform.position = p + Vector3.up * 1f;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(1.2f, 2f, 0.9f);
            go.GetComponent<Renderer>().sharedMaterial = Tint(new Color(0.85f, 0.15f, 0.2f), 0.4f);
            if (go.GetComponent<Collider>() == null) go.AddComponent<BoxCollider>();
            go.AddComponent<VendingMachine>();
            if (_rec != null && _rec.Grid != null) _rec.Grid.BlockRect(p.x, p.z, 1.2f, 1.2f, 0.4f);
        }

        private void MakeLocker(Vector3 p, float yaw)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Locker";
            go.transform.SetParent(BuildRoot(), false);
            go.transform.position = p + Vector3.up * 1f;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(0.9f, 2f, 0.6f);
            go.GetComponent<Renderer>().sharedMaterial = Tint(new Color(0.25f, 0.45f, 0.75f), 0.4f);
            if (go.GetComponent<Collider>() == null) go.AddComponent<BoxCollider>();
            go.AddComponent<Locker>();
        }

        /// <summary>
        /// A building you can go inside: a door at the front (-z side), open floors, a stairwell along the left wall
        /// (stacked ramps) and an elevator in the back-right corner, up to the roof.
        /// </summary>
        private MegaWorld.Building EnterBuilding(float cx, float cz, float w, float d, int floors, float fh, Color col, string name, bool lockers, bool vending, Rect? atrium = null)
        {
            const float T = 0.5f;
            float L = cx - w * 0.5f, R = cx + w * 0.5f, F = cz - d * 0.5f, B = cz + d * 0.5f;
            var wallM = Windowed(col);
            var plain = Tint(col);
            for (int f = 0; f < floors; f++)
            {
                float y = f * fh;
                if (f == 0)
                {
                    float dw = 6f, dh = 3.4f;
                    _mb.Box((L + cx - dw * 0.5f) * 0.5f, y, F, (cx - dw * 0.5f) - L, fh, T, wallM, plain);
                    _mb.Box((cx + dw * 0.5f + R) * 0.5f, y, F, R - (cx + dw * 0.5f), fh, T, wallM, plain);
                    _mb.Box(cx, y + dh, F, dw, fh - dh, T, plain, plain);
                    // a back door too, so nobody gets trapped
                    _mb.Box((L + cx - dw * 0.5f) * 0.5f, y, B, (cx - dw * 0.5f) - L, fh, T, wallM, plain);
                    _mb.Box((cx + dw * 0.5f + R) * 0.5f, y, B, R - (cx + dw * 0.5f), fh, T, wallM, plain);
                    _mb.Box(cx, y + dh, B, dw, fh - dh, T, plain, plain);
                }
                else
                {
                    _mb.Box(cx, y, F, w, fh, T, wallM, plain);
                    _mb.Box(cx, y, B, w, fh, T, wallM, plain);
                }
                _mb.Box(L, y, cz, T, fh, d, wallM, plain);
                _mb.Box(R, y, cz, T, fh, d, wallM, plain);
            }
            // floors and roof, with holes for the stairs, the elevator and (mall) the atrium
            float sx0 = L + T, sx1 = L + T + 3.4f, sz0 = B - T - 14f, sz1 = sz0 + 9.5f;
            float ex0 = R - T - 3.6f, ex1 = R - T, ez0 = B - T - 3.6f, ez1 = B - T;
            var holes = new List<Rect> { Rect.MinMaxRect(sx0, sz0, sx1, sz1), Rect.MinMaxRect(ex0, ez0, ex1, ez1) };
            if (atrium.HasValue) holes.Add(atrium.Value);
            for (int f = 1; f <= floors; f++)
                SlabWithHoles(Rect.MinMaxRect(L + T * 0.5f, F + T * 0.5f, R - T * 0.5f, B - T * 0.5f), holes, f * fh - 0.3f, 0.3f, f == floors ? _mRoof : _mFloor);
            // roof parapet
            float top = floors * fh;
            _mb.Box(cx, top, F, w, 1f, T, plain, plain); _mb.Box(cx, top, B, w, 1f, T, plain, plain);
            _mb.Box(L, top, cz, T, 1f, d, plain, plain); _mb.Box(R, top, cz, T, 1f, d, plain, plain);

            // stairs: one ramp per floor, stacked in the stairwell, rising toward the back
            float run = 9f, ang = Mathf.Atan2(fh, run) * Mathf.Rad2Deg, len = Mathf.Sqrt(fh * fh + run * run);
            float rampX = (sx0 + sx1) * 0.5f, rampZ = sz0 + 0.5f + run * 0.5f;   // ramp from z0+0.5 up to the landing at z0+9.5
            for (int f = 0; f < floors; f++)
            {
                var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                ramp.name = "Stairs";
                ramp.transform.SetParent(BuildRoot(), false);
                ramp.transform.position = new Vector3(rampX, f * fh + fh * 0.5f - 0.12f, rampZ);
                ramp.transform.rotation = Quaternion.Euler(-ang, 0f, 0f);
                ramp.transform.localScale = new Vector3(3.2f, 0.25f, len);
                ramp.GetComponent<Renderer>().sharedMaterial = _mConcrete;
                if (ramp.GetComponent<Collider>() == null) ramp.AddComponent<BoxCollider>();
            }
            // elevator
            var el = GameObject.CreatePrimitive(PrimitiveType.Cube);
            el.name = "Elevator";
            el.transform.SetParent(BuildRoot(), false);
            el.transform.position = new Vector3((ex0 + ex1) * 0.5f, -0.1f, (ez0 + ez1) * 0.5f);
            el.transform.localScale = new Vector3(3.3f, 0.3f, 3.3f);
            el.GetComponent<Renderer>().sharedMaterial = _mMetal;
            if (el.GetComponent<Collider>() == null) el.AddComponent<BoxCollider>();
            var elev = el.AddComponent<Elevator>();
            elev.FloorHeight = fh; elev.Floors = floors + 1; elev.Size = new Vector2(3.3f, 3.3f);

            if (lockers) for (int k = 0; k < 8; k++) MakeLocker(new Vector3(L + 12f + k * 1.1f, 0f, F + T + 0.4f), 0f);
            if (vending) MakeVending(new Vector3(cx + 5f, 0f, F + 2f), 0f);
            for (int f = 0; f <= floors; f++)
            {
                _spots.Add(new Vector3(cx + _mr.Range(-w * 0.25f, w * 0.25f), f * fh, cz + _mr.Range(-d * 0.2f, d * 0.2f)));
                if (w > 60f) _spots.Add(new Vector3(cx + _mr.Range(-w * 0.4f, w * 0.4f), f * fh, cz + _mr.Range(-d * 0.3f, d * 0.3f)));
            }
            var b = new MegaWorld.Building
            {
                Name = name,
                Area = Rect.MinMaxRect(L + T, F + T, R - T, B - T),
                Floors = floors + 1,
                FloorHeight = fh,
                Door = new Vector3(cx, 0f, F - 2.5f),
                StairBase = new Vector3(rampX, 0f, sz0 - 1.2f),
                StairTop = new Vector3(rampX, 0f, sz1 + 2f),
                RampX = rampX, RampZ0 = sz0 + 0.5f, RampZ1 = sz1, SideX = sx1 + 1.6f,
            };
            b.Holes.AddRange(holes);
            MegaWorld.Buildings.Add(b);
            if (_rec != null) _rec.Buildings.Add(b);
            return b;
        }

        /// <summary>A floor slab with rectangular holes cut out, built from as few boxes as possible.</summary>
        private void SlabWithHoles(Rect area, List<Rect> holes, float y0, float thick, Material m, bool gridBlock = true)
        {
            var xs = new List<float> { area.xMin, area.xMax };
            var zs = new List<float> { area.yMin, area.yMax };
            foreach (var h in holes)
            {
                if (h.xMin > area.xMin && h.xMin < area.xMax) xs.Add(h.xMin);
                if (h.xMax > area.xMin && h.xMax < area.xMax) xs.Add(h.xMax);
                if (h.yMin > area.yMin && h.yMin < area.yMax) zs.Add(h.yMin);
                if (h.yMax > area.yMin && h.yMax < area.yMax) zs.Add(h.yMax);
            }
            xs.Sort(); zs.Sort();
            for (int zi = 0; zi < zs.Count - 1; zi++)
            {
                float z0 = zs[zi], z1 = zs[zi + 1];
                if (z1 - z0 < 0.05f) continue;
                float runStart = float.NaN;
                for (int xi = 0; xi <= xs.Count - 1; xi++)
                {
                    bool solid = false;
                    if (xi < xs.Count - 1)
                    {
                        float x0 = xs[xi], x1 = xs[xi + 1];
                        if (x1 - x0 >= 0.05f)
                        {
                            var cell = new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);
                            solid = true;
                            foreach (var h in holes) if (h.Contains(cell)) { solid = false; break; }
                        }
                    }
                    if (solid && float.IsNaN(runStart)) runStart = xs[xi];
                    if (!solid && !float.IsNaN(runStart))
                    {
                        float runEnd = xs[xi];
                        _mb.Box((runStart + runEnd) * 0.5f, y0, (z0 + z1) * 0.5f, runEnd - runStart, thick, z1 - z0, m, m, true, true, 4f, 3.5f, gridBlock);
                        runStart = float.NaN;
                    }
                }
            }
        }
    }
}
