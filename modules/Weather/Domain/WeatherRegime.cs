using System;

namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// The sky the model can be in, ordered light to heavy so the index doubles as severity.
    /// Every band is expressed in the units the vanilla <c>LevelInfo</c> setters take, and the
    /// bands bracket the authored mission beats in <c>tools/build-boscali-summer-mission.ps1</c>.
    /// </summary>
    internal enum WeatherRegime
    {
        Clear,
        Fair,
        Haze,
        Overcast,
        Squall,
        Storm
    }

    internal static class WeatherRegimes
    {
        public const int Count = 6;

        private static readonly string[] Labels = { "CLEAR", "FAIR", "HAZE", "OVERCAST", "SQUALL", "STORM" };

        private static readonly float[] ConditionsLow = { 0.00f, 0.10f, 0.22f, 0.42f, 0.62f, 0.82f };
        private static readonly float[] ConditionsHigh = { 0.10f, 0.22f, 0.42f, 0.62f, 0.82f, 1.00f };
        private static readonly float[] CloudBaseLow = { 2600f, 2200f, 1800f, 1500f, 1000f, 550f };
        private static readonly float[] CloudBaseHigh = { 3200f, 2800f, 2400f, 2100f, 1600f, 1000f };
        private static readonly float[] WindLow = { 2f, 3f, 5f, 7f, 11f, 15f };
        private static readonly float[] WindHigh = { 4f, 6f, 8f, 11f, 16f, 23f };
        private static readonly float[] TurbulenceLow = { 0.03f, 0.05f, 0.10f, 0.18f, 0.38f, 0.60f };
        private static readonly float[] TurbulenceHigh = { 0.08f, 0.12f, 0.25f, 0.38f, 0.65f, 0.95f };

        public static int Index(WeatherRegime regime)
        {
            int index = (int)regime;
            return index < 0 ? 0 : index >= Count ? Count - 1 : index;
        }

        public static WeatherRegime FromIndex(int index)
        {
            if (index < 0) return WeatherRegime.Clear;
            return index >= Count ? WeatherRegime.Storm : (WeatherRegime)index;
        }

        public static string Label(WeatherRegime regime) => Labels[Index(regime)];

        /// <summary>Squall and Storm are the two regimes the panel paints as a hazard.</summary>
        public static bool IsSevere(WeatherRegime regime) => Index(regime) >= Index(WeatherRegime.Squall);

        /// <summary>Inverse of the band table: which regime a conditions value renders as.</summary>
        public static WeatherRegime FromConditions(float conditions)
        {
            if (float.IsNaN(conditions)) return WeatherRegime.Clear;
            for (int i = 0; i < Count; i++)
                if (conditions < ConditionsHigh[i]) return (WeatherRegime)i;
            return WeatherRegime.Storm;
        }

        public static float ConditionsLo(int index) => ConditionsLow[Clamp(index)];
        public static float ConditionsHi(int index) => ConditionsHigh[Clamp(index)];
        public static float CloudBaseLo(int index) => CloudBaseLow[Clamp(index)];
        public static float CloudBaseHi(int index) => CloudBaseHigh[Clamp(index)];
        public static float WindLo(int index) => WindLow[Clamp(index)];
        public static float WindHi(int index) => WindHigh[Clamp(index)];
        public static float TurbulenceLo(int index) => TurbulenceLow[Clamp(index)];
        public static float TurbulenceHi(int index) => TurbulenceHigh[Clamp(index)];

        public static float Clamp01(float value) =>
            float.IsNaN(value) ? 0f : Math.Max(0f, Math.Min(1f, value));

        private static int Clamp(int index) => index < 0 ? 0 : index >= Count ? Count - 1 : index;
    }
}
