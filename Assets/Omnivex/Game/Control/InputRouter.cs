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
        bool _mouseDown;
        float StickRadius => Mathf.Max(60f, Screen.dpi > 0 ? Screen.dpi * 0.42f : 70f);

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
            if (ts != null && ts.touches.Count > 0)
            {
                bool found = false;
                foreach (var t in ts.touches)
                {
                    var phase = t.phase.ReadValue();
                    int id = t.touchId.ReadValue();
                    var pos = t.position.ReadValue();
                    if (phase == UnityEngine.InputSystem.TouchPhase.Began)
                    {
                        if (BlockedByUi != null && BlockedByUi(pos)) continue;
                        if (_touchId < 0) { _touchId = id; StickOrigin = pos; }
                        else if (id != _touchId && Enabled) OnDash?.Invoke();   // a second finger tap dashes
                    }
                    if (id == _touchId)
                    {
                        if (phase == UnityEngine.InputSystem.TouchPhase.Ended || phase == UnityEngine.InputSystem.TouchPhase.Canceled) { _touchId = -1; continue; }
                        found = true; StickPos = pos; stick = true;
                    }
                }
                if (!found && _touchId >= 0 && !AnyTouchWithId(ts, _touchId)) _touchId = -1;
            }
            else
            {
                var m = Mouse.current;
                if (m != null)
                {
                    var pos = m.position.ReadValue();
                    if (m.leftButton.wasPressedThisFrame && !(BlockedByUi != null && BlockedByUi(pos))) { _mouseDown = true; StickOrigin = pos; }
                    if (!m.leftButton.isPressed) _mouseDown = false;
                    if (_mouseDown) { StickPos = pos; stick = true; }
                    if (Enabled && m.rightButton.wasPressedThisFrame) OnDash?.Invoke();
                }
            }

            if (stick)
            {
                var d = StickPos - StickOrigin;
                float r = StickRadius;
                if (d.magnitude > r) StickOrigin = StickPos - d.normalized * r;   // the base trails the finger
                var v = (StickPos - StickOrigin) / r;
                if (v.magnitude > 0.12f) move = v;
            }
            StickActive = stick && Enabled;
            Move = Enabled ? Vector2.ClampMagnitude(move, 1f) : Vector2.zero;
        }

        static bool AnyTouchWithId(Touchscreen ts, int id)
        {
            foreach (var t in ts.touches)
            {
                var ph = t.phase.ReadValue();
                if (t.touchId.ReadValue() == id && ph != UnityEngine.InputSystem.TouchPhase.None && ph != UnityEngine.InputSystem.TouchPhase.Ended && ph != UnityEngine.InputSystem.TouchPhase.Canceled) return true;
            }
            return false;
        }
    }
}
