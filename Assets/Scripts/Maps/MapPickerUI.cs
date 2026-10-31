using TMPro;
using UnityEngine;
using UnityEngine.UI;
using TagGame.UI;

namespace TagGame.Maps
{
    /// <summary>Practice-mode map picker, shown on the start screen (three buttons: Arena / School / City).</summary>
    public class MapPickerUI : MonoBehaviour
    {
        private GameObject _root;
        private Image[] _bg = new Image[MapManager.Count];
        private GameFlowUI _flow;

        private void Start()
        {
            _flow = FindFirstObjectByType<GameFlowUI>();
            if (_flow == null || _flow.StartPanel == null) { enabled = false; return; }

            _root = new GameObject("MapPicker", typeof(RectTransform));
            _root.transform.SetParent(_flow.StartPanel.transform, false);
            var rt = (RectTransform)_root.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(0f, 40f);
            rt.sizeDelta = new Vector2(1000f, 170f);

            Label("PRACTICE MAP", 26, new Vector2(0f, 150f), new Vector2(600f, 36f));
            for (int i = 0; i < MapManager.Count; i++)
            {
                int id = i;
                var go = new GameObject("Map" + i, typeof(RectTransform), typeof(Image), typeof(Button));
                go.transform.SetParent(_root.transform, false);
                var r = (RectTransform)go.transform;
                r.anchorMin = r.anchorMax = r.pivot = new Vector2(0.5f, 0f);
                r.anchoredPosition = new Vector2((i - 1) * 320f, 10f);
                r.sizeDelta = new Vector2(300f, 110f);
                _bg[i] = go.GetComponent<Image>();
                go.GetComponent<Button>().onClick.AddListener(() => { MapManager.I.Select(id); Refresh(); });
                var t1 = Label(MapManager.Names[i], 36, Vector2.zero, new Vector2(290f, 50f), go.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, 18f));
                t1.fontStyle = FontStyles.Bold;
                Label(MapManager.Blurbs[i], 18, Vector2.zero, new Vector2(290f, 40f), go.transform, new Vector2(0.5f, 0.5f), new Vector2(0f, -24f));
            }
            Refresh();
        }

        private TMP_Text Label(string text, float size, Vector2 pos, Vector2 dim, Transform parent = null, Vector2? anchor = null, Vector2? offset = null)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.transform.SetParent(parent != null ? parent : _root.transform, false);
            var r = (RectTransform)go.transform;
            var a = anchor ?? new Vector2(0.5f, 0f);
            r.anchorMin = r.anchorMax = r.pivot = a;
            r.anchoredPosition = offset ?? pos;
            r.sizeDelta = dim;
            var t = go.AddComponent<TextMeshProUGUI>();
            t.text = text; t.fontSize = size; t.alignment = TextAlignmentOptions.Center; t.color = Color.white; t.raycastTarget = false;
            return t;
        }

        private void Update()
        {
            // Only show while the start screen is up.
            if (_root != null) { bool on = _flow.StartPanel.activeInHierarchy; if (_root.activeSelf != on) _root.SetActive(on); }
        }

        private void Refresh()
        {
            for (int i = 0; i < _bg.Length; i++)
                _bg[i].color = i == MapManager.I.Selected ? new Color(0.15f, 0.6f, 0.3f, 0.95f) : new Color(0.15f, 0.15f, 0.2f, 0.85f);
        }
    }
}
