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
        }

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
