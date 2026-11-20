using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using TagGame.UI;
using MG = TagGame.Multiplayer.MultiplayerGame;

namespace TagGame.Mega
{
    /// <summary>A button you hold down (touch FIRE / TIE).</summary>
    public class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public System.Action<bool> OnHold;
        public void OnPointerDown(PointerEventData e) { OnHold?.Invoke(true); }
        public void OnPointerUp(PointerEventData e) { OnHold?.Invoke(false); }
        public void OnPointerExit(PointerEventData e) { OnHold?.Invoke(false); }
    }

    /// <summary>
    /// Mega Mode screens: the MEGA MODE button on the title screen, the setup panel (bot count), the in-game HUD
    /// (health, armour, slots, laces, rank, timer, feed), pause and results, and touch buttons on phones.
    /// </summary>
    public class MegaHud : MonoBehaviour
    {
        private GameFlowUI _flow;
        private GameObject _canvas, _setup, _hud, _pause, _results, _loading, _touch;
        private Button[] _botButtons, _countryButtons, _modeButtons;
        private Image _hpFill, _armFill, _flash, _tieFill, _reticle;
        private TMP_Text _hpText, _timer, _rank, _feed, _prompt, _laces, _resultText, _info;
        private readonly Image[] _slotBg = new Image[4];
        private readonly TMP_Text[] _slotText = new TMP_Text[4];
        private GameObject _tieBar;
        private float _flashA;

        private IEnumerator Start()
        {
            yield return null;   // let the other start-screen buttons go in first
            _flow = FindFirstObjectByType<GameFlowUI>();
            BuildCanvas();
            AddTitleButton();
            if (MegaGame.I != null)
            {
                MegaGame.I.OnFinished += ShowResults;
                MegaGame.I.OnPlayerHit += dmg => _flashA = Mathf.Clamp01(_flashA + dmg / 60f + 0.15f);
            }
        }

        private static readonly Color Orange = new Color(0.95f, 0.5f, 0.1f);

        private void AddTitleButton()
        {
            if (_flow == null || _flow.StartPanel == null) return;
            var b = MG.MakeButton(_flow.StartPanel.transform, "MEGA MODE", MG.C, new Vector2(0f, -238f), new Vector2(300f, 74f), Orange, OpenSetup, 34);
            b.GetComponentInChildren<TMP_Text>().fontStyle = FontStyles.Bold;
            var tag = MG.MakeText(b.transform, "NEW", 18, new Vector2(1f, 1f), new Vector2(10f, 12f), new Vector2(70f, 28f));
            tag.color = new Color(1f, 0.95f, 0.4f);
            tag.fontStyle = FontStyles.Bold;
        }

        private void BuildCanvas()
        {
            _canvas = new GameObject("MegaCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var cv = _canvas.GetComponent<Canvas>();
            cv.renderMode = RenderMode.ScreenSpaceOverlay;
            cv.sortingOrder = 40;
            var sc = _canvas.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1920f, 1080f);
            sc.matchWidthOrHeight = 0.5f;
            var root = _canvas.transform;

            // ---------------- setup
            _setup = MG.MakePanel(root, "MegaSetup", new Color(0.04f, 0.05f, 0.08f, 0.93f));
            var s = _setup.transform;
            var title = MG.MakeText(s, "MEGA MODE", 92, MG.C, new Vector2(0f, 410f), new Vector2(1200f, 120f));
            title.color = new Color(1f, 0.6f, 0.15f); title.fontStyle = FontStyles.Bold;
            MG.MakeText(s, "A world of walled island countries: Mega City, Harbor City and Sky City (10 km each) and the 100 km Super Mega World.\nSchools, malls, towers with stairs and elevators, lakes, forests and airports - fly planes between countries. Up to 100 taggers at once.", 24, MG.C, new Vector2(0f, 318f), new Vector2(1600f, 80f)).color = new Color(0.85f, 0.88f, 0.92f);
            var ml = MG.MakeText(s, "MODE", 30, MG.C, new Vector2(-330f, 248f), new Vector2(200f, 44f)); ml.fontStyle = FontStyles.Bold;
            _modeButtons = new[]
            {
                MG.MakeButton(s, "Normal\n<size=16>taggers vs runners, 10 min</size>", MG.C, new Vector2(-80f, 248f), new Vector2(260f, 64f), new Color(0.2f, 0.2f, 0.28f), () => { MegaGame.I.SetEndless(false); RefreshBots(); }, 26),
                MG.MakeButton(s, "Endless\n<size=16>everyone tags, never ends</size>", MG.C, new Vector2(200f, 248f), new Vector2(260f, 64f), new Color(0.2f, 0.2f, 0.28f), () => { MegaGame.I.SetEndless(true); RefreshBots(); }, 26),
            };
            MG.MakeText(s, "PLAYERS", 30, MG.C, new Vector2(0f, 190f), new Vector2(600f, 44f)).fontStyle = FontStyles.Bold;
            _botButtons = new Button[MegaGame.BotChoices.Length];
            for (int i = 0; i < _botButtons.Length; i++)
            {
                int id = i;
                long v = MegaGame.BotChoices[i];
                string label = v == 0 ? "Auto" : v >= 1000000000000 ? "1 TRILLION" : v >= 1000000000 ? "1 BILLION" : v >= 1000000 ? "1 MILLION" : v.ToString("N0");
                _botButtons[i] = MG.MakeButton(s, label, MG.C, new Vector2((i - (_botButtons.Length - 1) * 0.5f) * 230f, 125f), new Vector2(210f, 70f), new Color(0.2f, 0.2f, 0.28f), () => { MegaGame.I.SetBotChoice(id); RefreshBots(); }, 30);
            }
            _info = MG.MakeText(s, "", 22, MG.C, new Vector2(0f, 72f), new Vector2(1500f, 36f));
            _info.color = new Color(0.75f, 0.8f, 0.85f);
            MG.MakeText(s, "START IN", 30, MG.C, new Vector2(0f, 22f), new Vector2(600f, 44f)).fontStyle = FontStyles.Bold;
            _countryButtons = new Button[MegaWorld.Countries.Length];
            for (int i = 0; i < _countryButtons.Length; i++)
            {
                int id = i;
                var c = MegaWorld.Countries[i];
                _countryButtons[i] = MG.MakeButton(s, c.Name + "\n<size=18>" + (c.Half * 2f / 1000f).ToString("0") + " km</size>", MG.C, new Vector2((i - (_countryButtons.Length - 1) * 0.5f) * 300f, -45f), new Vector2(280f, 80f), new Color(0.2f, 0.2f, 0.28f), () => { MegaGame.I.StartCountry = id; RefreshBots(); }, 26);
            }
            MG.MakeText(s, "<b>Controls</b>   WASD move · Shift sprint · Ctrl/Q dash · <b>1-4</b> slots · <b>Click / F</b> fire or use · <b>E</b> vending, lockers, swap, cars, planes · <b>T</b> hold to tie laces · <b>G</b> dance · Esc pause\nCars: W/S drive · A/D steer · F self-driving · P auto park · C camera · E get out.   Planes: Shift/Ctrl power · W/S nose · A/D turn.   Wait for green at crossings!",
                20, MG.C, new Vector2(0f, -140f), new Vector2(1700f, 80f)).color = new Color(0.7f, 0.75f, 0.8f);
            MG.MakeButton(s, "START", MG.C, new Vector2(0f, -250f), new Vector2(380f, 96f), new Color(0.15f, 0.6f, 0.3f), StartGame, 46);
            MG.MakeButton(s, "Back", MG.C, new Vector2(0f, -355f), new Vector2(240f, 60f), new Color(0.5f, 0.2f, 0.2f), CloseSetup, 28);
            _setup.SetActive(false);

            _loading = MG.MakePanel(root, "MegaLoading", new Color(0.04f, 0.05f, 0.08f, 0.96f));
            MG.MakeText(_loading.transform, "Building the Mega City...", 56, MG.C, Vector2.zero, new Vector2(1200f, 100f));
            _loading.SetActive(false);

            // ---------------- HUD
            _hud = new GameObject("MegaHud", typeof(RectTransform));
            _hud.transform.SetParent(root, false);
            MG.Stretch((RectTransform)_hud.transform);
            var h = _hud.transform;

            _flash = MG.MakePanel(h, "Hurt", new Color(0.9f, 0.05f, 0.05f, 0f)).GetComponent<Image>();
            _flash.raycastTarget = false;

            var bars = Box(h, new Vector2(0f, 0f), new Vector2(28f, 28f), new Vector2(420f, 92f), new Color(0f, 0f, 0f, 0.5f));
            _hpFill = Fill(bars, 0, new Color(0.25f, 0.85f, 0.35f));
            _armFill = Fill(bars, 1, new Color(0.3f, 0.55f, 1f));
            _hpText = MG.MakeText(bars, "", 22, new Vector2(0f, 1f), new Vector2(10f, 30f), new Vector2(400f, 30f));
            ((RectTransform)_hpText.transform).pivot = new Vector2(0f, 0.5f);
            _hpText.alignment = TextAlignmentOptions.Left;

            for (int i = 0; i < 4; i++)
            {
                var slot = Box(h, new Vector2(0.5f, 0f), new Vector2((i - 1.5f) * 190f, 28f), new Vector2(175f, 96f), new Color(0f, 0f, 0f, 0.55f));
                ((RectTransform)slot).pivot = new Vector2(0.5f, 0f);
                _slotBg[i] = slot.GetComponent<Image>();
                _slotText[i] = MG.MakeText(slot, "", 22, MG.C, Vector2.zero, new Vector2(170f, 90f));
                var num = MG.MakeText(slot, (i + 1).ToString(), 18, new Vector2(0f, 1f), new Vector2(14f, -14f), new Vector2(30f, 24f));
                num.color = new Color(1f, 1f, 1f, 0.6f);
            }

            _timer = MG.MakeText(h, "", 44, MG.T, new Vector2(0f, -150f), new Vector2(400f, 60f));
            _timer.fontStyle = FontStyles.Bold;
            _rank = MG.MakeText(h, "", 24, new Vector2(1f, 1f), new Vector2(-24f, -400f), new Vector2(460f, 110f));
            ((RectTransform)_rank.transform).pivot = new Vector2(1f, 1f);
            _rank.alignment = TextAlignmentOptions.TopRight;
            _feed = MG.MakeText(h, "", 20, new Vector2(1f, 0.5f), new Vector2(-24f, -40f), new Vector2(520f, 220f));
            ((RectTransform)_feed.transform).pivot = new Vector2(1f, 0.5f);
            _feed.alignment = TextAlignmentOptions.BottomRight;
            _prompt = MG.MakeText(h, "", 30, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(1000f, 50f));
            _laces = MG.MakeText(h, "", 34, MG.C, new Vector2(0f, -170f), new Vector2(1100f, 60f));
            _laces.color = new Color(1f, 0.65f, 0.15f); _laces.fontStyle = FontStyles.Bold;
            _tieBar = Box(h, MG.C, new Vector2(0f, -215f), new Vector2(360f, 18f), new Color(0f, 0f, 0f, 0.6f)).gameObject;
            _tieFill = MG.MakePanel(_tieBar.transform, "Fill", new Color(1f, 0.65f, 0.15f)).GetComponent<Image>();
            ((RectTransform)_tieFill.transform).pivot = new Vector2(0f, 0.5f);
            _reticle = Box(h, Vector2.zero, Vector2.zero, new Vector2(46f, 46f), new Color(1f, 0.25f, 0.2f, 0.7f)).GetComponent<Image>();
            ((RectTransform)_reticle.transform).pivot = new Vector2(0.5f, 0.5f);
            _reticle.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
            _hud.SetActive(false);

            // ---------------- touch buttons
            bool touch = Application.isMobilePlatform || Touchscreen.current != null;
            _touch = new GameObject("MegaTouch", typeof(RectTransform));
            _touch.transform.SetParent(h, false);
            MG.Stretch((RectTransform)_touch.transform);
            Hold(_touch.transform, "FIRE", new Vector2(-150f, 560f), 150f, new Color(0.9f, 0.25f, 0.2f, 0.75f), v => MegaPlayer.TouchFire = v);
            Hold(_touch.transform, "TIE", new Vector2(-330f, 560f), 110f, new Color(0.95f, 0.6f, 0.15f, 0.75f), v => MegaPlayer.TouchTie = v);
            MG.MakeButton(_touch.transform, "USE", MG.BR, new Vector2(-330f, 690f), new Vector2(110f, 90f), new Color(0.2f, 0.45f, 0.85f, 0.75f), () => MegaPlayer.TouchUseFrame = Time.frameCount, 28);
            MG.MakeButton(_touch.transform, "SLOT", MG.BR, new Vector2(-150f, 720f), new Vector2(150f, 70f), new Color(0.3f, 0.3f, 0.35f, 0.75f), () => MegaPlayer.TouchSlotFrame = Time.frameCount, 26);
            MG.MakeButton(_touch.transform, "II", new Vector2(1f, 1f), new Vector2(-40f, -40f), new Vector2(90f, 90f), new Color(0f, 0f, 0f, 0.5f), () => MegaGame.I.TogglePause(), 36);
            _touch.SetActive(touch);

            // ---------------- pause / results
            _pause = MG.MakePanel(root, "MegaPause", new Color(0f, 0f, 0f, 0.75f));
            MG.MakeText(_pause.transform, "PAUSED", 80, MG.C, new Vector2(0f, 200f), new Vector2(800f, 110f));
            MG.MakeButton(_pause.transform, "Resume", MG.C, new Vector2(0f, 50f), new Vector2(300f, 80f), new Color(0.15f, 0.6f, 0.3f), () => MegaGame.I.TogglePause(), 34);
            MG.MakeButton(_pause.transform, "Restart", MG.C, new Vector2(0f, -50f), new Vector2(300f, 80f), new Color(0.2f, 0.45f, 0.85f), Restart, 34);
            MG.MakeButton(_pause.transform, "Menu", MG.C, new Vector2(0f, -150f), new Vector2(300f, 80f), new Color(0.5f, 0.2f, 0.2f), ToMenu, 34);
            _pause.SetActive(false);

            _results = MG.MakePanel(root, "MegaResults", new Color(0f, 0f, 0f, 0.8f));
            MG.MakeText(_results.transform, "MEGA MODE OVER", 64, MG.C, new Vector2(0f, 300f), new Vector2(1000f, 90f)).color = new Color(1f, 0.6f, 0.15f);
            _resultText = MG.MakeText(_results.transform, "", 34, MG.C, new Vector2(0f, 40f), new Vector2(1100f, 400f));
            MG.MakeButton(_results.transform, "Play again", MG.C, new Vector2(-170f, -260f), new Vector2(300f, 80f), new Color(0.15f, 0.6f, 0.3f), Restart, 34);
            MG.MakeButton(_results.transform, "Menu", MG.C, new Vector2(170f, -260f), new Vector2(300f, 80f), new Color(0.5f, 0.2f, 0.2f), ToMenu, 34);
            _results.SetActive(false);
        }

        private static RectTransform Box(Transform parent, Vector2 anchor, Vector2 pos, Vector2 size, Color c)
        {
            var go = new GameObject("Box", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = anchor;
            rt.anchoredPosition = pos; rt.sizeDelta = size;
            var im = go.GetComponent<Image>(); im.color = c; im.raycastTarget = false;
            return rt;
        }

        private static Image Fill(RectTransform parent, int row, Color c)
        {
            var bg = Box(parent, new Vector2(0f, 0f), new Vector2(10f, 10f + (1 - row) * 26f), new Vector2(400f, 20f), new Color(1f, 1f, 1f, 0.12f));
            var f = MG.MakePanel(bg, "Fill", c).GetComponent<Image>();
            f.raycastTarget = false;
            ((RectTransform)f.transform).pivot = new Vector2(0f, 0.5f);
            return f;
        }

        private static void Hold(Transform parent, string label, Vector2 pos, float size, Color c, System.Action<bool> onHold)
        {
            var b = MG.MakeButton(parent, label, MG.BR, pos, new Vector2(size, size), c, () => { }, 30);
            b.gameObject.AddComponent<HoldButton>().OnHold = onHold;
        }

        // ------------------------------------------------------------ flow

        private void OpenSetup()
        {
            if (_flow != null && _flow.StartPanel != null) _flow.StartPanel.SetActive(false);
            _setup.SetActive(true);
            RefreshBots();
        }

        private void CloseSetup()
        {
            _setup.SetActive(false);
            if (_flow != null && _flow.StartPanel != null) _flow.StartPanel.SetActive(true);
        }

        private void RefreshBots()
        {
            int sel = MegaGame.I.BotChoice;
            for (int i = 0; i < _botButtons.Length; i++)
                _botButtons[i].GetComponent<Image>().color = i == sel ? new Color(0.95f, 0.5f, 0.1f) : new Color(0.2f, 0.2f, 0.28f);
            for (int i = 0; i < _modeButtons.Length; i++)
                _modeButtons[i].GetComponent<Image>().color = (i == 1) == MegaGame.I.EndlessChoice ? new Color(0.85f, 0.25f, 0.45f) : new Color(0.2f, 0.2f, 0.28f);
            for (int i = 0; i < _countryButtons.Length; i++)
                _countryButtons[i].GetComponent<Image>().color = i == MegaGame.I.StartCountry ? new Color(0.2f, 0.5f, 0.9f) : new Color(0.2f, 0.2f, 0.28f);
            long n = MegaGame.BotChoices[sel];
            _info.text = n == 0
                ? "Auto: 10,000 players. The zone of full bots around you grows or shrinks to keep the game smooth"
                : n > MegaGame.MaxSimulated
                    ? n.ToString("N0") + " players - full bots around you, a million roaming your country, and the rest counted across the world. For really good PCs!"
                    : n.ToString("N0") + " players - the nearest (up to ~2,000) are full bots around you, the rest roam the country as the crowd";
        }

        private void StartGame() { StartCoroutine(StartRoutine()); }

        private IEnumerator StartRoutine()
        {
            _setup.SetActive(false); _results.SetActive(false); _pause.SetActive(false);
            _loading.SetActive(true);
            yield return null; yield return null;
            MegaGame.I.Begin();
            _loading.SetActive(false);
        }

        private void Restart()
        {
            if (MegaGame.I.Paused) MegaGame.I.TogglePause();
            StartCoroutine(StartRoutine());
        }

        private void ToMenu()
        {
            _pause.SetActive(false); _results.SetActive(false);
            MegaGame.I.Quit();
            if (_flow != null && _flow.StartPanel != null) _flow.StartPanel.SetActive(true);
        }

        private void ShowResults()
        {
            _resultText.text = MegaGame.I.ResultText;
            _results.SetActive(true);
        }

        // ------------------------------------------------------------ HUD

        private void Update()
        {
            var g = MegaGame.I;
            if (g == null || _hud == null) return;
            bool on = g.Running;
            if (_hud.activeSelf != on) _hud.SetActive(on);
            if (_pause.activeSelf != (on && g.Paused)) _pause.SetActive(on && g.Paused);
            if (!on || g.Me == null) return;
            var me = g.Me;
            var mp = MegaWorld.Player != null ? MegaWorld.Player.GetComponent<MegaPlayer>() : null;

            _hpFill.rectTransform.localScale = new Vector3(Mathf.Clamp01(me.Hp / 100f), 1f, 1f);
            _hpFill.color = me.Hp < 30f ? new Color(0.95f, 0.25f, 0.2f) : new Color(0.25f, 0.85f, 0.35f);
            _armFill.rectTransform.localScale = new Vector3(Mathf.Clamp01(me.Armour / 100f), 1f, 1f);
            _hpText.text = "<b>HEALTH " + Mathf.CeilToInt(me.Hp) + "</b>   armour " + Mathf.CeilToInt(me.Armour) + (mp != null && mp.SneakersOn ? "   <color=#FFA020>SNEAKERS</color>" : "");

            int secs = Mathf.CeilToInt(g.TimeLeft);
            _timer.text = g.Endless ? "ENDLESS" : (secs / 60) + ":" + (secs % 60).ToString("00");
            _timer.color = secs <= 30 ? new Color(1f, 0.35f, 0.3f) : Color.white;
            _rank.text = (g.Endless ? "Tags <b>" + me.Tags + "</b>  ·  " : "") + "Rank <b>" + g.MyRank().ToString("N0") + "</b> / " + g.TotalPlayers.ToString("N0") + "\n" + g.ItCount.ToString("N0") + " IT · bots " + g.BotCount.ToString("N0") + (g.AutoBots ? " (auto)" : "") + (g.CrowdMode ? "\n" + g.NearbyCount.ToString("N0") + " near you" : "") + " · " + Mathf.RoundToInt(g.Fps) + " fps";
            var sb = new StringBuilder();
            foreach (var f in g.Feed) sb.Append(f).Append('\n');
            _feed.text = sb.ToString();

            if (mp != null)
            {
                for (int i = 0; i < 4; i++)
                {
                    var s = mp.Slots[i];
                    bool sel = i == mp.Selected;
                    _slotBg[i].color = sel ? new Color(0.95f, 0.5f, 0.1f, 0.8f) : new Color(0f, 0f, 0f, 0.55f);
                    if (!s.Has) _slotText[i].text = "<color=#888>empty</color>";
                    else
                    {
                        string col = ColorUtility.ToHtmlStringRGB(MegaItems.ColorOf(s.Type));
                        _slotText[i].text = "<color=#" + col + "><b>" + MegaItems.NameOf(s.Type) + "</b></color>" + (MegaItems.IsGun(s.Type) ? "\n" + s.Ammo + " ammo" : "\nclick to use");
                    }
                }
                _prompt.text = mp.Prompt;
                bool tying = me.Tying;
                _tieBar.SetActive(tying);
                if (tying) _tieFill.rectTransform.localScale = new Vector3(mp.TieProgress, 1f, 1f);
                _laces.text = me.Out ? "<color=#B070FF>YOU'RE OUT - watching " + (g.Watching != null ? g.Watching.Name : "") + "</color>" : me.Knocked ? "<color=#B070FF>KNOCKED DOWN - easy to tag!</color>" : (me.Untied ? (tying ? "Tying your laces... (easy to tag!)" : "Shoelaces untied!  Hold T to tie them") : "");

                var cam = Camera.main;
                bool showRet = mp.Locked != null && cam != null;
                if (showRet)
                {
                    Vector3 sp = cam.WorldToScreenPoint(mp.Locked.Pos + Vector3.up * 1.1f);
                    showRet = sp.z > 0f;
                    if (showRet)
                    {
                        var crt = (RectTransform)_canvas.transform;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(crt, sp, null, out var lp);
                        _reticle.rectTransform.anchorMin = _reticle.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
                        _reticle.rectTransform.anchoredPosition = lp;
                    }
                }
                if (_reticle.gameObject.activeSelf != showRet) _reticle.gameObject.SetActive(showRet);
            }

            _flashA = Mathf.MoveTowards(_flashA, me.Knocked ? 0.25f : 0f, Time.unscaledDeltaTime * 1.5f);
            _flash.color = new Color(0.9f, 0.05f, 0.05f, _flashA * 0.45f);
        }
    }
}
