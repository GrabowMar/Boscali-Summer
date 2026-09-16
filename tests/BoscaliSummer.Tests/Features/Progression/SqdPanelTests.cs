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
            TestWingmanPresenceTracking();
            TestSquadBezelCoexistence();
            TestDossierStylesheet();
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
