using System.Collections.Generic;
using System.Text;
using UnityEngine;
using TagGame.Gameplay;
using TagGame.Juice;
using TagGame.Maps;
using TagGame.UI;

namespace TagGame.Mega
{
    /// <summary>Anyone playing a Mega Mode round: you or one of the bots.</summary>
    public class Agent
    {
        public int Id, ColorIdx;
        public string Name;
        public bool IsPlayer;
        public Vector3 Pos;            // feet
        public float Yaw;
        public bool It;
        public float Safe;             // seconds not being IT (the score)
        public float Hp = 100f, Armour;
        public float KnockedUntil, FrozenUntil, NoBackUntil;
        public Agent NoBackFrom;
        public bool Untied, Tying;
        public float TieDoneAt, LaceLockUntil, SneakersUntil, Stamina = 4f;
        public bool Sprinting;
        // bots only
        public bool HasGun; public ItemType Gun; public int Ammo; public float NextShot; public int Heals;
        public int Floor; public MegaWorld.Building Bld;
        public List<Vector3> Script; public int ScriptI;
        public List<Vector3> Path; public int PathI; public float RepathAt, ThinkAt;
        public Vector3 Goal; public bool HasGoal, WantSprint;
        public Vector3 ProgressPos; public float ProgressAt, DetourUntil; public Vector3 Detour;
        public float Accum, WanderUntil, StayUpUntil;
        public Agent Target;
        public GameObject Proxy;
        public bool Knocked => Time.time < KnockedUntil;
        public bool Busy => Knocked || Time.time < FrozenUntil || Tying;
    }

    /// <summary>
    /// Mega Mode: pass-the-tag in the 2 km Mega City with lots of bots, guns, health, items lying on the floor,
    /// shoelaces that come untied, and buildings with stairs, elevators and escalators.
    /// One manager runs every bot (cheap maths, no physics bodies) and draws them in batches.
    /// </summary>
    public class MegaGame : MonoBehaviour
    {
        public static MegaGame I { get; private set; }
        public static bool Active => I != null && I.Running;

        public const float RoundSeconds = 300f;
        public static readonly int[] BotChoices = { 0, 50, 100, 200, 400, 800 };   // 0 = Auto
        private const int AutoStart = 150, AutoMax = 600;
        private const float FreezeSeconds = 1.5f, NoBackSeconds = 3.5f, KnockSeconds = 4f, TieSeconds = 1.5f;
        private const float BotSpeed = 5.6f, SprintMul = 1.6f, UntiedMul = 0.45f, SneakerMul = 1.25f;
        private static readonly string[] FirstNames = { "Mia", "Leo", "Ava", "Max", "Zoe", "Sam", "Ivy", "Jay", "Kai", "Lily", "Noah", "Ella", "Finn", "Ruby", "Theo", "Isla", "Ben", "Cleo", "Eli", "Nina", "Owen", "Rosa", "Tom", "Uma", "Vic", "Wes", "Yara", "Zac", "Amy", "Ray" };
        private static readonly Color[] BotColors =
        {
            new Color(0.2f, 0.85f, 0.9f), new Color(0.4f, 0.85f, 0.35f), new Color(0.98f, 0.8f, 0.2f), new Color(0.75f, 0.45f, 0.95f),
            new Color(0.98f, 0.55f, 0.2f), new Color(0.95f, 0.5f, 0.8f), new Color(0.95f, 0.95f, 0.95f), new Color(0.35f, 0.5f, 0.95f),
        };

        public bool Running { get; private set; }
        public bool Paused { get; private set; }
        public bool Finished { get; private set; }
        public float TimeLeft { get; private set; }
        public bool CountingDown => Running && Time.time < _countdownUntil;
        public int BotChoice { get; private set; }               // index into BotChoices
        public bool AutoBots => BotChoices[BotChoice] == 0;
        public int BotCount => _agents.Count - 1;
        public float Fps { get; private set; } = 60f;
        public string ResultText { get; private set; } = "";
        public Agent Me { get; private set; }
        public IReadOnlyList<Agent> Agents => _agents;
        public readonly List<string> Feed = new List<string>();
        public System.Action OnFinished;
        public System.Action<float> OnPlayerHit;

        private readonly List<Agent> _agents = new List<Agent>();
        private readonly List<Agent> _its = new List<Agent>();
        private readonly Dictionary<long, List<Agent>> _hash = new Dictionary<long, List<Agent>>();
        private readonly Stack<List<Agent>> _listPool = new Stack<List<Agent>>();
        private PlayerController _player;
        private MegaPlayer _mp;
        private MapGrid _grid;
        private float _countdownUntil, _elapsed;
        private int _lastCount, _lastBeep = 99, _nextId, _frame;
        private int _pathBudget, _losBudget;
        private float _fpsT, _fpsFrames, _adjustAt;
        private float _savedFar = -1f;

        // drawing
        private Mesh _capsule, _cube, _sphere;
        private Material[] _bodyMats;
        private Material _itMat, _downMat, _eyeMat, _gunMat, _hatMat, _tracerMat;
        private readonly List<Tracer> _tracers = new List<Tracer>();
        private class Tracer { public Transform t; public float until; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<MegaGame>() != null) return;
            if (FindFirstObjectByType<PlayerController>() == null) return;
            var go = new GameObject("MegaGame");
            go.AddComponent<MegaGame>();
            go.AddComponent<MegaHud>();
        }

        private void Awake()
        {
            I = this;
            _player = FindFirstObjectByType<PlayerController>();
            BotChoice = Mathf.Clamp(PlayerPrefs.GetInt("TagGame.MegaBots", 0), 0, BotChoices.Length - 1);
        }

        private void OnDestroy() { if (I == this) I = null; }

        public void SetBotChoice(int i)
        {
            BotChoice = Mathf.Clamp(i, 0, BotChoices.Length - 1);
            PlayerPrefs.SetInt("TagGame.MegaBots", BotChoice);
            PlayerPrefs.Save();
        }

        private void EnsureMaterials()
        {
            if (_capsule != null) return;
            Mesh Grab(PrimitiveType t) { var g = GameObject.CreatePrimitive(t); var m = g.GetComponent<MeshFilter>().sharedMesh; Destroy(g); return m; }
            _capsule = Grab(PrimitiveType.Capsule); _cube = Grab(PrimitiveType.Cube); _sphere = Grab(PrimitiveType.Sphere);
            var pr = _player != null ? _player.GetComponent<Renderer>() : null;
            Material baseMat = pr != null ? pr.sharedMaterial : null;
            Material Make(Color c, float glow = 0f)
            {
                var m = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
                m.mainTexture = null; m.color = c;
                if (glow > 0f && m.HasProperty("_EmissionColor")) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", c * glow); }
                return m;
            }
            _bodyMats = new Material[BotColors.Length];
            for (int i = 0; i < BotColors.Length; i++) _bodyMats[i] = Make(BotColors[i]);
            _itMat = Make(new Color(0.95f, 0.15f, 0.15f), 0.4f);
            _downMat = Make(new Color(0.45f, 0.45f, 0.48f));
            _eyeMat = Make(Color.white);
            _gunMat = Make(new Color(0.12f, 0.12f, 0.14f));
            _hatMat = Make(new Color(1f, 0.1f, 0.1f), 1.5f);
            _tracerMat = Make(new Color(1f, 0.9f, 0.4f), 3f);
            MegaItems.Init(baseMat);
            Fx.Init(baseMat);
        }

        // ================================================================== round flow

        public void Begin()
        {
            EnsureMaterials();
            Time.timeScale = 1f;
            Paused = false; Finished = false;
            ClearAgents();
            Feed.Clear();
            if (MapManager.I != null && MapManager.I.Current != MapManager.MegaId) MapManager.I.Load(MapManager.MegaId);
            _grid = MapManager.I != null ? MapManager.I.Grid : null;
            MegaItems.Populate();

            _player.ResetForNewRound();
            _player.TeleportTo(MegaWorld.Spawn, Quaternion.identity);
            MegaWorld.Player = _player;
            _mp = _player.GetComponent<MegaPlayer>();
            if (_mp == null) _mp = _player.gameObject.AddComponent<MegaPlayer>();
            _mp.enabled = true;
            Me = new Agent { Id = _nextId++, Name = "You", IsPlayer = true, Pos = _player.transform.position };
            _mp.Begin(Me);
            _agents.Add(Me);

            var cam = Camera.main;
            if (cam != null) { if (_savedFar < 0f) _savedFar = cam.farClipPlane; cam.farClipPlane = 420f; }

            int want = AutoBots ? AutoStart : BotChoices[BotChoice];
            for (int i = 0; i < want; i++) AddBot(true);
            // starting ITs: about one per 30 people
            int its = ItTarget();
            var pool = new List<Agent>(_agents);
            for (int i = 0; i < its && pool.Count > 0; i++)
            {
                int k = Random.Range(0, pool.Count);
                SetIt(pool[k], true);
                pool.RemoveAt(k);
            }
            _elapsed = 0f;
            TimeLeft = RoundSeconds;
            _countdownUntil = Time.time + 3f;
            _lastCount = 4; _lastBeep = 99;
            _adjustAt = Time.unscaledTime + 5f; _fpsT = 0f; _fpsFrames = 0f;
            Running = true;
            AddFeed("Mega Mode: " + _agents.Count + " players, " + its + " IT");
        }

        private int ItTarget() => Mathf.Max(1, Mathf.RoundToInt(_agents.Count / 30f));

        public void Quit()
        {
            Running = false; Paused = false;
            Time.timeScale = 1f;
            ClearAgents();
            MegaItems.ClearAll();
            MegaWorld.Player = null;
            foreach (var t in _tracers) if (t.t != null) Destroy(t.t.gameObject);
            _tracers.Clear();
            if (_mp != null) { _mp.End(); _mp.enabled = false; }
            var cam = Camera.main;
            if (cam != null && _savedFar > 0f) cam.farClipPlane = _savedFar;
            MapBlips.It = null;
            TagPopup.SetStatus("");
            if (MapManager.I != null) MapManager.I.LoadSelected();
            _player.Frozen = false;
            _player.EnterLobby();
        }

        public void TogglePause()
        {
            if (!Running || Finished) return;
            Paused = !Paused;
            Time.timeScale = Paused ? 0f : 1f;
        }

        private void ClearAgents()
        {
            foreach (var a in _agents) if (a.Proxy != null) Destroy(a.Proxy);
            _agents.Clear();
            _its.Clear();
            Me = null;
        }

        private void Finish()
        {
            Finished = true;
            GameAudio.Play("buzzer");
            var rows = new List<Agent>(_agents);
            rows.Sort((a, b) => b.Safe.CompareTo(a.Safe));
            int rank = rows.IndexOf(Me) + 1;
            var sb = new StringBuilder();
            sb.Append(rank == 1 ? "<color=#FFD84A>YOU WIN MEGA MODE!</color>" : "Time's up!");
            sb.Append("\nYou were safe ").Append(Mathf.FloorToInt(Me.Safe)).Append("s - place ").Append(rank).Append(" of ").Append(rows.Count);
            sb.Append("\n\n");
            for (int i = 0; i < Mathf.Min(5, rows.Count); i++)
                sb.Append(i + 1).Append(". ").Append(rows[i].Name).Append("  ").Append(Mathf.FloorToInt(rows[i].Safe)).Append("s\n");
            ResultText = sb.ToString();
            _player.Frozen = true;
            OnFinished?.Invoke();
        }

        public int MyRank()
        {
            if (Me == null) return 0;
            int r = 1;
            foreach (var a in _agents) if (a != Me && a.Safe > Me.Safe) r++;
            return r;
        }

        public int ItCount => _its.Count;

        public void AddFeed(string s)
        {
            Feed.Add(s);
            while (Feed.Count > 6) Feed.RemoveAt(0);
        }

        // ================================================================== bots

        private void AddBot(bool atStart)
        {
            var a = new Agent { Id = _nextId++, IsPlayer = false, ColorIdx = Random.Range(0, BotColors.Length) };
            a.Name = FirstNames[a.Id % FirstNames.Length] + (a.Id >= FirstNames.Length ? " " + (a.Id / FirstNames.Length + 1) : "");
            a.Pos = RandomStreetPoint(atStart ? 40f : 90f, atStart ? 700f : 500f);
            a.Yaw = Random.Range(0f, 360f);
            a.ThinkAt = Time.time + Random.Range(0f, 0.5f);
            a.Stamina = 4f;
            _agents.Add(a);
        }

        private Vector3 RandomStreetPoint(float minFromPlayer, float maxFromPlayer)
        {
            Vector3 c = _player != null ? _player.transform.position : Vector3.zero;
            for (int tries = 0; tries < 60; tries++)
            {
                float x, z;
                if (Random.value < 0.5f) { x = Street(Random.Range(0, 24)); z = Random.Range(-950f, 950f); }
                else { z = Street(Random.Range(0, 24)); x = Random.Range(-950f, 950f); }
                x += Random.Range(-3f, 3f); z += Random.Range(-3f, 3f);
                float d = Vector2.Distance(new Vector2(x, z), new Vector2(c.x, c.z));
                if (d < minFromPlayer || d > maxFromPlayer) continue;
                if (_grid != null && _grid.IsBlocked(x, z)) continue;
                return new Vector3(x, 0f, z);
            }
            return new Vector3(Street(Random.Range(0, 24)), 0f, Street(Random.Range(0, 24)));
        }

        private void RemoveBot(Agent a)
        {
            if (a.IsPlayer) return;
            if (a.It) SetIt(a, false);
            if (a.Proxy != null) Destroy(a.Proxy);
            _agents.Remove(a);
            foreach (var o in _agents) if (o.Target == a) o.Target = null;
        }

        private static float Street(int k) => -920f + 80f * k;
        private static int StreetIdx(float v) => Mathf.Clamp(Mathf.RoundToInt((v + 920f) / 80f), 0, 23);

        private void SetIt(Agent a, bool on)
        {
            if (a.It == on) return;
            a.It = on;
            if (on)
            {
                _its.Add(a);
                if (!a.IsPlayer)
                {
                    a.Proxy = new GameObject("IT " + a.Name);
                    a.Proxy.transform.position = a.Pos;
                    MapBlips.Register(a.Proxy.transform, new Color(1f, 0.2f, 0.2f), 1.3f);
                }
            }
            else
            {
                _its.Remove(a);
                if (a.Proxy != null) { Destroy(a.Proxy); a.Proxy = null; }
            }
        }

        // ================================================================== rules

        private float TagRadius(Agent victim) => (victim.Knocked || victim.Untied || victim.Tying) ? 2.4f : 1.45f;

        private void TryTag(Agent it, Agent victim)
        {
            if (!it.It || victim.It || it == victim || it.Busy) return;
            if (victim == it.NoBackFrom && Time.time < it.NoBackUntil) return;
            SetIt(it, false);
            SetIt(victim, true);
            victim.FrozenUntil = Time.time + FreezeSeconds;
            victim.NoBackFrom = it; victim.NoBackUntil = Time.time + NoBackSeconds;
            victim.Target = null; it.Target = null;
            victim.Tying = false;
            float dist = Me != null ? Vector3.Distance(victim.Pos, Me.Pos) : 0f;
            if (dist < 60f || it.IsPlayer || victim.IsPlayer)
            {
                GameAudio.Play("tag", it.IsPlayer || victim.IsPlayer ? 1f : Mathf.Lerp(0.6f, 0.1f, dist / 60f), Random.Range(0.95f, 1.1f));
                Fx.Burst(victim.Pos, victim.IsPlayer ? new Color(0.13f, 0.84f, 0.87f) : new Color(0.95f, 0.2f, 0.2f));
            }
            if (victim.IsPlayer) { GameAudio.Play("it", 0.8f); FollowCamera.Shake(0.35f); TagPopup.Show(it.Name + " tagged you!  You're IT!", new Color(0.95f, 0.25f, 0.25f)); }
            else if (it.IsPlayer) { FollowCamera.Shake(0.15f); TagPopup.Show("You tagged " + victim.Name + "!", new Color(0.2f, 0.8f, 0.35f)); }
            if (dist < 120f || it.IsPlayer || victim.IsPlayer) AddFeed(it.Name + " tagged " + victim.Name);
        }

        /// <summary>Hurts someone. Armour soaks damage first. At zero health they're knocked down for a few seconds.</summary>
        public void Damage(Agent victim, float dmg, Agent by)
        {
            if (victim == null || victim.Knocked || !Running || Finished) return;
            float soak = Mathf.Min(victim.Armour, dmg * 0.7f);
            victim.Armour -= soak; dmg -= soak;
            victim.Hp -= dmg;
            if (victim.IsPlayer) { OnPlayerHit?.Invoke(dmg + soak); GameAudio.Play("hurt", 0.7f, Random.Range(0.9f, 1.1f)); FollowCamera.Shake(0.12f); }
            else if (by != null && by.IsPlayer) GameAudio.Play("hit", 0.5f, Random.Range(1.1f, 1.3f));
            if (victim.Hp <= 0f)
            {
                victim.Hp = 0f;
                victim.KnockedUntil = Time.time + KnockSeconds;
                victim.Tying = false;
                victim.Path = null; victim.Script = null;
                if (victim.IsPlayer) TagPopup.Show((by != null ? by.Name : "Someone") + " knocked you down!", new Color(0.6f, 0.3f, 0.9f));
                else if (by != null && by.IsPlayer) TagPopup.Show("You knocked down " + victim.Name + "!", new Color(0.6f, 0.3f, 0.9f));
                float dist = Me != null ? Vector3.Distance(victim.Pos, Me.Pos) : 0f;
                if (dist < 100f || victim.IsPlayer || (by != null && by.IsPlayer)) AddFeed((by != null ? by.Name : "?") + " knocked down " + victim.Name);
                if (dist < 60f) GameAudio.Play("knock", Mathf.Lerp(0.8f, 0.2f, dist / 60f));
            }
        }

        /// <summary>Fires a gun from origin along dir. Walls stop bullets. Returns true if someone was hit.</summary>
        public bool Shoot(Agent shooter, Vector3 origin, Vector3 dir, ItemType gun)
        {
            var w = MegaItems.Weapon(gun);
            bool any = false;
            float dToMe = Me != null ? Vector3.Distance(origin, Me.Pos) : 0f;
            if (shooter.IsPlayer || dToMe < 90f) GameAudio.Play(w.Sound, shooter.IsPlayer ? 0.8f : Mathf.Lerp(0.55f, 0.05f, dToMe / 90f), Random.Range(0.94f, 1.06f));
            for (int p = 0; p < w.Pellets; p++)
            {
                Vector3 d = Quaternion.Euler(Random.Range(-w.Spread, w.Spread) * 0.5f, Random.Range(-w.Spread, w.Spread), 0f) * dir;
                d.Normalize();
                float wall = w.Range;
                if (Physics.Raycast(origin, d, out var hit, w.Range, ~0, QueryTriggerInteraction.Ignore))
                {
                    if (_player == null || hit.collider.gameObject != _player.gameObject) wall = hit.distance;
                }
                Agent best = null; float bt = wall;
                foreach (var a in _agents)
                {
                    if (a == shooter || a.Knocked) continue;
                    Vector3 c = a.Pos + Vector3.up * 1.0f;
                    Vector3 oc = c - origin;
                    float t = Vector3.Dot(oc, d);
                    if (t < 0f || t > bt) continue;
                    float perp2 = oc.sqrMagnitude - t * t;
                    if (perp2 < 0.6f * 0.6f) { best = a; bt = t; }
                }
                if (dToMe < 120f || shooter.IsPlayer) AddTracer(origin, origin + d * bt);
                if (best != null)
                {
                    float dmg = w.Damage * (best.IsPlayer ? 0.55f : 1f);
                    Damage(best, dmg, shooter);
                    any = true;
                }
            }
            return any;
        }

        private void AddTracer(Vector3 a, Vector3 b)
        {
            Tracer tr = null;
            foreach (var t in _tracers) if (Time.time >= t.until) { tr = t; break; }
            if (tr == null)
            {
                if (_tracers.Count > 40) return;
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var c = go.GetComponent<Collider>(); if (c != null) Destroy(c);
                go.name = "Tracer";
                var r = go.GetComponent<Renderer>();
                r.sharedMaterial = _tracerMat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                tr = new Tracer { t = go.transform };
                _tracers.Add(tr);
            }
            float len = Vector3.Distance(a, b);
            if (len < 0.1f) return;
            tr.t.gameObject.SetActive(true);
            tr.t.position = (a + b) * 0.5f;
            tr.t.rotation = Quaternion.LookRotation(b - a);
            tr.t.localScale = new Vector3(0.05f, 0.05f, len);
            tr.until = Time.time + 0.06f;
        }

        // ================================================================== main loop

        private void Update()
        {
            if (!Running) return;
            // fps (for the auto bot count and the HUD)
            _fpsT += Time.unscaledDeltaTime; _fpsFrames++;
            if (_fpsT >= 1f) { Fps = _fpsFrames / _fpsT; _fpsT = 0f; _fpsFrames = 0f; }

            foreach (var t in _tracers) if (t.t != null && t.t.gameObject.activeSelf && Time.time >= t.until) t.t.gameObject.SetActive(false);
            if (Paused) { Draw(); return; }

            Me.Pos = _player.transform.position - Vector3.up * (_player.Controller != null ? _player.Controller.height * 0.5f - _player.Controller.center.y : 1f);
            Me.Yaw = _player.transform.eulerAngles.y;
            MegaItems.Tick(Me.Pos);

            if (Finished) { Draw(); return; }

            if (Time.time < _countdownUntil)
            {
                int c = Mathf.CeilToInt(_countdownUntil - Time.time);
                if (c != _lastCount) { _lastCount = c; TagPopup.Show(c.ToString(), new Color(0.2f, 0.5f, 0.95f)); GameAudio.Play("beep"); }
                _player.Frozen = true;
                Draw();
                return;
            }
            if (_lastCount > 0)
            {
                _lastCount = 0;
                GameAudio.Play("whistle");
                TagPopup.Show(Me.It ? "GO! You're IT - catch someone!" : "GO! " + _its.Count + " IT out there - run!", new Color(0.95f, 0.65f, 0.15f));
            }

            float dt = Time.deltaTime;
            _elapsed += dt;
            TimeLeft = Mathf.Max(0f, RoundSeconds - _elapsed);
            _frame++;
            _pathBudget = 3; _losBudget = 24;

            foreach (var a in _agents)
            {
                if (!a.It) a.Safe += dt;
                if (a.Hp <= 0f && !a.Knocked) { a.Hp = 70f; }   // got back up
                if (a.Tying && Time.time >= a.TieDoneAt) { a.Tying = false; a.Untied = false; if (a.IsPlayer) { GameAudio.Play("tie", 0.8f); TagPopup.Show("Laces tied!", new Color(0.2f, 0.8f, 0.35f)); } }
            }

            // bots
            Vector3 mp = Me.Pos;
            for (int i = 0; i < _agents.Count; i++)
            {
                var a = _agents[i];
                if (a.IsPlayer) continue;
                float dx = a.Pos.x - mp.x, dz = a.Pos.z - mp.z, d2 = dx * dx + dz * dz;
                int every = d2 < 130f * 130f || a.It ? 1 : (d2 < 320f * 320f ? 3 : 8);
                a.Accum += dt;
                if (every > 1 && (a.Id + _frame) % every != 0) continue;
                float step = Mathf.Min(a.Accum, 0.25f);
                a.Accum = 0f;
                TickBot(a, step, d2 < 150f * 150f);
                if (a.Proxy != null) a.Proxy.transform.position = a.Pos;
            }

            Tagging();
            KeepItCount();
            if (AutoBots) AdjustBots();

            // status line + the minimap's red ring on the nearest IT
            Agent near = null; float nd = float.MaxValue;
            foreach (var it in _its) { if (it == Me) continue; float d = (it.Pos - Me.Pos).sqrMagnitude; if (d < nd) { nd = d; near = it; } }
            MapBlips.It = Me.It ? _player.transform : (near != null && near.Proxy != null ? near.Proxy.transform : null);
            string itInfo = near != null ? "nearest IT " + Mathf.RoundToInt(Mathf.Sqrt(nd)) + "m" : "";
            TagPopup.SetStatus(Me.It
                ? "<color=#FF5050>YOU ARE IT!</color>  touch anyone  -  safe " + Mathf.FloorToInt(Me.Safe) + "s"
                : "<color=#FF5050>" + _its.Count + " IT</color> - run!  " + itInfo + "  -  safe " + Mathf.FloorToInt(Me.Safe) + "s");

            int secs = Mathf.CeilToInt(TimeLeft);
            if (secs <= 10 && secs > 0 && secs != _lastBeep) { _lastBeep = secs; GameAudio.Play("beep", 0.6f, secs <= 3 ? 1.3f : 1f); }
            Draw();
            if (TimeLeft <= 0f) Finish();
        }

        private static long HKey(int x, int z) => ((long)(x + 4000) << 20) | (long)(z + 4000);
        private const float HCell = 6f;

        private void Tagging()
        {
            foreach (var kv in _hash) { kv.Value.Clear(); _listPool.Push(kv.Value); }
            _hash.Clear();
            foreach (var a in _agents)
            {
                if (a.It) continue;
                long k = HKey(Mathf.FloorToInt(a.Pos.x / HCell), Mathf.FloorToInt(a.Pos.z / HCell));
                if (!_hash.TryGetValue(k, out var list)) { list = _listPool.Count > 0 ? _listPool.Pop() : new List<Agent>(); _hash[k] = list; }
                list.Add(a);
            }
            for (int i = _its.Count - 1; i >= 0; i--)
            {
                if (i >= _its.Count) continue;
                var it = _its[i];
                if (it.Busy) continue;
                int cx = Mathf.FloorToInt(it.Pos.x / HCell), cz = Mathf.FloorToInt(it.Pos.z / HCell);
                Agent hitA = null;
                for (int x = cx - 1; x <= cx + 1 && hitA == null; x++)
                    for (int z = cz - 1; z <= cz + 1 && hitA == null; z++)
                    {
                        if (!_hash.TryGetValue(HKey(x, z), out var list)) continue;
                        foreach (var v in list)
                        {
                            if (v.It) continue;
                            if (Mathf.Abs(v.Pos.y - it.Pos.y) > 1.6f) continue;
                            float r = TagRadius(v);
                            float dx = v.Pos.x - it.Pos.x, dz = v.Pos.z - it.Pos.z;
                            if (dx * dx + dz * dz < r * r && !(v == it.NoBackFrom && Time.time < it.NoBackUntil)) { hitA = v; break; }
                        }
                    }
                if (hitA != null) TryTag(it, hitA);
            }
        }

        private void KeepItCount()
        {
            int want = ItTarget();
            if (_its.Count < want)
            {
                var a = _agents[Random.Range(0, _agents.Count)];
                if (!a.It && !a.IsPlayer) { SetIt(a, true); a.FrozenUntil = Time.time + FreezeSeconds; }
            }
            else if (_its.Count > want + 1)
            {
                var a = _its[Random.Range(0, _its.Count)];
                if (!a.IsPlayer) SetIt(a, false);
            }
        }

        private void AdjustBots()
        {
            if (Time.unscaledTime < _adjustAt) return;
            _adjustAt = Time.unscaledTime + 3f;
            if (Fps < 28f && BotCount > 30)
            {
                int remove = Mathf.Max(5, BotCount / 5);
                for (int k = 0; k < remove; k++)
                {
                    // take away bots far from you first
                    Agent far = null; float fd = -1f;
                    for (int t = 0; t < 12; t++)
                    {
                        var a = _agents[Random.Range(0, _agents.Count)];
                        if (a.IsPlayer) continue;
                        float d = (a.Pos - Me.Pos).sqrMagnitude;
                        if (d > fd) { fd = d; far = a; }
                    }
                    if (far != null) RemoveBot(far);
                }
            }
            else if (Fps > 50f && BotCount < AutoMax)
            {
                for (int k = 0; k < 25 && BotCount < AutoMax; k++) AddBot(false);
            }
        }

        // ================================================================== bot brain

        private void TickBot(Agent a, float dt, bool near)
        {
            float now = Time.time;
            // stamina and laces
            if (a.Sprinting) a.Stamina = Mathf.Max(0f, a.Stamina - dt);
            else a.Stamina = Mathf.Min(4f, a.Stamina + dt);
            if (a.Sprinting && !a.Untied && a.Stamina < 0.6f && now > a.LaceLockUntil && Random.value < 0.3f * dt)
            {
                a.Untied = true;
                if (near) AddFeed(a.Name + "'s shoelaces came untied");
            }
            if (a.Busy) { a.Sprinting = false; return; }

            if (now >= a.ThinkAt) { a.ThinkAt = now + Random.Range(0.35f, 0.6f); Think(a, near); }

            // shooting
            if (a.HasGun && a.Target != null && now >= a.NextShot && !a.Target.Knocked)
            {
                var w = MegaItems.Weapon(a.Gun);
                Vector3 to = a.Target.Pos - a.Pos;
                float dist = to.magnitude;
                bool wantShoot = dist < w.Range * 0.85f && dist > 1.5f && (a.It ? dist > 4f : true);
                if (wantShoot)
                {
                    a.NextShot = now + w.Cooldown * Random.Range(1.6f, 2.6f);
                    bool close = near || a.Target.IsPlayer;
                    if (close && _losBudget > 0)
                    {
                        _losBudget--;
                        Vector3 o = a.Pos + Vector3.up * 1.3f;
                        Vector3 aim = (a.Target.Pos + Vector3.up * 1.0f) - o;
                        // bots aren't perfect shots, worse far away
                        float miss = Mathf.Lerp(1.5f, 9f, dist / w.Range);
                        aim = Quaternion.Euler(Random.Range(-miss, miss) * 0.4f, Random.Range(-miss, miss), 0f) * aim;
                        Shoot(a, o, aim.normalized, a.Gun);
                    }
                    else if (!close && Random.value < 0.35f) Damage(a.Target, w.Damage * w.Pellets * 0.5f, a);
                    if (--a.Ammo <= 0) { a.HasGun = false; }
                }
            }

            // heal when hurt
            if (a.Heals > 0 && a.Hp < 45f && (a.Target == null || !a.It)) { a.Heals--; a.Hp = Mathf.Min(100f, a.Hp + 50f); }

            // move
            if (a.HasGoal)
            {
                float speed = BotSpeed * (a.It ? 1.04f : 1f);
                bool sprint = a.WantSprint && a.Stamina > 0f && !a.Untied;
                a.Sprinting = sprint;
                if (sprint) speed *= SprintMul;
                if (a.Untied) speed *= UntiedMul;
                if (now < a.SneakersUntil) speed *= SneakerMul;
                MoveBot(a, a.Goal, speed, dt);
            }
            else a.Sprinting = false;

            // pick up items walked over
            if (near || (a.Id + _frame) % 4 == 0)
            {
                var item = MegaItems.Nearest(a.Pos, 1.3f);
                if (item != null) BotPickup(a, item);
            }
        }

        private void BotPickup(Agent a, FloorItem item)
        {
            var t = item.Type;
            if (MegaItems.IsGun(t))
            {
                if (a.HasGun && a.Gun == t) a.Ammo += MegaItems.Weapon(t).Mag;
                else if (!a.HasGun || (int)t > (int)a.Gun) { a.HasGun = true; a.Gun = t; a.Ammo = MegaItems.Weapon(t).Mag; }
                else return;
            }
            else switch (t)
            {
                case ItemType.Medkit: case ItemType.Bandage: if (a.Heals >= 2) return; a.Heals++; break;
                case ItemType.Shield: if (a.Armour >= 90f) return; a.Armour = Mathf.Min(100f, a.Armour + 50f); break;
                case ItemType.EnergyDrink: a.Stamina = 4f; a.LaceLockUntil = Time.time + 30f; a.Untied = false; break;
                case ItemType.Sneakers: a.SneakersUntil = Time.time + 25f; a.LaceLockUntil = Time.time + 25f; a.Untied = false; break;
                case ItemType.Ammo: if (!a.HasGun) return; a.Ammo += MegaItems.Weapon(a.Gun).Mag; break;
            }
            MegaItems.Take(item);
        }

        private Agent NearestIt(Agent a, float maxDist)
        {
            Agent best = null; float bd = maxDist * maxDist;
            foreach (var it in _its)
            {
                if (it == a) continue;
                float d = (it.Pos - a.Pos).sqrMagnitude;
                if (Mathf.Abs(it.Pos.y - a.Pos.y) > 3f) d *= 2.5f;
                if (d < bd) { bd = d; best = it; }
            }
            return best;
        }

        private void Think(Agent a, bool near)
        {
            float now = Time.time;
            a.WantSprint = false;
            if (a.It)
            {
                // chase the nearest runner (you a bit more often when you're close)
                Agent best = null; float bd = float.MaxValue;
                foreach (var o in _agents)
                {
                    if (o.It || o == a) continue;
                    if (o == a.NoBackFrom && now < a.NoBackUntil) continue;
                    float d = (o.Pos - a.Pos).sqrMagnitude;
                    if (Mathf.Abs(o.Pos.y - a.Pos.y) > 2f) d *= 3f;      // other floors take longer to reach
                    if (o.IsPlayer) d *= 0.6f;
                    if (o.Knocked || o.Untied) d *= 0.5f;
                    if (d < bd) { bd = d; best = o; }
                }
                a.Target = best;
                if (best != null)
                {
                    a.Goal = best.Pos; a.HasGoal = true;
                    a.WantSprint = bd < 30f * 30f;
                }
                return;
            }

            var threat = NearestIt(a, 32f);
            a.Target = threat;
            if (threat != null)
            {
                Vector3 away = a.Pos - threat.Pos; away.y = 0f;
                if (away.sqrMagnitude < 0.01f) away = Random.insideUnitSphere;
                away.y = 0f; away.Normalize();
                // choose the free direction most away from IT
                Vector3 bestDir = away; float bestScore = -9f;
                for (int k = -3; k <= 3; k++)
                {
                    Vector3 d = Quaternion.Euler(0f, k * 30f, 0f) * away;
                    float score = Vector3.Dot(d, away);
                    if (!FreeAhead(a, d, 6f)) score -= 2f;
                    if (score > bestScore) { bestScore = score; bestDir = d; }
                }
                a.Goal = a.Pos + bestDir * 18f; a.HasGoal = true;
                a.Path = null;
                a.WantSprint = true;
                return;
            }
            if (a.Untied && !a.Tying)
            {
                a.Tying = true; a.TieDoneAt = now + TieSeconds; a.HasGoal = false;
                return;
            }
            // grab a gun or something useful nearby
            if (!a.HasGun || a.Heals == 0)
            {
                var item = MegaItems.Nearest(a.Pos, 28f, !a.HasGun && Random.value < 0.7f);
                if (item != null) { a.Goal = item.Pos; a.HasGoal = true; return; }
            }
            // wander: a street corner somewhere, or into a building and up the stairs
            if (now >= a.WanderUntil || !a.HasGoal || (a.Goal - a.Pos).sqrMagnitude < 9f)
            {
                a.WanderUntil = now + Random.Range(8f, 20f);
                if (a.Floor > 0 && a.Bld != null && now < a.StayUpUntil)
                {
                    a.Goal = RandomInside(a.Bld, a.Floor);
                }
                else if (Random.value < 0.15f && MegaWorld.Buildings.Count > 0)
                {
                    var b = NearestBuilding(a.Pos, 160f);
                    if (b != null) { a.Goal = RandomInside(b, Random.Range(0, b.Floors)); a.StayUpUntil = now + Random.Range(20f, 50f); }
                    else a.Goal = new Vector3(Street(StreetIdx(a.Pos.x) + Random.Range(-2, 3)), 0f, Street(StreetIdx(a.Pos.z) + Random.Range(-2, 3)));
                }
                else a.Goal = new Vector3(Street(StreetIdx(a.Pos.x) + Random.Range(-3, 4)), 0f, Street(StreetIdx(a.Pos.z) + Random.Range(-3, 4)));
                a.HasGoal = true;
                a.Path = null;
            }
        }

        private static MegaWorld.Building NearestBuilding(Vector3 p, float max)
        {
            MegaWorld.Building best = null; float bd = max * max;
            foreach (var b in MegaWorld.Buildings)
            {
                Vector2 c = b.Area.center;
                float d = (c.x - p.x) * (c.x - p.x) + (c.y - p.z) * (c.y - p.z);
                if (d < bd) { bd = d; best = b; }
            }
            return best;
        }

        private static Vector3 RandomInside(MegaWorld.Building b, int floor)
        {
            for (int t = 0; t < 20; t++)
            {
                float x = Random.Range(b.Area.xMin + 1.5f, b.Area.xMax - 1.5f), z = Random.Range(b.Area.yMin + 1.5f, b.Area.yMax - 1.5f);
                if (floor > 0 && b.InHole(x, z)) continue;
                return new Vector3(x, floor * b.FloorHeight, z);
            }
            return new Vector3(b.Area.center.x, floor * b.FloorHeight, b.Area.center.y);
        }

        // ================================================================== bot movement

        private bool Free(Agent a, float x, float z)
        {
            if (a.Floor > 0 && a.Bld != null)
            {
                var r = a.Bld.Area;
                if (x < r.xMin + 0.4f || x > r.xMax - 0.4f || z < r.yMin + 0.4f || z > r.yMax - 0.4f) return false;
                return !a.Bld.InHole(x, z);
            }
            if (Mathf.Abs(x) > 995f || Mathf.Abs(z) > 995f) return false;
            return _grid == null || !_grid.IsBlocked(x, z);
        }

        private bool FreeAhead(Agent a, Vector3 dir, float dist)
        {
            for (float s = 1.5f; s <= dist; s += 1.5f)
                if (!Free(a, a.Pos.x + dir.x * s, a.Pos.z + dir.z * s)) return false;
            return true;
        }

        private int FloorOf(MegaWorld.Building b, float y) => b == null ? 0 : Mathf.Clamp(Mathf.RoundToInt(y / b.FloorHeight), 0, b.Floors - 1);

        private void MoveBot(Agent a, Vector3 goal, float speed, float dt)
        {
            // scripted stairs walk (up or down)
            if (a.Script != null)
            {
                Vector3 wp = a.Script[a.ScriptI];
                Vector3 d = wp - a.Pos;
                float step = speed * 0.8f * dt;
                if (d.magnitude <= step) { a.Pos = wp; if (++a.ScriptI >= a.Script.Count) { a.Script = null; a.Floor = FloorOf(a.Bld, a.Pos.y); } }
                else { a.Pos += d.normalized * step; }
                Vector3 flat = new Vector3(d.x, 0f, d.z);
                if (flat.sqrMagnitude > 0.001f) a.Yaw = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;
                return;
            }

            // goal on another floor (or in a building we're not in)
            var gb = MegaWorld.BuildingAt(goal);
            int goalFloor = gb != null ? FloorOf(gb, goal.y) : 0;
            if (gb != null && goalFloor > 0 && goal.y > gb.FloorHeight * 0.5f)
            {
                if (a.Floor == 0)
                {
                    if (gb.Contains(a.Pos))
                    {
                        // inside on the ground floor: walk to the stairs, then climb
                        Vector3 sb = gb.StairBase;
                        if (Flat(a.Pos - sb) < 1.6f) { a.Bld = gb; StartClimb(a, gb, 0, goalFloor); return; }
                        goal = sb;
                    }
                    else goal = gb.Door;
                }
                else if (a.Bld == gb && a.Floor != goalFloor)
                {
                    goal = StairsGoal(a, gb, goalFloor);
                    if (goal.y < -50f) return;
                }
            }
            else if (a.Floor > 0 && a.Bld != null)
            {
                // need to come down
                goal = StairsGoal(a, a.Bld, 0);
                if (goal.y < -50f) return;
            }

            Vector3 to = goal - a.Pos; to.y = 0f;
            float dist = to.magnitude;
            if (dist < 0.4f) { return; }

            Vector3 target = goal;
            if (a.Floor == 0)
            {
                if (Time.time < a.DetourUntil) target = a.Detour;
                else if (a.Path != null && a.PathI < a.Path.Count)
                {
                    target = a.Path[a.PathI];
                    if (Flat(target - a.Pos) < 1.2f) { a.PathI++; if (a.PathI >= a.Path.Count) a.Path = null; }
                }
                else if (dist > 70f && _grid != null && !_grid.Los(a.Pos, goal))
                    target = StreetWaypoint(a.Pos, goal);
            }

            Vector3 want = target - a.Pos; want.y = 0f;
            if (want.sqrMagnitude < 0.0001f) return;
            want.Normalize();
            Vector3 dir = want;
            bool found = false;
            for (int k = 0; k < Probe.Length; k++)
            {
                Vector3 r = Quaternion.Euler(0f, Probe[k], 0f) * want;
                if (Free(a, a.Pos.x + r.x * 1.6f, a.Pos.z + r.z * 1.6f)) { dir = r; found = true; break; }
            }
            float stepLen = speed * dt;
            Vector3 np = a.Pos + dir * Mathf.Min(stepLen, dist);
            if (found && Free(a, np.x, np.z)) a.Pos = np;
            else if (Free(a, np.x, a.Pos.z)) a.Pos.x = np.x;
            else if (Free(a, a.Pos.x, np.z)) a.Pos.z = np.z;
            a.Yaw = Mathf.LerpAngle(a.Yaw, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, Mathf.Clamp01(dt * 12f));
            if (a.Floor == 0) { a.Pos.y = 0f; a.Bld = null; }

            // stuck? find a real path (close goals) or take a little detour
            if (Time.time >= a.ProgressAt)
            {
                if (Flat(a.Pos - a.ProgressPos) < 1.2f && a.Floor == 0)
                {
                    if (_pathBudget > 0 && _grid != null && Flat(target - a.Pos) < 90f)
                    {
                        _pathBudget--;
                        a.Path = _grid.FindPath(a.Pos, target, 5000); a.PathI = 0;
                    }
                    if (a.Path == null)
                    {
                        Vector3 side = Quaternion.Euler(0f, Random.value < 0.5f ? 90f : -90f, 0f) * want;
                        a.Detour = a.Pos + side * 10f - want * 3f;
                        a.DetourUntil = Time.time + 1.5f;
                    }
                }
                a.ProgressPos = a.Pos;
                a.ProgressAt = Time.time + 2f;
            }
        }

        private static readonly float[] Probe = { 0f, 30f, -30f, 60f, -60f, 90f, -90f, 130f, -130f };
        private static float Flat(Vector3 v) => Mathf.Sqrt(v.x * v.x + v.z * v.z);

        /// <summary>Where to go on this floor to use the stairs toward another floor (or y = -99 if a climb just started).</summary>
        private Vector3 StairsGoal(Agent a, MegaWorld.Building b, int goalFloor)
        {
            float y = a.Floor * b.FloorHeight;
            if (goalFloor > a.Floor)
            {
                Vector3 sb = b.StairBase + Vector3.up * y;
                if (Flat(a.Pos - sb) < 1.6f) { StartClimb(a, b, a.Floor, goalFloor); return new Vector3(0f, -99f, 0f); }
                return SideRoute(a, b, sb);
            }
            Vector3 top = b.StairTop + Vector3.up * y;
            if (Flat(a.Pos - top) < 1.6f) { StartDescend(a, b, a.Floor); return new Vector3(0f, -99f, 0f); }
            return SideRoute(a, b, top);
        }

        /// <summary>On an upper floor the stairwell is a hole: go round it on the walkway beside it.</summary>
        private static Vector3 SideRoute(Agent a, MegaWorld.Building b, Vector3 dest)
        {
            bool aFront = a.Pos.z < b.RampZ0, dFront = dest.z < b.RampZ0;
            bool aBack = a.Pos.z > b.RampZ1, dBack = dest.z > b.RampZ1;
            if ((aFront && dBack) || (aBack && dFront))
            {
                if (a.Pos.x < b.SideX - 0.6f) return new Vector3(b.SideX, dest.y, a.Pos.z);
                return new Vector3(b.SideX, dest.y, dest.z);
            }
            return dest;
        }

        private void StartClimb(Agent a, MegaWorld.Building b, int from, int to)
        {
            var s = new List<Vector3>();
            float fh = b.FloorHeight;
            for (int f = from; f < to; f++)
            {
                float y = f * fh;
                if (f > from)
                {
                    s.Add(new Vector3(b.SideX, y, b.StairTop.z));
                    s.Add(new Vector3(b.SideX, y, b.StairBase.z));
                    s.Add(b.StairBase + Vector3.up * y);
                }
                s.Add(new Vector3(b.RampX, y, b.RampZ0));
                s.Add(new Vector3(b.RampX, y + fh, b.RampZ1));
                s.Add(b.StairTop + Vector3.up * (y + fh));
            }
            a.Bld = b; a.Script = s; a.ScriptI = 0; a.Path = null;
        }

        private void StartDescend(Agent a, MegaWorld.Building b, int from)
        {
            float fh = b.FloorHeight, y = from * fh;
            a.Script = new List<Vector3>
            {
                new Vector3(b.RampX, y, b.RampZ1),
                new Vector3(b.RampX, y - fh, b.RampZ0),
                b.StairBase + Vector3.up * (y - fh),
            };
            a.Bld = b; a.ScriptI = 0; a.Path = null;
        }

        /// <summary>Long trips follow the street grid: head for the next crossing toward the goal.</summary>
        private static Vector3 StreetWaypoint(Vector3 p, Vector3 goal)
        {
            int kx = StreetIdx(p.x), kz = StreetIdx(p.z);
            int gx = StreetIdx(goal.x), gz = StreetIdx(goal.z);
            bool onX = Mathf.Abs(p.z - Street(kz)) < 7f;   // on a street running along x
            bool onZ = Mathf.Abs(p.x - Street(kx)) < 7f;
            if (!onX && !onZ)
            {
                // inside a block: go out to the closest street
                float dz = Mathf.Abs(p.z - Street(kz)), dx = Mathf.Abs(p.x - Street(kx));
                return dz < dx ? new Vector3(p.x, 0f, Street(kz)) : new Vector3(Street(kx), 0f, p.z);
            }
            int ax = Mathf.Abs(gx - kx), az = Mathf.Abs(gz - kz);
            if (onX && (ax >= az || !onZ) && gx != kx) return new Vector3(Street(kx + (gx > kx ? 1 : -1)), 0f, Street(kz));
            if (onZ && gz != kz) return new Vector3(Street(kx), 0f, Street(kz + (gz > kz ? 1 : -1)));
            if (onX && gz != kz) return new Vector3(Street(kx), 0f, Street(kz));
            return goal;
        }

        // ================================================================== drawing

        private void Draw()
        {
            var cam = Camera.main;
            if (cam == null || _capsule == null) return;
            Vector3 cp = cam.transform.position, cf = cam.transform.forward;
            const float maxD = 170f;
            foreach (var a in _agents)
            {
                if (a.IsPlayer) continue;
                Vector3 to = a.Pos - cp;
                float d2 = to.sqrMagnitude;
                if (d2 > maxD * maxD) continue;
                if (d2 > 225f && Vector3.Dot(to, cf) < 0f) continue;
                Quaternion rot = Quaternion.Euler(0f, a.Yaw, 0f);
                Material body = a.Knocked ? _downMat : (a.It ? _itMat : _bodyMats[a.ColorIdx]);
                if (a.Knocked)
                {
                    Quaternion lie = rot * Quaternion.Euler(90f, 0f, 0f);
                    Graphics.DrawMesh(_capsule, Matrix4x4.TRS(a.Pos + Vector3.up * 0.5f, lie, Vector3.one), body, 0, null, 0, null, false, true);
                    continue;
                }
                float bob = a.HasGoal ? Mathf.Abs(Mathf.Sin(Time.time * 9f + a.Id)) * 0.08f : 0f;
                Vector3 c = a.Pos + Vector3.up * (1f + bob);
                Graphics.DrawMesh(_capsule, Matrix4x4.TRS(c, rot, Vector3.one), body, 0, null, 0, null, d2 < 40f * 40f, true);
                if (d2 < 70f * 70f)
                {
                    Vector3 fwd = rot * Vector3.forward, right = rot * Vector3.right;
                    Graphics.DrawMesh(_cube, Matrix4x4.TRS(c + Vector3.up * 0.45f + fwd * 0.42f, rot, new Vector3(0.6f, 0.16f, 0.12f)), _eyeMat, 0, null, 0, null, false, false);
                    if (a.HasGun) Graphics.DrawMesh(_cube, Matrix4x4.TRS(c + right * 0.55f + fwd * 0.3f, rot, new Vector3(0.12f, 0.14f, 0.7f)), _gunMat, 0, null, 0, null, false, false);
                    if (a.Untied) Graphics.DrawMesh(_cube, Matrix4x4.TRS(a.Pos + fwd * 0.5f + Vector3.up * 0.05f, rot, new Vector3(0.5f, 0.04f, 0.3f)), _eyeMat, 0, null, 0, null, false, false);
                }
                if (a.It) Graphics.DrawMesh(_sphere, Matrix4x4.TRS(a.Pos + Vector3.up * (2.5f + Mathf.Sin(Time.time * 5f + a.Id) * 0.12f), Quaternion.identity, Vector3.one * 0.55f), _hatMat, 0, null, 0, null, false, false);
            }
            // you: red marker when you're IT
            if (Me != null && Me.It && _player != null)
                Graphics.DrawMesh(_sphere, Matrix4x4.TRS(_player.transform.position + Vector3.up * (1.6f + Mathf.Sin(Time.time * 5f) * 0.12f), Quaternion.identity, Vector3.one * 0.55f), _hatMat, 0, null, 0, null, false, false);
        }

        /// <summary>The bot nearest the aim line, for the player's auto-aim.</summary>
        public Agent AimTarget(Vector3 origin, Vector3 dir, float range, float coneDeg)
        {
            Agent best = null; float bs = float.MaxValue;
            float cos = Mathf.Cos(coneDeg * Mathf.Deg2Rad);
            foreach (var a in _agents)
            {
                if (a.IsPlayer || a.Knocked) continue;
                Vector3 to = a.Pos + Vector3.up - origin;
                float d = to.magnitude;
                if (d > range || d < 0.5f) continue;
                Vector3 flat = new Vector3(to.x, 0f, to.z).normalized;
                float dot = Vector3.Dot(flat, new Vector3(dir.x, 0f, dir.z).normalized);
                if (dot < cos) continue;
                float score = d * (2f - dot) * (Me.It == a.It ? 1.8f : 1f);   // prefer the other side (IT vs runners)
                if (score < bs) { bs = score; best = a; }
            }
            return best;
        }
    }
}
