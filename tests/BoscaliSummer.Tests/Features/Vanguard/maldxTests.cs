using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins MALD-X BAIT cruise, loiter, exposure and lure numbers.</summary>
    internal static class MaldxTests
    {
        public static void Run()
        {
            TestAssert.Near(MaldxRules.Lifetime, 300f, "bait lives five minutes");
            TestAssert.Near(MaldxRules.CruiseAltitude(800f, 0f), 1500f, "low launch climbs to the 1500 m floor");
            TestAssert.Near(MaldxRules.CruiseAltitude(6000f, 0f), 6000f, "high launch keeps its altitude");
            TestAssert.Near(MaldxRules.CruiseAltitude(800f, 1400f), 1900f, "high ground lifts the floor to terrain + 500 m");

            TestAssert.Near(MaldxRules.Throttle(230f), 0.5f, "on speed: half throttle");
            TestAssert.Near(MaldxRules.Throttle(220f), 0.75f, "10 m/s slow: 0.75");
            TestAssert.Near(MaldxRules.Throttle(200f), 1f, "slow: full");
            TestAssert.Near(MaldxRules.Throttle(260f), 0f, "fast: idle");

            TestAssert.That(MaldxRules.AboveHorizon(5f, 1500f, 146000f), "ground radar sees a 1500 m bait at 146 km");
            TestAssert.That(!MaldxRules.AboveHorizon(5f, 1500f, 147000f), "but not past the summed horizons (146.2 km)");
            TestAssert.That(!MaldxRules.AboveHorizon(-10f, 0f, 1f), "below sea level clamps to zero horizon");

            Vector2 aim = MaldxRules.OrbitOffset(new Vector2(0f, -5000f), 3000f, 35f);
            TestAssert.That(Mathf.Abs(aim.x + 1720.73f) < 0.5f && Mathf.Abs(aim.y + 2457.46f) < 0.5f,
                "loiter leads 35 deg clockwise from south toward west at 3 km (got " + aim + ")");
            Vector2 centred = MaldxRules.OrbitOffset(Vector2.zero, 3000f, 0f);
            TestAssert.That(Mathf.Abs(centred.x) < 0.01f && Mathf.Abs(centred.y - 3000f) < 0.01f, "on the centre: aim north");

            var book = new MaldxLedger();
            TestAssert.That(!book.See(7, 0f, 1f) && !book.See(7, 1f, 1f), "two seconds of contact: not yet");
            TestAssert.That(book.See(7, 2f, 1f), "third cumulative second exposes");
            TestAssert.That(book.IsExposed(7, 2f) && book.Exposed(2f) == 1, "one live exposure");
            TestAssert.That(!book.See(7, 30f, 1f), "continued contact is not a new exposure");
            TestAssert.That(book.IsExposed(7, 89f) && !book.IsExposed(7, 90.5f), "contact at 30 s keeps it lit to 90 s");
            var live = new List<uint>();
            book.Live(91f, live);
            TestAssert.Eq(live.Count, 0, "lapsed exposure leaves the live list");
            TestAssert.That(!book.See(7, 100f, 1f), "a lapsed site must bank 3 s again");

            var cap = new MaldxLedger();
            for (uint site = 1; site <= 9; site++)
                for (int s = 0; s < 3; s++) cap.See(site, s, 1f);
            TestAssert.Eq(cap.Exposed(2f), MaldxRules.MaxExposed, "eight exposed at once");
            TestAssert.That(!cap.IsExposed(9, 2f), "the ninth waits for a slot");
            TestAssert.That(cap.See(9, 63f, 1f) && cap.Exposed(63f) == 1, "the ninth exposes once the first eight lapse at 62 s");
            cap.Forget(9);
            TestAssert.Eq(cap.Exposed(63f), 0, "a destroyed site is forgotten");

            TestAssert.Near(SeductionRule.LureRadius, 4000f, "lure covers friendlies within 4 km");
            TestAssert.That(SeductionRule.Lures("ARH", 0.24f) && SeductionRule.Lures("SARH", 0.24f), "radar seekers bite at 25 %");
            TestAssert.That(!SeductionRule.Lures("ARH", 0.26f) && !SeductionRule.Lures("IR", 0f), "above 25 % or IR: no switch");
        }
    }
}
