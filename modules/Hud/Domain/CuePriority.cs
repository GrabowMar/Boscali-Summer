using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>The nine pages of the TAC BOARD scope, highest priority first (the prototype's 90..10).</summary>
    internal enum ScopePage : byte { Threat, Shot, MissileCam, Approach, Wing, Strike, Dock, Objective, Systems }

    /// <summary>
    /// Page priority and selection for the TAC BOARD scope (HUD v8), pure. The board shows ONE page: the highest-priority page that
    /// has live content, SYSTEMS when none has (and SYSTEMS is the one page that folds to a compact strip). Every other live page
    /// gets a pip on the tab. The order is the spec's: THREAT 90, SHOT 80, MISSILE CAM 70, APPROACH 60, WING 50, STRIKE 40, DOCK 30,
    /// OBJECTIVE 20, SYSTEMS 10, with two exceptions. The MISSILE CAM ranks <see cref="RidingRank"/>: the missile being ridden is
    /// itself a shot in the air, so SHOT is always live under it and at 70 the cam would never show; an inbound missile still
    /// outranks the ride. SYSTEMS rises to <see cref="CriticalSystemsRank"/> while it has a fire, an engine out, BINGO or lost
    /// structure to report (above the pages that only inform, below the ones that fly the aircraft), because the damage and fuel
    /// cautions have no other place on the board. <see cref="ScopeSelector"/> adds the dwell that keeps the page from flapping.
    /// </summary>
    internal static class CuePriority
    {
        public const int PageCount = 9;

        /// <summary>SYSTEMS while it reports something critical: between APPROACH (60) and WING (50).</summary>
        public const int CriticalSystemsRank = 55;

        /// <summary>The MISSILE CAM (live only while the camera rides an own missile): between THREAT (90) and SHOT (80).</summary>
        public const int RidingRank = 85;

        /// <summary>The page's bit in a live mask.</summary>
        public static ushort Bit(ScopePage page) => (ushort)(1 << (int)page);

        public static bool Has(ushort mask, ScopePage page) => (mask & Bit(page)) != 0;

        /// <summary>0..90, higher wins (the prototype's page priorities); the cam is 85, SYSTEMS is 55 while <paramref name="systemsCritical"/>.</summary>
        public static int Rank(ScopePage page, bool systemsCritical = false) =>
            page == ScopePage.MissileCam ? RidingRank : page == ScopePage.Systems && systemsCritical ? CriticalSystemsRank : (PageCount - 1 - (int)page) * 10 + 10;

        /// <summary>The page to show for a live mask: the best-ranked live page, else SYSTEMS.</summary>
        public static ScopePage Best(ushort live, bool systemsCritical = false)
        {
            ScopePage best = ScopePage.Systems;
            int bestRank = -1;
            for (int i = 0; i < PageCount; i++)
            {
                var page = (ScopePage)i;
                if (!Has(live, page)) continue;
                int rank = Rank(page, systemsCritical);
                if (rank > bestRank) { best = page; bestRank = rank; }
            }
            return best;
        }

        /// <summary>The page outranks another.</summary>
        public static bool Outranks(ScopePage page, ScopePage other, bool systemsCritical = false) => Rank(page, systemsCritical) > Rank(other, systemsCritical);
    }

    /// <summary>
    /// The scope's page selection over time: a higher-priority live page takes the scope at once, but the scope only falls back to a
    /// lower one (or to SYSTEMS) after the current page has been out of the live set for <see cref="MinDwell"/>, so a contact that blinks
    /// in and out (however long it has held the scope before) does not wipe the scope back and forth. Pure; the caller feeds the live
    /// mask, the clock and whether SYSTEMS has something critical.
    /// </summary>
    internal sealed class ScopeSelector
    {
        public const float MinDwell = 1.5f;

        private float lastLive = float.NegativeInfinity;

        public ScopePage Page { get; private set; } = ScopePage.Systems;

        /// <summary>The page is SYSTEMS: the scope folds to its compact strip.</summary>
        public bool Compact => Page == ScopePage.Systems;

        /// <summary>The live pages other than the current one, for the tab's pips.</summary>
        public ushort Others { get; private set; }

        public ScopePage Update(ushort live, float now, bool systemsCritical = false)
        {
            if (CuePriority.Has(live, Page)) lastLive = now;
            ScopePage best = CuePriority.Best(live, systemsCritical);
            if (best != Page)
            {
                // A different best is either a page that outranks the current one (take over now) or the current page has gone quiet (fall back after the dwell).
                if (CuePriority.Outranks(best, Page, systemsCritical) || now - lastLive >= MinDwell)
                {
                    Page = best;
                    lastLive = now;
                }
            }
            Others = (ushort)(live & ~CuePriority.Bit(Page));
            return Page;
        }

        public void Reset()
        {
            Page = ScopePage.Systems;
            lastLive = float.NegativeInfinity;
            Others = 0;
        }
    }
}
