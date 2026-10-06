using NOAvionics;
using BoscaliSummer.Modules.Command.Domain;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// SITUATION hero: the threat condition. DEFCON as a five-step ladder (5 quiet .. 1 maximum) with
    /// the level, its word, and the staff's one-line assessment on hover. Status is the number, the word and
    /// the ladder position; colour only repeats it.
    /// </summary>
    internal sealed class StrThreatBanner : AvPart
    {
        private const float PadX = 12f, NumberW = 64f, BannerHeight = 64f;
        private static readonly string[] Steps = { "5", "4", "3", "2", "1" };
        private readonly AvFrame frame;
        private readonly Image rail;
        private readonly TMP_Text keyText, number, word;
        private readonly StrStageStrip ladder;
        private AvState state = AvState.Ready;

        public StrThreatBanner(RectTransform parent)
        {
            Rect = AvLay.Child(parent, "ThreatBanner");
            frame = AvFrame.Add(Rect, "Frame", AvChamfer.Diagonal(8f));
            AvLay.Fill(frame.rectTransform);
            frame.raycastTarget = true;
            rail = AvLay.Solid(Rect, "Rail", Color.clear);
            keyText = StrPaint.Fit(Rect, "Key", AvTextRole.Micro, TextAlignmentOptions.Center);
            keyText.text = "DEFCON";
            number = StrPaint.Fit(Rect, "Number", AvTextRole.Display, TextAlignmentOptions.Center);
            word = StrPaint.Fit(Rect, "Word", AvTextRole.Title);
            ladder = new StrStageStrip(Rect, Steps);
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
            bool changed = AvText.Set(number, clamped.ToString(System.Globalization.CultureInfo.InvariantCulture));
            changed |= AvText.Set(word, AvStates.Glyph(s) + WordFor(clamped));
            // The staff's sentence lives on hover; the panel body keeps only the number, the word and the ladder.
            string tip = "DEFCON " + clamped + ": " + assessmentText;
            AvHelpTip.Attach(frame.gameObject, string.IsNullOrEmpty(countsText) ? tip : tip + " · " + countsText);
            ladder.Set(5 - clamped, s);
            if (s != state) { state = s; Restyle(); }
            if (changed) Changed();
        }

        public override float Measure(float width) => BannerHeight;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(rail.rectTransform, 0f, 0f, 3f, s.H);
            AvLay.Place(keyText.rectTransform, PadX, 5f, NumberW, 15f);
            AvLay.Place(number.rectTransform, PadX, 19f, NumberW, 38f);
            float x = PadX + NumberW + 10f, w = s.W - x - PadX;
            AvLay.Place(word.rectTransform, x, 6f, w, 26f);
            ladder.Place(new AvSlot(x, 34f, w, StrStageStrip.Height));
        }

        public override void Restyle()
        {
            Color c = AvInk.State(state);
            frame.Paint(AvInk.Raised, c.WithAlpha(0.7f));
            rail.color = c;
            keyText.color = AvInk.Dim;
            number.color = state == AvState.Ready ? AvInk.Ink : c;
            word.color = state == AvState.Ready ? AvInk.Ink : c;
            ladder.Restyle();
        }
    }

    /// <summary>
    /// Air Tasking Order as one strip of columns, one per mission code: the code and its count on the top
    /// line, the share of observed sorties under it and a share bar on the floor. Columns with nothing tasked
    /// dim instead of disappearing; the task name and the exact figures ride on each column's hover help.
    /// </summary>
    internal sealed class StrAtoBoard : AvPart
    {
        private const float HeadH = 16f, BoxH = 44f, Gap = 3f;
        private readonly TMP_Text head, caption;
        private readonly Column[] columns;
        private int hot = -1;

        private sealed class Column
        {
            public RectTransform Box;
            public AvFrame Frame;
            public TMP_Text Code, Count, Share;
            public AvGaugeGraphic Bar;
        }

        public StrAtoBoard(RectTransform parent, int rows)
        {
            Rect = AvLay.Child(parent, "AtoBoard");
            head = StrPaint.Fit(Rect, "Head", AvTextRole.Micro);
            head.text = "// AIR TASKING ORDER";
            caption = StrPaint.Fit(Rect, "Caption", AvTextRole.Micro, TextAlignmentOptions.MidlineRight);
            columns = new Column[rows];
            for (int i = 0; i < rows; i++)
            {
                var c = new Column { Box = AvLay.Child(Rect, "Ato " + i) };
                c.Frame = AvFrame.Add(c.Box, "Frame", AvChamfer.Diagonal(5f));
                AvLay.Fill(c.Frame.rectTransform);
                c.Frame.raycastTarget = true;
                c.Code = StrPaint.Fit(c.Box, "Code", AvTextRole.DataStrong);
                c.Count = StrPaint.Fit(c.Box, "Count", AvTextRole.DataStrong, TextAlignmentOptions.MidlineRight);
                c.Share = StrPaint.Fit(c.Box, "Share", AvTextRole.DataSmall);
                var go = new GameObject("Bar", typeof(RectTransform), typeof(CanvasRenderer));
                go.transform.SetParent(c.Box, false);
                c.Bar = go.AddComponent<AvGaugeGraphic>();
                c.Bar.Shape = AvGaugeShape.Bar;
                c.Bar.raycastTarget = false;
                columns[i] = c;
            }
            Restyle();
        }

        public int Rows => columns.Length;

        public void SetCaption(string text) => AvText.Set(caption, text);

        /// <summary>Bind one column; <paramref name="share01"/> is the fraction of observed sorties.</summary>
        public void Set(int row, string code, string task, int n, float share01)
        {
            Column c = columns[row];
            AvText.Set(c.Code, code);
            AvText.Set(c.Count, StrPaint.Count(n));
            string share = TheaterReadout.Percent(share01);
            AvText.Set(c.Share, share);
            c.Bar.Value = share01;
            AvHelpTip.Attach(c.Frame.gameObject,
                task + ": " + n + (n == 1 ? " sortie" : " sorties") + ", " + share + " of the friendly AI air tasking observed.");
            StyleColumn(row);
        }

        /// <summary>Highlight the busiest code; -1 for none.</summary>
        public void SetHot(int row)
        {
            if (row == hot) return;
            hot = row;
            Restyle();
        }

        public override float Measure(float width) => HeadH + 3f + BoxH;

        public override void Place(AvSlot s)
        {
            base.Place(s);
            AvLay.Place(head.rectTransform, 0f, 0f, s.W * 0.5f, HeadH);
            AvLay.Place(caption.rectTransform, s.W * 0.5f, 0f, s.W * 0.5f, HeadH);
            float w = AvFlowMath.ColumnWidth(s.W, columns.Length, Gap), inner = Mathf.Max(20f, w - 12f);
            for (int i = 0; i < columns.Length; i++)
            {
                Column c = columns[i];
                AvLay.Place(c.Box, i * (w + Gap), HeadH + 3f, w, BoxH);
                AvLay.Place(c.Code.rectTransform, 6f, 3f, inner * 0.5f, 17f);
                AvLay.Place(c.Count.rectTransform, 6f + inner * 0.5f, 3f, inner * 0.5f, 17f);
                AvLay.Place(c.Share.rectTransform, 6f, 20f, inner, 16f);
                AvLay.Place((RectTransform)c.Bar.transform, 6f, BoxH - 7f, inner, 3f);
            }
        }

        public override void Restyle()
        {
            head.color = AvInk.Muted;
            caption.color = AvInk.Dim;
            for (int i = 0; i < columns.Length; i++) StyleColumn(i);
        }

        private void StyleColumn(int i)
        {
            Column c = columns[i];
            Color key = AvInk.Key;
            bool busy = i == hot;
            bool idle = c.Count.text == "0" || c.Count.text.Length == 0;
            c.Frame.Paint(busy ? key.WithAlpha(0.32f) : AvInk.Inert, busy ? key : AvInk.Hairline);
            c.Code.color = idle ? AvInk.Muted : AvInk.Ink;
            c.Count.color = idle ? AvInk.Muted : AvInk.Ink;
            c.Share.color = AvInk.Dim;
            c.Bar.Track = AvInk.Hairline;
            c.Bar.FillColor = c.Bar.FillEnd = busy ? key : key.WithAlpha(0.7f);
            c.Bar.SetVerticesDirty();
        }
    }

    /// <summary>
    /// The contested-ground list, sized by the page: it shows as many single-line rows as the slot it is given
    /// can hold (its natural height is three rows) and pages past that. A short bay pages, a tall bay lists.
    /// Natural height never depends on the fitted count, so a re-fit cannot re-lay the page.
    /// </summary>
  }
