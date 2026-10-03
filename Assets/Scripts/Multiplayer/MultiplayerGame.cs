using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TagGame.Gameplay;
using TagGame.UI;

namespace TagGame.Multiplayer
{
    /// <summary>
    /// Online free-for-all tag. Adds a Multiplayer button to the start screen, builds its own
    /// menus/HUD at runtime, talks to the Node game server (server/server.js) over a WebSocket,
    /// and shows other players as capsules with faces and floating names. The server decides
    /// every tag (first TAG press wins), the client just sends its position and button presses.
    /// </summary>
    public class MultiplayerGame : MonoBehaviour
    {
        // Public game server (a second Render service). Change if you host it elsewhere.
        public const string OfficialServerUrl = "wss://tag-game-server-zsyy.onrender.com";
        private const string PrefName = "TagGame.MpName";
        private const string PrefServerMode = "TagGame.MpServerMode"; // 0 official, 1 my own
        private const string PrefCustomUrl = "TagGame.MpCustomUrl";

        [Serializable] private class PInfo { public int id; public string name; public int score, wins, color; public float x, z, ry; }
        [Serializable] private class Spawn { public int id; public float x, z; }
        [Serializable]
        private class Msg
        {
            public string t, code, phase, msg, byName, targetName;
            public int id, hostId, by, target;
            public bool isPublic;
            public float duration, left, next, stunMs;
            public PInfo[] players, scores;
            public Spawn[] spawns;
            public string[] winners;
        }
        [Serializable] private class Out { public string t; public string name; public string code; }

        private static readonly Color[] Palette =
        {
            new Color(0.2f, 0.85f, 0.9f), new Color(0.95f, 0.35f, 0.35f), new Color(0.4f, 0.85f, 0.35f), new Color(0.98f, 0.8f, 0.2f),
            new Color(0.75f, 0.45f, 0.95f), new Color(0.98f, 0.55f, 0.2f), new Color(0.95f, 0.5f, 0.8f), new Color(0.95f, 0.95f, 0.95f),
        };
        private static Color ColorOf(int i) => Palette[Mathf.Abs(i) % Palette.Length];

        private enum Mode { Idle, Menu, Room, Playing }

        private class Remote { public Transform t; public Vector3 target; public float yaw; public string name; }

        private Mode _mode = Mode.Idle;
        private PlayerController _player;
        private GameFlowUI _flow;
        private Material _playerMaterial;
        private float _baseY = 1f;

        private int _myId, _hostId;
        private bool _roomIsPublic;
        private string _roomCode = "";
        private string _phase = "lobby";
        private PInfo[] _roster = new PInfo[0];
        private PInfo[] _scores = new PInfo[0];
        private readonly Dictionary<int, Remote> _remotes = new Dictionary<int, Remote>();
        private float _roundLeft, _nextRoundIn;
        private float _stunUntil, _nextTag, _nextSend, _feedUntil, _connectStarted;
        private Out _pending;

        // UI
        private GameObject _canvasGo, _menuPanel, _roomPanel, _hud, _resultsPanel, _tagButton, _startRoundButton, _customUrlRow;
        private TMP_InputField _nameInput, _codeInput, _urlInput;
        private GameObject[] _rowObjs = new GameObject[8];
        private Image[] _rowSwatch = new Image[8];
        private TMP_Text[] _rowText = new TMP_Text[8];
        private GameObject[] _rowKick = new GameObject[8];
        private int[] _rowId = new int[8];
        private TMP_Text _statusText, _roomCodeText, _playersText, _waitText, _timerText, _scoreText, _feedText,
            _resultsTitle, _resultsList, _serverToggleLabel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<MultiplayerGame>() != null) return;
            if (FindFirstObjectByType<GameFlowUI>() == null) return;
            new GameObject("MultiplayerGame").AddComponent<MultiplayerGame>();
        }

        private void Start()
        {
            Application.runInBackground = true;
            _player = FindFirstObjectByType<PlayerController>();
            _flow = FindFirstObjectByType<GameFlowUI>();
            if (_player == null || _flow == null) { enabled = false; return; }

            var rend = _player.GetComponent<Renderer>();
            _playerMaterial = rend != null ? rend.sharedMaterial : null;
            _baseY = _player.transform.position.y;
            PlayerFace.Attach(_player.gameObject, null, _playerMaterial); // you have a face now

            BuildUI();
            AddStartScreenButtons();
        }

        // ------------------------------------------------------------------ start screen

        private void AddStartScreenButtons()
        {
            var src = _flow.StartButton;
            var clone = Instantiate(src.gameObject, _flow.StartPanel.transform);
            clone.name = "MultiplayerButton";
            var rt = (RectTransform)clone.transform;
            rt.anchoredPosition = ((RectTransform)src.transform).anchoredPosition + new Vector2(0f, -100f);
            var label = clone.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = "Multiplayer";
            var btn = clone.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(OpenMenu);
        }

        private void OpenMenu()
        {
            _flow.StartPanel.SetActive(false);
            _mode = Mode.Menu;
            _menuPanel.SetActive(true);
            _statusText.text = "";
            _nameInput.text = PlayerPrefs.GetString(PrefName, "");
            UpdateServerToggle();
            StartConnect(); // connect early so a sleeping free server has time to wake up
        }

        private string CurrentUrl()
        {
            if (PlayerPrefs.GetInt(PrefServerMode, 0) == 1)
            {
                string u = (_urlInput != null ? _urlInput.text : PlayerPrefs.GetString(PrefCustomUrl, "")).Trim();
                if (u.Length == 0) u = "ws://localhost:8080";
                if (!u.Contains("://")) u = "ws://" + u;
                return u;
            }
            return OfficialServerUrl;
        }

        private void StartConnect()
        {
            if (GameSocket.State == GameSocket.Open || GameSocket.State == GameSocket.Connecting) return;
            _connectStarted = Time.unscaledTime;
            GameSocket.Connect(CurrentUrl());
        }

        private void ToggleServer()
        {
            int mode = PlayerPrefs.GetInt(PrefServerMode, 0) == 1 ? 0 : 1;
            PlayerPrefs.SetInt(PrefServerMode, mode);
            PlayerPrefs.Save();
            GameSocket.Close();
            UpdateServerToggle();
            StartConnect();
        }

        private void UpdateServerToggle()
        {
            bool own = PlayerPrefs.GetInt(PrefServerMode, 0) == 1;
            _serverToggleLabel.text = own ? "Server: My own (tap to switch)" : "Server: Official (tap to switch)";
            _customUrlRow.SetActive(own);
            if (own && string.IsNullOrEmpty(_urlInput.text)) _urlInput.text = PlayerPrefs.GetString(PrefCustomUrl, "ws://localhost:8080");
        }

        private string PlayerName()
        {
            string n = _nameInput.text.Trim();
            if (n.Length == 0) n = "Player" + UnityEngine.Random.Range(100, 999);
            PlayerPrefs.SetString(PrefName, _nameInput.text.Trim());
            if (PlayerPrefs.GetInt(PrefServerMode, 0) == 1) PlayerPrefs.SetString(PrefCustomUrl, _urlInput.text.Trim());
            PlayerPrefs.Save();
            return n;
        }

        private void DoCreate() => Request(new Out { t = "create", name = PlayerName() });
        private void DoPublic() => Request(new Out { t = "joinpublic", name = PlayerName() });

        private void DoJoin()
        {
            string code = _codeInput.text.Trim().ToUpperInvariant();
            if (code.Length != 4) { _statusText.text = "Type the 4-letter code first."; return; }
            Request(new Out { t = "join", name = PlayerName(), code = code });
        }

        private void Request(Out o)
        {
            _pending = o;
            _statusText.text = "Connecting...";
            if (GameSocket.State == GameSocket.Closed) StartConnect();
        }

        private void MenuBack()
        {
            GameSocket.Close();
            _pending = null;
            _mode = Mode.Idle;
            _menuPanel.SetActive(false);
            _flow.StartPanel.SetActive(true);
        }

        private void LeaveRoom()
        {
            GameSocket.Send(Json(new Out { t = "leave" }));
            ResetToIdle();
            _flow.StartPanel.SetActive(true);
        }

        private void ResetToIdle()
        {
            GameSocket.Close();
            _pending = null;
            _mode = Mode.Idle;
            ClearRemotes();
            _player.Frozen = false;
            var prend = _player.GetComponent<Renderer>();
            if (prend != null && _playerMaterial != null) prend.sharedMaterial = _playerMaterial;
            _player.EnterLobby();
            _menuPanel.SetActive(false);
            _roomPanel.SetActive(false);
            _hud.SetActive(false);
            _resultsPanel.SetActive(false);
        }

        private void ConnectionLost(string why)
        {
            bool wasIn = _mode == Mode.Room || _mode == Mode.Playing;
            ResetToIdle();
            _mode = Mode.Menu;
            _menuPanel.SetActive(true);
            _statusText.text = why;
            if (wasIn) _flow.StartPanel.SetActive(false);
        }

        // ------------------------------------------------------------------ update loop

        private void Update()
        {
            if (_mode == Mode.Idle) return;

            // connection state
            int st = GameSocket.State;
            if (st == GameSocket.Open && _pending != null)
            {
                GameSocket.Send(Json(_pending));
                _pending = null;
            }
            else if (st == GameSocket.Connecting)
            {
                if (_mode == Mode.Menu && _pending != null)
                    _statusText.text = "Connecting... the free server can take up to a minute to wake up.";
                if (Time.unscaledTime - _connectStarted > 90f) { GameSocket.Close(); ConnectionLost("Couldn't reach the server."); }
            }
            else if (st == GameSocket.Closed)
            {
                if (_mode == Mode.Room || _mode == Mode.Playing) ConnectionLost("Lost connection to the server.");
                else if (_mode == Mode.Menu && Time.unscaledTime - _connectStarted > 0.5f)
                {
                    if (_pending != null) { _pending = null; _statusText.text = "Couldn't connect to the server. Check the address and try again."; }
                }
            }

            string raw;
            int guard = 0;
            while (_mode != Mode.Idle && guard++ < 200 && GameSocket.TryReceive(out raw)) Handle(raw);
            if (_mode == Mode.Idle) return;

            if (_mode == Mode.Room || _mode == Mode.Playing) SendState();
            if (_mode == Mode.Playing) UpdateRemotes();
            UpdateInput();
            UpdateHud();
        }

        private void SendState()
        {
            if (Time.unscaledTime < _nextSend) return;
            _nextSend = Time.unscaledTime + 1f / 15f;
            var p = _player.transform.position;
            float ry = _player.transform.eulerAngles.y * Mathf.Deg2Rad;
            var inv = CultureInfo.InvariantCulture;
            GameSocket.Send("{\"t\":\"state\",\"x\":" + p.x.ToString("F2", inv) + ",\"z\":" + p.z.ToString("F2", inv)
                + ",\"ry\":" + ry.ToString("F3", inv) + "}");
        }

        private void UpdateInput()
        {
            if (_player.Frozen && Time.time >= _stunUntil && _mode == Mode.Playing) _player.Frozen = false;
            if (_mode != Mode.Playing) return;
            var kb = Keyboard.current;
            if (kb != null && (kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame)) PressTag();
        }

        private void PressTag()
        {
            if (_mode != Mode.Playing || Time.time < _stunUntil || Time.time < _nextTag) return;
            _nextTag = Time.time + 0.3f;
            GameSocket.Send("{\"t\":\"tag\"}");
        }

        private void UpdateRemotes()
        {
            float k = 1f - Mathf.Exp(-14f * Time.deltaTime);
            foreach (var r in _remotes.Values)
            {
                r.t.position = Vector3.Lerp(r.t.position, r.target, k);
                r.t.rotation = Quaternion.Slerp(r.t.rotation, Quaternion.Euler(0f, r.yaw, 0f), k);
            }
        }

        // ------------------------------------------------------------------ messages

        private void Handle(string raw)
        {
            Msg m;
            try { m = JsonUtility.FromJson<Msg>(raw); } catch (Exception) { return; }
            if (m == null || string.IsNullOrEmpty(m.t)) return;

            switch (m.t)
            {
                case "err":
                    if (_mode == Mode.Menu) _statusText.text = m.msg;
                    else Feed(m.msg, 3f);
                    break;
                case "joined":
                    _myId = m.id;
                    _mode = Mode.Room;
                    _menuPanel.SetActive(false);
                    _roomPanel.SetActive(true);
                    _scores = new PInfo[0];
                    PlayerFace.Attach(_player.gameObject, PlayerPrefs.GetString(PrefName, "") is var n && n.Length > 0 ? n : "You", _playerMaterial);
                    break;
                case "room":
                    _roomCode = m.code;
                    _hostId = m.hostId;
                    _phase = m.phase;
                    _roomIsPublic = m.isPublic;
                    _roster = m.players ?? new PInfo[0];
                    if (_mode == Mode.Room || _mode == Mode.Playing) RefreshRoomPanel();
                    break;
                case "start":
                    BeginRound(m);
                    break;
                case "snap":
                    ApplySnap(m);
                    break;
                case "tagged":
                    _scores = m.scores ?? _scores;
                    if (m.by == _myId) Feed("You tagged " + m.targetName + "!  +1", 2f);
                    else if (m.target == _myId)
                    {
                        _stunUntil = Time.time + m.stunMs / 1000f;
                        _player.Frozen = true;
                        Feed(m.byName + " tagged you!", 2f);
                    }
                    else Feed(m.byName + " tagged " + m.targetName, 2f);
                    break;
                case "miss":
                    Feed("Missed - face someone and get close!", 1f);
                    break;
                case "kicked":
                    ConnectionLost("The host removed you from the game.");
                    break;
                case "end":
                    EndRound(m);
                    break;
            }
        }

        private void BeginRound(Msg m)
        {
            _mode = Mode.Playing;
            _phase = "playing";
            _roundLeft = m.left > 0f ? m.left : m.duration;
            _scores = new PInfo[0];
            _stunUntil = 0f;
            _player.Frozen = false;
            _player.ExitLobby();
            _roomPanel.SetActive(false);
            _resultsPanel.SetActive(false);
            _hud.SetActive(true);
            _baseY = Mathf.Max(0.5f, _baseY);
            if (m.spawns != null)
            {
                foreach (var s in m.spawns)
                {
                    if (s.id != _myId) continue;
                    var pos = new Vector3(s.x, _baseY, s.z);
                    var look = Quaternion.LookRotation(new Vector3(-s.x, 0f, -s.z).normalized, Vector3.up);
                    _player.TeleportTo(pos, look);
                }
            }
            Feed("GO! Press TAG (E / Space) while facing someone", 3f);
        }

        private void EndRound(Msg m)
        {
            _mode = Mode.Room;
            _phase = "lobby";
            ClearRemotes();
            _player.Frozen = false;
            _player.EnterLobby();
            _hud.SetActive(false);
            _roomPanel.SetActive(true);
            RefreshRoomPanel();

            var sb = new StringBuilder();
            int rank = 1;
            if (m.scores != null)
                foreach (var s in m.scores) sb.AppendLine((rank++) + ".  " + s.name + "   " + s.score);
            _resultsList.text = sb.ToString();
            string w = m.winners != null && m.winners.Length > 0 ? string.Join(" & ", m.winners) : "nobody";
            _resultsTitle.text = (m.winners != null && m.winners.Length > 1 ? "Tie: " : "Winner: ") + w;
            _resultsPanel.SetActive(true);
        }

        private void ApplySnap(Msg m)
        {
            if (m.left > 0f || _mode == Mode.Playing) _roundLeft = m.left;
            _nextRoundIn = m.next;
            if (_mode != Mode.Playing || m.players == null) return;

            var seen = new HashSet<int>();
            foreach (var p in m.players)
            {
                if (p.id == _myId) continue;
                seen.Add(p.id);
                if (!_remotes.TryGetValue(p.id, out var r)) r = CreateRemote(p);
                r.target = new Vector3(p.x, _baseY, p.z);
                r.yaw = p.ry * Mathf.Rad2Deg;
            }
            List<int> gone = null;
            foreach (var id in _remotes.Keys) if (!seen.Contains(id)) (gone ??= new List<int>()).Add(id);
            if (gone != null) foreach (var id in gone) { Destroy(_remotes[id].t.gameObject); _remotes.Remove(id); }
        }

        private Remote CreateRemote(PInfo p)
        {
            string nm = p.name;
            foreach (var r0 in _roster) if (r0.id == p.id) nm = r0.name;
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Remote_" + nm;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var rend = go.GetComponent<Renderer>();
            Material baseMat = _playerMaterial != null ? _playerMaterial : rend.sharedMaterial;
            int ci = p.id; foreach (var r0b in _roster) if (r0b.id == p.id) ci = r0b.color;
            var mat = new Material(baseMat) { color = ColorOf(ci) };
            rend.sharedMaterial = mat;
            go.transform.position = new Vector3(p.x, _baseY, p.z);
            PlayerFace.Attach(go, nm, baseMat);
            var r = new Remote { t = go.transform, target = go.transform.position, yaw = p.ry * Mathf.Rad2Deg, name = nm };
            _remotes[p.id] = r;
            return r;
        }

        private void ClearRemotes()
        {
            foreach (var r in _remotes.Values) if (r.t != null) Destroy(r.t.gameObject);
            _remotes.Clear();
        }

        // ------------------------------------------------------------------ HUD / panels

        private void Feed(string text, float seconds)
        {
            _feedText.text = text;
            _feedUntil = Time.unscaledTime + seconds;
        }

        private void RefreshRoomPanel()
        {
            _roomCodeText.text = _roomIsPublic ? "PUBLIC" : _roomCode;
            bool iAmHost = _myId == _hostId && !_roomIsPublic;
            for (int i = 0; i < 8; i++)
            {
                bool has = i < _roster.Length;
                _rowObjs[i].SetActive(has);
                if (!has) continue;
                var p = _roster[i];
                _rowId[i] = p.id;
                _rowSwatch[i].color = ColorOf(p.color);
                _rowText[i].text = (p.id == _hostId && !_roomIsPublic ? "* " : "") + p.name + (p.id == _myId ? " (you)" : "") + "   <color=#FFE15A>" + p.wins + " wins</color>";
                _rowKick[i].SetActive(iAmHost && p.id != _myId);
                if (p.id == _myId)
                {
                    var rend = _player.GetComponent<Renderer>();
                    if (rend != null && _playerMaterial != null) rend.material.color = ColorOf(p.color);
                }
            }
            _startRoundButton.SetActive(iAmHost);
            _waitText.gameObject.SetActive(!iAmHost);
        }

        private void UpdateHud()
        {
            if (_mode == Mode.Room && _roomPanel.activeSelf && !_startRoundButton.activeSelf)
            {
                if (_roomIsPublic)
                    _waitText.text = _roster.Length < 2 ? "Waiting for another player..."
                        : "Next round in " + Mathf.CeilToInt(_nextRoundIn) + "s";
                else
                    _waitText.text = _roster.Length < 2 ? "Waiting for friends..." : "Waiting for the host to start...";
            }
            if (_mode == Mode.Room && _startRoundButton.activeSelf)
                _startRoundButton.GetComponentInChildren<TMP_Text>().text = _roster.Length < 2 ? "Need 2+ players" : "START GAME";

            if (_mode != Mode.Playing) return;
            int secs = Mathf.CeilToInt(_roundLeft);
            _timerText.text = (secs / 60) + ":" + (secs % 60).ToString("00");

            var sorted = new List<PInfo>(_roster);
            foreach (var s in _scores) foreach (var r in sorted) if (r.id == s.id) r.score = s.score;
            sorted.Sort((a, b) => b.score.CompareTo(a.score));
            var sb = new StringBuilder();
            foreach (var p in sorted)
                sb.AppendLine("<color=#" + ColorUtility.ToHtmlStringRGB(ColorOf(p.color)) + ">" + p.name + "</color>  " + p.score);
            _scoreText.text = sb.ToString();

            if (_feedText.text.Length > 0 && Time.unscaledTime > _feedUntil) _feedText.text = "";
            if (Time.time < _stunUntil && _feedText.text.Length == 0) Feed("You're frozen!", 0.5f);
        }

        // ------------------------------------------------------------------ UI building

        private static string Json(Out o) => JsonUtility.ToJson(o);

        private void BuildUI()
        {
            _canvasGo = new GameObject("MultiplayerCanvas");
            _canvasGo.transform.SetParent(transform, false);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
            var root = _canvasGo.transform;

            // --- menu
            _menuPanel = MakePanel(root, "MenuPanel", new Color(0f, 0f, 0f, 0.82f));
            var mp = _menuPanel.transform;
            MakeText(mp, "MULTIPLAYER", 56, C, new Vector2(0, 285), new Vector2(800, 80));
            _nameInput = MakeInput(mp, "Your name", C, new Vector2(0, 205), new Vector2(460, 56), 12, false);
            MakeButton(mp, "Join public game", C, new Vector2(0, 130), new Vector2(460, 64), new Color(0.15f, 0.6f, 0.3f), DoPublic, 30);
            MakeButton(mp, "Create private game", C, new Vector2(0, 55), new Vector2(460, 64), new Color(0.2f, 0.45f, 0.85f), DoCreate, 30);
            MakeText(mp, "or join friends with their code", 24, C, new Vector2(0, -10), new Vector2(700, 40));
            _codeInput = MakeInput(mp, "CODE", C, new Vector2(-120, -70), new Vector2(220, 64), 4, true);
            MakeButton(mp, "Join", C, new Vector2(110, -70), new Vector2(200, 64), new Color(0.2f, 0.45f, 0.85f), DoJoin, 30);
            var serverBtn = MakeButton(mp, "Server", C, new Vector2(0, -150), new Vector2(520, 48), new Color(0.3f, 0.3f, 0.3f, 0.9f), ToggleServer, 22);
            _serverToggleLabel = serverBtn.GetComponentInChildren<TMP_Text>();
            _customUrlRow = new GameObject("CustomUrlRow", typeof(RectTransform));
            _customUrlRow.transform.SetParent(mp, false);
            Stretch((RectTransform)_customUrlRow.transform);
            _urlInput = MakeInput(_customUrlRow.transform, "ws://192.168.1.5:8080", C, new Vector2(0, -210), new Vector2(520, 48), 80, false);
            _statusText = MakeText(mp, "", 24, C, new Vector2(0, -275), new Vector2(1000, 60));
            MakeButton(mp, "Back", C, new Vector2(0, -330), new Vector2(220, 50), new Color(0.5f, 0.2f, 0.2f), MenuBack, 26);
            _menuPanel.SetActive(false);

            // --- room (right side, so you can still run around the lobby circle)
            _roomPanel = MakePanel(root, "RoomPanel", new Color(0f, 0f, 0f, 0.65f));
            var rrt = (RectTransform)_roomPanel.transform;
            rrt.anchorMin = rrt.anchorMax = rrt.pivot = new Vector2(1f, 0.5f);
            rrt.sizeDelta = new Vector2(420f, 700f);
            rrt.anchoredPosition = new Vector2(-30f, 0f);
            var rp = _roomPanel.transform;
            MakeText(rp, "GAME CODE", 28, T, new Vector2(0, -40), new Vector2(380, 40));
            _roomCodeText = MakeText(rp, "----", 84, T, new Vector2(0, -115), new Vector2(400, 100));
            _roomCodeText.color = new Color(1f, 0.9f, 0.3f);
            MakeText(rp, "Friends: Multiplayer, type this code", 20, T, new Vector2(0, -200), new Vector2(400, 30));
            for (int i = 0; i < 8; i++)
            {
                int row = i;
                var rowGo = new GameObject("Row" + i, typeof(RectTransform));
                rowGo.transform.SetParent(rp, false);
                var rrow = (RectTransform)rowGo.transform;
                rrow.anchorMin = rrow.anchorMax = rrow.pivot = T;
                rrow.anchoredPosition = new Vector2(0, -250 - i * 34);
                rrow.sizeDelta = new Vector2(390, 32);
                var sw = new GameObject("Swatch", typeof(RectTransform), typeof(Image));
                sw.transform.SetParent(rowGo.transform, false);
                var swr = (RectTransform)sw.transform;
                swr.anchorMin = swr.anchorMax = swr.pivot = new Vector2(0f, 0.5f);
                swr.anchoredPosition = new Vector2(6, 0);
                swr.sizeDelta = new Vector2(22, 22);
                _rowSwatch[i] = sw.GetComponent<Image>();
                _rowText[i] = MakeText(rowGo.transform, "", 22, new Vector2(0f, 0.5f), new Vector2(38, 0), new Vector2(270, 30));
                _rowText[i].alignment = TextAlignmentOptions.Left;
                _rowText[i].overflowMode = TextOverflowModes.Ellipsis;
                var kick = MakeButton(rowGo.transform, "Kick", new Vector2(1f, 0.5f), new Vector2(-6, 0), new Vector2(64, 26), new Color(0.7f, 0.2f, 0.2f),
                    () => GameSocket.Send("{\"t\":\"kick\",\"id\":" + _rowId[row] + "}"), 16);
                _rowKick[i] = kick.gameObject;
                _rowObjs[i] = rowGo;
                rowGo.SetActive(false);
            }
            _startRoundButton = MakeButton(rp, "START GAME", T, new Vector2(0, -590), new Vector2(340, 56), new Color(0.15f, 0.6f, 0.3f),
                () => GameSocket.Send("{\"t\":\"start\"}"), 32).gameObject;
            _waitText = MakeText(rp, "", 26, T, new Vector2(0, -590), new Vector2(380, 56));
            MakeButton(rp, "Leave", T, new Vector2(0, -650), new Vector2(340, 42), new Color(0.6f, 0.2f, 0.2f), LeaveRoom, 26);
            _roomPanel.SetActive(false);

            // --- HUD
            _hud = new GameObject("Hud", typeof(RectTransform));
            _hud.transform.SetParent(root, false);
            Stretch((RectTransform)_hud.transform);
            var hp = _hud.transform;
            _timerText = MakeText(hp, "1:00", 56, T, new Vector2(0, -45), new Vector2(300, 80));
            _feedText = MakeText(hp, "", 34, T, new Vector2(0, -120), new Vector2(1000, 60));
            _scoreText = MakeText(hp, "", 26, TL, new Vector2(20, -135), new Vector2(360, 320));
            _scoreText.alignment = TextAlignmentOptions.TopLeft;
            _tagButton = MakeButton(hp, "TAG!", BR, new Vector2(-170, 400), new Vector2(170, 170), new Color(0.9f, 0.2f, 0.2f, 0.85f), PressTag, 44).gameObject;
            _hud.SetActive(false);

            // --- results
            _resultsPanel = MakePanel(root, "ResultsPanel", new Color(0f, 0f, 0f, 0.8f));
            var sp = _resultsPanel.transform;
            MakeText(sp, "ROUND OVER", 60, C, new Vector2(0, 230), new Vector2(900, 90));
            _resultsTitle = MakeText(sp, "", 44, C, new Vector2(0, 140), new Vector2(1000, 70));
            _resultsTitle.color = new Color(1f, 0.9f, 0.3f);
            _resultsList = MakeText(sp, "", 32, C, new Vector2(0, -30), new Vector2(700, 300));
            MakeButton(sp, "OK", C, new Vector2(0, -230), new Vector2(260, 70), new Color(0.2f, 0.45f, 0.85f), () => _resultsPanel.SetActive(false), 32);
            _resultsPanel.SetActive(false);
        }

        private static readonly Vector2 C = new Vector2(0.5f, 0.5f);
        private static readonly Vector2 T = new Vector2(0.5f, 1f);
        private static readonly Vector2 TL = new Vector2(0f, 1f);
        private static readonly Vector2 BR = new Vector2(1f, 0f);

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        private static GameObject MakePanel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            go.GetComponent<Image>().color = color;
            return go;
        }

        private static TMP_Text MakeText(Transform parent, string text, float size, Vector2 anchor, Vector2 pos, Vector2 dim)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = dim;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text;
            t.fontSize = size;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
            t.raycastTarget = false;
            t.richText = true;
            return t;
        }

        private static Button MakeButton(Transform parent, string label, Vector2 anchor, Vector2 pos, Vector2 dim, Color color, UnityAction onClick, float fontSize)
        {
            var go = new GameObject(label + "Button", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = dim;
            var img = go.GetComponent<Image>();
            img.color = color;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            btn.onClick.AddListener(onClick);
            var t = MakeText(go.transform, label, fontSize, C, Vector2.zero, dim);
            Stretch((RectTransform)t.transform);
            return btn;
        }

        private static TMP_InputField MakeInput(Transform parent, string placeholder, Vector2 anchor, Vector2 pos, Vector2 dim, int maxLen, bool upper)
        {
            var go = new GameObject("Input", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos;
            rt.sizeDelta = dim;
            go.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.18f);

            var area = new GameObject("TextArea", typeof(RectTransform), typeof(RectMask2D));
            area.transform.SetParent(go.transform, false);
            var art = (RectTransform)area.transform;
            Stretch(art);
            art.offsetMin = new Vector2(12f, 4f);
            art.offsetMax = new Vector2(-12f, -4f);

            var ph = MakeText(area.transform, placeholder, 28, C, Vector2.zero, Vector2.zero);
            Stretch((RectTransform)ph.transform);
            ph.color = new Color(1f, 1f, 1f, 0.45f);
            ph.fontStyle = FontStyles.Italic;
            var txt = MakeText(area.transform, "", 30, C, Vector2.zero, Vector2.zero);
            Stretch((RectTransform)txt.transform);

            var input = go.AddComponent<TMP_InputField>();
            input.textViewport = art;
            input.textComponent = txt;
            input.placeholder = ph;
            input.characterLimit = maxLen;
            if (upper) input.onValidateInput = (s, i, c) => char.IsLetter(c) ? char.ToUpperInvariant(c) : '\0';
            return input;
        }
    }
}
