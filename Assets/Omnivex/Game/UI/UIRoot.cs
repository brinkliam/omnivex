using System.Collections.Generic;
using OMX.Core;
using UnityEngine;
using UnityEngine.UIElements;
using static OMX.Game.UIKit;

namespace OMX.Game
{
    /// <summary>
    /// All interface, built in code with UI Toolkit (no OnGUI). Layers: HUD, screens, titles/toasts, flash, ad overlay.
    /// The panel scales so the screen's short side is always 720 units, in portrait or landscape, inside the safe area.
    /// </summary>
    public sealed partial class UIRoot : MonoBehaviour
    {
        public RunController Run; public ServicesHub Services; public PlayerCreature Player; public Matriarch Boss;
        public CameraRig Rig; public AudioDirector Audio; public Ecosystem Eco;
        UIDocument _doc;
        PanelSettings _ps;
        VisualElement _root, _safe, _hud, _screens, _titles, _toasts, _flash, _adLayer, _stick, _knob, _rareArrow;
        VisualElement _bioFill, _hpFill, _dna, _bossBox, _bossFill, _dashFill, _hiddenChip, _frenzyChip;
        Label _tier, _pending, _hpText, _bossName, _bigTitle, _bigSub, _frenzyText, _bioText;
        float _titleT, _titleDur, _flashA;
        Color _flashC;
        bool _photo;
        VisualElement _alertBox;
        readonly VisualElement[] _pips = new VisualElement[5];
        Label _alertText, _zoneText;
        Label _stickHint;
        float _hintSeen;
        public void ResetStickHint() => _hintSeen = 0;

        /// <summary>A small always-on line listing the dev keys (only shown with ?dev=1).</summary>
        public void SetDevHint(string text)
        {
            var l = Text(text, 12, Palette.Warning, true, TextAnchor.MiddleCenter);
            l.style.position = Position.Absolute; l.style.left = 0; l.style.right = 0; l.style.bottom = 4;
            l.style.backgroundColor = new Color(0, 0, 0, 0.45f);
            _safe.Add(l);
        }

        public void Init()
        {
            _doc = gameObject.AddComponent<UIDocument>();
            _ps = Resources.Load<PanelSettings>("UI/OMX_PanelSettings");
            if (_ps == null) { _ps = ScriptableObject.CreateInstance<PanelSettings>(); Debug.LogWarning("[OMX] PanelSettings missing; run Omnivex > Setup Project"); }
            _ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            _ps.referenceResolution = new Vector2Int(720, 720);
            _doc.panelSettings = _ps;
            UIKit.Audio = Audio;
            _root = _doc.rootVisualElement;
            _root.style.flexGrow = 1;
            _safe = Box(); Fill(_safe); _root.Add(_safe);
            _safe.pickingMode = PickingMode.Ignore;

            _hud = Box(); Fill(_hud); _hud.pickingMode = PickingMode.Ignore; _safe.Add(_hud);
            BuildHud();
            _titles = Centered(); _titles.pickingMode = PickingMode.Ignore; _safe.Add(_titles);
            _bigTitle = Text("", 64, Palette.Ui, true, TextAnchor.MiddleCenter);
            _bigSub = Text("", 22, Palette.UiDim, false, TextAnchor.MiddleCenter);
            _bigSub.style.maxWidth = 640;
            _titles.Add(_bigTitle); _titles.Add(_bigSub);
            _titles.style.opacity = 0;
            _toasts = Box(); _toasts.style.position = Position.Absolute; _toasts.style.top = 96; _toasts.style.left = 0; _toasts.style.right = 0;
            _toasts.style.alignItems = Align.Center; _toasts.pickingMode = PickingMode.Ignore; _safe.Add(_toasts);
            _screens = Box(); Fill(_screens); _screens.pickingMode = PickingMode.Ignore; _safe.Add(_screens);   // menus sit above titles and toasts
            _flash = Box(); Fill(_flash); _flash.pickingMode = PickingMode.Ignore; _root.Add(_flash);
            _adLayer = Box(); Fill(_adLayer); _adLayer.pickingMode = PickingMode.Ignore; _root.Add(_adLayer);

            InputRouter.I.BlockedByUi = BlocksPointer;
            InputRouter.I.OnPause += () => { if (Run.State == RunState.Playing) Run.Pause(); else if (Run.State == RunState.Paused) Run.Resume(); };
            InputRouter.I.OnDash += () => Player.Dash();
            Services.MockAds.Presenter = ShowTestAd;
            Services.Ads.OnTimedOut += _ => { _abortTestAd?.Invoke(); Toast("AD UNAVAILABLE", "No reward this time", Palette.UiDim); };
        }

        /// <summary>True if a screen position lands on an interactive UI element (so it shouldn't steer the creature).</summary>
        public bool BlocksPointer(Vector2 screenPos)
        {
            if (_root?.panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            var picked = _root.panel.Pick(p);
            if (picked == null || picked == _root || picked is Label) return false;
            for (var e = picked; e != null; e = e.parent) if (e == _root) return true;
            return false;
        }

        // ------------------------------------------------------------------ HUD

        void BuildHud()
        {
            // top-left: tier, growth, health, diet
            var tl = Box(); tl.style.position = Position.Absolute; tl.style.left = 18; tl.style.top = 14; tl.style.width = 300;
            tl.pickingMode = PickingMode.Ignore;
            var row = Box(true); row.style.alignItems = Align.Center; row.pickingMode = PickingMode.Ignore;
            _tier = Text("SPECK", 26, Palette.Ui, true);
            _pending = Text("", 16, Palette.Abyss, true, TextAnchor.MiddleCenter);
            _pending.style.backgroundColor = Palette.Accent; Radius(_pending, 10); _pending.style.marginLeft = 10;
            _pending.style.paddingLeft = 8; _pending.style.paddingRight = 8;
            row.Add(_tier); row.Add(_pending);
            tl.Add(row);
            var bio = Bar(Palette.Accent, 14, out _bioFill); bio.style.marginTop = 6; tl.Add(bio);
            _bioText = Text("", 13, Palette.UiDim); tl.Add(_bioText);
            var hp = Bar(Palette.Hex(0xFF5A7A), 10, out _hpFill); hp.style.marginTop = 6; tl.Add(hp);
            _hpText = Text("", 13, Palette.UiDim); tl.Add(_hpText);
            _dna = Box(true); _dna.style.height = 8; _dna.style.marginTop = 6; Radius(_dna, 4); _dna.style.overflow = Overflow.Hidden;
            _dna.pickingMode = PickingMode.Ignore;
            for (int i = 0; i < 7; i++) { var seg = Box(); seg.style.backgroundColor = Palette.Of((Dna)i); seg.style.flexGrow = 0; seg.pickingMode = PickingMode.Ignore; _dna.Add(seg); }
            tl.Add(_dna);
            var chips = Box(true); chips.style.marginTop = 8; chips.pickingMode = PickingMode.Ignore;
            _hiddenChip = Chip("HIDDEN", Palette.Of(Dna.Flora)); _hiddenChip.style.marginRight = 6; chips.Add(_hiddenChip);
            _frenzyChip = Chip("FRENZY", Palette.Of(Dna.Flesh)); chips.Add(_frenzyChip);
            _frenzyText = _frenzyChip.Q<Label>();
            tl.Add(chips);
            _hud.Add(tl);

            // top-right: pause + photo
            var tr = Box(true); tr.style.position = Position.Absolute; tr.style.right = 14; tr.style.top = 10;
            tr.Add(Btn("PHOTO", () => TogglePhoto(true), false, 16));
            tr.Add(Btn("II", () => Run.Pause(), false, 18));
            _hud.Add(tr);

            // boss bar
            _bossBox = Box(); _bossBox.style.position = Position.Absolute; _bossBox.style.top = 74; _bossBox.style.left = Length.Percent(20); _bossBox.style.right = Length.Percent(20);
            _bossBox.style.alignItems = Align.Stretch; _bossBox.pickingMode = PickingMode.Ignore;
            _bossName = Text("", 20, Palette.Hex(0xFF9AE0), true, TextAnchor.MiddleCenter);
            _bossBox.Add(_bossName);
            var bb = Bar(Palette.Hex(0xFF6AD5), 14, out _bossFill); _bossBox.Add(bb);
            _hud.Add(_bossBox);

            _alertBox = Box(); _alertBox.style.position = Position.Absolute; _alertBox.style.right = 16; _alertBox.style.top = 70;
            _alertBox.style.alignItems = Align.FlexEnd; _alertBox.pickingMode = PickingMode.Ignore;
            var pipRow = Box(true); pipRow.style.alignItems = Align.Center; pipRow.pickingMode = PickingMode.Ignore;
            _alertText = Text("ALERT", 15, Palette.Danger, true); _alertText.style.marginRight = 8;
            pipRow.Add(_alertText);
            for (int i = 0; i < 5; i++)
            {
                var pip = Box(); pip.style.width = 16; pip.style.height = 16; pip.style.marginLeft = 3; Radius(pip, 4);
                pip.style.rotate = new Rotate(45); pip.pickingMode = PickingMode.Ignore;
                _pips[i] = pip; pipRow.Add(pip);
            }
            _alertBox.Add(pipRow);
            _zoneText = Text("", 13, Palette.UiDim, false, TextAnchor.MiddleRight);
            _alertBox.Add(_zoneText);
            _hud.Add(_alertBox);

            // bottom-right dash button with cooldown fill
            var dash = Box(); dash.style.position = Position.Absolute; dash.style.right = 26; dash.style.bottom = 26;
            dash.style.width = 112; dash.style.height = 112; Radius(dash, 56); dash.style.overflow = Overflow.Hidden;
            dash.style.backgroundColor = new Color(0.04f, 0.16f, 0.18f, 0.75f); Border(dash, Palette.Accent, 2);
            dash.style.justifyContent = Justify.Center; dash.style.alignItems = Align.Center;
            _dashFill = Box(); _dashFill.style.position = Position.Absolute; _dashFill.style.left = 0; _dashFill.style.right = 0; _dashFill.style.bottom = 0;
            _dashFill.style.backgroundColor = new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.35f); _dashFill.pickingMode = PickingMode.Ignore;
            dash.Add(_dashFill);
            dash.Add(Text("DASH", 20, Palette.Ui, true, TextAnchor.MiddleCenter));
            dash.RegisterCallback<PointerDownEvent>(e => { InputRouter.I.RequestDash(); e.StopPropagation(); });
            _hud.Add(dash);

            // floating joystick
            _stick = Box(); _stick.style.position = Position.Absolute; _stick.style.width = 150; _stick.style.height = 150; Radius(_stick, 75);
            Border(_stick, new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.5f), 2); _stick.style.backgroundColor = new Color(0, 0, 0, 0.15f);
            _stick.pickingMode = PickingMode.Ignore;
            _knob = Box(); _knob.style.position = Position.Absolute; _knob.style.width = 62; _knob.style.height = 62; Radius(_knob, 31);
            _knob.style.backgroundColor = new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.55f); _knob.pickingMode = PickingMode.Ignore;
            _hud.Add(_stick); _hud.Add(_knob);
            _stickHint = Text("DRAG TO SWIM", 18, Palette.Accent, true, TextAnchor.MiddleCenter);
            _stickHint.style.position = Position.Absolute; _stickHint.style.width = 180;
            _hud.Add(_stickHint);

            _rareArrow = Box(); _rareArrow.style.position = Position.Absolute; _rareArrow.style.width = 0; _rareArrow.style.height = 0;
            _rareArrow.pickingMode = PickingMode.Ignore;
            var arrowLabel = Text("GULPER", 14, Palette.Of(Dna.Void), true, TextAnchor.MiddleCenter);
            arrowLabel.style.position = Position.Absolute; arrowLabel.style.left = -40; arrowLabel.style.width = 80; arrowLabel.style.top = -10;
            _rareArrow.Add(arrowLabel);
            _hud.Add(_rareArrow);
            _hud.style.display = DisplayStyle.None;
        }

        public void ShowHud(bool on = true)
        {
            ClearScreens();
            _hud.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
        }

        void Update()
        {
            if (_root == null) return;
            float dt = Time.unscaledDeltaTime;
            // portrait vs landscape: keep the short side at 720 units
            _ps.match = Screen.width >= Screen.height ? 1 : 0;
            var sa = Screen.safeArea;
            float k = _root.layout.height > 0 ? _root.layout.height / Screen.height : 1;
            _safe.style.left = sa.xMin * k; _safe.style.right = (Screen.width - sa.xMax) * k;
            _safe.style.top = (Screen.height - sa.yMax) * k; _safe.style.bottom = sa.yMin * k;

            if (_titleDur > 0)
            {
                _titleT += dt;
                float u = _titleT / _titleDur;
                _titles.style.opacity = u < 0.12f ? u / 0.12f : u > 0.8f ? Mathf.Max(0, (1 - u) / 0.2f) : 1;
                _bigTitle.style.letterSpacing = Mathf.Lerp(18, 6, Mathf.Clamp01(u * 2));
                if (u >= 1) _titleDur = 0;
            }
            _flashA = Mathf.MoveTowards(_flashA, 0, dt * 1.6f);
            _flash.style.backgroundColor = new Color(_flashC.r, _flashC.g, _flashC.b, _flashA);
            UpdateToasts(dt);
            _photoT += dt;
            if (!_photo && _savedPhoto) { _savedPhoto = false; Toast("PHOTO SAVED", "In the game's data folder", Palette.Accent); }
            if (_photo && _photoT > 0.35f && (UnityEngine.InputSystem.Mouse.current?.leftButton.wasPressedThisFrame == true || UnityEngine.InputSystem.Touchscreen.current?.primaryTouch.press.wasPressedThisFrame == true || UnityEngine.InputSystem.Keyboard.current?.anyKey.wasPressedThisFrame == true)) TogglePhoto(false);

            if (_hud.style.display == DisplayStyle.None || Player.Genome == null) return;
            var g = Player.Genome;
            _tier.text = Genome.TierNames[g.Tier].ToUpperInvariant();
            float prev = g.PrevThreshold, next = g.NextThreshold;
            _bioFill.style.width = Length.Percent(Mathf.Clamp01((g.BiomassTotal - prev) / Mathf.Max(0.01f, next - prev)) * 100);
            _bioText.text = g.BiomassTotal < 10 ? $"Biomass {g.BiomassTotal:0.0}  ·  next evolution {next:0}" : $"Biomass {g.BiomassTotal:0}  ·  next evolution {next:0}";
            _pending.text = g.PendingEvolutions > 0 ? "EVOLVE x" + g.PendingEvolutions : "";
            _pending.style.display = g.PendingEvolutions > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _hpFill.style.width = Length.Percent(Mathf.Clamp01(Player.Health / Player.MaxHealth) * 100);
            _hpText.text = $"{Mathf.CeilToInt(Player.Health)} / {Mathf.CeilToInt(Player.MaxHealth)}" + (g.Current.Armor > 0.01f ? $"  ·  armour {g.Current.Armor * 100:0}%" : "");
            float sum = 0; foreach (var a in g.Affinity) sum += a;
            for (int i = 0; i < 7; i++) _dna[i].style.flexGrow = sum > 0 ? g.Affinity[i] / sum : 0;
            _hiddenChip.style.display = Player.Hidden ? DisplayStyle.Flex : DisplayStyle.None;
            _frenzyChip.style.display = Player.FrenzyTime > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (Player.FrenzyTime > 0) _frenzyText.text = $"FRENZY {Player.FrenzyTime:0}s";
            _dashFill.style.height = Length.Percent(Player.DashCooldown01 * 100);

            var boss = Run.ActiveBoss;
            _bossBox.style.display = boss.Active ? DisplayStyle.Flex : DisplayStyle.None;
            if (boss.Active)
            {
                _bossName.text = boss.Title + (boss.Exposed ? "  -  WEAK POINT OPEN: BITE!" : boss.Phase == 2 ? "  -  ENRAGED" : "");
                _bossName.style.color = boss.Exposed ? Palette.Accent : Palette.Hex(0xFF9AE0);
                _bossFill.style.width = Length.Percent(boss.Hp / boss.MaxHp * 100);
            }
            // town: alert pips and the district name
            bool town = Run.Stage == 2;
            _alertBox.style.display = town ? DisplayStyle.Flex : DisplayStyle.None;
            if (town)
            {
                int stars = Run.Military.Stars;
                for (int i = 0; i < 5; i++) _pips[i].style.backgroundColor = i < stars ? Palette.Danger : new Color(1, 1, 1, 0.15f);
                _alertText.text = stars == 0 ? "UNNOTICED" : Run.Military.Quiet > 10 ? "COOLING" : "ALERT";
                _zoneText.text = Run.Town.ZoneName(Player.transform.position).ToUpperInvariant();
            }

            // joystick
            var ir = InputRouter.I;
            if (_root.panel != null)
            {
                bool active = ir.StickActive;
                float pk = _root.layout.height > 0 ? _root.layout.height / Screen.height : 1;
                float rad = ir.StickRadius * pk, knob = rad * 0.42f;
                var o = ToPanel(active ? ir.StickOrigin : ir.FixedCenter);
                // the knob follows the finger, travelling up to three quarters of the ring so it always stays visible inside it
                var kp = active ? ToPanel(ir.StickOrigin + Vector2.ClampMagnitude(ir.StickPos - ir.StickOrigin, ir.StickRadius * 0.75f)) : o;
                _stick.style.width = rad * 2; _stick.style.height = rad * 2; Radius(_stick, rad);
                _knob.style.width = knob * 2; _knob.style.height = knob * 2; Radius(_knob, knob);
                _stick.style.left = o.x - rad; _stick.style.top = o.y - rad;
                _knob.style.left = kp.x - knob; _knob.style.top = kp.y - knob;
                _stick.style.opacity = active ? 1f : 0.55f;
                _knob.style.opacity = active ? 1f : 0.6f;
                // keep the hint fully inside the safe area (it was clipped at the left edge on narrow screens)
                float maxLeft = Mathf.Max(8, _safe.layout.width - 188);
                _stickHint.style.left = Mathf.Clamp(o.x - 90, 8, maxLeft); _stickHint.style.top = Mathf.Max(8, o.y - rad - 30);
                if (active || Player.Velocity.sqrMagnitude > 1f) _hintSeen += Time.unscaledDeltaTime;
                _stickHint.style.display = _hintSeen < 1.5f ? DisplayStyle.Flex : DisplayStyle.None;
            }

            // rare-prey compass (Stalk Eyes)
            var rare = g.Level("eyes") > 0 ? Eco.FindRare() : null;
            _rareArrow.style.display = DisplayStyle.None;
            if (rare != null && Rig.Cam != null)
            {
                var sp = Rig.Cam.WorldToScreenPoint(rare.Pos);
                bool onScreen = sp.z > 0 && sp.x > 0 && sp.x < Screen.width && sp.y > 0 && sp.y < Screen.height;
                if (!onScreen)
                {
                    var c = new Vector2(Screen.width / 2f, Screen.height / 2f);
                    var dir = ((Vector2)sp - c); if (sp.z < 0) dir = -dir;
                    dir = dir.normalized;
                    var edge = c + dir * Mathf.Min(Screen.width, Screen.height) * 0.42f;
                    var pp = ToPanel(edge);
                    _rareArrow.style.left = pp.x; _rareArrow.style.top = pp.y;
                    _rareArrow.style.display = DisplayStyle.Flex;
                }
            }
        }

        Vector2 ToPanel(Vector2 screen)
        {
            var p = RuntimePanelUtils.ScreenToPanel(_root.panel, new Vector2(screen.x, Screen.height - screen.y));
            return p - new Vector2(_safe.layout.x, _safe.layout.y);
        }

        // ------------------------------------------------------------------ titles, toasts, flash

        public void BigTitle(string title, string sub, float duration)
        {
            _bigTitle.text = title; _bigSub.text = sub;
            _titleT = 0; _titleDur = duration;
        }

        public void BossIntro(string name, string hint)
        {
            BigTitle(name, hint, 4f);
            _bigTitle.style.color = Palette.Hex(0xFF9AE0);
            Flash(Palette.Hex(0xFF6AD5), 0.5f);
            _hud.style.display = DisplayStyle.Flex;
        }

        public void Flash(Color c, float a)
        {
            var settings = Services.Save.settings;
            if (settings.reducedFlashing) a *= 0.3f;
            _flashC = c; _flashA = Mathf.Max(_flashA, a);
        }

        sealed class ToastItem { public VisualElement el; public float t; }
        readonly List<ToastItem> _toastList = new List<ToastItem>();

        public void Toast(string title, string sub, Color c)
        {
            var p = Box(); p.pickingMode = PickingMode.Ignore;
            p.style.backgroundColor = Palette.UiPanel; Radius(p, 14); Border(p, c, 2);
            p.style.paddingLeft = 18; p.style.paddingRight = 18; p.style.paddingTop = 8; p.style.paddingBottom = 8; p.style.marginBottom = 6;
            p.style.alignItems = Align.Center;
            p.Add(Text(title, 20, c, true, TextAnchor.MiddleCenter));
            if (!string.IsNullOrEmpty(sub)) p.Add(Text(sub, 15, Palette.UiDim, false, TextAnchor.MiddleCenter));
            _toasts.Add(p);
            _toastList.Add(new ToastItem { el = p });
            if (_toastList.Count > 3) { _toastList[0].el.RemoveFromHierarchy(); _toastList.RemoveAt(0); }
        }

        void UpdateToasts(float dt)
        {
            if (_bigTitle.style.color != Palette.Ui && _titleDur <= 0) _bigTitle.style.color = Palette.Ui;
            for (int i = _toastList.Count - 1; i >= 0; i--)
            {
                var t = _toastList[i];
                t.t += dt;
                t.el.style.opacity = t.t < 0.2f ? t.t / 0.2f : t.t > 2.6f ? Mathf.Max(0, (3.1f - t.t) / 0.5f) : 1;
                t.el.style.translate = new Translate(0, Mathf.Lerp(-20, 0, Mathf.Clamp01(t.t / 0.25f)));
                if (t.t > 3.1f) { t.el.RemoveFromHierarchy(); _toastList.RemoveAt(i); }
            }
        }

        float _photoT;

        void TogglePhoto(bool on)
        {
            _photo = on;
            _photoT = 0;
            _safe.style.display = on ? DisplayStyle.None : DisplayStyle.Flex;
            if (on)
            {
                _photoScale = Time.timeScale;
                Time.timeScale = 0;
                if (Application.platform != RuntimePlatform.WebGLPlayer) StartCoroutine(CaptureNextFrame());
            }
            else Time.timeScale = _photoScale;
        }
        float _photoScale = 1;

        System.Collections.IEnumerator CaptureNextFrame()
        {
            yield return null; yield return new WaitForEndOfFrame();
            string path = System.IO.Path.Combine(Application.persistentDataPath, $"omnivex_{System.DateTime.Now:yyyyMMdd_HHmmss}.png");
            ScreenCapture.CaptureScreenshot(path);
            Services.Analytics.Track("photo_capture");
            _savedPhoto = true;
        }
        bool _savedPhoto;
    }
}
