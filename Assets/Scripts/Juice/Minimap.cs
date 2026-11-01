using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TagGame.Gameplay;
using TagGame.Maps;
using TagGame.UI;

namespace TagGame.Juice
{
    /// <summary>
    /// Top-right minimap for the big maps (walls and buildings drawn from the map's walk grid, dots for you,
    /// the tigers / other players, speed gems, and a red ring on whoever is IT), plus sprint and dash bars.
    /// </summary>
    public class Minimap : MonoBehaviour
    {
        private const float Size = 190f;
        private PlayerController _player;
        private GameFlowUI _flow;
        private RectTransform _mapRt, _dotsRt, _barsRt;
        private RawImage _img;
        private Image _stamina, _dash;
        private Texture2D _tex;
        private readonly List<Image> _dots = new List<Image>();
        private Image _me, _itRing;
        private float _half = 1f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (FindFirstObjectByType<Minimap>() != null) return;
            if (FindFirstObjectByType<PlayerController>() == null) return;
            new GameObject("Minimap").AddComponent<Minimap>();
        }

        private void Start()
        {
            _player = FindFirstObjectByType<PlayerController>();
            _flow = FindFirstObjectByType<GameFlowUI>();
            var cgo = new GameObject("MinimapCanvas", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;
            var sc = cgo.AddComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1280f, 720f);
            sc.matchWidthOrHeight = 0.5f;

            var frame = Box(cgo.transform, "MapFrame", new Vector2(-16f, -16f), new Vector2(Size + 8f, Size + 8f), new Color(0f, 0f, 0f, 0.55f));
            _mapRt = frame;
            var ig = new GameObject("Map", typeof(RectTransform), typeof(RawImage));
            ig.transform.SetParent(frame, false);
            var irt = (RectTransform)ig.transform;
            irt.anchorMin = irt.anchorMax = new Vector2(0.5f, 0.5f);
            irt.sizeDelta = new Vector2(Size, Size);
            _img = ig.GetComponent<RawImage>();
            _img.raycastTarget = false;
            _dotsRt = irt;
            _itRing = Dot(new Color(1f, 0.2f, 0.2f), 14f);
            _me = Dot(new Color(0.15f, 0.9f, 0.95f), 10f);

            _barsRt = Box(cgo.transform, "Bars", new Vector2(-16f, -16f - Size - 16f), new Vector2(Size + 8f, 30f), new Color(0f, 0f, 0f, 0.45f));
            _stamina = Bar(_barsRt, 0f, new Color(0.2f, 0.85f, 0.9f));
            _dash = Bar(_barsRt, 1f, new Color(1f, 0.8f, 0.2f));

            if (MapManager.I != null) { MapManager.I.OnMapChanged += Rebuild; Rebuild(MapManager.I.Current); }
        }

        private void OnDestroy() { if (MapManager.I != null) MapManager.I.OnMapChanged -= Rebuild; }

        private RectTransform Box(Transform parent, string name, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var im = go.GetComponent<Image>(); im.color = c; im.raycastTarget = false;
            return rt;
        }

        private Image Bar(RectTransform parent, float row, Color c)
        {
            var go = new GameObject("Bar", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = new Vector2(0f, 1f - (row + 1f) / 2f); rt.anchorMax = new Vector2(1f, 1f - row / 2f);
            rt.offsetMin = new Vector2(6f, 3f); rt.offsetMax = new Vector2(-6f, -3f);
            rt.pivot = new Vector2(0f, 0.5f);
            var im = go.GetComponent<Image>(); im.color = c; im.raycastTarget = false;
            return im;
        }

        private Image Dot(Color c, float size)
        {
            var go = new GameObject("Dot", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(_dotsRt, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            var im = go.GetComponent<Image>(); im.color = c; im.raycastTarget = false;
            return im;
        }

        private void Rebuild(int map)
        {
            var grid = MapManager.I != null ? MapManager.I.Grid : null;
            if (grid == null) { _img.texture = null; return; }
            _half = MapManager.I.PlayLimit + 4f;
            const int N = 160;
            if (_tex == null) { _tex = new Texture2D(N, N, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp }; }
            var px = new Color32[N * N];
            var free = new Color32(214, 206, 184, 230);
            var wall = new Color32(58, 64, 72, 255);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float wx = (x + 0.5f) / N * 2f * _half - _half, wz = (y + 0.5f) / N * 2f * _half - _half;
                    px[y * N + x] = grid.IsBlocked(wx, wz) ? wall : free;
                }
            _tex.SetPixels32(px); _tex.Apply(false);
            _img.texture = _tex;
        }

        private Vector2 ToMap(Vector3 w) => new Vector2(w.x / _half * Size * 0.5f, w.z / _half * Size * 0.5f);

        private void Update()
        {
            if (_player == null) return;
            bool title = _flow != null && _flow.StartPanel != null && _flow.StartPanel.activeInHierarchy;
            bool showMap = !title && _img.texture != null;
            _mapRt.gameObject.SetActive(showMap);
            bool showBars = !title && _player.isActiveAndEnabled;
            _barsRt.gameObject.SetActive(showBars);
            if (showBars)
            {
                _stamina.rectTransform.localScale = new Vector3(_player.StaminaNormalized, 1f, 1f);
                _dash.rectTransform.localScale = new Vector3(_player.DashReady, 1f, 1f);
            }
            if (!showMap) return;

            MapBlips.Prune();
            int n = 0;
            foreach (var b in MapBlips.Items)
            {
                if (!b.t.gameObject.activeInHierarchy) continue;
                if (n >= _dots.Count) _dots.Add(Dot(Color.white, 8f));
                var d = _dots[n++];
                d.gameObject.SetActive(true);
                d.color = b.c;
                d.rectTransform.sizeDelta = Vector2.one * 8f * b.size;
                d.rectTransform.anchoredPosition = ToMap(b.t.position);
            }
            for (int i = n; i < _dots.Count; i++) _dots[i].gameObject.SetActive(false);

            bool meOn = _player.isActiveAndEnabled;
            _me.gameObject.SetActive(meOn);
            if (meOn) { _me.rectTransform.anchoredPosition = ToMap(_player.transform.position); _me.transform.SetAsLastSibling(); }
            var it = MapBlips.It;
            bool itOn = it != null && it.gameObject.activeInHierarchy;
            _itRing.gameObject.SetActive(itOn);
            if (itOn) _itRing.rectTransform.anchoredPosition = ToMap(it.position);
            _itRing.transform.SetAsFirstSibling();
        }
    }
}
