using System;
using System.Text;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static class MfdSecondaryObjectives
    {
        public const int CardsPerPage = 2;

        public static string PlainObjective(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "OBJECTIVE";
            var text = new StringBuilder(value.Length);
            bool tag = false, space = true;
            foreach (char c in value)
            {
                if (c == '<') { tag = true; continue; }
                if (c == '>') { tag = false; continue; }
                if (tag) continue;
                if (char.IsWhiteSpace(c) || c == '_')
                { if (!space) text.Append(' '); space = true; }
                else { text.Append(c); space = false; }
            }
            return text.ToString().Trim();
        }

        public static int PageCount(int count, int perPage = CardsPerPage) => count <= 0 ? 1 : 1 + (count - 1) / Math.Max(1, perPage);

        public static int ClampPage(int page, int count, int perPage = CardsPerPage) => Math.Max(0, Math.Min(page, PageCount(count, perPage) - 1));

        public static string TimeLabel(bool complete, float secondsRemaining)
        {
            if (complete) return "COMPLETE";
            if (float.IsNaN(secondsRemaining) || float.IsInfinity(secondsRemaining)) return "TIME UNKNOWN";
            if (secondsRemaining <= 0f) return "ENDED";
            int seconds = (int)Math.Ceiling(Math.Min(86400f, secondsRemaining));
            return "LEFT " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
        }
    }
}
