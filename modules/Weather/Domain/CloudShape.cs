using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// One low-cloud genus after <see cref="CloudShape.Resolve"/> fills in anything the
    /// caller left at zero. Sheet and tower weights blend continuously during a state fade.
    /// The same three formulas are copied in FlightCloud.shader (ShapeProfile,
    /// ShapeFootprint, BaseGate). Change both together.
    /// </summary>
    internal readonly struct CloudGenus
    {
        public CloudGenus(float puffScale, float puffDepth, float baseSharp, float baseWobble,
            float dome, float billow, float anvil, float sheetBlend)
        {
            PuffScale = puffScale;
            PuffDepth = puffDepth;
            BaseSharp = baseSharp;
            BaseWobble = baseWobble;
            Dome = dome;
            Billow = billow;
            Anvil = anvil;
            SheetBlend = sheetBlend;
        }

        /// <summary>Metres across one primary lobe, not the repeating noise tile.</summary>
        public float PuffScale { get; }
        public float PuffDepth { get; }
        /// <summary>Metres of the base transition. Small is a flat cumulus base.</summary>
        public float BaseSharp { get; }
        /// <summary>Per-puff base rise and fall, metres.</summary>
        public float BaseWobble { get; }
        /// <summary>0 keeps the top, 1 rounds it off, above 1 also pinches the middle of a tower.</summary>
        public float Dome { get; }
        /// <summary>How hard detail noise eats the boundary. The CPU mirror does not erode.</summary>
        public float Billow { get; }
        /// <summary>0 none. Near 1 the top of a tower spreads into an anvil.</summary>
        public float Anvil { get; }

        public float SheetBlend { get; }
        public float TowerBlend => WeatherMath.Smoothstep(0.18f, 0.60f, Anvil);
        public bool Sheet => SheetBlend >= 0.999f;
    }

    /// <summary>Vertical profile of a cloud genus. Pure, so the shadow cookie and the tests share it.</summary>
    internal static class CloudShape
    {
        /// <summary>Clear the ordinary layers inside a hurricane eye above its low floor.
        /// The separate eyewall is never cut away. Mirrored by EyeCloudKeep in the shader.</summary>
        public static float EyeCloudKeep(float x, float y, float z, float eyeX, float eyeZ, float radius, float strength)
        {
            if (radius <= 0f || strength <= 0f) return 1f;
            float r = Scalar.Distance2D(x, z, eyeX, eyeZ);
            return 1f - strength * (1f - WeatherMath.Smoothstep(radius * 0.65f, radius * 0.98f, r)) *
                WeatherMath.Smoothstep(1500f, 2300f, y);
        }

        public static CloudGenus Resolve(StateParams sky)
        {
            float smooth = WeatherMath.Clamp01(sky.LayerSmooth);
            // Do not shrink lobes to zero on the way to a sheet. Fade their relief instead.
            float scale = sky.PuffScale > 0f ? Math.Max(1000f, sky.PuffScale) : 1800f;
            float depth = sky.PuffDepth >= 200f ? sky.PuffDepth : Math.Max(300f, sky.LayerDepth > 0f ? sky.LayerDepth : 900f);
            float sharp = sky.BaseSharp >= 10f ? sky.BaseSharp : WeatherMath.Lerp(50f, 180f, smooth);
            float wobble = sky.BaseWobble >= 1f ? sky.BaseWobble : WeatherMath.Lerp(140f, 40f, smooth);
            float dome = sky.Dome > 0f ? sky.Dome : WeatherMath.Lerp(0.9f, 0.2f, smooth);
            float billow = sky.Billow > 0f ? sky.Billow : WeatherMath.Lerp(0.8f, 0.22f, smooth);
            return new CloudGenus(scale, depth, sharp, wobble, dome, billow, sky.Anvil,
                WeatherMath.Smoothstep(0.45f, 0.85f, smooth));
        }

        /// <summary>Only a deep local column carries an anvil. A shallow dry cluster under
        /// a storm shield remains rounded. Mirrored by LocalAnvil in the shader.</summary>
        public static float LocalAnvil(float depth, float anvil)
            => WeatherMath.Clamp01(anvil) * WeatherMath.Smoothstep(2400f, 6500f, depth);

        /// <summary>Shallow cumulus rounds over; only a developed anvil pinches its stem.</summary>
        public static float LocalDome(float dome, float anvil)
            => WeatherMath.Lerp(Math.Min(1f, dome), dome, WeatherMath.Smoothstep(0.18f, 0.60f, anvil));

        /// <summary>Height within one resolved body; zero for a degenerate/empty interval.</summary>
        public static float LightingHeight(float y, float bottom, float top)
            => top > bottom ? WeatherMath.Clamp01((y - bottom) / (top - bottom)) : 0f;

        /// <summary>
        /// Density kept at height fraction <paramref name="h"/> (0 base, 1 top).
        /// The height gradient eats the top; an anvil puts a shield back in the top quarter.
        /// </summary>
        public static float Profile(float h, float dome, float anvil)
        {
            if (h <= 0f || h >= 1f) return 0f;
            float round = WeatherMath.Smoothstep(0.18f, 1f, h);
            float body = 1f - round * WeatherMath.Clamp(dome, 0f, 1f);
            float pinch = Math.Max(0f, dome - 1f) * WeatherMath.Smoothstep(0.15f, 0.55f, h);
            float cap = WeatherMath.Smoothstep(0.64f, 0.80f, h);
            return WeatherMath.Clamp01(body - pinch + anvil * cap) *
                (1f - WeatherMath.Smoothstep(0.90f, 1f, h));
        }

        /// <summary>A filled interior with a rounded silhouette. Multiplying all the shape
        /// fields made small clouds transparent throughout, like smoke. Subtract the dome
        /// from the boundary instead, leaving a dense core. Mirrored by ShapeMass.</summary>
        public static float Mass(float mask, float profile)
        {
            return WeatherMath.Clamp01((mask - (1f - profile) * 0.8f) * 2.6f) *
                WeatherMath.Smoothstep(0f, 0.18f, profile);
        }

        /// <summary>Horizontal radius scale. 1 at the base; the anvil widens the top.</summary>
        public static float Footprint(float h, float anvil)
        {
            float cap = WeatherMath.Smoothstep(0.58f, 0.86f, WeatherMath.Clamp01(h));
            return 1f + anvil * cap * 1.7f;
        }

        /// <summary>0 below the base, 1 once <paramref name="sharp"/> metres above it.</summary>
        public static float BaseGate(float y, float baseY, float sharp)
        {
            float s = Math.Max(20f, sharp);
            return WeatherMath.Smoothstep(baseY - s * 0.2f, baseY + s, y);
        }

        /// <summary>Visual top of a non-anvil tower: the genus depth, not the cell's rain column.</summary>
        public static float TowerCap(float baseY, CloudGenus genus, float layerDepth)
        {
            return baseY + Math.Max(280f, Math.Max(genus.PuffDepth, layerDepth)) * 1.15f;
        }
    }
}
