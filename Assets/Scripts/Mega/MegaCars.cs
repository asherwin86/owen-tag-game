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
    /// <summary>Walk up to a parked car and press E to drive it.</summary>
    public class CarInteract : MegaInteractable
    {
        public MegaCars.Car Car;
        private void Start() { Prompt = "E: drive the " + MegaCars.ModelName(Car.Model); Range = 3.6f; }
        public override void Use(Vector3 userPos) { if (MegaCars.I != null) MegaCars.I.Enter(Car); }
    }

    /// <summary>
    /// Cars for Mega Mode update 2: the Volt Sedan and the Volt Roadster (made-up electric cars). Drive them yourself
    /// (inside view with a dashboard, or outside view), turn on self-driving (it follows the streets and stops at red
    /// lights), or use Auto Park (pick a parking spot and press START). Traffic lights at every crossing; people on foot
    /// wait for green too. Some bots drive around as traffic. You can be tagged through the window when your car is stopped.
    /// Keys: W/S drive, A/D steer, Space brake, F self-driving, P auto park, C camera, E get out.
    /// </summary>
    public class MegaCars : MonoBehaviour
    {
        public static MegaCars I { get; private set; }

        public class Car
        {
            public Transform T;
            public int Model;               // 0 Volt Sedan, 1 Volt Roadster
            public float Speed, Yaw, VisYaw;
            public bool Traffic;            // driven by a bot
            public bool Lanes;              // following a lane (traffic or self-driving)
            public int Axis, K, Dir;        // lane: along x (0) or z (1), which street line, +1/-1
            public Renderer[] Brakes;
            public Transform Driver;
            public CarInteract Interact;
        }

        public const float LaneOff = 3.5f, Cycle = 24f;
        public static string ModelName(int m) => m == 1 ? "Volt Roadster" : "Volt Sedan";

        /// <summary>The car you're in (or null).</summary>
        public Car Mine { get; private set; }
        public bool Driving => Mine != null;
        public bool SelfDriving { get; private set; }
        public bool Inside { get; private set; } = true;
        public string Status { get; private set; } = "";

        private readonly List<Car> _cars = new List<Car>();
        private PlayerController _player;
        private FollowCamera _follow;
        private Material _glass, _tyre, _dash, _screen, _redOn, _redOff, _head, _greenMark;
        private readonly Dictionary<Color, Material> _paint = new Dictionary<Color, Material>();
        private float _spawnAt, _lightsAt;
        private Vector3 _savedOffset;
        private Renderer[] _hidden;

        // auto park
        private readonly List<Vector3> _spots = new List<Vector3>();
        private readonly List<Transform> _spotMarks = new List<Transform>();
        private int _spotSel = -1, _parkPhase;
        private Vector3 _parkTarget;

        // ui
        private GameObject _canvas, _panel, _parkPanel;
        private TMP_Text _speedText, _statusText, _fsdLabel, _camLabel;
        private TMP_Text _dashText;
        private readonly List<Button> _spotButtons = new List<Button>();

        // traffic lights
        private class Pole { public Transform T; public Renderer R, Y, G; }
        private readonly List<Pole> _poles = new List<Pole>();
        private Material _lampR, _lampY, _lampG, _lampOff, _poleMat;

        private void Awake() { I = this; _player = FindFirstObjectByType<PlayerController>(); }
        private void OnEnable() { MegaWorld.OnShift += OnShift; }
        private void OnDisable() { MegaWorld.OnShift -= OnShift; }

        // ================================================================== rules shared with everyone

        /// <summary>Traffic light at crossing (ix, iz) for traffic along x (axis 0) or z (axis 1): 0 red, 1 yellow, 2 green.</summary>
        public static int LightState(int ix, int iz, int axis)
        {
            long wx = ix + (long)System.Math.Round(MegaWorld.OriginX / 100.0), wz = iz + (long)System.Math.Round(MegaWorld.OriginZ / 100.0);
            uint h = (uint)(wx * 73856093L ^ wz * 19349663L);
            float t = (Time.time + (h % 1000) / 1000f * Cycle) % Cycle;
            if (axis == 0) return t < 10f ? 2 : (t < 12f ? 1 : 0);
            return t >= 12f && t < 22f ? 2 : (t >= 22f ? 1 : 0);
        }

        /// <summary>Is this a real road (in a city or town, not a lake or forest)?</summary>
        public static bool RoadOK(Vector3 local)
        {
            double wx = MegaWorld.WX(local.x), wz = MegaWorld.WZ(local.z);
            int k = MegaWorld.CountryAt(wx, wz);
            if (k < 0) return false;
            var c = MegaWorld.Countries[k];
            if (System.Math.Abs(wx - c.CX) > c.Half - 60 || System.Math.Abs(wz - c.CZ) > c.Half - 60) return false;
            int ci = (int)System.Math.Floor((wx - (c.CX - c.Half)) / MegaWorld.ChunkSize), cj = (int)System.Math.Floor((wz - (c.CZ - c.Half)) / MegaWorld.ChunkSize);
            var kind = MapManager.KindOf(c, ci, cj);
            return kind == MapManager.ChunkKind.City || kind == MapManager.ChunkKind.Town;
        }

        /// <summary>True if someone on foot moving from prev to next would walk into a crossing while it's red for them.</summary>
        public static bool PedBlocked(Vector3 prev, Vector3 next)
        {
            int ix = MegaWorld.StreetIdx(next.x), iz = MegaWorld.StreetIdx(next.z);
            float sx = MegaWorld.Street(ix), sz = MegaWorld.Street(iz);
            bool inNext = Mathf.Abs(next.x - sx) < 10f && Mathf.Abs(next.z - sz) < 10f;
            bool inPrev = Mathf.Abs(prev.x - sx) < 10f && Mathf.Abs(prev.z - sz) < 10f;
            if (!inNext || inPrev || next.y > 1.5f) return false;
            if (!RoadOK(new Vector3(sx + 30f, 0f, sz + 30f))) return false;
            int axis = Mathf.Abs(prev.x - sx) >= 10f ? 0 : 1;
            return LightState(ix, iz, axis) != 2;
        }

        // ================================================================== materials and models

        private void Mats()
        {
            if (_glass != null) return;
            var r = _player != null ? _player.GetComponent<Renderer>() : null;
            Material M(Color c, float sm, float glow = 0f)
            {
                var m = r != null ? new Material(r.sharedMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.mainTexture = null; m.color = c;
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", sm);
                if (glow > 0f && m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * glow); }
                return m;
            }
            _glass = M(new Color(0.08f, 0.1f, 0.14f), 0.95f);
            _tyre = M(new Color(0.08f, 0.08f, 0.08f), 0.2f);
            _dash = M(new Color(0.12f, 0.12f, 0.13f), 0.4f);
            _screen = M(new Color(0.1f, 0.2f, 0.35f), 0.9f, 1.2f);
            _redOn = M(new Color(1f, 0.1f, 0.1f), 0.5f, 3f);
            _redOff = M(new Color(0.35f, 0.05f, 0.05f), 0.5f);
            _head = M(new Color(1f, 0.97f, 0.85f), 0.5f, 2f);
            _greenMark = M(new Color(0.2f, 1f, 0.4f), 0.3f, 2f);
            _lampR = M(new Color(1f, 0.1f, 0.08f), 0.5f, 4f);
            _lampY = M(new Color(1f, 0.75f, 0.1f), 0.5f, 4f);
            _lampG = M(new Color(0.2f, 1f, 0.35f), 0.5f, 4f);
            _lampOff = M(new Color(0.12f, 0.12f, 0.12f), 0.3f);
            _poleMat = M(new Color(0.2f, 0.21f, 0.22f), 0.5f);
        }

        private Material Paint(Color c)
        {
            if (_paint.TryGetValue(c, out var m)) return m;
            var r = _player != null ? _player.GetComponent<Renderer>() : null;
            m = r != null ? new Material(r.sharedMaterial) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            m.mainTexture = null; m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.85f);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.3f);
            _paint[c] = m;
            return m;
        }

        private static readonly Color[] Colours =
        {
            new Color(0.95f, 0.95f, 0.96f), new Color(0.1f, 0.1f, 0.12f), new Color(0.75f, 0.08f, 0.1f), new Color(0.15f, 0.3f, 0.75f),
            new Color(0.55f, 0.57f, 0.6f), new Color(0.85f, 0.85f, 0.88f),
        };

        private Car MakeCar(int model, Vector3 pos, float yaw, bool traffic)
        {
            Mats();
            var root = new GameObject(ModelName(model)).transform;
            root.SetPositionAndRotation(pos, Quaternion.Euler(0f, yaw, 0f));
            var car = new Car { T = root, Model = model, Yaw = yaw, VisYaw = yaw, Traffic = traffic };
            Color col = model == 1 ? (Random.value < 0.6f ? new Color(0.8f, 0.05f, 0.08f) : Colours[Random.Range(0, Colours.Length)]) : Colours[Random.Range(0, Colours.Length)];
            var paint = Paint(col);
            Transform P(PrimitiveType t, Vector3 lp, Vector3 ls, Material m, Vector3? rot = null)
            {
                var g = GameObject.CreatePrimitive(t);
                var c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
                g.transform.SetParent(root, false);
                g.transform.localPosition = lp; g.transform.localScale = ls;
                if (rot.HasValue) g.transform.localRotation = Quaternion.Euler(rot.Value);
                var rr = g.GetComponent<Renderer>(); rr.sharedMaterial = m;
                rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                return g.transform;
            }
            bool rd = model == 1;
            float L = rd ? 4.3f : 4.8f, W = rd ? 1.95f : 1.95f, H = rd ? 0.55f : 0.75f;
            P(PrimitiveType.Cube, new Vector3(0f, 0.35f + H * 0.5f, 0f), new Vector3(W, H, L), paint);                         // body
            P(PrimitiveType.Cube, new Vector3(0f, 0.3f + H * 0.4f, L * 0.47f), new Vector3(W * 0.96f, H * 0.7f, 0.3f), paint);   // nose
            if (rd)
            {
                P(PrimitiveType.Cube, new Vector3(0f, 0.35f + H + 0.25f, -0.2f), new Vector3(W * 0.82f, 0.5f, 1.7f), _glass);      // low cabin
                P(PrimitiveType.Cube, new Vector3(0f, 0.35f + H + 0.18f, -L * 0.45f), new Vector3(W * 0.9f, 0.06f, 0.4f), paint); // spoiler
            }
            else
            {
                P(PrimitiveType.Cube, new Vector3(0f, 0.35f + H + 0.32f, -0.15f), new Vector3(W * 0.86f, 0.64f, 2.6f), _glass);    // cabin
                P(PrimitiveType.Cube, new Vector3(0f, 0.35f + H + 0.65f, -0.25f), new Vector3(W * 0.84f, 0.05f, 2.1f), paint);    // roof
            }
            float wy = 0.36f;
            foreach (var w in new[] { new Vector3(-W * 0.5f, wy, L * 0.32f), new Vector3(W * 0.5f, wy, L * 0.32f), new Vector3(-W * 0.5f, wy, -L * 0.32f), new Vector3(W * 0.5f, wy, -L * 0.32f) })
                P(PrimitiveType.Cylinder, w, new Vector3(0.72f, 0.14f, 0.72f), _tyre, new Vector3(0f, 0f, 90f));
            P(PrimitiveType.Cube, new Vector3(-W * 0.36f, 0.35f + H * 0.6f, L * 0.5f), new Vector3(0.38f, 0.12f, 0.05f), _head);
            P(PrimitiveType.Cube, new Vector3(W * 0.36f, 0.35f + H * 0.6f, L * 0.5f), new Vector3(0.38f, 0.12f, 0.05f), _head);
            var b1 = P(PrimitiveType.Cube, new Vector3(-W * 0.36f, 0.35f + H * 0.65f, -L * 0.5f), new Vector3(0.42f, 0.1f, 0.05f), _redOff);
            var b2 = P(PrimitiveType.Cube, new Vector3(W * 0.36f, 0.35f + H * 0.65f, -L * 0.5f), new Vector3(0.42f, 0.1f, 0.05f), _redOff);
            car.Brakes = new[] { b1.GetComponent<Renderer>(), b2.GetComponent<Renderer>() };
            // inside: dashboard, steering wheel and the big centre screen
            P(PrimitiveType.Cube, new Vector3(0f, 0.35f + H + 0.02f, rd ? 0.55f : 0.95f), new Vector3(W * 0.8f, 0.14f, 0.45f), _dash);
            P(PrimitiveType.Cylinder, new Vector3(-0.38f, 0.35f + H + 0.18f, rd ? 0.3f : 0.7f), new Vector3(0.36f, 0.02f, 0.36f), _dash, new Vector3(70f, 0f, 0f));
            P(PrimitiveType.Cube, new Vector3(0.05f, 0.35f + H + 0.25f, rd ? 0.38f : 0.78f), new Vector3(0.5f, 0.32f, 0.03f), _screen, new Vector3(-15f, 0f, 0f));
            if (traffic)
            {
                var d = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var dc = d.GetComponent<Collider>(); if (dc != null) Destroy(dc);
                d.transform.SetParent(root, false);
                d.transform.localPosition = new Vector3(-0.4f, 0.35f + H + 0.3f, -0.1f); d.transform.localScale = Vector3.one * 0.45f;
                d.GetComponent<Renderer>().sharedMaterial = Paint(Color.HSVToRGB(Random.value, 0.6f, 0.9f));
                car.Driver = d.transform;
            }
            else
            {
                car.Interact = root.gameObject.AddComponent<CarInteract>();
                car.Interact.Car = car;
            }
            _cars.Add(car);
            return car;
        }

        private void DestroyCar(Car c)
        {
            if (c.T != null) Destroy(c.T.gameObject);
            _cars.Remove(c);
        }

        // ================================================================== streets: spawning traffic and parked cars

        private bool LaneSpot(Vector3 near, float minD, float maxD, out int axis, out int k, out int dir, out float along)
        {
            axis = 0; k = 0; dir = 1; along = 0f;
            for (int t = 0; t < 30; t++)
            {
                axis = Random.value < 0.5f ? 0 : 1;
                dir = Random.value < 0.5f ? 1 : -1;
                int span = Mathf.CeilToInt(maxD / 100f);
                k = MegaWorld.StreetIdx(axis == 0 ? near.z : near.x) + Random.Range(-span, span + 1);
                along = (axis == 0 ? near.x : near.z) + Random.Range(-maxD, maxD);
                along = Mathf.Round(along / 100f) * 100f + Random.Range(20f, 80f);
                Vector3 p = LanePos(axis, k, dir, along, LaneOff);
                float d = Flat(p - near);
                if (d < minD || d > maxD) continue;
                if (!RoadOK(p)) continue;
                if (MegaWorld.Walk != null && MegaWorld.Walk.IsBlocked(p.x, p.z)) continue;
                return true;
            }
            return false;
        }

        private static Vector3 LanePos(int axis, int k, int dir, float along, float off)
        {
            float line = MegaWorld.Street(k);
            return axis == 0 ? new Vector3(along, 0f, line - off * dir) : new Vector3(line + off * dir, 0f, along);
        }

        private static float LaneYaw(int axis, int dir) => axis == 0 ? (dir > 0 ? 90f : 270f) : (dir > 0 ? 0f : 180f);
        private static float Flat(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);

        private void Populate()
        {
            Vector3 me = _player.transform.position;
            float fps = Performance.Fps;
            int wantTraffic = fps < 30f ? 10 : 28, wantParked = 14;
            int traffic = 0, parked = 0;
            for (int i = _cars.Count - 1; i >= 0; i--)
            {
                var c = _cars[i];
                if (c == Mine) continue;
                if (c.T == null) { _cars.RemoveAt(i); continue; }
                float d = Flat(c.T.position - me);
                if (d > (c.Traffic ? 380f : 450f)) { DestroyCar(c); continue; }
                if (c.Traffic) traffic++; else parked++;
            }
            for (int n = 0; n < 3 && traffic < wantTraffic; n++)
            {
                if (!LaneSpot(me, 140f, 300f, out int axis, out int k, out int dir, out float along)) break;
                var c = MakeCar(Random.value < 0.25f ? 1 : 0, LanePos(axis, k, dir, along, LaneOff), LaneYaw(axis, dir), true);
                c.Lanes = true; c.Axis = axis; c.K = k; c.Dir = dir; c.Speed = 8f;
                traffic++;
            }
            for (int n = 0; n < 3 && parked < wantParked; n++)
            {
                if (!LaneSpot(me, 25f, 220f, out int axis, out int k, out int dir, out float along)) break;
                Vector3 p = LanePos(axis, k, dir, along, 6.9f);
                if (Physics.CheckBox(p + Vector3.up, new Vector3(1.2f, 0.7f, 2.6f), Quaternion.Euler(0f, LaneYaw(axis, dir), 0f), ~0, QueryTriggerInteraction.Ignore)) continue;
                MakeCar(Random.value < 0.35f ? 1 : 0, p, LaneYaw(axis, dir), false);
                parked++;
            }
        }

        // ================================================================== driving along lanes (traffic + self-driving)

        private float LaneAlong(Car c) => c.Axis == 0 ? c.T.position.x : c.T.position.z;

        private void LaneStep(Car c, float dt, float cruise)
        {
            float pos = LaneAlong(c);
            int nextIdx = c.Dir > 0 ? Mathf.FloorToInt(pos / 100f) + 1 : Mathf.CeilToInt(pos / 100f) - 1;
            float next = MegaWorld.Street(nextIdx);
            float toCentre = (next - pos) * c.Dir;
            float stopDist = toCentre - 12f;
            int ix = c.Axis == 0 ? nextIdx : c.K, iz = c.Axis == 0 ? c.K : nextIdx;
            float target = cruise;
            string why = "";
            int light = LightState(ix, iz, c.Axis);
            if (light != 2 && stopDist > -0.5f && stopDist < 28f)
            {
                target = stopDist < 1f ? 0f : Mathf.Min(target, stopDist * 0.55f);
                why = light == 1 ? "Slowing for a yellow light" : "Stopping for a red light";
            }
            // someone in front (another car, a person, you)
            Vector3 fwd = Quaternion.Euler(0f, LaneYaw(c.Axis, c.Dir), 0f) * Vector3.forward;
            foreach (var o in _cars)
            {
                if (o == c || o.T == null) continue;
                Vector3 d = o.T.position - c.T.position;
                float ahead = Vector3.Dot(d, fwd), side = Vector3.Dot(d, Quaternion.Euler(0f, 90f, 0f) * fwd);
                if (ahead > 0f && ahead < 14f && Mathf.Abs(side) < 1.6f) { target = Mathf.Min(target, Mathf.Max(0f, (ahead - 6.5f) * 1.2f)); if (why == "") why = "Following the car ahead"; }
            }
            var g = MegaGame.I;
            if (g != null && g.Running)
                foreach (var a in g.Agents)
                {
                    if (a.Out) continue;
                    if (a.IsPlayer && Mine == c) continue;
                    Vector3 d = a.Pos - c.T.position;
                    float ahead = Vector3.Dot(d, fwd), side = Vector3.Dot(d, Quaternion.Euler(0f, 90f, 0f) * fwd);
                    if (ahead > 0f && ahead < 10f && Mathf.Abs(side) < 1.8f) { target = 0f; why = "Waiting for someone to cross"; }
                }
            float acc = target > c.Speed ? 5f : 11f;
            c.Speed = Mathf.MoveTowards(c.Speed, target, acc * dt);
            float np = pos + c.Dir * c.Speed * dt;
            // passing the middle of a crossing: maybe turn
            if ((next - pos) * c.Dir > 0f && (next - np) * c.Dir <= 0f)
            {
                int oldK = c.K, oldAxis = c.Axis;
                float roll = Random.value;
                bool straightOk = RoadOK(LanePos(c.Axis, c.K, c.Dir, next + c.Dir * 50f, 0f));
                int turnDir = Random.value < 0.5f ? 1 : -1;
                bool turnOk = RoadOK(LanePos(1 - c.Axis, nextIdx, turnDir, MegaWorld.Street(oldK) + turnDir * 50f, 0f));
                bool otherOk = RoadOK(LanePos(1 - c.Axis, nextIdx, -turnDir, MegaWorld.Street(oldK) - turnDir * 50f, 0f));
                if ((roll < 0.35f || !straightOk) && (turnOk || otherOk))
                {
                    if (!turnOk) turnDir = -turnDir;
                    c.Axis = 1 - oldAxis; c.K = nextIdx; c.Dir = turnDir;
                    Vector3 p = LanePos(c.Axis, c.K, c.Dir, MegaWorld.Street(oldK) + c.Dir * 2f, LaneOff);
                    c.T.position = p;
                    c.Yaw = LaneYaw(c.Axis, c.Dir);
                    return;
                }
                if (!straightOk) { c.Dir = -c.Dir; c.Yaw = LaneYaw(c.Axis, c.Dir); c.T.position = LanePos(c.Axis, c.K, c.Dir, np, LaneOff); return; }
            }
            c.T.position = LanePos(c.Axis, c.K, c.Dir, np, LaneOff);
            c.Yaw = LaneYaw(c.Axis, c.Dir);
            if (c == Mine) Status = why != "" ? why : (c.Speed > 1f ? "Self-driving" : "Self-driving: waiting");
        }

        // ================================================================== you driving

        public void Enter(Car c)
        {
            var g = MegaGame.I;
            if (Driving || c == null || g == null || !g.Running || g.Me == null) return;
            Mine = c;
            c.Traffic = false; c.Lanes = false; c.Speed = 0f;
            if (c.Interact != null) c.Interact.enabled = false;
            SelfDriving = false; _parkPhase = 0; ClearSpots();
            if (_player.Controller != null) _player.Controller.enabled = false;
            _player.Frozen = true;
            _hidden = _player.GetComponentsInChildren<Renderer>();
            foreach (var r in _hidden) r.enabled = false;
            _follow = FindFirstObjectByType<FollowCamera>();
            if (_follow != null) _savedOffset = _follow.Offset;
            Inside = true;
            ApplyCamera();
            BuildUi();
            _panel.SetActive(true);
            GameAudio.Play("pickup", 0.6f, 0.6f);
            TagPopup.Show("Driving the " + ModelName(c.Model) + "!  W/S drive, A/D steer, F self-driving, P auto park, C camera, E get out", new Color(0.2f, 0.6f, 0.95f));
        }

        public void Exit()
        {
            var c = Mine;
            if (c == null) return;
            Mine = null; SelfDriving = false; _parkPhase = 0; ClearSpots();
            if (c.Interact != null) c.Interact.enabled = true;
            Vector3 side = c.T.position - c.T.right * 2.4f; side.y = 1.2f;
            if (MegaWorld.Walk != null && MegaWorld.Walk.IsBlocked(side.x, side.z)) { side = c.T.position + c.T.right * 2.4f; side.y = 1.2f; }
            if (_player.Controller != null) _player.Controller.enabled = true;
            _player.TeleportTo(side, Quaternion.Euler(0f, c.Yaw, 0f));
            _player.Frozen = false;
            if (_hidden != null) foreach (var r in _hidden) if (r != null) r.enabled = true;
            if (_follow != null) { _follow.enabled = true; _follow.Offset = _savedOffset; }
            if (_panel != null) _panel.SetActive(false);
            if (_parkPanel != null) _parkPanel.SetActive(false);
            var g = MegaGame.I; if (g != null && g.Me != null) g.Me.CarMoving = false;
            Status = "";
        }

        /// <summary>Round over or quit: leave the car and clear the streets.</summary>
        public void EndAll()
        {
            if (Driving) Exit();
            foreach (var c in _cars) if (c.T != null) Destroy(c.T.gameObject);
            _cars.Clear();
            foreach (var p in _poles) if (p.T != null) p.T.gameObject.SetActive(false);
        }

        private void ApplyCamera()
        {
            if (_follow == null) return;
            if (Inside) _follow.enabled = false;
            else { _follow.enabled = true; _follow.Offset = new Vector3(0f, 4.2f, -9.5f); }
        }

        public void ToggleCamera() { Inside = !Inside; ApplyCamera(); }

        public void ToggleSelfDriving()
        {
            if (Mine == null) return;
            SelfDriving = !SelfDriving;
            _parkPhase = 0; ClearSpots();
            if (SelfDriving)
            {
                SnapToLane(Mine);
                TagPopup.Show("Self-driving on: it follows the streets and stops for red lights", new Color(0.2f, 0.7f, 0.4f));
            }
            else { Mine.Lanes = false; Status = ""; }
            GameAudio.Play("beep", 0.5f, SelfDriving ? 1.4f : 0.9f);
        }

        private void SnapToLane(Car c)
        {
            float s = Mathf.Sin(c.Yaw * Mathf.Deg2Rad), co = Mathf.Cos(c.Yaw * Mathf.Deg2Rad);
            c.Axis = Mathf.Abs(s) > Mathf.Abs(co) ? 0 : 1;
            c.Dir = c.Axis == 0 ? (s > 0 ? 1 : -1) : (co > 0 ? 1 : -1);
            c.K = MegaWorld.StreetIdx(c.Axis == 0 ? c.T.position.z : c.T.position.x);
            c.Lanes = true;
            c.T.position = LanePos(c.Axis, c.K, c.Dir, LaneAlong(c), LaneOff);
            c.Yaw = LaneYaw(c.Axis, c.Dir);
        }

        // ---- auto park

        public void OpenAutoPark()
        {
            if (Mine == null) return;
            SelfDriving = false;
            Mine.Speed = 0f;
            SnapToLane(Mine);
            Mine.Lanes = false;
            ClearSpots();
            float along = LaneAlong(Mine);
            for (float d = 8f; d <= 60f && _spots.Count < 3; d += 6f)
            {
                float a = along + Mine.Dir * d;
                Vector3 p = LanePos(Mine.Axis, Mine.K, Mine.Dir, a, 6.9f);
                float mid = Mathf.Abs(a - Mathf.Round(a / 100f) * 100f);
                if (mid < 16f) continue;                       // not in a crossing
                if (Physics.CheckBox(p + Vector3.up, new Vector3(1.1f, 0.7f, 2.5f), Quaternion.Euler(0f, Mine.Yaw, 0f), ~0, QueryTriggerInteraction.Ignore)) continue;
                bool near = false; foreach (var s in _spots) if ((s - p).sqrMagnitude < 30f) near = true;
                foreach (var oc in _cars) if (oc != Mine && oc.T != null && (oc.T.position - p).sqrMagnitude < 25f) near = true;
                if (near) continue;
                _spots.Add(p);
                var m = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = m.GetComponent<Collider>(); if (col != null) Destroy(col);
                m.transform.position = p + Vector3.up * 0.05f;
                m.transform.rotation = Quaternion.Euler(0f, Mine.Yaw, 0f);
                m.transform.localScale = new Vector3(2.3f, 0.06f, 5f);
                m.GetComponent<Renderer>().sharedMaterial = _greenMark;
                _spotMarks.Add(m.transform);
            }
            BuildUi();
            for (int i = 0; i < _spotButtons.Count; i++)
            {
                bool has = i < _spots.Count;
                _spotButtons[i].gameObject.SetActive(has);
                if (has) _spotButtons[i].GetComponentInChildren<TMP_Text>().text = "Spot " + (i + 1) + "\n<size=18>" + Mathf.RoundToInt(Flat(_spots[i] - Mine.T.position)) + " m</size>";
            }
            _spotSel = _spots.Count > 0 ? 0 : -1;
            HighlightSpots();
            _parkPanel.SetActive(true);
            if (_spots.Count == 0) TagPopup.Show("No free parking spots here - drive on a bit", new Color(0.6f, 0.6f, 0.6f));
        }

        private void HighlightSpots()
        {
            for (int i = 0; i < _spotMarks.Count; i++) _spotMarks[i].localScale = i == _spotSel ? new Vector3(2.4f, 0.12f, 5.2f) : new Vector3(2.1f, 0.04f, 4.6f);
            for (int i = 0; i < _spotButtons.Count; i++) _spotButtons[i].GetComponent<Image>().color = i == _spotSel ? new Color(0.15f, 0.65f, 0.3f) : new Color(0.2f, 0.22f, 0.28f);
        }

        private void StartPark()
        {
            if (Mine == null || _spotSel < 0 || _spotSel >= _spots.Count) return;
            _parkTarget = _spots[_spotSel];
            _parkPhase = 1;
            _parkPanel.SetActive(false);
            Status = "Auto parking...";
            GameAudio.Play("beep", 0.5f, 1.2f);
        }

        private void ClearSpots()
        {
            foreach (var m in _spotMarks) if (m != null) Destroy(m.gameObject);
            _spotMarks.Clear(); _spots.Clear(); _spotSel = -1;
            if (_parkPanel != null) _parkPanel.SetActive(false);
        }

        private void ParkStep(float dt)
        {
            var c = Mine;
            Vector3 lanePoint = LanePos(c.Axis, c.K, c.Dir, c.Axis == 0 ? _parkTarget.x : _parkTarget.z, LaneOff);
            if (_parkPhase == 1)
            {
                Vector3 d = lanePoint - c.T.position; d.y = 0f;
                float dist = d.magnitude;
                c.Speed = Mathf.Min(5f, dist * 0.8f + 0.5f);
                if (dist < 0.3f) { _parkPhase = 2; c.Speed = 0f; return; }
                c.T.position += d / Mathf.Max(dist, 0.001f) * Mathf.Min(dist, c.Speed * dt);
            }
            else if (_parkPhase == 2)
            {
                Vector3 d = _parkTarget - c.T.position; d.y = 0f;
                float dist = d.magnitude;
                c.Speed = 1.5f;
                c.VisYaw = c.Yaw + Mathf.Sin(Mathf.Clamp01(1f - dist / 3.4f) * Mathf.PI) * 18f * (c.Axis == 0 ? -c.Dir : c.Dir);
                if (dist < 0.05f)
                {
                    _parkPhase = 0; c.Speed = 0f; c.VisYaw = c.Yaw; c.T.position = _parkTarget; ClearSpots();
                    Status = "Parked";
                    GameAudio.Play("tie", 0.8f, 1.2f);
                    TagPopup.Show("Parked!", new Color(0.2f, 0.8f, 0.35f));
                    return;
                }
                c.T.position += d / dist * Mathf.Min(dist, 1.5f * dt);
            }
        }

        private void ManualStep(Car c, float dt)
        {
            var kb = Keyboard.current; var pad = Gamepad.current;
            float throttle = 0f, steer = 0f; bool brake = false;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) throttle += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) throttle -= 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) steer -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) steer += 1f;
                brake = kb.spaceKey.isPressed;
            }
            if (pad != null)
            {
                throttle += pad.rightTrigger.ReadValue() - pad.leftTrigger.ReadValue();
                float sx = pad.leftStick.ReadValue().x; if (Mathf.Abs(sx) > 0.1f) steer = sx;
            }
            var mv = MobileControls.MoveInput;
            if (mv.sqrMagnitude > 0.01f) { throttle = mv.y; steer = mv.x; }
            if ((Mathf.Abs(throttle) > 0.1f || Mathf.Abs(steer) > 0.1f) && (SelfDriving || _parkPhase > 0))
            {
                SelfDriving = false; c.Lanes = false; _parkPhase = 0; ClearSpots(); Status = "";
                TagPopup.Show("You took over the wheel", new Color(0.6f, 0.6f, 0.6f));
            }
            float max = c.Model == 1 ? 40f : 28f, acc = c.Model == 1 ? 14f : 8f;
            if (throttle > 0.1f) c.Speed = c.Speed < -0.5f ? c.Speed + 14f * dt : Mathf.Min(max, c.Speed + acc * throttle * dt);
            else if (throttle < -0.1f) c.Speed = c.Speed > 0.5f ? c.Speed - 14f * dt : Mathf.Max(-7f, c.Speed + 5f * throttle * dt);
            else c.Speed = Mathf.MoveTowards(c.Speed, 0f, 2.5f * dt);
            if (brake) c.Speed = Mathf.MoveTowards(c.Speed, 0f, 20f * dt);
            float turn = steer * Mathf.Clamp(Mathf.Abs(c.Speed) / 6f, 0f, 1f) * (c.Model == 1 ? 75f : 60f) * Mathf.Sign(c.Speed == 0f ? 1f : c.Speed);
            c.Yaw += turn * dt;
            Vector3 fwd = Quaternion.Euler(0f, c.Yaw, 0f) * Vector3.forward;
            Vector3 step = fwd * c.Speed * dt;
            Vector3 front = c.T.position + fwd * Mathf.Sign(c.Speed) * 2.6f + step;
            bool blocked = MegaWorld.Walk == null || MegaWorld.Walk.IsBlocked(front.x, front.z);
            if (!blocked && Physics.Raycast(c.T.position + Vector3.up * 0.7f, fwd * Mathf.Sign(c.Speed), out var hit, 2.6f + Mathf.Abs(c.Speed) * dt, ~0, QueryTriggerInteraction.Ignore)
                && hit.collider.gameObject != _player.gameObject) blocked = true;
            foreach (var o in _cars)
            {
                if (o == c || o.T == null) continue;
                if ((o.T.position - front).sqrMagnitude < 6f) blocked = true;
            }
            if (blocked)
            {
                if (Mathf.Abs(c.Speed) > 7f) { GameAudio.Play("knock", 0.7f, 0.8f); FollowCamera.Shake(0.3f); TagPopup.Show("Bump!", new Color(0.9f, 0.4f, 0.2f)); }
                c.Speed = -c.Speed * 0.2f;
                return;
            }
            c.T.position += step;
            Status = c.Speed < -0.3f ? "Reversing" : "";
        }

        private void PushPeopleAside(Car c)
        {
            var g = MegaGame.I;
            if (g == null || Mathf.Abs(c.Speed) < 2f) return;
            Vector3 fwd = Quaternion.Euler(0f, c.Yaw, 0f) * Vector3.forward;
            foreach (var a in g.Agents)
            {
                if (a.IsPlayer || a.Out) continue;
                Vector3 d = a.Pos - c.T.position;
                float ahead = Vector3.Dot(d, fwd);
                Vector3 side = d - fwd * ahead;
                if (ahead > -2.6f && ahead < 3.2f && side.magnitude < 1.5f)
                {
                    Vector3 push = side.sqrMagnitude > 0.01f ? side.normalized : Vector3.Cross(Vector3.up, fwd);
                    a.Pos += push * (1.6f - side.magnitude);   // they jump out of the way
                }
            }
        }

        // ================================================================== main loop

        private void Update()
        {
            var g = MegaGame.I;
            if (g == null || !g.Running) { if (_cars.Count > 0 || Driving) EndAll(); return; }
            if (g.Paused) return;
            float dt = Time.deltaTime;
            if (Time.unscaledTime >= _spawnAt) { _spawnAt = Time.unscaledTime + 0.5f; Populate(); }
            if (Time.unscaledTime >= _lightsAt) { _lightsAt = Time.unscaledTime + 0.25f; UpdateLights(); }

            foreach (var c in _cars)
            {
                if (c.T == null || c == Mine) continue;
                if (c.Traffic && c.Lanes) LaneStep(c, dt, c.Model == 1 ? 15f : 12f);
            }
            if (Mine != null)
            {
                var kb = Keyboard.current; var pad = Gamepad.current;
                if (kb != null && kb.fKey.wasPressedThisFrame) ToggleSelfDriving();
                if (kb != null && kb.pKey.wasPressedThisFrame) OpenAutoPark();
                if ((kb != null && kb.cKey.wasPressedThisFrame) || (pad != null && pad.buttonNorth.wasPressedThisFrame)) ToggleCamera();
                if (_parkPanel != null && _parkPanel.activeSelf && kb != null)
                {
                    if (kb.digit1Key.wasPressedThisFrame && _spots.Count > 0) { _spotSel = 0; HighlightSpots(); }
                    if (kb.digit2Key.wasPressedThisFrame && _spots.Count > 1) { _spotSel = 1; HighlightSpots(); }
                    if (kb.digit3Key.wasPressedThisFrame && _spots.Count > 2) { _spotSel = 2; HighlightSpots(); }
                    if (kb.enterKey.wasPressedThisFrame) StartPark();
                }
                bool exit = (kb != null && kb.eKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame) || Time.frameCount - MegaPlayer.TouchUseFrame <= 1;
                if (exit && Mathf.Abs(Mine.Speed) < 2f) { Exit(); return; }
                if (_parkPhase > 0) ParkStep(dt);
                else if (SelfDriving) LaneStep(Mine, dt, Mine.Model == 1 ? 14f : 12f);
                else ManualStep(Mine, dt);
                if (Mine == null) return;
                PushPeopleAside(Mine);
                // you ride along: tagging works through the window when the car is stopped
                _player.transform.position = Mine.T.position + Vector3.up * 1.1f;
                _player.transform.rotation = Quaternion.Euler(0f, Mine.Yaw, 0f);
                if (g.Me != null) { g.Me.CarMoving = Mathf.Abs(Mine.Speed) > 1.5f; g.Me.InCar = true; }
                if (!Inside && _follow != null) _follow.ForceYaw(Mine.Yaw);
                UpdateUi();
            }
            else if (g.Me != null) { g.Me.CarMoving = false; g.Me.InCar = false; }

            foreach (var c in _cars)
            {
                if (c.T == null) continue;
                if (c != Mine || _parkPhase != 2) c.VisYaw = Mathf.LerpAngle(c.VisYaw, c.Yaw, Mathf.Clamp01(dt * 8f));
                c.T.rotation = Quaternion.Euler(0f, c.VisYaw, 0f);
                bool braking = c.Speed < 2f || (c.Traffic && c.Speed < 6f);
                if (c.Brakes != null) foreach (var b in c.Brakes) if (b != null) b.sharedMaterial = braking ? _redOn : _redOff;
            }
        }

        private void LateUpdate()
        {
            if (Mine == null || !Inside) return;
            var cam = Camera.main;
            if (cam == null) return;
            bool rd = Mine.Model == 1;
            cam.transform.position = Mine.T.TransformPoint(new Vector3(-0.38f, rd ? 1.18f : 1.4f, rd ? -0.35f : 0.05f));
            cam.transform.rotation = Quaternion.Euler(7f, Mine.VisYaw, 0f);
        }

        // ================================================================== traffic lights near you

        private void UpdateLights()
        {
            Mats();
            Vector3 me = _player.transform.position;
            int used = 0;
            int cx = MegaWorld.StreetIdx(me.x), cz = MegaWorld.StreetIdx(me.z);
            for (int ix = cx - 2; ix <= cx + 2; ix++)
                for (int iz = cz - 2; iz <= cz + 2; iz++)
                {
                    float sx = MegaWorld.Street(ix), sz = MegaWorld.Street(iz);
                    if (Mathf.Abs(sx - me.x) > 190f || Mathf.Abs(sz - me.z) > 190f) continue;
                    if (!RoadOK(new Vector3(sx + 30f, 0f, sz + 30f))) continue;
                    // one light for traffic along x, one for traffic along z, on opposite corners
                    for (int axis = 0; axis < 2; axis++)
                    {
                        if (used >= _poles.Count) _poles.Add(MakePole());
                        var p = _poles[used++];
                        p.T.gameObject.SetActive(true);
                        p.T.position = axis == 0 ? new Vector3(sx - 9.2f, 0f, sz - 9.2f) : new Vector3(sx + 9.2f, 0f, sz + 9.2f);
                        p.T.rotation = Quaternion.Euler(0f, axis == 0 ? 270f : 180f, 0f);
                        int s = LightState(ix, iz, axis);
                        p.R.sharedMaterial = s == 0 ? _lampR : _lampOff;
                        p.Y.sharedMaterial = s == 1 ? _lampY : _lampOff;
                        p.G.sharedMaterial = s == 2 ? _lampG : _lampOff;
                    }
                }
            for (int i = used; i < _poles.Count; i++) _poles[i].T.gameObject.SetActive(false);
        }

        private Pole MakePole()
        {
            var root = new GameObject("TrafficLight").transform;
            Renderer Part(PrimitiveType t, Vector3 lp, Vector3 ls, Material m)
            {
                var g = GameObject.CreatePrimitive(t);
                var c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
                g.transform.SetParent(root, false); g.transform.localPosition = lp; g.transform.localScale = ls;
                var r = g.GetComponent<Renderer>(); r.sharedMaterial = m;
                return r;
            }
            Part(PrimitiveType.Cube, new Vector3(0f, 2.2f, 0f), new Vector3(0.18f, 4.4f, 0.18f), _poleMat);
            Part(PrimitiveType.Cube, new Vector3(0f, 4.4f, 0f), new Vector3(0.45f, 1.3f, 0.35f), _poleMat);
            var p = new Pole { T = root };
            p.R = Part(PrimitiveType.Sphere, new Vector3(0f, 4.82f, 0.19f), Vector3.one * 0.3f, _lampOff);
            p.Y = Part(PrimitiveType.Sphere, new Vector3(0f, 4.42f, 0.19f), Vector3.one * 0.3f, _lampOff);
            p.G = Part(PrimitiveType.Sphere, new Vector3(0f, 4.02f, 0.19f), Vector3.one * 0.3f, _lampOff);
            return p;
        }

        // ================================================================== car screen (overlay) and the dashboard screen

        private void BuildUi()
        {
            if (_canvas != null) return;
            _canvas = new GameObject("CarCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var cv = _canvas.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 42;
            var sc = _canvas.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;
            _panel = new GameObject("CarScreen", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(_canvas.transform, false);
            var rt = (RectTransform)_panel.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 205f); rt.sizeDelta = new Vector2(900f, 150f);
            _panel.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 0.82f);
            _speedText = MG.MakeText(_panel.transform, "0", 64, new Vector2(0f, 0.5f), new Vector2(110f, 12f), new Vector2(200f, 80f));
            _speedText.fontStyle = FontStyles.Bold;
            MG.MakeText(_panel.transform, "km/h", 20, new Vector2(0f, 0.5f), new Vector2(110f, -38f), new Vector2(200f, 30f)).color = new Color(0.7f, 0.75f, 0.8f);
            _statusText = MG.MakeText(_panel.transform, "", 22, new Vector2(0.5f, 1f), new Vector2(60f, -22f), new Vector2(600f, 30f));
            _statusText.color = new Color(0.55f, 0.85f, 1f);
            var fsd = MG.MakeButton(_panel.transform, "SELF-DRIVE\n<size=16>F</size>", new Vector2(0.5f, 0.5f), new Vector2(-110f, -12f), new Vector2(170f, 84f), new Color(0.2f, 0.45f, 0.85f), ToggleSelfDriving, 22);
            _fsdLabel = fsd.GetComponentInChildren<TMP_Text>();
            MG.MakeButton(_panel.transform, "AUTO PARK\n<size=16>P</size>", new Vector2(0.5f, 0.5f), new Vector2(70f, -12f), new Vector2(170f, 84f), new Color(0.15f, 0.6f, 0.35f), OpenAutoPark, 22);
            var cam = MG.MakeButton(_panel.transform, "CAMERA\n<size=16>C</size>", new Vector2(0.5f, 0.5f), new Vector2(250f, -12f), new Vector2(150f, 84f), new Color(0.3f, 0.3f, 0.38f), ToggleCamera, 22);
            _camLabel = cam.GetComponentInChildren<TMP_Text>();
            MG.MakeButton(_panel.transform, "GET OUT\n<size=16>E</size>", new Vector2(0.5f, 0.5f), new Vector2(400f, -12f), new Vector2(120f, 84f), new Color(0.6f, 0.2f, 0.2f), () => { if (Mine != null && Mathf.Abs(Mine.Speed) < 2f) Exit(); }, 20);
            _panel.SetActive(false);

            _parkPanel = new GameObject("AutoPark", typeof(RectTransform), typeof(Image));
            _parkPanel.transform.SetParent(_canvas.transform, false);
            var pr = (RectTransform)_parkPanel.transform;
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0f);
            pr.anchoredPosition = new Vector2(0f, 375f); pr.sizeDelta = new Vector2(760f, 190f);
            _parkPanel.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 0.9f);
            MG.MakeText(_parkPanel.transform, "AUTO PARK - pick a spot (1-3), then START", 24, new Vector2(0.5f, 1f), new Vector2(0f, -24f), new Vector2(740f, 34f));
            _spotButtons.Clear();
            for (int i = 0; i < 3; i++)
            {
                int id = i;
                _spotButtons.Add(MG.MakeButton(_parkPanel.transform, "Spot " + (i + 1), new Vector2(0.5f, 0.5f), new Vector2(-260f + i * 160f, -20f), new Vector2(150f, 80f), new Color(0.2f, 0.22f, 0.28f), () => { _spotSel = id; HighlightSpots(); }, 24));
            }
            MG.MakeButton(_parkPanel.transform, "START", new Vector2(0.5f, 0.5f), new Vector2(250f, -20f), new Vector2(200f, 80f), new Color(0.15f, 0.65f, 0.3f), StartPark, 34);
            _parkPanel.SetActive(false);
        }

        private void UpdateUi()
        {
            if (_panel == null || Mine == null) return;
            int kmh = Mathf.RoundToInt(Mathf.Abs(Mine.Speed) * 3.6f);
            _speedText.text = kmh.ToString();
            string gear = _parkPhase > 0 ? "AUTO PARK" : (Mine.Speed < -0.3f ? "R" : (Mathf.Abs(Mine.Speed) < 0.3f ? "P" : "D"));
            _statusText.text = ModelName(Mine.Model) + "  ·  " + gear + (Status != "" ? "  ·  " + Status : "");
            _fsdLabel.text = SelfDriving ? "SELF-DRIVE\n<size=16>ON</size>" : "SELF-DRIVE\n<size=16>F</size>";
            _camLabel.text = Inside ? "CAMERA\n<size=16>inside</size>" : "CAMERA\n<size=16>outside</size>";
        }

        private void OnShift(Vector3 d)
        {
            foreach (var c in _cars)
            {
                if (c.T == null) continue;
                c.T.position -= d;
                c.K = MegaWorld.StreetIdx(c.Axis == 0 ? c.T.position.z : c.T.position.x);
            }
            for (int i = 0; i < _spots.Count; i++) _spots[i] -= d;
            foreach (var m in _spotMarks) if (m != null) m.position -= d;
            _parkTarget -= d;
            _lightsAt = 0f;
        }
    }
}
