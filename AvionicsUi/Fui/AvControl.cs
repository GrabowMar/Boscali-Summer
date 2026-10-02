using System;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace NOAvionics
{
    /// <summary>Kit v2 button: five styles × six states, painted from avionics.fui.avss, with an optional icon.</summary>
    public sealed class AvControl : MonoBehaviour
    {
        public struct Spec
        {
            public string Label; public AvIcon Icon; public AvButtonStyle Style; public Action OnClick; public bool IconRight;
            public Spec(string label, Action onClick, AvButtonStyle style = AvButtonStyle.Default, AvIcon icon = AvIcon.None, bool iconRight = false)
            { Label = label; OnClick = onClick; Style = style; Icon = icon; IconRight = iconRight; }
        }

        private AvFrame frame;
        private UnityEngine.UI.Image rail;
        private TMP_Text text, glyph;
        private AvFx fx;
        private string classes;
        private bool hover, pressed, latched, armed, interactable = true, iconRight, iconOnly;

        public RectTransform Rect => (RectTransform)transform;
        public event Action Clicked;

        public static AvControl Make(RectTransform parent, Spec spec, string cssClass = "control")
        {
            RectTransform root = AvLay.Child(parent, "Control " + spec.Label);
            var c = root.gameObject.AddComponent<AvControl>();
            c.classes = cssClass + (spec.Style == AvButtonStyle.Primary ? " primary" : spec.Style == AvButtonStyle.Quiet ? " quiet"
                : spec.Style == AvButtonStyle.Danger ? " danger" : "");
            c.frame = AvFrame.Add(root, "Frame", AvChamfer.Diagonal(4f));
            c.frame.Bracket = 4f;
            AvLay.Fill(c.frame.rectTransform);
            c.rail = AvLay.Solid(root, "Rail", Color.clear);
            c.rail.rectTransform.anchorMin = new Vector2(0f, 0f); c.rail.rectTransform.anchorMax = new Vector2(1f, 0f);
            c.rail.rectTransform.pivot = new Vector2(0.5f, 0f); c.rail.rectTransform.sizeDelta = new Vector2(0f, 2f);
            c.rail.rectTransform.anchoredPosition = Vector2.zero;
            if (spec.Icon != AvIcon.None)
            {
                c.glyph = AvIcons.Make(root, spec.Icon, AvGridTokens.IconInline, Color.white);
                c.iconRight = spec.IconRight && !string.IsNullOrEmpty(spec.Label);
                c.iconOnly = string.IsNullOrEmpty(spec.Label);
                var g = c.glyph.rectTransform;
                g.anchorMin = g.anchorMax = c.iconOnly ? new Vector2(0.5f, 0.5f) : c.iconRight ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
                g.pivot = c.iconOnly ? new Vector2(0.5f, 0.5f) : c.iconRight ? new Vector2(1f, 0.5f) : new Vector2(0f, 0.5f);
                g.sizeDelta = new Vector2(16f, 16f);
                g.anchoredPosition = c.iconOnly ? Vector2.zero : c.iconRight ? new Vector2(-8f, 0f) : new Vector2(8f, 0f);
            }
            c.text = AvText.Make(root, "Label", AvTextRole.Head, spec.Label, TextAlignmentOptions.Center);
            AvText.Fit(c.text, true); // long labels shrink, then wrap, never spill into a neighbour
            AvLay.Fill(c.text.rectTransform);
            if (c.glyph != null && !c.iconOnly) { if (c.iconRight) c.text.rectTransform.offsetMax = new Vector2(-24f, 0f); else c.text.rectTransform.offsetMin = new Vector2(24f, 0f); }
            if (spec.OnClick != null) c.Clicked += spec.OnClick;
            AvHit hit = AvHit.On(c.frame);
            hit.Hover = h => { c.hover = h; if (h && c.interactable) c.fx?.Play(AvFxKind.Shine, 0.15f); c.Restyle(); };
            hit.Press = p => { c.pressed = p; c.Restyle(); };
            hit.Click = e => { if (e.button == PointerEventData.InputButton.Left) c.Clicked?.Invoke(); };
            c.fx = AvFx.On(c.frame);
            c.Restyle();
            return c;
        }

        public bool Latched { get => latched; set { if (latched == value) return; latched = value; if (value) fx?.Play(AvFxKind.Shine, 0.2f); Restyle(); } }
        public bool Armed { get => armed; set { if (armed == value) return; armed = value; fx?.Set(value ? AvFxKind.Glow : AvFxKind.None, 0.5f, 10f); Restyle(); } }
        public bool Interactable { get => interactable; set { interactable = value; frame.GetComponent<AvHit>().Interactable = value; Restyle(); } }
        public string Label { get => text.text; set => text.text = value ?? ""; }

        /// <summary>Swap the button's icon after build (play/pause); no-op when the button has none.</summary>
        public void SetIcon(AvIcon icon) { if (glyph != null && icon != AvIcon.None) { AvIcons.Set(glyph, icon, AvGridTokens.IconInline); glyph.color = text.color; } }

        /// <summary>Label shrinks toward the floor but never wraps (tabs, where a break would split a word).</summary>
        public void SingleLine() => AvText.Fit(text, false);

        /// <summary>Hover help shown in the console footer (null/empty = none).</summary>
        public string Help { get => tip != null ? tip.Text : null; set => tip = AvHelpTip.Attach(frame.gameObject, value); }
        private AvHelpTip tip;

        /// <summary>Height the label needs at this width when it wraps at full size (the row grows rather than spill).</summary>
        public float PreferredHeight(float width) =>
            AvText.Height(text, Mathf.Max(1f, width - (glyph != null ? 24f : 0f) - 8f)) + 10f;

        public void Restyle()
        {
            string state = !interactable ? "disabled" : pressed ? "pressed" : hover ? "hover" : armed ? "armed" : latched ? "latched" : null;
            AvStyle s = AvStyleHost.FuiStyle(classes, state);
            frame.Paint(AvStyleHost.Resolve(s.Background, AvTheme.Surface), AvStyleHost.Resolve(s.Border, AvTheme.Frame));
            Color ink = AvStyleHost.Resolve(s.Color, AvTheme.TextPrimary);
            text.color = ink;
            frame.BracketColor = ink.WithAlpha(latched || armed || hover ? 0.72f : 0.18f);
            frame.SetVerticesDirty();
            if (glyph != null) glyph.color = ink;
            rail.color = s.Rail.HasValue ? AvStyleHost.Resolve(s.Rail, Color.clear) : Color.clear;
        }
    }
}
