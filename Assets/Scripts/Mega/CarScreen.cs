using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TagGame.Maps;
using MG = TagGame.Multiplayer.MultiplayerGame;

namespace TagGame.Mega
{
    /// <summary>Drag, tap and scroll on the car's map.</summary>
    public class MapPointer : MonoBehaviour, IBeginDragHandler, IDragHandler, IPointerClickHandler, IScrollHandler
    {
        public CarScreen Screen;
        public void OnBeginDrag(PointerEventData e) { Screen.Follow = false; }
        public void OnDrag(PointerEventData e) { Screen.Pan(e.delta); }
        public void OnPointerClick(PointerEventData e) { if (!e.dragging) Screen.ClickMap(e.position, e.pressEventCamera); }
        public void OnScroll(PointerEventData e) { Screen.Zoom(e.scrollDelta.y > 0 ? 0.5 : 2.0); }
    }

    /// <summary>
    /// The car's big touchscreen (our own design): a self-driving view of the road around you on the left (lanes,
    /// other cars, people, the next traffic light and your route), and a map of the whole world on the right that you
    /// can drag, zoom from street level out to all 1,000,000 km of Mega Earth, search, and tap to pick a destination.
    /// Press NAVIGATE and self-driving takes you there, turning by itself, and parks when you arrive. M: bigger map.
    /// </summary>
    public class CarScreen : MonoBehaviour
    {
        public static CarScreen I { get; private set; }
        public static bool Typing => I != null && I._search != null && I._search.isFocused;

        public bool Follow = true;
        private bool _shown, _big;

        // layout
        private GameObject _canvas, _panel;
        private RectTransform _panelRt, _vizRt, _vizWorld, _mapRt, _overlay;
        private TMP_Text _speed, _gear, _status, _nav, _signalL, _signalR, _lightText, _scaleText;
        private Image _fsdBtn, _lightDot;
        private TMP_InputField _search;
        private GameObject _results, _card;
        private readonly List<Button> _resultBtns = new List<Button>();
        private readonly List<MegaPlaces.Place> _resultPlaces = new List<MegaPlaces.Place>();
        private TMP_Text _cardTitle, _cardDetail, _cardGo;
        private MegaPlaces.Place _cardPlace;
        private float _searchAt; private string _lastQuery = "";

        // map
        private RawImage _mapImg;
        private Texture2D _tex;
        private Color32[] _px;
        private int _tw = 400, _th = 124, _row;
        private double _cx, _cz, _mpp = 4.0;            // current view (world centre, metres per pixel)
        private double _rcx, _rcz, _rmpp;                 // the view the texture is being drawn for
        private bool _viewDirty = true;
        private readonly List<TMP_Text> _labels = new List<TMP_Text>();
        private readonly List<Image> _routeDots = new List<Image>();
        private Image _me, _meHead, _pin;
        private float _labelsAt;

        // visualization
        private const float VizScale = 3.2f;   // pixels per metre
        private readonly List<Image> _vizRoads = new List<Image>(), _vizCars = new List<Image>(), _vizPeople = new List<Image>(), _vizRoute = new List<Image>();
        private Image _vizMe;
        private float _vizAt;

        private void Awake() { I = this; }

        // ================================================================== building the screen

        private static Image Img(Transform parent, string name, Color c, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var im = go.GetComponent<Image>(); im.color = c; im.raycastTarget = false;
            var rt = (RectTransform)go.transform; rt.sizeDelta = size;
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            return im;
        }

        private void Build()
        {
            if (_canvas != null) return;
            _canvas = new GameObject("CarScreenCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var cv = _canvas.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 43;
            var sc = _canvas.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;

            _panel = new GameObject("CarScreen", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(_canvas.transform, false);
            _panelRt = (RectTransform)_panel.transform;
            _panel.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.08f, 0.95f);
            Layout();

            // ---- top bar
            var bar = new GameObject("Bar", typeof(RectTransform)).GetComponent<RectTransform>();
            bar.SetParent(_panelRt, false);
            bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = new Vector2(1f, 1f); bar.pivot = new Vector2(0.5f, 1f);
            bar.offsetMin = new Vector2(0f, -62f); bar.offsetMax = Vector2.zero;
            _speed = MG.MakeText(bar, "0", 50, new Vector2(0f, 0.5f), new Vector2(70f, 0f), new Vector2(130f, 60f)); _speed.fontStyle = FontStyles.Bold;
            var kmh = MG.MakeText(bar, "km/h", 16, new Vector2(0f, 0.5f), new Vector2(150f, -14f), new Vector2(60f, 24f)); kmh.color = new Color(0.6f, 0.65f, 0.7f);
            _gear = MG.MakeText(bar, "P", 30, new Vector2(0f, 0.5f), new Vector2(205f, 0f), new Vector2(60f, 50f)); _gear.color = new Color(0.55f, 0.85f, 1f);
            _signalL = MG.MakeText(bar, "<<", 30, new Vector2(0f, 0.5f), new Vector2(255f, 0f), new Vector2(50f, 50f)); _signalL.color = new Color(1f, 0.6f, 0.1f);
            _signalR = MG.MakeText(bar, ">>", 30, new Vector2(0f, 0.5f), new Vector2(300f, 0f), new Vector2(50f, 50f)); _signalR.color = new Color(1f, 0.6f, 0.1f);
            _status = MG.MakeText(bar, "", 20, new Vector2(0f, 0.5f), new Vector2(560f, 12f), new Vector2(460f, 28f)); _status.alignment = TextAlignmentOptions.Left; _status.color = new Color(0.55f, 0.85f, 1f);
            _nav = MG.MakeText(bar, "", 18, new Vector2(0f, 0.5f), new Vector2(560f, -14f), new Vector2(460f, 26f)); _nav.alignment = TextAlignmentOptions.Left;
            float bx = -60f;
            Button B(string label, UnityEngine.Events.UnityAction a, Color c, float w)
            {
                var b = MG.MakeButton(bar, label, new Vector2(1f, 0.5f), new Vector2(bx - w * 0.5f, 0f), new Vector2(w, 46f), c, a, 18);
                bx -= w + 10f;
                return b;
            }
            B("GET OUT  E", () => { var m = MegaCars.I; if (m != null && m.Mine != null && Mathf.Abs(m.Mine.Speed) < 2f) m.Exit(); }, new Color(0.55f, 0.18f, 0.18f), 130f);
            B("MAP  M", ToggleBig, new Color(0.25f, 0.27f, 0.33f), 100f);
            B("CAMERA  C", () => MegaCars.I?.ToggleCamera(), new Color(0.25f, 0.27f, 0.33f), 130f);
            B("AUTO PARK  P", () => MegaCars.I?.OpenAutoPark(), new Color(0.15f, 0.5f, 0.3f), 150f);
            _fsdBtn = B("SELF-DRIVE  F", () => MegaCars.I?.ToggleSelfDriving(), new Color(0.18f, 0.38f, 0.75f), 160f).GetComponent<Image>();

            // ---- self-driving view (left)
            var viz = new GameObject("Viz", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
            viz.transform.SetParent(_panelRt, false);
            _vizRt = (RectTransform)viz.transform;
            _vizRt.anchorMin = new Vector2(0f, 0f); _vizRt.anchorMax = new Vector2(0f, 1f); _vizRt.pivot = new Vector2(0f, 0.5f);
            _vizRt.offsetMin = new Vector2(12f, 12f); _vizRt.offsetMax = new Vector2(512f, -70f);
            viz.GetComponent<Image>().color = new Color(0.09f, 0.1f, 0.13f);
            _vizWorld = new GameObject("World", typeof(RectTransform)).GetComponent<RectTransform>();
            _vizWorld.SetParent(_vizRt, false);
            _vizWorld.anchorMin = _vizWorld.anchorMax = new Vector2(0.5f, 0.3f); _vizWorld.sizeDelta = Vector2.zero;
            _vizMe = Img(_vizRt, "Me", new Color(0.95f, 0.95f, 0.97f), new Vector2(2f * VizScale, 4.6f * VizScale));
            var meRt = _vizMe.rectTransform; meRt.anchorMin = meRt.anchorMax = new Vector2(0.5f, 0.3f);
            _lightDot = Img(_vizRt, "Light", Color.green, new Vector2(22f, 22f));
            _lightDot.rectTransform.anchorMin = _lightDot.rectTransform.anchorMax = new Vector2(0f, 1f); _lightDot.rectTransform.anchoredPosition = new Vector2(24f, -24f);
            _lightText = MG.MakeText(_vizRt, "", 16, new Vector2(0f, 1f), new Vector2(110f, -24f), new Vector2(160f, 24f)); _lightText.alignment = TextAlignmentOptions.Left;
            var vlabel = MG.MakeText(_vizRt, "SELF-DRIVING VIEW", 13, new Vector2(1f, 1f), new Vector2(-80f, -16f), new Vector2(160f, 20f)); vlabel.color = new Color(0.5f, 0.55f, 0.6f);

            // ---- map (right)
            var mapGo = new GameObject("Map", typeof(RectTransform), typeof(RawImage), typeof(RectMask2D));
            mapGo.transform.SetParent(_panelRt, false);
            _mapRt = (RectTransform)mapGo.transform;
            _mapRt.anchorMin = new Vector2(0f, 0f); _mapRt.anchorMax = new Vector2(1f, 1f);
            _mapRt.offsetMin = new Vector2(524f, 12f); _mapRt.offsetMax = new Vector2(-12f, -70f);
            _mapImg = mapGo.GetComponent<RawImage>();
            _mapImg.color = Color.white;
            mapGo.AddComponent<MapPointer>().Screen = this;
            _overlay = new GameObject("Overlay", typeof(RectTransform)).GetComponent<RectTransform>();
            _overlay.SetParent(_mapRt, false); MG.Stretch(_overlay);
            _pin = Img(_overlay, "Pin", new Color(0.95f, 0.2f, 0.25f), new Vector2(16f, 16f)); _pin.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            _me = Img(_overlay, "You", new Color(0.2f, 0.6f, 1f), new Vector2(16f, 16f));
            _meHead = Img(_me.rectTransform, "Heading", Color.white, new Vector2(4f, 14f)); _meHead.rectTransform.anchoredPosition = new Vector2(0f, 10f);

            _search = MG.MakeInput(_mapRt, "Search places or an address (e.g. 214 Maple Avenue)", new Vector2(0f, 1f), new Vector2(250f, -28f), new Vector2(480f, 42f), 60, false);
            _search.pointSize = 18;
            _search.onSubmit.AddListener(_ => RunSearch());
            _search.onValueChanged.AddListener(_ => _searchAt = Time.unscaledTime + 0.45f);
            _results = new GameObject("Results", typeof(RectTransform), typeof(Image));
            _results.transform.SetParent(_mapRt, false);
            var rr = (RectTransform)_results.transform; rr.anchorMin = rr.anchorMax = rr.pivot = new Vector2(0f, 1f);
            rr.anchoredPosition = new Vector2(10f, -52f); rr.sizeDelta = new Vector2(480f, 6 * 46f + 6f);
            _results.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 0.96f);
            for (int i = 0; i < 6; i++)
            {
                int id = i;
                var b = MG.MakeButton(_results.transform, "", new Vector2(0.5f, 1f), new Vector2(0f, -26f - i * 46f), new Vector2(470f, 42f), new Color(0.14f, 0.16f, 0.2f), () => PickResult(id), 17);
                b.GetComponentInChildren<TMP_Text>().alignment = TextAlignmentOptions.Left;
                b.GetComponentInChildren<TMP_Text>().margin = new Vector4(12f, 0f, 8f, 0f);
                _resultBtns.Add(b);
            }
            _results.SetActive(false);

            var zin = MG.MakeButton(_mapRt, "+", new Vector2(1f, 0f), new Vector2(-30f, 116f), new Vector2(44f, 44f), new Color(0.15f, 0.17f, 0.22f, 0.95f), () => Zoom(0.5), 30);
            var zout = MG.MakeButton(_mapRt, "-", new Vector2(1f, 0f), new Vector2(-30f, 66f), new Vector2(44f, 44f), new Color(0.15f, 0.17f, 0.22f, 0.95f), () => Zoom(2.0), 30);
            MG.MakeButton(_mapRt, "CENTER", new Vector2(1f, 0f), new Vector2(-50f, 20f), new Vector2(90f, 32f), new Color(0.15f, 0.17f, 0.22f, 0.95f), () => { Follow = true; _viewDirty = true; }, 14);
            _scaleText = MG.MakeText(_mapRt, "", 14, new Vector2(1f, 1f), new Vector2(-90f, -18f), new Vector2(170f, 22f)); _scaleText.alignment = TextAlignmentOptions.Right;

            // destination card
            _card = new GameObject("Card", typeof(RectTransform), typeof(Image));
            _card.transform.SetParent(_mapRt, false);
            var cr = (RectTransform)_card.transform; cr.anchorMin = cr.anchorMax = cr.pivot = new Vector2(0f, 0f);
            cr.anchoredPosition = new Vector2(10f, 10f); cr.sizeDelta = new Vector2(470f, 104f);
            _card.GetComponent<Image>().color = new Color(0.08f, 0.09f, 0.12f, 0.96f);
            _cardTitle = MG.MakeText(_card.transform, "", 22, new Vector2(0f, 1f), new Vector2(160f, -22f), new Vector2(300f, 30f)); _cardTitle.alignment = TextAlignmentOptions.Left; _cardTitle.fontStyle = FontStyles.Bold;
            _cardDetail = MG.MakeText(_card.transform, "", 15, new Vector2(0f, 1f), new Vector2(160f, -52f), new Vector2(300f, 40f)); _cardDetail.alignment = TextAlignmentOptions.TopLeft; _cardDetail.color = new Color(0.7f, 0.75f, 0.8f);
            var go = MG.MakeButton(_card.transform, "NAVIGATE", new Vector2(1f, 0.5f), new Vector2(-78f, 14f), new Vector2(140f, 44f), new Color(0.18f, 0.45f, 0.9f), CardGo, 20);
            _cardGo = go.GetComponentInChildren<TMP_Text>();
            MG.MakeButton(_card.transform, "CLOSE", new Vector2(1f, 0.5f), new Vector2(-78f, -30f), new Vector2(140f, 32f), new Color(0.25f, 0.25f, 0.3f), CardClose, 14);
            _card.SetActive(false);

            _panel.SetActive(false);
        }

        private void Layout()
        {
            if (_big) { _panelRt.anchorMin = _panelRt.anchorMax = _panelRt.pivot = new Vector2(0.5f, 0.5f); _panelRt.anchoredPosition = new Vector2(0f, -20f); _panelRt.sizeDelta = new Vector2(1820f, 960f); }
            else { _panelRt.anchorMin = _panelRt.anchorMax = _panelRt.pivot = new Vector2(0.5f, 0f); _panelRt.anchoredPosition = new Vector2(0f, 10f); _panelRt.sizeDelta = new Vector2(1560f, 400f); }
            _viewDirty = true;
        }

        public void ToggleBig() { _big = !_big; Layout(); }

        public void Show(bool on)
        {
            Build();
            _shown = on;
            _panel.SetActive(on);
            if (on) { Follow = true; _viewDirty = true; }
            else { _search.text = ""; _results.SetActive(false); _card.SetActive(false); if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); }
        }

        // ================================================================== update

        private void Update()
        {
            var cars = MegaCars.I;
            if (!_shown || cars == null || cars.Mine == null) { if (_shown && (cars == null || cars.Mine == null)) Show(false); return; }
            var kb = Keyboard.current;
            if (kb != null && !Typing && kb.mKey.wasPressedThisFrame) ToggleBig();
            var car = cars.Mine;

            // top bar
            _speed.text = Mathf.RoundToInt(Mathf.Abs(car.Speed) * 3.6f).ToString();
            _gear.text = cars.AutoParking ? "AP" : (car.Speed < -0.3f ? "R" : (Mathf.Abs(car.Speed) < 0.3f ? "P" : "D"));
            int sig = cars.ShownSignal;
            bool blink = (Time.time * 1.6f) % 1f < 0.5f;
            _signalL.enabled = (sig == -1 || sig == 3) && blink;
            _signalR.enabled = (sig == 1 || sig == 3) && blink;
            _status.text = MegaCars.ModelName(car.Model) + (cars.Status != "" ? "  ·  " + cars.Status : (cars.SelfDriving ? "  ·  Self-driving" : ""));
            _fsdBtn.color = cars.SelfDriving ? new Color(0.15f, 0.7f, 0.95f) : new Color(0.18f, 0.38f, 0.75f);
            if (cars.HasDest)
            {
                double eta = cars.RouteMeters / 12.0;
                _nav.text = "<color=#7FC4FF>To " + cars.DestName + "</color>  ·  " + MegaCars.FormatDist(cars.RouteMeters) + (cars.RouteTruncated ? "+" : "") + "  ·  " + MegaCars.FormatTime(eta) + (cars.Navigating ? "" : "  <color=#FFA020>(paused)</color>");
            }
            else _nav.text = "Pick a place on the map or search to navigate";

            if (Time.unscaledTime >= _vizAt) { _vizAt = Time.unscaledTime + 0.08f; DrawViz(car); }
            UpdateMap(car);
            if (_searchAt > 0f && Time.unscaledTime >= _searchAt) { _searchAt = 0f; RunSearch(); }
        }

        // ================================================================== self-driving view

        private Image Pooled(List<Image> pool, int i, Transform parent, Color c)
        {
            if (i >= pool.Count) pool.Add(Img(parent, "e", c, Vector2.one));
            var im = pool[i];
            im.gameObject.SetActive(true);
            im.color = c;
            return im;
        }

        private static void HideFrom(List<Image> pool, int n) { for (int i = n; i < pool.Count; i++) pool[i].gameObject.SetActive(false); }

        private void DrawViz(MegaCars.Car car)
        {
            Vector3 me = car.T.position;
            float s = VizScale;
            _vizWorld.localRotation = Quaternion.Euler(0f, 0f, car.VisYaw);
            int n = 0;
            // roads (and lane lines) within about 90 m
            int kx0 = MegaWorld.StreetIdx(me.x - 120f), kx1 = MegaWorld.StreetIdx(me.x + 120f);
            int kz0 = MegaWorld.StreetIdx(me.z - 120f), kz1 = MegaWorld.StreetIdx(me.z + 120f);
            Color road = new Color(0.2f, 0.21f, 0.25f), lane = new Color(0.55f, 0.57f, 0.62f, 0.5f), mid = new Color(0.85f, 0.72f, 0.25f, 0.8f);
            for (int k = kz0; k <= kz1; k++)
            {
                float y = (MegaWorld.Street(k) - me.z) * s;
                var r = Pooled(_vizRoads, n++, _vizWorld, road); r.rectTransform.anchoredPosition = new Vector2(0f, y); r.rectTransform.sizeDelta = new Vector2(300f * s, 16f * s); r.rectTransform.localRotation = Quaternion.identity;
                var m = Pooled(_vizRoads, n++, _vizWorld, mid); m.rectTransform.anchoredPosition = new Vector2(0f, y); m.rectTransform.sizeDelta = new Vector2(300f * s, 2f); m.rectTransform.localRotation = Quaternion.identity;
                foreach (float off in new[] { -3.35f, 3.35f })
                { var l = Pooled(_vizRoads, n++, _vizWorld, lane); l.rectTransform.anchoredPosition = new Vector2(0f, y + off * s); l.rectTransform.sizeDelta = new Vector2(300f * s, 1.5f); l.rectTransform.localRotation = Quaternion.identity; }
            }
            for (int k = kx0; k <= kx1; k++)
            {
                float x = (MegaWorld.Street(k) - me.x) * s;
                var r = Pooled(_vizRoads, n++, _vizWorld, road); r.rectTransform.anchoredPosition = new Vector2(x, 0f); r.rectTransform.sizeDelta = new Vector2(16f * s, 300f * s); r.rectTransform.localRotation = Quaternion.identity;
                var m = Pooled(_vizRoads, n++, _vizWorld, mid); m.rectTransform.anchoredPosition = new Vector2(x, 0f); m.rectTransform.sizeDelta = new Vector2(2f, 300f * s); m.rectTransform.localRotation = Quaternion.identity;
                foreach (float off in new[] { -3.35f, 3.35f })
                { var l = Pooled(_vizRoads, n++, _vizWorld, lane); l.rectTransform.anchoredPosition = new Vector2(x + off * s, 0f); l.rectTransform.sizeDelta = new Vector2(1.5f, 300f * s); l.rectTransform.localRotation = Quaternion.identity; }
            }
            HideFrom(_vizRoads, n);

            // route
            n = 0;
            var cars = MegaCars.I;
            if (cars.HasDest)
            {
                for (int i = 0; i + 1 < cars.Route.Count && n < 40; i++)
                {
                    Vector3 a = cars.Route[i], b = cars.Route[i + 1];
                    if ((a - me).sqrMagnitude > 250f * 250f && (b - me).sqrMagnitude > 250f * 250f) continue;
                    Vector3 mid2 = (a + b) * 0.5f, d = b - a;
                    var seg = Pooled(_vizRoute, n++, _vizWorld, new Color(0.2f, 0.55f, 1f, 0.7f));
                    seg.rectTransform.anchoredPosition = new Vector2((mid2.x - me.x) * s, (mid2.z - me.z) * s);
                    seg.rectTransform.sizeDelta = new Vector2(2.4f * s, new Vector2(d.x, d.z).magnitude * s);
                    seg.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
                }
            }
            HideFrom(_vizRoute, n);

            // other cars
            n = 0;
            foreach (var o in cars.Cars)
            {
                if (o == car || o.T == null) continue;
                Vector3 d = o.T.position - me;
                if (d.sqrMagnitude > 90f * 90f) continue;
                var im = Pooled(_vizCars, n++, _vizWorld, o.Traffic ? new Color(0.6f, 0.62f, 0.68f) : new Color(0.38f, 0.4f, 0.45f));
                im.rectTransform.anchoredPosition = new Vector2(d.x * s, d.z * s);
                im.rectTransform.sizeDelta = new Vector2(2f * s, 4.6f * s);
                im.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -o.VisYaw);
            }
            HideFrom(_vizCars, n);

            // people
            n = 0;
            var g = MegaGame.I;
            if (g != null)
                foreach (var a in g.Agents)
                {
                    if (a.IsPlayer || a.Out) continue;
                    Vector3 d = a.Pos - me;
                    if (d.sqrMagnitude > 70f * 70f) continue;
                    var im = Pooled(_vizPeople, n++, _vizWorld, a.It ? new Color(1f, 0.3f, 0.3f) : new Color(0.35f, 0.8f, 1f));
                    im.rectTransform.anchoredPosition = new Vector2(d.x * s, d.z * s);
                    im.rectTransform.sizeDelta = new Vector2(1.3f * s, 1.3f * s);
                    if (n > 150) break;
                }
            HideFrom(_vizPeople, n);

            // the next traffic light
            if (car.Lanes || cars.SelfDriving)
            {
                int l = MegaCars.LightAhead(car, out float dist);
                bool show = dist > -2f && dist < 120f && MegaCars.RoadOK(car.T.position + car.T.forward * 30f);
                _lightDot.enabled = show; _lightText.enabled = show;
                if (show) { _lightDot.color = l == 2 ? new Color(0.2f, 1f, 0.4f) : (l == 1 ? new Color(1f, 0.75f, 0.1f) : new Color(1f, 0.15f, 0.12f)); _lightText.text = (l == 2 ? "Green" : l == 1 ? "Yellow" : "Red") + "  " + Mathf.Max(0, Mathf.RoundToInt(dist)) + " m"; }
            }
            else { _lightDot.enabled = false; _lightText.enabled = false; }
        }

        // ================================================================== map

        public void Pan(Vector2 screenDelta)
        {
            float k = _mapRt.rect.width / _tw;   // UI units per texture pixel
            Vector2 d = screenDelta / Mathf.Max(0.01f, _canvas.GetComponent<Canvas>().scaleFactor) / Mathf.Max(0.01f, k);
            _cx -= d.x * _mpp; _cz -= d.y * _mpp;
            _viewDirty = true;
        }

        public void Zoom(double factor)
        {
            _mpp = System.Math.Max(0.6, System.Math.Min(4.0e6, _mpp * factor));
            _viewDirty = true;
        }

        private void ViewFromCar(MegaCars.Car car)
        {
            double wx = MegaWorld.WX(car.T.position.x), wz = MegaWorld.WZ(car.T.position.z);
            if (_viewDirty || System.Math.Abs(wx - _rcx) > _tw * _rmpp * 0.18 || System.Math.Abs(wz - _rcz) > _th * _rmpp * 0.18 || System.Math.Abs(_mpp - _rmpp) > 1e-9)
            { _cx = wx; _cz = wz; _viewDirty = true; }
        }

        private void UpdateMap(MegaCars.Car car)
        {
            if (Follow) ViewFromCar(car);
            // texture size follows the map's shape
            Rect r = _mapRt.rect;
            int tw = _big ? 520 : 400, th = Mathf.Clamp(Mathf.RoundToInt(tw * r.height / Mathf.Max(1f, r.width)), 40, 400);
            if (_tex == null || _tex.width != tw || _tex.height != th)
            {
                _tw = tw; _th = th;
                _tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
                _px = new Color32[tw * th];
                _mapImg.texture = _tex;
                _viewDirty = true;
            }
            if (_viewDirty)
            {
                _viewDirty = false;
                _rcx = _cx; _rcz = _cz; _rmpp = _mpp; _row = 0;
                _labelsAt = 0f;
            }
            // draw a few rows each frame
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (_row < _th && sw.ElapsedMilliseconds < 3)
            {
                double wz = _rcz + (_row - _th * 0.5 + 0.5) * _rmpp;
                for (int x = 0; x < _tw; x++)
                {
                    double wx = _rcx + (x - _tw * 0.5 + 0.5) * _rmpp;
                    _px[_row * _tw + x] = MapColor(wx, wz, _rmpp);
                }
                _row++;
                if (_row == _th || _row % 12 == 0) { _tex.SetPixels32(_px); _tex.Apply(false); }
            }
            float scaleM = (float)(_rmpp * _tw * 0.25);
            _scaleText.text = "1 square = " + MegaCars.FormatDist(scaleM);

            // markers: you, destination, route, labels
            PlaceMarker(_me.rectTransform, MegaWorld.WX(car.T.position.x), MegaWorld.WZ(car.T.position.z));
            _me.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -car.VisYaw);
            var cars = MegaCars.I;
            bool hasPin = cars.HasDest || _card.activeSelf;
            _pin.enabled = hasPin;
            if (hasPin)
            {
                var p = cars.HasDest && !_card.activeSelf ? cars.Dest : _cardPlace;
                var c = MegaWorld.Countries[p.Country];
                PlaceMarker(_pin.rectTransform, c.CX + p.LX, c.CZ + p.LZ);
            }
            int n = 0;
            if (cars.HasDest && cars.Route.Count > 1)
            {
                for (int i = 0; i + 1 < cars.Route.Count && n < 400; i++)
                {
                    Vector3 a = cars.Route[i], b = cars.Route[i + 1];
                    float segPx = (float)((b - a).magnitude / _rmpp);
                    int steps = Mathf.Clamp(Mathf.CeilToInt(segPx / 7f), 1, 60);
                    for (int t = 0; t < steps && n < 400; t++)
                    {
                        Vector3 p = Vector3.Lerp(a, b, t / (float)steps);
                        var dot = Pooled(_routeDots, n++, _overlay, new Color(0.25f, 0.6f, 1f));
                        dot.rectTransform.sizeDelta = new Vector2(5f, 5f);
                        PlaceMarker(dot.rectTransform, MegaWorld.WX(p.x), MegaWorld.WZ(p.z));
                    }
                }
            }
            HideFrom(_routeDots, n);
            _me.transform.SetAsLastSibling(); _pin.transform.SetAsLastSibling();
            if (Time.unscaledTime >= _labelsAt) { _labelsAt = Time.unscaledTime + 1f; UpdateLabels(); }
        }

        /// <summary>Puts a marker at a world position on the map (hidden when off the map).</summary>
        private void PlaceMarker(RectTransform rt, double wx, double wz)
        {
            Rect r = _mapRt.rect;
            double px = (wx - _rcx) / _rmpp, pz = (wz - _rcz) / _rmpp;
            float k = r.width / _tw;
            float x = (float)px * k, y = (float)pz * k;
            bool on = Mathf.Abs(x) < r.width * 0.5f && Mathf.Abs(y) < r.height * 0.5f;
            rt.gameObject.SetActive(on);
            if (!on) return;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
        }

        private static readonly Color32 Sea = new Color32(14, 38, 64, 255), CityC = new Color32(58, 62, 72, 255), TownC = new Color32(74, 79, 70, 255),
            ParkC = new Color32(46, 104, 58, 255), ForestC = new Color32(32, 78, 44, 255), FarmC = new Color32(104, 104, 58, 255),
            LakeC = new Color32(28, 78, 122, 255), AirC = new Color32(88, 88, 94, 255), RoadC = new Color32(120, 124, 134, 255), WallC = new Color32(150, 150, 150, 255),
            BeachC = new Color32(200, 184, 140, 255);

        private static Color32 MapColor(double wx, double wz, double mpp)
        {
            int k = MegaWorld.CountryAt(wx, wz);
            if (k < 0) return Sea;
            var c = MegaWorld.Countries[k];
            double lx = wx - c.CX, lz = wz - c.CZ;
            if (c.Half - System.Math.Abs(lx) < mpp * 1.2 || c.Half - System.Math.Abs(lz) < mpp * 1.2) return WallC;
            int ci = (int)System.Math.Floor((lx + c.Half) / MegaWorld.ChunkSize), cj = (int)System.Math.Floor((lz + c.Half) / MegaWorld.ChunkSize);
            var kind = MapManager.KindOf(c, ci, cj);
            double inX = lx + c.Half - (double)ci * MegaWorld.ChunkSize, inZ = lz + c.Half - (double)cj * MegaWorld.ChunkSize;
            if (kind == MapManager.ChunkKind.Lake)
            {
                if (mpp < 25 && (inX < 30 || inX > 470 || inZ < 30 || inZ > 470)) return BeachC;
                return LakeC;
            }
            bool roads = kind == MapManager.ChunkKind.City || kind == MapManager.ChunkKind.Town || kind == MapManager.ChunkKind.Airport;
            if (roads && mpp < 9)
            {
                double mx = inX % 100.0, mz = inZ % 100.0;
                double w = System.Math.Max(8.0, mpp * 0.8);
                if (mx < w || mx > 100 - w || mz < w || mz > 100 - w) return RoadC;
            }
            switch (kind)
            {
                case MapManager.ChunkKind.Park: return ParkC;
                case MapManager.ChunkKind.Forest: return ForestC;
                case MapManager.ChunkKind.Farm: return FarmC;
                case MapManager.ChunkKind.Airport: return AirC;
                case MapManager.ChunkKind.Town: return TownC;
                default:
                    {
                        float d = MapManager.Density(c, lx, lz);
                        byte v = (byte)Mathf.Lerp(78, 46, d);
                        return new Color32(v, (byte)(v + 3), (byte)(v + 12), 255);
                    }
            }
        }

        private void UpdateLabels()
        {
            var tmp = new List<MegaPlaces.Place>();
            double halfW = _tw * 0.5 * _rmpp, halfH = _th * 0.5 * _rmpp;
            int k = MegaWorld.CountryAt(_rcx, _rcz);
            if (_rmpp > 400)
            {
                for (int i = 0; i < MegaWorld.Countries.Length; i++)
                {
                    var cc = MegaWorld.Countries[i];
                    tmp.Add(new MegaPlaces.Place { Name = cc.Name, Kind = MegaPlaces.Kind.Country, Country = i, LX = 0, LZ = 0 });
                }
            }
            if (k >= 0)
            {
                var c = MegaWorld.Countries[k];
                double lx = _rcx - c.CX, lz = _rcz - c.CZ;
                if (_rmpp > 25 && _rmpp < 20000) MegaPlaces.CitiesNear(k, lx, lz, System.Math.Max(halfW, halfH), tmp, 60);
                if (_rmpp <= 25) MegaPlaces.PlacesNear(k, lx, lz, System.Math.Max(halfW, halfH), tmp, 60);
            }
            int n = 0;
            Rect r = _mapRt.rect;
            float kk = r.width / _tw;
            foreach (var p in tmp)
            {
                if (n >= 40) break;
                var c = MegaWorld.Countries[p.Country];
                double wx = c.CX + p.LX, wz = c.CZ + p.LZ;
                float x = (float)((wx - _rcx) / _rmpp) * kk, y = (float)((wz - _rcz) / _rmpp) * kk;
                if (Mathf.Abs(x) > r.width * 0.5f - 20f || Mathf.Abs(y) > r.height * 0.5f - 10f) continue;
                if (n >= _labels.Count)
                {
                    var t = MG.MakeText(_overlay, "", 14, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(220f, 22f));
                    t.raycastTarget = false;
                    _labels.Add(t);
                }
                var lbl = _labels[n++];
                lbl.gameObject.SetActive(true);
                lbl.rectTransform.anchoredPosition = new Vector2(x, y);
                lbl.text = p.Name;
                lbl.fontSize = p.Kind == MegaPlaces.Kind.Country ? 20 : (p.Kind == MegaPlaces.Kind.City ? 16 : 13);
                lbl.color = p.Kind == MegaPlaces.Kind.City || p.Kind == MegaPlaces.Kind.Country ? Color.white : new Color(0.8f, 0.85f, 0.9f);
            }
            for (int i = n; i < _labels.Count; i++) _labels[i].gameObject.SetActive(false);
        }

        public void ClickMap(Vector2 screenPos, Camera cam)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(_mapRt, screenPos, cam, out var local)) return;
            Rect r = _mapRt.rect;
            float k = r.width / _tw;
            double wx = _rcx + (local.x - r.center.x) / k * _rmpp, wz = _rcz + (local.y - r.center.y) / k * _rmpp;
            int country = MegaWorld.CountryAt(wx, wz);
            if (country < 0) { MegaCars.I?.Status.ToString(); return; }
            var c = MegaWorld.Countries[country];
            var p = MegaPlaces.At(country, wx - c.CX, wz - c.CZ, 14.0 / k * _rmpp);
            ShowCard(p);
        }

        // ================================================================== search and the destination card

        private void RunSearch()
        {
            var cars = MegaCars.I;
            if (cars == null || cars.Mine == null) return;
            string q = _search.text;
            if (q == _lastQuery && _results.activeSelf) return;
            _lastQuery = q;
            if (string.IsNullOrWhiteSpace(q)) { _results.SetActive(false); return; }
            var found = MegaPlaces.Search(q, cars.Mine.T.position, 6);
            _resultPlaces.Clear(); _resultPlaces.AddRange(found);
            for (int i = 0; i < _resultBtns.Count; i++)
            {
                bool has = i < found.Count;
                _resultBtns[i].gameObject.SetActive(has);
                if (has) _resultBtns[i].GetComponentInChildren<TMP_Text>().text = found[i].Name + "  <size=13><color=#9AA4B2>" + found[i].Detail + "</color></size>";
            }
            _results.SetActive(true);
            if (found.Count == 0)
            {
                _resultBtns[0].gameObject.SetActive(true);
                _resultBtns[0].GetComponentInChildren<TMP_Text>().text = "<color=#9AA4B2>Nothing found nearby</color>";
                _resultPlaces.Add(default);
            }
        }

        private void PickResult(int i)
        {
            if (i >= _resultPlaces.Count || _resultPlaces[i].Name == null) return;
            _results.SetActive(false);
            var p = _resultPlaces[i];
            ShowCard(p);
            var c = MegaWorld.Countries[p.Country];
            _cx = c.CX + p.LX; _cz = c.CZ + p.LZ; Follow = false;
            if (_mpp > 20) _mpp = 6;
            _viewDirty = true;
        }

        private void ShowCard(MegaPlaces.Place p)
        {
            _cardPlace = p;
            _card.SetActive(true);
            _cardTitle.text = p.Name;
            var cars = MegaCars.I;
            string extra = "";
            if (cars != null && cars.Mine != null)
            {
                int here = MegaWorld.CountryAtLocal(cars.Mine.T.position);
                if (p.Country != here) extra = "\nIn " + MegaWorld.Countries[p.Country].Name + ": fly there first";
                else
                {
                    Vector3 d = p.Local - cars.Mine.T.position;
                    double m = Mathf.Abs(d.x) + Mathf.Abs(d.z);
                    extra = "\nAbout " + MegaCars.FormatDist(m) + " by road  ·  " + MegaCars.FormatTime(m / 12.0);
                }
            }
            _cardDetail.text = p.Detail + extra;
            _cardGo.text = "NAVIGATE";
        }

        private void CardGo()
        {
            if (MegaCars.I == null || _cardPlace.Name == null) return;
            MegaCars.I.NavigateTo(_cardPlace);
            _card.SetActive(false);
            Follow = true; _viewDirty = true;
        }

        private void CardClose() { _card.SetActive(false); }
    }
}
