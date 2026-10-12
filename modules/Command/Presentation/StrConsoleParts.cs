using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// Kit v2 building blocks used by the STR console.
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

    /// <summary>Text helpers the STR parts share (colours come from <see cref="AvInk"/>).</summary>
    internal static class StrPaint
    {
        public static string Count(int n) => AvNum.Thousands(n);

        /// <summary>A fixed-role text child that never wraps and shrinks toward the floor instead of spilling.</summary>
        public static TMP_Text Fit(RectTransform parent, string name, AvTextRole role,
            TextAlignmentOptions align = TextAlignmentOptions.MidlineLeft)
        {
            TextMeshProUGUI t = AvText.Make(parent, name, role, "", align);
            AvText.Fit(t, false);
            return t;
        }
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
            bool changed = AvText.Set(title, AvStates.Glyph(s) + (headline ?? ""));
            changed |= AvText.Set(body, detail);
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
            frame.Paint(AvInk.Inert, AvInk.Hairline);
            Color c = state == AvState.Inert ? AvInk.Frame : AvInk.State(state);
            rail.color = c;
            icon.color = c;
            title.color = state == AvState.Inert ? AvInk.Ink : AvInk.State(state);
            body.color = AvInk.Dim;
        }
    }
}
