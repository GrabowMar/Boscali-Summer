using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Autopilot.Domain
{
    /// <summary>Greedy word wrap by measured width; a hyphenated token (AAM-4) never splits.</summary>
    internal static class MenuReadoutFit
    {

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
