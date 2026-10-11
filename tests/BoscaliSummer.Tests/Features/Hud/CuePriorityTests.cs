using BoscaliSummer.Core.Contracts;
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
            TestAssert.Eq(CuePriority.Rank(ScopePage.MissileCam), 85, "The missile cam ranks 85: riding a missile means a shot is in the air, so at the prototype's 70 SHOT would always hide it");
            TestAssert.Eq(CuePriority.Rank(ScopePage.Systems), 10, "SYSTEMS ranks 10");
            TestAssert.That(CuePriority.Rank(ScopePage.Threat) > CuePriority.Rank(ScopePage.MissileCam) && CuePriority.Rank(ScopePage.MissileCam) > CuePriority.Rank(ScopePage.Shot) && CuePriority.Rank(ScopePage.Shot) > CuePriority.Rank(ScopePage.Approach), "The order: threat, cam, shot, approach");
            TestAssert.Eq(CuePriority.Rank(ScopePage.Systems, true), 55, "A critical SYSTEMS ranks 55...");
            TestAssert.That(CuePriority.Rank(ScopePage.Systems, true) > CuePriority.Rank(ScopePage.Wing) && CuePriority.Rank(ScopePage.Systems, true) < CuePriority.Rank(ScopePage.Approach), "...above WING, below APPROACH");

            TestAssert.Eq(CuePriority.Best(0), ScopePage.Systems, "Nothing live: SYSTEMS");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.Dock, ScopePage.Approach)), ScopePage.Approach, "The higher-ranked live page wins");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.Threat, ScopePage.Shot)), ScopePage.Threat, "THREAT beats SHOT");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.Threat, ScopePage.MissileCam)), ScopePage.Threat, "An inbound missile outranks the ride on our own");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.MissileCam, ScopePage.Approach, ScopePage.Wing)), ScopePage.MissileCam, "...but the cam outranks the flight pages");
            TestAssert.Eq(CuePriority.Best(Live(ScopePage.Shot, ScopePage.MissileCam)), ScopePage.MissileCam, "...and the SHOT page its own missile keeps live");
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

            Timing();
            UnderOptions();
        }

        /// <summary>The scope under the pilot's options (what the board's update runs): the page switches, the scope mode, the least stay and the critical SYSTEMS reach the selection.</summary>
        private static void UnderOptions()
        {
            BoardOptions o = BoardOptions.Default;
            BoardEffective allow = o.Effective();
            var s = new ScopeSelector();
            s.Select(Live(ScopePage.Wing), allow, 0f, 0f, false);
            TestAssert.That(s.Page == ScopePage.Wing, "A page whose switch is on takes the scope");

            // A page whose switch is off is never live: it neither takes the scope nor earns a pip.
            o.PageThreat = false;
            s.Reset();
            s.Select(Live(ScopePage.Threat, ScopePage.Wing), o.Effective(), 0f, 0f, false);
            TestAssert.That(s.Page == ScopePage.Wing && s.Others == 0, "THREAT switched off: WING takes the scope and THREAT earns no pip");
            s.Reset();
            s.Select(Live(ScopePage.Threat), o.Effective(), 0f, 0f, false);
            TestAssert.That(s.Page == ScopePage.Systems && s.Compact, "...and alone it leaves the scope in cruise");

            // Switched off while it holds the scope: it lets go at once, not after its linger.
            s.Reset();
            s.Select(Live(ScopePage.Threat), allow, 0f, 0f, false);
            TestAssert.That(s.Page == ScopePage.Threat, "THREAT holds the scope");
            s.Select(Live(ScopePage.Threat), o.Effective(), 0f, 0.1f, false);
            TestAssert.That(s.Page == ScopePage.Systems, "...and lets go the moment its switch goes off");

            // Scope mode Off allows no page at all.
            o = BoardOptions.Default;
            o.ScopeMode = BoardScopeMode.Off;
            s.Reset();
            s.Select(Live(ScopePage.Threat), allow, 0f, 0f, false);
            s.Select(Live(ScopePage.Threat, ScopePage.Wing), o.Effective(), 0f, 0.1f, false);
            TestAssert.That(s.Page == ScopePage.Systems && s.Others == 0, "Scope mode Off: the page on the scope goes and nothing takes its place");

            // The least stay is the pilot's: 3 s keeps a page that went quiet, 0 leaves it to the linger.
            s.Reset();
            s.Select(Live(ScopePage.Wing), allow, 3f, 0f, false);
            s.Select(0, allow, 3f, 2.75f, false);
            TestAssert.That(s.Page == ScopePage.Wing && s.MinStay[(int)ScopePage.Wing] == 3f, "A 3 s minimum stay keeps WING though its linger is long gone");
            s.Select(0, allow, 3f, 3f, false);
            TestAssert.That(s.Page == ScopePage.Systems, "...and the scope folds at 3 s");
            s.Reset();
            s.Select(Live(ScopePage.Wing), allow, 0f, 0f, false);
            s.Select(0, allow, 0f, 1.5f, false);
            TestAssert.That(s.Page == ScopePage.Systems && s.MinStay[(int)ScopePage.Wing] == 0f, "No minimum stay: the linger alone, 1.5 s");

            // A critical SYSTEMS takes the scope only while the SYSTEMS switch is on.
            s.Reset();
            s.Select(Live(ScopePage.Wing), allow, 0f, 0f, false);
            o = BoardOptions.Default;
            o.PageSystems = false;
            s.Select(Live(ScopePage.Wing, ScopePage.Systems), o.Effective(), 0f, 0.1f, true);
            TestAssert.That(s.Page == ScopePage.Wing && s.Others == 0, "A fire does not take the scope from WING when SYSTEMS is switched off");
            s.Select(Live(ScopePage.Wing, ScopePage.Systems), allow, 0f, 0.2f, true);
            TestAssert.That(s.Page == ScopePage.Systems, "...with the switch on it does");
        }

        /// <summary>The per-page linger and minimum stay: a page falls back only when both have passed; a page that outranks the current one still takes over at once.</summary>
        private static void Timing()
        {
            var s = new ScopeSelector();
            bool defaults = true;
            for (int i = 0; i < CuePriority.PageCount; i++) defaults &= s.Linger[i] == 1.5f && s.MinStay[i] == 0f;
            TestAssert.That(defaults && ScopeSelector.DefaultLinger == 1.5f, "Every page lingers 1.5 s and has no minimum stay until told otherwise");

            // A page's linger is its own: THREAT keeps the scope 4 s after its last live tick, SHOT keeps the default 1.5 s.
            s.Linger[(int)ScopePage.Threat] = 4f;
            s.Update(Live(ScopePage.Threat), 0f);
            s.Update(0, 3.5f);
            TestAssert.That(s.Page == ScopePage.Threat, "THREAT keeps the scope for its longer linger");
            s.Update(0, 4f);
            TestAssert.That(s.Page == ScopePage.Systems, "...and gives it up when the linger has run out");
            s.Update(Live(ScopePage.Shot), 10f);
            s.Update(0, 11.25f);
            TestAssert.That(s.Page == ScopePage.Shot, "SHOT is still inside the default linger");
            s.Update(0, 11.5f);
            TestAssert.That(s.Page == ScopePage.Systems, "...and folds at 1.5 s, as before");

            // The minimum stay holds a page whose linger is long gone (a lone ping of a search radar shows for its whole stay).
            s.Reset();
            s.SetMinStay(3f);
            s.Update(Live(ScopePage.Wing), 0f);
            s.Update(0, 1.75f);
            TestAssert.That(s.Page == ScopePage.Wing, "Linger passed (1.5 s) but the 3 s minimum stay has not: the page stays");
            s.Update(0, 2.75f);
            TestAssert.That(s.Page == ScopePage.Wing, "...still inside the minimum stay");
            s.Update(0, 3f);
            TestAssert.That(s.Page == ScopePage.Systems, "...both passed: the scope folds");

            // Both must have passed: a short minimum stay does not cut a long linger short.
            s.Reset();
            s.Linger[(int)ScopePage.Wing] = 5f;
            s.SetMinStay(1f);
            s.Update(Live(ScopePage.Wing), 0f);
            s.Update(0, 4f);
            TestAssert.That(s.Page == ScopePage.Wing, "The minimum stay (1 s) passed but the 5 s linger has not");
            s.Update(0, 5f);
            TestAssert.That(s.Page == ScopePage.Systems, "...both passed: the scope folds");

            // The minimum stay counts from when the page took the scope.
            s.Reset();
            s.Linger[(int)ScopePage.Wing] = 1.5f;
            s.SetMinStay(3f);
            s.Update(Live(ScopePage.Wing), 0f);
            for (float t = 0.5f; t <= 10f; t += 0.5f) s.Update(Live(ScopePage.Wing), t);
            s.Update(0, 11f);
            TestAssert.That(s.Page == ScopePage.Wing, "A page that held the scope for 10 s still keeps it for its linger");
            s.Update(0, 11.5f);
            TestAssert.That(s.Page == ScopePage.Systems, "...and its minimum stay (long served) adds nothing");

            // A higher-ranked page takes over at once, whatever the current page's linger and minimum stay say.
            s.Reset();
            s.SetMinStay(10f);
            s.Update(Live(ScopePage.Wing), 0f);
            s.Update(Live(ScopePage.Wing, ScopePage.Threat), 0.5f);
            TestAssert.That(s.Page == ScopePage.Threat && s.Others == Live(ScopePage.Wing), "THREAT takes the scope from WING inside WING's minimum stay");
            s.Update(Live(ScopePage.Wing), 5f);
            TestAssert.That(s.Page == ScopePage.Threat, "...and now holds it for its own minimum stay, though WING is live and THREAT is not");
            s.Update(Live(ScopePage.Wing), 10.5f);
            TestAssert.That(s.Page == ScopePage.Wing, "...then the scope falls to the best remaining page");

            // SYSTEMS, the page the scope falls back to, never has a minimum stay.
            s.SetMinStay(3f);
            bool minStays = true;
            for (int i = 0; i < CuePriority.PageCount; i++) minStays &= s.MinStay[i] == ((ScopePage)i == ScopePage.Systems ? 0f : 3f);
            TestAssert.That(minStays, "SetMinStay sets every page but SYSTEMS");
            s.Reset();
            s.Update(Live(ScopePage.Wing), 0f);
            s.Update(Live(ScopePage.Wing, ScopePage.Systems), 0.5f, true);
            s.Update(Live(ScopePage.Wing), 0.75f);
            TestAssert.That(s.Page == ScopePage.Wing, "A critical SYSTEMS that clears gives the scope back at once, whatever the others' minimum stay");

            // A reset keeps the configured timings.
            s.Linger[(int)ScopePage.Dock] = 7f;
            s.Reset();
            TestAssert.That(s.Linger[(int)ScopePage.Dock] == 7f && s.MinStay[(int)ScopePage.Wing] == 3f, "Reset returns to cruise, not to the defaults");
            s.Update(Live(ScopePage.Wing), 20f);
            TestAssert.That(s.Page == ScopePage.Wing, "...and the first page after a reset takes the scope at once");
        }
    }
}
