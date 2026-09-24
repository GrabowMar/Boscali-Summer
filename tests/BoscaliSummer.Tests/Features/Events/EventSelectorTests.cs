using System;
using System.Collections.Generic;
using BoscaliSummer.Features.Events.Domain;

namespace BoscaliSummer.Tests.Features.Events
{
    internal static class EventSelectorTests
    {
        public static void Run()
        {
            CatalogIsWellFormed();
            CatalogIsGraded();
            SelectionIsStableAndAvoidsRepeats();
            DurationStaysInsideItsWindow();
            EffectStrengthScalesTheModifier();
            ResponseOnlyMovesThePriceInThePlayersFavour();
            DecisionRoutesStayBounded();
        }

        private static void CatalogIsWellFormed()
        {
            TestAssert.That(EventCatalog.Count >= 8, "catalog is curated, not a stub");
            TestAssert.That(EventCatalog.Count <= sbyte.MaxValue + 1,
                "catalog indices fit the signed-byte event protocol");
            foreach (string id in new[] { "press_censorship", "dust_storm", "holiday_stand_down", "scrap_drive" })
            {
                EventDefinition addition = Array.Find(EventCatalog.All, entry => entry.Id == id);
                TestAssert.That(addition != null && addition.SupportCostMultiplier != 1f,
                    "New minor dispatch " + id + " changes a real support price");
            }
            for (int i = 0; i < EventCatalog.Count; i++)
            {
                EventDefinition entry = EventCatalog.At(i);
                TestAssert.That(entry != null, "no null catalog entry");
                TestAssert.That(!string.IsNullOrWhiteSpace(entry.Id), "entry has an id");
                TestAssert.That(!string.IsNullOrWhiteSpace(entry.Title), "entry has a title");
                TestAssert.That(!string.IsNullOrWhiteSpace(entry.FlavorText), "entry has flavor text");
                TestAssert.That(!string.IsNullOrWhiteSpace(entry.IconKey), "entry has an icon key");
                TestAssert.That(entry.SupportCostMultiplier >= 0.5f && entry.SupportCostMultiplier <= 2f,
                    "modifier stays in a sane band");
                TestAssert.That(entry.SupportCooldownMultiplier >= 0.8f && entry.SupportCooldownMultiplier <= 1.2f,
                    "event tempo stays within the authored support reset band");
                TestAssert.That(entry.DurationMinSeconds > 0 &&
                                entry.DurationMaxSeconds >= entry.DurationMinSeconds,
                    "duration window is ordered and positive");
                for (int j = i + 1; j < EventCatalog.Count; j++)
                    TestAssert.That(EventCatalog.At(j).Id != entry.Id, "ids are unique");
            }

            TestAssert.That(EventCatalog.At(-1) == null && EventCatalog.At(EventCatalog.Count) == null,
                "out-of-range lookups return null");
        }

        private static void CatalogIsGraded()
        {
            int minors = 0, mediums = 0, supers = 0;
            for (int i = 0; i < EventCatalog.Count; i++)
            {
                EventDefinition entry = EventCatalog.At(i);
                switch (entry.Tier)
                {
                    case EventTier.Minor: minors++; break;
                    case EventTier.Medium: mediums++; break;
                    default: supers++; break;
                }

                if (entry.Tier == EventTier.Super)
                {
                    TestAssert.That(entry.Script.Length > 0,
                        "a superevent is scripted, not a bigger medium");
                    TestAssert.That(entry.Script.Length <= 4 && entry.Script[0].AtSeconds == 0,
                        "a super moves the theater immediately and fits the alert timeline");
                    TestAssert.That(entry.DurationMinSeconds >= 180,
                        "a superevent runs long enough to feel like news");
                    TestAssert.That(EventCatalog.TierLabel(entry.Tier) == "SUPEREVENT",
                        "the tier word is the one the panel shows");
                    bool movesConvoy = false;
                    for (int s = 0; s < entry.Script.Length; s++)
                    {
                        EventStep step = entry.Script[s];
                        movesConvoy |= step.Effect == EventEffect.Convoy;
                        TestAssert.That(step.Effect != EventEffect.None,
                            "a scripted beat does something");
                        TestAssert.That(!string.IsNullOrWhiteSpace(step.Label),
                            "a scripted beat names itself for the ticker");
                        TestAssert.That(step.AtSeconds < entry.DurationMinSeconds,
                            "every beat lands inside the shortest possible run");
                        TestAssert.That(step.Amount >= 0f || step.Effect == EventEffect.Funds ||
                            step.Effect == EventEffect.Morale,
                            "only treasury or morale beats may withdraw value");
                        if (s > 0)
                            TestAssert.That(step.AtSeconds > entry.Script[s - 1].AtSeconds,
                                "scripted beats are ordered");
                    }
                    TestAssert.That(movesConvoy,
                        "each super attempts a visible vanilla convoy deployment");
                    TestAssert.That(entry.Target != EventTarget.All ||
                                    entry.SupportCostMultiplier != 1f,
                        "a global superevent still changes a price");
                }
                else
                {
                    TestAssert.That(entry.Script.Length == 0,
                        "minor and medium events carry no script");
                    TestAssert.That(entry.Target == EventTarget.All,
                        "only a superevent is aimed at one side");
                    float deviation = Math.Abs(entry.SupportCostMultiplier - 1f);
                    TestAssert.That(entry.Tier == EventTier.Minor
                            ? deviation <= 0.15f
                            : deviation > 0.15f && deviation <= 0.5f,
                        "the tier matches how far the event moves the price");
                }
            }

            TestAssert.That(minors >= 3, "the director has quiet rolls to spend");
            TestAssert.That(mediums >= 6, "the director has ordinary drama to spend");
            TestAssert.That(supers >= 4, "the director has superevents to escalate to");
        }

        private static void SelectionIsStableAndAvoidsRepeats()
        {
            TestAssert.That(EventSelector.SelectIndex(1234u, 1, null) == 0,
                "a one-entry catalog always draws it");
            TestAssert.That(
                EventSelector.SelectIndex(99u, EventCatalog.Count, new List<int>()) ==
                EventSelector.SelectIndex(99u, EventCatalog.Count, new List<int>()),
                "selection is seed-stable");

            var recent = new List<int>(EventSelector.RecentWindow);
            for (int i = 0; i < 256; i++)
            {
                int index = EventSelector.SelectIndex((uint)(i * 7919 + 17), EventCatalog.Count, recent);
                TestAssert.That(index >= 0 && index < EventCatalog.Count, "index stays inside the catalog");
                TestAssert.That(!recent.Contains(index), "a recent entry is never drawn again");
                recent.Add(index);
                if (recent.Count > EventSelector.RecentWindow) recent.RemoveAt(0);
            }
        }

        private static void DurationStaysInsideItsWindow()
        {
            for (int i = 0; i < EventCatalog.Count; i++)
            {
                EventDefinition entry = EventCatalog.At(i);
                for (int draw = 0; draw < 64; draw++)
                {
                    int duration = EventSelector.RollDuration(
                        (uint)(i * 131 + draw * 7 + 1), entry.DurationMinSeconds, entry.DurationMaxSeconds);
                    TestAssert.That(
                        duration >= entry.DurationMinSeconds && duration <= entry.DurationMaxSeconds,
                        "duration inside its window");
                }
            }

            TestAssert.That(EventSelector.RollDuration(7u, 30, 30) == 30, "a fixed window rolls its only value");
            TestAssert.That(EventSelector.RollDuration(7u, 0, 0) == 0, "zero window is zero");
            TestAssert.That(EventSelector.RollDuration(7u, 40, 10) == 40, "inverted window degrades to the minimum");
        }

        private static void EffectStrengthScalesTheModifier()
        {
            TestAssert.That(EventSelector.EffectiveSupportMultiplier(1.5f, 1f) == 1.5f,
                "shipped strength keeps the authored multiplier");
            TestAssert.That(EventSelector.EffectiveSupportMultiplier(1.5f, 0f) == 1f,
                "zero strength disables the modifier");
            TestAssert.That(EventSelector.EffectiveSupportMultiplier(0.5f, 0.5f) == 0.75f,
                "half strength halves a discount");
            TestAssert.That(EventSelector.EffectiveSupportMultiplier(1.5f, 2f) == 2f,
                "double strength doubles a surcharge");
            TestAssert.That(EventSelector.EffectiveSupportMultiplier(0.6f, -1f) == 1f,
                "negative strength is clamped off");
        }

        private static void ResponseOnlyMovesThePriceInThePlayersFavour()
        {
            TestAssert.That(EventSelector.ResponseKind(1.25f) == EventResponseKind.Contain,
                "a penalty invites contain");
            TestAssert.That(EventSelector.ResponseKind(0.75f) == EventResponseKind.Leverage,
                "a discount invites leverage");
            TestAssert.That(EventSelector.ResponseKind(1f) == EventResponseKind.None,
                "a neutral event offers no response");

            TestAssert.That(Near(EventSelector.ApplyResponse(1.5f, EventResponseKind.Contain), 1.25f),
                "contain halves the penalty");
            TestAssert.That(Near(EventSelector.ApplyResponse(0.6f, EventResponseKind.Leverage), 0.4f),
                "leverage deepens the discount");
            TestAssert.That(Near(EventSelector.ApplyResponse(1.5f, EventResponseKind.None), 1.5f),
                "no response changes nothing");

            TestAssert.That(EventSelector.ResponseCost(1f) == 0, "a neutral event costs nothing");
            int light = EventSelector.ResponseCost(1.1f);
            int heavy = EventSelector.ResponseCost(1.5f);
            TestAssert.That(light >= 200 && heavy > light, "cost grows with severity");
            TestAssert.That(heavy % 50 == 0, "cost is rounded to a readable figure");

            TestAssert.That(EventSelector.EffectSummary(1.25f) == "+25% SUPPORT COST", "surcharge badge");
            TestAssert.That(EventSelector.EffectSummary(0.6f) == "-40% SUPPORT COST", "discount badge");
            TestAssert.That(EventSelector.EffectSummary(1f) == "NO EFFECT", "neutral badge");

            for (int i = 0; i < EventCatalog.Count; i++)
            {
                EventDefinition entry = EventCatalog.At(i);
                float effective = EventSelector.EffectiveSupportMultiplier(entry.SupportCostMultiplier, 1f);
                EventResponseKind kind = EventSelector.ResponseKind(effective);
                if (kind == EventResponseKind.None) continue;
                TestAssert.That(EventSelector.ResponseCost(effective) > 0, "a real modifier has a price");
                TestAssert.That(EventSelector.ApplyResponse(effective, kind) < effective,
                    "a response only ever lowers the multiplier in the player's favour");
            }
        }

        private static void DecisionRoutesStayBounded()
        {
            TestAssert.That(EventSelector.TreasuryCost(1f) == 0,
                "a cost-neutral event cannot spend faction funds on a price response");
            TestAssert.That(EventSelector.TreasuryCost(1.35f) > 20 &&
                EventSelector.TreasuryCost(1.5f) > EventSelector.TreasuryCost(1.35f),
                "treasury price rises with event severity");
            TestAssert.That(EventSelector.ApplyResponse(
                EventSelector.ApplyResponse(0.6f, EventResponseKind.Treasury),
                EventResponseKind.Leverage) >= 0.3f,
                "a personal response stacked after a faction discount cannot make support free");
            foreach (float multiplier in new[] { 0.6f, 0.85f, 1.15f, 1.5f, 2f })
            {
                float baseline = EventSelector.ApplyResponse(multiplier,
                    EventSelector.ResponseKind(multiplier));
                foreach (EventResponseKind kind in new[]
                    { EventResponseKind.Treasury, EventResponseKind.Contract, EventResponseKind.Perk })
                {
                    float answer = EventSelector.ApplyResponse(multiplier, kind);
                    TestAssert.That(answer >= 0.3f && answer < baseline,
                        "alternate response improves the price without making support free");
                }
            }
            bool neutralCostWithTempo = false;
            foreach (EventDefinition entry in EventCatalog.All)
                neutralCostWithTempo |= entry.SupportCostMultiplier == 1f &&
                    entry.SupportCooldownMultiplier != 1f;
            TestAssert.That(neutralCostWithTempo,
                "a tempo-only event is distinct from a price event");
            TestAssert.That(Near(EventSelector.EffectiveSupportMultiplier(1.2f, 0f), 1f) &&
                Near(EventSelector.EffectiveSupportMultiplier(1.2f, 2f), 1.4f),
                "host strength scales support reset as well as price");
        }

        private static bool Near(float a, float b) => Math.Abs(a - b) < 0.0001f;
    }
}
