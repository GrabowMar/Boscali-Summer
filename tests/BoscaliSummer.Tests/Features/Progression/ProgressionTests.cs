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
            Lanes();
            Points();
            PilotPoints();
            Spending();
            Prerequisites();
            SharedSkills();
        }

        private static void Catalog()
        {
            var capabilities = new HashSet<string>();
            var lanes = new List<string>();
            int tools = 0;
            for (int i = 0; i < PerkCatalog.All.Length; i++)
            {
                PerkDefinition definition = PerkCatalog.All[i];
                TestAssert.That(definition.Id == i, "perk id must equal its catalogue index");
                TestAssert.That(definition.Cost == PerkDefinition.PickCost,
                    "grade " + definition.Name + " costs something other than one pick");
                TestAssert.That(!string.IsNullOrEmpty(definition.Lane),
                    "grade " + definition.Name + " has no lane");
                TestAssert.That(!string.IsNullOrEmpty(definition.Icon),
                    "grade " + definition.Name + " has no icon key");
                TestAssert.That(!string.IsNullOrEmpty(PerkCatalog.CodeOf(definition)),
                    "grade " + definition.Name + " has no display code");
                TestAssert.That(definition.Grade >= 1 && definition.Grade <= PerkCatalog.MaximumDepth,
                    "grade " + definition.Name + " sits outside the board");

                if (lanes.Count == 0 || lanes[lanes.Count - 1] != definition.Lane)
                    lanes.Add(definition.Lane);

                // The prerequisite is derived from table order, so the shape of the table is
                // the whole guarantee: contiguous lanes, gapless grades, tools first.
                byte prerequisite = PerkCatalog.PrerequisiteOf(definition.Id);
                if (definition.Grade == 1)
                {
                    TestAssert.That(prerequisite == PerkView.NoPrerequisite,
                        "grade 1 of " + definition.Lane + " hangs off another grade");
                    tools++;
                }
                else
                {
                    TestAssert.That(prerequisite == definition.Id - 1,
                        "grade " + definition.Grade + " of " + definition.Lane +
                        " does not follow the grade before it");
                    TestAssert.That(string.Equals(
                            PerkCatalog.Get(prerequisite).Lane, definition.Lane,
                            System.StringComparison.Ordinal),
                        "grade " + definition.Name + " hangs off a grade in another lane");
                }

                if (definition.IsTool)
                {
                    TestAssert.That(definition.Grade == 1,
                        "tool " + definition.Name + " is not its lane's grade 1");
                    TestAssert.That(definition.Multiplier == 1f,
                        "tool " + definition.Name + " both grants and scales");
                    TestAssert.That(capabilities.Add(definition.Capability),
                        "capability " + definition.Capability + " is granted by more than one tool");
                    continue;
                }
                TestAssert.That(definition.Multiplier != 1f,
                    "passive grade " + definition.Name + " has no effect");
            }

            TestAssert.That(PerkCatalog.All.Length <= PerkCatalog.MaximumPerks,
                "the catalogue outgrew the perk mask");
            TestAssert.That(tools == lanes.Count,
                "every lane must be entered through exactly one tool");
            TestAssert.That(lanes.Count * PerkCatalog.MaximumDepth == PerkCatalog.All.Length,
                "the board is no longer lanes x grades");
            TestAssert.That(lanes.Count > PerkCatalog.AuthorisationLimit,
                "a board with no closed lane cannot force a choice");

            // Depth is the chain length the grade order guarantees; the panel draws it.
            for (int i = 0; i < PerkCatalog.All.Length; i++)
            {
                int depth = 0;
                for (byte p = PerkCatalog.PrerequisiteOf(PerkCatalog.All[i].Id);
                     p != PerkView.NoPrerequisite && PerkCatalog.IsDefined(p);
                     p = PerkCatalog.PrerequisiteOf(p)) depth++;
                TestAssert.That(depth < PerkCatalog.MaximumDepth,
                    "grade " + PerkCatalog.All[i].Name + " is deeper than the board");
                if (PerkCatalog.All[i].Grade == PerkCatalog.MaximumDepth)
                    TestAssert.That(depth == PerkCatalog.MaximumDepth - 1,
                        "capstone " + PerkCatalog.All[i].Name + " is not at the end of its lane");
            }

            // The one guard that keeps the perk and support catalogues from drifting apart:
            // every support capability must be reachable through exactly one lane tool.
            string[] required =
            {
                SupportCapabilities.Recon, SupportCapabilities.Fortify,
                SupportCapabilities.Artillery, SupportCapabilities.Emp
            };
            for (int i = 0; i < required.Length; i++)
                TestAssert.That(capabilities.Contains(required[i]),
                    "no lane tool grants the support capability " + required[i]);
            TestAssert.That(capabilities.Count == required.Length,
                "a tool grants a capability no support action requires");
        }

        /// <summary>
        /// The rules a career lives under: grade order gates a lane, and a career may hold
        /// only its two tools. The panel renders the same <c>Block</c> values, so both are
        /// asserted through the store rather than through the view.
        /// </summary>
        private static void Lanes()
        {
            byte strikeTool = ToolOf(PerkCatalog.Strike);
            byte strikeChild = (byte)(strikeTool + 1);
            byte reconTool = ToolOf(PerkCatalog.Recon);
            byte signalsTool = ToolOf(PerkCatalog.Signals);

            var state = new PerkState();
            TestAssert.That(state.BlockOf(strikeChild, 20) == PerkView.BlockGrade,
                "a lane's grades opened before its tool");
            TestAssert.That(state.TryUnlock(strikeTool, 20), "a lane tool failed with picks to spare");
            TestAssert.That(state.Authorisations == 1, "a committed tool did not count");
            TestAssert.That(state.BlockOf(strikeChild, 20) == PerkView.BlockNone,
                "a lane's grade 2 stayed closed behind its committed tool");

            TestAssert.That(state.TryUnlock(reconTool, 20), "a second lane tool failed");
            TestAssert.That(state.Authorisations == PerkCatalog.AuthorisationLimit,
                "the career cap did not count two tools");
            TestAssert.That(state.BlockOf(signalsTool, 20) == PerkView.BlockCap,
                "a third tool was not refused by the career cap");
            TestAssert.That(!state.TryUnlock(signalsTool, 20), "a third tool was committed");
            TestAssert.That(state.BlockOf((byte)(ToolOf(PerkCatalog.Engineer) + 1), 20) == PerkView.BlockGrade,
                "a closed lane reported something other than its missing tool");

            // Two lanes stay open: the cap must not block deepening the ones already held.
            TestAssert.That(state.TryUnlock(strikeChild, 20), "a held lane could not be deepened");
            TestAssert.That(state.TryUnlock((byte)(strikeChild + 1), 20),
                "a held lane stopped at grade 2");

            var bypass = new PerkState();
            for (int i = 0; i < PerkCatalog.All.Length; i++)
                if (PerkCatalog.All[i].IsTool) bypass.ForceUnlock(PerkCatalog.All[i].Id);
            TestAssert.That(bypass.Authorisations > PerkCatalog.AuthorisationLimit,
                "the debug bypass must be able to open every lane");
        }

        private static void Points()
        {
            // One pick per grade, and grade n costs n x the interval: 500, 1000, 1500, 2000, 2500.
            TestAssert.That(PerkPoints.Earned(0, 500, 6) == 0, "an unflown mission granted a pick");
            TestAssert.That(PerkPoints.Earned(499, 500, 6) == 0, "a partial grade granted a pick");
            TestAssert.That(PerkPoints.Earned(500, 500, 6) == 1, "the first grade granted no pick");
            TestAssert.That(PerkPoints.Earned(1499, 500, 6) == 1, "the second grade came early");
            TestAssert.That(PerkPoints.Earned(1500, 500, 6) == 2, "the second grade granted no pick");
            TestAssert.That(PerkPoints.Earned(3000, 500, 6) == 3, "the third grade granted no pick");
            TestAssert.That(PerkPoints.Earned(5000, 500, 6) == 4, "the fourth grade granted no pick");
            TestAssert.That(PerkPoints.Earned(7499, 500, 6) == 4, "the capstone grade came early");
            TestAssert.That(PerkPoints.Earned(7500, 500, 6) == 5, "the capstone grade granted no pick");
            TestAssert.That(PerkPoints.Earned(100000, 500, 6) == PerkCatalog.MaximumDepth,
                "score outgrew the grade ladder");
            TestAssert.That(PerkPoints.Earned(3000, 500, 2) == 2, "the configured pick ceiling was ignored");
            TestAssert.That(PerkPoints.Earned(int.MaxValue, int.MaxValue, 20) == 1,
                "an extreme interval must not wrap into a full ladder");
            TestAssert.That(PerkPoints.Earned(-50, 500, 6) == 0, "a negative score granted picks");
            TestAssert.That(PerkPoints.Earned(500, 0, 6) == 0, "a zero interval did not fail closed");
            TestAssert.That(PerkPoints.Earned(500, 500, 0) == 0, "a zero ceiling did not fail closed");

            TestAssert.That(PerkPoints.RemainingToNext(0, 500) == 500, "the first grade's price is wrong");
            TestAssert.That(PerkPoints.RemainingToNext(499, 500) == 1, "the first grade's remainder is wrong");
            TestAssert.That(PerkPoints.RemainingToNext(500, 500) == 1000, "the second grade's price is wrong");
            TestAssert.That(PerkPoints.RemainingToNext(1500, 500) == 1500, "the third grade's price is wrong");
            TestAssert.That(PerkPoints.RemainingToNext(7500, 500) == -1, "an exhausted ladder still hints");
            TestAssert.That(PerkPoints.RemainingToNext(7500, 0) == -1, "an invalid interval still hints");
        }

        private static void PilotPoints()
        {
            TestAssert.That(PerkPoints.EarnedForPilot(70500, 70000, 500, 6, 0) == 1,
                "a successor above ushort score range must earn only its own score grade");
            TestAssert.That(PerkPoints.EarnedForPilot(70000, 70000, 500, 6, 0) == 0,
                "a new pilot inherited the retired pilot's score");
            TestAssert.That(PerkPoints.EarnedForPilot(69999, 70000, 500, 6, 2) == 2,
                "a score below the origin must retain bonus picks without earning score grades");
            TestAssert.That(PerkPoints.EarnedForPilot(100000, 0, 500, 6, 3) == 8,
                "ace bonus picks must extend past the grade ladder, not replace it");
            TestAssert.That(PerkPoints.EarnedForPilot(499, 0, 500, 6, 1) == 1,
                "a partial score grade must not be rounded up when adding an ace bonus");
            TestAssert.That(PerkPoints.EarnedForPilot(int.MaxValue, 0, 1, int.MaxValue, int.MaxValue) == 20,
                "large score and bonus inputs must not overflow or exceed the overall 20-pick ceiling");
            TestAssert.That(PerkPoints.EarnedForPilot(int.MaxValue, int.MinValue, 1, 6, 0) == 5,
                "a negative origin must be normalized before score subtraction");
            TestAssert.That(PerkPoints.EarnedForPilot(int.MinValue, int.MaxValue, 500, 6, 3) == 3,
                "extreme negative score must not wrap into score-derived picks");
            TestAssert.That(PerkPoints.EarnedForPilot(500, 0, 500, 6, int.MinValue) == 1,
                "negative bonuses must not remove legitimately earned score grades");
            TestAssert.That(PerkPoints.EarnedForPilot(500, 0, 0, 6, 2) == 2,
                "an invalid score interval must disable score awards while preserving independent bonuses");
            TestAssert.That(PerkPoints.EarnedForPilot(500, 0, 500, -1, 2) == 2,
                "an invalid pick ceiling must not invalidate independent ace bonuses");
        }

        private static void Spending()
        {
            byte tool = ToolOf(PerkCatalog.Strike);
            byte child = (byte)(tool + 1);

            var state = new PerkState();
            TestAssert.That(state.AvailablePoints(0) == 0, "an empty state started with picks");
            TestAssert.That(!state.TryUnlock(tool, 0), "a grade was bought with no picks");
            TestAssert.That(state.TryUnlock(tool, 1), "a grade could not be bought");
            TestAssert.That(state.AvailablePoints(1) == 0, "a spent pick stayed available");
            TestAssert.That(!state.TryUnlock(tool, 6), "a grade was bought twice");
            TestAssert.That(state.TryUnlock(child, 2), "a grade was not bought with picks to spare");
            TestAssert.That(state.SpentPoints == 2, "spent picks do not sum the grade costs");
            TestAssert.That(!state.TryUnlock(255, 6), "an undefined grade id was accepted");

            // The mask is a uint and the wire format packs it as one, so the whole catalogue
            // must survive a round trip through that width.
            var everything = new PerkState();
            for (byte i = 0; i < PerkCatalog.All.Length; i++) everything.ForceUnlock(i);
            var restored = new PerkState(everything.Mask);
            for (byte i = 0; i < PerkCatalog.All.Length; i++)
                TestAssert.That(restored.Has(i), "grade " + i + " was lost in the mask round trip");

            var debug = new PerkState();
            TestAssert.That(debug.ForceUnlock(tool), "bypass failed to grant a grade");
            TestAssert.That(!debug.ForceUnlock(tool), "bypass granted a duplicate grade");
        }

        /// <summary>
        /// The server is the only thing that enforces the grade order, so the store has to
        /// refuse a grade whose predecessor is not committed — and the bypass has to skip that.
        /// </summary>
        private static void Prerequisites()
        {
            byte tool = ToolOf(PerkCatalog.Strike);
            byte child = (byte)(tool + 1);

            var state = new PerkState();
            TestAssert.That(!state.TryUnlock(child, 20), "a grade unlocked with its predecessor missing");
            TestAssert.That(state.PrerequisiteMet(tool), "a lane tool must count as its own prerequisite");
            TestAssert.That(state.TryUnlock(tool, 20), "a lane tool failed with picks to spare");
            TestAssert.That(state.TryUnlock(child, 20), "a grade failed after its predecessor");

            // The chain is the lane: reaching a capstone means committing every grade before it.
            byte capstone = (byte)(tool + PerkCatalog.MaximumDepth - 1);
            var chain = new PerkState();
            TestAssert.That(!chain.TryUnlock(capstone, 20), "a capstone unlocked without its chain");
            for (byte id = tool; id < capstone; id++)
                TestAssert.That(chain.TryUnlock(id, 20), "grade " + id + " failed in its own chain");
            TestAssert.That(!chain.TryUnlock(capstone, 4), "a capstone was bought with too few picks");
            TestAssert.That(chain.TryUnlock(capstone, 5), "a capstone failed with its chain and picks");

            var bypass = new PerkState();
            TestAssert.That(bypass.ForceUnlock(child), "bypass refused a grade with no predecessor");
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
            TestAssert.That(PerkCatalog.CodeOf(PerkCatalog.Get((byte)(ToolOf(PerkCatalog.Strike) + 1))) == "PAS",
                "a passive grade must read as PAS");
            for (int i = 0; i < PerkCatalog.All.Length; i++)
            {
                if (PerkCatalog.All[i].Capability != SupportCapabilities.Emp) continue;
                TestAssert.That(PerkCatalog.CodeOf(PerkCatalog.All[i]) == "EW",
                    "the electronic warfare tool must read as EW");
            }
        }

        private static byte ToolOf(string lane)
        {
            for (int i = 0; i < PerkCatalog.All.Length; i++)
                if (PerkCatalog.All[i].IsTool &&
                    string.Equals(PerkCatalog.All[i].Lane, lane, System.StringComparison.Ordinal))
                    return PerkCatalog.All[i].Id;
            TestAssert.That(false, "the catalogue has no tool in the lane " + lane);
            return 0;
        }
    }
}
