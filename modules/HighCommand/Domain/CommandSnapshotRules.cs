namespace BoscaliSummer.Features.HighCommand.Domain
{
    using BoscaliSummer.Framework.Contracts;

    /// <summary>
    /// Bounds shared by the runtime snapshot builder and the wire reader. A malformed row
    /// is rejected, never clamped into looking valid.
    /// </summary>
    internal static class CommandSnapshotRules
    {
        public const int MaximumNodes = 32;
        public const int MaximumIdentifier = 64;
        public const int MaximumTier = 2;
        public const int MaximumLogRows = 6;
        public const int MaximumLogText = 64;
        public const byte FlagsMask = 0x7F;
        public const float MaximumIntelAge = 600f;
        public const float MaximumLogAge = 900f;
        public const float MaximumCoordinate = 2000000f;

        public static bool ValidHeader(float cohesion, int active, int kia) =>
            Finite(cohesion) && cohesion >= 0f && cohesion <= 1f &&
            active >= 0 && active <= CommandTier.MaximumSlots * 8 &&
            kia >= 0 && kia <= CommandTier.MaximumSlots * 8;

        public static bool ValidNode(int id, int parentId, byte tier, byte flags,
            float intelAge, float weight, float x, float z) =>
            id >= 0 && id < MaximumIdentifier &&
            parentId >= -1 && parentId < MaximumIdentifier &&
            tier <= MaximumTier &&
            (flags & ~FlagsMask) == 0 &&
            Finite(intelAge) && intelAge >= -1f && intelAge <= MaximumIntelAge &&
            Finite(weight) && weight >= 0f && weight <= 1f &&
            Finite(x) && Finite(z) &&
            x >= -MaximumCoordinate && x <= MaximumCoordinate &&
            z >= -MaximumCoordinate && z <= MaximumCoordinate;

        public static bool ValidLogRow(int targetId, byte tone, string text, float age) =>
            targetId >= -1 && targetId < MaximumIdentifier &&
            tone <= (byte)CommanderLogTone.Alert &&
            text != null && text.Length <= MaximumLogText &&
            Finite(age) && age >= 0f && age <= MaximumLogAge;

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
