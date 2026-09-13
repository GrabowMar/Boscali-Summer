using System.Collections.Generic;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.Progression
{
    internal static class ProgressionTests
    {
        public static void Run()
        {
            Catalog();
            Points();
            PilotPoints();
            Spending();
            SharedSkills();
        }

        private static void Catalog()
        {
            var capabilities = new HashSet<string>();
            for (int i = 0; i < PerkCatalog.All.Length; i++)
            {
                PerkDefinition definition = PerkCatalog.All[i];
                TestAssert.That(definition.Id == i, "perk id must equal its catalogue index");
                TestAssert.That(definition.Cost >= 1, "perk " + definition.Name + " costs nothing");
                TestAssert.That(!string.IsNullOrEmpty(definition.Group),
                    "perk " + definition.Name + " has no group heading");
                TestAssert.That(!string.IsNullOrEmpty(definition.Icon),
                    "perk " + definition.Name + " has no icon key");
                TestAssert.That(!string.IsNullOrEmpty(PerkCatalog.CodeOf(definition)),
                    "perk " + definition.Name + " has no display code");
                if (definition.Capability == null)
                {
                    TestAssert.That(definition.Multiplier != 1f,
                        "passive perk " + definition.Name + " has no effect");
                    continue;
                }
                TestAssert.That(definition.Multiplier == 1f,
                    "perk " + definition.Name + " both grants and scales");
                TestAssert.That(capabilities.Add(definition.Capability),
                    "capability " + definition.Capability + " is granted by more than one perk");
            }
            TestAssert.That(PerkCatalog.All.Length <= PerkCatalog.MaximumPerks,
                "the catalogue outgrew the perk mask");

            // The one guard that keeps the perk and support catalogues from drifting apart:
            // every support capability must be reachable through exactly one perk.
            string[] required =
            {
                SupportCapabilities.Recon, SupportCapabilities.Fortify,
                SupportCapabilities.Artillery, SupportCapabilities.Emp
            };
            for (int i = 0; i < required.Length; i++)
                TestAssert.That(capabilities.Contains(required[i]),
                    "no perk grants the support capability " + required[i]);
            TestAssert.That(capabilities.Count == required.Length,
                "a perk grants a capability no support action requires");
        }

        private static void Points()
        {
            TestAssert.That(PerkPoints.Earned(0, 500, 6) == 0, "an unflown mission granted a point");
            TestAssert.That(PerkPoints.Earned(499, 500, 6) == 0, "a partial tier granted a point");
            TestAssert.That(PerkPoints.Earned(500, 500, 6) == 1, "the first tier granted no point");
            TestAssert.That(PerkPoints.Earned(1499, 500, 6) == 2, "tier rounding is wrong");
            TestAssert.That(PerkPoints.Earned(100000, 500, 6) == 6, "the point ceiling was exceeded");
            TestAssert.That(PerkPoints.Earned(-50, 500, 6) == 0, "a negative score granted points");
            TestAssert.That(PerkPoints.Earned(500, 0, 6) == 0, "a zero tier size did not fail closed");
        }

        private static void PilotPoints()
        {
            TestAssert.That(PerkPoints.EarnedForPilot(70500, 70000, 500, 6, 0) == 1,
                "a successor above ushort score range must earn only its own score point");
            TestAssert.That(PerkPoints.EarnedForPilot(70000, 70000, 500, 6, 0) == 0,
                "a new pilot inherited the retired pilot's score");
            TestAssert.That(PerkPoints.EarnedForPilot(69999, 70000, 500, 6, 2) == 2,
                "a score below the origin must retain bonus points without earning score points");
            TestAssert.That(PerkPoints.EarnedForPilot(100000, 0, 500, 6, 3) == 9,
                "ace bonus points must extend the score-only ceiling");
            TestAssert.That(PerkPoints.EarnedForPilot(499, 0, 500, 6, 1) == 1,
                "a partial score point must not be rounded up when adding an ace bonus");
            TestAssert.That(PerkPoints.EarnedForPilot(int.MaxValue, 0, 1, int.MaxValue, int.MaxValue) == 20,
                "large score and bonus inputs must not overflow or exceed the overall 20-point ceiling");
            TestAssert.That(PerkPoints.EarnedForPilot(int.MaxValue, int.MinValue, 1, 6, 0) == 6,
                "a negative origin must be normalized before score subtraction");
            TestAssert.That(PerkPoints.EarnedForPilot(int.MinValue, int.MaxValue, 500, 6, 3) == 3,
                "extreme negative score must not wrap into score-derived points");
            TestAssert.That(PerkPoints.EarnedForPilot(500, 0, 500, 6, int.MinValue) == 1,
                "negative bonuses must not remove legitimately earned score points");
            TestAssert.That(PerkPoints.EarnedForPilot(500, 0, 0, 6, 2) == 2,
                "an invalid score interval must disable score awards while preserving independent bonuses");
            TestAssert.That(PerkPoints.EarnedForPilot(500, 0, 500, -1, 2) == 2,
                "an invalid score ceiling must not invalidate independent ace bonuses");
        }

        private static void Spending()
        {
            byte oneCost = FindByCost(1);
            byte twoCost = FindByCost(2);

            var state = new PerkState();
            TestAssert.That(state.AvailablePoints(0) == 0, "an empty state started with points");
            TestAssert.That(!state.TryUnlock(twoCost, 1), "a two-point perk was bought with one point");
            TestAssert.That(state.TryUnlock(oneCost, 1), "a one-point perk could not be bought");
            TestAssert.That(state.AvailablePoints(1) == 0, "a spent point stayed available");
            TestAssert.That(!state.TryUnlock(oneCost, 6), "a perk was bought twice");
            TestAssert.That(state.TryUnlock(twoCost, 3), "a two-point perk failed with three points");
            TestAssert.That(state.SpentPoints == 3, "spent points do not sum the perk costs");
            TestAssert.That(!state.TryUnlock(255, 6), "an undefined perk id was accepted");

            // The mask is a uint and the wire format packs it as one, so the whole catalogue
            // must survive a round trip through that width.
            var everything = new PerkState();
            for (byte i = 0; i < PerkCatalog.All.Length; i++) everything.ForceUnlock(i);
            var restored = new PerkState(everything.Mask);
            for (byte i = 0; i < PerkCatalog.All.Length; i++)
                TestAssert.That(restored.Has(i), "perk " + i + " was lost in the mask round trip");

            var debug = new PerkState();
            TestAssert.That(debug.ForceUnlock(twoCost), "bypass failed to grant a perk");
            TestAssert.That(!debug.ForceUnlock(twoCost), "bypass granted a duplicate perk");
        }

        private static void SharedSkills()
        {
            TestAssert.That(AceSkillCatalog.All.Length == AceSkillCatalog.MaximumSkills,
                "the shared ace skill catalogue changed size");
            for (int i = 0; i < AceSkillCatalog.All.Length; i++)
            {
                AceSkillDefinition skill = AceSkillCatalog.All[i];
                TestAssert.That(skill.Bit == i, "ace skill bits must equal their catalogue index");
                TestAssert.That(!string.IsNullOrEmpty(skill.Code) && !string.IsNullOrEmpty(skill.Name) &&
                    !string.IsNullOrEmpty(skill.Description),
                    "ace skill " + i + " is missing presentation metadata");
                TestAssert.That(AceSkillCatalog.Has(1 << i, i), "an active ace skill bit read as missing");
                TestAssert.That(!AceSkillCatalog.Has(1 << i, (i + 1) % AceSkillCatalog.All.Length),
                    "an ace skill bit leaked into its neighbour");
            }
            TestAssert.That(AceSkillCatalog.MaximumSkills <= 32 &&
                (1 << AceSkillCatalog.MaximumSkills) - 1 <= AceSkillCatalog.MaskWindow,
                "the shared skill catalogue no longer fits the replicated four-bit mask");
            TestAssert.That(PerkCatalog.CodeOf(PerkCatalog.All[0]) == "PAS",
                "a passive perk must read as PAS");
            for (int i = 0; i < PerkCatalog.All.Length; i++)
            {
                if (PerkCatalog.All[i].Capability != SupportCapabilities.Emp) continue;
                TestAssert.That(PerkCatalog.CodeOf(PerkCatalog.All[i]) == "EW",
                    "the electronic warfare authorisation must read as EW");
            }
        }

        private static byte FindByCost(byte cost)
        {
            for (int i = 0; i < PerkCatalog.All.Length; i++)
                if (PerkCatalog.All[i].Cost == cost) return PerkCatalog.All[i].Id;
            TestAssert.That(false, "the catalogue has no perk costing " + cost + " points");
            return 0;
        }
    }
}
