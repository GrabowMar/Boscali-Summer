using System;
using BoscaliSummer.Features.Command.Presentation.MapUi;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class MfdNewsTickerTests
    {
        public static void Run()
        {
            TestLarpHeadlinesPool();
            TestAirbaseCaptureParsing();
            TestNuclearEventParsing();
            TestAceDefeatParsing();
            TestIndividualTrafficStaysOffTheWire();
            TestMajorUnitKillParsing();
            TestInterceptionParsing();
            TestPilotRescueAndCaptureParsing();
            TestWarheadDefenseParsing();
            TestLogisticsThrottle();
            TestMarqueeFormattingAndLoop();
            TestTheaterStatusGeneration();
            TestCleanTagsAndDedup();
        }

        private static void TestLarpHeadlinesPool()
        {
            TestAssert.That(MfdNewsFeed.LarpHeadlines.Length >= 40, "LARP headline pool must contain at least 40 items");

            for (int i = 0; i < MfdNewsFeed.LarpHeadlines.Length; i++)
            {
                string item = MfdNewsFeed.LarpHeadlines[i];
                TestAssert.That(!string.IsNullOrWhiteSpace(item), $"Headline at index {i} must not be empty");
                TestAssert.That(item.Length >= 20, $"Headline at index {i} should be descriptive");
            }
        }

        private static void TestAirbaseCaptureParsing()
        {
            string line = "Airbase Nova has been captured by Boscali Defense Force";
            var parsed = MfdNewsFeed.ParseEvent(line);
            TestAssert.That(parsed != null, "Must parse airbase capture");
            TestAssert.That(parsed.Tag == "FLASH", "Capture tag must be FLASH");
            TestAssert.That(parsed.IsUrgent, "Airbase capture must be urgent");
            TestAssert.That(parsed.Text.Contains("AIRBASE NOVA"), "Must include captured base name");
            TestAssert.That(parsed.Text.Contains("BOSCALI DEFENSE FORCE"), "Must include capturing faction");
        }

        private static void TestNuclearEventParsing()
        {
            string line = "Nuclear weapon launched, exclusion zone active!";
            var parsed = MfdNewsFeed.ParseEvent(line);
            TestAssert.That(parsed != null, "Must parse nuclear weapon event");
            TestAssert.That(parsed.Tag == "CRITICAL ALERT", "Nuclear tag must be CRITICAL ALERT");
            TestAssert.That(parsed.IsUrgent, "Nuclear detonation must be urgent");
            TestAssert.That(parsed.Text.Contains("STRATEGIC WEAPON"), "Must reference strategic weapon");
        }

        private static void TestAceDefeatParsing()
        {
            string line = "Red Wing ace defeated.";
            var parsed = MfdNewsFeed.ParseEvent(line);
            TestAssert.That(parsed != null, "Must parse ace defeat event");
            TestAssert.That(parsed.Tag == "AIR SUPREMACY", "Ace tag must be AIR SUPREMACY");
            TestAssert.That(parsed.IsUrgent, "Ace defeat must be urgent");
            TestAssert.That(parsed.Text.Contains("HOSTILE ACE PILOT"), "Must reference ace pilot");
        }

        private static void TestIndividualTrafficStaysOffTheWire()
        {
            TestAssert.That(MfdNewsFeed.ParseEvent("Linebreaker TFX destroyed MIG-47 Anvil") == null,
                "Routine vehicle kills belong in the tactical log");
            TestAssert.That(MfdNewsFeed.ParseEvent("FGA-57 Anvil shot down MIG-47 Foxbat") == null,
                "Individual air kills belong in the tactical log");
            TestAssert.That(MfdNewsFeed.ParseEvent("FGA-57 Anvil intercepted AGM-48") == null,
                "Routine interceptions belong in the tactical log");
            TestAssert.That(MfdNewsFeed.ParseEvent("MIG-47 Foxbat crashed") == null,
                "Crash sites belong in the tactical log");
            TestAssert.That(MfdNewsFeed.ParseEvent("Linebreaker TFX sank Patrol Boat") == null,
                "Non-capital vessel losses belong in the tactical log");
            TestAssert.That(MfdNewsFeed.ParseEvent("Linebreaker SAM was repaired") == null,
                "Repairs never reach the wire");

            var feed = new MfdNewsFeed();
            for (int i = 0; i < 3; i++)
            {
                feed.IngestGameEvent($"FGA-57 Anvil shot down MIG-{40 + i}", 10f + i * 10f);
            }
            feed.IngestGameEvent("FGA-57 Anvil intercepted AGM-61", 40f);
            feed.IngestGameEvent("SAH-46 Chicane destroyed Type-12 MBT 4", 40f);

            string text = feed.BuildMarqueeText(6);
            TestAssert.That(!text.Contains("SHOT DOWN"), "Kill feed must not reach the marquee");
            TestAssert.That(!text.Contains("ACE WATCH"), "Kill streaks must not reach the marquee");
            TestAssert.That(!text.Contains("OPENING SHOTS"), "First blood must not reach the marquee");
            TestAssert.That(!text.Contains("ARMOR"), "Armor tallies must not reach the marquee");
            TestAssert.That(!text.Contains("SESSION TALLY"), "Casualty tallies must not reach the marquee");
        }

        private static void TestMajorUnitKillParsing()
        {
            string line = "Linebreaker TFX destroyed Shard Class Corvette";
            var parsed = MfdNewsFeed.ParseEvent(line);
            TestAssert.That(parsed != null, "Must parse major unit kill");
            TestAssert.That(parsed.Tag == "CRITICAL KILL", "Capital unit kill must be CRITICAL KILL");
            TestAssert.That(parsed.IsUrgent, "Capital unit kill must be marked urgent");
            TestAssert.That(parsed.Text.Contains("SHARD CLASS CORVETTE"), "Must include ship name");

            var sunk = MfdNewsFeed.ParseEvent("Linebreaker TFX sank Shard Class Cruiser");
            TestAssert.That(sunk != null, "Must parse capital ship loss");
            TestAssert.That(sunk.Tag == "CRITICAL KILL", "Capital ship loss must be CRITICAL KILL");
            TestAssert.That(sunk.IsUrgent, "Capital ship loss must be urgent");

            var strategic = MfdNewsFeed.ParseEvent("Linebreaker TFX demolished Radar Station");
            TestAssert.That(strategic != null, "Strategic demolition must reach the wire");
            TestAssert.That(strategic.Tag == "DEMOLITION", "Strategic demolition tag must be DEMOLITION");
            TestAssert.That(strategic.IsUrgent, "Strategic demolition must interrupt the wire");
        }

        private static void TestInterceptionParsing()
        {
            TestAssert.That(MfdNewsFeed.ParseEvent("Shard Class Corvette intercepted AGM-48") == null,
                "Routine interceptions must stay in the tactical log");

            var parsed = MfdNewsFeed.ParseEvent("Shard Class Corvette intercepted Nuclear Warhead");
            TestAssert.That(parsed != null, "Must parse nuclear interception");
            TestAssert.That(parsed.Tag == "CRITICAL ALERT", "Nuclear interception tag must be CRITICAL ALERT");
            TestAssert.That(parsed.IsUrgent, "Nuclear interception must be urgent");
            TestAssert.That(parsed.Text.Contains("NUCLEAR WARHEAD"), "Must include intercepted ordnance");
        }

        private static void TestPilotRescueAndCaptureParsing()
        {
            var rescued = MfdNewsFeed.ParseEvent("Lt. Voss was rescued by Rescue-1");
            TestAssert.That(rescued != null, "Must parse pilot rescue");
            TestAssert.That(rescued.Tag == "CSAR", "Rescue tag must be CSAR");
            TestAssert.That(rescued.IsUrgent, "Pilot rescue must be urgent");
            TestAssert.That(rescued.Text.Contains("RESCUE-1"), "Must credit the rescuer");

            var captured = MfdNewsFeed.ParseEvent("Lt. Voss was captured by Prime Militia");
            TestAssert.That(captured != null, "Must parse pilot capture");
            TestAssert.That(captured.Tag == "POW", "Capture tag must be POW");
            TestAssert.That(captured.IsUrgent, "Pilot capture must be urgent");
            TestAssert.That(captured.Text.Contains("PRIME MILITIA"), "Must credit the captor");
        }

        private static void TestWarheadDefenseParsing()
        {
            var parsed = MfdNewsFeed.ParseEvent("Warning : 2 warheads destroyed at North Coast Enrichment Plant");
            TestAssert.That(parsed != null, "Must parse warhead defense message");
            TestAssert.That(parsed.Tag == "BASE DEFENSE", "Warhead defense tag must be BASE DEFENSE");
            TestAssert.That(parsed.IsUrgent, "Warhead defense must be urgent");
            TestAssert.That(parsed.Text.Contains("NORTH COAST ENRICHMENT PLANT"), "Must name the defended base");
        }

        private static void TestLogisticsThrottle()
        {
            var feed = new MfdNewsFeed();
            bool first = feed.IngestGameEvent("Captain Hall donated 2 Munitions to the war effort", 10f);
            TestAssert.That(first, "First logistics message should reach the wire");

            bool throttled = feed.IngestGameEvent("Major Reyes donated 1 Munition to the war effort", 20f);
            TestAssert.That(!throttled, "Second logistics message inside the window must be dropped");

            bool later = feed.IngestGameEvent("Major Reyes donated 1 Munition to the war effort", 120f);
            TestAssert.That(later, "Logistics message after the window should reach the wire");
            TestAssert.That(feed.BuildMarqueeText(6).Contains("CAPTAIN HALL"), "Must credit the donating player");
        }

        private static void TestMarqueeFormattingAndLoop()
        {
            var feed = new MfdNewsFeed(seed: 123);
            string text = feed.BuildMarqueeText(6);

            TestAssert.That(!string.IsNullOrEmpty(text), "Marquee text must not be empty");
            TestAssert.That(text.Contains("+++"), "Marquee must include separator");
            TestAssert.That(text.Contains("<color="), "Marquee must include rich text color tags");

            feed.Enqueue(new MfdNewsFeed.HeadlineItem("SITREP", "#66CCFF", "FRONT STATUS UNCHANGED"));
            feed.Enqueue(new MfdNewsFeed.HeadlineItem("SITREP", "#66CCFF", "FRONT STATUS UNCHANGED"));
            text = feed.BuildMarqueeText(6);
            TestAssert.That(text.IndexOf("FRONT STATUS UNCHANGED", StringComparison.Ordinal) ==
                text.LastIndexOf("FRONT STATUS UNCHANGED", StringComparison.Ordinal),
                "Repeated status dispatches must not stack in the wire");

            feed.OnMarqueeCycleComplete();
            string nextText = feed.BuildMarqueeText(6);
            TestAssert.That(!string.IsNullOrEmpty(nextText), "Next cycle marquee text must not be empty");
        }

        private static void TestTheaterStatusGeneration()
        {
            var feed = new MfdNewsFeed();
            feed.UpdateTheaterStatus(
                now: 100f,
                defcon: 1,
                territoryRatio: 0.5f,
                airSuperiorityRatio: 0.5f,
                activeClashes: 2,
                contestedAirbases: 0);

            string text = feed.BuildMarqueeText();
            TestAssert.That(text.Contains("DEFCON ALERT"), "Must report a defcon alert");
            TestAssert.That(!text.Contains("DEFCON 1"), "Defcon numbers must stay off the wire");

            feed.UpdateTheaterStatus(
                now: 200f,
                defcon: 3,
                territoryRatio: 0.75f,
                airSuperiorityRatio: 0.7f,
                activeClashes: 1,
                contestedAirbases: 0);

            text = feed.BuildMarqueeText();
            TestAssert.That(text.Contains("PRESSING THE ADVANTAGE") || text.Contains("FRONT ADVANCE"),
                "Must report territorial advantage");
            TestAssert.That(!text.Contains("% THEATER CONTROL") && !text.Contains("EFFECTIVENESS"),
                "Percentages and unit tallies must stay off the wire");
            TestAssert.That(!text.Contains("SPREAD ACROSS"), "Sector counts must stay off the wire");
        }

        private static void TestCleanTagsAndDedup()
        {
            var feed = new MfdNewsFeed();
            bool ingested1 = feed.IngestGameEvent("<color=#FF0000>Airbase Alpha has been captured by Prime</color>");
            TestAssert.That(ingested1, "Should ingest event with HTML color tags");

            // Ingesting duplicate should be rejected
            bool ingested2 = feed.IngestGameEvent("Airbase Alpha has been captured by Prime");
            TestAssert.That(!ingested2, "Duplicate event within history window must be ignored");
        }
    }
}
