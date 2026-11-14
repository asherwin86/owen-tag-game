using System.Collections.Generic;
using UnityEngine;
using TagGame.Juice;

namespace TagGame.Mega
{
    public enum ItemType { Pistol, SMG, Shotgun, Rifle, Sniper, Medkit, Bandage, Shield, EnergyDrink, Sneakers, Ammo }

    /// <summary>How each gun behaves.</summary>
    public struct WeaponInfo
    {
        public string Name;
        public float Damage, Cooldown, Range, Spread;
        public int Mag, Pellets;
        public string Sound;
    }

    /// <summary>A thing lying on the floor that anyone (you or a bot) can walk over to pick up.</summary>
    public class FloorItem
    {
        public ItemType Type;
        public Vector3 Pos;
        public Transform Vis;
        public bool Respawn, Taken;
        public float BackAt;
        public long Cell;
    }

    /// <summary>
    /// Mega Mode items: guns, healing, armour, energy drinks, super sneakers and ammo. They lie on the floor all
    /// over the Mega City (and drop out of vending machines and lockers). Walk over one to pick it up.
    /// </summary>
    public static class MegaItems
    {
        public static readonly List<FloorItem> All = new List<FloorItem>();
        private static readonly Dictionary<long, List<FloorItem>> _hash = new Dictionary<long, List<FloorItem>>();
        private const float HashCell = 16f;
        private const float RespawnSeconds = 45f;
        private static Transform _root;
        private static Material _base;
        private static readonly Dictionary<int, Material> _mats = new Dictionary<int, Material>();

        public static bool IsGun(ItemType t) => t <= ItemType.Sniper;

        public static readonly WeaponInfo[] Weapons =
        {
            new WeaponInfo { Name = "Pistol",  Damage = 25f, Cooldown = 0.35f, Range = 40f,  Spread = 2f,  Mag = 15, Pellets = 1, Sound = "shot" },
            new WeaponInfo { Name = "SMG",     Damage = 12f, Cooldown = 0.10f, Range = 35f,  Spread = 5f,  Mag = 40, Pellets = 1, Sound = "smg" },
            new WeaponInfo { Name = "Shotgun", Damage = 12f, Cooldown = 0.90f, Range = 15f,  Spread = 9f,  Mag = 8,  Pellets = 6, Sound = "shotgun" },
            new WeaponInfo { Name = "Rifle",   Damage = 34f, Cooldown = 0.18f, Range = 60f,  Spread = 1.5f, Mag = 30, Pellets = 1, Sound = "rifle" },
            new WeaponInfo { Name = "Sniper",  Damage = 90f, Cooldown = 1.40f, Range = 120f, Spread = 0.2f, Mag = 5,  Pellets = 1, Sound = "sniper" },
        };

        public static WeaponInfo Weapon(ItemType t) => Weapons[Mathf.Clamp((int)t, 0, Weapons.Length - 1)];

        public static string NameOf(ItemType t)
        {
            switch (t)
            {
                case ItemType.Medkit: return "Medkit";
                case ItemType.Bandage: return "Bandage";
                case ItemType.Shield: return "Armour";
                case ItemType.EnergyDrink: return "Energy drink";
                case ItemType.Sneakers: return "Super sneakers";
                case ItemType.Ammo: return "Ammo box";
                default: return Weapon(t).Name;
            }
        }

        public static Color ColorOf(ItemType t)
        {
            switch (t)
            {
                case ItemType.Pistol: return new Color(0.75f, 0.75f, 0.8f);
                case ItemType.SMG: return new Color(0.4f, 0.75f, 1f);
                case ItemType.Shotgun: return new Color(0.95f, 0.55f, 0.2f);
                case ItemType.Rifle: return new Color(0.55f, 0.9f, 0.35f);
                case ItemType.Sniper: return new Color(0.8f, 0.4f, 1f);
                case ItemType.Medkit: return new Color(0.95f, 0.2f, 0.25f);
                case ItemType.Bandage: return new Color(1f, 0.85f, 0.8f);
                case ItemType.Shield: return new Color(0.25f, 0.5f, 1f);
                case ItemType.EnergyDrink: return new Color(0.3f, 1f, 0.45f);
                case ItemType.Sneakers: return new Color(1f, 0.6f, 0.1f);
                default: return new Color(1f, 0.85f, 0.2f);
            }
        }

        /// <summary>Random item, rarer things less often.</summary>
        public static ItemType RandomType()
        {
            float r = Random.value * 100f;
            if (r < 13f) return ItemType.Pistol;
            if (r < 22f) return ItemType.SMG;
            if (r < 29f) return ItemType.Shotgun;
            if (r < 35f) return ItemType.Rifle;
            if (r < 38f) return ItemType.Sniper;
            if (r < 47f) return ItemType.Medkit;
            if (r < 61f) return ItemType.Bandage;
            if (r < 70f) return ItemType.Shield;
            if (r < 81f) return ItemType.EnergyDrink;
            if (r < 87f) return ItemType.Sneakers;
            return ItemType.Ammo;
        }

        public static void Init(Material baseMat) { if (baseMat != null) _base = baseMat; }

        /// <summary>Puts an item on every item spot of the Mega City.</summary>
        public static void Populate()
        {
            foreach (var p in MegaWorld.ItemSpots) Spawn(RandomType(), p, true);
        }

        public static void ClearAll()
        {
            foreach (var it in All) if (it.Vis != null) Object.Destroy(it.Vis.gameObject);
            All.Clear();
            _hash.Clear();
            if (_root != null) Object.Destroy(_root.gameObject);
            _root = null;
        }

        private static long Key(int cx, int cz) => ((long)(cx + 100000) << 24) | (long)(cz + 100000);
        private static long KeyOf(Vector3 p) => Key(Mathf.FloorToInt(p.x / HashCell), Mathf.FloorToInt(p.z / HashCell));

        public static FloorItem Spawn(ItemType type, Vector3 pos, bool respawn)
        {
            if (_root == null) _root = new GameObject("MegaItems").transform;
            var it = new FloorItem { Type = type, Pos = pos, Respawn = respawn, Cell = KeyOf(pos) };
            it.Vis = MakeVisual(type, pos);
            All.Add(it);
            if (!_hash.TryGetValue(it.Cell, out var list)) { list = new List<FloorItem>(); _hash[it.Cell] = list; }
            list.Add(it);
            return it;
        }

        private static Material Mat(Color c)
        {
            int key = ((int)(c.r * 255f) << 16) | ((int)(c.g * 255f) << 8) | (int)(c.b * 255f);
            if (_mats.TryGetValue(key, out var m)) return m;
            m = _base != null ? new Material(_base) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.mainTexture = null;
            m.color = c;
            if (m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * 0.6f); }
            _mats[key] = m;
            return m;
        }

        private static Transform MakeVisual(ItemType t, Vector3 pos)
        {
            var go = new GameObject(NameOf(t));
            go.transform.SetParent(_root, false);
            go.transform.position = pos + Vector3.up * 0.55f;
            Color c = ColorOf(t);
            void Part(PrimitiveType pt, Vector3 lp, Vector3 ls, Color col)
            {
                var p = GameObject.CreatePrimitive(pt);
                var cl = p.GetComponent<Collider>(); if (cl != null) Object.Destroy(cl);
                p.transform.SetParent(go.transform, false);
                p.transform.localPosition = lp; p.transform.localScale = ls;
                var r = p.GetComponent<Renderer>();
                r.sharedMaterial = Mat(col);
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if (IsGun(t))
            {
                float len = t == ItemType.Sniper ? 1.3f : (t == ItemType.Rifle ? 1.0f : (t == ItemType.Pistol ? 0.45f : 0.8f));
                Part(PrimitiveType.Cube, new Vector3(0f, 0f, 0f), new Vector3(0.16f, 0.16f, len), new Color(0.15f, 0.15f, 0.17f));
                Part(PrimitiveType.Cube, new Vector3(0f, -0.15f, -len * 0.25f), new Vector3(0.12f, 0.28f, 0.14f), c);
                Part(PrimitiveType.Cube, new Vector3(0f, 0.1f, 0f), new Vector3(0.1f, 0.06f, len * 0.6f), c);
            }
            else if (t == ItemType.EnergyDrink) Part(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.22f, 0.22f, 0.22f), c);
            else if (t == ItemType.Sneakers)
            {
                Part(PrimitiveType.Cube, new Vector3(-0.14f, 0f, 0f), new Vector3(0.18f, 0.16f, 0.42f), c);
                Part(PrimitiveType.Cube, new Vector3(0.14f, 0f, 0f), new Vector3(0.18f, 0.16f, 0.42f), c);
            }
            else if (t == ItemType.Medkit)
            {
                Part(PrimitiveType.Cube, Vector3.zero, new Vector3(0.5f, 0.32f, 0.36f), Color.white);
                Part(PrimitiveType.Cube, new Vector3(0f, 0f, -0.185f), new Vector3(0.3f, 0.08f, 0.02f), c);
                Part(PrimitiveType.Cube, new Vector3(0f, 0f, -0.185f), new Vector3(0.08f, 0.24f, 0.02f), c);
            }
            else if (t == ItemType.Shield) Part(PrimitiveType.Cube, Vector3.zero, new Vector3(0.45f, 0.5f, 0.14f), c);
            else Part(PrimitiveType.Cube, Vector3.zero, new Vector3(0.36f, 0.26f, 0.3f), c);
            // a glowing ring on the floor so you can spot items from far
            var ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            var rc = ring.GetComponent<Collider>(); if (rc != null) Object.Destroy(rc);
            ring.transform.SetParent(go.transform, false);
            ring.transform.localPosition = new Vector3(0f, -0.5f, 0f);
            ring.transform.localScale = new Vector3(1.1f, 0.01f, 1.1f);
            var rr = ring.GetComponent<Renderer>();
            rr.sharedMaterial = Mat(c * 0.8f);
            rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return go.transform;
        }

        /// <summary>Takes the item off the floor. Map items come back after a while; dropped ones are gone.</summary>
        public static void Take(FloorItem it)
        {
            if (it.Taken) return;
            it.Taken = true;
            if (it.Respawn)
            {
                it.BackAt = Time.time + RespawnSeconds;
                if (it.Vis != null) it.Vis.gameObject.SetActive(false);
            }
            else
            {
                if (it.Vis != null) Object.Destroy(it.Vis.gameObject);
                All.Remove(it);
                if (_hash.TryGetValue(it.Cell, out var list)) list.Remove(it);
            }
        }

        /// <summary>Removes an item for good (its chunk of the world was unloaded).</summary>
        public static void Remove(FloorItem it)
        {
            if (it == null) return;
            if (it.Vis != null) Object.Destroy(it.Vis.gameObject);
            All.Remove(it);
            if (_hash.TryGetValue(it.Cell, out var list)) list.Remove(it);
        }

        /// <summary>The world's origin moved: move every item with it and re-bucket them.</summary>
        public static void Shift(Vector3 d)
        {
            _hash.Clear();
            foreach (var it in All)
            {
                it.Pos -= d;
                if (it.Vis != null) it.Vis.position -= d;
                it.Cell = KeyOf(it.Pos);
                if (!_hash.TryGetValue(it.Cell, out var list)) { list = new List<FloorItem>(); _hash[it.Cell] = list; }
                list.Add(it);
            }
        }

        /// <summary>Nearest item lying within r of p (same floor), or null.</summary>
        public static FloorItem Nearest(Vector3 p, float r, bool gunsOnly = false)
        {
            FloorItem best = null; float bd = r * r;
            int x0 = Mathf.FloorToInt((p.x - r) / HashCell), x1 = Mathf.FloorToInt((p.x + r) / HashCell);
            int z0 = Mathf.FloorToInt((p.z - r) / HashCell), z1 = Mathf.FloorToInt((p.z + r) / HashCell);
            for (int cx = x0; cx <= x1; cx++)
                for (int cz = z0; cz <= z1; cz++)
                {
                    if (!_hash.TryGetValue(Key(cx, cz), out var list)) continue;
                    foreach (var it in list)
                    {
                        if (it.Taken || (gunsOnly && !IsGun(it.Type))) continue;
                        if (Mathf.Abs(it.Pos.y - p.y) > 1.6f) continue;
                        float dx = it.Pos.x - p.x, dz = it.Pos.z - p.z, d = dx * dx + dz * dz;
                        if (d < bd) { bd = d; best = it; }
                    }
                }
            return best;
        }

        /// <summary>Spin and bob the items near the camera, and bring back taken ones.</summary>
        public static void Tick(Vector3 viewer)
        {
            float t = Time.time;
            for (int i = 0; i < All.Count; i++)
            {
                var it = All[i];
                if (it.Taken)
                {
                    if (it.Respawn && t >= it.BackAt)
                    {
                        it.Taken = false;
                        if (it.Vis != null) it.Vis.gameObject.SetActive(true);
                    }
                    continue;
                }
                if (it.Vis == null) continue;
                float dx = it.Pos.x - viewer.x, dz = it.Pos.z - viewer.z;
                if (dx * dx + dz * dz > 60f * 60f) continue;
                it.Vis.position = it.Pos + Vector3.up * (0.55f + Mathf.Sin(t * 2.2f + it.Pos.x) * 0.1f);
                it.Vis.rotation = Quaternion.Euler(0f, t * 90f + it.Pos.z * 10f, 0f);
            }
        }
    }
}
