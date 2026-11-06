using System;
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
        private string classes, baseClass;
        private bool hover, pressed, latched, armed, interactable = true, iconRight, iconOnly;
        private Color? accent;

        public RectTransform Rect => (RectTransform)transform;
        public event Action Clicked;

        public static AvControl Make(RectTransform parent, Spec spec, string cssClass = "control")
        {
            RectTransform root = AvLay.Child(parent, "Control " + spec.Label);
            var c = root.gameObject.AddComponent<AvControl>();
            c.baseClass = cssClass;
            c.classes = ClassesFor(cssClass, spec.Style);
            c.frame = AvFrame.Add(root, "Frame", AvChamfer.Diagonal(4f));
            c.frame.Bracket = 6f;
            AvLay.Fill(c.frame.rectTransform);
            c.rail = AvLay.Solid(root, "Rail", Color.clear);
            c.rail.rectTransform.anchorMin = new Vector2(0f, 0f); c.rail.rectTransform.anchorMax = new Vector2(1f, 0f);
            // The rail starts after the bottom-left cut, so it never pokes out past the chamfer.
            c.rail.rectTransform.pivot = new Vector2(0.5f, 0f); c.rail.rectTransform.sizeDelta = new Vector2(-5f, 2f);
            c.rail.rectTransform.anchoredPosition = new Vector2(2.5f, 0f);
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
            c.text.rectTransform.offsetMin = new Vector2(8f, 0f);
            c.text.rectTransform.offsetMax = new Vector2(-8f, 0f);
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

        private static string ClassesFor(string cssClass, AvButtonStyle style) => cssClass + (style == AvButtonStyle.Primary ? " primary" : style == AvButtonStyle.Quiet ? " quiet"
            : style == AvButtonStyle.Danger ? " danger" : "");

        /// <summary>Swap the button's style after build (a row's one action changes AUTHORIZE to EXECUTE to DENIED).</summary>
        public void SetStyle(AvButtonStyle style)
        {
            string next = ClassesFor(baseClass, style);
            if (next == classes) return;
            classes = next;
            Restyle();
        }

        public bool Latched { get => latched; set { if (latched == value) return; latched = value; if (value) fx?.Play(AvFxKind.Shine, 0.2f); Restyle(); } }
        public bool Armed { get => armed; set { if (armed == value) return; armed = value; fx?.Set(value ? AvFxKind.Glow : AvFxKind.None, 0.5f, 10f); Restyle(); } }
        public bool Interactable { get => interactable; set { if (interactable == value) return; interactable = value; if (!value) pressed = false; frame.GetComponent<AvHit>().Interactable = value; Restyle(); } }
        public string Label { get => text.text; set => AvText.Set(text, value); }

        /// <summary>
        /// A console's own accent over the scheme (the OPS rooms are different consoles): a primary control fills with it, a latched one
        /// takes its wash, edge and rail, the rest keep a hairline of it. A danger control keeps its own red. Null = the scheme's look.
        /// </summary>
        public Color? Accent { get => accent; set { accent = value; Restyle(); } }

        /// <summary>Swap the button's icon after build (play/pause); no-op when the button has none.</summary>
        public void SetIcon(AvIcon icon) { if (glyph != null && icon != AvIcon.None) { AvIcons.Set(glyph, icon, AvGridTokens.IconInline); glyph.color = text.color; } }

        /// <summary>Label shrinks toward the floor but never wraps (tabs, where a break would split a word).</summary>
        public void SingleLine() => AvText.Fit(text, false);

        /// <summary>Hover help shown in the console footer (null/empty = none).</summary>
        public string Help { get => tip != null ? tip.Text : null; set => tip = AvHelpTip.Attach(frame.gameObject, value, text); }
        private AvHelpTip tip;

        /// <summary>Height the label needs at this width when it wraps at full size (the row grows rather than spill).</summary>
        public float PreferredHeight(float width) =>
            AvText.Height(text, Mathf.Max(1f, width - (glyph != null && !iconOnly ? 24f : 8f) - 8f)) + 10f;

        // TMP preferred values already use fontSizeMax when autosizing is enabled.
        internal float PreferredWidth => AvText.Width(text) + (glyph != null && !iconOnly ? 32f : 16f);

        public void Restyle()
        {
            string state = !interactable ? "disabled" : pressed ? "pressed" : armed ? "armed" : latched ? "latched" : hover ? "hover" : null;
            AvStyle s = AvStyleHost.FuiStyle(classes, state);
            Color back = AvStyleHost.Resolve(s.Background, AvTheme.Surface), edge = AvStyleHost.Resolve(s.Border, AvTheme.Frame);
            Color ink = AvStyleHost.Resolve(s.Color, AvTheme.TextPrimary);
            // A read-only choice still has to say which option is set: the latched look, held back, instead of the plain disabled one
            // (which made every host-only or built-in setting read as "nothing selected").
            if (!interactable && latched)
            {
                s = AvStyleHost.FuiStyle(classes, "latched");
                Color on = AvStyleHost.Resolve(s.Background, AvTheme.Surface);
                back = on.WithAlpha(on.a * 0.7f);
                edge = AvStyleHost.Resolve(s.Border, AvTheme.Frame).WithAlpha(0.75f);
                ink = AvStyleHost.Resolve(s.Color, AvTheme.TextPrimary).WithAlpha(0.82f);
            }
            if (interactable && hover && !pressed && (latched || armed)) edge = Color.Lerp(edge, ink, 0.35f);
            Color? railInk = null;
            if (accent.HasValue && interactable && !classes.Contains("danger"))
            {
                Color a = accent.Value;
                if (classes.Contains("primary")) { back = a.WithAlpha(hover || pressed ? 0.95f : 0.82f); edge = a; ink = Color.Lerp(Color.black, a, 0.1f); }
                else if (latched || armed || pressed) { back = a.WithAlpha(0.18f); edge = a; ink = Color.Lerp(Color.white, a, 0.25f); railInk = a; }
                else if (hover) { back = a.WithAlpha(0.1f); edge = a.WithAlpha(0.8f); ink = Color.white; }
                else edge = a.WithAlpha(0.35f);
            }
            frame.Paint(back, edge);
            text.color = ink;
            Color bracket = (accent ?? ink).WithAlpha(interactable && hover ? 1f : latched || armed ? 0.85f : interactable ? 0.42f : 0.16f);
            if (bracket != frame.BracketColor) { frame.BracketColor = bracket; frame.SetVerticesDirty(); }
            if (glyph != null) glyph.color = ink;
            rail.color = railInk ?? (s.Rail.HasValue ? AvStyleHost.Resolve(s.Rail, Color.clear) : Color.clear);
        }
    }
}
