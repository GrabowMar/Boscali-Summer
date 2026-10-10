using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Tests.Features.Hud
{
    internal static class HudTests
    {
        public static void Run()
        {
            FeedStore();
            Ranking();
        }

        private static void FeedStore()
        {
            float now = 0;
            var store = new HudFeedStore(() => now);
            IHudLine line = store.Acquire("a", "flight", "status");
            TestAssert.That(ReferenceEquals(line, store.Acquire("a", "flight", "status")), "Acquiring a stable key is idempotent");
            line.Set(HudTone.Warning, "PULL UP", "TERRAIN", float.NaN);
            store.Notice("info", HudTone.Info, "Saved", null);
            var output = new HudMessage[4];
            TestAssert.That(store.Snapshot(output, 1, CueContext.None) == 1 && output[0].Text == "PULL UP" && output[0].Bar == 0,
                "Notices never occupy a cell: the cells hold feeds only");
            TestAssert.That(store.Ticker(out TickerLine ticker) && ticker.Text == "Saved" && ticker.Channel == "info", "A notice goes to the ticker");
            TestAssert.That(store.HasLive("flight") && !store.HasLive("nobody"), "The board can ask which feeds are up");
            now = 2;
            TestAssert.That(store.Snapshot(output, 4, CueContext.None) == 1, "A line survives a one second hitch in its refresh");
            now = 3;
            TestAssert.That(store.Snapshot(output, 4, CueContext.None) == 0 && !store.HasLive("flight"), "Stale lines expire without Unity objects");
            for (int i = 0; i < HudFeedStore.Capacity; i++) store.Acquire("pool", "info", i.ToString())?.Set(HudTone.Info, "Live", null, 0);
            TestAssert.That(store.Acquire("over", "info", "ceiling") == null, "Pool capacity is hard bounded");
            store.Reset(); line.Set(HudTone.Warning, "Ghost", null, 1);
            TestAssert.That(store.Snapshot(output, 4, CueContext.None) == 0, "Pre-reset handles cannot resurrect old messages");
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

        /// <summary>Severity first, then the situation's context rank, then acquisition order.</summary>
        private static void Ranking()
        {
            float now = 0;
            var store = new HudFeedStore(() => now);
            // Acquired in the order that used to make the routine fuel/radio lines own the cue strip.
            store.Acquire("qol", "radio", "r").Set(HudTone.Info, "RADIO", "AGRAPOL FM", 0.1f);
            store.Acquire("ap", "ils", "i").Set(HudTone.Info, "ILS", "LOCALIZER LEFT 2", 0.4f);
            store.Acquire("th", "theater", "t").Set(HudTone.Info, "THEATER", "FRONT 3", 0.2f);
            var output = new HudMessage[4];
            store.Snapshot(output, 4, CueContext.None);
            TestAssert.That(output[0].Channel == "theater" && output[1].Channel == "ils" && output[2].Channel == "radio",
                "With no context the default ranks decide, not who acquired first");
            store.Snapshot(output, 4, CuePriority.Detect(true, false, false));
            TestAssert.That(output[0].Channel == "ils", "On approach the ILS line leads and owns the cue strip");

            store.Acquire("v", "skywell", "s").Set(HudTone.Info, "SKYWELL", "DOWN 12 · LEFT 14", 0.5f);
            store.Snapshot(output, 4, CuePriority.Detect(false, true, false));
            TestAssert.That(output[0].Channel == "skywell", "At the boom the SKYWELL cues lead");

            store.Acquire("wx", "weather", "w").Set(HudTone.Caution, "ICING", "FREEZING LEVEL 1500", 0f);
            store.Snapshot(output, 4, CuePriority.Detect(true, false, false));
            TestAssert.That(output[0].Channel == "weather", "A Caution still beats an Info, whatever the context");

            store.Acquire("a", "autopilot", "x").Set(HudTone.Info, "AP", null, 0f);
            TestAssert.That(store.Snapshot(output, 2, CueContext.None) == 2, "The limit still bounds the board");
            now = 0.5f;
            TestAssert.That(store.Snapshot(output, 4, CueContext.None) == 4 && output[3].Channel != null, "Every live line is ranked");
        }
    }
}
