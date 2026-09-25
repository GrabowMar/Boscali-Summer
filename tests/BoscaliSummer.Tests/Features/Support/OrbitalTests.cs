using System;
using BoscaliSummer.Features.Support.Runtime;
using BoscaliSummer.Features.Support.Domain.Orbital;

namespace BoscaliSummer.Tests.Features.Support
{
    internal static class OrbitalTests
    {
        public static void Run()
        {
            TestStationKeeping();
            TestOrbitMath();
            TestBands();
            TestPassGeometry();
            TestCatalogue();
            TestPlacement();
            TestMissionFitting();
            TestJettison();
            TestPower();
            TestDockingAndCargo();
            TestWeaponUpgrades();
            TestMtiSharesImagerTasking();
            TestAbilities();
            TestManoeuvres();
            TestSafeMode();
            TestDebris();
            TestSnapshot();
            TestForeign();
            TestWords();
            TestSarProjection();
            TestSarFormation();
        }

        /// <summary>
        /// MTI rides the radar tasking SAR already spends: one window, one spend, one recharge.
        /// The mapping lives in the manager (Unity-bound); the pure suite pins the wire id and the
        /// single-window platform semantics both modes share.
        /// </summary>
        private static void TestMtiSharesImagerTasking()
        {
            TestAssert.That((byte)SupportActionId.MtiSweep == 24, "MTI keeps wire id 24");
            var seen = new System.Collections.Generic.HashSet<byte>();
            foreach (SupportActionId id in (SupportActionId[])Enum.GetValues(typeof(SupportActionId)))
                TestAssert.That(seen.Add((byte)id), "wire id " + (byte)id + " must be unique");

            OrbitalPlatform platform = Station(out double now);
            Add(platform, ModuleKind.Imager, 6, ref now);
            Add(platform, ModuleKind.Solar, 5, ref now);
            now = FirstPassTime(platform, now);
            for (int i = 0; i < 60; i++) platform.Tick(now, 5f, true);
            TestAssert.That(platform.Check(PlatformAbility.RadarScan, now) == PlatformDenial.None,
                "a charged imager offers its radar window");
            float energy = platform.Energy;
            platform.Consume(PlatformAbility.RadarScan, now);
            TestAssert.That(platform.Check(PlatformAbility.RadarScan, now) == PlatformDenial.Recharging,
                "one radar task — SAR or MTI — closes the window for both");
            TestAssert.That(Near(platform.Energy, energy - PlatformAbilities.Info(PlatformAbility.RadarScan).EnergyKj, 0.01),
                "the shared tasking spends its energy once");
        }

        private static void TestStationKeeping()
        {
            OrbitalPlatform station = Station(out double now);
            OrbitState before = station.State(now);
            OrbitState hoursLater = station.State(now + 86400);
            TestAssert.That(before.InPass && hoursLater.InPass && before.SubX == hoursLater.SubX && before.SubZ == hoursLater.SubZ,
                "a station holds its position indefinitely");
            TestAssert.That(station.PositionIndex == StationKeeping.Centre, "initial station is at theatre centre");
            TestAssert.That(!station.TryRelocate(0, now), "movement requires a propulsion module");
            Add(station, ModuleKind.Propulsion, 6, ref now);
            float fuel = station.Fuel;
            TestAssert.That(!station.TryRelocate(9, now) && !station.TryRelocate(-1, now) &&
                !station.TryRelocate(StationKeeping.Centre, now) && station.Fuel == fuel,
                "invalid and current destinations never spend fuel");
            TestAssert.That(station.TryRelocate(0, now) && station.Fuel == fuel - 25,
                "a valid destination spends fuel once");
            TestAssert.That(!station.TryRelocate(8, now), "an active relocation cannot be replaced");
            var snapshot = new PlatformSnapshot();
            station.Export(now + 5, snapshot);
            var remote = new OrbitalPlatform();
            remote.Mirror(snapshot, 900);
            OrbitState moving = station.State(now + 5), mirrored = remote.State(900);
            TestAssert.That(Near(moving.SubX, mirrored.SubX, 0.01) && Near(moving.SubZ, mirrored.SubZ, 0.01),
                "a late join mirrors the relocation trajectory with rebased time");
            OrbitState arrived = station.State(now + 10);
            TestAssert.That(arrived.InPass && arrived.SubX == StationKeeping.X(0) && arrived.SubZ == StationKeeping.Z(0),
                "arrival holds the chosen sector");
            TestAssert.That(station.CheckRelocate(8, now + 10) == PlatformDenial.Recharging,
                "propulsion recharge survives arrival");
            TestAssert.That(station.TryRelocate(8, now + 31), "a later move can be selected explicitly");
            int known = remote.Seed;
            snapshot.Seed = 81;
            remote.Mirror(snapshot, 901);
            TestAssert.That(remote.Seed == known, "a malformed sector route cannot move the client station");
        }

        private static bool Near(double value, double expected, double tolerance) =>
            Math.Abs(value - expected) <= tolerance;

        private static void TestMissionFitting()
        {
            for (int missionIndex = 0; missionIndex < PlatformMissions.Count; missionIndex++)
            {
                PlatformMission mission = (PlatformMission)missionIndex;
                var station = new OrbitalPlatform();
                double now = 0.0;
                PlatformFitStep step = PlatformMissions.Next(station, mission, now);
                TestAssert.That(step.Module == ModuleKind.Core && step.Cell == OrbitalPlatform.CoreCell,
                    "a mission fit starts with the existing core launch");
                int launches = 0;
                while (!step.Complete && launches < OrbitalPlatform.CellCount)
                {
                    TestAssert.That(step.Failure == PlacementFailure.None,
                        mission + " fit must recommend a legal launch: " + step.Failure);
                    TestAssert.That(station.TryLaunch(step.Module, step.Cell, OrbitRegimes.Standard, 42, now,
                        PlatformModules.LaunchPrice(step.Module), Insertion, Dock) == PlacementFailure.None,
                        mission + " recommendation must pass the authoritative placement rules");
                    if (step.Module != ModuleKind.Core)
                    {
                        PlatformFitStep waiting = PlatformMissions.Next(station, mission, now);
                        TestAssert.That(waiting.Failure == PlacementFailure.LaunchInFlight && !waiting.Complete,
                            "the planner must wait for docking, without counting an in-flight module as fitted");
                    }
                    now += Dock;
                    station.Tick(now, 0.01f, true);
                    launches++;
                    step = PlatformMissions.Next(station, mission, now);
                }
                TestAssert.That(step.Complete && step.Fitted == step.Total && launches == step.Total,
                    mission + " fit must finish without duplicate launches");
                PlatformStats stats = station.Stats(now);
                TestAssert.That(stats.Mass <= OrbitalPlatform.MassLimit && stats.NetSunKw >= 0f &&
                    stats.StorageKj >= PlatformAbilities.Info(PlatformMissions.Ability(mission)).EnergyKj,
                    mission + " fit must support its payload mass, solar draw and burst storage");
                if (mission == PlatformMission.Emp)
                    TestAssert.That(!station.RunsHot(ModuleKind.Emp, now), "guided EMP radiator must cool the emitter");
                if (mission == PlatformMission.PrecisionStrike)
                    TestAssert.That(stats.Stabilised && station.Rods > 0, "strike fit must carry rods and the scatter-reducing gyro");
                TestAssert.That(PlatformMissions.Next(station, mission, now).Complete,
                    "a completed fit never schedules another module");
            }

            OrbitalPlatform heavy = Station(out double heavyNow);
            Add(heavy, ModuleKind.Reactor, 2, ref heavyNow);
            Add(heavy, ModuleKind.Rods, 6, ref heavyNow);
            Add(heavy, ModuleKind.Rods, 8, ref heavyNow);
            Add(heavy, ModuleKind.Habitat, 3, ref heavyNow);
            Add(heavy, ModuleKind.Battery, 1, ref heavyNow);
            PlatformFitStep blocked = PlatformMissions.Next(heavy, PlatformMission.Emp, heavyNow);
            TestAssert.That(!blocked.Complete && blocked.Module == ModuleKind.Emp && blocked.Failure == PlacementFailure.OverMass,
                "a custom station over the mission mass allowance must explain the blockage, never suggest an illegal cell");
            TestAssert.That(heavy.Stats(heavyNow).Mass == 37.5f, "planning must leave a player's custom station untouched");
        }

        private const double Insertion = 45.0;
        private const double Dock = 20.0;

        /// <summary>A core on Standard orbit, past insertion, sitting at the start of its first pass.</summary>
        private static OrbitalPlatform Station(out double now)
        {
            var platform = new OrbitalPlatform();
            PlacementFailure failure = platform.TryLaunch(ModuleKind.Core, OrbitalPlatform.CoreCell, OrbitRegimes.Standard,
                42, 100.0, 1300f, Insertion, Dock);
            TestAssert.That(failure == PlacementFailure.None, "a core must launch onto Standard orbit");
            now = FirstPassTime(platform, 100.0 + Insertion);
            return platform;
        }

        private static double FirstPassTime(OrbitalPlatform platform, double from)
        {
            OrbitState state = platform.State(from);
            return state.InPass ? from : from + state.TimeToPass + 0.5;
        }

        private static void Add(OrbitalPlatform platform, ModuleKind kind, int cell, ref double now)
        {
            PlacementFailure failure = platform.TryLaunch(kind, cell, 0, 0, now, 100f, Insertion, Dock);
            TestAssert.That(failure == PlacementFailure.None, "launching " + kind + " to " + cell + " failed: " + failure);
            now += Dock;
            platform.Tick(now, 0.01f, true);
            TestAssert.That(platform.Cell(cell) == kind, kind + " must dock at " + cell);
        }

        private static void TestOrbitMath()
        {
            TestAssert.That(Near(OrbitMath.Velocity(525000.0), 7600.0, 15.0), "525 km orbital velocity must be ~7.6 km/s");
            TestAssert.That(Near(OrbitMath.Elevation(525000.0, 0.0), Math.PI * 0.5, 1e-9), "overhead must read 90°");
            TestAssert.That(Near(OrbitMath.OffNadir(525000.0, Math.PI * 0.5), 0.0, 1e-9), "overhead must be nadir");
            TestAssert.That(Near(OrbitMath.SlantRange(525000.0, 0.0), 525000.0, 1e-6), "overhead slant must equal altitude");

            double central = OrbitMath.CentralAngleForOffNadir(525000.0, 30.0 * OrbitMath.Deg);
            double elevation = OrbitMath.Elevation(525000.0, central);
            TestAssert.That(Near(OrbitMath.OffNadir(525000.0, elevation), 30.0 * OrbitMath.Deg, 1e-6),
                "off-nadir and central angle must invert each other");
            TestAssert.That(OrbitMath.Incidence(elevation) > 30.0 * OrbitMath.Deg,
                "Earth curvature makes incidence exceed off-nadir");
        }

        private static void TestBands()
        {
            TestAssert.That(OrbitRegimes.All.Length == 1, "only a single orbit regime must exist");
            OrbitRegime leo = OrbitRegimes.Get(OrbitRegimes.Standard);
            TestAssert.That(leo.Index == OrbitRegimes.Standard, "regime index must be Standard");
            TestAssert.That(leo.Code == "LEO" && leo.Name == "LOW EARTH ORBIT", "regime must be LEO");
            TestAssert.That(Near(leo.Altitude, 500000.0, 1e-6), "altitude must be 500 km");
            TestAssert.That(Near(leo.InclinationDeg, 51.6, 1e-6), "inclination must be 51.6°");
            TestAssert.That(Near(leo.NadirGsd, 0.4, 1e-6), "nadir GSD must be 0.4 m");
            TestAssert.That(Near(leo.ScanScale, 1.0f, 1e-6), "scan scale must be 1.0");
            TestAssert.That(Near(leo.RodScatter, 15f, 1e-6), "rod scatter must be 15 m");
            TestAssert.That(Near(leo.EmpScale, 1.0f, 1e-6), "EMP scale must be 1.0");
            TestAssert.That(leo.DragFuelPerSecond == 0f, "space orbit must have zero drag fuel");

            // Backward compatibility constants
            TestAssert.That(OrbitRegimes.Low == 0 && OrbitRegimes.Mid == 0 && OrbitRegimes.High == 0,
                "compat constants Low, Mid, High must all map to 0");
            TestAssert.That(OrbitRegimes.Valid(OrbitRegimes.Standard), "Standard orbit must be valid");
            TestAssert.That(!OrbitRegimes.Valid(1) && !OrbitRegimes.Valid(-1), "other orbit indices must be invalid");
            TestAssert.That(OrbitRegimes.Get(99).Index == OrbitRegimes.Standard, "Get must return the single orbit");
        }

        private static void TestPassGeometry()
        {
            OrbitRegime mid = OrbitRegimes.Get(OrbitRegimes.Mid);
            var nadirPass = new OrbitState(OrbitPhase.InPass, new PassPlan(0.0), mid.Altitude, 0.0, 0.0, 0.0, 10.0);
            var holding = new OrbitState(OrbitPhase.Hold, new PassPlan(0.0), mid.Altitude, 0.0, 0.0, 12.0, 0.0);
            TestAssert.That(!TheaterTrack.Look(holding, 0.0, 0.0).Visible, "a holding station must not be visible");
            LookAngles overhead = TheaterTrack.Look(nadirPass, 0.0, 0.0);
            double straightDown = TheaterTrack.GroundSample(mid, overhead, false);
            TestAssert.That(Near(straightDown, mid.NadirGsd, 1e-6), "nadir GSD must be the band's");
            LookAngles oblique = TheaterTrack.Look(nadirPass, 400000.0, 0.0);
            TestAssert.That(TheaterTrack.GroundSample(mid, oblique, false) > straightDown * 1.5,
                "an oblique look must image coarser");
            TestAssert.That(Near(TheaterTrack.GroundSample(mid, overhead, true), straightDown * 3.0, 1e-6),
                "infrared must be three times coarser");
        }

        private static void TestCatalogue()
        {
            TestAssert.That(PlatformModules.Designs.Length == 14, "the catalogue must hold the core and 13 designs");
            float everything = 0f;
            int copies = 0;
            for (int i = 0; i < PlatformModules.Designs.Length; i++)
            {
                ModuleInfo info = PlatformModules.Designs[i];
                TestAssert.That(info.Code.Length == 3 && info.Mass > 0f && info.Price > 0f && info.MaxCopies >= 1,
                    info.Kind + " must be a complete design");
                TestAssert.That(PlatformModules.Info(info.Kind).Code == info.Code, info.Kind + " must look itself up");
                TestAssert.That(info.Mass <= PlatformModules.Vehicles[PlatformModules.Vehicles.Length - 1].Capacity,
                    info.Kind + " must fit the heaviest vehicle");
                if (info.Kind == ModuleKind.Core) continue;
                everything += info.Mass * info.MaxCopies;
                copies += info.MaxCopies;
            }
            TestAssert.That(copies > OrbitalPlatform.CellCount - 1, "there must be more modules than free cells");
            TestAssert.That(everything > (OrbitalPlatform.MassLimit - PlatformModules.Info(ModuleKind.Core).Mass) * 2f,
                "the catalogue must far outweigh what one station can lift");
            TestAssert.That(PlatformModules.VehicleFor(1f).Code == "LIGHT" && PlatformModules.VehicleFor(4f).Code == "MEDIUM" &&
                            PlatformModules.VehicleFor(12f).Code == "HEAVY", "vehicles must be chosen by mass");
            TestAssert.That(PlatformModules.LaunchPrice(ModuleKind.Solar) == 250f, "launch price must add the vehicle");
            TestAssert.That(!PlatformModules.Placeable((int)ModuleKind.Cargo) && !PlatformModules.Placeable(0),
                "cargo and empty must never be placeable");
            TestAssert.That(OrbitalPlatform.CellName(OrbitalPlatform.CoreCell) == "B3", "the core must sit in B3");
            for (int i = 0; i < PlatformAbilities.Count; i++)
                TestAssert.That((int)PlatformAbilities.All[i].Ability == i, "abilities must be indexed by their byte");
        }

        private static void TestPlacement()
        {
            var empty = new OrbitalPlatform();
            TestAssert.That(!empty.Exists, "a new platform must not exist");
            TestAssert.That(empty.CheckPlacement(ModuleKind.Solar, 6, 0, 0.0) == PlacementFailure.NoPlatform,
                "a module needs a core");
            TestAssert.That(empty.CheckPlacement(ModuleKind.Core, 0, OrbitRegimes.Standard, 0.0) == PlacementFailure.None,
                "a bare core can launch without propulsion");
            TestAssert.That(empty.CheckPlacement(ModuleKind.Core, 0, 9, 0.0) == PlacementFailure.UnknownOrbit,
                "an unknown band must be refused");

            OrbitalPlatform platform = Station(out double now);
            TestAssert.That(platform.Exists && platform.Energy == 600f, "the core must launch charged");
            TestAssert.That(platform.CheckPlacement(ModuleKind.Core, 0, OrbitRegimes.Standard, now) == PlacementFailure.PlatformExists,
                "one station per faction");
            TestAssert.That(platform.CanAttach(6) && platform.CanAttach(2) && !platform.CanAttach(0),
                "only cells next to the station can take a module");
            TestAssert.That(platform.CheckPlacement(ModuleKind.Solar, 0, 0, now) == PlacementFailure.NotAttached,
                "a detached cell must be refused");
            TestAssert.That(platform.CheckPlacement(ModuleKind.Solar, 7, 0, now) == PlacementFailure.CellOccupied,
                "the core cell is taken");
            TestAssert.That(platform.CheckPlacement(ModuleKind.Solar, 15, 0, now) == PlacementFailure.OutsideGrid,
                "cells outside the truss must be refused");
            TestAssert.That(platform.CheckPlacement(ModuleKind.Cargo, 6, 0, now) == PlacementFailure.UnknownModule,
                "cargo is not a module");

            TestAssert.That(platform.TryLaunch(ModuleKind.Solar, 6, 0, 0, now, 250f, Insertion, Dock) == PlacementFailure.None,
                "a solar array must launch");
            TestAssert.That(platform.CheckPlacement(ModuleKind.Battery, 8, 0, now) == PlacementFailure.LaunchInFlight,
                "one launch at a time");
            TestAssert.That(!platform.CanAttach(6), "the cell a module is flying to is reserved");
            platform.Tick(now + Dock - 1.0, 0.01f, true);
            TestAssert.That(platform.Cell(6) == ModuleKind.None, "a module must not dock early");
            now += Dock;
            platform.Tick(now, 0.01f, true);
            TestAssert.That(platform.Cell(6) == ModuleKind.Solar && platform.Notice == PlatformNotice.Docked,
                "a module must dock on time with a notice");
            TestAssert.That(platform.CanAttach(5) && platform.CanAttach(1), "docked modules extend the attach frontier");

            Add(platform, ModuleKind.Habitat, 8, ref now);
            TestAssert.That(platform.CheckPlacement(ModuleKind.Habitat, 2, 0, now) == PlacementFailure.CopyLimit,
                "copy limits must hold");

            // Fill toward the mass limit: core 12 + SOL 1.5 + HAB 6 + RTG 6 + ROD 5.5 + ROD 5.5 = 36.5.
            Add(platform, ModuleKind.Reactor, 2, ref now);
            Add(platform, ModuleKind.Rods, 12, ref now);
            Add(platform, ModuleKind.Rods, 5, ref now);
            TestAssert.That(Near(platform.Stats(now).Mass, 36.5, 1e-3), "mass must add up");
            TestAssert.That(platform.CheckPlacement(ModuleKind.Emp, 9, 0, now) == PlacementFailure.OverMass,
                "the mass limit must refuse a heavy module");
            TestAssert.That(platform.CheckPlacement(ModuleKind.Radiator, 9, 0, now) == PlacementFailure.None,
                "a light module must still fit");
            TestAssert.That(platform.LayoutMask() == ((1 << 7) | (1 << 6) | (1 << 8) | (1 << 2) | (1 << 12) | (1 << 5)),
                "the layout mask must mark occupied cells");
        }

        private static void TestJettison()
        {
            OrbitalPlatform platform = Station(out double now);
            Add(platform, ModuleKind.Solar, 6, ref now);
            Add(platform, ModuleKind.Battery, 5, ref now);
            TestAssert.That(platform.WouldStrand(6) && !platform.WouldStrand(5), "a bridge module must not strand others");
            TestAssert.That(platform.TryJettison(6, out _, out _) == PlacementFailure.WouldStrand,
                "jettisoning a bridge must be refused");
            TestAssert.That(platform.TryJettison(0, out _, out _) == PlacementFailure.EmptyCell, "an empty cell has nothing to drop");

            platform.Tick(now + 1.0, 1f, true);
            TestAssert.That(platform.TryJettison(5, out ModuleKind removed, out float refund) == PlacementFailure.None &&
                            removed == ModuleKind.Battery && refund == 100f, "a leaf module must jettison with its price");
            TestAssert.That(platform.Energy <= platform.Stats(now).StorageKj, "energy must clamp to the smaller store");

            TestAssert.That(platform.TryLaunch(ModuleKind.Battery, 5, 0, 0, now, 100f, Insertion, Dock) == PlacementFailure.None,
                "a module must launch toward a leaf");
            TestAssert.That(platform.WouldStrand(6), "the module in flight must keep its docking neighbour");

            TestAssert.That(platform.TryJettison(OrbitalPlatform.CoreCell, out removed, out refund) == PlacementFailure.None &&
                            removed == ModuleKind.Core && refund == 1400f, "jettisoning the core must deorbit everything paid");
            TestAssert.That(!platform.Exists && platform.Pending == ModuleKind.None, "a deorbited station must be gone");
        }

        private static void TestPower()
        {
            OrbitalPlatform platform = Station(out double now);
            Add(platform, ModuleKind.Imager, 6, ref now);
            Add(platform, ModuleKind.Sigint, 8, ref now);
            PlatformStats stats = platform.Stats(now);
            TestAssert.That(Near(stats.LoadKw, 3.5, 1e-4) && Near(stats.SolarKw, 4.0, 1e-4) && stats.StorageKj == 600f,
                "stats must total the docked modules");

            // Sunlit: +4 kW solar against a 3.5 kW load.
            float before = platform.Energy;
            platform.Tick(now, 1f, true);
            TestAssert.That(platform.Energy <= 600f && platform.Energy >= before - 0.001f, "a sunlit station must not drain");

            // Night pass: no solar, the load drains the core cells to a brownout.
            now = FirstPassTime(platform, now);
            for (int i = 0; i < 200 && !platform.Brownout; i++)
            {
                platform.Tick(now, 1f, false);
                now += 1.0;
                if (!platform.State(now).InPass) now = FirstPassTime(platform, now);
            }
            TestAssert.That(platform.Brownout && platform.Energy == 0f, "a dark load must brown the station out");
            TestAssert.That(platform.Check(PlatformAbility.Uplink, now) == PlatformDenial.Brownout,
                "a browned-out station must refuse every ability");

            // Daylight recharges a fixed station; waiting never creates free sunlight.
            for (int i = 0; i < 100 && platform.Brownout; i++) platform.Tick(now, 1f, true);
            TestAssert.That(!platform.Brownout && platform.Energy >= 600f * OrbitalPlatform.BrownoutRecovery,
                "sunlight must end the brownout at a quarter charge");

            // A reactor works in the dark, at half output without a radiator.
            Add(platform, ModuleKind.Reactor, 2, ref now);
            TestAssert.That(Near(platform.Stats(now).SteadyKw, 5.0, 1e-4) && platform.Stats(now).Overheating,
                "an uncooled reactor must run at half output");
            Add(platform, ModuleKind.Radiator, 1, ref now);
            TestAssert.That(Near(platform.Stats(now).SteadyKw, 10.0, 1e-4) && !platform.Stats(now).Overheating,
                "a radiator next to the reactor must restore it");
            TestAssert.That(platform.Stats(now).NetEclipseKw > 0f, "a cooled reactor must carry the night load");
        }

        private static double FirstAwayDelay(OrbitalPlatform platform, double now)
        {
            OrbitState state = platform.State(now);
            return state.InPass ? state.TimeToPassEnd + 0.5 : 0.0;
        }

        private static void TestDockingAndCargo()
        {
            OrbitalPlatform platform = Station(out double now);
            Add(platform, ModuleKind.Propulsion, 6, ref now);
            Add(platform, ModuleKind.Rods, 8, ref now);
            TestAssert.That(platform.Fuel == 100f && platform.Rods == 3, "tanks and magazines must arrive full");
            platform.Consume(PlatformAbility.RodStrike, now);
            platform.Consume(PlatformAbility.Rephase, now);
            TestAssert.That(platform.Rods == 2 && platform.Fuel == 75f, "consumption must spend rods and fuel");

            TestAssert.That(platform.TryResupply(now, Dock) == PlacementFailure.None && platform.Pending == ModuleKind.Cargo,
                "a cargo vehicle must launch");
            TestAssert.That(platform.TryResupply(now, Dock) == PlacementFailure.LaunchInFlight, "one cargo at a time");
            TestAssert.That(platform.CanAttach(5), "cargo must not reserve a cell");
            now += Dock;
            platform.Tick(now, 0.01f, true);
            TestAssert.That(platform.Fuel == 100f && platform.Rods == 3 && platform.Pending == ModuleKind.None,
                "cargo must refill tanks and magazines");
            TestAssert.That(platform.Cell(OrbitalPlatform.CoreCell) == ModuleKind.Core, "cargo must not replace the core");
            TestAssert.That(new OrbitalPlatform().TryResupply(0.0, Dock) == PlacementFailure.NoPlatform,
                "cargo needs a station");
        }

        private static void TestWeaponUpgrades()
        {
            OrbitalPlatform platform = Station(out double now);
            Add(platform, ModuleKind.Rods, 8, ref now);
            TestAssert.That(platform.RodSalvoCount(now) == 1, "one magazine releases one rod");
            Add(platform, ModuleKind.Rods, 9, ref now);
            TestAssert.That(platform.RodSalvoCount(now) == 2 && platform.Rods == 6,
                "a second magazine adds one shot and three stored rods");
            platform.Consume(PlatformAbility.RodStrike, now, platform.RodSalvoCount(now));
            TestAssert.That(platform.Rods == 4, "the salvo spends one rod per projectile");

            float baseScale = platform.Orbit.EmpScale;
            Add(platform, ModuleKind.Battery, 6, ref now);
            TestAssert.That(Near(platform.EmpScaleAt(now), baseScale, 1e-6),
                "the first battery establishes the base EMP radius");
            Add(platform, ModuleKind.Battery, 5, ref now);
            TestAssert.That(Near(platform.EmpScaleAt(now), baseScale * 1.25, 1e-6),
                "a second battery widens the actual EMP pulse");
            Add(platform, ModuleKind.Battery, 10, ref now);
            TestAssert.That(Near(platform.EmpScaleAt(now), baseScale * 1.5, 1e-6),
                "the third battery widens the actual EMP pulse again");
        }

        private static void TestAbilities()
        {
            var nothing = new OrbitalPlatform();
            TestAssert.That(nothing.Check(PlatformAbility.RadarScan, 0.0) == PlatformDenial.NoPlatform,
                "no station must be the first reason");

            var inserting = new OrbitalPlatform();
            inserting.TryLaunch(ModuleKind.Core, 0, OrbitRegimes.Standard, 5, 0.0, 0f, Insertion, Dock);
            TestAssert.That(inserting.Check(PlatformAbility.RadarScan, 1.0) == PlatformDenial.NotFitted,
                "an unfitted ability must say so");
            TestAssert.That(inserting.HoldAt(1.0) == PlatformHold.Insertion && inserting.HoldAt(Insertion + 1.0) == PlatformHold.None,
                "insertion must hold until the cycle begins");

            OrbitalPlatform platform = Station(out double now);
            Add(platform, ModuleKind.Imager, 6, ref now);
            Add(platform, ModuleKind.Solar, 5, ref now);
            Add(platform, ModuleKind.Rods, 8, ref now);
            Add(platform, ModuleKind.Emp, 9, ref now);

            now = FirstPassTime(platform, now);
            for (int i = 0; i < 60; i++) platform.Tick(now, 5f, true);
            TestAssert.That(platform.Check(PlatformAbility.RadarScan, now) == PlatformDenial.None,
                "a fitted, charged, overhead imager must be ready");
            TestAssert.That(platform.Check(PlatformAbility.Rephase, now) == PlatformDenial.NotFitted,
                "rephase needs propulsion");
            TestAssert.That(platform.Check(PlatformAbility.EmpBurst, now) == PlatformDenial.LowEnergy,
                "EMP needs more than the core can store");

            platform.Consume(PlatformAbility.RadarScan, now);
            TestAssert.That(platform.Check(PlatformAbility.RadarScan, now + 1.0) == PlatformDenial.Recharging &&
                            Near(platform.RechargeRemaining(PlatformAbility.RadarScan, now), 45.0, 1e-6),
                "a scan must recharge for 45 s");
            TestAssert.That(Near(platform.RechargeSeconds(PlatformAbility.EmpBurst, now), 360.0, 1e-3),
                "an uncooled EMP must recharge twice as slowly");

            for (int i = 0; i < 3; i++) platform.Consume(PlatformAbility.RodStrike, now + 100.0 * (i + 1));
            platform.Tick(now + 400.0, 0.01f, true);
            double later = FirstPassTime(platform, now + 400.0);
            for (int i = 0; i < 60; i++) platform.Tick(later, 5f, true);
            TestAssert.That(platform.Rods == 0 && platform.Check(PlatformAbility.RodStrike, later) == PlatformDenial.Expended,
                "an empty magazine must refuse a strike");

            OrbitState state = platform.State(later);
            double away = later + state.TimeToPassEnd + 1.0;
            TestAssert.That(platform.Check(PlatformAbility.RadarScan, away) == PlatformDenial.None,
                "station abilities remain available without a pass window");

            // Candidates run in cell order, so pick 0 is the solar array in cell 5.
            platform.StrikeDebris(0, later, 45.0);
            TestAssert.That(!platform.IsOnline(5, later), "a debris hit must take a module offline");

            OrbitalPlatform crewed = Station(out double t);
            Add(crewed, ModuleKind.Imager, 6, ref t);
            Add(crewed, ModuleKind.Habitat, 8, ref t);
            TestAssert.That(Near(crewed.RechargeSeconds(PlatformAbility.RadarScan, t), 45.0 * 0.75, 1e-3),
                "a crew must speed recharge");
            TestAssert.That(Near(crewed.ScanScale(t), 1f, 1e-6), "an unboosted LEO scan must be nominal");
            Add(crewed, ModuleKind.Relay, 1, ref t);
            TestAssert.That(Near(crewed.ScanScale(t), OrbitalPlatform.RelayBoost, 1e-6), "a relay next to the imager must boost it");
            TestAssert.That(Near(crewed.RodScatter(t), 15f, 1e-6), "LEO rods must scatter 15 m");
            Add(crewed, ModuleKind.Gyro, 2, ref t);
            TestAssert.That(Near(crewed.RodScatter(t), 7.5f, 1e-6), "gyros must halve rod scatter");
        }

        private static void TestManoeuvres()
        {
            OrbitalPlatform platform = Station(out double now);
            Add(platform, ModuleKind.Propulsion, 6, ref now);

            double pass = FirstPassTime(platform, now);
            TestAssert.That(platform.Check(PlatformAbility.Rephase, pass) == PlatformDenial.None,
                "propulsion permits relocation while on station");
            double away = pass + platform.State(pass).TimeToPassEnd + 1.0;
            TestAssert.That(platform.TryRelocate((platform.PositionIndex + 1) % StationKeeping.Count, away),
                "an away station with fuel must rephase");
            TestAssert.That(platform.Fuel == 75f && platform.HoldAt(away) == PlatformHold.Rephase, "rephase must burn and hold");
            OrbitState soon = platform.State(away + OrbitalPlatform.RephaseLeadSeconds + 0.1);
            TestAssert.That(soon.InPass || soon.TimeToPass < 10.0, "the next pass must come within seconds");
            TestAssert.That(platform.Check(PlatformAbility.RadarScan, away + 1.0) == PlatformDenial.NotFitted,
                "holds must not hide a missing module");

            double later = away + 200.0;
            TestAssert.That(platform.CheckShift(OrbitRegimes.Standard, later) == PlatformDenial.SameOrbit,
                "shift to same orbit must be refused");
            TestAssert.That(platform.CheckShift(1, later) == PlatformDenial.SameOrbit,
                "shift to invalid orbit must be refused as SameOrbit");
            TestAssert.That(!platform.TryShift(OrbitRegimes.Standard, later, 88), "a station cannot shift in single orbit");
            TestAssert.That(!platform.TryShift(1, later, 88), "a station cannot shift to invalid orbit");
            TestAssert.That(platform.Regime == OrbitRegimes.Standard, "regime must remain Standard");
        }

        private static void TestSafeMode()
        {
            OrbitalPlatform platform = Station(out double now);
            Add(platform, ModuleKind.Propulsion, 6, ref now);
            TestAssert.That(platform.Orbit.DragFuelPerSecond == 0f, "space orbit must have no drag fuel");
            float fuel = platform.Fuel;
            platform.Tick(now, 100f, true);
            TestAssert.That(platform.Fuel == fuel, "no fuel burned from atmospheric drag");
            TestAssert.That(platform.Regime == OrbitRegimes.Standard, "platform remains in Standard orbit");
        }

        private static void TestDebris()
        {
            OrbitalPlatform platform = Station(out double now);
            TestAssert.That(!platform.StrikeDebris(0, now, 45.0), "a bare core has nothing to hit");
            Add(platform, ModuleKind.Solar, 6, ref now);
            Add(platform, ModuleKind.Shield, 5, ref now);
            Add(platform, ModuleKind.Imager, 8, ref now);
            // Candidates in cell order: 5 SHD, 6 SOL, 8 IMG.
            TestAssert.That(platform.IsShielded(5) && platform.IsShielded(6) && !platform.IsShielded(8),
                "a shield must cover itself and its neighbours");
            platform.StrikeDebris(1, now, 45.0);
            TestAssert.That(platform.Notice == PlatformNotice.DebrisDeflected && platform.IsOnline(6, now),
                "a shielded module must deflect");
            platform.StrikeDebris(2, now, 45.0);
            TestAssert.That(platform.Notice == PlatformNotice.DebrisHit && platform.NoticeCell == 8 && !platform.IsOnline(8, now),
                "an unshielded module must drop offline");
            TestAssert.That(platform.Check(PlatformAbility.RadarScan, now) == PlatformDenial.Offline,
                "an offline imager must refuse scans");
            TestAssert.That(Near(platform.OfflineRemaining(8, now + 10.0), 35.0, 1e-6) && platform.IsOnline(8, now + 45.0),
                "a hit module must come back after its outage");
            TestAssert.That(platform.Stats(now).Online == 3 && platform.Stats(now).LoadKw == 0f,
                "an offline module must draw nothing");
        }

        private static void TestSnapshot()
        {
            OrbitalPlatform host = Station(out double now);
            Add(host, ModuleKind.Imager, 6, ref now);
            Add(host, ModuleKind.Propulsion, 8, ref now);
            host.Consume(PlatformAbility.RadarScan, now);
            host.StrikeDebris(0, now, 45.0);
            host.TryLaunch(ModuleKind.Solar, 5, 0, 0, now, 250f, Insertion, Dock);

            var snapshot = new PlatformSnapshot();
            host.Export(now, snapshot);
            TestAssert.That(snapshot.Active && snapshot.Modules[OrbitalPlatform.CoreCell] == (byte)ModuleKind.Core,
                "an export must carry the layout");

            var client = new OrbitalPlatform();
            double local = 5000.0;
            client.Mirror(snapshot, local);
            double offset = local - now;
            TestAssert.That(client.Exists && client.Cell(6) == ModuleKind.Imager && client.Cell(8) == ModuleKind.Propulsion,
                "a mirror must rebuild the layout");
            TestAssert.That(client.Seed == host.Seed && client.Regime == host.Regime && Near(client.CycleStart, host.CycleStart + offset, 1e-3),
                "a mirror must rebase the pass clock");
            TestAssert.That(client.State(local).Phase == host.State(now).Phase,
                "host and client must agree on the phase");
            TestAssert.That(Near(client.RechargeRemaining(PlatformAbility.RadarScan, local), 45.0, 1e-3),
                "a mirror must carry recharge timers");
            TestAssert.That(client.Pending == ModuleKind.Solar && client.PendingCell == 5 && Near(client.DockAt - local, Dock, 1e-3),
                "a mirror must carry the launch in flight");
            TestAssert.That(!client.IsOnline(6, local) || !client.IsOnline(8, local), "a mirror must carry outages");
            TestAssert.That(client.NoticeSerial == host.NoticeSerial && client.Fuel == host.Fuel && client.Energy == host.Energy,
                "a mirror must carry notices and resources");

            double epoch = client.CycleStart;
            host.Export(now + 2.0, snapshot);
            snapshot.CycleClock += 0.3f; // late packet
            client.Mirror(snapshot, local + 2.0);
            TestAssert.That(client.CycleStart == epoch, "latency jitter must not move the epoch");
            snapshot.CycleClock += 5f;
            client.Mirror(snapshot, local + 2.0);
            TestAssert.That(client.CycleStart != epoch, "a real clock change must rebase");

            snapshot.Modules[0] = 99;
            client.Mirror(snapshot, local + 3.0);
            TestAssert.That(client.Cell(0) == ModuleKind.None, "an invalid snapshot must be ignored");
            snapshot.Modules[0] = 0;
            snapshot.Energy = float.NaN;
            client.Mirror(snapshot, local + 3.0);
            TestAssert.That(!float.IsNaN(client.Energy), "a non-finite snapshot must be ignored");

            var gone = new PlatformSnapshot();
            client.Mirror(gone, local + 4.0);
            TestAssert.That(!client.Exists, "an inactive snapshot must clear the mirror");
        }

        private static void TestForeign()
        {
            var foreign = new ForeignPlatform();
            foreign.Mirror(OrbitRegimes.Standard, 9, 100.0, (ushort)((1 << 7) | (1 << 6) | (1 << 8)));
            TestAssert.That(foreign.ModuleCount == 3 && foreign.Occupied(6) && !foreign.Occupied(0),
                "a foreign silhouette must count its cells");
            foreign.Mirror(OrbitRegimes.Standard, 9, 100.4, foreign.Layout);
            TestAssert.That(foreign.CycleStart == 100.0, "foreign jitter must be held");
            foreign.Mirror(OrbitRegimes.Standard, 10, 100.4, foreign.Layout);
            TestAssert.That(foreign.CycleStart == 100.4, "a new orbit must rebase");
            TestAssert.That(foreign.State(50.0).Phase == OrbitPhase.Hold,
                "a foreign station before its epoch must hold");
        }

        private static void TestWords()
        {
            TestAssert.That(PlatformWords.Phase(null, 0.0) == "NO STATION ON ORBIT", "no station must say so");
            var inserting = new OrbitalPlatform();
            inserting.TryLaunch(ModuleKind.Core, 0, OrbitRegimes.Standard, 5, 0.0, 0f, Insertion, Dock);
            TestAssert.That(PlatformWords.Phase(inserting, 14.5) == "INSERTION · T-00:31",
                "a hold must count down, rounded up: " + PlatformWords.Phase(inserting, 14.5));
            OrbitalPlatform platform = Station(out double now);
            TestAssert.That(PlatformWords.Phase(platform, now).StartsWith("ON STATION · ", StringComparison.Ordinal),
                "a fixed station names its sector");
            TestAssert.That(PlatformWords.Denial(PlatformDenial.NotFitted, platform, PlatformAbility.Elint, now) ==
                            "NOT FITTED · BUILD SIG", "a missing module must name what to build");
            TestAssert.That(PlatformWords.Denial(PlatformDenial.LowEnergy, platform, PlatformAbility.EmpBurst, now) ==
                            "LOW ENERGY · 600/900 KJ", "low energy must show have and need");
            TestAssert.That(PlatformWords.Placement(PlacementFailure.OverMass) == "OVER MASS LIMIT", "placement words must read");
            TestAssert.That(PlatformWords.Kilowatts(-1.5f) == "−1.5 KW" && PlatformWords.Kilowatts(12f) == "+12.0 KW",
                "power must carry its sign");
            TestAssert.That(PlatformWords.Clock(0.2) == "00:01" && PlatformWords.Clock(-5.0) == "00:00",
                "countdowns must round up and never go negative");
        }

        private static void TestSarProjection()
        {
            var geometry = new SarGeometry(30.0 * OrbitMath.Deg, 1.0, 0.0, 700000.0, 7500.0);
            var former = new SarImageFormer(200, 200, 1000.0, geometry, 7);
            TestAssert.That(former.Project(0.0, 0.0, 0.0, 0.0, out int groundColumn, out int groundRow),
                "the scene centre must project into the image");
            TestAssert.That(former.Project(0.0, 100.0, 0.0, 0.0, out int tallColumn, out int tallRow), "a tall point must project");
            double layover = (groundColumn - tallColumn) * former.PixelRange;
            TestAssert.That(Near(layover, 100.0 / Math.Tan(30.0 * OrbitMath.Deg), former.PixelRange * 1.01),
                "height must lay over toward the sensor by h·cot(incidence)");
            TestAssert.That(tallRow == groundRow, "layover must not move a point in azimuth");

            TestAssert.That(former.Project(0.0, 0.0, 0.0, 5.0, out _, out int movingRow), "a slow mover must stay in the scene");
            double shift = Math.Abs(movingRow - groundRow) * former.PixelAzimuth;
            TestAssert.That(Near(shift, 5.0 * 700000.0 / 7500.0, former.PixelAzimuth * 1.01),
                "radial velocity must displace a target in azimuth by v·R/V");

            TestAssert.That(!former.Project(5000.0, 0.0, 0.0, 0.0, out _, out _), "points outside the scene must be dropped");
            former.Add(5000.0, 0.0, 0.0, 1.0, 0.0);
            TestAssert.That(former.Dropped == 1 && former.Accepted == 0, "dropped samples must be counted, not drawn");

            former.PlanRay(0, 300, out double x0, out double z0);
            TestAssert.That(x0 > 900.0 && Math.Abs(z0) <= 1000.0, "the first ray must start at near range");
            TestAssert.That(former.RayCount(300) == 300 * 200, "ray count must be range samples × azimuth lines");
        }

        private static void TestSarFormation()
        {
            var geometry = new SarGeometry(35.0 * OrbitMath.Deg, 0.0, 1.0, 650000.0, 7500.0);
            byte[] Build(int seed)
            {
                var former = new SarImageFormer(64, 64, 640.0, geometry, seed);
                for (int i = 0; i < former.RayCount(96); i++)
                {
                    former.PlanRay(i, 96, out double x, out double z);
                    bool water = z < 0.0;
                    former.Add(x, 0.0, z, water ? 0.004 : 0.12, 0.0);
                }
                former.Add(0.0, 0.0, 300.0, 40.0, 0.0);
                return former.Form(2, 0.0005);
            }

            byte[] a = Build(21);
            byte[] b = Build(21);
            byte[] c = Build(22);
            bool same = true, differs = false;
            for (int i = 0; i < a.Length; i++)
            {
                same &= a[i] == b[i];
                differs |= a[i] != c[i];
            }
            TestAssert.That(same, "the same samples and seed must form identical images");
            TestAssert.That(differs, "a different seed must change the speckle");

            // Range axis points north here: +z (land) is near range (low columns), -z (water) far range.
            double land = 0.0, water = 0.0;
            int landCount = 0, waterCount = 0;
            for (int row = 0; row < 64; row++)
            {
                for (int column = 4; column < 28; column++) { land += a[row * 64 + column]; landCount++; }
                for (int column = 36; column < 60; column++) { water += a[row * 64 + column]; waterCount++; }
            }
            TestAssert.That(land / landCount > water / waterCount + 40.0, "water must image much darker than land");

            var probe = new SarImageFormer(64, 64, 640.0, geometry, 21);
            probe.Project(0.0, 0.0, 300.0, 0.0, out int pointColumn, out int pointRow);
            TestAssert.That(a[pointRow * 64 + pointColumn] >= 250, "a point target must saturate");
        }
    }
}
