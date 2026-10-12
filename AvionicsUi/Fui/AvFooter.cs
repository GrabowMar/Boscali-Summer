using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// Console footer: a fixed strip of sentence-case status / help. Its height never changes (a hover hint must
    /// not make the page above it jump); longer text shrinks toward the 11 px floor. While idle, the right end
    /// carries the ambient status column (<see cref="AvAmbient"/>); a hover hint takes the full width back.
    /// </summary>
    public sealed class AvFooter : AvPart
    {
        private readonly Image back, statusRule;
        private readonly AvFrame tag;
        private readonly TMP_Text text, tagText;
        private readonly TMP_Text[] status = new TMP_Text[AvAmbient.Lines];
        private AvState state;
        private string baseText = "", hint;
        private bool statusShown = true;

        public AvFooter(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "Footer");
            back = AvLay.Solid(Rect, "Back", Color.clear); AvLay.Fill(back.rectTransform);
            AvSurfaceGradient.Apply(back, Color.white, new Color(.72f, .77f, .79f, 1f));
            // The badge sits in its own container so the frame is not read as the backing of the footer text beside it.
            RectTransform badge = AvLay.Child(Rect, "TipBadge");
            AvLay.Place(badge, AvGridTokens.Pad, 8f, TagW, 18f);
            tag = AvFrame.Add(badge, "TipTag", default(AvChamfer)); tag.Stroke = 0f; AvLay.Fill(tag.rectTransform);
            tagText = AvText.Make(badge, "TipWord", AvTextRole.Micro, "TIP", TextAlignmentOptions.Center);
            AvText.Fit(tagText, false);
            text = AvText.Make(Rect, "Text", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
            AvText.Fit(text, true);
            AvLay.Fill(text.rectTransform, 0f); text.rectTransform.offsetMin = new Vector2(TagW + 2f * AvGridTokens.Pad, 6f);
            AvLay.Fill(tagText.rectTransform);

            statusRule = AvLay.Solid(Rect, "StatusRule", Color.clear);
            PinRight(statusRule.rectTransform, AvGridTokens.Pad + StatusW + 7f, 9f, 1f, AvGridTokens.Footer - 18f);
            for (int i = 0; i < status.Length; i++)
            {
                status[i] = AvText.Make(Rect, "Status" + i, AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
                AvText.Fit(status[i], false);
                PinRight(status[i].rectTransform, AvGridTokens.Pad, 9f + i * 19f, StatusW, 18f);
            }
            ShowStatus(false);
            Restyle();
        }

        public void Set(string value, AvState s = AvState.Inert)
        {
            string composed = AvStates.Glyph(s) + (value ?? "");
            if (baseText == composed && state == s) return;
            baseText = composed; state = s;
            if (hint == null) text.text = composed;
            Restyle();
        }

        /// <summary>Hover help: temporarily show <paramref name="value"/>; null restores the status line.</summary>
        public void SetHint(string value)
        {
            hint = string.IsNullOrEmpty(value) ? null : value;
            text.text = hint ?? baseText;
            Tick();
            Restyle();
        }

        /// <summary>Refreshes the idle status column; the owning console or window calls it on its slow tick.</summary>
        public void Tick()
        {
            bool any = false;
            if (hint == null)
                for (int i = 0; i < status.Length; i++)
                {
                    string line = AvAmbient.Line(i);
                    AvText.Set(status[i], line);
                    any |= line.Length > 0;
                }
            ShowStatus(any);
        }

        private void ShowStatus(bool on)
        {
            if (on == statusShown) return;
            statusShown = on;
            statusRule.enabled = on;
            foreach (TMP_Text line in status) line.enabled = on;
            text.rectTransform.offsetMax = new Vector2(-(AvGridTokens.Pad + (on ? StatusW + 14f : 0f)), -6f);
        }

        private static void PinRight(RectTransform t, float right, float top, float w, float h)
        {
            t.anchorMin = t.anchorMax = t.pivot = new Vector2(1f, 1f);
            t.anchoredPosition = new Vector2(-right, -top);
            t.sizeDelta = new Vector2(w, h);
        }

        private const float TagW = 34f, StatusW = 124f;

        public override float Measure(float width) => AvGridTokens.Footer;

        public override void Restyle()
        {
            AvStyle s = AvStyleHost.FuiStyle("footer " + AvStates.Class(state));
            back.color = AvStyleHost.Resolve(s.Background, AvTheme.SurfaceInert);
            text.color = AvStyleHost.Resolve(hint != null ? AvStyleHost.FuiStyle("title").Color : s.Color, AvTheme.Dim);
            tag.Paint(AvStyleHost.FuiFill("slab " + (hint != null || state == AvState.Inert ? "ready" : AvStates.Class(state)), AvTheme.Accent), Color.clear);
            tagText.color = AvStyleHost.FuiInk("slab", Color.black);
            tagText.text = hint != null ? "TIP" : "SYS";
            statusRule.color = AvStyleHost.FuiFill("decor", AvTheme.Hairline);
            status[0].color = AvStyleHost.FuiInk("title", AvTheme.TextPrimary);
            for (int i = 1; i < status.Length; i++) status[i].color = AvStyleHost.FuiInk("page-index", AvTheme.Dim);
        }
    }
}
