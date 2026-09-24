using System;

namespace BoscaliSummer.Features.Radio.Domain
{
    internal enum InterceptKind : byte
    {
        Cookoff = 0,
        Alarm = 1,
        Staff = 2,
        Event = 3
    }

    /// <summary>When a battlefield beat is allowed onto the receiver wire. Pure.</summary>
    internal static class CombatInterceptNet
    {
        public const int LiveCap = 4;
        public const float GlobalGap = 1.5f;
        public const float SourceGap = 8f;
        public const int LineMax = 64;
        public const float MaxRangeKm = 7f;

        public static string KindWord(InterceptKind kind)
        {
            switch (kind)
            {
                case InterceptKind.Cookoff: return "COOKOFF";
                case InterceptKind.Alarm: return "ALARM";
                case InterceptKind.Staff: return "STAFF";
                default: return "FLASH";
            }
        }

        public static string Line(InterceptKind kind, string distanceText)
        {
            string word = KindWord(kind);
            string text = string.IsNullOrEmpty(distanceText)
                ? "INTERCEPT · " + word
                : "INTERCEPT · " + word + " " + distanceText;
            return text.Length <= LineMax ? text : text.Substring(0, LineMax);
        }

        public static bool InRange(float distanceKm) =>
            distanceKm >= 0f && distanceKm <= MaxRangeKm && !float.IsNaN(distanceKm);

        public static bool TryAccept(
            int liveCount, float now, float lastGlobal, float lastSource,
            float quality, float squelch)
        {
            if (liveCount >= LiveCap) return false;
            if (now < lastGlobal + GlobalGap) return false;
            if (now < lastSource + SourceGap) return false;
            if (float.IsNaN(quality) || quality < squelch) return false;
            return true;
        }

        public static int SourceKey(InterceptKind kind, int sourceId) =>
            ((int)kind << 24) ^ sourceId;
    }
}
