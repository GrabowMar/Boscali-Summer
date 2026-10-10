using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>
    /// LANCE presentation maths, identical on every peer: the Barrel joint slams back and eases home, and the seven
    /// Charge strips light in sequence with the bank (aft to fore), pulse when full, flash on release and refill while
    /// the bank primes. Heat swings the strip colour from cyan toward orange.
    /// </summary>
    internal static class LancePose
    {
        public const int Strips = 7;
        public const float TravelMin = 0.10f, TravelMax = 0.25f, PeakSeconds = 0.07f, DecaySeconds = 0.35f;
        public const float PulseHz = 6f;

        /// <summary>Metres the barrel travels back for a shot of this power.</summary>
        public static float BarrelTravel(float power01) => TravelMin + (TravelMax - TravelMin) * Mathf.Clamp01(power01);

        /// <summary>Metres BACK along the barrel, secondsSinceShot after release (0 before the shot).</summary>
        public static float BarrelOffset(float secondsSinceShot, float power01)
        {
            if (secondsSinceShot <= 0f) return 0f;
            float travel = BarrelTravel(power01);
            return secondsSinceShot < PeakSeconds
                ? travel * secondsSinceShot / PeakSeconds
                : travel * Mathf.Exp(-(secondsSinceShot - PeakSeconds) / DecaySeconds);
        }

        /// <summary>How much of strip <paramref name="stage"/> is lit at this charge (0..1).</summary>
        public static float StripFill(int stage, float charge01) => Mathf.Clamp01(charge01 * Strips - stage);

        /// <summary>
        /// Emission multiplier for one strip. charging: bank charge; t: scene time (pulse phase); secondsSinceShot and
        /// lastPower: release afterglow (secondsSinceShot &lt; 0 = none); recharge01: 0..1 refill, &lt; 0 = not recharging.
        /// </summary>
        public static float StripIntensity(int stage, float charge01, bool charging, float t, float secondsSinceShot, float lastPower01, float recharge01)
        {
            float v = 0f;
            if (charging)
                v += StripFill(stage, charge01) * 3.2f * (charge01 >= 0.995f ? 0.8f + 0.2f * Mathf.Sin(2f * Mathf.PI * PulseHz * t) : 1f);
            if (secondsSinceShot >= 0f) v += lastPower01 * 2f * Mathf.Exp(-secondsSinceShot / 0.9f);
            if (recharge01 >= 0f) v += 0.35f * Mathf.Clamp01(recharge01 * Strips - stage);
            return v;
        }

        /// <summary>0 = cyan strips, 1 = orange: the rails are running hot.</summary>
        public static float HeatBlend(float heat01) => LanceCapacitor.Smooth(0.5f, 1.1f, heat01);
    }
}
