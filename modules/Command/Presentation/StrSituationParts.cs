using System;
using BoscaliSummer.Features.Command.Domain;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    /// <summary>
    /// SITUATION hero: the threat condition. DEFCON as a five-step ladder (5 quiet .. 1 maximum) with
    /// the level, its word, and the staff's one-line assessment. Status is the number, the word and the
    /// ladder position; colour only repeats it.
    /// </summary>
    internal sealed class StrThreatBanner : AvPart
    {
        private const float PadX = 14f, NumberW = 76f;
        private static readonly string[] Steps = { "5", "4", "3", "2", "1" };
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text keyText, number, word, assessment, counts;
        private readonly StrStageStrip ladder;
        private AvState state = AvState.Ready;

        public StrThreatBanner(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "ThreatBanner");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            keyText = StrPaint.Fit(Rect, "Key", AvTextRole.Micro, TextAlignmentOptions.Center);
            keyText.text = "DEFCON";
            number = StrPaint.Fit(Rect, "Number", AvTextRole.Display, TextAlignmentOptions.Center);
            word = StrPaint.Fit(Rect, "Word", AvTextRole.Title);
            ladder = new StrStageStrip(Rect, Steps);
            assessment = AvText.Make(Rect, "Assessment", AvTextRole.Prose, "", TextAlignmentOptions.TopLeft, true);
            counts = AvText.Make(Rect, "Counts", AvTextRole.Micro, "", TextAlignmentOptions.TopLeft, true);
            Restyle();
        }

        public static string WordFor(int level)
        {
            switch (level)
            {
                case 1: return "MAXIMUM READINESS";
                case 2: return "HIGH ALERT";
                case 3: return "ELEVATED";
                case 4: return "GUARDED";
                default: return "NOMINAL";
            }
        }

        public static AvState StateFor(int level) =>
            level <= 2 ? AvState.Danger : level == 3 ? AvState.Caution : AvState.Ready;

        public void Set(int level, string assessmentText, string countsText)
        {
            int clamped = Mathf.Clamp(level, 1, 5);
            AvState s = StateFor(clamped);
            bool changed = StrPaint.Put(number, clamped.ToString(System.Globalization.CultureInfo.InvariantCulture));
            changed |= StrPaint.Put(word, AvStates.Glyph(s) + WordFor(clamped));
            changed |= StrPaint.Put(assessment, assessmentText);
            changed |= StrPaint.Put(counts, countsText);
            ladder.Set(5 - clamped, s);
            if (s != state) { state = s; Restyle(); }
            if (changed) Changed();
        }

        private float TextW(float width) => Mathf.Max(20f, width - 2f * PadX);

        public override float Measure(float width) =>
            72f + AvText.Height(assessment, TextW(width)) + (counts.text.Length > 0 ? 3f + AvText.Height(counts, TextW(width)) : 0f) + 12f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(keyText.rectTransform, PadX, 10f, NumberW, 15f);
            AvLay.Place(number.rectTransform, PadX, 24f, NumberW, 46f);
            float x = PadX + NumberW + 12f, w = s.W - x - PadX;
            AvLay.Place(word.rectTransform, x, 12f, w, 26f);
            ladder.Place(new AvSlot(x + 0f, 44f, w, StrStageStrip.Height));
            float aw = TextW(s.W), ah = AvText.Height(assessment, aw);
            AvLay.Place(assessment.rectTransform, PadX, 76f, aw, ah);
            AvLay.Place(counts.rectTransform, PadX, 76f + ah + 3f, aw, AvText.Height(counts, aw));
        }

        public override void Restyle()
        {
            Color c = StrPaint.State(state);
            frame.Paint(StrPaint.Raised, c.WithAlpha(0.7f));
            rail.color = c;
            keyText.color = StrPaint.Dim;
            number.color = state == AvState.Ready ? StrPaint.Ink : c;
            word.color = state == AvState.Ready ? StrPaint.Ink : c;
            assessment.color = StrPaint.Ink;
            counts.color = StrPaint.Dim;
            ladder.Restyle();
        }
    }

    /// <summary>
    /// The force-balance "tug of war": allied, contested, unclaimed and hostile sector shares on one bar
    /// with the percentages above and sector counts below. Contested ground is its own band, not a
    /// shortfall in somebody's fill.
    /// </summary>
    internal sealed class StrForceBar : AvPart
    {
        private const float PadX = 14f, BarH = 16f;
        private readonly AvFrame frame;
        private readonly TMP_Text[] labels = new TMP_Text[3];
        private readonly TMP_Text[] values = new TMP_Text[3];
        private readonly TMP_Text[] counts = new TMP_Text[3];
        private readonly Image[] bands = new Image[4];
        private readonly Image tick;
        private readonly float[] share = new float[4];
        private bool known, styledKnown;
        private float barW;
        private AvState contestedState = AvState.Inert;

        private static readonly string[] Keys = { "ALLIED", "CONTESTED", "HOSTILE" };

        public StrForceBar(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "ForceBar");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            for (int i = 0; i < 3; i++)
            {
                TextAlignmentOptions a = i == 0 ? TextAlignmentOptions.MidlineLeft
                    : i == 1 ? TextAlignmentOptions.Midline : TextAlignmentOptions.MidlineRight;
                labels[i] = StrPaint.Fit(Rect, "Label " + Keys[i], AvTextRole.Micro, a);
                labels[i].text = Keys[i];
                values[i] = StrPaint.Fit(Rect, "Value " + Keys[i], AvTextRole.Display, a);
                counts[i] = StrPaint.Fit(Rect, "Count " + Keys[i], AvTextRole.DataSmall, a);
            }
            for (int i = 0; i < bands.Length; i++) bands[i] = AvLay.Solid(Rect, "Band " + i, Color.clear);
            tick = AvLay.Solid(Rect, "Midline", Color.clear);
            Restyle();
        }

        /// <summary>Shares are fractions of all sectors; the remainder is unclaimed ground.</summary>
        public void Set(bool haveField, float allied, float contested, float hostile,
            int alliedCount, int contestedCount, int hostileCount)
        {
            known = haveField;
            float a = known ? Mathf.Clamp01(allied) : 0f;
            float h = known ? Mathf.Clamp01(hostile) : 0f;
            float c = known ? Mathf.Clamp01(contested) : 0f;
            float total = a + c + h;
            if (total > 1f) { a /= total; c /= total; h /= total; total = 1f; }
            share[0] = a; share[1] = c; share[2] = Mathf.Max(0f, 1f - total); share[3] = h;
            values[0].text = known ? TheaterReadout.Percent(allied) : "—";
            values[1].text = known ? TheaterReadout.Percent(contested) : "—";
            values[2].text = known ? TheaterReadout.Percent(hostile) : "—";
            counts[0].text = known ? StrPaint.Count(alliedCount) + " SECTORS" : "NO FIELD";
            counts[1].text = known ? StrPaint.Count(contestedCount) : "";
            counts[2].text = known ? StrPaint.Count(hostileCount) + " SECTORS" : "";
            AvState cs = known && contestedCount > 0 ? AvState.Caution : AvState.Inert;
            if (cs != contestedState || known != styledKnown) { contestedState = cs; styledKnown = known; Restyle(); }
            PlaceBands();
        }

        public override float Measure(float width) => 100f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float w = s.W - 2f * PadX, third = w / 3f;
            barW = w;
            for (int i = 0; i < 3; i++)
            {
                float x = PadX + i * third;
                AvLay.Place(labels[i].rectTransform, x, 10f, third, 15f);
                AvLay.Place(values[i].rectTransform, x, 25f, third, 30f);
                AvLay.Place(counts[i].rectTransform, x, 76f, third, 16f);
            }
            PlaceBands();
        }

        private void PlaceBands()
        {
            if (barW <= 0f) return;
            float x = PadX, y = 60f;
            for (int i = 0; i < bands.Length; i++)
            {
                float w = Mathf.Round(barW * share[i]);
                bands[i].enabled = w >= 1f;
                if (w >= 1f) AvLay.Place(bands[i].rectTransform, x, y, w, BarH);
                x += w;
            }
            // Fill any rounding gap on the hostile end so the bar always spans edge to edge.
            if (bands[3].enabled)
            {
                float end = PadX + barW;
                RectTransform r = bands[3].rectTransform;
                float left = r.anchoredPosition.x;
                AvLay.Place(r, left, y, end - left, BarH);
            }
            AvLay.Place(tick.rectTransform, PadX + barW * 0.5f - 0.5f, y - 3f, 1f, BarH + 6f);
            tick.enabled = known;
        }

        public override void Restyle()
        {
            frame.Paint(StrPaint.Inert, StrPaint.Hairline);
            Color friendly = StrPaint.Friendly, hostile = StrPaint.Hostile;
            Color contested = contestedState == AvState.Caution ? StrPaint.State(AvState.Caution) : StrPaint.Muted;
            bands[0].color = friendly;
            bands[1].color = contested;
            bands[2].color = StrPaint.Hairline;
            bands[3].color = hostile;
            tick.color = StrPaint.Ink.WithAlpha(0.85f);
            for (int i = 0; i < 3; i++)
            {
                Color c = i == 0 ? friendly : i == 2 ? hostile : contested;
                labels[i].color = StrPaint.Dim;
                values[i].color = known ? c : StrPaint.Muted;
                counts[i].color = StrPaint.Dim;
            }
        }
    }

    /// <summary>
    /// Air Tasking Order: one compact row per mission code — a code chip, the task, a share bar, the share
    /// and the count in mono. Rows with nothing tasked dim instead of disappearing.
    /// </summary>
    internal sealed class StrAtoBoard : AvPart
    {
        private const float HeaderH = 20f, RowH = 26f, PadX = 10f, ChipW = 44f, BarW = 84f, ShareW = 40f, CountW = 34f;
        private readonly AvFrame frame;
        private readonly TMP_Text[] heads = new TMP_Text[4];
        private readonly Line[] lines;
        private int count;
        private int hot = -1;

        private sealed class Line
        {
            public RectTransform Chip;
            public AvFrame ChipFrame;
            public TMP_Text Code, Name, Share, Count;
            public AvGaugeGraphic Bar;
            public RectTransform BarRect;
        }

        public StrAtoBoard(RectTransform parent, int rows)
        {
            Rect = AvLay.Child(parent, "AtoBoard");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            string[] names = { "CODE", "TASK", "SHARE", "N" };
            for (int i = 0; i < heads.Length; i++)
            {
                heads[i] = StrPaint.Fit(Rect, "Head " + names[i], AvTextRole.Micro,
                    i == 3 ? TextAlignmentOptions.MidlineRight : TextAlignmentOptions.MidlineLeft);
                heads[i].text = names[i];
            }
            lines = new Line[rows];
            for (int i = 0; i < rows; i++)
            {
                var l = new Line { Chip = AvLay.Child(Rect, "Chip " + i) };
                l.ChipFrame = AvFrame.Add(l.Chip, "Frame", AvChamfer.Diagonal(4f));
                AvLay.Fill(l.ChipFrame.rectTransform);
                l.Code = AvText.Make(l.Chip, "Code", AvTextRole.DataStrong, "", TextAlignmentOptions.Center);
                AvText.Fit(l.Code, false);
                AvLay.Fill(l.Code.rectTransform, 1f);
                l.Name = StrPaint.Fit(Rect, "Name " + i, AvTextRole.Label);
                l.Share = StrPaint.Fit(Rect, "Share " + i, AvTextRole.DataSmall, TextAlignmentOptions.MidlineRight);
                l.Count = StrPaint.Fit(Rect, "Count " + i, AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight);
                var go = new GameObject("Bar " + i, typeof(RectTransform), typeof(CanvasRenderer));
                go.transform.SetParent(Rect, false);
                l.Bar = go.AddComponent<AvGaugeGraphic>();
                l.Bar.Shape = AvGaugeShape.Bar;
                l.Bar.raycastTarget = false;
                l.BarRect = (RectTransform)go.transform;
                lines[i] = l;
            }
            Restyle();
        }

        public int Rows => lines.Length;

        /// <summary>Bind one row; <paramref name="share01"/> is the fraction of observed sorties.</summary>
        public void Set(int row, string code, string task, int n, float share01)
        {
            Line l = lines[row];
            StrPaint.Put(l.Code, code);
            StrPaint.Put(l.Name, task);
            StrPaint.Put(l.Count, StrPaint.Count(n));
            StrPaint.Put(l.Share, TheaterReadout.Percent(share01));
            l.Bar.Value = share01;
            StyleLine(row);
        }

        /// <summary>Highlight the busiest code; -1 for none.</summary>
        public void SetHot(int row)
        {
            if (row == hot) return;
            hot = row;
            Restyle();
        }

        public override float Measure(float width) => 8f + HeaderH + lines.Length * RowH + 6f;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            float right = s.W - PadX;
            float countX = right - CountW, shareX = countX - 4f - ShareW, barX = shareX - 6f - BarW;
            float nameX = PadX + ChipW + 10f;
            AvLay.Place(heads[0].rectTransform, PadX, 8f, ChipW, HeaderH);
            AvLay.Place(heads[1].rectTransform, nameX, 8f, barX - nameX, HeaderH);
            AvLay.Place(heads[2].rectTransform, barX, 8f, BarW, HeaderH);
            AvLay.Place(heads[3].rectTransform, countX, 8f, CountW, HeaderH);
            for (int i = 0; i < lines.Length; i++)
            {
                Line l = lines[i];
                float y = 8f + HeaderH + i * RowH;
                AvLay.Place(l.Chip, PadX, y + 2f, ChipW, RowH - 4f);
                AvLay.Place(l.Name.rectTransform, nameX, y, barX - nameX - 6f, RowH);
                AvLay.Place(l.BarRect, barX, y + (RowH - 6f) * 0.5f, BarW - 4f, 6f);
                AvLay.Place(l.Share.rectTransform, shareX, y, ShareW, RowH);
                AvLay.Place(l.Count.rectTransform, countX, y, CountW, RowH);
            }
        }

        public override void Restyle()
        {
            frame.Paint(StrPaint.Inert, StrPaint.Hairline);
            for (int i = 0; i < heads.Length; i++) heads[i].color = StrPaint.Muted;
            for (int i = 0; i < lines.Length; i++) StyleLine(i);
        }

        private void StyleLine(int i)
        {
            Line l = lines[i];
            Color key = StrPaint.Key;
            bool busy = i == hot;
            l.ChipFrame.Paint(busy ? key.WithAlpha(0.42f) : StrPaint.Raised, busy ? key : StrPaint.Frame.WithAlpha(0.7f));
            l.Code.color = StrPaint.Ink;
            bool idle = l.Count.text == "0" || l.Count.text.Length == 0;
            l.Name.color = idle ? StrPaint.Muted : StrPaint.Ink;
            l.Count.color = idle ? StrPaint.Muted : StrPaint.Ink;
            l.Share.color = StrPaint.Dim;
            l.Bar.Track = StrPaint.Hairline;
            l.Bar.FillColor = l.Bar.FillEnd = busy ? key : key.WithAlpha(0.7f);
            l.Bar.SetVerticesDirty();
        }
    }
}
