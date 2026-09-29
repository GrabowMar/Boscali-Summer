using System;
using BoscaliSummer.Core;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Pure, engine-free garrison variety: per-zone rooftop rotation, metros opening heavy,
    /// and per-site encampment typing. Deterministic from names and positions, so every
    /// capture agrees without network traffic. Unity-free so tests can compile it.
    /// </summary>
    internal static class GarrisonComposition
    {
        public const int HeavyTier = 3;
        public const int HeavySlot = 0;
        public const int HeavyDefinition = 2;
        public const int EncampmentTypes = 3;
        public const float EncampmentCellMeters = 16f;

        /// <summary>Zone name to a rotation seed: neighbouring zones field different orders.</summary>
        public static int ZoneSeed(string zone) => (int)(Deterministic.HashString(zone) % 3);

        /// <summary>
        /// Rooftop definition index for a slot: rotated per zone over count entries, with
        /// metro zones opening heavy (23mm at slot 0) when the table holds it.
        /// </summary>
        public static int DefinitionIndex(int slot, int seed, int tier, int count)
        {
            if (count <= 0) return 0;
            if (tier >= HeavyTier && slot == HeavySlot && count > HeavyDefinition) return HeavyDefinition;
            int rotated = (slot + seed) % count;
            if (rotated < 0) rotated += count;
            return rotated;
        }

        /// <summary>Encampment type 0-2 from the site position: stable per ground, not per order.</summary>
        public static int EncampmentTypeIndex(float x, float z)
        {
            int cx = (int)Math.Floor(x / EncampmentCellMeters);
            int cz = (int)Math.Floor(z / EncampmentCellMeters);
            return (int)(Deterministic.Hash(cx, cz, 11) % (uint)EncampmentTypes);
        }
    }
}
