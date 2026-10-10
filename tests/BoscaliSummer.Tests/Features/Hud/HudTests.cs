using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Tests.Features.Hud
{
    internal static class HudTests
    {
        public static void Run()
        {
            float now = 0;
            var store = new HudFeedStore(() => now);
            IHudLine line = store.Acquire("a", "flight", "status");
            TestAssert.That(ReferenceEquals(line, store.Acquire("a", "flight", "status")), "Acquiring a stable key is idempotent");
            line.Set(HudTone.Warning, "PULL UP", "TERRAIN", float.NaN);
            store.Notice("info", HudTone.Info, "Saved", null);
            TestAssert.That(store.Ticker(out TickerLine ticker) && ticker.Text == "Saved" && ticker.Channel == "info", "A notice goes to the ticker");
            TestAssert.That(store.HasLive("flight") && !store.HasLive("nobody") && !store.HasLive("info"), "The board can ask which feeds are up; a notice is not a feed");
            now = 2;
            TestAssert.That(store.HasLive("flight"), "A line survives a one second hitch in its refresh");
            now = 3;
            TestAssert.That(!store.HasLive("flight"), "Stale lines expire without Unity objects");
            for (int i = 0; i < HudFeedStore.Capacity; i++) store.Acquire("pool", "info", i.ToString())?.Set(HudTone.Info, "Live", null, 0);
            TestAssert.That(store.Acquire("over", "info", "ceiling") == null, "Pool capacity is hard bounded");
            store.Reset(); line.Set(HudTone.Warning, "Ghost", null, 1);
            TestAssert.That(!store.HasLive("flight"), "Pre-reset handles cannot resurrect old messages");
            store.Notice("info", HudTone.Info, "Cleared by reset", null); store.Reset();
            TestAssert.That(!store.Ticker(out _), "A scene reset clears the ticker too");

            // An evicted line's handle is dead, but acquiring again yields a fresh live line (the contract line used to go dead for good).
            var small = new HudFeedStore(() => now);
            IHudLine contract = small.Acquire("ops", "contracts", "card0");
            contract.Set(HudTone.Info, "CONTRACT", null, 0f);
            contract.Release();
            var others = new IHudLine[HudFeedStore.Capacity];
            for (int i = 0; i < HudFeedStore.Capacity; i++)
            {
                others[i] = small.Acquire("other", "info", "k" + i);   // the last one evicts the released contract line
                others[i].Set(HudTone.Info, "Live", null, 0f);
            }
            contract.Set(HudTone.Info, "CONTRACT", null, 0f);
            TestAssert.That(!small.HasLive("contracts"), "A handle kept across an eviction shows nothing");
            others[0].Release();
            IHudLine revived = small.Acquire("ops", "contracts", "card0");
            TestAssert.That(revived != null, "Acquiring again after the eviction yields a fresh line");
            revived.Set(HudTone.Info, "CONTRACT", null, 0f);
            TestAssert.That(small.HasLive("contracts"), "...and it is live");
        }
    }
}
