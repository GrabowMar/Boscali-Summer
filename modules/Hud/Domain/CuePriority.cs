using System;

namespace BoscaliSummer.Modules.Hud.Domain
{
    /// <summary>The nine pages of the TAC BOARD scope, highest priority first (the prototype's 90..10).</summary>
    internal enum ScopePage : byte { Threat, Shot, MissileCam, Approach, Wing, Strike, Dock, Objective, Systems }

    /// <summary>
    /// Page priority and selection for the TAC BOARD scope (HUD v8), pure. The board shows ONE page: the highest-priority page that
    /// has live content, SYSTEMS when none has (and SYSTEMS is the one page that folds to a compact strip). Riding one of our own
    /// missiles is a deliberate act of the pilot's and wins whatever else is live: the screen is the feed. Every other live page
    /// gets a pip on the tab. <see cref="ScopeSelector"/> adds the dwell that keeps the page from flapping.
    /// </summary>
    internal static class CuePriority
    {
        public const int PageCount = 9;

        /// <summary>The page's bit in a live mask.</summary>
        public static ushort Bit(ScopePage page) => (ushort)(1 << (int)page);

        public static bool Has(ushort mask, ScopePage page) => (mask & Bit(page)) != 0;

        /// <summary>0..90, higher wins (the prototype's page priorities).</summary>
        public static int Rank(ScopePage page) => (PageCount - 1 - (int)page) * 10 + 10;

        /// <summary>The page to show for a live mask: the cam while riding, else the best-ranked live page, else SYSTEMS.</summary>
        public static ScopePage Best(ushort live)
        {
            if (Has(live, ScopePage.MissileCam)) return ScopePage.MissileCam;
            for (int i = 0; i < PageCount; i++)
                if ((live & (1 << i)) != 0) return (ScopePage)i;
            return ScopePage.Systems;
        }

        /// <summary>The page outranks another (the cam outranks everything).</summary>
        public static bool Outranks(ScopePage page, ScopePage other) =>
            page == ScopePage.MissileCam ? other != ScopePage.MissileCam : other != ScopePage.MissileCam && Rank(page) > Rank(other);
    }

    /// <summary>
    /// The scope's page selection over time: a higher-priority live page takes the scope at once, but the scope only falls back to a
    /// lower one (or to SYSTEMS) after <see cref="MinDwell"/> on the current page, so a contact that blinks in and out does not
    /// wipe the scope back and forth. Pure; the caller feeds the live mask and the clock.
    /// </summary>
    internal sealed class ScopeSelector
    {
        public const float MinDwell = 1.5f;

        private float since = float.NegativeInfinity;

        public ScopePage Page { get; private set; } = ScopePage.Systems;

        /// <summary>The page is SYSTEMS: the scope folds to its compact strip.</summary>
        public bool Compact => Page == ScopePage.Systems;

        /// <summary>The live pages other than the current one, for the tab's pips.</summary>
        public ushort Others { get; private set; }

        public ScopePage Update(ushort live, float now)
        {
            ScopePage best = CuePriority.Best(live);
            if (best != Page)
            {
                // Best is never the current page's lower-ranked live neighbour while the current page is live, so a different best
                // is either a page that outranks it (take over now) or the current page is gone (fall back after the dwell).
                if (CuePriority.Outranks(best, Page) || now - since >= MinDwell)
                {
                    Page = best;
                    since = now;
                }
            }
            Others = (ushort)(live & ~CuePriority.Bit(Page));
            return Page;
        }

        public void Reset()
        {
            Page = ScopePage.Systems;
            since = float.NegativeInfinity;
            Others = 0;
        }
    }
}
