using System.Collections.Generic;
using UnityEngine;

namespace TagGame.Maps
{
    public partial class MapManager
    {
        // ======================================================================= SCHOOL (150 m)
        private void BuildSchool()
        {
            const float H = 75f;
            Grid = new MapGrid(H + 1f, 1f);
            MakeGround(H, new Color(0.45f, 0.65f, 0.35f));
            var rng = new Rng(7001);
            var used = new List<Vector4>(); // x0,z0,x1,z1 areas kept free of trees
            Color brick = new Color(0.72f, 0.4f, 0.32f), cream = new Color(0.9f, 0.86f, 0.7f), grey = new Color(0.6f, 0.6f, 0.65f);
            float wh = 5f;

            // perimeter fence
            Box(0, H, 2 * H + 2, 1f, 3f, grey); Box(0, -H, 2 * H + 2, 1f, 3f, grey);
            Box(H, 0, 1f, 2 * H + 2, 3f, grey); Box(-H, 0, 1f, 2 * H + 2, 3f, grey);

            // paths
            Patch(0, 0, 8, 150, new Color(0.78f, 0.74f, 0.65f)); Patch(0, 0, 150, 8, new Color(0.78f, 0.74f, 0.65f));
            Patch(0, 0, 62, 62, new Color(0.85f, 0.82f, 0.72f), 0.02f);

            // ---- main building (north): hallway + five classrooms
            used.Add(new Vector4(-60, 33, 60, 72));
            Box(0, 53f, 112, 36, 0.3f, cream, false, -0.0f);
            WallX(38f, -56, 56, 1f, wh, brick, new Vector2(0, 7), new Vector2(-32, 4), new Vector2(32, 4));
            WallX(68f, -56, 56, 1f, wh, brick);
            WallZ(-56f, 38, 68, 1f, wh, brick); WallZ(56f, 38, 68, 1f, wh, brick);
            WallX(48f, -56, 56, 0.6f, wh, cream, new Vector2(-44, 3.2f), new Vector2(-22, 3.2f), new Vector2(0, 3.2f), new Vector2(22, 3.2f), new Vector2(44, 3.2f));
            foreach (float dx in new[] { -33f, -11f, 11f, 33f }) WallZ(dx, 48, 68, 0.6f, wh, cream);
            for (int r = 0; r < 5; r++)
            {
                float cx = -44f + 22f * r;
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < 3; col++)
                        Box(cx - 6 + col * 6, 53f + row * 5f, 1.6f, 1.0f, 0.9f, new Color(0.55f, 0.4f, 0.25f));
                Box(cx, 66.5f, 5f, 1f, 1.1f, new Color(0.3f, 0.3f, 0.32f)); // teacher desk
            }
            // lockers in the hallway
            for (int i = 0; i < 6; i++) { Box(-50f + i * 20f, 39.3f, 3f, 0.8f, 2f, new Color(0.25f, 0.45f, 0.7f)); }

            // ---- gym (east)
            used.Add(new Vector4(36, -27, 72, 27));
            Patch(54, 0, 28, 44, new Color(0.85f, 0.65f, 0.35f), 0.02f);
            WallX(27f, 38, 70, 1f, wh + 2, brick); WallX(-27f, 38, 70, 1f, wh + 2, brick);
            WallZ(70f, -27, 27, 1f, wh + 2, brick);
            WallZ(38f, -27, 27, 1f, wh + 2, brick, new Vector2(0, 7), new Vector2(18, 4), new Vector2(-18, 4));
            Box(66f, 0, 0.6f, 4f, 3.2f, new Color(0.9f, 0.4f, 0.2f)); Box(42f, 0, 0.6f, 4f, 3.2f, new Color(0.9f, 0.4f, 0.2f));
            Box(54f, 22f, 20f, 2f, 1.2f, new Color(0.5f, 0.5f, 0.55f)); Box(54f, -22f, 20f, 2f, 1.2f, new Color(0.5f, 0.5f, 0.55f));
            Box(54f, 0, 2f, 2f, 1f, new Color(0.9f, 0.9f, 0.3f));

            // ---- cafeteria (west)
            used.Add(new Vector4(-72, -25, -36, 25));
            Patch(-54, 0, 32, 46, new Color(0.9f, 0.85f, 0.75f), 0.02f);
            WallX(25f, -70, -38, 1f, wh, cream); WallX(-25f, -70, -38, 1f, wh, cream);
            WallZ(-70f, -25, 25, 1f, wh, cream);
            WallZ(-38f, -25, 25, 1f, wh, cream, new Vector2(0, 7), new Vector2(15, 4), new Vector2(-15, 4));
            for (int i = 0; i < 3; i++) for (int j = 0; j < 4; j++)
                    Box(-63f + i * 9f, -17f + j * 11f, 6f, 1.6f, 0.9f, new Color(0.8f, 0.55f, 0.3f));
            Box(-66f, 0, 1.5f, 14f, 1.2f, new Color(0.7f, 0.7f, 0.75f)); // serving counter

            // ---- library (south-west)
            used.Add(new Vector4(-72, -72, -34, -38));
            WallX(-38f, -70, -36, 1f, wh, brick, new Vector2(-52, 5));
            WallX(-72f, -70, -36, 1f, wh, brick);
            WallZ(-70f, -72, -38, 1f, wh, brick); WallZ(-36f, -72, -38, 1f, wh, brick, new Vector2(-58, 4));
            for (int i = 0; i < 4; i++) Box(-62f + i * 8f, -55f, 1f, 18f, 2.6f, new Color(0.45f, 0.3f, 0.2f));
            Box(-52f, -42f, 6f, 1.2f, 1f, new Color(0.35f, 0.35f, 0.4f));

            // ---- parking lot (south centre)
            used.Add(new Vector4(-30, -72, 20, -42));
            Patch(-5, -58, 50, 30, new Color(0.3f, 0.3f, 0.33f), 0.02f);
            for (int i = 0; i < 6; i++)
            {
                Color cc = Color.HSVToRGB(rng.Value(), 0.6f, 0.8f);
                Box(-25f + i * 8f, -52f, 2f, 4.4f, 1.4f, cc);
                if (i % 2 == 0) Box(-25f + i * 8f, -66f, 2f, 4.4f, 1.4f, Color.HSVToRGB(rng.Value(), 0.6f, 0.8f));
            }

            // ---- field (south-east)
            used.Add(new Vector4(24, -72, 72, -36));
            Patch(48, -54, 46, 34, new Color(0.35f, 0.7f, 0.3f), 0.02f);
            Box(26f, -54f, 0.6f, 6f, 3f, Color.white); Box(70f, -54f, 0.6f, 6f, 3f, Color.white);
            for (int i = 0; i < 3; i++) Box(48f, -40f - i * 1.4f, 30f - i * 4f, 1.4f, 0.5f * (i + 1), new Color(0.6f, 0.6f, 0.65f));

            // ---- playground (north-west of plaza edge)
            used.Add(new Vector4(-30, 12, -12, 30));
            Box(-20f, 22f, 6f, 1f, 2.4f, new Color(0.9f, 0.3f, 0.3f)); Box(-20f, 20f, 1f, 4f, 0.9f, new Color(0.3f, 0.5f, 0.9f));

            // trees
            for (int t = 0; t < 70; t++)
            {
                float x = rng.Range(-72f, 72f), z = rng.Range(-72f, 72f);
                if (x * x + z * z < 32f * 32f) continue;
                if (Mathf.Abs(x) < 6f || Mathf.Abs(z) < 6f) continue;
                bool bad = false;
                for (int u = 0; u < used.Count; u++) { var a = used[u]; if (x > a.x - 2 && x < a.z + 2 && z > a.y - 2 && z < a.w + 2) { bad = true; break; } }
                if (bad) continue;
                Tree(x, z, rng.Range(0.9f, 1.4f), Color.Lerp(new Color(0.2f, 0.55f, 0.2f), new Color(0.35f, 0.7f, 0.25f), rng.Value()));
            }
        }

        // ======================================================================= CITY (250 m)
        private void BuildCity()
        {
            const float H = 125f;
            Grid = new MapGrid(H + 1f, 1f);
            MakeGround(H, new Color(0.27f, 0.27f, 0.3f));
            var rng = new Rng(9137);
            Color[] pal =
            {
                new Color(0.75f,0.55f,0.45f), new Color(0.6f,0.65f,0.75f), new Color(0.85f,0.8f,0.65f),
                new Color(0.55f,0.7f,0.6f), new Color(0.8f,0.6f,0.7f), new Color(0.7f,0.7f,0.72f)
            };
            // perimeter wall
            Color wallC = new Color(0.45f, 0.45f, 0.5f);
            Box(0, H, 2 * H + 2, 2f, 6f, wallC); Box(0, -H, 2 * H + 2, 2f, 6f, wallC);
            Box(H, 0, 2f, 2 * H + 2, 6f, wallC); Box(-H, 0, 2f, 2 * H + 2, 6f, wallC);
            // centre-lines on streets
            Color line = new Color(0.9f, 0.8f, 0.2f);
            foreach (float c in new[] { -100f, -75f, -25f, 25f, 75f, 100f })
            { }
            foreach (float c in new[] { -75f, -25f, 25f, 75f })
            {
                Patch(c, 0, 0.3f, 2 * H, line, 0.015f); Patch(0, c, 2 * H, 0.3f, line, 0.015f);
            }
            // plaza in the middle
            Patch(0, 0, 36, 36, new Color(0.7f, 0.68f, 0.6f), 0.02f);

            for (int bi = -2; bi <= 2; bi++)
                for (int bj = -2; bj <= 2; bj++)
                {
                    float cx = bi * 50f, cz = bj * 50f;
                    if (bi == 0 && bj == 0) continue;
                    float dist = Mathf.Sqrt(cx * cx + cz * cz);
                    Patch(cx, cz, 38f, 38f, new Color(0.68f, 0.68f, 0.7f), 0.02f);
                    float roll = rng.Value();
                    if (roll < 0.17f)
                    {
                        // park
                        Patch(cx, cz, 34f, 34f, new Color(0.3f, 0.65f, 0.3f), 0.03f);
                        for (int t = 0; t < 7; t++) Tree(cx + rng.Range(-14f, 14f), cz + rng.Range(-14f, 14f), rng.Range(0.9f, 1.4f), new Color(0.2f, 0.6f, 0.25f));
                        Box(cx, cz, 4f, 4f, 0.7f, new Color(0.6f, 0.6f, 0.65f)); // fountain base
                        continue;
                    }
                    bool landmark = roll > 0.93f && dist > 70f;
                    for (int qx = 0; qx < 2; qx++)
                        for (int qz = 0; qz < 2; qz++)
                        {
                            float lx = cx + (qx == 0 ? -9.5f : 9.5f), lz = cz + (qz == 0 ? -9.5f : 9.5f);
                            float sx = rng.Range(13f, 16f), sz = rng.Range(13f, 16f);
                            float hgt = dist < 80f ? rng.Range(6f, 12f) : rng.Range(8f, 24f);
                            if (landmark && qx == 0 && qz == 0) { hgt = 40f; sx = 15f; sz = 15f; }
                            Color col = pal[rng.Int(0, pal.Length)];
                            Box(lx, lz, sx, sz, hgt, col, true, 0f, Windowed(col), Tint(col * 0.6f));
                        }
                }

            // street furniture: parked cars and lamps, kept clear of the central plaza and spawn ring
            for (int i = 0; i < 90; i++)
            {
                bool alongX = rng.Chance(0.5f);
                float street = (rng.Int(-2, 2) + 0.5f) * 50f; // centre of one of the 4 inner streets
                float along = rng.Range(-115f, 115f);
                float off = rng.Chance(0.5f) ? -5f : 5f;
                float x = alongX ? along : street + off, z = alongX ? street + off : along;
                if (x * x + z * z < 34f * 34f) continue;
                Box(x, z, alongX ? 4.2f : 1.9f, alongX ? 1.9f : 4.2f, 1.4f, Color.HSVToRGB(rng.Value(), 0.55f, 0.85f));
            }
            for (int i = -2; i <= 2; i++)
                for (int j = -2; j <= 2; j++)
                {
                    float x = i * 50f + 20f, z = j * 50f + 20f;
                    if (x * x + z * z < 34f * 34f) continue;
                    Box(x, z, 0.3f, 0.3f, 5f, new Color(0.2f, 0.2f, 0.22f));
                }
        }
    }
}
