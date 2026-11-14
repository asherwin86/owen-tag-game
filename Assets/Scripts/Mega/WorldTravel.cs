using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TagGame.Gameplay;
using TagGame.Juice;
using TagGame.Maps;
using TagGame.UI;
using MG = TagGame.Multiplayer.MultiplayerGame;

namespace TagGame.Mega
{
    /// <summary>Walk up to a parked plane and press E.</summary>
    public class PlaneInteract : MegaInteractable
    {
        public WorldTravel.Plane Plane;
        private void Awake() { Prompt = "E: board the plane"; Range = 7f; }
        public override void Use(Vector3 userPos) { if (WorldTravel.I != null) WorldTravel.I.OpenMenu(Plane); }
    }

    /// <summary>
    /// Aeroplanes between the countries. Every airport has planes parked by the terminal. Board one and either take a
    /// flight (it takes off, flies over the sea and lands at the airport you picked) or fly it yourself:
    /// W/S nose down/up, A/D turn, Shift more power, Ctrl less power (controller: left stick, RT/LT).
    /// Land gently on flat ground and press E to get out. Crash and you're knocked down.
    /// </summary>
    public class WorldTravel : MonoBehaviour
    {
        public static WorldTravel I { get; private set; }

        public class Plane
        {
            public Transform T;
            public bool Parked = true, FromStand;
            public ChunkRecord Rec;
            public int Stand;
            public float Speed, Throttle, Pitch, Yaw, Roll, VSpeed;
        }

        private enum Mode { None, Auto, Manual }
        private enum Leg { Takeoff, Climb, Cruise, Approach, Rollout }

        public bool InPlane => _mode != Mode.None;
        private Mode _mode;
        private Leg _leg;
        private Plane _plane;
        private readonly List<Plane> _planes = new List<Plane>();
        private PlayerController _player;
        private GameObject _canvas, _menu, _hud;
        private TMP_Text _hudText, _menuTitle;
        private Transform _menuList;
        private int _destCountry, _destAirport;
        private float _legT;
        private Vector3 _savedCamOffset;
        private Material _body, _stripe, _glass, _dark;
        private bool _grounded;

        private void Awake()
        {
            I = this;
            _player = FindFirstObjectByType<PlayerController>();
        }

        private void OnEnable() { MegaWorld.OnShift += OnShift; }
        private void OnDisable() { MegaWorld.OnShift -= OnShift; }

        private void Mats()
        {
            if (_body != null) return;
            var r = _player != null ? _player.GetComponent<Renderer>() : null;
            Material M(Color c, float sm)
            {
                var m = r != null ? new Material(r.sharedMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.mainTexture = null; m.color = c;
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", sm);
                return m;
            }
            _body = M(new Color(0.95f, 0.95f, 0.97f), 0.75f);
            _stripe = M(new Color(0.15f, 0.45f, 0.9f), 0.6f);
            _glass = M(new Color(0.1f, 0.15f, 0.22f), 0.95f);
            _dark = M(new Color(0.25f, 0.27f, 0.3f), 0.5f);
        }

        // ------------------------------------------------------------ planes at the airports

        /// <summary>A chunk with plane stands was built: park a plane on each.</summary>
        public void StandsReady(ChunkRecord rec)
        {
            for (int i = 0; i < rec.PlaneStands.Count; i++)
            {
                bool have = false;
                foreach (var p in _planes) if (p.Rec == rec && p.Stand == i) have = true;
                if (have) continue;
                var pose = rec.PlaneStands[i];
                var pl = MakePlane(pose.position, pose.rotation);
                pl.Rec = rec; pl.Stand = i; pl.FromStand = true;
            }
        }

        /// <summary>A chunk went away: its parked planes go too (not the one you're in).</summary>
        public void StandsGone(ChunkRecord rec)
        {
            for (int i = _planes.Count - 1; i >= 0; i--)
            {
                var p = _planes[i];
                if (p.Rec != rec || p == _plane) continue;
                if (p.T != null) Destroy(p.T.gameObject);
                _planes.RemoveAt(i);
            }
        }

        public void EndAll()
        {
            if (InPlane) GetOut(false);
            foreach (var p in _planes) if (p.T != null) Destroy(p.T.gameObject);
            _planes.Clear();
            if (_menu != null) _menu.SetActive(false);
            if (_hud != null) _hud.SetActive(false);
        }

        private Plane MakePlane(Vector3 pos, Quaternion rot)
        {
            Mats();
            var root = new GameObject("Plane").transform;
            root.SetPositionAndRotation(pos, rot);
            void P(Vector3 lp, Vector3 ls, Material m, PrimitiveType t = PrimitiveType.Cube)
            {
                var g = GameObject.CreatePrimitive(t);
                var c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
                g.transform.SetParent(root, false);
                g.transform.localPosition = lp; g.transform.localScale = ls;
                g.GetComponent<Renderer>().sharedMaterial = m;
            }
            P(new Vector3(0f, 2.2f, 0f), new Vector3(2.6f, 2.6f, 16f), _body);                 // fuselage
            P(new Vector3(0f, 2.2f, 8.4f), new Vector3(2.2f, 2.2f, 1.6f), _body);              // nose
            P(new Vector3(0f, 2.9f, 7.6f), new Vector3(2f, 0.8f, 1.4f), _glass);               // cockpit windows
            P(new Vector3(0f, 2.0f, 0f), new Vector3(2.65f, 0.35f, 16.1f), _stripe);           // stripe
            P(new Vector3(0f, 1.8f, 0.5f), new Vector3(18f, 0.35f, 3.2f), _body);              // wings
            P(new Vector3(-8.6f, 2.2f, 0.4f), new Vector3(0.8f, 0.9f, 1.6f), _stripe);         // wing tips
            P(new Vector3(8.6f, 2.2f, 0.4f), new Vector3(0.8f, 0.9f, 1.6f), _stripe);
            P(new Vector3(-4f, 1.2f, 1.6f), new Vector3(1.3f, 1.3f, 3f), _dark, PrimitiveType.Cylinder); // engines
            P(new Vector3(4f, 1.2f, 1.6f), new Vector3(1.3f, 1.3f, 3f), _dark, PrimitiveType.Cylinder);
            root.GetChild(root.childCount - 1).localRotation = Quaternion.Euler(90f, 0f, 0f);
            root.GetChild(root.childCount - 2).localRotation = Quaternion.Euler(90f, 0f, 0f);
            P(new Vector3(0f, 4.6f, -7f), new Vector3(0.35f, 3.6f, 2.4f), _stripe);            // tail fin
            P(new Vector3(0f, 2.8f, -7.4f), new Vector3(6.5f, 0.3f, 1.8f), _body);             // tail wings
            P(new Vector3(0f, 0.45f, 5.5f), new Vector3(0.3f, 0.9f, 0.3f), _dark);             // wheels
            P(new Vector3(-2f, 0.45f, -0.5f), new Vector3(0.4f, 0.9f, 0.6f), _dark);
            P(new Vector3(2f, 0.45f, -0.5f), new Vector3(0.4f, 0.9f, 0.6f), _dark);
            var pl = new Plane { T = root, Yaw = rot.eulerAngles.y };
            var it = root.gameObject.AddComponent<PlaneInteract>();
            it.Plane = pl;
            _planes.Add(pl);
            return pl;
        }

        // ------------------------------------------------------------ menu

        private void BuildUi()
        {
            if (_canvas != null) return;
            _canvas = new GameObject("TravelCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var cv = _canvas.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 45;
            var sc = _canvas.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;
            _menu = MG.MakePanel(_canvas.transform, "PlaneMenu", new Color(0.03f, 0.05f, 0.1f, 0.88f));
            _menuTitle = MG.MakeText(_menu.transform, "", 60, MG.C, new Vector2(0f, 320f), new Vector2(1400f, 90f));
            _menuTitle.fontStyle = FontStyles.Bold;
            var list = new GameObject("List", typeof(RectTransform));
            list.transform.SetParent(_menu.transform, false);
            MG.Stretch((RectTransform)list.transform);
            _menuList = list.transform;
            _menu.SetActive(false);
            _hud = new GameObject("FlightHud", typeof(RectTransform));
            _hud.transform.SetParent(_canvas.transform, false);
            MG.Stretch((RectTransform)_hud.transform);
            _hudText = MG.MakeText(_hud.transform, "", 28, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1500f, 120f));
            _hud.SetActive(false);
        }

        public void OpenMenu(Plane p)
        {
            if (InPlane || p == null || MegaGame.I == null || !MegaGame.I.Running) return;
            BuildUi();
            foreach (Transform ch in _menuList) Destroy(ch.gameObject);
            _menuTitle.text = "WHERE TO?";
            float y = 190f;
            MG.MakeButton(_menuList, "Fly it yourself", MG.C, new Vector2(0f, y), new Vector2(620f, 80f), new Color(0.9f, 0.5f, 0.1f), () => { CloseMenu(); Board(p, Mode.Manual, -1, 0); }, 34);
            y -= 110f;
            int here = MegaWorld.CurrentCountry;
            for (int k = 0; k < MegaWorld.Countries.Length; k++)
            {
                var c = MegaWorld.Countries[k];
                if (k == here && c.Airports.Count < 2) continue;
                int kk = k;
                int ap = c.Airports.Count / 2;
                if (k == here) { ap = (NearestAirport(k) + 1 + Random.Range(0, c.Airports.Count - 1)) % c.Airports.Count; }
                double dx = c.CX + c.Airports[ap].x - MegaWorld.WX(p.T.position.x), dz = c.CZ + c.Airports[ap].y - MegaWorld.WZ(p.T.position.z);
                float km = (float)System.Math.Sqrt(dx * dx + dz * dz) / 1000f;
                string label = (k == here ? "Another airport in " : "Flight to ") + c.Name + "   (" + km.ToString("0") + " km)";
                int app = ap;
                MG.MakeButton(_menuList, label, MG.C, new Vector2(0f, y), new Vector2(760f, 74f), new Color(0.15f, 0.45f, 0.85f), () => { CloseMenu(); Board(p, Mode.Auto, kk, app); }, 30);
                y -= 90f;
            }
            MG.MakeButton(_menuList, "Cancel", MG.C, new Vector2(0f, y - 20f), new Vector2(260f, 60f), new Color(0.5f, 0.2f, 0.2f), CloseMenu, 28);
            _menu.SetActive(true);
            if (MegaGame.I != null && !MegaGame.I.Paused) MegaGame.I.TogglePause();
        }

        private void CloseMenu()
        {
            if (_menu != null) _menu.SetActive(false);
            if (MegaGame.I != null && MegaGame.I.Paused) MegaGame.I.TogglePause();
        }

        private int NearestAirport(int k)
        {
            var c = MegaWorld.Countries[k];
            double wx = MegaWorld.WX(_player.transform.position.x), wz = MegaWorld.WZ(_player.transform.position.z);
            int best = 0; double bd = double.MaxValue;
            for (int i = 0; i < c.Airports.Count; i++)
            {
                double dx = c.CX + c.Airports[i].x - wx, dz = c.CZ + c.Airports[i].y - wz, d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        // ------------------------------------------------------------ flying

        private void Board(Plane p, Mode mode, int country, int airport)
        {
            var g = MegaGame.I;
            if (g == null || g.Me == null) return;
            _plane = p; _mode = mode; _destCountry = country; _destAirport = airport;
            p.Parked = false; p.Rec = null;   // it's yours now: it stays wherever you leave it
            _grounded = true;
            p.Speed = 0f; p.Throttle = 0f; p.Pitch = 0f; p.Roll = 0f; p.VSpeed = 0f;
            p.Yaw = p.T.eulerAngles.y;
            g.Me.Away = true;
            Realism.Flying = true;
            if (_player.Controller != null) _player.Controller.enabled = false;
            _player.Frozen = true;
            var cam = FindFirstObjectByType<FollowCamera>();
            if (cam != null) { _savedCamOffset = cam.Offset; cam.Offset = new Vector3(0f, 9f, -30f); }
            if (WorldStream.I != null) WorldStream.I.Focus = () => _plane != null && _plane.T != null ? _plane.T.position : _player.transform.position;
            BuildUi();
            _hud.SetActive(true);
            GameAudio.Play("whistle", 0.5f, 1.4f);
            if (mode == Mode.Auto)
            {
                // line up on this airport's runway, heading east
                var c = MegaWorld.Countries[MegaWorld.CurrentCountry];
                int here = NearestAirport(MegaWorld.CurrentCountry);
                Vector3 rw = MegaWorld.ToLocal(c.CX + c.Airports[here].x - 1300.0, c.CZ + c.Airports[here].y);
                p.T.SetPositionAndRotation(rw, Quaternion.Euler(0f, 90f, 0f));
                p.Yaw = 90f;
                _leg = Leg.Takeoff; _legT = 0f;
                TagPopup.Show("Flight to " + MegaWorld.Countries[country].Name + " - press E to skip ahead", new Color(0.2f, 0.6f, 0.95f));
            }
            else TagPopup.Show("You're the pilot!  Shift = power, W/S = nose, A/D = turn", new Color(0.9f, 0.5f, 0.1f));
        }

        private void GetOut(bool onGround)
        {
            var g = MegaGame.I;
            var p = _plane;
            _mode = Mode.None;
            Realism.Flying = false;
            if (_hud != null) _hud.SetActive(false);
            var cam = FindFirstObjectByType<FollowCamera>();
            if (cam != null && _savedCamOffset != Vector3.zero) cam.Offset = _savedCamOffset;
            if (WorldStream.I != null) WorldStream.I.Focus = () => _player.transform.position;
            if (p != null && p.T != null)
            {
                Vector3 side = p.T.position - p.T.right * 7f;
                side.y = 1.2f;
                if (_player.Controller != null) _player.Controller.enabled = true;
                _player.TeleportTo(side, Quaternion.Euler(0f, p.Yaw, 0f));
                p.Parked = true; p.Speed = 0f;
                p.T.rotation = Quaternion.Euler(0f, p.Yaw, 0f);
                var pos = p.T.position; pos.y = 0f; p.T.position = pos;
            }
            else if (_player.Controller != null) _player.Controller.enabled = true;
            _player.Frozen = false;
            if (g != null && g.Me != null) { g.Me.Away = false; g.ArrivedIn(MegaWorld.CountryAtLocal(_player.transform.position)); }
            _plane = null;
        }

        private void Crash(string why)
        {
            var p = _plane;
            Vector3 at = p.T.position;
            Fx.Burst(at, new Color(1f, 0.5f, 0.1f)); Fx.Burst(at + Vector3.up * 2f, new Color(0.3f, 0.3f, 0.3f));
            GameAudio.Play("knock", 1f, 0.5f); GameAudio.Play("shotgun", 1f, 0.5f);
            FollowCamera.Shake(0.8f);
            TagPopup.Show(why, new Color(0.9f, 0.3f, 0.2f));
            bool overLand = MegaWorld.CountryAtLocal(at) >= 0;
            // the plane is wrecked: it disappears
            _planes.Remove(p);
            if (p.T != null) Destroy(p.T.gameObject);
            _plane = null;
            _mode = Mode.None;
            Realism.Flying = false;
            if (_hud != null) _hud.SetActive(false);
            var cam = FindFirstObjectByType<FollowCamera>();
            if (cam != null && _savedCamOffset != Vector3.zero) cam.Offset = _savedCamOffset;
            Vector3 dest;
            if (overLand) dest = new Vector3(MegaWorld.Street(MegaWorld.StreetIdx(at.x)), 1.2f, at.z);
            else
            {
                var c = MegaWorld.Countries[MegaWorld.CurrentCountry];
                var ap = c.Airports[NearestAirport(MegaWorld.CurrentCountry)];
                dest = MegaWorld.ToLocal(c.CX + ap.x + 250.0, c.CZ + ap.y + 222.0, 1.2f);
            }
            if (WorldStream.I != null) { WorldStream.I.Focus = () => _player.transform.position; WorldStream.I.BuildNowAround(dest); }
            if (_player.Controller != null) _player.Controller.enabled = true;
            _player.TeleportTo(dest, Quaternion.identity);
            _player.Frozen = false;
            var g = MegaGame.I;
            if (g != null && g.Me != null)
            {
                g.Me.Away = false;
                g.Me.Hp = 0f; g.Me.KnockedUntil = Time.time + 4f;
                g.ArrivedIn(MegaWorld.CountryAtLocal(dest));
            }
        }

        private void Update()
        {
            if (!InPlane && Time.frameCount % 120 == 0 && _player != null)
            {
                // planes left far away (after a landing somewhere) are tidied up
                for (int i = _planes.Count - 1; i >= 0; i--)
                {
                    var q = _planes[i];
                    if (q.T == null) { _planes.RemoveAt(i); continue; }
                    if (q.Rec == null && (q.T.position - _player.transform.position).sqrMagnitude > 4000f * 4000f) { Destroy(q.T.gameObject); _planes.RemoveAt(i); }
                }
            }
            if (!InPlane || _plane == null || _plane.T == null) { if (InPlane) GetOut(false); return; }
            var g = MegaGame.I;
            if (g == null || !g.Running) { GetOut(false); return; }
            if (g.Paused) return;
            float dt = Time.deltaTime;
            if (_mode == Mode.Auto) AutoFly(dt); else ManualFly(dt);
            if (_plane == null) return;

            // you ride along
            _player.Frozen = true;
            _player.transform.position = _plane.T.position + Vector3.up * 2.5f;
            _player.transform.rotation = Quaternion.Euler(0f, _plane.Yaw, 0f);
            var cam = FindFirstObjectByType<FollowCamera>();
            if (cam != null) cam.ForceYaw(_plane.Yaw);
            if (WorldStream.I != null) WorldStream.I.DetailOn = _plane.T.position.y < 160f;

            float alt = _plane.T.position.y;
            string where = MegaWorld.CountryAtLocal(_plane.T.position) is int k && k >= 0 ? MegaWorld.Countries[k].Name : "over the sea";
            _hudText.text = "<b>" + Mathf.RoundToInt(_plane.Speed * 3.6f) + " km/h</b>   altitude " + Mathf.RoundToInt(alt) + " m   " + where
                + (_mode == Mode.Manual ? "\n<size=22>Shift/RT power " + Mathf.RoundToInt(_plane.Throttle * 100f) + "%  ·  Ctrl/LT less  ·  W/S nose  ·  A/D turn  ·  " + (_grounded && _plane.Speed < 4f ? "<color=#FFD84A>E: get out</color>" : "land gently on flat ground") + "</size>"
                : "\n<size=22>Flight to " + MegaWorld.Countries[_destCountry].Name + "  ·  " + DistLeft().ToString("0.0") + " km to go  ·  E: skip ahead</size>");
            // engine hum
            if (Time.frameCount % 20 == 0) GameAudio.Play("dash", 0.08f + _plane.Throttle * 0.08f, 0.4f + _plane.Speed / 400f);
        }

        private float DistLeft()
        {
            var c = MegaWorld.Countries[_destCountry];
            Vector3 d = MegaWorld.ToLocal(c.CX + c.Airports[_destAirport].x, c.CZ + c.Airports[_destAirport].y) - _plane.T.position;
            d.y = 0f;
            return d.magnitude / 1000f;
        }

        private void AutoFly(float dt)
        {
            var p = _plane;
            var c = MegaWorld.Countries[_destCountry];
            var apt = c.Airports[_destAirport];
            Vector3 touch = MegaWorld.ToLocal(c.CX + apt.x - 1100.0, c.CZ + apt.y);
            Vector3 approachStart = touch - Vector3.right * 6000f + Vector3.up * 420f;
            var kb = Keyboard.current; var pad = Gamepad.current;
            bool skip = (kb != null && kb.eKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame) || Time.frameCount - MegaPlayer.TouchUseFrame <= 1;
            _legT += dt;
            Vector3 pos = p.T.position;
            switch (_leg)
            {
                case Leg.Takeoff:
                    p.Speed = Mathf.Min(85f, p.Speed + 9f * dt);
                    p.Pitch = p.Speed > 70f ? Mathf.MoveTowards(p.Pitch, -9f, 6f * dt) : 0f;
                    if (pos.y > 30f) { _leg = Leg.Climb; _legT = 0f; }
                    break;
                case Leg.Climb:
                case Leg.Cruise:
                    {
                        Vector3 to = approachStart - pos; float horiz = new Vector2(to.x, to.z).magnitude;
                        float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                        p.Yaw = Mathf.MoveTowardsAngle(p.Yaw, wantYaw, 9f * dt);
                        float cruiseAlt = horiz > 15000f ? 900f : 420f;
                        float targetSpeed = horiz > 20000f ? 700f : (horiz > 5000f ? 260f : 120f);
                        p.Speed = Mathf.MoveTowards(p.Speed, targetSpeed, 25f * dt);
                        float climb = Mathf.Clamp((cruiseAlt - pos.y) * 0.08f, -15f, 25f);
                        p.Pitch = -Mathf.Atan2(climb, Mathf.Max(30f, p.Speed)) * Mathf.Rad2Deg;
                        p.Roll = Mathf.Clamp(Mathf.DeltaAngle(p.Yaw, wantYaw) * 1.2f, -30f, 30f);
                        if (skip && horiz > 3000f)
                        {
                            Vector3 jump = approachStart - new Vector3(0f, 0f, 0f);
                            if (WorldStream.I != null) WorldStream.I.Focus = () => _plane != null ? _plane.T.position : _player.transform.position;
                            p.T.position = jump; p.Yaw = 90f; p.Speed = 120f;
                            MegaWorld.ShiftOrigin(p.T.position);
                            pos = p.T.position;
                            TagPopup.Show("Arriving at " + c.Name, new Color(0.2f, 0.6f, 0.95f));
                        }
                        if (horiz < 250f) { _leg = Leg.Approach; _legT = 0f; }
                        break;
                    }
                case Leg.Approach:
                    {
                        Vector3 to = touch - pos; float horiz = new Vector2(to.x, to.z).magnitude;
                        float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                        p.Yaw = Mathf.MoveTowardsAngle(p.Yaw, wantYaw, 6f * dt);
                        p.Speed = Mathf.MoveTowards(p.Speed, 75f, 8f * dt);
                        float wantY = Mathf.Lerp(0f, 420f, Mathf.Clamp01(horiz / 6000f));
                        float vs = Mathf.Clamp((wantY - pos.y) * 0.25f, -9f, 5f);
                        p.Pitch = -Mathf.Atan2(vs, Mathf.Max(30f, p.Speed)) * Mathf.Rad2Deg;
                        p.Roll = 0f;
                        if (pos.y < 1.5f && horiz < 400f) { _leg = Leg.Rollout; _legT = 0f; pos.y = 0f; GameAudio.Play("step", 1f, 0.5f); }
                        break;
                    }
                case Leg.Rollout:
                    p.Pitch = 0f; p.Roll = 0f; p.Yaw = Mathf.MoveTowardsAngle(p.Yaw, 90f, 10f * dt);
                    p.Speed = Mathf.Max(0f, p.Speed - 14f * dt);
                    if (p.Speed <= 0.1f)
                    {
                        _grounded = true;
                        TagPopup.Show("Welcome to " + c.Name + "!", new Color(0.2f, 0.6f, 0.95f));
                        GetOut(true);
                        return;
                    }
                    break;
            }
            Move(p, dt, _leg == Leg.Rollout || (_leg == Leg.Takeoff && p.Speed < 70f), true);
        }

        private void ManualFly(float dt)
        {
            var p = _plane;
            var kb = Keyboard.current; var pad = Gamepad.current;
            float up = 0f, turn = 0f, power = 0f;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) up -= 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) up += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) turn -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) turn += 1f;
                if (kb.leftShiftKey.isPressed) power += 1f;
                if (kb.leftCtrlKey.isPressed) power -= 1f;
            }
            if (pad != null)
            {
                Vector2 s = pad.leftStick.ReadValue();
                if (Mathf.Abs(s.x) > 0.1f) turn = s.x;
                if (Mathf.Abs(s.y) > 0.1f) up = -s.y;
                power += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
            }
            var mv = MobileControls.MoveInput;
            if (mv.sqrMagnitude > 0.01f) { turn = mv.x; up = -mv.y; }
            if (MobileControls.SprintHeld) power += 1f;

            p.Throttle = Mathf.Clamp01(p.Throttle + power * 0.6f * dt);
            float thrust = p.Throttle * 34f;
            float drag = 0.0009f * p.Speed * p.Speed + (_grounded ? (p.Throttle < 0.05f ? 9f : 1f) : 0.5f);
            p.Speed = Mathf.Clamp(p.Speed + (thrust - drag) * dt - Mathf.Sin(-p.Pitch * Mathf.Deg2Rad) * 9.8f * dt, 0f, 330f);

            bool canFly = p.Speed > 55f;
            if (_grounded)
            {
                p.Yaw += turn * 40f * dt * Mathf.Clamp01(p.Speed / 10f + 0.2f);
                p.Roll = 0f;
                p.Pitch = canFly && up > 0f ? Mathf.MoveTowards(p.Pitch, -10f, 20f * dt) : 0f;   // pull back (S) to take off
            }
            else
            {
                p.Roll = Mathf.MoveTowards(p.Roll, -turn * 45f, 70f * dt);
                p.Yaw += -p.Roll * 0.9f * dt;
                p.Pitch = Mathf.Clamp(p.Pitch - up * 45f * dt, -35f, 30f);   // W: nose down, S: nose up
                if (!canFly) p.Pitch = Mathf.MoveTowards(p.Pitch, 25f, 15f * dt);   // stall: nose drops
            }
            bool exit = (kb != null && kb.eKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame) || Time.frameCount - MegaPlayer.TouchUseFrame <= 1;
            if (exit && _grounded && p.Speed < 4f) { GetOut(true); return; }
            Move(p, dt, false, false);
        }

        private void Move(Plane p, float dt, bool forceGround, bool auto)
        {
            Quaternion rot = Quaternion.Euler(p.Pitch, p.Yaw, p.Roll);
            Vector3 fwd = rot * Vector3.forward;
            Vector3 pos = p.T.position + fwd * p.Speed * dt;
            if (!_grounded || p.Speed < 55f) pos.y -= (p.Speed < 55f && !_grounded ? 6f : 0f) * dt;
            // what's under us: sea, flat ground, or buildings (from the layout, even where nothing is built yet)
            bool land = MegaWorld.CountryAtLocal(pos) >= 0;
            float floor = land ? 0f : MegaWorld.SeaLevel;
            float roofs = land && !auto ? WorldStream.GroundClearance(pos) : floor;
            if (!auto && Physics.Raycast(new Vector3(pos.x, pos.y + 2f, pos.z), Vector3.down, out var hit, 200f, ~0, QueryTriggerInteraction.Ignore))
                if (_player == null || hit.collider.gameObject != _player.gameObject) roofs = Mathf.Max(roofs, hit.point.y);
            if (forceGround) { pos.y = 0f; _grounded = true; }
            else if (pos.y <= Mathf.Max(floor, roofs) + 0.05f)
            {
                if (!land) { Crash("Splash! Your plane went into the sea"); return; }
                if (roofs > 1.5f && !(_grounded)) { Crash("Crash! You flew into a building"); return; }
                float vs = (p.T.position.y - pos.y) / Mathf.Max(dt, 0.0001f);
                if (!_grounded && (vs > 9f || Mathf.Abs(p.Roll) > 25f)) { Crash("Crash landing!"); return; }
                if (!_grounded) { _grounded = true; GameAudio.Play("step", 1f, 0.5f); FollowCamera.Shake(0.2f); }
                pos.y = Mathf.Max(floor, roofs);
                if (p.Pitch > 0f) p.Pitch = 0f;
            }
            else if (pos.y > Mathf.Max(floor, roofs) + 0.6f) _grounded = false;
            if (_grounded && roofs > 1.5f && p.Speed > 15f) { Crash("Crash! You hit something"); return; }
            p.T.SetPositionAndRotation(pos, rot);
            if (pos.y > 4000f) { pos.y = 4000f; p.T.position = pos; }
        }

        private void OnShift(Vector3 d)
        {
            foreach (var p in _planes) if (p.T != null) p.T.position -= d;
        }
    }
}
