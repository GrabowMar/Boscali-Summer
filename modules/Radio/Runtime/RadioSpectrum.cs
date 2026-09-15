using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.Radio.Runtime
{
    /// <summary>A carrier the waterfall should draw: where it is, and how strong it reads.</summary>
    internal readonly struct RadioSignal
    {
        public float Kilohertz { get; }
        public float Strength { get; }

        public RadioSignal(float kilohertz, float strength)
        {
            Kilohertz = kilohertz;
            Strength = strength;
        }
    }

    /// <summary>
    /// Builds one waterfall row: a noise floor with a carrier peak and its skirts where each
    /// station sits. It is a synthetic spectrum, not an FFT — the receiver is a fixture, and
    /// what the player needs to see is the band's shape and their own tuning, cheaply.
    ///
    /// <para>Pure and deterministic for a given <c>time</c>, so a row can be asserted in a
    /// test without a renderer. The presentation layer owns the texture and the scroll.</para>
    /// </summary>
    internal static class RadioSpectrum
    {
        public const int DefaultBins = 96;
        public const int SkirtBins = 3;

        /// <summary>Write one row of magnitudes into <paramref name="magnitudes"/>.</summary>
        public static void Fill(
            float[] magnitudes, IReadOnlyList<RadioSignal> signals,
            int minKilohertz, int maxKilohertz, float noiseFloor, float time)
        {
            if (magnitudes == null || magnitudes.Length == 0) return;

            int bins = magnitudes.Length;
            int frame = (int)(time * 12f);
            for (int bin = 0; bin < bins; bin++)
                magnitudes[bin] = noiseFloor * (0.55f + 0.45f * Noise(bin, frame));

            int span = maxKilohertz - minKilohertz;
            if (span <= 0 || signals == null) return;

            for (int i = 0; i < signals.Count; i++)
            {
                float kilohertz = signals[i].Kilohertz;
                if (kilohertz < minKilohertz || kilohertz > maxKilohertz) continue;

                int centre = (int)Math.Round((kilohertz - minKilohertz) / span * (bins - 1));
                float amplitude = Clamp01(signals[i].Strength);
                for (int offset = -SkirtBins; offset <= SkirtBins; offset++)
                {
                    int bin = centre + offset;
                    if (bin < 0 || bin >= bins) continue;
                    float skirt = 1f - Math.Abs(offset) / (SkirtBins + 0.5f);
                    float value = amplitude * skirt * (0.82f + 0.18f * Noise(bin, frame + 31));
                    if (value > magnitudes[bin]) magnitudes[bin] = value;
                }
            }
        }

        /// <summary>The magnitude at one frequency, for tests and for the tuned-carrier read.</summary>
        public static float Level(float[] magnitudes, float kilohertz, int minKilohertz, int maxKilohertz)
        {
            if (magnitudes == null || magnitudes.Length == 0) return 0f;
            int span = maxKilohertz - minKilohertz;
            if (span <= 0) return 0f;
            int bin = (int)Math.Round((kilohertz - minKilohertz) / span * (magnitudes.Length - 1));
            if (bin < 0 || bin >= magnitudes.Length) return 0f;
            return magnitudes[bin];
        }

        /// <summary>Deterministic value noise in [0,1); the same bin and frame always match.</summary>
        private static float Noise(int bin, int frame)
        {
            unchecked
            {
                uint hash = 2166136261u;
                hash = (hash ^ (uint)bin) * 16777619u;
                hash = (hash ^ (uint)frame) * 16777619u;
                hash ^= hash >> 13;
                hash *= 0x5bd1e995u;
                hash ^= hash >> 15;
                return (hash & 0xFFFFu) / 65536f;
            }
        }

        private static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;
    }
}
