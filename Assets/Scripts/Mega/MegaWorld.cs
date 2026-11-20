using System.Collections.Generic;
using UnityEngine;
using TagGame.Gameplay;
using TagGame.Juice;

namespace TagGame.Mega
{
    /// <summary>Everything Mega Mode needs to know about the Mega City: buildings you can go inside, where items go, things to use.</summary>
    public static class MegaWorld
    {
        public class Building
        {
            public string Name;
            public Rect Area;            // inside area (x, z)
            public int Floors;
            public float FloorHeight;
            public Vector3 Door;         // just outside the front door, on the ground
            public Vector3 StairBase;    // bottom of the stairs, inside, ground floor
            public Vector3 StairTop;     // where you step off the stairs on each floor (y is added per floor)
            public float RampX, RampZ0, RampZ1, SideX;  // SideX: walkway beside the stairwell   // the stair ramp runs along x = RampX from z0 (low) to z1 (high)
            public readonly List<Rect> Holes = new List<Rect>();        // stairwell, lift shaft, atrium (upper floors)
            public readonly List<Vector3> Escalators = new List<Vector3>();
            public bool InHole(float x, float z) { foreach (var h in Holes) if (h.Contains(new Vector2(x, z))) return true; return false; }
            public bool Contains(Vector3 p) => Area.Contains(new Vector2(p.x, p.z));
            public void Shift(Vector3 d)
            {
                Area.position -= new Vector2(d.x, d.z);
                Door -= Flat(d); StairBase -= Flat(d); StairTop -= Flat(d);
                RampX -= d.x; RampZ0 -= d.z; RampZ1 -= d.z; SideX -= d.x;
                for (int i = 0; i < Holes.Count; i++) { var h = Holes[i]; h.position -= new Vector2(d.x, d.z); Holes[i] = h; }
                for (int i = 0; i < Escalators.Count; i++) Escalators[i] -= Flat(d);
            }
            private static Vector3 Flat(Vector3 d) => new Vector3(d.x, 0f, d.z);
        }

        // ------------------------------------------------------------------ the world: countries and the floating origin

        /// <summary>A country: a walled city on its own island. Streets sit on every 100 m line of the world grid.</summary>
        public class Country
        {
            public string Name;
            public double CX, CZ;         // world centre (multiple of 500)
            public double Half;           // half size in metres (multiple of 500)
            public uint Seed;
            public int Style;             // 0 Mega City, 1 Harbor City, 2 Sky City, 3 Super Mega World, 4 Mega Earth
            public readonly List<V2d> Airports = new List<V2d>();   // named airports: runway centres relative to CX/CZ
            public double AirportGrid;    // big worlds: also an airport roughly every this many metres (0 = none)
            public bool Huge => Style >= 3;
            public float SizeKm => (float)(Half * 2.0 / 1000.0);
            public bool Contains(double x, double z) => System.Math.Abs(x - CX) < Half && System.Math.Abs(z - CZ) < Half;
        }

        /// <summary>A position with double precision (for places millions of metres away).</summary>
        public struct V2d { public double x, y; public V2d(double x, double y) { this.x = x; this.y = y; } }

        public static readonly Country[] Countries = MakeCountries();

        private static Country[] MakeCountries()
        {
            var c = new[]
            {
                new Country { Name = "Mega City",        CX = 0,      CZ = 0,     Half = 5000f,  Seed = 1101, Style = 0 },
                new Country { Name = "Harbor City",      CX = 24000,  CZ = 0,     Half = 5000f,  Seed = 2202, Style = 1 },
                new Country { Name = "Sky City",         CX = 12000,  CZ = 21000, Half = 5000f,  Seed = 3303, Style = 2 },
                new Country { Name = "Super Mega World", CX = -80000, CZ = 0,     Half = 50000f, Seed = 4404, Style = 3 },
                // a million kilometres across: 25 times around the Earth
                new Country { Name = "Mega Earth",       CX = 500100000, CZ = 0,  Half = 500000000, Seed = 5505, Style = 4, AirportGrid = 40000 },
            };
            // airports: runway along x, centred on these spots (one for each city, a grid of them in the Super Mega World)
            for (int i = 0; i < 3; i++) c[i].Airports.Add(new V2d(0, -2250));
            for (int ax = -2; ax <= 2; ax++)
                for (int az = -2; az <= 2; az++)
                    c[3].Airports.Add(new V2d(ax * 20000.0, az * 20000.0 - 2250.0));
            c[4].Airports.Add(new V2d(0, -2250));
            return c;
        }

        /// <summary>Airports near a country-relative point: the named ones plus (in Mega Earth) one every AirportGrid metres.</summary>
        public static void AirportsNear(Country c, double lx, double lz, double r, List<V2d> into)
        {
            into.Clear();
            foreach (var a in c.Airports) if (System.Math.Abs(a.x - lx) < r && System.Math.Abs(a.y - lz) < r) into.Add(a);
            if (c.AirportGrid <= 0) return;
            double g = c.AirportGrid;
            long x0 = (long)System.Math.Floor((lx - r) / g), x1 = (long)System.Math.Ceiling((lx + r) / g);
            long z0 = (long)System.Math.Floor((lz - r) / g), z1 = (long)System.Math.Ceiling((lz + r) / g);
            for (long ix = x0; ix <= x1; ix++)
                for (long iz = z0; iz <= z1; iz++)
                {
                    if (ix == 0 && iz == 0) continue;
                    uint h = (uint)(ix * 73856093L ^ iz * 19349663L ^ c.Seed);
                    h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
                    if (h % 10 >= 6) continue;     // about 6 in 10 grid spots have an airport
                    var a = new V2d(ix * g, iz * g - 2250.0);
                    if (System.Math.Abs(a.x) > c.Half - 4000 || System.Math.Abs(a.y) > c.Half - 4000) continue;
                    if (System.Math.Abs(a.x - lx) < r && System.Math.Abs(a.y - lz) < r) into.Add(a);
                }
        }

        private static Country[] Unused()
        {
            var c = new Country[0];
            return c;
        }

        public const float Pitch = 100f, BlockSize = 80f, ChunkSize = 500f;
        public const float SeaLevel = -1.5f;

        /// <summary>World position of the scene's (0,0,0). Moves in 500 m steps so far-away places stay precise.</summary>
        public static double OriginX, OriginZ;
        /// <summary>Raised after the origin moves by delta: everything in the scene was moved by -delta.</summary>
        public static event System.Action<Vector3> OnShift;
        public static int CurrentCountry;

        public static Vector3 ToLocal(double x, double z, float y = 0f) => new Vector3((float)(x - OriginX), y, (float)(z - OriginZ));
        public static double WX(float localX) => localX + OriginX;
        public static double WZ(float localZ) => localZ + OriginZ;

        public static int CountryAt(double x, double z)
        {
            for (int i = 0; i < Countries.Length; i++) if (Countries[i].Contains(x, z)) return i;
            return -1;
        }
        public static int CountryAtLocal(Vector3 p) => CountryAt(WX(p.x), WZ(p.z));

        /// <summary>Moves the origin so that local point p becomes (about) the new centre.</summary>
        public static void ShiftOrigin(Vector3 p)
        {
            float dx = Mathf.Round(p.x / ChunkSize) * ChunkSize, dz = Mathf.Round(p.z / ChunkSize) * ChunkSize;
            if (dx == 0f && dz == 0f) return;
            var d = new Vector3(dx, 0f, dz);
            OriginX += dx; OriginZ += dz;
            foreach (var b in Buildings) b.Shift(d);
            for (int i = 0; i < ItemSpots.Count; i++) ItemSpots[i] -= d;
            Spawn -= d;
            OnShift?.Invoke(d);
        }

        /// <summary>Streets run along every 100 m line (in local coordinates too, since the origin moves in 500 m steps).</summary>
        public static float Street(int k) => Pitch * k;
        public static int StreetIdx(float v) => Mathf.RoundToInt(v / Pitch);

        /// <summary>Where bots can walk (set by the world streamer).</summary>
        public static TagGame.Maps.IWalk Walk;

        public static readonly List<Building> Buildings = new List<Building>();
        public static readonly List<Vector3> ItemSpots = new List<Vector3>();
        public static readonly List<MegaInteractable> Interactables = new List<MegaInteractable>();
        public static Vector3 Spawn = new Vector3(0f, 1.1f, 0f);
        public static PlayerController Player;

        public static void Clear()
        {
            Buildings.Clear();
            ItemSpots.Clear();
            Interactables.Clear();
        }

        public static Building BuildingAt(Vector3 p)
        {
            foreach (var b in Buildings) if (b.Contains(p)) return b;
            return null;
        }

        /// <summary>Feet height of the player's capsule.</summary>
        public static float FeetY(PlayerController pc)
        {
            var cc = pc.Controller;
            return cc != null ? pc.transform.position.y + cc.center.y - cc.height * 0.5f : pc.transform.position.y - 1f;
        }
    }

    /// <summary>Something you can walk up to and press E on.</summary>
    public abstract class MegaInteractable : MonoBehaviour
    {
        public string Prompt = "Use";
        public float Range = 2.4f;
        protected float _readyAt;
        public bool Ready => Time.time >= _readyAt;
        public abstract void Use(Vector3 userPos);
        protected virtual void OnEnable() { if (!MegaWorld.Interactables.Contains(this)) MegaWorld.Interactables.Add(this); }
        protected virtual void OnDisable() { MegaWorld.Interactables.Remove(this); }
    }

    /// <summary>Vending machine: drops an energy drink or a bandage.</summary>
    public class VendingMachine : MegaInteractable
    {
        private void Awake() { Prompt = "E: buy a drink"; }
        public override void Use(Vector3 userPos)
        {
            if (!Ready) return;
            _readyAt = Time.time + 15f;
            Vector3 front = transform.position + transform.forward * 1.3f;
            MegaItems.Spawn(Random.value < 0.7f ? ItemType.EnergyDrink : ItemType.Bandage, new Vector3(front.x, transform.position.y - 0.6f, front.z), false);
            GameAudio.Play("pickup", 0.6f, 0.8f);
        }
    }

    /// <summary>School locker: something random inside, once in a while.</summary>
    public class Locker : MegaInteractable
    {
        private void Awake() { Prompt = "E: open locker"; }
        public override void Use(Vector3 userPos)
        {
            if (!Ready) return;
            _readyAt = Time.time + 40f;
            Vector3 front = transform.position + transform.forward * 1.0f;
            MegaItems.Spawn(MegaItems.RandomType(), new Vector3(front.x, transform.position.y - 0.9f, front.z), false);
            GameAudio.Play("beep", 0.6f, 0.7f);
        }
    }

    /// <summary>Elevator: a platform that rides between floors and waits a moment at each one. It carries you with it.</summary>
    public class Elevator : MonoBehaviour
    {
        public float FloorHeight = 4.5f;
        public int Floors = 3;
        public Vector2 Size = new Vector2(3.2f, 3.2f);
        private float _baseY, _t;
        private int _floor, _dir = 1;
        private float _waitUntil;
        private const float Speed = 2.2f, Wait = 2.5f;

        private void Start() { _baseY = transform.position.y; }

        private void Update()
        {
            float prevTop = transform.position.y + 0.15f;
            Vector3 delta = Vector3.zero;
            if (Time.time >= _waitUntil)
            {
                float targetY = _baseY + (_floor + _dir) * FloorHeight;
                float y = Mathf.MoveTowards(transform.position.y, targetY, Speed * Time.deltaTime);
                delta = new Vector3(0f, y - transform.position.y, 0f);
                transform.position += delta;
                if (Mathf.Abs(y - targetY) < 0.001f)
                {
                    _floor += _dir;
                    if (_floor >= Floors - 1) _dir = -1; else if (_floor <= 0) _dir = 1;
                    _waitUntil = Time.time + Wait;
                    GameAudio.Play("beep", 0.25f, 1.6f);
                }
            }
            var pc = MegaWorld.Player;
            if (pc == null || delta == Vector3.zero || !pc.isActiveAndEnabled) return;
            Vector3 p = pc.transform.position;
            Vector3 c = transform.position;
            float feet = MegaWorld.FeetY(pc);
            if (Mathf.Abs(p.x - c.x) < Size.x * 0.5f && Mathf.Abs(p.z - c.z) < Size.y * 0.5f && Mathf.Abs(feet - prevTop) < 0.6f)
                pc.Controller.Move(delta + (delta.y > 0f ? Vector3.up * 0.02f : Vector3.zero));
        }
    }

    /// <summary>Escalator: a slope that carries you along while you stand on it.</summary>
    public class Escalator : MonoBehaviour
    {
        public Vector3 Direction = Vector3.forward;   // world direction of travel (along the slope)
        public Vector3 HalfSize = new Vector3(1.2f, 0.2f, 5f);
        public float Speed = 2.6f;

        private void Update()
        {
            var pc = MegaWorld.Player;
            if (pc == null || !pc.isActiveAndEnabled) return;
            Vector3 local = Quaternion.Inverse(transform.rotation) * (pc.transform.position - transform.position);
            if (Mathf.Abs(local.x) < HalfSize.x && Mathf.Abs(local.z) < HalfSize.z && local.y > 0f && local.y < 2.6f)
                pc.Controller.Move(Direction.normalized * Speed * Time.deltaTime);
        }
    }

    /// <summary>Jump pad: step on it to be thrown high into the air.</summary>
    public class JumpPad : MonoBehaviour
    {
        public float Radius = 1.6f, Power = 17f;
        private float _cool;

        private void Update()
        {
            var pc = MegaWorld.Player;
            if (pc == null || !pc.isActiveAndEnabled || Time.time < _cool) return;
            Vector3 d = pc.transform.position - transform.position; float feet = MegaWorld.FeetY(pc) - transform.position.y;
            d.y = 0f;
            if (d.sqrMagnitude < Radius * Radius && feet < 0.6f && feet > -0.5f)
            {
                pc.Launch(Power);
                _cool = Time.time + 0.6f;
                GameAudio.Play("dash", 1f, 0.6f);
                Fx.Burst(transform.position, new Color(0.3f, 0.9f, 1f));
            }
        }
    }
}
