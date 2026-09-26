using BoscaliSummer.Features.Hud.Domain;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.Hud
{
    internal static class HudTests
    {
        private static void FeedStore()
        {
            float now = 0;
            var store = new HudFeedStore(() => now);
            IHudLine line = store.Acquire("a", "flight", "status");
            TestAssert.That(ReferenceEquals(line, store.Acquire("a", "flight", "status")), "Acquiring a stable key is idempotent");
            line.Set(HudTone.Warning, "PULL UP", "TERRAIN", float.NaN);
            store.Notice("info", HudTone.Info, "Saved", null, 8);
            var output = new HudMessage[4];
            TestAssert.That(store.Snapshot(output, 1, _ => true, true) == 1 && output[0].Text == "PULL UP" && output[0].Bar == 0,
                "Routine notices cannot displace warnings, including in a one-row layout");
            TestAssert.That(store.Snapshot(output, 4, _ => false, false) == 0, "Muted feeds must disappear");
            now = 2;
            TestAssert.That(store.Snapshot(output, 4, _ => true, false) == 0, "Stale lines expire without Unity objects");
            for (int i = 0; i < HudFeedStore.Capacity; i++) store.Acquire("pool", "info", i.ToString())?.Set(HudTone.Info, "Live", null, 0);
            TestAssert.That(store.Acquire("over", "info", "ceiling") == null, "Pool capacity is hard bounded");
            store.Reset(); line.Set(HudTone.Warning, "Ghost", null, 1);
            TestAssert.That(store.Snapshot(output, 4, _ => true, true) == 0, "Pre-reset handles cannot resurrect old messages");
            store.Notice("info", HudTone.Info, "Mute me", null, 8); store.Mute("info");
            TestAssert.That(store.Snapshot(output, 4, _ => true, true) == 0, "Muting purges active notices");
        }

        public static void Run()
        {
            var queue = new HudNoticeQueue();
            queue.Push("muted", HudTone.Warning, "Old warning", null, 0, 10);
            queue.Push("kept", HudTone.Info, "Visible notice", null, 0, 10);
            queue.RemoveChannel("muted");
            TestAssert.That(queue.Count == 1 && queue.TryGet(0, out _, out string text, out _) && text == "Visible notice",
                "Muting a feed must remove already queued notices without touching other feeds");
            FeedStore();
            Layout();
            Notices();
        }

        /// <summary>
        /// The presentation ladder the settings page labels and the board applies. Every step is
        /// reachable from both directions, every value is inside its bound, and every anchor
        /// resolves to the screen edge it names.
        /// </summary>
        private static void Layout()
        {
            TestAssert.That(HudLayout.AnchorCount == 8, "Eight anchor presets, no more and no less");
            TestAssert.That(HudLayout.AnchorName(0) == "UNDER WEAPONS",
                "The default anchor is the one under the vanilla weapon column");
            for (int i = 0; i < HudLayout.AnchorCount; i++)
            {
                TestAssert.That(HudLayout.Cycle(i, HudLayout.AnchorCount, 1) == (i + 1) % HudLayout.AnchorCount,
                    "Next from anchor " + i + " is reachable");
                TestAssert.That(HudLayout.Cycle(i, HudLayout.AnchorCount, -1) ==
                    (i + HudLayout.AnchorCount - 1) % HudLayout.AnchorCount,
                    "Previous from anchor " + i + " is reachable");
                TestAssert.That(!string.IsNullOrEmpty(HudLayout.AnchorName(i)),
                    "Every anchor has a label the pilot can read");
            }

            TestAssert.That(HudLayout.ClampAnchor(-1) == 0 && HudLayout.ClampAnchor(99) == HudLayout.AnchorCount - 1,
                "An out-of-range anchor clamps instead of throwing");
            TestAssert.That(HudLayout.ClampScale(-5) == 0 && HudLayout.ClampScale(99) == HudLayout.ScaleCount - 1,
                "An out-of-range size step clamps");
            TestAssert.That(HudLayout.ClampOpacity(99) == HudLayout.OpacityCount - 1,
                "An out-of-range opacity step clamps");
            TestAssert.That(HudLayout.ClampRows(0) == HudLayout.MinRows && HudLayout.ClampRows(99) == HudLayout.MaxRows,
                "The row cap is bounded at both ends");
            TestAssert.That(HudLayout.ClampNoticeSeconds(float.NaN) == HudLayout.DefaultNoticeSeconds,
                "A nonfinite notice dwell falls back to the default instead of NaN");
            TestAssert.That(HudLayout.ClampNoticeSeconds(-1f) == HudLayout.MinNoticeSeconds &&
                HudLayout.ClampNoticeSeconds(9999f) == HudLayout.MaxNoticeSeconds,
                "The notice dwell is bounded at both ends");

            for (int step = 1; step < HudLayout.ScaleCount; step++)
            {
                TestAssert.That(HudLayout.Scale(step) > HudLayout.Scale(step - 1),
                    "Size steps grow monotonically");
            }
            TestAssert.That(HudLayout.Opacity(0) > HudLayout.Opacity(1) &&
                HudLayout.Opacity(1) > HudLayout.Opacity(2),
                "Opacity steps recede monotonically");
            TestAssert.That(HudLayout.Opacity(HudLayout.OpacityCount - 1) == 0f,
                "The last opacity step is OFF, which hides the element");
            TestAssert.That(HudLayout.Opacity(0) <= 1f && HudLayout.Opacity(0) > 0f,
                "FULL is fully solid but never over-bright");

            TestAssert.That(HudLayout.Place(HudAnchor.TopCentre).AnchorY > 0.9f,
                "A top anchor sits at the top of the screen");
            TestAssert.That(HudLayout.Place(HudAnchor.BottomRight).AnchorY < 0.1f,
                "A bottom anchor sits at the bottom of the screen");
            TestAssert.That(HudLayout.Place(HudAnchor.UnderWeapons).AnchorX == 1f &&
                HudLayout.Place(HudAnchor.UnderWeapons).AnchorY == 1f,
                "The under-weapons anchor starts from the top-right corner the weapon column occupies");
            TestAssert.That(HudLayout.Place(HudAnchor.MiddleLeft).AnchorX == 0f &&
                HudLayout.Place(HudAnchor.MiddleLeft).AnchorY == 0.5f,
                "A middle anchor is centred vertically on its edge");

            for (int i = 0; i < HudLayout.AnchorCount; i++)
            {
                HudAnchor anchor = (HudAnchor)i;
                HudPlacement place = HudLayout.Place(anchor);
                TestAssert.That(place.AnchorX >= 0f && place.AnchorX <= 1f &&
                    place.AnchorY >= 0f && place.AnchorY <= 1f,
                    "Anchor " + HudLayout.AnchorName(i) + " resolves to a normalised screen anchor");
            }
        }

        /// <summary>
        /// The transient feed. It is a fixed ring, it never stacks a duplicate, and the oldest
        /// notice is the one that goes when it is full.
        /// </summary>
        private static void Notices()
        {
            var queue = new HudNoticeQueue();
            TestAssert.That(queue.Count == 0, "A fresh queue holds nothing");

            queue.Push("weather", HudTone.Warning, "SUPERCELL", null, 0f, 8f);
            TestAssert.That(queue.Count == 1, "A pushed notice is live");

            // Re-pushing the same channel and words refreshes the dwell instead of stacking:
            // otherwise a condition that re-fires every tick would flood the element.
            queue.Push("weather", HudTone.Warning, "SUPERCELL", null, 4f, 8f);
            TestAssert.That(queue.Count == 1, "Identical text within the dwell refreshes rather than stacks");
            TestAssert.That(!queue.Expire(11f), "A refreshed notice outlives its first expiry");
            TestAssert.That(queue.Expire(12.5f), "A refreshed notice expires at the refreshed time");
            TestAssert.That(queue.Count == 0, "An expired notice leaves the queue");

            queue.Push("a", HudTone.Info, "ONE", null, 0f, 8f);
            queue.Push("b", HudTone.Info, "TWO", null, 0f, 8f);
            queue.Push("c", HudTone.Info, "THREE", null, 0f, 8f);
            queue.Push("d", HudTone.Info, "FOUR", null, 0f, 8f);
            TestAssert.That(queue.Count == HudNoticeQueue.Capacity,
                "The queue is bounded at its capacity, not by the caller's discipline");
            queue.TryGet(0, out _, out string oldest, out _);
            TestAssert.That(oldest == "TWO", "A full queue drops its oldest notice, not its newest");
            queue.TryGet(HudNoticeQueue.Capacity - 1, out _, out string newest, out _);
            TestAssert.That(newest == "FOUR", "A new notice lands last, under the ones already read");

            queue.Push("a", HudTone.Warning, "", null, 0f, 8f);
            TestAssert.That(queue.Count == HudNoticeQueue.Capacity, "An empty notice is not pushed at all");

            queue.Clear();
            TestAssert.That(queue.Count == 0 && !queue.TryGet(0, out _, out _, out _),
                "Clear empties the queue and reads of an empty queue fail honestly");

            var reused = new HudNoticeQueue();
            for (int i = 0; i < 50; i++)
                reused.Push("churn", HudTone.Info, "LINE " + i, null, i, 1f);
            Reusable(reused);
        }

        private static void Reusable(HudNoticeQueue queue)
        {
            for (int i = 0; i < 50; i++)
            {
                queue.Expire(i + 10f);
                if (i % 3 == 0) queue.Push("churn", HudTone.Info, "LATER " + i, null, i + 10f, 1f);
            }
            queue.Expire(1e6f);
            TestAssert.That(queue.Count == 0, "Churn across a whole mission empties cleanly");
        }
    }
}
