using System;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Pure, engine-free math for infantry air assaults so the drop-size and
    /// encampment tiering rules are unit-testable without the game running.
    /// </summary>
    internal static class TroopDeploymentMath
    {
        /// <summary>Infantry committed per fast-rope trigger on the UH-90 Ibis.</summary>
        public const int DefaultSquadSize = 8;

        /// <summary>How many infantry actually slide down the ropes this trigger.</summary>
        public static int ComputeDropSize(int ammo, int desiredPerDeploy)
        {
            int desired = Math.Max(1, desiredPerDeploy);
            int available = Math.Max(0, ammo);
            return Math.Min(desired, available);
        }

        /// <summary>
        /// Encampment tier given the total infantry committed to a site. The bigger the
        /// committed force, the heavier the emplacements that get reinforced in.
        /// </summary>
        public static int ComputeTier(int totalTroops)
        {
            if (totalTroops >= 32) return 4;
            if (totalTroops >= 24) return 3;
            if (totalTroops >= 16) return 2;
            return 1;
        }

        /// <summary>Slowest canopy sink rate; the first jumper out hangs longest.</summary>
        public const float ParachuteDescentRate = 5.2f;
        /// <summary>Combined winch-out and slide rate of a fast-rope insertion.</summary>
        public const float FastRopeDescentRate = 5f;
        /// <summary>Hard ceiling for any drop: about 3 km of canopy at the slowest sink rate.</summary>
        public const float MaxParadropSeconds = 600f;

        /// <summary>Seconds to come down <paramref name="height"/> metres; bounded and never NaN.</summary>
        public static float DescentSeconds(float height, float descentRate)
        {
            if (!(height > 0f)) return 0f;
            if (!(descentRate > 0f)) return MaxParadropSeconds;
            return Math.Min(MaxParadropSeconds, height / descentRate);
        }

        /// <summary>
        /// Lifetime of a paradrop visual: the slowest descent plus the stick's exit stagger and
        /// landing hold, never shorter than a low-level drop and never past the ceiling.
        /// </summary>
        public static float ParadropOperationSeconds(float dropHeight, float descentRate)
        {
            return Math.Min(MaxParadropSeconds, Math.Max(40f, DescentSeconds(dropHeight, descentRate) + 25f));
        }
    }
}
