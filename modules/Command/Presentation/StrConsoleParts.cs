using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// Kit v2 building blocks shared by the STR console and its floating operations room.
    /// The kit has no wrapped-paragraph part, no stage strip and no tile; these are built from
    /// <see cref="AvPart"/>/<see cref="AvText"/>/<see cref="AvLay"/>/<see cref="AvFrame"/> exactly as the
    /// kit's own parts are, and every one that changes its own text or height reports it through
    /// <see cref="AvPart.Changed"/>. Kit gaps are listed in the slice report.
    /// </summary>
    internal static class TabHelp
    {
        /// <summary>Hover help for a console's icon tabs, in tab order (null keeps a tab without help).</summary>
        public static void Apply(AvTabBar bar, params string[] hints)
        {
            if (bar == null) return;
            AvControl[] tabs = bar.Rect.GetComponentsInChildren<AvControl>(true);
            for (int i = 0; i < tabs.Length && i < hints.Length; i++)
                if (hints[i] != null) tabs[i].Help = hints[i];
        }
    }

    /// <summary>Hover help for the console's header tiles (the kit gives metrics no help seam; the tile's frame takes one).</summary>
    internal static class StrTips
    {
        public static void Metric(AvMetric metric, string tip)
        {
            if (metric == null || metric.Rect == null) return;
            Transform frame = metric.Rect.Find("Frame");
            var graphic = frame != null ? frame.GetComponent<AvFrame>() : null;
            if (graphic == null) return;
            graphic.raycastTarget = true;
            // The header tiles live on the console's non-interactive "live" canvas; hover needs a raycaster there.
            Canvas host = metric.Rect.GetComponentInParent<Canvas>();
            if (host != null && host.GetComponent<GraphicRaycaster>() == null) host.gameObject.AddComponent<GraphicRaycaster>();
            AvHelpTip.Attach(graphic.gameObject, tip);
        }
    }

    /// <summary>Colour roles the STR parts paint with, read from the live kit sheet (never literals).</summary>
    internal static class StrPaint
    {
        public static Color State(AvState s)
        {
            switch (s)
            {
                case AvState.Ready: return AvStyleHost.FuiColor("ready", AvTheme.RailReady);
                case AvState.Caution: return AvStyleHost.FuiColor("caution", AvTheme.RailCaution);
                case AvState.Danger: return AvStyleHost.FuiColor("danger", AvTheme.RailDanger);
                case AvState.Info: return AvStyleHost.FuiColor("info", AvTheme.RailInfo);
                default: return AvStyleHost.FuiColor("inert", AvTheme.RailInert).WithAlpha(1f);
            }
        }

        public static Color Ink => AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
        public static Color Dim => AvStyleHost.FuiColor("ink-dim", AvTheme.Dim);
        public static Color Muted => AvStyleHost.FuiColor("ink-muted", AvTheme.Disabled);
        public static Color Hairline => AvStyleHost.FuiColor("hairline", AvTheme.Hairline);
        public static Color Frame => AvStyleHost.FuiColor("frame", AvTheme.Frame);
        public static Color Select => AvStyleHost.FuiColor("select", AvTheme.Accent);
        public static Color Friendly => AvStyleHost.FuiColor("friendly", AvTheme.Friendly);
        public static Color Hostile => AvStyleHost.FuiColor("hostile", AvTheme.Hostile);
        public static Color Inert => AvStyleHost.FuiColor("surface-inert", AvTheme.SurfaceInert);
        public static Color Raised => AvStyleHost.FuiColor("surface-raised", AvTheme.SurfaceRaised);
        public static Color Surface => AvStyleHost.FuiColor("surface", AvTheme.Surface);
        public static Color Key => AvStyleHost.FuiColor("key", AvTheme.RailInfo);

        /// <summary>Text tone for a state word: caution/danger/ready keep their colour, the rest read dim.</summary>
        public static Color StateText(AvState s) =>
            s == AvState.Inert ? Muted : s == AvState.Info ? Ink : State(s);

        public static string Count(int n) => AvNum.Thousands(n);

        /// <summary>A fixed-role text child that never wraps and shrinks toward the floor instead of spilling.</summary>
        public static TMP_Text Fit(RectTransform parent, string name, AvTextRole role,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            TextMeshProUGUI t = AvText.Make(parent, name, role, "", align);
            AvText.Fit(t, false);
            return t;
        }

        /// <summary>Set text only when it changed; returns true if it did.</summary>
        public static bool Put(TMP_Text t, string value)
        {
            string v = value ?? "";
            if (t.text == v) return false;
            t.text = v;
            return true;
        }
    }

    internal sealed class ProseText : AvPart
    {
        private readonly TMP_Text text;

        public ProseText(RectTransform parent, AvTextRole role = AvTextRole.ProseSmall)
        {
            Rect = AvLay.Child(parent, "Prose");
            text = AvText.Make(Rect, "Text", role, "", TextAlignmentOptions.TopLeft, true);
            AvLay.Fill(text.rectTransform);
            Restyle();
        }

        public void Set(string value)
        {
            if (StrPaint.Put(text, value)) Changed();
        }

        public override float Measure(float width) => text.text.Length == 0 ? 0f : Mathf.Max(14f, AvText.Height(text, width));

        public override void Restyle() =>
            text.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
    }

    /// <summary>
    /// The one empty-state card: an icon, what is missing, and what to do about it. Used instead of
    /// section headers with blank space under them.
    /// </summary>
    internal sealed class StrNote : AvPart
    {
        private const float PadX = 12f, PadY = 9f, IconW = 26f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text icon, title, body;
        private AvState state = AvState.Inert;

        public StrNote(RectTransform parent, AvIcon glyph)
        {
            Rect = AvLay.Child(parent, "Note");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            icon = AvIcons.Make(Rect, glyph, AvGridTokens.IconTool, Color.white);
            title = AvText.Make(Rect, "Title", AvTextRole.Head, "", TextAlignmentOptions.TopLeft, true);
            body = AvText.Make(Rect, "Body", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        public void Set(string headline, string detail, AvState s = AvState.Inert)
        {
            bool changed = StrPaint.Put(title, AvStates.Glyph(s) + (headline ?? ""));
            changed |= StrPaint.Put(body, detail);
            if (s != state) { state = s; Restyle(); }
            if (changed) Changed();
        }

        private float TextW(float width) => Mathf.Max(20f, width - PadX - IconW - PadX - 4f);

        public override float Measure(float width)
        {
            float w = TextW(width);
            float h = PadY + AvText.Height(title, w) + (body.text.Length > 0 ? 3f + AvText.Height(body, w) : 0f) + PadY;
            return Mathf.Max(AvGridTokens.Row + 8f, h);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = TextW(s.W), th = AvText.Height(title, w), bh = AvText.Height(body, w);
            float content = th + (body.text.Length > 0 ? 3f + bh : 0f);
            // A note that soaks up leftover height draws a compact card centred in the slot: a frame
            // stretched across the whole well reads as an empty container, not an empty state.
            float h0 = Mathf.Min(s.H, Mathf.Max(AvGridTokens.Row + 8f, PadY + content + PadY));
            float y0 = (s.H - h0) * 0.5f;
            float top = y0 + Mathf.Max(PadY, (h0 - content) * 0.5f);
            AvLay.Place(frame.rectTransform, 0f, y0, s.W, h0);
            AvLay.Place(rail.rectTransform, 0f, y0, 2f, h0);
            AvLay.Place(icon.rectTransform, PadX, top - 1f, IconW, 22f);
            AvLay.Place(title.rectTransform, PadX + IconW + 4f, top, w, th);
            AvLay.Place(body.rectTransform, PadX + IconW + 4f, top + th + 3f, w, bh);
        }

        public override void Restyle()
        {
            frame.Paint(StrPaint.Inert, StrPaint.Hairline);
            Color c = state == AvState.Inert ? StrPaint.Frame : StrPaint.State(state);
            rail.color = c;
            icon.color = c;
            title.color = state == AvState.Inert ? StrPaint.Ink : StrPaint.State(state);
            body.color = StrPaint.Dim;
        }
    }

    /// <summary>
    /// A row of equal, numbered or worded stages with the current one lit. One implementation for the
    /// DEFCON ladder (fills up to the current level) and the operation phase tracker (lights only the
    /// current phase, earlier ones stay lit in a quieter tint).
    /// </summary>
    internal sealed class StrStageStrip : AvPart
    {
        public const float Height = 24f;
        private const float Gap = 3f;
        private readonly Seg[] segs;
        private int current = -1;
        private AvState state = AvState.Inert;

        private sealed class Seg
        {
            public RectTransform Box;
            public AvFrame Frame;
            public TMP_Text Text;
        }

        public StrStageStrip(RectTransform parent, string[] labels, AvTextRole role = AvTextRole.DataStrong)
        {
            Rect = AvLay.Child(parent, "Stages");
            segs = new Seg[labels.Length];
            for (int i = 0; i < segs.Length; i++)
            {
                var seg = new Seg { Box = AvLay.Child(Rect, "Stage " + labels[i]) };
                seg.Frame = AvFrame.Add(seg.Box, "Frame", default(AvChamfer));
                AvLay.Fill(seg.Frame.rectTransform);
                seg.Text = AvText.Make(seg.Box, "Label", role, labels[i], TextAlignmentOptions.Center);
                AvText.Fit(seg.Text, false);
                AvLay.Fill(seg.Text.rectTransform, 1f);
                segs[i] = seg;
            }
            Restyle();
        }

        /// <summary>Light stage <paramref name="index"/> (-1 for none) in <paramref name="s"/>'s colour.</summary>
        public void Set(int index, AvState s)
        {
            if (index == current && s == state) return;
            current = index;
            state = s;
            Restyle();
        }

        public override float Measure(float width) => Height;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = AvFlowMath.ColumnWidth(s.W, segs.Length, Gap);
            for (int i = 0; i < segs.Length; i++)
                AvLay.Place(segs[i].Box, i * (w + Gap), 0f, w, s.H);
        }

        public override void Restyle()
        {
            Color c = StrPaint.State(state);
            for (int i = 0; i < segs.Length; i++)
            {
                Seg seg = segs[i];
                if (i == current)
                {
                    seg.Frame.Paint(c.WithAlpha(0.20f), c);
                    seg.Text.color = StrPaint.Ink;
                }
                else if (i < current)
                {
                    seg.Frame.Paint(c.WithAlpha(0.06f), c.WithAlpha(0.4f));
                    seg.Text.color = StrPaint.Dim;
                }
                else
                {
                    seg.Frame.Paint(StrPaint.Inert, StrPaint.Hairline);
                    seg.Text.color = StrPaint.Muted;
                }
            }
        }
    }

    /// <summary>
    /// A compact readout tile: icon + key on the first line, one big mono figure under it and a share bar on
    /// the floor. Four of them share one row. What used to be a wrapped caption rides on the hover help.
    /// </summary>
    internal sealed class StrTile : AvPart
    {
        public const float TileHeight = 58f;
        private const float PadX = 8f;
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text icon, key, value;
        private readonly AvGaugeGraphic bar;
        private readonly string keyText;
        private AvState state = AvState.Info;
        private bool hasBar;
        private string help = "", caption = "";

        public StrTile(RectTransform parent, AvIcon glyph, string keyText)
        {
            this.keyText = keyText;
            Rect = AvLay.Child(parent, "Tile " + keyText);
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(6f));
            AvLay.Fill(frame.rectTransform);
            frame.raycastTarget = true;
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            icon = AvIcons.Make(Rect, glyph, AvGridTokens.IconInline, Color.white);
            key = StrPaint.Fit(Rect, "Key", AvTextRole.Micro);
            key.text = keyText;
            value = StrPaint.Fit(Rect, "Value", AvTextRole.Display);
            var go = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(Rect, false);
            bar = go.AddComponent<AvGaugeGraphic>();
            bar.Shape = AvGaugeShape.Bar;
            bar.raycastTarget = false;
            bar.gameObject.SetActive(false);
            Restyle();
        }

        public void Set(string figure, string captionText, AvState s, float share = -1f)
        {
            bool changed = StrPaint.Put(value, figure);
            caption = captionText ?? "";
            // Status is never colour alone: a tile in caution or danger prints the glyph before its key.
            StrPaint.Put(key, AvStates.Glyph(s) + keyText);
            bool wantBar = share >= 0f;
            if (wantBar != hasBar) { hasBar = wantBar; bar.gameObject.SetActive(wantBar); changed = true; }
            if (wantBar) bar.Value = share;
            if (s != state) { state = s; Restyle(); }
            PushHelp();
            if (changed) Changed();
        }

        /// <summary>What the tile counts (the live caption is appended on hover).</summary>
        public string Help { set { help = value ?? ""; PushHelp(); } }

        private void PushHelp() =>
            AvHelpTip.Attach(frame.gameObject, caption.Length == 0 ? help : help + " Now: " + caption + ".");

        public override float Measure(float width) => TileHeight;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = Mathf.Max(20f, s.W - 2f * PadX);
            AvLay.Place(rail.rectTransform, 0f, 0f, 2f, s.H);
            AvLay.Place(icon.rectTransform, PadX, 7f, 14f, 15f);
            AvLay.Place(key.rectTransform, PadX + 18f, 6f, w - 18f, 15f);
            AvLay.Place(value.rectTransform, PadX, 20f, w, 30f);
            AvLay.Place((RectTransform)bar.transform, PadX, s.H - 7f, w, 3f);
        }

        public override void Restyle()
        {
            frame.Paint(StrPaint.Inert, StrPaint.Hairline);
            Color c = StrPaint.State(state);
            rail.color = c;
            icon.color = StrPaint.Key;
            key.color = StrPaint.Key;
            value.color = state == AvState.Info || state == AvState.Ready || state == AvState.Inert
                ? StrPaint.Ink : StrPaint.State(state);
            bar.Track = StrPaint.Hairline;
            bar.FillColor = bar.FillEnd = c;
            bar.SetVerticesDirty();
        }
    }

    /// <summary>
    /// A vertical stack of parts that is itself one part, so a two-column flow row can hold "a card, its
    /// empty-state note and a button" in one column. Hidden children collapse; a child's Changed() reaches
    /// the flow through the stack; children with <see cref="AvPart.Grow"/> share the height the row gives
    /// the stack beyond its natural height.
    /// </summary>
    internal sealed class StrStack : AvPart
    {
        private const float Gap = 8f;
        private readonly System.Collections.Generic.List<AvPart> children = new System.Collections.Generic.List<AvPart>(4);

        public StrStack(RectTransform parent) => Rect = AvLay.Child(parent, "Stack");

        public T Add<T>(T part) where T : AvPart
        {
            part.Parent = this;
            children.Add(part);
            part.Rect.SetParent(Rect, false);
            return part;
        }

        public override float Measure(float width)
        {
            float h = 0f;
            for (int i = 0; i < children.Count; i++)
                if (children[i].Shown) h += children[i].Measure(width) + Gap;
            return Mathf.Max(0f, h - Gap);
        }

        public override void Place(AvSlot s)
        {
            base.Place(s);
            // A column that is shorter than its row hands the leftover height to its growing children.
            float weights = 0f;
            for (int i = 0; i < children.Count; i++)
                if (children[i].Shown) weights += children[i].Grow;
            float extra = weights > 0f ? Mathf.Max(0f, s.H - Measure(s.W)) : 0f;
            float y = 0f;
            for (int i = 0; i < children.Count; i++)
            {
                AvPart c = children[i];
                if (!c.Shown) continue;
                float h = c.Measure(s.W) + (extra > 0f ? extra * c.Grow / weights : 0f);
                c.Place(new AvSlot(0f, y, s.W, h));
                y += h + Gap;
            }
        }

        public override void Restyle()
        {
            for (int i = 0; i < children.Count; i++) children[i].Restyle();
        }
    }
}
