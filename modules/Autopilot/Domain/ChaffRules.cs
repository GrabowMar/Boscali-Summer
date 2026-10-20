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

        // Seduction (2026-10-20, user: chaff "does nothing basically"): the game's own dazzle counts only while a seeker holds an active lock,
        // scores tail-on chaff as nothing, and stops counting once the dazzled seeker drops to search, so the jam never lasts long enough to
        // break the lock. On top of it every ejection rolls this chance per radar missile tracking the aircraft; a seduced missile loses its target.
        public const float SeduceBase = 0.15f, SeduceFullRange = 3000f, SeduceMaxRange = 8000f, SeduceTailFloor = 0.3f;

        /// <summary>
        /// The chance one ejection breaks a radar missile's lock: best on the beam (<paramref name="beam"/> 1 = flying across its line of sight)
        /// and inside <see cref="SeduceFullRange"/>, a third of that tail-on or head-on, nothing past <see cref="SeduceMaxRange"/>.
        /// Five ejections a second: on the beam up close about 55 % a one-second salvo, tail-on about 20 %.
        /// </summary>
        public static float SeduceChance(float rangeM, float beam)
        {
            float near = Math.Max(0f, Math.Min(1f, (SeduceMaxRange - rangeM) / (SeduceMaxRange - SeduceFullRange)));
            float aspect = SeduceTailFloor + (1f - SeduceTailFloor) * Math.Max(0f, Math.Min(1f, beam));
            return SeduceBase * aspect * near;
        }

        /// <summary>
        /// Should the dispenser run this frame? A salvo from the menu or the key always does. Linked to the countermeasure button it runs
        /// only while that button is held AND a radar missile is inbound, so flares against a heat seeker never spend chaff.
        /// </summary>
        public static bool ShouldRun(bool salvo, bool linked, bool countermeasureHeld, bool radarMissileInbound, int ammo) =>
            ammo > 0 && (salvo || (linked && countermeasureHeld && radarMissileInbound));

        /// <summary>
        /// The dispenser is one more stop of the Next Countermeasure cycle, after the game's last station: FLARES, JAMMER, CHAFF, FLARES.
        /// An index past the list (255: the game chose "none") is left to the game.
        /// </summary>
        public static bool NextIsChaff(int activeIndex, int stations) => stations > 0 && activeIndex == stations - 1;

        /// <summary>
        /// The dispenser is refilled with the rest of the aircraft: when a rearm it asked for completes, or when the flares come back up.
        /// </summary>
        public static bool Refilled(bool rearmWasPending, bool rearmPending, int flaresBefore, int flaresNow) =>
            (rearmWasPending && !rearmPending) || flaresNow > flaresBefore;
    }
}
