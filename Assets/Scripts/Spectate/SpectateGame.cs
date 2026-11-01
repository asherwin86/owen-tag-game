using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TagGame.Gameplay;
using TagGame.Maps;
using TagGame.Multiplayer;
using TagGame.UI;

namespace TagGame.Spectate
{
    /// <summary>
    /// Watch other people's games in 3D. A Spectate button on the title screen lists live public games and
    /// Practice rounds (players who have "Allow spectators" on). Pick one and the real map loads; you follow any
    /// player with the orbit camera, or switch to a free camera and fly around the map yourself.
    /// </summary>
    public class SpectateGame : MonoBehaviour
    {
        [Serializable] private class Head { public string t; }
        [Serializable] private class Item { public string key, kind, label, phase; public int map, players; }
        [Serializable] private class ListMsg { public Item[] items; }
        [Serializable] private class VP { public int id, score, c; public string n; public float x, z, ry; }
        [Serializable] private class ViewMsg { public string key, label, kind, phase; public int map, it, hidden; public float left; public VP[] players; }
        [Serializable] private class ErrMsg { public string msg; }

        private enum State { Idle, List, Watch }
        private class Rem { public Transform t; public Vector3 target; public float yaw; public string name; }

        private const int Rows = 6;

        private State _state = State.Idle;
        private PlayerController _player;
        private GameFlowUI _flow;
        private FollowCamera _cam;
        private Material _playerMat;
        private float _baseY = 1f;
        private Transform _target;
        private GameObject _marker;

        private GameObject _canvasGo, _listPanel, _hud;
        private TMP_Text _status, _title, _itText, _board, _hint;
        private TMP_InputField _code;
        private readonly Button[] _rowBtn = new Button[Rows];
        private readonly TMP_Text[] _rowText = new TMP_Text[Rows];
        private readonly string[] _rowKey = new string[Rows];

        private readonly Dictionary<int, Rem> _remotes = new Dictionary<int, Rem>();
        private ViewMsg _view;
        private int _follow = -1;          // player id being followed; -1 = free camera
        private int _curMap = -1;
        private float _connectAt, _nextList, _left;
        private string _watchKey;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<SpectateGame>() != null) return;
            if (FindFirstObjectByType<GameFlowUI>() == null) return;
            new GameObject("SpectateGame").AddComponent<SpectateGame>();
        }

        private void Start()
        {
            _player = FindFirstObjectByType<PlayerController>();
            _flow = FindFirstObjectByType<GameFlowUI>();
            _cam = FindFirstObjectByType<FollowCamera>();
            if (_player == null || _flow == null || _cam == null) { enabled = false; return; }
            var rend = _player.GetComponent<Renderer>();
            _playerMat = rend != null ? rend.sharedMaterial : null;
            _baseY = _player.transform.position.y;
            _target = new GameObject("SpectateTarget").transform;
            BuildUI();
            AddStartScreenButton();
        }

        private void AddStartScreenButton()
        {
            var src = _flow.StartButton;
            var clone = Instantiate(src.gameObject, _flow.StartPanel.transform);
            clone.name = "SpectateButton";
            var rt = (RectTransform)clone.transform;
            rt.anchoredPosition = ((RectTransform)src.transform).anchoredPosition + new Vector2(0f, -200f);
            var label = clone.GetComponentInChildren<TMP_Text>();
            if (label != null) label.text = "Spectate";
            var btn = clone.GetComponent<Button>();
            btn.onClick.RemoveAllListeners();
            btn.onClick.AddListener(Open);
        }

        // ------------------------------------------------------------------ open / close

        private void Open()
        {
            _flow.StartPanel.SetActive(false);
            _state = State.List;
            _player.gameObject.SetActive(false);
            _target.position = new Vector3(0f, _baseY, 0f);
            _cam.SetTarget(_target);
            _listPanel.SetActive(true);
            _hud.SetActive(false);
            _status.text = "Connecting... the free server can take up to a minute to wake up.";
            ClearRows();
            if (GameSocket.State == GameSocket.Closed) GameSocket.Connect(PracticeSpectate.ServerUrl());
            _connectAt = Time.unscaledTime;
            _nextList = 0f;
        }

        private void Exit()
        {
            if (GameSocket.State == GameSocket.Open) GameSocket.Send("{\"t\":\"unwatch\"}");
            GameSocket.Close();
            ClearRemotes();
            if (_marker != null) _marker.SetActive(false);
            _state = State.Idle;
            _view = null; _follow = -1; _curMap = -1; _watchKey = null;
            _listPanel.SetActive(false);
            _hud.SetActive(false);
            _player.gameObject.SetActive(true);
            _player.EnterLobby();
            _cam.SetTarget(_player.transform);
            if (MapManager.I != null) MapManager.I.LoadSelected();
            _flow.StartPanel.SetActive(true);
        }

        private void BackToList(string why)
        {
            if (GameSocket.State == GameSocket.Open) GameSocket.Send("{\"t\":\"unwatch\"}");
            ClearRemotes();
            if (_marker != null) _marker.SetActive(false);
            _view = null; _follow = -1; _watchKey = null;
            _state = State.List;
            _hud.SetActive(false);
            _listPanel.SetActive(true);
            _status.text = why;
            _nextList = 0f;
        }

        private void ClearRows() { for (int i = 0; i < Rows; i++) _rowBtn[i].gameObject.SetActive(false); }

        // ------------------------------------------------------------------ update

        private void Update()
        {
            if (_state == State.Idle) return;

            int st = GameSocket.State;
            if (st == GameSocket.Closed && Time.unscaledTime - _connectAt > 1f)
            {
                if (_state == State.Watch) BackToList("Lost connection to the server.");
                else _status.text = "Couldn't connect to the server. Go back and try again.";
            }
            if (st == GameSocket.Open && _state == State.List && Time.unscaledTime >= _nextList)
            {
                _nextList = Time.unscaledTime + 4f;
                GameSocket.Send("{\"t\":\"list\"}");
            }
            if (st == GameSocket.Connecting && Time.unscaledTime - _connectAt > 90f) { GameSocket.Close(); }

            string raw;
            int guard = 0;
            while (_state != State.Idle && guard++ < 200 && GameSocket.TryReceive(out raw)) Handle(raw);
            if (_state == State.Idle) return;

            if (_state == State.Watch)
            {
                _left = Mathf.Max(0f, _left - Time.unscaledDeltaTime);
                UpdateRemotes();
                UpdateCameraTarget();
                ReadInput();
                UpdateHud();
            }
            else if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Exit();
        }

        private void Handle(string raw)
        {
            Head h;
            try { h = JsonUtility.FromJson<Head>(raw); } catch (Exception) { return; }
            if (h == null || string.IsNullOrEmpty(h.t)) return;
            switch (h.t)
            {
                case "list":
                    if (_state == State.List) ShowList(JsonUtility.FromJson<ListMsg>(raw));
                    break;
                case "view":
                    ApplyView(JsonUtility.FromJson<ViewMsg>(raw));
                    break;
                case "viewend":
                    if (_state == State.Watch) BackToList("That game ended. Pick another one.");
                    break;
                case "err":
                    var e = JsonUtility.FromJson<ErrMsg>(raw);
                    if (_state == State.List && e != null) _status.text = e.msg;
                    break;
            }
        }

        private void ShowList(ListMsg m)
        {
            var items = m != null && m.items != null ? m.items : new Item[0];
            _status.text = items.Length == 0 ? "No one is playing right now (or everyone has spectators switched off). This list refreshes by itself." : "Pick a game to watch.";
            for (int i = 0; i < Rows; i++)
            {
                bool has = i < items.Length;
                _rowBtn[i].gameObject.SetActive(has);
                if (!has) continue;
                var it = items[i];
                _rowKey[i] = it.key;
                string map = it.map >= 0 && it.map < MapManager.Count ? MapManager.Names[it.map] : "?";
                _rowText[i].text = it.label + "   -   " + map + "   -   " + it.players + (it.players == 1 ? " player" : " players") + (it.phase == "lobby" ? "   (lobby)" : "");
            }
        }

        private void WatchKey(string key)
        {
            _watchKey = key;
            GameSocket.Send("{\"t\":\"watch\",\"key\":\"" + key + "\"}");
            _status.text = "Loading...";
        }

        private void WatchCode()
        {
            string c = _code.text.Trim().ToUpperInvariant();
            if (c.Length != 4) { _status.text = "Room codes have 4 letters."; return; }
            foreach (char ch in c) if (!char.IsLetter(ch)) { _status.text = "Room codes have 4 letters."; return; }
            GameSocket.Send("{\"t\":\"watch\",\"code\":\"" + c + "\"}");
            _status.text = "Looking for that room...";
        }

        // ------------------------------------------------------------------ the 3D view

        private void ApplyView(ViewMsg v)
        {
            if (v == null || v.players == null) return;
            if (_state == State.List)
            {
                _state = State.Watch;
                _listPanel.SetActive(false);
                _hud.SetActive(true);
                if (_follow < 0 && v.players.Length > 0) _follow = v.players[0].id;
            }
            if (_state != State.Watch) return;

            if (v.map != _curMap)
            {
                _curMap = v.map;
                if (MapManager.I != null) MapManager.I.Load(v.map);
                ClearRemotes();
            }
            _view = v;
            _left = v.left;

            var seen = new HashSet<int>();
            foreach (var p in v.players)
            {
                seen.Add(p.id);
                if (!_remotes.TryGetValue(p.id, out var r)) r = CreateRemote(p, v.kind == "practice");
                r.target = new Vector3(p.x, _baseY, p.z);
                r.yaw = p.ry * Mathf.Rad2Deg;
            }
            List<int> gone = null;
            foreach (var id in _remotes.Keys) if (!seen.Contains(id)) (gone ??= new List<int>()).Add(id);
            if (gone != null) foreach (var id in gone) { Destroy(_remotes[id].t.gameObject); _remotes.Remove(id); }
            if (_follow >= 0 && !_remotes.ContainsKey(_follow)) _follow = v.players.Length > 0 ? v.players[0].id : -1;
        }

        private Rem CreateRemote(VP p, bool practice)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "Watched_" + p.n;
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var rend = go.GetComponent<Renderer>();
            Material baseMat = _playerMat != null ? _playerMat : rend.sharedMaterial;
            Color c = practice ? (p.c == 0 ? MultiplayerGame.ColorOf(0) : new Color(0.82f, 0.12f, 0.82f)) : MultiplayerGame.ColorOf(p.c);
            rend.sharedMaterial = new Material(baseMat) { color = c };
            go.transform.position = new Vector3(p.x, _baseY, p.z);
            PlayerFace.Attach(go, p.n, baseMat);
            var r = new Rem { t = go.transform, target = go.transform.position, yaw = p.ry * Mathf.Rad2Deg, name = p.n };
            TagGame.Juice.MapBlips.Register(go.transform, c);
            _remotes[p.id] = r;
            return r;
        }

        private void ClearRemotes()
        {
            foreach (var r in _remotes.Values) if (r.t != null) Destroy(r.t.gameObject);
            _remotes.Clear();
        }

        private void UpdateRemotes()
        {
            float k = 1f - Mathf.Exp(-12f * Time.deltaTime);
            foreach (var r in _remotes.Values)
            {
                if (r.t == null) continue;
                r.t.position = Vector3.Lerp(r.t.position, r.target, k);
                r.t.rotation = Quaternion.Slerp(r.t.rotation, Quaternion.Euler(0f, r.yaw, 0f), k);
            }
            // red ball over whoever is IT
            if (_view != null && _remotes.TryGetValue(_view.it, out var it) && it.t != null)
            {
                if (_marker == null)
                {
                    _marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    var c = _marker.GetComponent<Collider>(); if (c != null) Destroy(c);
                    _marker.name = "ItMarker";
                    var rd = _marker.GetComponent<Renderer>();
                    if (_playerMat != null) rd.sharedMaterial = new Material(_playerMat) { color = new Color(1f, 0.15f, 0.15f) };
                    rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    _marker.transform.localScale = Vector3.one * 0.6f;
                }
                _marker.SetActive(true);
                TagGame.Juice.MapBlips.It = it.t;
                _marker.transform.position = it.t.position + Vector3.up * (2.3f + Mathf.Sin(Time.time * 5f) * 0.12f);
            }
            else if (_marker != null) _marker.SetActive(false);
        }

        private void UpdateCameraTarget()
        {
            if (_follow >= 0 && _remotes.TryGetValue(_follow, out var r) && r.t != null)
            {
                _target.position = Vector3.Lerp(_target.position, r.t.position, 1f - Mathf.Exp(-10f * Time.deltaTime));
                return;
            }
            // free camera: fly around relative to where the camera looks
            Vector2 mv = Vector2.zero;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) mv.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) mv.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) mv.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) mv.x -= 1f;
            }
            var pad = Gamepad.current;
            if (pad != null && mv.sqrMagnitude < 0.01f) mv = pad.leftStick.ReadValue();
            if (mv.sqrMagnitude > 1f) mv.Normalize();
            float speed = 20f * ((kb != null && kb.leftShiftKey.isPressed) ? 3f : 1f);
            Vector3 fwd = Camera.main != null ? Camera.main.transform.forward : Vector3.forward;
            fwd.y = 0f; fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
            Vector3 right = new Vector3(fwd.z, 0f, -fwd.x);
            Vector3 np = _target.position + (fwd * mv.y + right * mv.x) * speed * Time.deltaTime;
            float lim = MapManager.I != null ? MapManager.I.PlayLimit : 30f;
            np.x = Mathf.Clamp(np.x, -lim, lim); np.z = Mathf.Clamp(np.z, -lim, lim); np.y = _baseY;
            _target.position = np;
        }

        private void ReadInput()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.tabKey.wasPressedThisFrame) Cycle(kb.leftShiftKey.isPressed ? -1 : 1);
                if (kb.fKey.wasPressedThisFrame) ToggleFree();
                if (kb.escapeKey.wasPressedThisFrame) Exit();
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                if (pad.rightShoulder.wasPressedThisFrame) Cycle(1);
                if (pad.leftShoulder.wasPressedThisFrame) Cycle(-1);
                if (pad.buttonNorth.wasPressedThisFrame) ToggleFree();
                if (pad.buttonEast.wasPressedThisFrame) Exit();
            }
        }

        private void Cycle(int dir)
        {
            if (_view == null || _view.players == null || _view.players.Length == 0) return;
            int idx = -1;
            for (int i = 0; i < _view.players.Length; i++) if (_view.players[i].id == _follow) idx = i;
            idx = idx < 0 ? 0 : (idx + dir + _view.players.Length) % _view.players.Length;
            _follow = _view.players[idx].id;
        }

        private void ToggleFree()
        {
            if (_follow >= 0) _follow = -1;
            else if (_view != null && _view.players != null && _view.players.Length > 0) _follow = _view.players[0].id;
        }

        // ------------------------------------------------------------------ HUD

        private void UpdateHud()
        {
            if (_view == null) return;
            string map = _view.map >= 0 && _view.map < MapManager.Count ? MapManager.Names[_view.map] : "?";
            int secs = Mathf.CeilToInt(_left);
            string time = _view.phase == "lobby" ? "Lobby" : (secs / 60) + ":" + (secs % 60).ToString("00");
            _title.text = _view.label + "  -  " + map + "  -  " + time;

            string itName = "";
            string followName = "Free camera";
            var sorted = new List<VP>(_view.players);
            foreach (var p in sorted)
            {
                if (p.id == _view.it) itName = p.n;
                if (p.id == _follow) followName = "Following " + p.n;
            }
            _itText.text = (itName.Length > 0 ? "<color=#FF5050>" + itName + "</color> is IT" : "") + "   -   " + followName;

            sorted.Sort((a, b) => b.score.CompareTo(a.score));
            var sb = new StringBuilder();
            foreach (var p in sorted) sb.AppendLine(p.n + "  " + p.score + "s" + (p.id == _view.it ? "  <color=#FF5050>IT</color>" : ""));
            if (_view.hidden > 0) sb.AppendLine("<size=80%>" + _view.hidden + " hidden (spectators off)</size>");
            _board.text = sb.ToString();
        }

        // ------------------------------------------------------------------ UI

        private void BuildUI()
        {
            _canvasGo = new GameObject("SpectateCanvas");
            _canvasGo.transform.SetParent(transform, false);
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 62;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
            var root = _canvasGo.transform;
            var C = MultiplayerGame.C; var T = MultiplayerGame.T; var TL = MultiplayerGame.TL; var BR = MultiplayerGame.BR;

            _listPanel = MultiplayerGame.MakePanel(root, "SpectateList", new Color(0f, 0f, 0f, 0.8f));
            var lp = _listPanel.transform;
            MultiplayerGame.MakeText(lp, "SPECTATE", 56, C, new Vector2(0, 290), new Vector2(800, 80));
            _status = MultiplayerGame.MakeText(lp, "", 24, C, new Vector2(0, 225), new Vector2(1000, 60));
            for (int i = 0; i < Rows; i++)
            {
                int idx = i;
                var b = MultiplayerGame.MakeButton(lp, "", C, new Vector2(0, 150 - i * 62), new Vector2(760, 54), new Color(0.2f, 0.45f, 0.85f, 0.95f), () => WatchKey(_rowKey[idx]), 26);
                _rowBtn[i] = b;
                _rowText[i] = b.GetComponentInChildren<TMP_Text>();
                b.gameObject.SetActive(false);
            }
            MultiplayerGame.MakeText(lp, "Private room? Type its code", 22, C, new Vector2(0, -250), new Vector2(700, 36));
            _code = MultiplayerGame.MakeInput(lp, "CODE", C, new Vector2(-110, -305), new Vector2(200, 56), 4, true);
            MultiplayerGame.MakeButton(lp, "Watch", C, new Vector2(100, -305), new Vector2(200, 56), new Color(0.15f, 0.6f, 0.3f), WatchCode, 28);
            MultiplayerGame.MakeButton(lp, "Back", C, new Vector2(0, -362), new Vector2(220, 46), new Color(0.5f, 0.2f, 0.2f), Exit, 24);
            _listPanel.SetActive(false);

            _hud = new GameObject("SpectateHud", typeof(RectTransform));
            _hud.transform.SetParent(root, false);
            MultiplayerGame.Stretch((RectTransform)_hud.transform);
            var hp = _hud.transform;
            _title = MultiplayerGame.MakeText(hp, "", 34, T, new Vector2(0, -35), new Vector2(1000, 50));
            _itText = MultiplayerGame.MakeText(hp, "", 26, T, new Vector2(0, -82), new Vector2(1000, 40));
            _board = MultiplayerGame.MakeText(hp, "", 22, TL, new Vector2(20, -120), new Vector2(340, 300));
            _board.alignment = TextAlignmentOptions.TopLeft;
            var BC = new Vector2(0.5f, 0f);
            MultiplayerGame.MakeButton(hp, "< Prev", BC, new Vector2(-330, 40), new Vector2(150, 50), new Color(0.2f, 0.45f, 0.85f, 0.9f), () => Cycle(-1), 24);
            MultiplayerGame.MakeButton(hp, "Next >", BC, new Vector2(-165, 40), new Vector2(150, 50), new Color(0.2f, 0.45f, 0.85f, 0.9f), () => Cycle(1), 24);
            MultiplayerGame.MakeButton(hp, "Free cam", BC, new Vector2(0, 40), new Vector2(170, 50), new Color(0.15f, 0.6f, 0.3f, 0.9f), ToggleFree, 24);
            MultiplayerGame.MakeButton(hp, "Exit", BC, new Vector2(185, 40), new Vector2(150, 50), new Color(0.6f, 0.2f, 0.2f, 0.9f), Exit, 24);
            _hint = MultiplayerGame.MakeText(hp, "Tab: next player   F: free camera   Right mouse: look   Scroll: zoom   Esc: exit", 18, BC, new Vector2(0, 100), new Vector2(1100, 30));
            _hint.color = new Color(1f, 1f, 1f, 0.7f);
            _hud.SetActive(false);
        }
    }
}
