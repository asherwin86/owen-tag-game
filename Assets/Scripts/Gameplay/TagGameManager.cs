using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using TagGame.Maps;
using TagGame.Services;
using TagGame.UI;

namespace TagGame.Gameplay
{
    /// <summary>
    /// Practice-mode (offline) round controller. You and a pack of bots play pass-the-tag for a
    /// fixed time: one participant is "it", only "it" can tag, whoever is tagged becomes "it".
    /// Score = seconds spent NOT being "it". The round always lasts the full time; being tagged
    /// doesn't end it. Online play lives in Multiplayer/MultiplayerGame.cs.
    /// </summary>
    public class TagGameManager : MonoBehaviour
    {
        [Header("Scene refs")]
        [SerializeField] private PlayerController player;
        [SerializeField] private TaggerAI taggerPrefab;
        [SerializeField] private Transform[] taggerSpawnPoints;

        [Header("Fallback settings (overridden by Remote Config if available)")]
        [SerializeField] private int taggerCount = 2;
        [SerializeField] private float roundDurationSeconds = 60f;

        private static readonly float[] MapSeconds = { 60f, 90f, 120f };
        private const float FreezeSeconds = 1.5f, NoTagBackSeconds = 3.5f;

        public bool RoundInProgress { get; private set; }
        public bool IsPaused { get; private set; }
        public float TimeRemaining { get; private set; }
        public Transform ItTransform { get; private set; }
        public string ResultSummary { get; private set; } = "";
        public event Action<float> OnTimeRemainingChanged;
        public event Action<bool> OnRoundEnded; // true = you finished (always true now)
        public event Action<bool> OnPauseChanged; // true = now paused

        private readonly List<TaggerAI> _activeTaggers = new List<TaggerAI>();
        private readonly Dictionary<Transform, float> _safe = new Dictionary<Transform, float>();
        private float _elapsed, _duration, _freezeUntil, _noBackUntil;
        private Transform _lastIt;
        private GameObject _marker;

        private async void Start()
        {
            var config = await RemoteConfigManager.Instance.FetchTagGameConfigAsync();
            if (config != null)
            {
                taggerCount = config.taggerCount;
                roundDurationSeconds = config.roundDurationSeconds;
            }
        }

        public void BeginGame() { StartRound(); }

        public void RestartRound()
        {
            Time.timeScale = 1f;
            IsPaused = false;
            StartRound();
        }

        public void StartRound()
        {
            ClearTaggers();
            _safe.Clear();

            player.ResetForNewRound();
            player.Frozen = false;
            _elapsed = 0f;
            var maps = MapManager.I;
            _duration = maps != null ? MapSeconds[Mathf.Clamp(maps.Current, 0, MapSeconds.Length - 1)] : roundDurationSeconds;
            TimeRemaining = _duration;
            RoundInProgress = true;
            _safe[player.transform] = 0f;

            if (maps != null)
            {
                int n = maps.PracticeTaggerCount;
                float y = taggerSpawnPoints != null && taggerSpawnPoints.Length > 0 ? taggerSpawnPoints[0].position.y : taggerPrefab.transform.position.y;
                for (int i = 0; i < n; i++)
                {
                    Vector3 pos = maps.TaggerSpawn(i, n, y);
                    Spawn(pos, Quaternion.LookRotation(-new Vector3(pos.x, 0f, pos.z).normalized, Vector3.up), i);
                }
            }
            else
            {
                int spawnCount = Mathf.Min(taggerCount, taggerSpawnPoints.Length);
                for (int i = 0; i < spawnCount; i++) Spawn(taggerSpawnPoints[i].position, taggerSpawnPoints[i].rotation, i);
            }

            // random starter; shouldn't be you every time
            var all = new List<Transform>(_safe.Keys);
            ItTransform = all[UnityEngine.Random.Range(0, all.Count)];
            _lastIt = null;
            _freezeUntil = Time.time + 1.0f;
            if (ItTransform == player.transform) player.Frozen = true;
            TagPopup.Show(ItTransform == player.transform ? "You're IT! Catch a tiger!" : NameOf(ItTransform) + " starts as IT!", new Color(0.95f, 0.65f, 0.15f));
        }

        private void Spawn(Vector3 pos, Quaternion rot, int i)
        {
            TaggerAI tagger = Instantiate(taggerPrefab, pos, rot);
            tagger.Initialize(this, "Tiger " + (i + 1));
            var rend = tagger.GetComponent<Renderer>();
            PlayerFace.Attach(tagger.gameObject, tagger.DisplayName, rend != null ? rend.sharedMaterial : null);
            _activeTaggers.Add(tagger);
            _safe[tagger.transform] = 0f;
        }

        private void ClearTaggers()
        {
            foreach (var tagger in _activeTaggers) if (tagger != null) Destroy(tagger.gameObject);
            _activeTaggers.Clear();
            if (_marker != null) _marker.SetActive(false);
            TagPopup.SetStatus("");
        }

        // ------------------------------------------------------------------ rules (used by the bots)

        public bool IsIt(Transform t) => t == ItTransform;
        public bool IsFrozen(Transform t) => t == ItTransform && Time.time < _freezeUntil;

        private string NameOf(Transform t)
        {
            if (t == player.transform) return "You";
            var ai = t != null ? t.GetComponent<TaggerAI>() : null;
            return ai != null ? ai.DisplayName : "Someone";
        }

        /// <summary>Closest participant "it" is allowed to tag right now (not the one who just tagged them).</summary>
        public Transform NearestRunnerFor(Transform it)
        {
            Transform best = null; float bd = float.MaxValue;
            foreach (var kv in _safe)
            {
                var t = kv.Key;
                if (t == null || t == it) continue;
                if (t == _lastIt && Time.time < _noBackUntil) continue;
                float d = (t.position - it.position).sqrMagnitude;
                if (d < bd) { bd = d; best = t; }
            }
            return best;
        }

        public bool TryTag(Transform tagger, Transform victim)
        {
            if (!RoundInProgress || tagger != ItTransform || victim == null || tagger == victim) return false;
            if (Time.time < _freezeUntil) return false;
            if (victim == _lastIt && Time.time < _noBackUntil) return false;

            _lastIt = tagger;
            _noBackUntil = Time.time + NoTagBackSeconds;
            ItTransform = victim;
            _freezeUntil = Time.time + FreezeSeconds;
            if (victim == player.transform) player.Frozen = true;

            string a = NameOf(tagger), b = NameOf(victim);
            if (tagger == player.transform) TagPopup.Show("You tagged " + b + "!", new Color(0.2f, 0.8f, 0.35f));
            else if (victim == player.transform) TagPopup.Show(a + " tagged you!  You're IT!", new Color(0.95f, 0.25f, 0.25f));
            else TagPopup.Show(a + " tagged " + b + "!", new Color(0.95f, 0.65f, 0.15f));
            return true;
        }

        /// <summary>One line of JSON describing the round for spectators (see server 'pstate').</summary>
        public string SpectateJson(string playerName, int map)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            int idx = 0, itIdx = -1;
            sb.Append("{\"t\":\"pstate\",\"name\":\"").Append(playerName).Append("\",\"map\":").Append(map)
              .Append(",\"left\":").Append(TimeRemaining.ToString("F1", inv)).Append(",\"e\":[");
            void Add(Transform t, string n, bool isPlayer)
            {
                if (t == null) return;
                if (idx > 0) sb.Append(',');
                _safe.TryGetValue(t, out float sc);
                sb.Append("{\"n\":\"").Append(n).Append("\",\"x\":").Append(t.position.x.ToString("F1", inv))
                  .Append(",\"z\":").Append(t.position.z.ToString("F1", inv))
                  .Append(",\"ry\":").Append((t.eulerAngles.y * Mathf.Deg2Rad).ToString("F2", inv))
                  .Append(",\"s\":").Append(Mathf.FloorToInt(sc));
                if (isPlayer) sb.Append(",\"p\":1");
                sb.Append('}');
                if (t == ItTransform) itIdx = idx;
                idx++;
            }
            Add(player.transform, "You", true);
            foreach (var tg in _activeTaggers) if (tg != null) Add(tg.transform, tg.DisplayName, false);
            sb.Append("],\"it\":").Append(itIdx).Append('}');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ pause / loop

        public void TogglePause()
        {
            if (!RoundInProgress) return;
            IsPaused = !IsPaused;
            Time.timeScale = IsPaused ? 0f : 1f;
            OnPauseChanged?.Invoke(IsPaused);
        }

        private void Update()
        {
            if (RoundInProgress && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) TogglePause();
            if (!RoundInProgress || IsPaused) return;

            _elapsed += Time.deltaTime;
            TimeRemaining = Mathf.Max(0f, _duration - _elapsed);
            OnTimeRemainingChanged?.Invoke(TimeRemaining);

            // score: everyone who isn't "it" earns safe seconds
            var keys = new List<Transform>(_safe.Keys);
            foreach (var k in keys) if (k != null && k != ItTransform) _safe[k] += Time.deltaTime;

            // you are "it": touching a tiger tags it (no button needed)
            if (ItTransform == player.transform && Time.time >= _freezeUntil)
            {
                if (player.Frozen) player.Frozen = false;
                foreach (var t in keys)
                {
                    if (t == null || t == player.transform) continue;
                    if (t == _lastIt && Time.time < _noBackUntil) continue;
                    Vector3 d = t.position - player.transform.position; d.y = 0f;
                    if (d.magnitude < 1.4f) { TryTag(player.transform, t); break; }
                }
            }
            else if (ItTransform != player.transform && player.Frozen && Time.time >= _freezeUntil) player.Frozen = false;

            UpdateMarker();
            float mine = _safe.TryGetValue(player.transform, out var ms) ? ms : 0f;
            TagPopup.SetStatus(ItTransform == player.transform
                ? "<color=#FF5050>YOU ARE IT!</color>  touch a tiger  -  safe " + Mathf.FloorToInt(mine) + "s"
                : "<color=#FF5050>" + NameOf(ItTransform) + "</color> is IT - run!  -  safe " + Mathf.FloorToInt(mine) + "s");

            if (TimeRemaining <= 0f) EndRound();
        }

        private void UpdateMarker()
        {
            if (ItTransform == null) return;
            if (_marker == null)
            {
                _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                var c = _marker.GetComponent<Collider>(); if (c != null) Destroy(c);
                _marker.name = "ItMarker";
                var r = _marker.GetComponent<Renderer>();
                var pr = player.GetComponent<Renderer>();
                if (pr != null && pr.sharedMaterial != null) r.sharedMaterial = new Material(pr.sharedMaterial) { color = new Color(1f, 0.15f, 0.15f) };
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _marker.transform.localScale = Vector3.one * 0.6f;
            }
            _marker.SetActive(true);
            _marker.transform.position = ItTransform.position + Vector3.up * (2.3f + Mathf.Sin(Time.time * 5f) * 0.12f);
        }

        private async void EndRound()
        {
            RoundInProgress = false;
            float mine = _safe.TryGetValue(player.transform, out var ms) ? ms : 0f;

            // results: rank everyone by safe seconds
            var rows = new List<KeyValuePair<Transform, float>>(_safe);
            rows.RemoveAll(r => r.Key == null);
            rows.Sort((a, b) => b.Value.CompareTo(a.Value));
            int myRank = rows.FindIndex(r => r.Key == player.transform) + 1;
            var sb = new StringBuilder();
            sb.Append(myRank == 1 ? "You win!" : "Time's up!");
            sb.Append("  You were safe " + Mathf.FloorToInt(mine) + "s  (place " + myRank + " of " + rows.Count + ")");
            sb.Append("\nTop: ");
            for (int i = 0; i < Mathf.Min(3, rows.Count); i++)
                sb.Append((i > 0 ? "   " : "") + (i + 1) + ". " + NameOf(rows[i].Key) + " " + Mathf.FloorToInt(rows[i].Value) + "s");
            ResultSummary = sb.ToString();

            ClearTaggers();
            player.Frozen = false;
            player.EnterLobby();
            ItTransform = null;

            OnRoundEnded?.Invoke(true);
            await LeaderboardManager.Instance.SubmitSurvivalTimeAsync(mine, true);
        }
    }
}
