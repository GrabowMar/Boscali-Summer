using System;
using BoscaliSummer.Core;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Pure layout for deterministic warzone dressing: every peer derives the same street
    /// cluster (angle, radius, variant) from a replicated defense's network name, so
    /// barricades match everywhere with no network traffic.
    /// </summary>
    internal static class WarzoneDressingMath
    {
        public const int MaxClusters = 48;
        public const float MinRadius = 8f;
        public const float MaxRadius = 20f;
        public const int VariantBarricade = 0;
        public const int VariantChicane = 1;
        public const int VariantBurntCar = 2;
        public const int VariantCount = 3;

        public static void ClusterLayout(string identity, out float angle, out float radius, out int variant)
        {
            uint h = Deterministic.HashString(identity ?? string.Empty);
            angle = Deterministic.UnitFloat(h) * (float)(Math.PI * 2.0);
            radius = MinRadius + Deterministic.UnitFloat(Deterministic.Hash((int)h, 7, 0)) * (MaxRadius - MinRadius);
            variant = (int)(Deterministic.UnitFloat(Deterministic.Hash((int)h, 13, 0)) * VariantCount) % VariantCount;
        }
    }
}
