using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TagGame.Gameplay;
using TagGame.Juice;
using TagGame.UI;
using MG = TagGame.Multiplayer.MultiplayerGame;

namespace TagGame.Mega
{
    /// <summary>
    /// Dances (Mega Mode update 2). Press G (controller: D-pad up, touch: DANCE) to open the dance wheel. Pick a dance,
    /// or press SPIN and the wheel picks one for you. Moving stops the dance. Careful: dancing makes you easy to tag!
    /// Bots dance too when nobody is chasing them.
    /// </summary>
    public class MegaDance : MonoBehaviour
    {
        public static readonly string[] Names = { "Spin", "Hop", "Wiggle", "Flip", "Robot", "Sway" };
        private static readonly Color[] Colours =
        {
            new Color(0.95f, 0.35f, 0.35f), new Color(0.98f, 0.7f, 0.2f), new Color(0.4f, 0.85f, 0.35f),
            new Color(0.2f, 0.75f, 0.95f), new Color(0.45f, 0.45f, 0.95f), new Color(0.9f, 0.45f, 0.85f),
        };

        /// <summary>How a dancer is turned and lifted at time t (shared by you and the bots).</summary>
        public static void Pose(int type, float t, out Quaternion extra, out float lift)
        {
            extra = Quaternion.identity; lift = 0f;
            switch (type)
            {
                case 0: extra = Quaternion.Euler(0f, t * 540f % 360f, 0f); break;                                          // spin
                case 1: lift = Mathf.Abs(Mathf.Sin(t * 6f)) * 0.7f; break;                                                  // hop
                case 2: extra = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 10f) * 20f); break;                                  // wiggle
                case 3: { float k = (t % 1.6f) / 1.6f; extra = Quaternion.Euler(k * 360f, 0f, 0f); lift = Mathf.Sin(k * Mathf.PI) * 1.4f; break; } // flip
                case 4: extra = Quaternion.Euler(0f, Mathf.Floor(t * 2f) % 4f * 90f, 0f); lift = (Mathf.Floor(t * 4f) % 2f) * 0.12f; break; // robot
                default: extra = Quaternion.Euler(Mathf.Sin(t * 4f) * 15f, Mathf.Sin(t * 2f) * 30f, 0f); break;            // sway
            }
        }

        private PlayerController _player;
        private GameObject _canvas, _wheel, _touchBtn;
        private RectTransform _pointer;
        private bool _spinning;
        private float _spinT, _spinSpeed, _spinAngle;
        private bool _dancing;
        private Vector3 _basePos; private float _baseYaw, _startT;

        private void Awake() { _player = FindFirstObjectByType<PlayerController>(); }

        private void BuildUi()
        {
            if (_canvas != null) return;
            _canvas = new GameObject("DanceCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var cv = _canvas.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 44;
            var sc = _canvas.GetComponent<CanvasScaler>(); sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; sc.referenceResolution = new Vector2(1920f, 1080f); sc.matchWidthOrHeight = 0.5f;
            _wheel = new GameObject("DanceWheel", typeof(RectTransform));
            _wheel.transform.SetParent(_canvas.transform, false);
            var wr = (RectTransform)_wheel.transform;
            wr.anchorMin = wr.anchorMax = wr.pivot = new Vector2(0.5f, 0.5f); wr.sizeDelta = new Vector2(700f, 700f);
            var back = MG.MakePanel(_wheel.transform, "Back", new Color(0f, 0f, 0f, 0.55f));
            back.GetComponent<Image>().raycastTarget = false;
            MG.MakeText(_wheel.transform, "DANCE", 44, MG.C, new Vector2(0f, 300f), new Vector2(500f, 60f)).fontStyle = FontStyles.Bold;
            for (int i = 0; i < Names.Length; i++)
            {
                int id = i;
                float a = (90f - i * 60f) * Mathf.Deg2Rad;
                var b = MG.MakeButton(_wheel.transform, Names[i], MG.C, new Vector2(Mathf.Cos(a) * 220f, Mathf.Sin(a) * 220f), new Vector2(170f, 90f), Colours[i], () => Begin(id), 30);
            }
            var pointer = new GameObject("Pointer", typeof(RectTransform), typeof(Image));
            pointer.transform.SetParent(_wheel.transform, false);
            _pointer = (RectTransform)pointer.transform;
            _pointer.anchorMin = _pointer.anchorMax = new Vector2(0.5f, 0.5f); _pointer.pivot = new Vector2(0.5f, 0f);
            _pointer.sizeDelta = new Vector2(16f, 130f);
            pointer.GetComponent<Image>().color = Color.white; pointer.GetComponent<Image>().raycastTarget = false;
            MG.MakeButton(_wheel.transform, "SPIN!", MG.C, Vector2.zero, new Vector2(150f, 150f), new Color(0.15f, 0.15f, 0.2f), Spin, 36);
            MG.MakeText(_wheel.transform, "Pick a dance, or SPIN and let the wheel choose.  Moving stops the dance - and dancers are easy to tag!", 22, MG.C, new Vector2(0f, -330f), new Vector2(900f, 40f));
            _wheel.SetActive(false);
            bool touch = Application.isMobilePlatform || Touchscreen.current != null;
            var tb = MG.MakeButton(_canvas.transform, "DANCE", MG.BR, new Vector2(-510f, 560f), new Vector2(120f, 90f), new Color(0.9f, 0.45f, 0.85f, 0.75f), Toggle, 26);
            _touchBtn = tb.gameObject;
            _touchBtn.SetActive(false);
            _touchBtn.name = touch ? "DanceTouch" : "DanceTouchOff";
        }

        public void Toggle()
        {
            BuildUi();
            bool open = !_wheel.activeSelf;
            _wheel.SetActive(open);
            _spinning = false;
        }

        private void Spin()
        {
            if (_spinning) return;
            _spinning = true; _spinT = 0f; _spinSpeed = Random.Range(900f, 1300f);
            GameAudio.Play("beep", 0.5f, 1.4f);
        }

        private void Begin(int type)
        {
            var g = MegaGame.I;
            if (g == null || g.Me == null || g.Me.Out) return;
            if (_wheel != null) _wheel.SetActive(false);
            _spinning = false;
            g.Me.DanceType = type;
            g.Me.DanceUntil = Time.time + 6f;
            _dancing = true; _startT = Time.time;
            _basePos = _player.transform.position; _baseYaw = _player.transform.eulerAngles.y;
            if (_player.Controller != null) _player.Controller.enabled = false;
            GameAudio.Play("pickup", 0.6f, 1.2f);
            TagPopup.Show("Dance: " + Names[type] + "!", Colours[type]);
        }

        private void Stop()
        {
            _dancing = false;
            var g = MegaGame.I;
            if (g != null && g.Me != null) g.Me.DanceUntil = 0f;
            _player.transform.SetPositionAndRotation(_basePos, Quaternion.Euler(0f, _baseYaw, 0f));
            if (_player.Controller != null) _player.Controller.enabled = true;
        }

        private void Update()
        {
            var g = MegaGame.I;
            bool playing = g != null && g.Running && !g.Finished && g.Me != null;
            if (_touchBtn != null) _touchBtn.SetActive(playing && _touchBtn.name == "DanceTouch" && !(MegaCars.I != null && MegaCars.I.Driving));
            if (!playing)
            {
                if (_dancing) Stop();
                if (_wheel != null && _wheel.activeSelf) _wheel.SetActive(false);
                return;
            }
            if (g.Paused) return;
            var kb = Keyboard.current; var pad = Gamepad.current;
            bool busy = (MegaCars.I != null && MegaCars.I.Driving) || (WorldTravel.I != null && WorldTravel.I.InPlane) || g.Me.Out || g.Me.Knocked;
            if (!busy && ((kb != null && kb.gKey.wasPressedThisFrame) || (pad != null && pad.dpad.up.wasPressedThisFrame))) Toggle();

            if (_spinning && _wheel != null)
            {
                _spinT += Time.deltaTime;
                _spinSpeed = Mathf.MoveTowards(_spinSpeed, 0f, 520f * Time.deltaTime);
                _spinAngle = (_spinAngle + _spinSpeed * Time.deltaTime) % 360f;
                _pointer.localRotation = Quaternion.Euler(0f, 0f, -_spinAngle);
                if (Mathf.FloorToInt(_spinAngle / 60f) != Mathf.FloorToInt((_spinAngle - _spinSpeed * Time.deltaTime) / 60f)) GameAudio.Play("step", 0.4f, 1.8f);
                if (_spinSpeed <= 0f)
                {
                    _spinning = false;
                    int pick = Mathf.RoundToInt(_spinAngle / 60f) % Names.Length;
                    Begin(pick);
                }
            }

            if (_dancing)
            {
                bool moveKey = kb != null && (kb.wKey.isPressed || kb.aKey.isPressed || kb.sKey.isPressed || kb.dKey.isPressed || kb.upArrowKey.isPressed || kb.downArrowKey.isPressed || kb.leftArrowKey.isPressed || kb.rightArrowKey.isPressed);
                if (pad != null && pad.leftStick.ReadValue().sqrMagnitude > 0.1f) moveKey = true;
                if (MobileControls.MoveInput.sqrMagnitude > 0.1f) moveKey = true;
                if (moveKey || !g.Me.Dancing || g.Me.Knocked || g.Me.Out || busy) { Stop(); return; }
                Pose(g.Me.DanceType, Time.time - _startT, out var extra, out float lift);
                _player.transform.SetPositionAndRotation(_basePos + Vector3.up * lift, Quaternion.Euler(0f, _baseYaw, 0f) * extra);
            }
        }
    }
}
