using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TagGame.UI
{
    /// <summary>Big banner that pops up in the middle of the screen whenever someone gets tagged.</summary>
    public class TagPopup : MonoBehaviour
    {
        private static TagPopup _inst;
        private RectTransform _box;
        private TMP_Text _text;
        private Image _bg;
        private TMP_Text _status;
        private float _t = 99f;
        private const float Life = 1.9f;

        /// <summary>Small line under the timer ("YOU ARE IT!", "Tiger 3 is IT", safe seconds...). Empty hides it.</summary>
        public static void SetStatus(string text)
        {
            if (_inst == null)
            {
                if (string.IsNullOrEmpty(text)) return;
                var go = new GameObject("TagPopup");
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<TagPopup>();
                _inst.Build();
            }
            _inst._status.text = text;
        }

        public static void Show(string text, Color accent)
        {
            if (_inst == null)
            {
                var go = new GameObject("TagPopup");
                DontDestroyOnLoad(go);
                _inst = go.AddComponent<TagPopup>();
                _inst.Build();
            }
            _inst._text.text = text;
            _inst._bg.color = new Color(accent.r * 0.55f, accent.g * 0.55f, accent.b * 0.55f, 0.92f);
            _inst._t = 0f;
            _inst._box.gameObject.SetActive(true);
        }

        private void Build()
        {
            var cgo = new GameObject("Canvas", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 500;
            var sc = cgo.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920f, 1080f);
            sc.matchWidthOrHeight = 0.5f;

            var bx = new GameObject("Box", typeof(RectTransform), typeof(Image));
            bx.transform.SetParent(cgo.transform, false);
            _box = (RectTransform)bx.transform;
            _box.anchorMin = _box.anchorMax = _box.pivot = new Vector2(0.5f, 0.5f);
            _box.anchoredPosition = new Vector2(0f, 190f);
            _box.sizeDelta = new Vector2(900f, 130f);
            _bg = bx.GetComponent<Image>();
            _bg.raycastTarget = false;

            var tgo = new GameObject("Text", typeof(RectTransform));
            tgo.transform.SetParent(bx.transform, false);
            var rt = (RectTransform)tgo.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            _text = tgo.AddComponent<TextMeshProUGUI>();
            _text.alignment = TextAlignmentOptions.Center;
            _text.fontSize = 56;
            _text.fontStyle = FontStyles.Bold;
            _text.color = Color.white;
            _text.raycastTarget = false;
            _text.enableAutoSizing = true;
            _text.fontSizeMin = 24;
            _text.fontSizeMax = 56;
            _box.gameObject.SetActive(false);

            var sgo = new GameObject("Status", typeof(RectTransform));
            sgo.transform.SetParent(cgo.transform, false);
            var srt = (RectTransform)sgo.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(0.5f, 1f);
            srt.anchoredPosition = new Vector2(0f, -110f);
            srt.sizeDelta = new Vector2(1100f, 60f);
            _status = sgo.AddComponent<TextMeshProUGUI>();
            _status.alignment = TextAlignmentOptions.Center;
            _status.fontSize = 36;
            _status.fontStyle = FontStyles.Bold;
            _status.color = Color.white;
            _status.raycastTarget = false;
            _status.richText = true;
        }

        private void Update()
        {
            if (_box == null || !_box.gameObject.activeSelf) return;
            _t += Time.unscaledDeltaTime;
            if (_t >= Life) { _box.gameObject.SetActive(false); return; }
            float pop = Mathf.Clamp01(_t / 0.18f);
            float s = Mathf.Lerp(0.4f, 1f, 1f - (1f - pop) * (1f - pop)) + Mathf.Sin(pop * Mathf.PI) * 0.15f;
            _box.localScale = Vector3.one * s;
            float fade = Mathf.Clamp01((Life - _t) / 0.4f);
            _text.alpha = fade;
            var c = _bg.color; c.a = 0.92f * fade; _bg.color = c;
        }
    }
}
