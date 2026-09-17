using System;
using System.Globalization;

namespace BoscaliSummer.Features.DynamicOperations.Domain
{
    /// <summary>
    /// How urgent a contract reads. Presentation maps it onto the vanilla avionics rails;
    /// the domain stays free of colours.
    /// </summary>
    internal enum MarkerTone
    {
        Info,
        Caution,
        Ready
    }

    /// <summary>
    /// What a contract is asking for right now: close the distance, hold the area, or bring
    /// the acquired report home.
    /// </summary>
    internal enum MarkerField
    {
        Approach,
        Hold,
        Deliver
    }

    /// <summary>
    /// The copy every contract marker prints — map plate, cockpit overlay and vicinity card
    /// share it so one contract reads the same on all three surfaces. Pure: the test project
    /// links this file directly.
    /// </summary>
    internal static class OperationMarkerCopy
    {
        public const float UrgentSeconds = 120f;
        public const float VicinityFloorMetres = 20000f;
        public const float VicinityRadiusFactor = 4f;

        /// <summary>Title line: "#5 SURVEY THE AFTERMATH".</summary>
        public static string Title(int id, string title) =>
            "#" + id + " " + (string.IsNullOrEmpty(title) ? "SECONDARY OBJECTIVE" : title);

        /// <summary>A returning contract lands its report in the same aircraft.</summary>
        public static bool Returning(string status) =>
            !string.IsNullOrEmpty(status) && status.IndexOf("RETURN", StringComparison.Ordinal) >= 0;

        public static MarkerField Field(bool inside, bool returning) =>
            !inside ? MarkerField.Approach : returning ? MarkerField.Deliver : MarkerField.Hold;

        /// <summary>
        /// Detail line: family, then what the contract asks for, then the clock when one is
        /// running. A part the host cannot report is dropped rather than guessed.
        /// </summary>
        public static string Detail(string family, float distanceMetres, float radius, float secondsRemaining, float progress, MarkerField field, bool metric)
        {
            string state = field switch
            {
                MarkerField.Hold => "HOLD " + Percent(progress),
                MarkerField.Deliver => "LAND TO DELIVER",
                _ => Finite(distanceMetres)
                    ? Distance(distanceMetres, metric) + (Finite(radius) && radius > 0f ? " TO AREA" : "")
                    : ""
            };
            string text = string.IsNullOrEmpty(state)
                ? family
                : state.StartsWith(family, StringComparison.Ordinal) ? state : family + " · " + state;
            string clock = Clock(secondsRemaining);
            return string.IsNullOrEmpty(clock) ? text : (string.IsNullOrEmpty(text) ? clock : text + " · " + clock);
        }

        /// <summary>The vicinity card's cue bar: closing on the edge, then the hold itself.</summary>
        public static float Bar(float distanceMetres, float radius, bool inside, float progress)
        {
            if (inside) return Finite(progress) ? (progress < 0f ? 0f : progress > 1f ? 1f : progress) : 0f;
            return Approach(distanceMetres, radius);
        }

        /// <summary>
        /// Vanilla's own distance reading (<c>UnitConverter.DistanceReading</c>), so a contract
        /// reads exactly like an objective: metric switches to kilometres past a kilometre and
        /// drops the decimal past ten, imperial reads yards then nautical miles. The culture is
        /// the caller's, so the runtime follows the machine and a test can pin it.
        /// </summary>
        public static string Distance(float metres, bool metric, IFormatProvider culture = null)
        {
            if (!Finite(metres) || metres < 0f) return "—";
            IFormatProvider provider = culture ?? CultureInfo.CurrentCulture;
            if (metric)
            {
                if (metres > 10000f) return (metres * 0.001f).ToString("F0", provider) + "km";
                if (metres > 1000f) return (metres * 0.001f).ToString("F1", provider) + "km";
                return metres.ToString("F0", provider) + "m";
            }

            float yards = metres * 1.09361f;
            return yards < 1000f
                ? yards.ToString("F0", provider) + "yd"
                : (metres * 0.000539957f).ToString("F1", provider) + "nm";
        }

        /// <summary>
        /// Vanilla's area-ring fade: solid once a ring is inside a thirteenth of its radius in
        /// range (radius/distance = 1/13.3), gone past forty times, linear in between.
        /// </summary>
        public static float RingAlpha(float radius, float distance)
        {
            if (!Finite(radius) || !Finite(distance) || radius <= 0f || distance <= 0.01f) return 0f;
            float value = radius * 20f / distance - 0.5f;
            return value < 0f ? 0f : value > 1f ? 1f : value;
        }

        /// <summary>Countdown text, or empty when no clock is running.</summary>
        public static string Clock(float seconds)
        {
            if (!Finite(seconds) || seconds < 0f) return "";
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

        /// <summary>
        /// A contract returning to base is green, one whose clock is nearly out is amber,
        /// everything else is cyan. The status word itself still prints, so colour is never
        /// the only carrier.
        /// </summary>
        public static MarkerTone Tone(string status, float secondsRemaining)
        {
            if (Returning(status)) return MarkerTone.Ready;
            if (Finite(secondsRemaining) && secondsRemaining > 0f && secondsRemaining <= UrgentSeconds)
                return MarkerTone.Caution;
            return MarkerTone.Info;
        }

        /// <summary>
        /// Outer edge of the contract's vicinity: the radius plus a generous floor, so a
        /// contract flown toward is called out tens of kilometres out rather than only in
        /// the last few seconds. Point contracts (no area) still get the floor.
        /// </summary>
        public static float VicinityBand(float radius)
        {
            float safe = Finite(radius) && radius > 0f ? radius : 0f;
            return safe + Math.Max(safe * VicinityRadiusFactor, VicinityFloorMetres);
        }

        /// <summary>0 at the band edge, 1 at the area edge; the vicinity card's cue bar.</summary>
        public static float Approach(float distanceMetres, float radius)
        {
            if (!Finite(distanceMetres) || !Finite(radius) || radius < 0f) return 0f;
            float band = VicinityBand(radius) - radius;
            if (band <= 0f) return 0f;
            float remaining = distanceMetres - radius;
            float ratio = 1f - remaining / band;
            return ratio < 0f ? 0f : ratio > 1f ? 1f : ratio;
        }

        internal static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
