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
            grid.Clear();
            TestAssert.That(grid.FriendlySectorCount == 0 && grid.HostileSectorCount == 0, "Grid starts empty");

            // Add friendly troops at (0, 0)
            grid.AddTroopPresence(0f, 0f, 1.5f, false);
            // Add hostile troops at (20000, 0)
            grid.AddTroopPresence(20000f, 0f, 1.5f, true);

            grid.EvaluateSectors();
            TestAssert.That(grid.FriendlySectorCount == 1, "Friendly troop creates 1 friendly held sector");
            TestAssert.That(grid.HostileSectorCount == 1, "Hostile troop creates 1 hostile held sector");
            TestAssert.That(grid.GetSectorControl(16, 10) == SectorControl.Friendly, "Center sector is Friendly");

            // Add hostile troop to same sector (16, 10) -> should become Contested!
            grid.AddTroopPresence(0f, 0f, 2.0f, true);
            grid.EvaluateSectors();
            TestAssert.That(grid.GetSectorControl(16, 10) == SectorControl.Contested, "Contested battle sector when both troops present");
            TestAssert.That(grid.ContestedSectorCount == 1, "Must report 1 contested sector");

            // 3. Airbase strategic anchor & Wavefront growth
            grid.Clear();
            grid.AddAirbasePresence(0f, 0f, false);
            grid.EvaluateSectors();
            TestAssert.That(grid.FriendlySectorCount >= 9, "Airbase core and perimeter sectors must be secured");
            TestAssert.That(grid.GetSectorControl(16, 10) == SectorControl.Friendly, "Airbase core is Friendly");

            // 4. Opposing bases, frontline edge detection, and RWR 66% rule
            grid.Clear();
            grid.AddAirbasePresence(-15000f, 0f, false, 32000f);
            grid.AddAirbasePresence(15000f, 0f, true, 32000f);
            grid.EvaluateSectors();

            TestAssert.That(grid.FriendlySectorCount > 0, "Allied sectors exist");
            TestAssert.That(grid.HostileSectorCount > 0, "Hostile sectors exist");
            TestAssert.That(grid.TotalNodesCount == 2, "2 strategic nodes registered");
            TestAssert.That(grid.FriendlySectorCount + grid.HostileSectorCount == grid.TotalSectors,
                "Wavefront expands across the entire theater until meeting opposing automata; total claimed sectors equals TotalSectors");

            // 5. Test 66% Force Superiority Rule & Contested Clashes
            // Spawn hostile armor in neutral sector (mid-point)
            grid.AddTroopPresence(0f, 0f, 4.0f, true); // Hostile heavy armor
            grid.AddTroopPresence(0f, 0f, 1.0f, false); // Friendly light probe
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
            grid.Clear();
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
            grid.Clear();
            grid.RegisterNode(301, "Allied_Spawn_Point", -25000f, 10000f, SectorControl.Friendly, 0f, false);
            grid.RegisterNode(302, "Hostile_Vehicle_Depot", 25000f, -10000f, SectorControl.Hostile, 0f, false);
            grid.EvaluateSectors();
            TestAssert.That(grid.FriendlySectorCount > 0, "Allied territory forms from synthesized depot node");
            TestAssert.That(grid.HostileSectorCount > 0, "Hostile territory forms from synthesized depot node");
            TestAssert.That(grid.FriendlySectorCount + grid.HostileSectorCount == grid.TotalSectors,
                "Full wavefront frontline collision achieved without any airbases");

            // 9. Test Ground Force Seeding Fallback (zero nodes registered at all)
            grid.Clear();
            grid.AddTroopPresence(-20000f, 0f, 3.0f, false);
            grid.AddTroopPresence(20000f, 0f, 3.0f, true);
            grid.EvaluateSectors();
            TestAssert.That(grid.FriendlySectorCount > 0, "Fallback ground forces seed friendly wavefront");
            TestAssert.That(grid.HostileSectorCount > 0, "Fallback ground forces seed hostile wavefront");
        }
    }
}
