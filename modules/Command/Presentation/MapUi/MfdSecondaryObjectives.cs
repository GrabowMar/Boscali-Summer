using System;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static class MfdSecondaryObjectives
    {
        public const int CardsPerPage = 2;

        public static int PageCount(int count) => count <= 0 ? 1 : 1 + (count - 1) / CardsPerPage;

        public static int ClampPage(int page, int count) => Math.Max(0, Math.Min(page, PageCount(count) - 1));

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
