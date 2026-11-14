using System.Collections.Generic;
using UnityEngine;
using TagGame.Mega;

namespace TagGame.Maps
{
    /// <summary>
    /// Mega City (Mega Mode only): about 2 km across. A street grid of 25 x 25 blocks with a central park,
    /// five school campuses, a two-floor mall with escalators, downtown towers, apartments and houses.
    /// About a dozen buildings can be entered and have stairs, an elevator and a rooftop.
    /// Built from boxes batched into big meshes so it still runs in a browser.
    /// </summary>
    public partial class MapManager
    {
        private const float MegaHalf = 1000f;
        private const float BlockPitch = 80f, BlockSize = 64f;
        private MegaBatcher _mb;
        private Rng _mr;
        private Material _mConcrete, _mFloor, _mRoof, _mGrass, _mPath, _mSidewalk, _mLine, _mTrunk, _mLeaf, _mGlass, _mWall, _mMetal, _mWater, _mField, _mCourt;
        private static readonly Color[] MegaPalette =
        {
            new Color(0.75f, 0.55f, 0.45f), new Color(0.6f, 0.65f, 0.75f), new Color(0.85f, 0.8f, 0.65f), new Color(0.55f, 0.7f, 0.6f),
            new Color(0.8f, 0.6f, 0.7f), new Color(0.7f, 0.7f, 0.72f), new Color(0.5f, 0.55f, 0.65f), new Color(0.82f, 0.7f, 0.5f),
        };

        private static float BlockC(int k) => -960f + BlockPitch * k;

        private void BuildMega()
        {
            MegaWorld.Clear();
            Grid = new MapGrid(MegaHalf + 2f, 2f);
            MakeGround(MegaHalf, new Color(0.26f, 0.26f, 0.29f));
            _mb = new MegaBatcher(Root(), Grid);
            _mr = new Rng(20261114);
            _mConcrete = Tint(new Color(0.62f, 0.62f, 0.6f));
            _mFloor = Tint(new Color(0.78f, 0.74f, 0.66f));
            _mRoof = Tint(new Color(0.42f, 0.42f, 0.45f));
            _mGrass = Tint(new Color(0.33f, 0.62f, 0.3f), 0.02f);
            _mPath = Tint(new Color(0.78f, 0.74f, 0.62f), 0.02f);
            _mSidewalk = Tint(new Color(0.66f, 0.66f, 0.68f), 0.02f);
            _mLine = Tint(new Color(0.92f, 0.8f, 0.2f), 0.02f);
            _mTrunk = Tint(new Color(0.4f, 0.28f, 0.18f));
            _mLeaf = Tint(new Color(0.22f, 0.55f, 0.24f));
            _mGlass = Tint(new Color(0.45f, 0.65f, 0.8f), 0.6f);
            _mWall = Tint(new Color(0.88f, 0.86f, 0.8f));
            _mMetal = Tint(new Color(0.55f, 0.58f, 0.62f), 0.5f);
            _mWater = Tint(new Color(0.25f, 0.5f, 0.75f), 0.7f);
            _mField = Tint(new Color(0.35f, 0.68f, 0.3f), 0.02f);
            _mCourt = Tint(new Color(0.82f, 0.55f, 0.32f), 0.05f);

            // outer wall
            var wallM = Tint(new Color(0.45f, 0.45f, 0.5f));
            _mb.Box(0f, 0f, MegaHalf, MegaHalf * 2f + 4f, 8f, 2f, wallM, wallM);
            _mb.Box(0f, 0f, -MegaHalf, MegaHalf * 2f + 4f, 8f, 2f, wallM, wallM);
            _mb.Box(MegaHalf, 0f, 0f, 2f, 8f, MegaHalf * 2f + 4f, wallM, wallM);
            _mb.Box(-MegaHalf, 0f, 0f, 2f, 8f, MegaHalf * 2f + 4f, wallM, wallM);

            // street centre lines
            for (int k = 0; k < 24; k++)
            {
                float c = BlockC(k) + BlockPitch * 0.5f;
                _mb.Patch(c, 0f, 0.35f, MegaHalf * 2f, _mLine, 0.015f);
                _mb.Patch(0f, c, MegaHalf * 2f, 0.35f, _mLine, 0.015f);
            }

            // reserved areas (block coordinates)
            var used = new bool[25, 25];
            void Reserve(int i, int j, int n) { for (int a = i; a < i + n; a++) for (int b = j; b < j + n; b++) used[a, b] = true; }
            Reserve(11, 11, 3);                                          // central park
            int[][] schools = { new[] { 3, 3 }, new[] { 3, 20 }, new[] { 20, 3 }, new[] { 20, 20 }, new[] { 5, 12 } };
            foreach (var s in schools) Reserve(s[0], s[1], 2);
            Reserve(16, 11, 2);                                          // mall
            int[][] towers = { new[] { 10, 9 }, new[] { 14, 9 }, new[] { 9, 13 }, new[] { 15, 14 }, new[] { 10, 15 }, new[] { 13, 16 }, new[] { 8, 10 }, new[] { 12, 8 }, new[] { 12, 17 }, new[] { 18, 15 } };
            foreach (var t in towers) Reserve(t[0], t[1], 1);

            BuildPark();
            for (int s = 0; s < schools.Length; s++) BuildSchoolCampus(BlockC(schools[s][0]) + BlockPitch * 0.5f, BlockC(schools[s][1]) + BlockPitch * 0.5f, s + 1);
            BuildMall(BlockC(16) + BlockPitch * 0.5f, BlockC(11) + BlockPitch * 0.5f);
            for (int t = 0; t < towers.Length; t++)
            {
                float cx = BlockC(towers[t][0]), cz = BlockC(towers[t][1]);
                _mb.Patch(cx, cz, BlockSize + 2f, BlockSize + 2f, _mSidewalk, 0.02f);
                Color col = MegaPalette[_mr.Int(0, MegaPalette.Length)];
                EnterBuilding(cx, cz, 44f, 34f, _mr.Int(3, 6), 4.5f, col, "Tower " + (t + 1), false, true);
            }

            for (int i = 0; i < 25; i++)
                for (int j = 0; j < 25; j++)
                {
                    if (used[i, j]) continue;
                    float cx = BlockC(i), cz = BlockC(j);
                    int d = Mathf.Max(Mathf.Abs(i - 12), Mathf.Abs(j - 12));
                    _mb.Patch(cx, cz, BlockSize + 2f, BlockSize + 2f, _mSidewalk, 0.02f);
                    if (d <= 4) Downtown(cx, cz);
                    else if (d <= 8) Apartments(cx, cz);
                    else Houses(cx, cz);
                    // items on the corners of the block
                    if (_mr.Chance(0.55f)) MegaWorld.ItemSpots.Add(new Vector3(cx + (_mr.Chance(0.5f) ? -1 : 1) * (BlockSize * 0.5f - 1f), 0f, cz + (_mr.Chance(0.5f) ? -1 : 1) * (BlockSize * 0.5f - 1f)));
                }

            StreetLife();
            _mb.Flush(true);
            MegaWorld.Spawn = new Vector3(0f, 1.1f, -6f);
        }

        // ---------------------------------------------------------------- districts

        private void Downtown(float cx, float cz)
        {
            for (int qx = 0; qx < 2; qx++)
                for (int qz = 0; qz < 2; qz++)
                {
                    float lx = cx + (qx == 0 ? -16f : 16f), lz = cz + (qz == 0 ? -16f : 16f);
                    float sx = _mr.Range(22f, 28f), sz = _mr.Range(22f, 28f), h = _mr.Range(24f, 70f);
                    Color c = MegaPalette[_mr.Int(0, MegaPalette.Length)];
                    _mb.Box(lx, 0f, lz, sx, h, sz, Windowed(c), _mRoof);
                    if (_mr.Chance(0.3f)) _mb.Box(lx, h, lz, sx * 0.5f, _mr.Range(6f, 16f), sz * 0.5f, Windowed(c * 0.9f), _mRoof);
                }
        }

        private void Apartments(float cx, float cz)
        {
            if (_mr.Chance(0.12f)) { MiniPark(cx, cz); return; }
            for (int qx = 0; qx < 2; qx++)
                for (int qz = 0; qz < 2; qz++)
                {
                    float lx = cx + (qx == 0 ? -16f : 16f), lz = cz + (qz == 0 ? -16f : 16f);
                    float sx = _mr.Range(18f, 26f), sz = _mr.Range(18f, 26f), h = _mr.Range(9f, 24f);
                    Color c = MegaPalette[_mr.Int(0, MegaPalette.Length)];
                    _mb.Box(lx, 0f, lz, sx, h, sz, Windowed(c), _mRoof);
                }
        }

        private void Houses(float cx, float cz)
        {
            if (_mr.Chance(0.1f)) { MiniPark(cx, cz); return; }
            _mb.Patch(cx, cz, BlockSize - 2f, BlockSize - 2f, _mGrass, 0.025f);
            for (int qx = 0; qx < 3; qx++)
                for (int qz = 0; qz < 3; qz++)
                {
                    if (qx == 1 && qz == 1) { Tree(cx, cz); continue; }
                    float lx = cx + (qx - 1) * 21f, lz = cz + (qz - 1) * 21f;
                    Color c = MegaPalette[_mr.Int(0, MegaPalette.Length)];
                    float w = _mr.Range(10f, 13f), d = _mr.Range(9f, 12f), h = _mr.Range(5f, 7.5f);
                    _mb.Box(lx, 0f, lz, w, h, d, Windowed(c), Tint(new Color(0.55f, 0.25f, 0.2f)));
                    _mb.Box(lx, h, lz, w * 0.7f, 1.6f, d * 0.7f, Tint(new Color(0.55f, 0.25f, 0.2f)), Tint(new Color(0.5f, 0.22f, 0.18f)));
                }
        }

        private void MiniPark(float cx, float cz)
        {
            _mb.Patch(cx, cz, BlockSize - 2f, BlockSize - 2f, _mGrass, 0.025f);
            _mb.Patch(cx, cz, 4f, BlockSize - 2f, _mPath, 0.03f);
            for (int t = 0; t < 8; t++) Tree(cx + _mr.Range(-27f, 27f), cz + _mr.Range(-27f, 27f));
            if (_mr.Chance(0.5f)) MakeJumpPad(new Vector3(cx, 0f, cz));
            MegaWorld.ItemSpots.Add(new Vector3(cx + 6f, 0f, cz));
        }

        private void Tree(float x, float z)
        {
            if (Mathf.Abs(x) < 14f && Mathf.Abs(z) < 14f) return;
            float s = _mr.Range(0.9f, 1.4f);
            _mb.Box(x, 0f, z, 0.5f * s, 2.4f * s, 0.5f * s, _mTrunk, _mTrunk);
            _mb.Box(x, 2.2f * s, z, 2.8f * s, 2.6f * s, 2.8f * s, _mLeaf, _mLeaf, false);
        }

        private void BuildPark()
        {
            float half = BlockPitch * 1.5f + 8f;
            _mb.Patch(0f, 0f, half * 2f, half * 2f, _mGrass, 0.025f);
            _mb.Patch(0f, 0f, 6f, half * 2f, _mPath, 0.03f);
            _mb.Patch(0f, 0f, half * 2f, 6f, _mPath, 0.03f);
            _mb.Patch(0f, 0f, 30f, 30f, _mPath, 0.032f);
            _mb.Patch(-70f, 60f, 60f, 40f, _mWater, 0.035f);                 // pond
            _mb.Box(0f, 0f, 9f, 5f, 0.8f, 5f, _mConcrete, _mWater);           // fountain
            for (int t = 0; t < 70; t++)
            {
                float x = _mr.Range(-half + 4f, half - 4f), z = _mr.Range(-half + 4f, half - 4f);
                if (Mathf.Abs(x) < 6f || Mathf.Abs(z) < 6f) continue;
                if (x > -102f && x < -38f && z > 38f && z < 82f) continue;
                Tree(x, z);
            }
            Vector3[] pads = { new Vector3(40f, 0f, 40f), new Vector3(-40f, 0f, -40f), new Vector3(40f, 0f, -40f), new Vector3(80f, 0f, 0f), new Vector3(-80f, 0f, -20f), new Vector3(0f, 0f, -80f) };
            foreach (var p in pads) MakeJumpPad(p);
            MakeVending(new Vector3(12f, 0f, -14f), 0f);
            MakeVending(new Vector3(-12f, 0f, 14f), 180f);
            for (int k = 0; k < 10; k++) MegaWorld.ItemSpots.Add(new Vector3(_mr.Range(-90f, 90f), 0f, _mr.Range(-90f, 90f)));
        }

        private void BuildSchoolCampus(float cx, float cz, int n)
        {
            float H = 70f;
            _mb.Patch(cx, cz, H * 2f, H * 2f, _mPath, 0.025f);
            var fence = Tint(new Color(0.6f, 0.62f, 0.66f));
            // fence with gates in the middle of each side
            FenceX(cz - H, cx - H, cx + H, fence); FenceX(cz + H, cx - H, cx + H, fence);
            FenceZ(cx - H, cz - H, cz + H, fence); FenceZ(cx + H, cz - H, cz + H, fence);
            Color brick = new Color(0.72f, 0.4f, 0.32f);
            EnterBuilding(cx, cz + 32f, 100f, 34f, 2, 4.5f, brick, "School " + n, true, false);       // main building
            EnterBuilding(cx + 38f, cz - 34f, 50f, 40f, 1, 7f, new Color(0.65f, 0.38f, 0.3f), "School " + n + " gym", false, true);
            _mb.Patch(cx - 30f, cz - 34f, 70f, 56f, _mField, 0.03f);              // sports field
            _mb.Box(cx - 64f, 0f, cz - 34f, 0.4f, 2.6f, 7f, _mWall, _mWall);
            _mb.Box(cx + 4f, 0f, cz - 34f, 0.4f, 2.6f, 7f, _mWall, _mWall);
            _mb.Patch(cx + 38f, cz - 34f, 30f, 18f, _mCourt, 0.035f);
            for (int k = 0; k < 4; k++) MegaWorld.ItemSpots.Add(new Vector3(cx + _mr.Range(-60f, 60f), 0f, cz + _mr.Range(-60f, 5f)));
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
            _mb.Patch(cx, cz, 150f, 150f, _mSidewalk, 0.025f);
            var b = EnterBuilding(cx, cz, 130f, 96f, 2, 6f, new Color(0.9f, 0.88f, 0.82f), "Mall", false, true, new Rect(cx - 18f, cz - 14f, 36f, 26f));
            // shops along the walls on the ground floor
            for (int k = 0; k < 6; k++)
            {
                Color sc = MegaPalette[k % MegaPalette.Length];
                _mb.Box(cx - 52f + k * 20f, 0f, cz - 40f, 14f, 3.2f, 6f, Tint(sc), _mRoof);
                MegaWorld.ItemSpots.Add(new Vector3(cx - 52f + k * 20f, 0f, cz - 34f));
                MegaWorld.ItemSpots.Add(new Vector3(cx - 52f + k * 20f, 6f, cz - 34f));
            }
            // escalators in the atrium: they rise toward the front, landing on the upper floor at the atrium's front edge
            float rise = 6f, run = 10.4f, len = Mathf.Sqrt(rise * rise + run * run), ang = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            for (int e = 0; e < 2; e++)
            {
                float ex = cx - 6f + e * 4.2f, ez = cz - 14f + run * 0.5f;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = e == 0 ? "EscalatorUp" : "EscalatorDown";
                go.transform.SetParent(Root(), false);
                go.transform.position = new Vector3(ex, rise * 0.5f - 0.1f, ez);
                go.transform.rotation = Quaternion.Euler(ang, 0f, 0f);       // the -z end is the high end
                go.transform.localScale = new Vector3(2.4f, 0.25f, len);
                go.GetComponent<Renderer>().sharedMaterial = _mMetal;
                if (go.GetComponent<Collider>() == null) go.AddComponent<BoxCollider>();
                var esc = go.AddComponent<Escalator>();
                esc.HalfSize = new Vector3(1.2f, 0.2f, len * 0.5f);
                esc.Direction = e == 0 ? -go.transform.forward : go.transform.forward;
                b.Escalators.Add(new Vector3(ex, 0f, ez));
            }
            MegaWorld.ItemSpots.Add(new Vector3(cx, 6f, cz + 20f));
            MegaWorld.ItemSpots.Add(new Vector3(cx + 30f, 0f, cz + 20f));
        }

        private void StreetLife()
        {
            // parked cars and street lamps along the streets, kept away from the park spawn
            for (int k = 0; k < 520; k++)
            {
                bool alongX = _mr.Chance(0.5f);
                float street = BlockC(_mr.Int(0, 24)) + BlockPitch * 0.5f;
                float along = _mr.Range(-980f, 980f);
                float off = _mr.Chance(0.5f) ? -5.5f : 5.5f;
                float x = alongX ? along : street + off, z = alongX ? street + off : along;
                if (Mathf.Abs(x) < 130f && Mathf.Abs(z) < 130f) continue;
                Color c = Color.HSVToRGB(_mr.Value(), 0.55f, 0.85f);
                var m = Tint(new Color(Mathf.Round(c.r * 4f) / 4f, Mathf.Round(c.g * 4f) / 4f, Mathf.Round(c.b * 4f) / 4f));
                _mb.Box(x, 0f, z, alongX ? 4.3f : 1.9f, 1.3f, alongX ? 1.9f : 4.3f, m, m);
                _mb.Box(x, 1.3f, z, alongX ? 2.4f : 1.6f, 0.6f, alongX ? 1.6f : 2.4f, _mGlass, m, false);
                if (k % 9 == 0) MegaWorld.ItemSpots.Add(new Vector3(x + (alongX ? 0f : 2f), 0f, z + (alongX ? 2f : 0f)));
            }
            for (int i = 0; i < 25; i++)
                for (int j = 0; j < 25; j++)
                {
                    float x = BlockC(i) + 34f, z = BlockC(j) + 34f;
                    _mb.Box(x, 0f, z, 0.25f, 5f, 0.25f, _mMetal, _mMetal);
                    if ((i + j) % 6 == 0 && Mathf.Abs(x) > 130f) MakeVending(new Vector3(x - 2f, 0f, z - 2f), 225f);
                }
        }

        // ---------------------------------------------------------------- interactive bits

        private void MakeJumpPad(Vector3 p)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "JumpPad";
            var col = go.GetComponent<Collider>(); if (col != null) Destroy(col);
            go.transform.SetParent(Root(), false);
            go.transform.position = p + Vector3.up * 0.06f;
            go.transform.localScale = new Vector3(2.8f, 0.12f, 2.8f);
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = Tint(new Color(0.2f, 0.85f, 0.95f), 0.6f);
            go.AddComponent<JumpPad>();
        }

        private void MakeVending(Vector3 p, float yaw)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Vending";
            go.transform.SetParent(Root(), false);
            go.transform.position = p + Vector3.up * 1f;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = new Vector3(1.2f, 2f, 0.9f);
            go.GetComponent<Renderer>().sharedMaterial = Tint(new Color(0.85f, 0.15f, 0.2f), 0.4f);
            if (go.GetComponent<Collider>() == null) go.AddComponent<BoxCollider>();
            go.AddComponent<VendingMachine>();
            if (Grid != null) Grid.BlockRect(p.x, p.z, 1.2f, 1.2f, 0.4f);
        }

        private void MakeLocker(Vector3 p, float yaw)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Locker";
            go.transform.SetParent(Root(), false);
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
                ramp.transform.SetParent(Root(), false);
                ramp.transform.position = new Vector3(rampX, f * fh + fh * 0.5f - 0.12f, rampZ);
                ramp.transform.rotation = Quaternion.Euler(-ang, 0f, 0f);
                ramp.transform.localScale = new Vector3(3.2f, 0.25f, len);
                ramp.GetComponent<Renderer>().sharedMaterial = _mConcrete;
                if (ramp.GetComponent<Collider>() == null) ramp.AddComponent<BoxCollider>();
            }
            // elevator
            var el = GameObject.CreatePrimitive(PrimitiveType.Cube);
            el.name = "Elevator";
            el.transform.SetParent(Root(), false);
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
                MegaWorld.ItemSpots.Add(new Vector3(cx + _mr.Range(-w * 0.25f, w * 0.25f), f * fh, cz + _mr.Range(-d * 0.2f, d * 0.2f)));
                if (w > 60f) MegaWorld.ItemSpots.Add(new Vector3(cx + _mr.Range(-w * 0.4f, w * 0.4f), f * fh, cz + _mr.Range(-d * 0.3f, d * 0.3f)));
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
            return b;
        }

        /// <summary>A floor slab with rectangular holes cut out, built from as few boxes as possible.</summary>
        private void SlabWithHoles(Rect area, List<Rect> holes, float y0, float thick, Material m)
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
                        _mb.Box((runStart + runEnd) * 0.5f, y0, (z0 + z1) * 0.5f, runEnd - runStart, thick, z1 - z0, m, m, true, true);
                        runStart = float.NaN;
                    }
                }
            }
        }
    }
}
