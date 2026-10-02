using NOAvionics;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Calls;
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
    /// One CALL as a single fixed-height line: name, tier word, price, reason chip, state word and up to three
    /// controls. Text shrinks toward the 10 px floor; the line never wraps, so its height is constant and a
    /// refresh can never move the page.
    /// </summary>
    internal sealed class CallLine : AvPart
    {
        public const float Height = 24f;
        private readonly float rowHeight;
        private string measuredState, measuredReason, rowHelp;
        private float stateW, reasonW;
        private const float PadX = 8f, CtlH = 20f, CallW = 40f, StarW = 24f, UnlaseW = 56f, Gap = 3f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text name, tier, cost, reason, state;
        private readonly List<AvControl> controls = new List<AvControl>(3);
        private AvState tone = AvState.Info;
        private bool hover, armed, dim;

        /// <summary>A line of <paramref name="lineHeight"/> px (never below <see cref="Height"/>): a taller panel spreads the rows instead of leaving an empty band.</summary>
        public CallLine(RectTransform parent, float lineHeight = Height)
        {
            rowHeight = Mathf.Max(Height, lineHeight);
            Rect = AvLay.Child(parent, "CallLine");
            frame = AvFrame.Add(Rect, "Frame", default(AvChamfer));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            name = Micro(OpsText.Line(Rect, "Name", AvTextRole.Label, TextAlignmentOptions.MidlineLeft));
            tier = Micro(OpsText.Line(Rect, "Tier", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft));
            cost = Micro(OpsText.Line(Rect, "Cost", AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight));
            reason = Micro(OpsText.Line(Rect, "Reason", AvTextRole.Micro, TextAlignmentOptions.MidlineLeft));
            state = Micro(OpsText.Line(Rect, "State", AvTextRole.Micro, TextAlignmentOptions.MidlineRight));
            AvHit hit = AvHit.On(frame);
            hit.Hover = h => { hover = h; Restyle(); };
            Restyle();
        }

        private static TMP_Text Micro(TMP_Text t) { t.fontSizeMin = AvTokens.FontMicro; return t; }

        public AvControl AddControl(AvControl.Spec spec, string help = null)
        {
            if (controls.Count >= 3) return null;
            AvControl c = AvControl.Make(Rect, spec);
            c.SingleLine();
            if (help != null) c.Help = help;
            controls.Add(c);
            return c;
        }

        /// <summary>The CALL button (first control) glows while this line is armed.</summary>
        public bool Armed
        {
            get => armed;
            set { if (armed == value) return; armed = value; if (controls.Count > 0) controls[0].Armed = value; Restyle(); }
        }

        public bool Dim { get => dim; set { if (dim == value) return; dim = value; Restyle(); } }

        public void Set(CallTile t, AvState st)
        {
            OpsText.Set(name, t.Label);
            OpsText.Set(tier, t.TierWord);
            OpsText.Set(cost, t.CostText);
            OpsText.Set(reason, t.Reason);
            OpsText.Set(state, t.StateWord);
            if (st != tone) { tone = st; Restyle(); }
            Layout();
        }

        public override float Measure(float width) => rowHeight;

        public override void Place(AvSlot s) { base.Place(s); Layout(); }

        private void Layout()
        {
            float w = PlacedWidth > 0f ? PlacedWidth : AvTokens.PanelWidth - 30f, h = rowHeight;
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, h);
            float x = w - PadX;
            for (int i = controls.Count - 1; i >= 0; i--)
            {
                float cw = i == 0 ? CallW : i == 1 ? StarW : UnlaseW;
                x -= cw;
                AvLay.Place(controls[i].Rect, x, (h - CtlH) * 0.5f, cw, CtlH);
                x -= Gap;
            }
            float right = x - 2f;
            OpsText.Place(name, PadX + 2f, 0f, 100f, h);
            OpsText.Place(tier, 112f, 0f, 54f, h);
            OpsText.Place(cost, 166f, 0f, 40f, h);
            // The reason chip is shown only when it fits beside the whole state word; a long word (ARMED — PRESS AGAIN,
            // a lock reason) or a third control wins the room, and the price and reason then go to the row's hover help.
            if (measuredState != state.text) { measuredState = state.text; stateW = Natural(state); }
            if (measuredReason != reason.text) { measuredReason = reason.text; reasonW = Natural(reason); }
            bool chip = reason.text.Length > 0 && 210f + reasonW + 6f + stateW <= right;
            string tip = reason.text.Length > 0 && !chip ? cost.text + " · " + reason.text : null;
            if (tip != rowHelp)
            {
                rowHelp = tip;
                frame.raycastTarget = true;
                AvHelpTip.Attach(frame.gameObject, tip ?? "");
            }
            reason.gameObject.SetActive(chip);
            if (chip) OpsText.Place(reason, 210f, 0f, reasonW + 2f, h);
            float sx = chip ? 210f + reasonW + 6f : 210f;
            OpsText.Place(state, sx, 0f, Mathf.Max(20f, right - sx), h);
        }

        private static float Natural(TMP_Text t) =>
            t.text.Length == 0 ? 0f : Mathf.Ceil(t.GetPreferredValues(t.text, 1000f, 100f).x) + 2f;

        public override void Restyle()
        {
            string st = armed ? "armed" : hover ? "hover" : null;
            AvStyle r = AvStyleHost.FuiStyle("row " + AvStates.Class(tone), st);
            frame.Paint(AvStyleHost.Resolve(r.Background, AvTheme.SurfaceInert),
                r.Border.HasValue ? AvStyleHost.Resolve(r.Border, Color.clear) : Color.clear);
            Color hue = OpsInk.Rail(tone);
            rail.color = armed ? OpsInk.Select : hue;
            name.color = dim ? OpsInk.Muted : OpsInk.Ink;
            tier.color = OpsInk.Muted;
            cost.color = dim ? OpsInk.Muted : OpsInk.Word(tone == AvState.Inert ? AvState.Info : tone);
            reason.color = OpsInk.Dim;
            state.color = OpsInk.Word(tone);
            foreach (AvControl c in controls) c.Restyle();
        }
    }
}
