using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The bake of spec 7.2: a tape's keyframes become the 4096-texel array the CPU samples and the shader texture is uploaded from. Each texel is the
    /// exact smoothstep blend of the keyframes either side of the nearest boundary; the genus is that of the keyframe on the near side of the ramp
    /// midpoint, the cumulus level is recomputed from the blended cover and capacity (never lerped), and the horizon deck altitude is the length-weighted
    /// median of the low bases over the tape. The baked array is truth: nothing else evaluates the smoothstep.
    /// </summary>
    internal static class TapeBaker
    {
        /// <summary>The library default deck of a tape with no low layer anywhere: base 1500 m + 0.4 * 500 m (spec 7.3).</summary>
        public const float DefaultDeckAltitude = 1700f;

        public static BakedTape Bake(WeatherTape tape)
        {
            TapeKeyframe[] k = tape.Keyframes;
            var baked = new BakedTape(BakedTape.FullTexels);
            for (int i = 0; i < baked.Texels; i++)
            {
                float s = (i + 0.5f) * baked.TexelMetres;
                int ramp = RampAt(k, s);
                SkyParams row;
                LowGenus genus;
                if (ramp > 0)
                {
                    float t = WeatherMath.Smoothstep(k[ramp].RampStart, k[ramp].RampEnd, s);
                    row = SkyParams.Lerp(k[ramp - 1].Sky, k[ramp].Sky, t);
                    genus = t < 0.5f ? k[ramp - 1].Genus : k[ramp].Genus;
                }
                else
                {
                    int plateau = PlateauAt(k, s);
                    row = k[plateau].Sky;
                    genus = k[plateau].Genus;
                }
                row.CumulusLevel = CumulusLevels.For(row.CumulusCover, row.CapacityCover);
                baked.SetTexel(i, row, genus);
            }
            baked.Finish();
            baked.DeckAltitude = DeckAltitude(k);
            return baked;
        }

        /// <summary>The keyframe whose ramp contains s, or -1 when s lies on a plateau. Ramps never overlap (the generator keeps segments at least half the sum of their ramps).</summary>
        public static int RampAt(TapeKeyframe[] k, float s)
        {
            for (int i = 1; i < k.Length; i++)
            {
                if (s < k[i].RampStart) return -1;
                if (s < k[i].RampEnd) return i;
            }
            return -1;
        }

        /// <summary>The keyframe that holds at s outside any ramp: the last whose ramp has ended.</summary>
        public static int PlateauAt(TapeKeyframe[] k, float s)
        {
            int j = 0;
            while (j + 1 < k.Length && s >= k[j + 1].RampEnd) j++;
            return j;
        }

        /// <summary>The exact keyframe function at s: what the baked array must stay within 0.005 of at every ramp (spec 5.2).</summary>
        public static SkyParams Exact(WeatherTape tape, float s)
        {
            TapeKeyframe[] k = tape.Keyframes;
            int ramp = RampAt(k, s);
            if (ramp < 0) return k[PlateauAt(k, s)].Sky;
            return SkyParams.Lerp(k[ramp - 1].Sky, k[ramp].Sky, WeatherMath.Smoothstep(k[ramp].RampStart, k[ramp].RampEnd, s));
        }

        /// <summary>H = length-weighted median low CloudBase over the keyframes that have a low layer + 0.4 * the median low depth (DeckTop - CloudBase).</summary>
        public static float DeckAltitude(TapeKeyframe[] k)
        {
            var bases = new List<KeyValuePair<float, float>>();
            var depths = new List<KeyValuePair<float, float>>();
            for (int i = 0; i < k.Length; i++)
            {
                if (!k[i].HasLow) continue;
                float end = i + 1 < k.Length ? k[i + 1].S : WeatherTape.SpanMetres;
                float length = Math.Max(1f, end - k[i].S);
                bases.Add(new KeyValuePair<float, float>(k[i].Sky.CloudBase, length));
                depths.Add(new KeyValuePair<float, float>(SkyArchetypes.DeckTop(k[i].Sky) - k[i].Sky.CloudBase, length));
            }
            return bases.Count == 0 ? DefaultDeckAltitude : WeightedMedian(bases) + 0.4f * WeightedMedian(depths);
        }

        public static float WeightedMedian(List<KeyValuePair<float, float>> valueWeight)
        {
            valueWeight.Sort((a, b) => a.Key.CompareTo(b.Key));
            float total = 0f;
            foreach (var p in valueWeight) total += p.Value;
            float half = 0.5f * total, run = 0f;
            foreach (var p in valueWeight)
            {
                run += p.Value;
                if (run >= half) return p.Key;
            }
            return valueWeight[valueWeight.Count - 1].Key;
        }
    }
}
