using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Autopilot.Domain
{
    /// <summary>How the hub's action plate sets its text so nothing is ever clipped: title size, title wrap, description lines.</summary>
    internal readonly struct ReadoutFit
    {
        public ReadoutFit(float titleSize, int titleLines, int descLines, float height)
        {
            TitleSize = titleSize; TitleLines = titleLines; DescLines = descLines; Height = height;
        }

        /// <summary>Pixels (already scaled).</summary>
        public float TitleSize { get; }
        public int TitleLines { get; }
        public int DescLines { get; }
        /// <summary>Height of the action plate for this text.</summary>
        public float Height { get; }
    }

    /// <summary>One "KEY VALUE" cell of the hub's status summary, placed by <see cref="MenuReadoutFit.Flow"/>.</summary>
    internal readonly struct SummaryCell
    {
        public SummaryCell(int line, float x, float keyW, string key, string value, bool caution)
        {
            Line = line; X = x; KeyW = keyW; Key = key; Value = value; Caution = caution;
        }

        public int Line { get; }
        /// <summary>From the plate's inner left edge, pixels.</summary>
        public float X { get; }
        public float KeyW { get; }
        public string Key { get; }
        public string Value { get; }
        public bool Caution { get; }
    }

    /// <summary>
    /// The prototype's fitText / wrap rules for the hub action plate: a title shrinks from 17 to 12 px, then wraps to two lines at 15 px;
    /// the description takes two lines (three in the compact 720p layout while the title stays on one). Greedy word wrap, a hyphenated
    /// token (AAM-4) never splits.
    /// </summary>
    internal static class MenuReadoutFit
    {
        public const float MinTitle = 12f;
        public const float SummaryGap = 3f;

        /// <summary>Height of one summary line, pixels.</summary>
        public static float SummaryLine(float t) => 13f * t * MenuFonts.LinePitch;

        /// <summary>
        /// Status summary cells flowed left to right and wrapped by measured width (a fixed grid clips at 720p), each line centred in
        /// <paramref name="width"/>. <paramref name="lines"/> is the line count; the plate needs <c>lines * SummaryLine(t) + (lines - 1) * SummaryGap * t</c>.
        /// </summary>
        public static List<SummaryCell> Flow(List<MenuSummary> items, float width, IMenuMetrics m, float t, out int lines)
        {
            var cells = new List<SummaryCell>(items.Count);
            float space = m.Width(" ", MenuFont.Badge) * t, gap = 2f * space;
            var widths = new List<float>(items.Count);
            var keyWs = new List<float>(items.Count);
            foreach (MenuSummary s in items)
            {
                float kw = m.Width(s.Key, MenuFont.Badge) * t;
                keyWs.Add(kw);
                widths.Add(kw + space + m.Width(s.Value, MenuFont.Badge) * t);
            }
            lines = items.Count == 0 ? 0 : 1;
            int start = 0, line = 0;
            float used = 0f;
            for (int i = 0; i <= items.Count; i++)
            {
                bool wrap = i < items.Count && i > start && used + gap + widths[i] > width;
                if (i < items.Count && !wrap) { used += (i > start ? gap : 0f) + widths[i]; continue; }
                float x = (width - used) / 2f;
                for (int k = start; k < i; k++)
                {
                    cells.Add(new SummaryCell(line, x, keyWs[k], items[k].Key, items[k].Value, items[k].Caution));
                    x += widths[k] + gap;
                }
                if (i >= items.Count) break;
                line++; lines = line + 1; start = i; used = widths[i];
            }
            return cells;
        }

        public static ReadoutFit Fit(MenuLayout l, IMenuMetrics m, string title, string desc)
        {
            float t = l.T, pad = (l.ReadoutCompact ? 4f : 6f) + 1f;
            float avail = l.Readout.W - 2f * pad;
            float w = m.Width(title, MenuFont.Row) * t;
            float size = 17f * t;
            int titleLines = 1;
            if (w > avail)
            {
                float shrunk = 17f * t * avail / w;
                if (shrunk >= MinTitle) size = shrunk;
                else
                {
                    size = 15f * t;
                    titleLines = Lines(title, avail, m, MenuFont.Plate, t);
                }
            }
            int maxDesc = l.ReadoutCompact && titleLines == 1 ? 3 : 2;
            int descLines = Math.Min(maxDesc, Math.Max(desc == null || desc.Length == 0 ? 0 : 1, Lines(desc, avail, m, MenuFont.State, t)));
            float height = (l.ReadoutCompact ? 6f : 9f) * t + 2f + size * MenuFonts.LinePitch * titleLines + descLines * 13f * t * MenuFonts.LinePitch + 1f;
            height = Math.Max(height, l.ReadoutCompact ? 0f : l.ReadoutActH);
            return new ReadoutFit(size, titleLines, descLines, height);
        }

        /// <summary>Lines the text needs at <paramref name="width"/>; 0 for empty text. A token wider than the line counts as one line.</summary>
        public static int Lines(string text, float width, IMenuMetrics m, MenuFont font, float t)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            int lines = 1;
            float used = 0f, space = m.Width(" ", font) * t;
            foreach (string word in text.Split(' '))
            {
                float ww = m.Width(word, font) * t;
                if (used == 0f) used = ww;
                else if (used + space + ww <= width) used += space + ww;
                else { lines++; used = ww; }
            }
            return lines;
        }

        /// <summary>The words of the greedy wrap, one string per line (the view sets them with explicit breaks so TMP never re-wraps differently).</summary>
        public static List<string> Wrap(string text, float width, IMenuMetrics m, MenuFont font, float t)
        {
            var lines = new List<string>(3);
            if (string.IsNullOrEmpty(text)) return lines;
            string line = "";
            float used = 0f, space = m.Width(" ", font) * t;
            foreach (string word in text.Split(' '))
            {
                float ww = m.Width(word, font) * t;
                if (line.Length == 0) { line = word; used = ww; }
                else if (used + space + ww <= width) { line += " " + word; used += space + ww; }
                else { lines.Add(line); line = word; used = ww; }
            }
            lines.Add(line);
            return lines;
        }
    }
}
