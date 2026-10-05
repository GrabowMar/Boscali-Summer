using System.Globalization;

namespace BoscaliSummer.Modules.Wing.Domain.Pure
{
    /// <summary>The words of BEHAVIOUR › SORTIE: a step's timing against its plan, and how an event line reads on the timeline.</summary>
    internal static class SortieWords
    {
        /// <summary>Within this many seconds of its plan a step is on time.</summary>
        public const float OnTime = 5f;

        /// <summary>"m:ss" (minutes unbounded, never negative); "-" for NaN.</summary>
        public static string Clock(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return "-";
            int s = (int)System.Math.Round(System.Math.Max(0f, seconds));
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>"LATE 0:54", "EARLY 0:12", "ON TIME"; empty when either time is unknown.</summary>
        public static string Diff(float planned, float actual)
        {
            if (float.IsNaN(planned) || float.IsNaN(actual)) return "";
            float d = actual - planned;
            return System.Math.Abs(d) < OnTime ? "ON TIME" : (d > 0f ? "LATE " : "EARLY ") + Clock(System.Math.Abs(d));
        }

        /// <summary>The tone of an event line: 2 danger (a loss, a failure), 1 caution (bingo, winchester, late, falling behind),
        /// 0 everything else.</summary>
        public static int Tone(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            string t = text.ToLowerInvariant();
            if (t.Contains("lost") || t.Contains("failed") || t.Contains("unable") || t.Contains("shot down") || t.Contains("crash") || t.Contains("ejected")) return 2;
            if (t.Contains("bingo") || t.Contains("joker") || t.Contains("winchester") || t.Contains("late") || t.Contains("behind") || t.Contains("damaged")) return 1;
            return 0;
        }

        /// <summary>"T+2:42" while a plan runs and the time is after EXECUTE; else the plain mission clock "12:30".</summary>
        public static string Stamp(float time, bool live, float executedAt) =>
            live && time >= executedAt ? "T+" + Clock(time - executedAt) : Clock(time);
    }
}
