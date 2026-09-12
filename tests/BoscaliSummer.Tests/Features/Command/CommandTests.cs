using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Features.Command.Runtime;
using NOAvionics.Tests;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class CommandTests
    {
        public static void Run()
        {
            AvionicsProtocolTests.Run(TestAssert.That);
            TestWingScoring();
            TestMapPanelOwnership();
            MfdPanelTests.Run();
            TestLogSpace();
            AvionicsTokenTests.Run(TestAssert.That);
            AvBoxTests.Run(TestAssert.That);
            AvGridTests.Run(TestAssert.That);
            AvStyleTests.Run(TestAssert.That);

            TestAssert.That(CommandDoctrineHelper.CanSetDoctrine(0),
                "Doctrine is host-always-on; vanilla rank is not a lock");
            TestAssert.That(CommandDoctrineHelper.MaxPriorityTargets(0) == 3,
                "Priority marks are not rank-gated");
            TestAssert.That(!CommandDoctrineHelper.CanOrderSectorStrike(5),
                "Unwired sector strike stays unavailable");
            TestAssert.That(!CommandDoctrineHelper.CanOrderScramble(5),
                "Unwired scramble stays unavailable");

            CommandDoctrine[] doctrines = (CommandDoctrine[])System.Enum.GetValues(typeof(CommandDoctrine));
            TestAssert.That(doctrines.Length == 5, "Must have exactly 5 strategic doctrines");
            for (int i = 0; i < doctrines.Length; i++)
            {
                TestAssert.That(!string.IsNullOrEmpty(CommandDoctrineHelper.GetName(doctrines[i])),
                    "Doctrine name must not be empty for " + doctrines[i]);
                TestAssert.That(!string.IsNullOrEmpty(CommandDoctrineHelper.GetDescription(doctrines[i])),
                    "Doctrine description must not be empty for " + doctrines[i]);
            }

            TestTacticalSectorGrid();
            FrontlineTests.Run();
            StrPanelTests.Run();
        }

        private static void TestLogSpace()
        {
            TestAssert.That(LogSpace(LogSpace(1038f, 180f), 364f) == 128f,
                "The visible MFD frame must shrink the log even when its content starts lower");
            TestAssert.That(LogSpace(1064f, -564f + 596f) == 460f,
                "A bottom-aligned 596px display leaves room for the kill log on a 1080px canvas");
            TestAssert.That(LogSpace(1000f, 400f) == 92f,
                "A tall panel leaves only the actual gap above its rendered top");
            TestAssert.That(LogSpace(1000f, 550f) == 0f,
                "A panel extending above the bay must suppress the log");
            TestAssert.That(LogSpace(LogSpace(1000f, 100f), 400f) == 92f,
                "Every open panel reserves space, not just the first screen");
            TestAssert.That(LogSpace(LogSpace(1000f, 400f), 100f) == 92f,
                "Screen enumeration order must not affect the available space");
            TestAssert.That(BoscaliSummer.Features.Command.Presentation.MapUi.MfdLogSpace.Remaining(
                1000f, 0f, 500f, 440f, 450f, 900f, -500f, 400f, 8f) == 1000f,
                "A screen outside the log column must not consume its space");
        }

        private static float LogSpace(float height, float top) =>
            BoscaliSummer.Features.Command.Presentation.MapUi.MfdLogSpace.Remaining(
                height, 0f, 500f, 440f, 0f, 440f, -500f, top, 8f);

        private static void TestMapPanelOwnership()
        {
            foreach (string name in new[] { "WMC", "OPS", "RAD", "SET", "SQD", "", null })
                TestAssert.That(BoscaliSummer.Features.Command.Presentation.MapUi.VanillaMfdPanelCatalog.FromShortName(name) ==
                    BoscaliSummer.Features.Command.Presentation.MapUi.VanillaMfdPanelId.Unknown,
                    "The vanilla UI adapter must not rebuild a companion mod's page");
            foreach (string name in new[] { "BDF", "MAP", "HUD", "PALA", "TGT", "MIS" })
                TestAssert.That(BoscaliSummer.Features.Command.Presentation.MapUi.VanillaMfdPanelCatalog.FromShortName(name) !=
                    BoscaliSummer.Features.Command.Presentation.MapUi.VanillaMfdPanelId.Unknown,
                    "All vanilla bezel pages must have an adapter");
        }

        private static void TestWingScoring()
        {
            var previous = NOAvionics.PresenceBoard.GetInts(NOAvionics.PresenceBoard.WingMemberIds);
            try
            {
                NOAvionics.PresenceBoard.SetInts(NOAvionics.PresenceBoard.WingMemberIds, new[] { 42 });
                int[] wing = NOAvionics.PresenceBoard.GetInts(NOAvionics.PresenceBoard.WingMemberIds);
                for (int doctrine = 0; doctrine <= 4; doctrine++)
                {
                    TestAssert.That(CommandScoring.Bias(true, NOAvionics.PresenceBoard.Contains(wing, 42), NOAvionics.PresenceBoard.Contains(wing, 99), doctrine, true, true, false, true) == 1f,
                        "Boscali doctrine must not alter a recruited wingman's target scoring");
                    TestAssert.That(CommandScoring.Bias(false, NOAvionics.PresenceBoard.Contains(wing, 10), NOAvionics.PresenceBoard.Contains(wing, 99), doctrine, true, true, false, true) == 1f,
                        "Enemy analyzers must remain unaffected");
                }
                TestAssert.That(CommandScoring.Bias(true, NOAvionics.PresenceBoard.Contains(wing, 10), NOAvionics.PresenceBoard.Contains(wing, 99), 1, false, true, false, false) > 1f,
                    "Friendly mission AI must still receive doctrine with Wing Command present");
                NOAvionics.PresenceBoard.SetInts(NOAvionics.PresenceBoard.WingMemberIds, null);
                int[] noWing = NOAvionics.PresenceBoard.GetInts(NOAvionics.PresenceBoard.WingMemberIds);
                TestAssert.That(CommandScoring.Bias(true, NOAvionics.PresenceBoard.Contains(noWing, 42), NOAvionics.PresenceBoard.Contains(noWing, 99), 1, false, true, false, false) > 1f,
                    "Doctrine must work without Wing Command or after wing membership is cleared");
            }
            finally { NOAvionics.PresenceBoard.SetInts(NOAvionics.PresenceBoard.WingMemberIds, previous); }
        }

        private static void TestTacticalSectorGrid()
        {
            // 1. Test square and non-square world initialization
            TacticalSectorGrid squareGrid = new TacticalSectorGrid(32, 100000f, 100000f);
            TestAssert.That(squareGrid.Resolution == 32, "Grid resolution must be 32");
            TestAssert.That(squareGrid.TotalSectors == 1024, "Total sectors for square map must be 1024");

            TacticalSectorGrid grid = new TacticalSectorGrid(32, 120000f, 80000f);
            TestAssert.That(grid.ResolutionX == 32, "Grid ResolutionX must be 32");
            TestAssert.That(grid.ResolutionY == 21, "Grid ResolutionY for 3:2 map must be 21");
            TestAssert.That(grid.TotalSectors == 32 * 21, "Total sectors must match ResolutionX * ResolutionY");
            TestAssert.That(System.Math.Abs(grid.WorldSizeX - 120000f) < 0.01f, "WorldSizeX must match 120000");
            TestAssert.That(System.Math.Abs(grid.WorldSizeY - 80000f) < 0.01f, "WorldSizeY must match 80000");

            // World to Cell mapping across coordinate extremes on non-square map
            TestAssert.That(grid.WorldToCell(0f, 0f, out int midC, out int midR), "Center must map within bounds");
            TestAssert.That(midC == 16 && midR == 10, "Center must map to sector (16, 10)");

            TestAssert.That(grid.WorldToCell(-59999f, -39999f, out int minC, out int minR), "Min bounds must map");
            TestAssert.That(minC == 0 && minR == 0, "Min bounds must map to (0, 0)");

            TestAssert.That(grid.WorldToCell(59999f, 39999f, out int maxC, out int maxR), "Max bounds must map");
            TestAssert.That(maxC == 31 && maxR == 20, "Max bounds must map to (31, 20)");

            // 2. Troop presence & sector evaluation
            grid.ResetAll();
            TestAssert.That(grid.FriendlySectorCount == 0 && grid.HostileSectorCount == 0, "Grid starts empty");

            // Add friendly troops at (0, 0)
            grid.AddTroopPresence(0f, 0f, 1.5f, false, 0f);
            // Add hostile troops at (20000, 0)
            grid.AddTroopPresence(20000f, 0f, 1.5f, true, 0f);

            grid.EvaluateSectors();
            TestAssert.That(grid.FriendlySectorCount == 1, "Friendly troop creates 1 friendly held sector");
            TestAssert.That(grid.HostileSectorCount == 1, "Hostile troop creates 1 hostile held sector");
            TestAssert.That(grid.GetSectorControl(16, 10) == SectorControl.Friendly, "Center sector is Friendly");

            // Add hostile troop to same sector (16, 10) -> should become Contested!
            grid.AddTroopPresence(0f, 0f, 2.0f, true, 0f);
            grid.EvaluateSectors();
            TestAssert.That(grid.GetSectorControl(16, 10) == SectorControl.Contested, "Contested battle sector when both troops present");
            TestAssert.That(grid.ContestedSectorCount == 1, "Must report 1 contested sector");

            // 3. Airbase strategic anchor & Wavefront growth
            grid.ResetAll();
            grid.AddAirbasePresence(0f, 0f, false);
            grid.EvaluateSectors();
            TestAssert.That(grid.FriendlySectorCount >= 9, "Airbase core and perimeter sectors must be secured");
            TestAssert.That(grid.GetSectorControl(16, 10) == SectorControl.Friendly, "Airbase core is Friendly");

            // 4. Opposing bases, frontline edge detection, and RWR 66% rule
            grid.ResetAll();
            grid.AddAirbasePresence(-15000f, 0f, false, 32000f);
            grid.AddAirbasePresence(15000f, 0f, true, 32000f);
            grid.EvaluateSectors();

            TestAssert.That(grid.FriendlySectorCount > 0, "Allied sectors exist");
            TestAssert.That(grid.HostileSectorCount > 0, "Hostile sectors exist");
            TestAssert.That(grid.TotalNodesCount == 2, "2 strategic nodes registered");
            TestAssert.That(grid.FriendlySectorCount + grid.HostileSectorCount == grid.TotalSectors,
                "Opposing strategic influence divides the theater without gaps");

            // 5. Test 66% Force Superiority Rule & Contested Clashes
            // Friendly armor pushes into the hostile side of the midpoint.
            grid.AddTroopPresence(0f, 0f, 1.0f, true);
            grid.AddTroopPresence(0f, 0f, 4.0f, false);
            grid.EvaluateSectors();

            TestAssert.That(grid.ContestedSectorCount >= 1, "Clash detected at contested contact point");
            TestAssert.That(grid.ActiveClashesCount >= 1, "Clash count reports active battle");

            // 6. Texture baking (RWR tactical grid + frontline borders)
            var pixels = grid.BakeTexture(128, 128, true, true, 0.35f);
            TestAssert.That(pixels != null && pixels.Length == 128 * 128, "BakeTexture generates valid pixel array");

            int drawnPixels = 0;
            int frontlineBorderPixels = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].a > 0)
                {
                    drawnPixels++;
                    if (pixels[i].a >= 100)
                    {
                        frontlineBorderPixels++;
                    }
                }
            }
            TestAssert.That(drawnPixels > 50, "Texture baking produces rendered territory pixels");
            TestAssert.That(frontlineBorderPixels > 0, "Discrete frontline boundary edges rendered");

            // 7. Test Neutral Node Non-Expansion & Queue Overflow Immunity
            grid.ResetAll();
            // Register 8 neutral airbases (like in custom made missions)
            for (int i = 0; i < 8; i++)
            {
                grid.RegisterNode(100 + i, "NeutralAirbase_" + i, -20000f + (i * 5000f), 0f, SectorControl.Neutral, 0f, true);
            }
            // Add opposing active strategic nodes
            grid.RegisterNode(201, "FriendlyFOB", -30000f, 0f, SectorControl.Friendly, 0f, false);
            grid.RegisterNode(202, "HostileDepot", 30000f, 0f, SectorControl.Hostile, 0f, false);
            // Must NOT throw IndexOutOfRangeException!
            grid.EvaluateSectors();
            TestAssert.That(grid.FriendlySectorCount > 0, "Friendly wavefront expands around neutral bases");
            TestAssert.That(grid.HostileSectorCount > 0, "Hostile wavefront expands around neutral bases");
            TestAssert.That(grid.FriendlySectorCount + grid.HostileSectorCount + grid.ContestedSectorCount + grid.NeutralSectorCount == grid.TotalSectors,
                "All sectors accounted for with neutral nodes present");

            // 8. Test Made Mission with Zero Airbases (only depots and objectives)
            grid.ResetAll();
            grid.RegisterNode(301, "Allied_Spawn_Point", -25000f, 10000f, SectorControl.Friendly, 0f, false);
            grid.RegisterNode(302, "Hostile_Vehicle_Depot", 25000f, -10000f, SectorControl.Hostile, 0f, false);
            grid.EvaluateSectors();
            TestAssert.That(grid.FriendlySectorCount > 0, "Allied territory forms from synthesized depot node");
            TestAssert.That(grid.HostileSectorCount > 0, "Hostile territory forms from synthesized depot node");
            TestAssert.That(grid.FriendlySectorCount + grid.HostileSectorCount == grid.TotalSectors,
                "Strategic influence covers the theater without airbases");

            // 9. Test Ground Force Seeding Fallback (zero nodes registered at all)
            grid.ResetAll();
            grid.AddTroopPresence(-20000f, 0f, 3.0f, false);
            grid.AddTroopPresence(20000f, 0f, 3.0f, true);
            grid.EvaluateSectors();
            TestAssert.That(grid.FriendlySectorCount > 0, "Fallback ground forces seed friendly wavefront");
            TestAssert.That(grid.HostileSectorCount > 0, "Fallback ground forces seed hostile wavefront");
        }
    }
}
