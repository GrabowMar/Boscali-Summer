using System;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>Pure reads of a map point for the context card: where it lies from another point, and who holds it.</summary>
    internal static class MapFix
    {
        internal enum Hold { Unknown, Unclaimed, Friendly, Contested, Hostile }

        /// <summary>A cell counts as held past this hold strength (the same line LivingFrontService draws).</summary>
        private const float HeldAbove = .35f;

        /// <summary>At or below this the control field makes no claim on the cell (TacticalSectorGrid skips it too).</summary>
        private const float NoClaim = .01f;

        /// <summary>Compass bearing from a to b in whole degrees, 0..359; north is +Z, east is +X.</summary>
        internal static int Bearing(float ax, float az, float bx, float bz)
        {
            int degrees = (int)Math.Round(Math.Atan2(bx - ax, bz - az) * 180.0 / Math.PI);
            return (degrees % 360 + 360) % 360;
        }

        internal static float Range(float ax, float az, float bx, float bz)
        {
            double dx = bx - ax, dz = bz - az;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Whole seconds to cover <paramref name="metres"/> at <paramref name="speed"/> m/s; -1 when too slow to mean anything or over 99 minutes.</summary>
        internal static int EtaSeconds(float metres, float speed)
        {
            if (!(speed >= 30f) || !(metres >= 0f)) return -1;
            float seconds = metres / speed;
            return seconds < 6000f ? (int)seconds : -1;
        }

        /// <summary>Signed hold strength (positive = ours) as a word; an unread field stays unknown, never unclaimed.</summary>
        internal static Hold Classify(bool known, float hold) =>
            !known || float.IsNaN(hold) ? Hold.Unknown :
            hold > HeldAbove ? Hold.Friendly :
            hold < -HeldAbove ? Hold.Hostile :
            Math.Abs(hold) <= NoClaim ? Hold.Unclaimed : Hold.Contested;
    }
}
