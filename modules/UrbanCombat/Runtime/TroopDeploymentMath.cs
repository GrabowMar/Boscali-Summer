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
            int available = Math.Max(1, ammo);
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

    }
}
