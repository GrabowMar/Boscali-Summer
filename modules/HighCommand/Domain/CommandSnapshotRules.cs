namespace BoscaliSummer.Features.HighCommand.Domain
{
    /// <summary>
    /// Bounds shared by the runtime snapshot builder and the wire reader. A malformed row
    /// is rejected, never clamped into looking valid.
    /// </summary>
    internal static class CommandSnapshotRules
    {
        public const int MaximumNodes = 32;
        public const int MaximumIdentifier = 64;
        public const int MaximumTier = 2;
        public const byte FlagsMask = 0x3F;
        public const byte ActionsMask = 0x07;
        public const float MaximumIntelAge = 600f;
        public const float MaximumCoordinate = 2000000f;

        public static bool ValidHeader(float cohesion, int points, int active, int kia) =>
            Finite(cohesion) && cohesion >= 0f && cohesion <= 1f &&
            points >= 0 && points <= 32 &&
            active >= 0 && active <= CommandTier.MaximumSlots * 8 &&
            kia >= 0 && kia <= CommandTier.MaximumSlots * 8;

        public static bool ValidNode(int id, int parentId, byte tier, byte flags, byte actions,
            float intelAge, float weight, float x, float z) =>
            id >= 0 && id < MaximumIdentifier &&
            parentId >= -1 && parentId < MaximumIdentifier &&
            tier <= MaximumTier &&
            (flags & ~FlagsMask) == 0 &&
            (actions & ~ActionsMask) == 0 &&
            Finite(intelAge) && intelAge >= -1f && intelAge <= MaximumIntelAge &&
            Finite(weight) && weight >= 0f && weight <= 1f &&
            Finite(x) && Finite(z) &&
            x >= -MaximumCoordinate && x <= MaximumCoordinate &&
            z >= -MaximumCoordinate && z <= MaximumCoordinate;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
