using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace OMX.Game
{
    /// <summary>Tiny builder helpers so screens read like layout, not boilerplate. Every colour comes from Palette.</summary>
    public static class UIKit
    {
        public static AudioDirector Audio;

        public static VisualElement Box(bool row = false, string name = null)
        {
            var v = new VisualElement { name = name };
            v.style.flexDirection = row ? FlexDirection.Row : FlexDirection.Column;
            return v;
        }

        public static VisualElement Panel(float pad = 22)
        {
            var v = Box();
            v.style.backgroundColor = Palette.UiPanel;
            Radius(v, 18);
            Border(v, new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.35f), 1.5f);
            Pad(v, pad);
            return v;
        }

        public static Label Text(string s, float size, Color c, bool bold = false, TextAnchor align = TextAnchor.MiddleLeft)
        {
            var l = new Label(s);
            l.style.fontSize = size; l.style.color = c;
            l.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            l.style.unityTextAlign = align;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginBottom = 0; l.style.marginTop = 0; l.style.paddingLeft = 0; l.style.paddingRight = 0;
            l.pickingMode = PickingMode.Ignore;
            return l;
        }

        public static Button Btn(string text, Action onClick, bool primary = false, float size = 22, float minWidth = 0)
        {
            var b = new Button(() => { Audio?.Play("click", 0.8f, primary ? 1.1f : 1f); onClick?.Invoke(); }) { text = text };
            b.style.fontSize = size;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.color = primary ? Palette.Abyss : Palette.Ui;
            b.style.backgroundColor = primary ? Palette.Accent : new Color(0.08f, 0.22f, 0.25f, 0.95f);
            Radius(b, 14);
            Border(b, primary ? Palette.Hex(0xA8FFF2) : new Color(Palette.Accent.r, Palette.Accent.g, Palette.Accent.b, 0.4f), 1.5f);
            b.style.paddingTop = 12; b.style.paddingBottom = 12; b.style.paddingLeft = 22; b.style.paddingRight = 22;
            b.style.marginTop = 6; b.style.marginBottom = 6; b.style.marginLeft = 4; b.style.marginRight = 4;
            if (minWidth > 0) b.style.minWidth = minWidth;
            b.RegisterCallback<PointerEnterEvent>(_ => { b.style.scale = new Scale(new Vector3(1.04f, 1.04f, 1)); Audio?.Play("hover", 0.5f); });
            b.RegisterCallback<PointerLeaveEvent>(_ => b.style.scale = new Scale(Vector3.one));
            return b;
        }

        public static void SetBtnEnabled(Button b, bool on)
        {
            b.SetEnabled(on);
            b.style.opacity = on ? 1f : 0.45f;
        }

        public static VisualElement Bar(Color fill, float height, out VisualElement fillEl)
        {
            var bg = Box();
            bg.style.height = height;
            bg.style.backgroundColor = new Color(0, 0, 0, 0.45f);
            Radius(bg, height / 2);
            bg.style.overflow = Overflow.Hidden;
            fillEl = Box();
            fillEl.style.height = Length.Percent(100);
            fillEl.style.width = Length.Percent(50);
            fillEl.style.backgroundColor = fill;
            Radius(fillEl, height / 2);
            bg.Add(fillEl);
            bg.pickingMode = PickingMode.Ignore; fillEl.pickingMode = PickingMode.Ignore;
            return bg;
        }

        public static void Radius(VisualElement v, float r)
        {
            v.style.borderTopLeftRadius = r; v.style.borderTopRightRadius = r; v.style.borderBottomLeftRadius = r; v.style.borderBottomRightRadius = r;
        }

        public static void Border(VisualElement v, Color c, float w)
        {
            v.style.borderTopColor = c; v.style.borderBottomColor = c; v.style.borderLeftColor = c; v.style.borderRightColor = c;
            v.style.borderTopWidth = w; v.style.borderBottomWidth = w; v.style.borderLeftWidth = w; v.style.borderRightWidth = w;
        }

        public static void Pad(VisualElement v, float p)
        {
            v.style.paddingTop = p; v.style.paddingBottom = p; v.style.paddingLeft = p; v.style.paddingRight = p;
        }

        public static void Fill(VisualElement v)
        {
            v.style.position = Position.Absolute;
            v.style.left = 0; v.style.right = 0; v.style.top = 0; v.style.bottom = 0;
        }

        public static VisualElement Centered()
        {
            var v = Box(); Fill(v);
            v.style.alignItems = Align.Center; v.style.justifyContent = Justify.Center;
            return v;
        }

        public static VisualElement Spacer(float h) { var v = Box(); v.style.height = h; v.style.width = h; v.pickingMode = PickingMode.Ignore; return v; }

        public static VisualElement Chip(string text, Color c, float size = 14)
        {
            var v = Box(true);
            v.style.backgroundColor = new Color(c.r, c.g, c.b, 0.22f);
            Border(v, c, 1);
            Radius(v, 10);
            v.style.paddingLeft = 8; v.style.paddingRight = 8; v.style.paddingTop = 2; v.style.paddingBottom = 2;
            v.style.alignSelf = Align.FlexStart;
            v.Add(Text(text, size, c, true));
            v.pickingMode = PickingMode.Ignore;
            return v;
        }
    }
}
