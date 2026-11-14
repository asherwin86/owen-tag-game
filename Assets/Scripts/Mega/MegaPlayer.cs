using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TagGame.Gameplay;
using TagGame.Juice;
using TagGame.UI;

namespace TagGame.Mega
{
    /// <summary>
    /// Your side of Mega Mode: 4 inventory slots, guns and healing, picking things up, using vending machines
    /// and lockers, and the shoelaces (sprint on low stamina and they can come untied - hold T to tie them).
    /// Keys: 1-4 choose a slot, left click or F to fire/use, E to use things or swap items, T to tie laces.
    /// Controller: RB fire/use, B use/swap, Y tie (tap Y for the next slot), D-pad left/right slots.
    /// </summary>
    public class MegaPlayer : MonoBehaviour
    {
        public struct Slot { public bool Has; public ItemType Type; public int Ammo; }

        public readonly Slot[] Slots = new Slot[4];
        public int Selected { get; private set; }
        public string Prompt { get; private set; } = "";
        public float TieProgress { get; private set; }
        public Agent Locked { get; private set; }
        public Agent Me => _me;
        public bool SneakersOn => _me != null && Time.time < _me.SneakersUntil;

        // set by the on-screen buttons (touch)
        public static bool TouchFire, TouchTie;
        public static int TouchUseFrame = -10, TouchSlotFrame = -10;

        private Agent _me;
        private PlayerController _pc;
        private float _nextShot, _yHeld;
        private FloorItem _swapItem;
        private MegaInteractable _near;

        private void Awake() { _pc = GetComponent<PlayerController>(); }

        public void Begin(Agent me)
        {
            _me = me;
            for (int i = 0; i < Slots.Length; i++) Slots[i] = default;
            Slots[0] = new Slot { Has = true, Type = ItemType.Bandage };
            Selected = 0;
            _nextShot = 0f;
            Prompt = "";
        }

        public void End()
        {
            if (_pc == null) return;
            _pc.ExternalSpeed = 1f;
            _pc.SprintBlocked = false;
            _pc.Frozen = false;
            Prompt = "";
            Locked = null;
        }

        public void SelectSlot(int i) { Selected = Mathf.Clamp(i, 0, Slots.Length - 1); GameAudio.Play("step", 0.5f, 1.6f); }

        private void Update()
        {
            var g = MegaGame.I;
            if (g == null || !g.Running || _me == null) return;
            var kb = Keyboard.current; var pad = Gamepad.current; var mouse = Mouse.current;

            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.startButton.wasPressedThisFrame)) g.TogglePause();
            if (g.Paused || g.Finished) return;

            // slots
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) SelectSlot(0);
                if (kb.digit2Key.wasPressedThisFrame) SelectSlot(1);
                if (kb.digit3Key.wasPressedThisFrame) SelectSlot(2);
                if (kb.digit4Key.wasPressedThisFrame) SelectSlot(3);
            }
            if (mouse != null)
            {
                float sc = mouse.scroll.ReadValue().y;
                if (sc > 0.1f) SelectSlot((Selected + 3) % 4); else if (sc < -0.1f) SelectSlot((Selected + 1) % 4);
            }
            if (pad != null)
            {
                if (pad.dpad.right.wasPressedThisFrame) SelectSlot((Selected + 1) % 4);
                if (pad.dpad.left.wasPressedThisFrame) SelectSlot((Selected + 3) % 4);
            }
            if (Time.frameCount - TouchSlotFrame <= 1) SelectSlot((Selected + 1) % 4);

            float now = Time.time, dt = Time.deltaTime;
            bool frozen = g.CountingDown || _me.Knocked || now < _me.FrozenUntil || _me.Tying;
            _pc.Frozen = frozen;

            // shoelaces
            _me.Sprinting = _pc.IsSprinting;
            if (_pc.IsSprinting && !_me.Untied && _pc.StaminaNormalized < 0.15f && now > _me.LaceLockUntil && Random.value < 0.3f * dt)
            {
                _me.Untied = true;
                GameAudio.Play("laces", 0.9f);
                TagPopup.Show("Your shoelaces came untied!  Hold T to tie them", new Color(0.95f, 0.55f, 0.1f));
            }
            bool tieHeld = (kb != null && kb.tKey.isPressed) || TouchTie || (pad != null && pad.buttonNorth.isPressed && _me.Untied);
            if (pad != null)
            {
                if (pad.buttonNorth.isPressed) _yHeld += dt;
                else { if (_yHeld > 0f && _yHeld < 0.25f && !_me.Untied) SelectSlot((Selected + 1) % 4); _yHeld = 0f; }
            }
            if (_me.Untied && !_me.Knocked)
            {
                if (tieHeld && !_me.Tying) { _me.Tying = true; _me.TieDoneAt = now + 1.5f; GameAudio.Play("step", 0.6f, 0.7f); }
                else if (!tieHeld && _me.Tying) _me.Tying = false;
            }
            TieProgress = _me.Tying ? Mathf.Clamp01(1f - (_me.TieDoneAt - now) / 1.5f) : 0f;
            _pc.ExternalSpeed = (_me.Untied ? 0.45f : 1f) * (SneakersOn ? 1.25f : 1f);
            _pc.SprintBlocked = _me.Untied;
            if (SneakersOn && !_me.Untied) _pc.Boost(1f, 0.1f);

            if (frozen) { Prompt = _me.Knocked ? "KNOCKED DOWN" : (_me.Tying ? "Tying your laces..." : ""); Locked = null; return; }

            // pick up things you walk over
            _swapItem = null;
            var item = MegaItems.Nearest(_me.Pos, 1.5f);
            string prompt = "";
            if (item != null && !TryPickup(item))
            {
                _swapItem = item;
                prompt = item.Type == ItemType.Ammo ? "Ammo box - find a gun first" : "E: swap for " + MegaItems.NameOf(item.Type);
            }
            // things to use (vending machines, lockers)
            _near = null;
            float bd = float.MaxValue;
            foreach (var m in MegaWorld.Interactables)
            {
                if (m == null) continue;
                Vector3 d = m.transform.position - transform.position;
                if (Mathf.Abs(d.y) > 2.2f) continue;
                d.y = 0f;
                float dd = d.magnitude;
                if (dd < m.Range && dd < bd) { bd = dd; _near = m; }
            }
            if (_near != null) prompt = _near.Ready ? _near.Prompt : "Empty - try again soon";
            Prompt = prompt;

            bool usePressed = (kb != null && kb.eKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame) || Time.frameCount - TouchUseFrame <= 1;
            if (usePressed)
            {
                if (_near != null && _near.Ready) _near.Use(transform.position);
                else if (_swapItem != null && _swapItem.Type != ItemType.Ammo) Swap(_swapItem);
            }

            // fire / use the selected item
            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool fireHeld = (mouse != null && mouse.leftButton.isPressed && !overUi) || (kb != null && kb.fKey.isPressed) || (pad != null && pad.rightShoulder.isPressed) || TouchFire;
            bool firePressed = (mouse != null && mouse.leftButton.wasPressedThisFrame && !overUi) || (kb != null && kb.fKey.wasPressedThisFrame) || (pad != null && pad.rightShoulder.wasPressedThisFrame) || (TouchFire && !_touchFireWas);
            _touchFireWas = TouchFire;
            var s = Slots[Selected];
            Locked = null;
            if (s.Has && MegaItems.IsGun(s.Type))
            {
                var w = MegaItems.Weapon(s.Type);
                Vector3 origin = transform.position + Vector3.up * 0.4f;
                Vector3 dir = transform.forward;
                Locked = g.AimTarget(origin, dir, w.Range, 24f);
                if (fireHeld && now >= _nextShot)
                {
                    if (s.Ammo <= 0) { if (firePressed) { GameAudio.Play("click", 0.7f); TagPopup.Show("Out of ammo - find an ammo box", new Color(0.6f, 0.6f, 0.6f)); } }
                    else
                    {
                        _nextShot = now + w.Cooldown;
                        if (Locked != null) dir = ((Locked.Pos + Vector3.up * 1.0f) - origin).normalized;
                        g.Shoot(_me, origin, dir, s.Type);
                        s.Ammo--;
                        Slots[Selected] = s;
                        FollowCamera.Shake(s.Type == ItemType.Sniper || s.Type == ItemType.Shotgun ? 0.12f : 0.04f);
                    }
                }
            }
            else if (s.Has && firePressed) UseItem(Selected);
        }

        private bool _touchFireWas;

        private int FreeSlot()
        {
            if (!Slots[Selected].Has) return Selected;
            for (int i = 0; i < Slots.Length; i++) if (!Slots[i].Has) return i;
            return -1;
        }

        /// <summary>Walk-over pickup. False if there's no room (then E swaps).</summary>
        private bool TryPickup(FloorItem item)
        {
            var t = item.Type;
            if (t == ItemType.Ammo)
            {
                int gi = -1;
                if (Slots[Selected].Has && MegaItems.IsGun(Slots[Selected].Type)) gi = Selected;
                else for (int i = 0; i < Slots.Length; i++) if (Slots[i].Has && MegaItems.IsGun(Slots[i].Type)) { gi = i; break; }
                if (gi < 0) return false;
                var sl = Slots[gi]; sl.Ammo += MegaItems.Weapon(sl.Type).Mag; Slots[gi] = sl;
                Picked(item, "+ammo for " + MegaItems.NameOf(sl.Type));
                return true;
            }
            if (MegaItems.IsGun(t))
                for (int i = 0; i < Slots.Length; i++)
                    if (Slots[i].Has && Slots[i].Type == t)
                    {
                        var sl = Slots[i]; sl.Ammo += MegaItems.Weapon(t).Mag; Slots[i] = sl;
                        Picked(item, "+ammo for " + MegaItems.NameOf(t));
                        return true;
                    }
            int free = FreeSlot();
            if (free < 0) return false;
            Slots[free] = new Slot { Has = true, Type = t, Ammo = MegaItems.IsGun(t) ? MegaItems.Weapon(t).Mag : 0 };
            if (MegaItems.IsGun(t) && !(Slots[Selected].Has && MegaItems.IsGun(Slots[Selected].Type))) Selected = free;
            Picked(item, "Picked up " + MegaItems.NameOf(t));
            return true;
        }

        private void Picked(FloorItem item, string msg)
        {
            MegaItems.Take(item);
            GameAudio.Play("pickup", 0.7f, MegaItems.IsGun(item.Type) ? 0.8f : 1.1f);
            if (MegaItems.IsGun(item.Type)) GameAudio.Play("reload", 0.6f);
            TagPopup.Show(msg, MegaItems.ColorOf(item.Type));
        }

        private void Swap(FloorItem item)
        {
            var old = Slots[Selected];
            MegaItems.Take(item);
            if (old.Has) MegaItems.Spawn(old.Type, _me.Pos + transform.forward * 0.8f, false);
            Slots[Selected] = new Slot { Has = true, Type = item.Type, Ammo = MegaItems.IsGun(item.Type) ? MegaItems.Weapon(item.Type).Mag : 0 };
            GameAudio.Play("pickup", 0.7f, 0.9f);
            TagPopup.Show("Swapped for " + MegaItems.NameOf(item.Type), MegaItems.ColorOf(item.Type));
        }

        private void UseItem(int i)
        {
            var s = Slots[i];
            string msg;
            switch (s.Type)
            {
                case ItemType.Medkit:
                    if (_me.Hp >= 100f) { TagPopup.Show("Already full health", new Color(0.6f, 0.6f, 0.6f)); return; }
                    _me.Hp = Mathf.Min(100f, _me.Hp + 60f); msg = "+60 health"; break;
                case ItemType.Bandage:
                    if (_me.Hp >= 100f) { TagPopup.Show("Already full health", new Color(0.6f, 0.6f, 0.6f)); return; }
                    _me.Hp = Mathf.Min(100f, _me.Hp + 25f); msg = "+25 health"; break;
                case ItemType.Shield:
                    if (_me.Armour >= 100f) { TagPopup.Show("Armour is full", new Color(0.6f, 0.6f, 0.6f)); return; }
                    _me.Armour = Mathf.Min(100f, _me.Armour + 50f); msg = "+50 armour"; break;
                case ItemType.EnergyDrink:
                    _pc.RefillStamina(); _me.Untied = false; _me.Tying = false; _me.LaceLockUntil = Time.time + 30f;
                    msg = "Energy! Full stamina, laces stay tied for 30s"; break;
                case ItemType.Sneakers:
                    _me.SneakersUntil = Time.time + 25f; _me.LaceLockUntil = Time.time + 25f; _me.Untied = false; _me.Tying = false;
                    msg = "Super sneakers! Faster for 25s"; break;
                default: return;
            }
            Slots[i] = default;
            GameAudio.Play("heal", 0.8f);
            Fx.Burst(transform.position - Vector3.up * 0.5f, MegaItems.ColorOf(s.Type));
            TagPopup.Show(msg, MegaItems.ColorOf(s.Type));
        }
    }
}
