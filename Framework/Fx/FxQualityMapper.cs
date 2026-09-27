namespace BoscaliSummer.Framework.Fx
{
    /// <summary>Client-effects quality tier, mapped from the game's own quality level.</summary>
    internal enum FxQuality
    {
        Low,
        Medium,
        High
    }

    /// <summary>Multipliers one tier applies to scalable budgets (1 = full).</summary>
    internal readonly struct FxScales
    {
        public float RenderTargets { get; }
        public float Particles { get; }
        public float Sway { get; }
        public float Voices { get; }

        public FxScales(float renderTargets, float particles, float sway, float voices)
        {
            RenderTargets = renderTargets;
            Particles = particles;
            Sway = sway;
            Voices = voices;
        }
    }

    /// <summary>
    /// Pure mapping from the vanilla quality level to the effects tier. No new setting: the
    /// player already picks Low/Medium/High for the game, and effects follow it.
    /// </summary>
    internal static class FxQualityMapper
    {
        public static FxQuality Map(int qualityLevel, int levelCount)
        {
            if (levelCount <= 1) return FxQuality.High;
            if (qualityLevel <= 0) return FxQuality.Low;
            if (qualityLevel >= levelCount - 1) return FxQuality.High;
            float t = (float)qualityLevel / (levelCount - 1);
            if (t < 0.34f) return FxQuality.Low;
            if (t < 0.67f) return FxQuality.Medium;
            return FxQuality.High;
        }

        public static FxScales ScalesFor(FxQuality quality)
        {
            switch (quality)
            {
                case FxQuality.Low: return new FxScales(0.5f, 0.5f, 0.5f, 0.5f);
                case FxQuality.Medium: return new FxScales(0.75f, 0.75f, 0.75f, 0.75f);
                default: return new FxScales(1f, 1f, 1f, 1f);
            }
        }
    }
}
