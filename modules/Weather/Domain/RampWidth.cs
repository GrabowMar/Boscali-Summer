using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The contrast-scaled ramp of spec 5.2: the width in metres of the smoothstep between two keyframes,
    /// <c>W = max(12 km, 16.9 km*|dC|, 1.35 km per mm/h*(|dR| + 5*R_hi*|dG|), 2.25 km per km*|dB|, 1.125 km per mm/h*A_m*|dG|)</c>.
    /// The constants make the no-pop caps of 9.6.2 hold by construction at 45 m/s: cover 0.004/s, gated rain 0.05 mm/h per s, base and depth 30 m/s.
    /// </summary>
    internal static class RampWidth
    {
        public const float MinMetres = 12000f;

        public static float Metres(in SkyParams a, in SkyParams b, float markerPeakRain = 0f)
        {
            // Lerp at 0 and 1 gives both rows with an absent layer's geometry inherited from its neighbour: a fade-in sweeps no altitude.
            SkyParams x = SkyParams.Lerp(a, b, 0f, false), y = SkyParams.Lerp(a, b, 1f, false);
            float dC = Max(Math.Abs(x.SheetCover - y.SheetCover), Math.Abs(x.CumulusCover - y.CumulusCover), Math.Abs(x.Mid.Cover - y.Mid.Cover),
                Math.Abs(x.High.Cover - y.High.Cover), Math.Abs(x.Convective - y.Convective));
            float dR = Math.Abs(x.StratRain - y.StratRain), rHi = Math.Max(x.StratRain, y.StratRain);
            float rcA = SkyMath.RainCover(x), rcB = SkyMath.RainCover(y);
            // The gate's slope only matters where the ramp crosses its 0.3-0.6 window (spec 5.2).
            float dG = Math.Min(rcA, rcB) < 0.6f && Math.Max(rcA, rcB) > 0.3f ? Math.Abs(rcA - rcB) : 0f;
            float dB = Max(Math.Abs(x.CloudBase - y.CloudBase), Math.Abs(ArchetypeTop(x) - ArchetypeTop(y)), Math.Abs(x.DeckDepth - y.DeckDepth),
                Math.Abs(x.Mid.Base - y.Mid.Base), Math.Abs(x.Mid.Top - y.Mid.Top), Math.Abs(x.High.Base - y.High.Base), Math.Abs(x.High.Top - y.High.Top)) * 0.001f;
            float km = Max(12f, 16.9f * dC, 1.35f * (dR + 5f * rHi * dG), 2.25f * dB, 1.125f * markerPeakRain * dG);
            return km * 1000f;
        }

        private static float ArchetypeTop(in SkyParams s) => SkyArchetypes.DeckTop(s);

        private static float Max(params float[] values)
        {
            float m = values[0];
            for (int i = 1; i < values.Length; i++) if (values[i] > m) m = values[i];
            return m;
        }
    }
}
