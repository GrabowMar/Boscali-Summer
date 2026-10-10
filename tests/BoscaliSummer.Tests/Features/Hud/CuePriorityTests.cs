using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>Which page the TAC BOARD scope shows: priority, the missile cam, the dwell and the pips.</summary>
    internal static class CuePriorityTests
    {
        private static ushort Live(params ScopePage[] pages)
        {
            ushort mask = 0;
            foreach (ScopePage p in pages) mask |= CuePriority.Bit(p);
            return mask;
        }

        public static void Run()
        {
            TestAssert.Eq((int)ScopePage.Systems, CuePriority.PageCount - 1, "SYSTEMS is the lowest page");
            TestAssert.Eq(CuePriority.Rank(ScopePage.Threat), 90, "THREAT ranks 90");
            TestAssert.Eq(CuePriority.Rank(ScopePage.Systems), 10, "SYSTEMS ranks 10");
            TestAssert.That(CuePriority.Rank(ScopePage.Shot) > CuePriority.Rank(ScopePage.MissileCam) && CuePriority.Rank(ScopePage.MissileCam) > CuePriority.Rank(ScopePage.Approach), "The prototype order: shot, cam, approach");

            TestAssert.Eq(CuePriority.Best(0), ScopePage.Systems, "Nothing live: SYSTEMS");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.Dock, ScopePage.Approach)), ScopePage.Approach, "The higher-ranked live page wins");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.Threat, ScopePage.Shot)), ScopePage.Threat, "THREAT beats SHOT");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.Threat, ScopePage.MissileCam)), ScopePage.MissileCam, "Riding a missile wins whatever else is live");
            TestAssert.That(CuePriority.Outranks(ScopePage.MissileCam, ScopePage.Threat) && !CuePriority.Outranks(ScopePage.Threat, ScopePage.MissileCam), "The cam outranks everything");
            TestAssert.That(CuePriority.Outranks(ScopePage.Strike, ScopePage.Systems) && !CuePriority.Outranks(ScopePage.Systems, ScopePage.Strike), "Everything outranks SYSTEMS");

            var s = new ScopeSelector();
            TestAssert.That(s.Compact && s.Page == ScopePage.Systems, "Cruise: the compact SYSTEMS strip");
            s.Update(0, 0f);
            TestAssert.That(s.Compact && s.Others == 0, "Still cruise with nothing live");
            s.Update(Live(ScopePage.Shot), 1f);
            TestAssert.That(s.Page == ScopePage.Shot && !s.Compact, "A live page unfolds the scope at once");
            s.Update(Live(ScopePage.Shot, ScopePage.Wing), 1.1f);
            TestAssert.That(s.Page == ScopePage.Shot && s.Others == Live(ScopePage.Wing), "The other live page earns a pip, the current one does not");
            s.Update(0, 1.5f);
            TestAssert.That(s.Page == ScopePage.Shot, "A page that blinked out keeps the scope for its dwell");
            s.Update(0, 2.6f);
            TestAssert.That(s.Page == ScopePage.Systems && s.Compact, "...then the scope folds back to SYSTEMS");
            s.Update(Live(ScopePage.Approach), 3f);
            s.Update(Live(ScopePage.Approach, ScopePage.Threat), 3.1f);
            TestAssert.That(s.Page == ScopePage.Threat && s.Others == Live(ScopePage.Approach), "A higher-priority page takes over inside the dwell");
            s.Update(Live(ScopePage.Approach, ScopePage.Threat, ScopePage.MissileCam), 3.2f);
            TestAssert.That(s.Page == ScopePage.MissileCam && s.Others == Live(ScopePage.Approach, ScopePage.Threat), "The cam takes over and the rest are pips");
            s.Update(Live(ScopePage.Approach), 3.3f);
            TestAssert.That(s.Page == ScopePage.MissileCam, "The cam holds its dwell after the missile is gone");
            s.Update(Live(ScopePage.Approach), 5f);
            TestAssert.That(s.Page == ScopePage.Approach, "...then the scope falls to the best remaining page");
            s.Reset();
            TestAssert.That(s.Page == ScopePage.Systems && s.Others == 0, "Reset returns to cruise");
        }
    }
}
