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
            // s only grows, so both searches resume where the last texel left them (a keyframe passed for one s is passed for every larger s: same answers as a scan from the start).
            int rampFrom = 1, plateauFrom = 0;
            // The level is a pure function of the two covers, and a plateau repeats them for hundreds of texels: remember the last pair (by bits).
            int coverBits = 0, capacityBits = 0;
            float level = 0f;
            bool haveLevel = false;
            // The ends of the ramp being baked (see SkyParams.LerpEnds): the same for all its texels.
            int endsOf = -1;
            SkyParams endA = default, endB = default;
            for (int i = 0; i < baked.Texels; i++)
            {
                float s = (i + 0.5f) * baked.TexelMetres;
                int ramp = RampAt(k, s, ref rampFrom);
                SkyParams row;
                LowGenus genus;
                if (ramp > 0)
                {
                    float t = WeatherMath.Smoothstep(k[ramp].RampStart, k[ramp].RampEnd, s);
                    if (endsOf != ramp) { SkyParams.LerpEnds(k[ramp - 1].Sky, k[ramp].Sky, out endA, out endB); endsOf = ramp; }
                    row = SkyParams.LerpBetween(endA, endB, t);
                    genus = t < 0.5f ? k[ramp - 1].Genus : k[ramp].Genus;
                }
                else
                {
                    int plateau = PlateauAt(k, s, ref plateauFrom);
                    row = k[plateau].Sky;
                    genus = k[plateau].Genus;
                }
                int cb = BitConverter.SingleToInt32Bits(row.CumulusCover), kb = BitConverter.SingleToInt32Bits(row.CapacityCover);
                if (!haveLevel || cb != coverBits || kb != capacityBits)
                {
                    level = CumulusLevels.For(row.CumulusCover, row.CapacityCover);
                    coverBits = cb; capacityBits = kb; haveLevel = true;
                }
                row.CumulusLevel = level;
                baked.SetTexel(i, row, genus);
            }
            baked.Finish();
            baked.DeckAltitude = DeckAltitude(k);
            return baked;
        }

        /// <summary>The keyframe whose ramp contains s, or -1 when s lies on a plateau. Ramps never overlap (the generator keeps segments at least half the sum of their ramps).</summary>
        public static int RampAt(TapeKeyframe[] k, float s)
        {
            int from = 1;
            return RampAt(k, s, ref from);
        }

        /// <summary><see cref="RampAt(TapeKeyframe[], float)"/> for a caller whose s never decreases: <paramref name="from"/> is the first keyframe the scan has not yet passed (start at 1).
        /// A keyframe passed for one s (s at or past its ramp's start and end) is passed for every larger s, so resuming there finds the same ramp.</summary>
        public static int RampAt(TapeKeyframe[] k, float s, ref int from)
        {
            for (int i = from; i < k.Length; i++)
            {
                if (s < k[i].RampStart) return -1;
                if (s < k[i].RampEnd) return i;
                from = i + 1;
            }
            return -1;
        }

        /// <summary>The keyframe that holds at s outside any ramp: the last whose ramp has ended.</summary>
        public static int PlateauAt(TapeKeyframe[] k, float s)
        {
            int j = 0;
            return PlateauAt(k, s, ref j);
        }

        /// <summary><see cref="PlateauAt(TapeKeyframe[], float)"/> for a caller whose s never decreases: <paramref name="j"/> (start at 0) is where the last call ended, and the scan resumes there.</summary>
        public static int PlateauAt(TapeKeyframe[] k, float s, ref int j)
        {
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
