using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>Which page the TAC BOARD scope shows: priority, the critical SYSTEMS, the dwell and the pips.</summary>
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
            TestAssert.Eq(CuePriority.Rank(ScopePage.MissileCam), 70, "The missile cam ranks 70, as the prototype and the spec have it");
            TestAssert.Eq(CuePriority.Rank(ScopePage.Systems), 10, "SYSTEMS ranks 10");
            TestAssert.That(CuePriority.Rank(ScopePage.Shot) > CuePriority.Rank(ScopePage.MissileCam) && CuePriority.Rank(ScopePage.MissileCam) > CuePriority.Rank(ScopePage.Approach), "The prototype order: shot, cam, approach");
            TestAssert.Eq(CuePriority.Rank(ScopePage.Systems, true), 55, "A critical SYSTEMS ranks 55...");
            TestAssert.That(CuePriority.Rank(ScopePage.Systems, true) > CuePriority.Rank(ScopePage.Wing) && CuePriority.Rank(ScopePage.Systems, true) < CuePriority.Rank(ScopePage.Approach), "...above WING, below APPROACH");

            TestAssert.Eq(CuePriority.Best(0), ScopePage.Systems, "Nothing live: SYSTEMS");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.Dock, ScopePage.Approach)), ScopePage.Approach, "The higher-ranked live page wins");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.Threat, ScopePage.Shot)), ScopePage.Threat, "THREAT beats SHOT");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.Threat, ScopePage.MissileCam)), ScopePage.Threat, "An inbound missile outranks the ride on our own");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.MissileCam, ScopePage.Approach, ScopePage.Wing)), ScopePage.MissileCam, "...but the cam outranks the flight pages");
            TestAssert.That(CuePriority.Outranks(ScopePage.Strike, ScopePage.Systems) && !CuePriority.Outranks(ScopePage.Systems, ScopePage.Strike), "Everything outranks SYSTEMS");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.Wing, ScopePage.Systems)), ScopePage.Wing, "An idle-ish SYSTEMS does not outrank WING...");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.Wing, ScopePage.Systems), true), ScopePage.Systems, "...a critical one does (a fire is not read off a pip)");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.Approach, ScopePage.Systems), true), ScopePage.Approach, "...but not the landing");

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
            s.Update(0, 2.7f);
            TestAssert.That(s.Page == ScopePage.Systems && s.Compact, "...then the scope folds back to SYSTEMS");
            s.Update(Live(ScopePage.Approach), 3f);
            s.Update(Live(ScopePage.Approach, ScopePage.Threat), 3.1f);
            TestAssert.That(s.Page == ScopePage.Threat && s.Others == Live(ScopePage.Approach), "A higher-priority page takes over inside the dwell");
            s.Update(Live(ScopePage.Approach, ScopePage.Threat, ScopePage.MissileCam), 3.2f);
            TestAssert.That(s.Page == ScopePage.Threat && s.Others == Live(ScopePage.Approach, ScopePage.MissileCam), "...and holds against the cam, which is a pip");
            s.Update(Live(ScopePage.Approach, ScopePage.MissileCam), 3.3f);
            TestAssert.That(s.Page == ScopePage.Threat, "THREAT holds its dwell after the missile is gone");
            s.Update(Live(ScopePage.Approach, ScopePage.MissileCam), 5f);
            TestAssert.That(s.Page == ScopePage.MissileCam, "...then the scope falls to the best remaining page: the cam");

            // The dwell runs from when the page was last live, not from when it took the scope: a page that held for a long time and drops falls back only after the dwell.
            s.Reset();
            s.Update(Live(ScopePage.Wing), 0f);
            for (float t = 0.5f; t <= 20f; t += 0.5f) s.Update(Live(ScopePage.Wing), t);
            s.Update(0, 20.5f);
            TestAssert.That(s.Page == ScopePage.Wing, "A page that held the scope for 20 s still keeps it for the dwell after it drops");
            s.Update(Live(ScopePage.Wing), 21f);
            s.Update(0, 21.5f);
            TestAssert.That(s.Page == ScopePage.Wing, "...and a contact that blinks back resets the dwell (no wipe back and forth)");
            s.Update(0, 22.6f);
            TestAssert.That(s.Page == ScopePage.Systems, "...the scope folds once it has been quiet for the whole dwell");

            // A critical SYSTEMS takes the scope from WING at once.
            s.Reset();
            s.Update(Live(ScopePage.Wing), 0f);
            s.Update(Live(ScopePage.Wing, ScopePage.Systems), 0.1f, true);
            TestAssert.That(s.Page == ScopePage.Systems && s.Others == Live(ScopePage.Wing), "A fire takes the scope from the wing page, which becomes a pip");
            s.Update(Live(ScopePage.Wing, ScopePage.Systems), 0.2f, true);
            TestAssert.That(s.Page == ScopePage.Systems, "...and keeps it while the fire burns");
            s.Update(Live(ScopePage.Wing, ScopePage.Systems), 0.3f, false);
            TestAssert.That(s.Page == ScopePage.Wing, "...and gives it back to the wing page once nothing on SYSTEMS is critical any more");

            s.Reset();
            TestAssert.That(s.Page == ScopePage.Systems && s.Others == 0, "Reset returns to cruise");
        }
    }
}
