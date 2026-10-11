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
    /// lower one (or to SYSTEMS) after the current page has been out of the live set for its <see cref="Linger"/> AND has been on the
    /// scope for its <see cref="MinStay"/>, so a contact that blinks in and out (however long it has held the scope before) does not
    /// wipe the scope back and forth. Pure; the caller feeds the live mask, the clock and whether SYSTEMS has something critical.
    /// </summary>
    internal sealed class ScopeSelector
    {
        /// <summary>How long a page keeps the scope after it last had live content, unless the caller says otherwise.</summary>
        public const float DefaultLinger = 1.5f;

        /// <summary>Per page (index = <see cref="ScopePage"/>): seconds the page keeps the scope after its last live tick, and the least seconds it stays once it has taken the scope.</summary>
        public readonly float[] Linger = new float[CuePriority.PageCount], MinStay = new float[CuePriority.PageCount];

        private float lastLive = float.NegativeInfinity, shownAt = float.NegativeInfinity;

        public ScopeSelector()
        {
            for (int i = 0; i < Linger.Length; i++) Linger[i] = DefaultLinger;
        }

        public ScopePage Page { get; private set; } = ScopePage.Systems;

        /// <summary>The page is SYSTEMS: the scope folds to its compact strip.</summary>
        public bool Compact => Page == ScopePage.Systems;

        /// <summary>The live pages other than the current one, for the tab's pips.</summary>
        public ushort Others { get; private set; }

        /// <summary>The least stay of every page but SYSTEMS (the page the scope falls back to never has to stay).</summary>
        public void SetMinStay(float seconds)
        {
            for (int i = 0; i < MinStay.Length; i++) MinStay[i] = i == (int)ScopePage.Systems ? 0f : seconds;
        }

        public ScopePage Update(ushort live, float now, bool systemsCritical = false)
        {
            if (CuePriority.Has(live, Page)) lastLive = now;
            ScopePage best = CuePriority.Best(live, systemsCritical);
            if (best != Page)
            {
                // A different best is either a page that outranks the current one (take over now) or the current page has gone quiet (fall back once its linger and its minimum stay have passed).
                if (CuePriority.Outranks(best, Page, systemsCritical) || (now - lastLive >= Linger[(int)Page] && now - shownAt >= MinStay[(int)Page]))
                {
                    Page = best;
                    lastLive = shownAt = now;
                }
            }
            Others = (ushort)(live & ~CuePriority.Bit(Page));
            return Page;
        }

        /// <summary>
        /// <see cref="Update"/> under the pilot's options: only the pages <paramref name="allow"/> lets show can be live (so a SYSTEMS whose switch is off is never critical either), a page
        /// the pilot has just switched off does not hold the scope until its linger runs out, and the least stay is <paramref name="minStay"/>.
        /// </summary>
        public ScopePage Select(ushort live, in BoardEffective allow, float minStay, float now, bool systemsCritical)
        {
            if (Page != ScopePage.Systems && !CuePriority.Has(allow.Pages, Page)) Reset();
            SetMinStay(minStay);
            return Update((ushort)(live & allow.Pages), now, systemsCritical && CuePriority.Has(allow.Pages, ScopePage.Systems));
        }

        public void Reset()
        {
            Page = ScopePage.Systems;
            lastLive = shownAt = float.NegativeInfinity;
            Others = 0;
        }
    }
}
