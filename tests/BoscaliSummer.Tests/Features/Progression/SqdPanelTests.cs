using BoscaliSummer.Features.Progression.Presentation;
using BoscaliSummer.Features.Progression.Runtime;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Runtime;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Progression
{
    internal static class ProgressionPresentationTests
    {
        public static void Run()
        {
            TestPerkClassification();
            TestBoardGeometry();
            TestEffectLabels();
            TestWingmanPresenceTracking();
            TestSquadBezelCoexistence();
            TestDossierStylesheet();
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

            float roomy = SkillBoardLayout.RowHeight(604f, PerkCatalog.MaximumDepth);
            TestAssert.That(roomy >= SkillBoardLayout.MinCellHeight &&
                            roomy <= SkillBoardLayout.MaxCellHeight,
                "a tall sheet must keep the grade rows inside the readable band");
            float tight = SkillBoardLayout.RowHeight(100f, PerkCatalog.MaximumDepth);
            TestAssert.That(tight == SkillBoardLayout.MinCellHeight,
                "a short sheet must keep the grade rows at the readable minimum");

            // The scroll content must declare everything the board draws at the minimum row
            // height - masthead, clause heading, rail key, lane header and rows - or the parts
            // below the declared height can never be scrolled into view.
            float declared = SkillBoardLayout.ContentHeight(
                SkillBoardLayout.MinCellHeight, PerkCatalog.MaximumDepth);
            float drawn = SkillBoardLayout.FileHeaderHeight + SkillBoardLayout.TitleHeight +
                SkillBoardLayout.LegendHeight + SkillBoardLayout.LaneHeaderHeight + SkillBoardLayout.Gap +
                PerkCatalog.MaximumDepth * SkillBoardLayout.MinCellHeight;
            TestAssert.That(declared >= drawn - 0.01f,
                "the scroll content height must cover every band the board draws");
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

        /// <summary>
        /// The dossier look is data in the shipped sheet, and a typo there only logs a
        /// warning at runtime. Parse it here so a broken class is a red test instead.
        /// </summary>
        private static void TestDossierStylesheet()
        {
            string text;
            using (System.IO.Stream stream = typeof(ProgressionPresentationTests).Assembly
                .GetManifestResourceStream("BoscaliSummer.Tests.avionics.avss"))
            {
                TestAssert.That(stream != null, "the shipped avionics sheet must be embedded for this check");
                using (var reader = new System.IO.StreamReader(stream)) text = reader.ReadToEnd();
            }

            AvStyleSheet sheet = AvStyleSheet.Parse(text);
            for (int i = 0; i < sheet.Errors.Count; i++)
                TestAssert.That(false, "avionics.avss " + sheet.Errors[i]);

            TestAssert.That(sheet.Resolve("stamp ok").Color.HasValue, "the dossier stamp must colour by state");
            TestAssert.That(sheet.Resolve("stamp bad").Background.HasValue, "a red stamp must carry its own wash");
            TestAssert.That(sheet.Resolve("form-key").HasFont, "dossier field keys must carry a type size");
            TestAssert.That(sheet.Resolve("leader").Tracking > 0f, "the dotted leader needs tracking to read as a rule");
            TestAssert.That(sheet.Resolve("punch").Background.HasValue, "a punched hole needs a fill");
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
