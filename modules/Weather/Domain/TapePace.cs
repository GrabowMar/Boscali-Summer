using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// How fast the weather tape slides (spec 5.4): a pace 1-6 picked by the host, scaled by the mission wind and
    /// clamped to 18-45 m/s. The console shows the result so a fast sky is never mistaken for a bug.
    /// </summary>
    internal static class TapePace
    {
        public const int MinPace = 1, MaxPace = 6, DefaultPace = 3;
        public const float MinSpeed = 18f, MaxSpeed = 45f;

        private static readonly float[] PaceSpeed = { 14f, 20f, 27f, 34f, 40f, 45f };

        /// <summary>0.85 at a 6 m/s mission wind or less, 1.15 at 18 m/s or more.</summary>
        public static float WindFactor(float missionWind)
        {
            float wind = float.IsNaN(missionWind) ? 6f : Scalar.Clamp(missionWind, 6f, 18f);
            return Scalar.Lerp(0.85f, 1.15f, (wind - 6f) / 12f);
        }

        /// <summary>System speed in m/s for a pace and the mission's wind speed.</summary>
        public static float Speed(int pace, float missionWind)
        {
            int index = (pace < MinPace ? MinPace : pace > MaxPace ? MaxPace : pace) - 1;
            return Scalar.Clamp(PaceSpeed[index] * WindFactor(missionWind), MinSpeed, MaxSpeed);
        }
    }
}
