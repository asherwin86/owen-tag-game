using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TMPro;

namespace TagGame.Gameplay
{
    /// <summary>
    /// On-screen touch controls: a virtual joystick (bottom-left), a sprint button
    /// (bottom-right) and a pause button (top-right). Created automatically at
    /// runtime on touch devices only, so desktop play is unchanged and no scene
    /// edits are needed. PlayerController reads MoveInput / SprintHeld.
    /// </summary>
    public class MobileControls : MonoBehaviour
    {
        public static Vector2 MoveInput { get; private set; }
        public static bool SprintHeld { get; private set; }

        /// <summary>Set to true to preview the controls with a mouse (e.g. in the editor).</summary>
        public static bool ForceShow = false;

        private static Sprite _circleSprite;
        private TagGameManager _manager;
        private GameObject _pauseButton;
        private GameObject _controlsRoot;
        private TextMeshProUGUI _toggleLabel;
        private Image _toggleImage;
        private const string PrefKey = "TagGame.MobileControlsEnabled";
        private bool _controlsEnabled = true;
        private GameObject _canvasGo;
        private bool _deviceWantsControls; // auto-detected: touch in use vs keyboard in use

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            bool touch = Application.isMobilePlatform || Touchscreen.current != null;
            if (!touch && !ForceShow) return;
            if (FindFirstObjectByType<MobileControls>() != null) return;

            var go = new GameObject("MobileControls");
            go.AddComponent<MobileControls>();
        }

        private void Awake()
        {
            _manager = FindFirstObjectByType<TagGameManager>();
            _controlsEnabled = PlayerPrefs.GetInt(PrefKey, 1) == 1;
            // Phones/tablets start with controls on; desktops (even touch laptops) start off
            // until the screen is actually touched.
            _deviceWantsControls = Application.isMobilePlatform || ForceShow;
            BuildUI();
            ApplyEnabled();
            _canvasGo.SetActive(_deviceWantsControls);
        }

        /// <summary>Turns the joystick and sprint button on or off (remembered between visits).</summary>
        public void SetControlsEnabled(bool enabled)
        {
            _controlsEnabled = enabled;
            PlayerPrefs.SetInt(PrefKey, enabled ? 1 : 0);
            PlayerPrefs.Save();
            ApplyEnabled();
        }

        private void ApplyEnabled()
        {
            if (!_controlsEnabled)
            {
                MoveInput = Vector2.zero;
                SprintHeld = false;
            }
            if (_controlsRoot != null) _controlsRoot.SetActive(_controlsEnabled);
            if (_toggleLabel != null) _toggleLabel.text = _controlsEnabled ? "CTRL\nON" : "CTRL\nOFF";
            if (_toggleImage != null) _toggleImage.color = new Color(1f, 1f, 1f, _controlsEnabled ? 0.3f : 0.15f);
        }

        private void OnDisable()
        {
            MoveInput = Vector2.zero;
            SprintHeld = false;
        }

        private void Update()
        {
            DetectInputDevice();

            // Pause button only makes sense while a round is running.
            if (_pauseButton != null && _manager != null)
            {
                bool show = _manager.RoundInProgress;
                if (_pauseButton.activeSelf != show) _pauseButton.SetActive(show);
            }
        }

        /// <summary>Touch -> show the controls. Keyboard -> hide them. Switches live, so a
        /// touch laptop or a tablet with a keyboard always shows the right thing.</summary>
        private void DetectInputDevice()
        {
            bool want = _deviceWantsControls;

            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame) want = true;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.anyKey.wasPressedThisFrame && !ForceShow) want = false;

            if (want == _deviceWantsControls) return;
            _deviceWantsControls = want;
            _canvasGo.SetActive(want);
            if (!want)
            {
                MoveInput = Vector2.zero;
                SprintHeld = false;
            }
        }

        private void BuildUI()
        {
            var canvasGo = new GameObject("MobileCanvas");
            canvasGo.transform.SetParent(transform, false);
            _canvasGo = canvasGo;
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            // Joystick + sprint live under one root so they can be switched off together.
            var rootGo = new GameObject("ControlsRoot", typeof(RectTransform));
            rootGo.transform.SetParent(canvasGo.transform, false);
            var rootRt = (RectTransform)rootGo.transform;
            rootRt.anchorMin = Vector2.zero;
            rootRt.anchorMax = Vector2.one;
            rootRt.offsetMin = rootRt.offsetMax = Vector2.zero;
            _controlsRoot = rootGo;

            // Joystick
            var joyBase = MakeCircle(rootGo.transform, "JoystickBase", new Vector2(0f, 0f),
                new Vector2(210f, 190f), 260f, new Color(1f, 1f, 1f, 0.22f));
            var knob = MakeCircle(joyBase.transform, "JoystickKnob", new Vector2(0.5f, 0.5f),
                Vector2.zero, 110f, new Color(1f, 1f, 1f, 0.6f));
            knob.raycastTarget = false;
            var joystick = joyBase.gameObject.AddComponent<Joystick>();
            joystick.Init(joyBase.rectTransform, knob.rectTransform);

            // Sprint
            var sprint = MakeCircle(rootGo.transform, "SprintButton", new Vector2(1f, 0f),
                new Vector2(-170f, 190f), 160f, new Color(1f, 1f, 1f, 0.3f));
            sprint.gameObject.AddComponent<HoldButton>().OnHeld = held => SprintHeld = held;
            AddLabel(sprint.transform, "RUN", 40f);

            // On/off toggle (top-left)
            var toggle = MakeCircle(canvasGo.transform, "ControlsToggle", new Vector2(0f, 1f),
                new Vector2(70f, -70f), 90f, new Color(1f, 1f, 1f, 0.3f));
            toggle.gameObject.AddComponent<TapButton>().OnTap = () => SetControlsEnabled(!_controlsEnabled);
            _toggleImage = toggle;
            _toggleLabel = AddLabel(toggle.transform, "CTRL\nON", 22f);

            // Pause
            var pause = MakeCircle(canvasGo.transform, "PauseButton", new Vector2(1f, 1f),
                new Vector2(-70f, -70f), 90f, new Color(1f, 1f, 1f, 0.3f));
            pause.gameObject.AddComponent<TapButton>().OnTap = () =>
            {
                if (_manager != null) _manager.TogglePause();
            };
            AddLabel(pause.transform, "II", 36f);
            _pauseButton = pause.gameObject;
            _pauseButton.SetActive(false);
        }

        private static Sprite GetCircleSprite()
        {
            if (_circleSprite != null) return _circleSprite;
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            float r = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                    float a = Mathf.Clamp01(r - d); // 1px soft edge
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            _circleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _circleSprite;
        }

        private static Image MakeCircle(Transform parent, string name, Vector2 anchor, Vector2 pos, float size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = new Vector2(size, size);
            var img = go.GetComponent<Image>();
            img.sprite = GetCircleSprite();
            img.color = color;
            return img;
        }

        private static TextMeshProUGUI AddLabel(Transform parent, string text, float fontSize)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            tmp.raycastTarget = false;
            return tmp;
        }

        // ---- input widgets -------------------------------------------------

        private class Joystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
        {
            private RectTransform _base;
            private RectTransform _knob;

            public void Init(RectTransform joyBase, RectTransform knob)
            {
                _base = joyBase;
                _knob = knob;
            }

            public void OnPointerDown(PointerEventData e) => OnDrag(e);

            public void OnDrag(PointerEventData e)
            {
                RectTransformUtility.ScreenPointToLocalPointInRectangle(_base, e.position, e.pressEventCamera, out var local);
                float radius = _base.rect.width * 0.5f;
                Vector2 v = Vector2.ClampMagnitude(local / radius, 1f);
                _knob.anchoredPosition = v * radius;
                MoveInput = v.magnitude < 0.12f ? Vector2.zero : v;
            }

            public void OnPointerUp(PointerEventData e)
            {
                _knob.anchoredPosition = Vector2.zero;
                MoveInput = Vector2.zero;
            }
        }

        private class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            public System.Action<bool> OnHeld;
            public void OnPointerDown(PointerEventData e) => OnHeld?.Invoke(true);
            public void OnPointerUp(PointerEventData e) => OnHeld?.Invoke(false);
        }

        private class TapButton : MonoBehaviour, IPointerClickHandler
        {
            public System.Action OnTap;
            public void OnPointerClick(PointerEventData e) => OnTap?.Invoke();
        }
    }
}
