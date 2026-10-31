using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TagGame.Gameplay;
using TagGame.Multiplayer;
using TagGame.UI;

namespace TagGame.Spectate
{
    /// <summary>
    /// Lets other people watch your Practice round on the website. When "Allow spectators" is on, a Practice
    /// round streams where you, the tigers and who is IT are to the game server about 10 times a second.
    /// Adds the on/off switch to the start screen. Nothing is sent when it is off.
    /// </summary>
    public class PracticeSpectate : MonoBehaviour
    {
        private TagGameManager _manager;
        private GameFlowUI _flow;
        private TMP_Text _label;
        private bool _owns;
        private float _nextSend, _nextTry;
        private bool _wasStreaming;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<PracticeSpectate>() != null) return;
            if (FindFirstObjectByType<TagGameManager>() == null) return;
            new GameObject("PracticeSpectate").AddComponent<PracticeSpectate>();
        }

        private void Start()
        {
            _manager = FindFirstObjectByType<TagGameManager>();
            _flow = FindFirstObjectByType<GameFlowUI>();
            if (_flow == null || _flow.StartPanel == null) return;

            var go = new GameObject("SpectateToggle", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_flow.StartPanel.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 232f);
            rt.sizeDelta = new Vector2(330f, 34f);
            var img = go.GetComponent<Image>();
            img.color = new Color(0.15f, 0.15f, 0.2f, 0.85f);
            go.GetComponent<Button>().onClick.AddListener(Toggle);

            var tgo = new GameObject("Text", typeof(RectTransform));
            tgo.transform.SetParent(go.transform, false);
            var trt = (RectTransform)tgo.transform;
            trt.anchorMin = Vector2.zero; trt.anchorMax = Vector2.one; trt.offsetMin = trt.offsetMax = Vector2.zero;
            _label = tgo.AddComponent<TextMeshProUGUI>();
            _label.fontSize = 18; _label.alignment = TextAlignmentOptions.Center; _label.color = Color.white; _label.raycastTarget = false;
            _label.text = SpectateSettings.Label;
        }

        private void Toggle()
        {
            SpectateSettings.Allow = !SpectateSettings.Allow;
            if (_label != null) _label.text = SpectateSettings.Label;
        }

        private static string Url()
        {
            if (PlayerPrefs.GetInt("TagGame.MpServerMode", 0) == 1)
            {
                string custom = PlayerPrefs.GetString("TagGame.MpCustomUrl", "");
                if (!string.IsNullOrEmpty(custom)) return custom;
            }
            return MultiplayerGame.OfficialServerUrl;
        }

        private void Update()
        {
            if (_manager == null) return;
            bool want = SpectateSettings.Allow && _manager.RoundInProgress;

            if (want)
            {
                if (GameSocket.State == GameSocket.Closed && Time.unscaledTime >= _nextTry)
                {
                    _nextTry = Time.unscaledTime + 15f;
                    GameSocket.Connect(Url());
                    _owns = true;
                }
                string drain;
                while (_owns && GameSocket.TryReceive(out drain)) { }   // nothing we need from the server
                if (_owns && GameSocket.State == GameSocket.Open && Time.unscaledTime >= _nextSend)
                {
                    _nextSend = Time.unscaledTime + 0.1f;
                    GameSocket.Send(_manager.SpectateJson(SpectateSettings.DisplayName(), MapNow()));
                    _wasStreaming = true;
                }
            }
            else if (_wasStreaming || _owns)
            {
                if (GameSocket.State == GameSocket.Open) GameSocket.Send("{\"t\":\"pend\"}");
                if (_owns) GameSocket.Close();
                _owns = false; _wasStreaming = false;
            }
        }

        private static int MapNow() => TagGame.Maps.MapManager.I != null ? TagGame.Maps.MapManager.I.Current : 0;
    }
}
