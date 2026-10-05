using BoscaliSummer.Modules.Progression.Presentation;
using BoscaliSummer.Modules.Progression.Runtime;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Progression
{
    internal static class SqdPanelTests
    {
        public static void Run()
        {
            TestPerkClassification();
            TestBoardGeometry();
            TestEffectLabels();
            TestWingmanPresenceTracking();
            TestSquadBezelCoexistence();
        }

        /// <summary>
        /// The board shipped once with every cell 56px wide and no row height at all, which
        /// rendered as overlapping one-letter columns. Pin the arithmetic: four lanes of equal,
        /// readable, non-overlapping columns that end exactly at the sheet's right edge.
        /// </summary>
        private static void TestBoardGeometry()
        {
            const float bodyWidth = 452f;
            const float sheetWidth = bodyWidth - 14f;
            int lanes = 0;
            for (int i = 0; i < PerkCatalog.All.Length; i++)
                if (PerkCatalog.All[i].Grade == 1) lanes++;

            TestAssert.That(lanes >= 2, "the board needs at least two qualifications to compare");
            float cell = SkillBoardLayout.CellWidth(sheetWidth, lanes);
            TestAssert.That(cell >= 88f,
                "a qualification cell narrower than 88px cannot hold a name and its state");

            for (int l = 0; l < lanes; l++)
            {
                float x = SkillBoardLayout.CellX(0f, cell, l);
                TestAssert.That(x >= 0f, "lane " + l + " starts left of the sheet");
                TestAssert.That(x + cell <= sheetWidth + 0.01f,
                    "lane " + l + " runs past the sheet's right edge");
                if (l == 0) continue;
                float previous = SkillBoardLayout.CellX(0f, cell, l - 1);
                TestAssert.That(System.Math.Abs(x - (previous + cell) - SkillBoardLayout.Gap) < 0.01f,
                    "lanes " + (l - 1) + " and " + l + " are not separated by exactly one gap");
            }

            // The flow measures the board by ContentHeight, so it must reach the bottom of the last
            // node row or the tail of the tree can never be scrolled into view; and every tier row
            // must sit below the one before it with a connector gap between them.
            int tiers = PerkCatalog.MaximumDepth;
            float declared = SkillBoardLayout.ContentHeight(tiers);
            float lastBottom = SkillBoardLayout.NodeTop(tiers - 1) + SkillBoardLayout.NodeHeight;
            TestAssert.That(System.Math.Abs(declared - lastBottom) < 0.01f,
                "the board's declared height must end exactly at its last node");
            TestAssert.That(SkillBoardLayout.NodeTop(0) >= SkillBoardLayout.HeaderTop + SkillBoardLayout.LaneHeaderHeight,
                "the first tier must start below the lane headers");
            for (int t = 1; t < tiers; t++)
                TestAssert.That(System.Math.Abs(SkillBoardLayout.NodeTop(t) - SkillBoardLayout.NodeTop(t - 1) -
                                SkillBoardLayout.NodeHeight - SkillBoardLayout.NodeGap) < 0.01f,
                    "tier " + t + " is not separated from the tier above by exactly one node gap");
            TestAssert.That(SkillBoardLayout.NodeGap >= 6f,
                "the connector between two tiers needs room to be seen");
            TestAssert.That(SkillBoardLayout.NodeHeight >= 44f,
                "a node must hold its glyph and a two-line name");
        }

        /// <summary>
        /// The board prints a passive grade's effect as its own line, so every grade needs copy
        /// that fits one cell and agrees with the sentence in its description - the two used to
        /// be written independently, which is exactly how a board starts lying about a number.
        /// </summary>
        private static void TestEffectLabels()
        {
            for (int i = 0; i < PerkCatalog.All.Length; i++)
            {
                PerkDefinition definition = PerkCatalog.All[i];
                string label = PerkCatalog.EffectLabel(definition.Id);
                if (definition.IsTool)
                {
                    TestAssert.That(label.Length == 0,
                        "tool " + definition.Name + " has an effect label but grants no multiplier");
                    continue;
                }

                TestAssert.That(label.Length > 0 && label.Contains("%"),
                    "passive grade " + definition.Name + " has no effect label");
                TestAssert.That(label.Length <= 13,
                    "the effect label '" + label + "' is wider than one board cell");
                string digits = new string(System.Array.FindAll(label.ToCharArray(), char.IsDigit));
                TestAssert.That(definition.Description.Contains(digits),
                    definition.Name + " advertises " + label + " but describes '" +
                    definition.Description + "'");
            }
        }

        private static void TestPerkClassification()
        {
            for (int i = 0; i < PerkCatalog.All.Length; i++)
            {
                PerkDefinition def = PerkCatalog.All[i];
                if (def.Capability != null)
                {
                    TestAssert.That(def.Multiplier == 1f, "authorisation perk must not have a multiplier");
                    TestAssert.That(def.Cost >= 1, "authorisation perk must cost at least 1 point");
                }
                else
                {
                    TestAssert.That(def.Multiplier != 1f, "passive perk must modify multiplier");
                }
            }
        }

        private static void TestWingmanPresenceTracking()
        {
            var prevGuid = PresenceBoard.GetString(PresenceBoard.WingGuid);
            var prevIds = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);

            try
            {
                PresenceBoard.SetString(PresenceBoard.WingGuid, "com.marci.wingcommand");
                TestAssert.That(PresenceBoard.GetString(PresenceBoard.WingGuid) == "com.marci.wingcommand",
                    "wing command GUID must be readable");

                int[] testWing = new[] { 101, 202, 303 };
                PresenceBoard.SetInts(PresenceBoard.WingMemberIds, testWing);

                int[] retrieved = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);
                TestAssert.That(retrieved.Length == 3, "wing member count mismatch");
                TestAssert.That(PresenceBoard.Contains(retrieved, 202), "wingman 202 must be found");
                TestAssert.That(!PresenceBoard.Contains(retrieved, 999), "unknown unit 999 must not be found");
            }
            finally
            {
                PresenceBoard.SetString(PresenceBoard.WingGuid, prevGuid);
                PresenceBoard.SetInts(PresenceBoard.WingMemberIds, prevIds);
            }
        }

        private static void TestSquadBezelCoexistence()
        {
            string[] screens = { BezelRegistry.Wmc, MfdSlots.Sqd, MfdSlots.Ops,
                MfdSlots.Str, MfdSlots.Rad, MfdSlots.Set };
            BezelRegistry.Reset();
            try
            {
                var occupied = new System.Collections.Generic.HashSet<string>();
                foreach (string screen in screens)
                {
                    TestAssert.That(BezelRegistry.TryClaim(screen, true, 6, 6,
                        (_, index) => index >= 3, out bool left, out int slot),
                        screen + " must fit alongside the other screens in the six unused vanilla slots");
                    TestAssert.That(occupied.Add(left + ":" + slot), "SQD must never replace another bezel claim");
                }
                TestAssert.That(!BezelRegistry.TryClaim("EXTRA", true, 6, 6,
                    (_, index) => index >= 3, out _, out _), "a full bezel must reject an extra screen without eviction");
                BezelRegistry.Release(MfdSlots.Sqd);
                TestAssert.That(!BezelRegistry.IsClaimed(MfdSlots.Sqd), "SQD reset must release its reservation");
                TestAssert.That(BezelRegistry.IsClaimed(BezelRegistry.Wmc) && BezelRegistry.IsClaimed(MfdSlots.Ops),
                    "SQD reset must preserve Wing Command and OPS reservations");
            }
            finally { BezelRegistry.Reset(); }
        }
    }
}
