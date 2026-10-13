using System;

namespace BoscaliSummer.Modules.Autopilot.Domain
{
    /// <summary>
    /// The numbers and decisions of the chaff dispenser (<c>Runtime/ChaffDispenser</c>), pure. The game's radar seekers already
    /// answer chaff: every bundle adds dazzle to a seeker locked on the aircraft, scaled by how close the missile is and by how far
    /// the aircraft flies ACROSS the missile's line of sight (nothing head-on or tail-on, everything on the beam). The dispenser only
    /// supplies the bundles the stock aircraft never carried.
    /// </summary>
    internal static class ChaffRules
    {
        public const int MinLoad = 24, MaxLoad = 96;
        /// <summary>Seconds between ejections, and bundles per ejection: ten bundles a second while it runs.</summary>
        public const float Interval = 0.2f;
        public const int Grouping = 2;
        /// <summary>How long one POP CHAFF press keeps the dispenser running.</summary>
        public const float SalvoSeconds = 1f;
        /// <summary>A bundle blooms, drifts and is gone.</summary>
        public const float BundleSeconds = 5f;

        /// <summary>The airframe carries as many bundles as flares, within sane bounds (a trainer is not defenceless, a bomber not bottomless).</summary>
        public static int Load(int flaresMax) => Math.Max(MinLoad, Math.Min(MaxLoad, flaresMax));

        public static bool IsRadarSeeker(string seeker) => seeker == "ARH" || seeker == "SARH";

        /// <summary>
        /// Should the dispenser run this frame? A salvo from the menu or the key always does. Linked to the countermeasure button it runs
        /// only while that button is held AND a radar missile is inbound, so flares against a heat seeker never spend chaff.
        /// </summary>
        public static bool ShouldRun(bool salvo, bool linked, bool countermeasureHeld, bool radarMissileInbound, int ammo) =>
            ammo > 0 && (salvo || (linked && countermeasureHeld && radarMissileInbound));

        /// <summary>
        /// The dispenser is refilled with the rest of the aircraft: when a rearm it asked for completes, or when the flares come back up.
        /// </summary>
        public static bool Refilled(bool rearmWasPending, bool rearmPending, int flaresBefore, int flaresNow) =>
            (rearmWasPending && !rearmPending) || flaresNow > flaresBefore;
    }
}
