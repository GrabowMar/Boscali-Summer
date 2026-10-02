using NOAvionics;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// Colours for the OPS hero parts, always read from the live style sheet roles (never a literal),
    /// so the three themes and the game accent restyle them with the rest of the console.
    /// </summary>
    internal static class OpsInk
    {
        public static Color Role(string role, Color fallback) => AvStyleHost.FuiColor(role, fallback);

        /// <summary>The rail colour a chip/row of this state wears (the state's own hue).</summary>
        public static Color Rail(AvState state) =>
            AvStyleHost.Resolve(AvStyleHost.FuiStyle("chip " + AvStates.Class(state)).Rail, AvTheme.RailInert);

        /// <summary>The text colour for a word of this state (inert words read as plain ink).</summary>
        public static Color Word(AvState state) => state == AvState.Inert
            ? AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary)
            : AvStyleHost.Resolve(AvStyleHost.FuiStyle("chip " + AvStates.Class(state)).Color, AvTheme.TextPrimary);

        public static Color Ink => AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-name").Color, AvTheme.TextPrimary);
        public static Color Dim => AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
        public static Color Muted => Role("ink-muted", AvTheme.Disabled);
        public static Color Hairline => Role("hairline", AvTheme.Hairline);
        public static Color Frame => Role("frame", AvTheme.Frame);
        public static Color Key => Role("key", AvTheme.RailInfo);
        public static Color Select => Role("select", AvTheme.Accent);
        public static Color Sunken => AvStyleHost.Resolve(AvStyleHost.FuiStyle("header").Background, AvTheme.Surface);
        public static Color Inert => AvStyleHost.Resolve(AvStyleHost.FuiStyle("card inert").Background, AvTheme.SurfaceInert);

        public static Color A(Color c, float alpha) => c.WithAlpha(alpha);
    }

    /// <summary>Text helpers shared by the hero parts.</summary>
    internal static class OpsText
    {
        /// <summary>A single-line label that shrinks toward the 11 px floor instead of spilling.</summary>
        public static TMP_Text Line(RectTransform parent, string name, AvTextRole role, TextAlignmentOptions align)
        {
            TMP_Text t = AvText.Make(parent, name, role, "", align, false);
            AvText.Fit(t, false);
            return t;
        }

        /// <summary>A wrapped block that the owning part measures with <see cref="AvText.Height"/>.</summary>
        public static TMP_Text Block(RectTransform parent, string name, AvTextRole role)
        {
            return AvText.Make(parent, name, role, "", TextAlignmentOptions.TopLeft, true);
        }

        /// <summary>Set text only when it changed; returns true when it did.</summary>
        public static bool Set(TMP_Text t, string value)
        {
            string v = value ?? "";
            if (t.text == v) return false;
            t.text = v;
            return true;
        }

        public static void Place(TMP_Text t, float x, float y, float w, float h) => AvLay.Place(t.rectTransform, x, y, w, h);
    }

    /// <summary>
    /// A compact banner: a state rail, one headline word-line and an optional wrapped body, with an
    /// optional control on the right (ABORT while armed). The one empty-state card and the one
    /// advice line of a page. Height follows the wrapped text.
    /// </summary>
    internal sealed class BriefCard : AvPart
    {
        private const float PadX = 12f, PadY = 5f, ControlW = 92f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text head, body;
        private AvControl control;
        private AvState state = AvState.Inert;

        public BriefCard(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Brief");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            head = OpsText.Block(Rect, "Head", AvTextRole.Label);
            body = OpsText.Block(Rect, "Body", AvTextRole.ProseSmall);
            Restyle();
        }

        public AvControl AddControl(AvControl.Spec spec)
        {
            control = AvControl.Make(Rect, spec);
            control.Rect.gameObject.SetActive(false);
            return control;
        }

        public AvControl Control => control;

        public void ShowControl(bool shown)
        {
            if (control == null || control.Rect.gameObject.activeSelf == shown) return;
            control.Rect.gameObject.SetActive(shown);
            Changed();
        }

        public void Set(string headline, string text, AvState tone)
        {
            bool grew = OpsText.Set(head, headline);
            grew |= OpsText.Set(body, text);
            if (tone != state) { state = tone; Restyle(); }
            if (grew) Changed();
        }

        private bool ControlOn => control != null && control.Rect.gameObject.activeSelf;
        private float TextW(float w) => w - PadX - 10f - (ControlOn ? ControlW + 8f : 0f);

        public override float Measure(float width)
        {
            float w = TextW(width);
            float h = PadY + AvText.Height(head, w) + (body.text.Length > 0 ? 2f + AvText.Height(body, w) : 0f) + PadY;
            return Mathf.Max(ControlOn ? 34f : 28f, h);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = TextW(s.W), hh = AvText.Height(head, w);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            OpsText.Place(head, PadX, PadY, w, hh);
            OpsText.Place(body, PadX, PadY + hh + 2f, w, body.text.Length > 0 ? AvText.Height(body, w) : 0f);
            if (control != null) AvLay.Place(control.Rect, s.W - ControlW - 8f, (s.H - 26f) * 0.5f, ControlW, 26f);
        }

        public override void Restyle()
        {
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(state));
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert), Color.clear);
            rail.color = OpsInk.Rail(state);
            head.color = state == AvState.Inert ? OpsInk.Ink : OpsInk.Word(state);
            body.color = OpsInk.Dim;
            control?.Restyle();
        }
    }

    /// <summary>
    /// The one ACTIONS-page unit: an icon plate, the ability's name, the reason or readiness in words,
    /// the cost in mono with its unit and up to two controls. Replaces the plain list row so every
    /// ACTIONS page reads as the same set of tiles. Height follows the wrapped text.
    /// </summary>
    internal sealed class ActionTile : AvPart
    {
        private const float PadX = 8f, PadY = 4f, Plate = 30f, ValueW = 62f, TrailW = 58f, TrailH = 24f, Min = 38f;
        private readonly AvFrame frame, plate;
        private readonly Image rail;
        private readonly TMP_Text icon, name, sub, value, unit;
        private readonly List<AvControl> trailing = new List<AvControl>(2);
        private AvState state = AvState.Info;
        private bool hover, armed, dim;
        private AvIcon glyph = AvIcon.None;

        public ActionTile(RectTransform parent, AvIcon iconKind)
        {
            Rect = AvLay.Child(parent, "Tile");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            plate = AvFrame.Add(Rect, "Plate", AvChamfer.Diagonal(6f));
            glyph = iconKind;
            icon = AvIcons.Make(Rect, iconKind, AvGridTokens.IconTool, Color.white);
            name = OpsText.Block(Rect, "Name", AvTextRole.Label);
            sub = OpsText.Block(Rect, "Sub", AvTextRole.ProseSmall);
            value = OpsText.Line(Rect, "Value", AvTextRole.DataStrong, TextAlignmentOptions.TopRight);
            unit = OpsText.Line(Rect, "Unit", AvTextRole.Micro, TextAlignmentOptions.TopRight);
            AvHit hit = AvHit.On(frame);
            hit.Hover = h => { hover = h; Restyle(); };
            Restyle();
        }

        /// <summary>Hover help shown in the console footer.</summary>
        public string Help { set { frame.raycastTarget = true; AvHelpTip.Attach(frame.gameObject, value); } }

        public bool Armed { get => armed; set { if (armed == value) return; armed = value; Restyle(); } }

        /// <summary>Muted name: the ability cannot be used right now.</summary>
        public bool Dim { get => dim; set { if (dim == value) return; dim = value; Restyle(); } }

        public void Set(string n, string s, string v, string u, AvState st, AvIcon kind)
        {
            bool grew = OpsText.Set(name, n);
            grew |= OpsText.Set(sub, s);
            bool valueChanged = OpsText.Set(value, v);
            OpsText.Set(unit, u);
            grew |= valueChanged && (value.text.Length == 0 || string.IsNullOrEmpty(v));
            if (kind != glyph) { glyph = kind; AvIcons.Set(icon, kind, AvGridTokens.IconTool); }
            if (st != state) { state = st; Restyle(); }
            if (grew) Changed();
        }

        public AvControl AddTrailing(AvControl.Spec spec)
        {
            if (trailing.Count >= 2) return null;
            AvControl c = AvControl.Make(Rect, spec);
            trailing.Add(c);
            return c;
        }

        private float TextW(float w) => w - PadX - Plate - 12f - PadX - (value.text.Length > 0 ? ValueW + 4f : 0f) - trailing.Count * (TrailW + 4f);

        public override float Measure(float width)
        {
            float w = TextW(width);
            float h = PadY + AvText.Height(name, w) + (sub.text.Length > 0 ? 2f + AvText.Height(sub, w) : 0f) + PadY;
            return Mathf.Max(Min, h);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = TextW(s.W), nh = AvText.Height(name, w), x0 = PadX + Plate + 12f;
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(plate.rectTransform, PadX + 2f, (s.H - Plate) * 0.5f, Plate, Plate);
            AvLay.Place(icon.rectTransform, PadX + 2f + (Plate - 20f) * 0.5f, (s.H - 20f) * 0.5f, 20f, 20f);
            OpsText.Place(name, x0, PadY, w, nh);
            OpsText.Place(sub, x0, PadY + nh + 2f, w, sub.text.Length > 0 ? AvText.Height(sub, w) : 0f);
            float x = s.W - PadX;
            for (int i = trailing.Count - 1; i >= 0; i--)
            {
                x -= TrailW;
                AvLay.Place(trailing[i].Rect, x, (s.H - TrailH) * 0.5f, TrailW, TrailH);
                x -= 4f;
            }
            if (value.text.Length > 0)
            {
                OpsText.Place(value, x - ValueW, PadY, ValueW, 18f);
                OpsText.Place(unit, x - ValueW, PadY + 18f, ValueW, 15f);
            }
        }

        public override void Restyle()
        {
            string st = armed ? "armed" : hover ? "hover" : null;
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(state), st);
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            Color hue = OpsInk.Rail(state);
            rail.color = armed ? OpsInk.Select : hue;
            plate.Paint(OpsInk.A(hue, 0.14f), OpsInk.A(hue, 0.6f));
            icon.color = dim && state == AvState.Inert ? OpsInk.Muted : OpsInk.Word(state == AvState.Inert ? AvState.Info : state);
            name.color = dim ? OpsInk.Muted : OpsInk.Ink;
            sub.color = OpsInk.Dim;
            value.color = dim ? OpsInk.Muted : OpsInk.Word(state == AvState.Inert ? AvState.Info : state);
            unit.color = OpsInk.Muted;
            foreach (AvControl c in trailing) c.Restyle();
        }
    }
}
