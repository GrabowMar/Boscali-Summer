namespace NOAvionics
{
    /// <summary>Effect ids — the shader contract of NOA/UI/Fx (P0). Do not renumber.</summary>
    public enum AvFxKind { None = 0, Shine = 1, Glow = 2, Dissolve = 3, Scan = 4, Pulse = 5 }

    public enum AvFxTier { Off = 0, Lite = 1, Full = 2 }

    /// <summary>
    /// Settings-to-vertex rules for effects, engine-free. UIEffect's technique: parameters ride in
    /// vertex UVs so every effected Graphic shares one material and still batches.
    /// </summary>
    public static class AvFxPacking
    {
        public static float DurationOf(AvFxKind k)
        {
            switch (k)
            {
                case AvFxKind.Shine: return 0.40f;
                case AvFxKind.Dissolve: return 0.35f;
                case AvFxKind.Scan: return 0.25f;
                default: return 0f;
            }
        }

        public static bool IsTimed(AvFxKind k) => DurationOf(k) > 0f;

        public static AvFxKind Resolve(AvFxKind requested, AvFxTier tier, bool reducedMotion)
        {
            if (tier == AvFxTier.Off || requested == AvFxKind.None) return AvFxKind.None;
            if (IsTimed(requested) && (reducedMotion || tier == AvFxTier.Lite)) return AvFxKind.None;
            if (requested == AvFxKind.Pulse && reducedMotion) return AvFxKind.None;
            return requested;
        }

        public static float Aspect(float w, float h) => h <= 0f ? 1f : w / h;
        public static float U(float x, float xMin, float w) => w <= 0f ? 0f : (x - xMin) / w;
        public static float V(float y, float yMin, float h) => h <= 0f ? 0f : (y - yMin) / h;
    }
}
