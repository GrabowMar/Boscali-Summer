using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The sea the ocean shader draws (2026-10-19): which wind the waves follow, a Pierson-Moskowitz wave train over sixteen fixed
    /// wavelengths, and the foam, streak, ripple and roughness levels that go with it, plus how hard a low aircraft throws spray.
    /// Wavelengths never change (a changing wavelength slides the whole pattern); only amplitudes follow the sea. Pure and render-only:
    /// peers need not agree on the waves, so nothing here is networked.
    /// </summary>
    internal static class SeaState
    {
        internal const int WaveCount = 16;
        internal const int SwellCount = 4;
        private const double Gravity = 9.81;
        /// <summary>The longest wave; each next one is <see cref="WaveRatio"/> of the one before (320 m down to ~5 m).</summary>
        private const double LongestWave = 320.0, WaveRatio = 0.76;
        /// <summary>The significant height the waves ease toward and never pass (a soft tanh cap): storms read as storms, but stay below
        /// the decks of the game's ships and the cruise height of its sea-skimming missiles.</summary>
        internal const float HeightCap = 4.5f;
        /// <summary>The wind at which a fully developed sea reaches about the cap (Pierson-Moskowitz Hs = 0.21 U^2 / g).</summary>
        private const double WindCap = 15.5;
        /// <summary>Significant height shared by four crossing swell components, so calm water has short, irregular crests.</summary>
        internal const float SwellHeight = 0.7f;

        internal static double Wavelength(int i) => LongestWave * Math.Pow(WaveRatio, i);

        /// <summary>Separate swell directions in four strata; shorter wind waves cover both sides of the wind without clumping.</summary>
        internal static float DirectionOffset(int i)
        {
            if (i < SwellCount)
                return (i - 1.5f) * 22f + WeatherMath.HashRange(0x5EA5u, i, 0, 0, -6f, 6f);
            float width = 25f + 40f * (i - SwellCount) / (WaveCount - SwellCount - 1f);
            float sample = (i * 0.61803399f + 0.17f) % 1f;
            return (sample * 2f - 1f) * width;
        }

        /// <summary>The wind the sea follows: the mission's own, or the local storm's (cover and rain from the weather field), whichever is stronger.
        /// Full rain under full cover is a 22 m/s gale.</summary>
        internal static float SeaWind(float nativeWind, float cover, float rain)
            => Math.Max(Math.Max(nativeWind, CalmFloor), 5f * Scalar.Clamp01(cover) + 17f * Scalar.Clamp01(rain));

        /// <summary>Without the weather field: the game's own conditions (0 clear, 1 storm) stand in for the storm.</summary>
        internal static float SeaWindFromConditions(float nativeWind, float conditions)
            => Math.Max(Math.Max(nativeWind, CalmFloor), 22f * WeatherMath.Smoothstep(0.75f, 1f, conditions));

        /// <summary>The sea never follows less than a light breeze: a dead calm drew a flat mirror (live check 2026-10-19).</summary>
        internal const float CalmFloor = 3.5f;

        /// <summary>Eases the sea toward its wind: it builds over ~45 s and settles over ~90 s, so a squall line arriving roughens the water
        /// in front of the pilot instead of switching it.</summary>
        internal static float Ease(float current, float target, float dt)
        {
            if (current < 0f) return target;
            float tau = target > current ? 45f : 90f;
            return current + (target - current) * (1f - (float)Math.Exp(-Math.Max(dt, 0f) / tau));
        }

        /// <summary>
        /// Wave amplitudes (metres) for a fully developed sea in this wind, from the Pierson-Moskowitz spectrum sampled at the sixteen
        /// wavelengths. Past ~15 m/s the spectrum stops growing (a soft minimum on the wind) instead of every wave being scaled down,
        /// so a storm keeps a storm's steepness under <see cref="HeightCap"/>; waves shorter than the peak get steeper as the sea
        /// storms (a young, wind-driven chop). The four longest also carry the background swell.
        /// </summary>
        internal static void Amplitudes(float wind, float[] amplitudes)
        {
            double u = Math.Max(wind, 0.5);
            double grown = u / Math.Pow(1.0 + Math.Pow(u / WindCap, 4), 0.25);
            double peak = 0.877 * Gravity / grown;
            double peakLength = 2.0 * Math.PI * Gravity / (peak * peak);
            float chop = 1f + 0.8f * Storminess(wind);
            // Sixteen samples 0.76 apart in wavelength are ~1.147 apart in frequency: each stands for the band halfway to its neighbours.
            double band = (Math.Sqrt(1.0 / WaveRatio) - Math.Sqrt(WaveRatio)) / 2.0;
            for (int i = 0; i < WaveCount; i++)
            {
                double omega = Math.Sqrt(Gravity * 2.0 * Math.PI / Wavelength(i));
                double s = 0.0081 * Gravity * Gravity * Math.Pow(omega, -5) * Math.Exp(-1.25 * Math.Pow(peak / omega, 4));
                amplitudes[i] = (float)(Math.Sqrt(2.0 * s * omega * band) * (Wavelength(i) < 0.7 * peakLength ? chop : 1f));
            }
            // Preserve swell energy (Hs = sqrt(8 sum a^2)) while sharing it across independent directions and periods.
            double swellEnergy = 0;
            for (int i = 0; i < SwellCount; i++) swellEnergy += Math.Exp(-0.6 * i);
            double swell = SwellHeight / Math.Sqrt(8.0 * swellEnergy);
            for (int i = 0; i < SwellCount; i++)
                amplitudes[i] = Math.Max(amplitudes[i], (float)(swell * Math.Exp(-0.3 * i)));
            float hs = SignificantHeight(amplitudes);
            if (hs > HeightCap)
                for (int i = 0; i < WaveCount; i++) amplitudes[i] *= HeightCap / hs;
        }

        internal static float SignificantHeight(float[] amplitudes)
        {
            double sum = 0;
            for (int i = 0; i < amplitudes.Length; i++) sum += amplitudes[i] * amplitudes[i] / 2.0;
            return (float)(4.0 * Math.Sqrt(sum));
        }

        /// <summary>How far crests lean forward (0 round, 1 cusped). The shader scales each wave's lean so the train never folds over.</summary>
        internal static float Steepness(float wind) => WeatherMath.Lerp(0.35f, 0.85f, WeatherMath.Smoothstep(6f, 22f, wind));

        /// <summary>Share of the sea white with foam: Monahan's U^3.41 law, half again so whitecaps read from the air. Tripled, a gale turned
        /// the sea white with dark holes (live check 2026-10-19).</summary>
        internal static float Whitecaps(float wind) => (float)Math.Min(0.35, 1.5 * 3.84e-6 * Math.Pow(Math.Max(wind, 0f), 3.41));

        /// <summary>Where whitecaps start, in standard deviations of the crest compression: the summed compression of many crossing waves is
        /// close to normal, so the share above this many sigmas is the coverage (the normal quantile, Abramowitz-Stegun 26.2.23, error
        /// under 5e-4). The shader measures the sigma of the waves it actually resolves, so the share holds near and far.</summary>
        internal static float WhitecapSigma(float coverage)
        {
            double p = Math.Min(Math.Max(coverage, 1e-6), 0.5);
            double t = Math.Sqrt(-2.0 * Math.Log(p));
            return (float)(t - (2.515517 + 0.802853 * t + 0.010328 * t * t) / (1.0 + 1.432788 * t + 0.189269 * t * t + 0.001308 * t * t * t));
        }

        /// <summary>Wind-driven foam streaks (gale and up, Beaufort 8+).</summary>
        internal static float Streaks(float wind) => WeatherMath.Smoothstep(14f, 22f, wind);

        /// <summary>Strength of the small ripple normals: a breath of wind already wrinkles the surface.</summary>
        internal static float Ripples(float wind) => WeatherMath.Lerp(0.25f, 1f, WeatherMath.Smoothstep(1f, 12f, wind));

        /// <summary>GGX roughness of the surface below the smallest drawn wave.</summary>
        internal static float Roughness(float wind) => WeatherMath.Lerp(0.03f, 0.1f, WeatherMath.Smoothstep(2f, 20f, wind));

        /// <summary>Darker, greener-grey water as the sea builds (0 calm blue, 1 storm).</summary>
        internal static float Storminess(float wind) => WeatherMath.Smoothstep(9f, 22f, wind);

        /// <summary>Highest an aircraft (by its radius) can fly and still kick up spray.</summary>
        /// <summary>Generous on purpose (fun over realism): a fighter at 15 m already throws a plume (12 + 1.2 r drew a wisp, live check 2026-10-19).</summary>
        internal static float SprayCeiling(float radius) => 18f + 1.6f * Math.Max(radius, 0f);

        /// <summary>Spray thrown up behind an aircraft over water, 0..1: jet blast and wingtip vortices need both height under the ceiling and speed.</summary>
        internal static float SprayIntensity(float heightAboveSea, float speed, float radius)
        {
            float low = 1f - Scalar.Clamp01(heightAboveSea / SprayCeiling(radius));
            return low * (float)Math.Sqrt(low) * WeatherMath.Smoothstep(20f, 110f, speed);
        }

        /// <summary>The ruffled, foaming patch on the water under any low aircraft, hovering or fast, 0..1.</summary>
        internal static float WashIntensity(float heightAboveSea, float radius)
            => 1f - Scalar.Clamp01(heightAboveSea / (1.6f * SprayCeiling(radius)));
    }
}
