using BoscaliSummer.Modules.Intel.Domain;
using BoscaliSummer.Core.Contracts;

namespace BoscaliSummer.Tests.Features.Intel
{
    /// <summary>Where the faction has looked, how long ago, and what ground power it knows about.</summary>
    internal static class ObservationTests
    {
        public static void Run()
        {
            GridStampsCellsWithinRadius();
            APassStampsEachSourceCellOnce();
            NegativeCellsNeverAlias();
            GridStopsAtItsCeiling();
            ScoutedExpiresButAgeStaysKnown();
            RolesFollowTheUnitCode();
            AreaIntelSumsFreshGroundPower();
        }

        private static bool Near(float a, float b) => a - b <= 0.001f && b - a <= 0.001f;

        private static void GridStampsCellsWithinRadius()
        {
            var grid = new ObservationGrid();
            grid.BeginPass();
            grid.Stamp(1000f, 1000f, ObservationGrid.GroundRadius, 100f);
            TestAssert.That(grid.LastStamp(7000f, 1000f, 0f) == 100f, "a cell 6 km out, centre to centre, is stamped");
            TestAssert.That(float.IsNaN(grid.LastStamp(9500f, 1000f, 0f)), "a cell 8 km out is not");
            TestAssert.That(grid.LastStamp(9500f, 1000f, 2500f) == 100f,
                "an area read takes the freshest cell whose centre lies within the radius");
        }

        private static void APassStampsEachSourceCellOnce()
        {
            var grid = new ObservationGrid();
            grid.BeginPass();
            grid.Stamp(100f, 100f, ObservationGrid.GroundRadius, 50f);
            grid.Stamp(1900f, 1900f, ObservationGrid.GroundRadius, 60f);
            TestAssert.That(grid.LastStamp(1000f, 1000f, 0f) == 50f,
                "a second unit in the same cell and radius class adds nothing this pass");
            grid.Stamp(100f, 100f, ObservationGrid.AircraftRadius, 60f);
            TestAssert.That(grid.LastStamp(1000f, 1000f, 0f) == 60f, "another radius class from the same cell still stamps");
            grid.BeginPass();
            grid.Stamp(1900f, 1900f, ObservationGrid.GroundRadius, 70f);
            TestAssert.That(grid.LastStamp(1000f, 1000f, 0f) == 70f, "the next pass stamps again");
        }

        private static void NegativeCellsNeverAlias()
        {
            var grid = new ObservationGrid();
            grid.BeginPass();
            grid.Stamp(-5000f, 3000f, 1f, 10f);
            grid.Stamp(-100f, 100f, 1f, 20f);
            TestAssert.That(grid.LastStamp(-5000f, 3000f, 0f) == 10f, "negative coordinates stamp their own cell");
            TestAssert.That(float.IsNaN(grid.LastStamp(3000f, -5000f, 0f)), "(-3, 1) and (1, -3) are different cells");
            TestAssert.That(float.IsNaN(grid.LastStamp(100f, -100f, 0f)), "(-1, 0) and (0, -1) are different cells");
            TestAssert.That(ObservationGrid.CellOf(-0.5f) == -1 && ObservationGrid.CellOf(0f) == 0,
                "cells floor; they do not truncate toward zero");
        }

        private static void GridStopsAtItsCeiling()
        {
            var grid = new ObservationGrid(4);
            grid.BeginPass();
            grid.Stamp(0f, 0f, ObservationGrid.GroundRadius, 1f);
            TestAssert.That(grid.Count == 4 && grid.Overflowed, "the grid never grows past its ceiling, and says so");
            grid.Clear();
            TestAssert.That(grid.Count == 0 && !grid.Overflowed, "a scene reset empties it");
        }

        private static void ScoutedExpiresButAgeStaysKnown()
        {
            var grid = new ObservationGrid();
            grid.BeginPass();
            grid.Stamp(0f, 0f, ObservationGrid.GroundRadius, 100f);
            grid.Read(0f, 0f, 3000f, 340f, out bool scouted, out float age);
            TestAssert.That(scouted && Near(age, 240f), "stamped 240 s ago still counts as scouted");
            grid.Read(0f, 0f, 3000f, 341f, out scouted, out age);
            TestAssert.That(!scouted && Near(age, 241f), "past 240 s the area is unscouted, and its age is still reported");
            grid.Read(90000f, 0f, 3000f, 341f, out scouted, out age);
            TestAssert.That(!scouted && float.IsNaN(age), "ground never looked at reads UNSCOUTED with age NaN, never 0");
        }

        private static void RolesFollowTheUnitCode()
        {
            TestAssert.That(ForceRoles.Classify("MBT", false, 0.7f, 0.2f) == ForceRole.Mbt &&
                            ForceRoles.GroundWeight(ForceRole.Mbt) == 1.2f, "a tank weighs 1.2");
            TestAssert.That(ForceRoles.Classify("IFV", false, 0.59f, 0.43f) == ForceRole.Ifv, "IFV");
            TestAssert.That(ForceRoles.Classify("MRAP", false, 0.25f, 0.15f) == ForceRole.Apc, "an MRAP counts as an APC");
            TestAssert.That(ForceRoles.Classify("ATGM", true, 0.5f, 0f) == ForceRole.AntiTank,
                "the AT-145 emplacement is anti-tank power, not a generic emplacement");
            TestAssert.That(ForceRoles.Classify("SAM IR", false, 0f, 0.74f) == ForceRole.AirDefence &&
                            ForceRoles.GroundWeight(ForceRole.AirDefence) == 0.8f, "an IR SAM carries only its 0.8 ground weight");
            TestAssert.That(ForceRoles.Classify("MLRS", false, 1f, 0f) == ForceRole.Artillery, "MLRS is artillery");
            TestAssert.That(ForceRoles.Classify("TRK", false, 0f, 0f) == ForceRole.Truck &&
                            ForceRoles.GroundWeight(ForceRole.Truck) == 0.1f, "a truck weighs 0.1");
            TestAssert.That(ForceRoles.Classify("BNKR", true, 0.28f, 0.25f) == ForceRole.Emplacement, "a bunker is an emplacement");
            TestAssert.That(ForceRoles.Classify("LCV25", false, 0.5f, 0.25f) == ForceRole.AntiTank, "LCV25 AT falls back on its role identity");
            TestAssert.That(ForceRoles.Classify("LCV25", false, 0.25f, 0.5f) == ForceRole.AirDefence, "LCV25 AA falls back on its role identity");
            TestAssert.That(ForceRoles.Classify(null, false, 0.25f, 0f) == ForceRole.Other, "an unknown code with no role reads as other");
        }

        private static KnownHostile Ground(uint id, float x, float z, ForceRole role, float spottedAt, bool isStatic,
            UnitClass unitClass = UnitClass.GroundVehicle) => new KnownHostile
        {
            Id = id,
            X = x,
            Z = z,
            SpottedAt = spottedAt,
            Priority = KnownHostileFilter.PriorityMobile,
            Static = isStatic,
            Class = unitClass,
            Role = role,
            Profile = -1,
            Confirmed = true,
            DeadSince = float.NaN
        };

        private static void AreaIntelSumsFreshGroundPower()
        {
            var table = new KnownHostileTable();
            table.Upsert(Ground(1, 1000f, 0f, ForceRole.Mbt, 90f, false));
            table.Upsert(Ground(2, 0f, 1000f, ForceRole.Ifv, 90f, false));
            table.Upsert(Ground(3, -2000f, 0f, ForceRole.AntiTank, 10f, true, UnitClass.Building));
            table.Upsert(Ground(4, 0f, -2500f, ForceRole.AirDefence, -500f, true));
            table.Upsert(Ground(5, 5000f, 0f, ForceRole.Mbt, 90f, false));
            table.Upsert(Ground(6, 500f, 0f, ForceRole.Apc, -550f, false));
            table.Upsert(Ground(7, 0f, 0f, ForceRole.Other, 99f, false, UnitClass.Aircraft));
            var grid = new ObservationGrid();
            grid.BeginPass();
            grid.Stamp(0f, 0f, ObservationGrid.GroundRadius, 90f);

            AreaIntel seen = AreaIntelReader.Read(table, grid, 0f, 0f, 3000f, 100f);
            TestAssert.That(seen.Scouted && Near(seen.LastObservedAgeSeconds, 10f), "own units stamped the area 10 s ago");
            TestAssert.That(Near(seen.Power, 4.0f),
                "MBT 1.2 + IFV 1.0 + AT emplacement 1.0 + static SAM 0.8; the far tank, the 650 s old APC and the aircraft do not count");
            TestAssert.That(Near(seen.AntiTank, 1.0f) && Near(seen.AirDefence, 0.8f) && Near(seen.Artillery, 0f),
                "the role sums split the same power");

            AreaIntel blind = AreaIntelReader.Read(table, new ObservationGrid(), 0f, 0f, 3000f, 100f);
            TestAssert.That(!blind.Scouted && float.IsNaN(blind.LastObservedAgeSeconds) && Near(blind.Power, 4.0f),
                "unscouted ground still reports what is known there; the caller decides what unscouted means");
        }
    }
}
