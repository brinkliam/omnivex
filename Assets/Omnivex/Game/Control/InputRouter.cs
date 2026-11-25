using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace OMX.Game
{
    /// <summary>
    /// One steering model for every device: a floating joystick that appears wherever you press (touch or mouse),
    /// plus WASD/arrows and a gamepad stick. Dash: Space, gamepad South, a second finger, or the HUD button.
    /// </summary>
    public sealed class InputRouter : MonoBehaviour
    {
        public static InputRouter I { get; private set; }
        public Vector2 Move { get; private set; }
        public bool StickActive { get; private set; }
        public Vector2 StickOrigin { get; private set; }
        public Vector2 StickPos { get; private set; }
        public bool Enabled = true;
        public Func<Vector2, bool> BlockedByUi;
        public event Action OnDash;
        public event Action OnPause;

        int _touchId = -1;
        readonly System.Collections.Generic.HashSet<int> _knownTouches = new System.Collections.Generic.HashSet<int>();
        bool _mouseDown;
        public float StickRadius => Mathf.Clamp(Screen.dpi > 0 ? Screen.dpi * 0.5f : Mathf.Min(Screen.width, Screen.height) * 0.11f, 56f, 220f);

        /// <summary>The always-visible thumbstick in the lower-left corner (screen pixels). Pressing near it uses it as a
        /// fixed stick; pressing anywhere else spawns a floating stick under the finger.</summary>
        public Vector2 FixedCenter
        {
            get
            {
                var sa = Screen.safeArea;
                float r = StickRadius;
                float pad = Mathf.Max(16f, Mathf.Min(Screen.width, Screen.height) * 0.03f);
                return new Vector2(sa.xMin + r * 1.55f + pad, sa.yMin + r * 1.55f + pad);
            }
        }
        public bool UsingFixed { get; private set; }

        Vector2 BeginStick(Vector2 pos)
        {
            UsingFixed = (pos - FixedCenter).magnitude < StickRadius * 1.5f;
            return UsingFixed ? FixedCenter : pos;
        }

        void Awake() => I = this;

        public void RequestDash() { if (Enabled) OnDash?.Invoke(); }

        void Update()
        {
            Vector2 move = Vector2.zero;
            bool stick = false;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1;
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1;
                if (Enabled && (kb.spaceKey.wasPressedThisFrame || kb.leftShiftKey.wasPressedThisFrame)) OnDash?.Invoke();
                if (kb.escapeKey.wasPressedThisFrame || kb.pKey.wasPressedThisFrame) OnPause?.Invoke();
            }
            var gp = Gamepad.current;
            if (gp != null)
            {
                var s = gp.leftStick.ReadValue();
                if (s.sqrMagnitude > 0.04f) move += s;
                if (Enabled && gp.buttonSouth.wasPressedThisFrame) OnDash?.Invoke();
                if (gp.startButton.wasPressedThisFrame) OnPause?.Invoke();
            }

            var ts = Touchscreen.current;
            bool anyTouch = false;
            if (ts != null)
            {
                foreach (var t in ts.touches)
                {
                    var phase = t.phase.ReadValue();
                    int id = t.touchId.ReadValue();
                    bool down = phase == UnityEngine.InputSystem.TouchPhase.Began || phase == UnityEngine.InputSystem.TouchPhase.Moved || phase == UnityEngine.InputSystem.TouchPhase.Stationary;
                    if (!down)
                    {
                        if (id != 0) _knownTouches.Remove(id);
                        if (id == _touchId) _touchId = -1;
                        continue;
                    }
                    anyTouch = true;
                    // A finger we haven't seen yet is a new touch, even if its first sampled phase is already Moved
                    // (a fast touch-and-drag can begin and move inside one frame). Decide its role from where it started.
                    if (_knownTouches.Add(id))
                    {
                        var start = t.startPosition.ReadValue();
                        if (BlockedByUi != null && BlockedByUi(start)) continue;   // buttons handle their own touches
                        if (IsEdge(start)) continue;                                // palm or grip on the bezel: ignore it
                        if (_touchId < 0 && InStickZone(start)) { _touchId = id; StickOrigin = BeginStick(start); }
                        else if (Enabled) OnDash?.Invoke();                         // another finger (e.g. the right thumb) dashes
                    }
                    if (id == _touchId) { StickPos = t.position.ReadValue(); stick = true; }
                }
                if (!anyTouch) { _touchId = -1; _knownTouches.Clear(); }
            }
            if (!anyTouch)
            {
                var m = Mouse.current;
                if (m != null)
                {
                    var pos = m.position.ReadValue();
                    if (m.leftButton.wasPressedThisFrame && !(BlockedByUi != null && BlockedByUi(pos))) { _mouseDown = true; StickOrigin = BeginStick(pos); }
                    if (!m.leftButton.isPressed) _mouseDown = false;
                    if (_mouseDown) { StickPos = pos; stick = true; }
                    if (Enabled && m.rightButton.wasPressedThisFrame) OnDash?.Invoke();
                }
            }

            if (stick)
            {
                var d = StickPos - StickOrigin;
                float r = StickRadius;
                if (d.magnitude > r && !UsingFixed) StickOrigin = StickPos - d.normalized * r;   // a floating base trails the finger
                var v = (StickPos - StickOrigin) / r;
                if (v.magnitude > 0.12f) move = v;
            }
            StickActive = stick && Enabled;
            Move = Enabled ? Vector2.ClampMagnitude(move, 1f) : Vector2.zero;
        }

        /// <summary>Touches starting this close to the screen border are treated as a resting palm and ignored.</summary>
        static bool IsEdge(Vector2 p)
        {
            float m = Mathf.Max(12f, Mathf.Min(Screen.width, Screen.height) * 0.035f);
            return p.x < m || p.y < m || p.x > Screen.width - m || p.y > Screen.height - m;
        }

        /// <summary>The fixed stick, or anywhere in the left 60% of the screen (where a floating stick may start).</summary>
        bool InStickZone(Vector2 p) => (p - FixedCenter).magnitude < StickRadius * 1.6f || p.x < Screen.width * 0.6f;
    }
}
