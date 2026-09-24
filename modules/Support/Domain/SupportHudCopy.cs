using System;
using System.Globalization;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Features.Support.Domain
{
    /// <summary>
    /// The support-readiness widget's copy and tone, pure so the test project links it directly.
    /// It states only what the local peer already knows: whether a request is in flight, how
    /// long the net is cooling, and the local allocation.
    /// </summary>
    internal static class SupportHudCopy
    {
        /// <summary>Below this the cooldown reads as done; matches the panel's own threshold.</summary>
        public const float CoolingSeconds = 0.5f;

        /// <summary>"T-12s" / "T-1:05"; empty when nothing is cooling.</summary>
        public static string Cooldown(float seconds)
        {
            if (!Finite(seconds) || seconds <= 0f) return "";
            int total = (int)Math.Ceiling(seconds);
            if (total < 60) return "T-" + total + "s";
            return "T-" + (total / 60) + ":" + (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public static string Percent(float ratio)
        {
            if (!Finite(ratio)) return "—";
            float clamped = ratio < 0f ? 0f : ratio > 1f ? 1f : ratio;
            return ((int)Math.Round(clamped * 100f, MidpointRounding.AwayFromZero))
                .ToString(CultureInfo.InvariantCulture) + "%";
        }

        public static HudTone Tone(bool pending, float cooldown)
        {
            if (pending) return HudTone.Caution;
            return Finite(cooldown) && cooldown > CoolingSeconds ? HudTone.Caution : HudTone.Info;
        }

        /// <summary>While cooling the bar drains toward ready; otherwise it is the allocation.</summary>
        public static float Bar(float remaining, float total, float allocation)
        {
            if (Finite(remaining) && Finite(total) && total > 0f && remaining > 0f)
                return Clamp(1f - remaining / total);
            return Clamp(Finite(allocation) ? allocation : 0f);
        }

        private static float Clamp(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
