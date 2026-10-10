using System;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The typed scope-feed registry and the gauge registry.</summary>
    internal static class ScopeFeedsTests
    {
        private struct FeedA { public int N; }
        private struct FeedB { public string Word; }

        public static void Run()
        {
            ScopeFeeds.Clear();
            TestAssert.That(!ScopeFeeds.Has<FeedA>() && !ScopeFeeds.TryRead(out FeedA none) && none.N == 0, "Nothing registered reads false and default");

            int reads = 0;
            ScopeProvider<FeedA> one = (out FeedA d) => { reads++; d = new FeedA { N = 7 }; return true; };
            ScopeFeeds.Register(one);
            TestAssert.That(ScopeFeeds.Has<FeedA>() && ScopeFeeds.TryRead(out FeedA a) && a.N == 7 && reads == 1, "A registered provider answers on demand");
            TestAssert.That(!ScopeFeeds.Has<FeedB>(), "Feeds are independent: no shared file, no shared slot");

            ScopeProvider<FeedA> two = (out FeedA d) => { d = new FeedA { N = 9 }; return true; };
            ScopeFeeds.Register(two);
            ScopeFeeds.TryRead(out a);
            TestAssert.Eq(a.N, 9, "A second registration replaces the first");
            ScopeFeeds.Unregister(one);
            TestAssert.That(ScopeFeeds.TryRead(out a) && a.N == 9, "Unregistering a replaced provider leaves the newer owner alone");
            ScopeFeeds.Unregister(two);
            TestAssert.That(!ScopeFeeds.TryRead(out a) && a.N == 0, "Unregistering the current one withdraws the feed");

            ScopeFeeds.Register<FeedA>((out FeedA d) => { d = new FeedA { N = 1 }; return false; });
            TestAssert.That(!ScopeFeeds.TryRead(out a) && a.N == 0, "A provider with nothing live reads false and a default struct");

            string reported = null;
            ScopeFeeds.OnError = (site, e) => reported = site;
            ScopeFeeds.Register<FeedB>((out FeedB d) => throw new InvalidOperationException("boom"));
            TestAssert.That(!ScopeFeeds.TryRead(out FeedB b) && b.Word == null && reported != null && reported.Contains("FeedB"), "A throwing provider is reported and reads as not live");
            ScopeFeeds.OnError = null;

            ScopeFeeds.Publish(new FeedB { Word = "fixture" });
            TestAssert.That(ScopeFeeds.TryRead(out b) && b.Word == "fixture", "Publish serves a constant (the offline fixtures)");
            ScopeFeeds.Clear();
            TestAssert.That(!ScopeFeeds.Has<FeedA>() && !ScopeFeeds.Has<FeedB>(), "Clear withdraws every feed");

            ScopeGauges.Clear();
            GaugeProvider lance = (out GaugeSpec g) => { g = new GaugeSpec { Label = "LANCE", Value = 0.5f }; return true; };
            GaugeProvider idle = (out GaugeSpec g) => { g = default; return false; };
            ScopeGauges.Register("lance", lance);
            ScopeGauges.Register("radio", idle);
            TestAssert.That(ScopeGauges.Count == 2 && ScopeGauges.TryRead(0, out GaugeSpec spec) && spec.Id == "lance" && spec.Label == "LANCE", "A gauge reads under its registered id");
            TestAssert.That(!ScopeGauges.TryRead(1, out _) && !ScopeGauges.TryRead(5, out _), "A gauge with nothing live, or out of range, reads false");
            ScopeGauges.Unregister("lance", idle);
            TestAssert.Eq(ScopeGauges.Count, 2, "Only the registered provider can unregister its gauge");
            ScopeGauges.Unregister("lance", lance);
            TestAssert.Eq(ScopeGauges.Count, 1, "Unregister removes it");
            ScopeGauges.Clear();
        }
    }
}
