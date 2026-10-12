using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The altitude bounds of the volume from the baked tape (spec 7.3), by potential extent: puff depth, deck depth, wobble and the tower allowance, not the tops of
    /// actual cells. The window-global bounds of the maps are gone. NearHalfWidth is the near march (100 km) plus the meander twice (2A) plus 2 km; the far bounds are
    /// 240 km plus A. Both are padded by the row's BaseVariation in altitude.
    /// </summary>
    internal static class TapeBounds
    {
        public const float NearHalfWidth = 130000f, FarHalfWidth = 254000f;
        /// <summary>What a Cumulonimbus (anvil) row may add above its deck: the old map ceiling is 16 km, so a tower is bounded by it.</summary>
        public const float TowerAllowance = 9000f;
        /// <summary>WeatherField lowers a base by up to 300 m under rain (smoothstep 0 to 15 mm/h), and a base never goes below 250 m; a non-anvil tower reaches CloudShape.TowerCap = 1.15 times the layer depth.</summary>
        public const float RainLowering = 300f, MinBase = 250f, TowerCapFactor = 1.15f;

        public static void Compute(BakedTape tape, float sCamera, float halfWidth, out float bottom, out float top)
        {
            bottom = float.MaxValue; top = float.MinValue;
            int first = (int)Math.Max(0, Math.Floor((sCamera - halfWidth) / tape.TexelMetres));
            int last = (int)Math.Min(tape.Texels - 1, Math.Ceiling((sCamera + halfWidth) / tape.TexelMetres));
            if (first > last) first = last = Math.Max(0, Math.Min(tape.Texels - 1, first));
            for (int i = first; i <= last; i++)
            {
                SkyParams row = tape.Texel(i);
                float pad = row.BaseVariation;
                float lowTop = row.CloudBase + TowerCapFactor * Math.Max(280f, Math.Max(row.PuffDepth, row.DeckDepth)) + row.BaseWobble + (row.Anvil > 0.05f ? TowerAllowance : 0f);
                bool low = row.SheetCover > 0.02f || row.CumulusCover > 0.02f || row.Convective > 0.02f || row.HorizonCover > 0.02f;
                if (low) { bottom = Math.Min(bottom, Math.Max(MinBase, row.CloudBase - pad - RainLowering)); top = Math.Max(top, lowTop + pad); }
                if (row.Mid.Present) { bottom = Math.Min(bottom, row.Mid.Base); top = Math.Max(top, row.Mid.Top); }
                if (row.High.Present) { bottom = Math.Min(bottom, row.High.Base); top = Math.Max(top, row.High.Top); }
            }
            if (bottom > top) { bottom = 0f; top = 0f; }
        }
    }
}
