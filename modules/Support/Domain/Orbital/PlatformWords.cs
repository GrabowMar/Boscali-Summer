using System;
using System.Globalization;

namespace BoscaliSummer.Features.Support.Domain.Orbital
{
    /// <summary>
    /// Operator words for station state, shared by the console, the uplink and the map so a
    /// refusal reads the same everywhere. Status is always text; colour only repeats it.
    /// </summary>
    internal static class PlatformWords
    {
        public static string Hold(PlatformHold hold)
        {
            switch (hold)
            {
                case PlatformHold.Insertion: return "INSERTION";
                case PlatformHold.Transfer: return "ORBIT TRANSFER";
                case PlatformHold.Rephase: return "RELOCATING";
                case PlatformHold.SafeMode: return "SAFE MODE CLIMB";
                default: return "ON STATION";
            }
        }

        /// <summary>One-line flight status: OVERHEAD · LOS 01:22, AWAY · AOS 00:41, INSERTION · T-00:31.</summary>
        public static string Phase(OrbitalPlatform platform, double now)
        {
            if (platform == null || !platform.Exists) return "NO STATION ON ORBIT";
            PlatformHold hold = platform.HoldAt(now);
            if (hold != PlatformHold.None) return Hold(hold) + " · T-" + Clock(platform.CycleStart - now);
            return "ON STATION · " + StationKeeping.Name(platform.PositionIndex);
        }

        public static string Denial(PlatformDenial denial, OrbitalPlatform platform, PlatformAbility ability, double now)
        {
            AbilityInfo info = PlatformAbilities.Info(ability);
            switch (denial)
            {
                case PlatformDenial.None: return "READY";
                case PlatformDenial.NoPlatform: return "NO STATION · LAUNCH A CORE";
                case PlatformDenial.NotFitted: return "NOT FITTED · BUILD " + PlatformModules.Info(info.Module).Code;
                case PlatformDenial.Offline: return "MODULE OFFLINE · " + Clock(Outage(platform, info.Module, now));
                case PlatformDenial.Brownout: return "BROWNOUT · CELLS RECHARGING";
                case PlatformDenial.Holding:
                    return platform == null ? "HOLDING"
                        : Hold(platform.HoldAt(now)) + " · T-" + Clock(platform.CycleStart - now);
                case PlatformDenial.NotOverhead:
                    return platform == null ? "NOT OVERHEAD"
                        : "AWAY · AOS " + Clock(platform.State(now).TimeToPass);
                case PlatformDenial.Overhead:
                    return platform == null ? "OVERHEAD"
                        : "OVERHEAD · BURN AFTER LOS " + Clock(platform.State(now).TimeToPassEnd);
                case PlatformDenial.LowEnergy:
                    return "LOW ENERGY · " + Whole(platform != null ? platform.Energy : 0f) + "/" + Whole(info.EnergyKj) + " KJ";
                case PlatformDenial.Recharging:
                    return "RECHARGING · " + Clock(platform != null ? platform.RechargeRemaining(ability, now) : 0.0);
                case PlatformDenial.Expended: return "NO RODS · SEND CARGO";
                case PlatformDenial.NoFuel: return "NO FUEL · SEND CARGO";
                case PlatformDenial.SameOrbit: return "SELECT ANOTHER POSITION";
                default: return denial.ToString().ToUpperInvariant();
            }
        }

        public static string Placement(PlacementFailure failure)
        {
            switch (failure)
            {
                case PlacementFailure.None: return "FITS";
                case PlacementFailure.NoPlatform: return "NO STATION";
                case PlacementFailure.PlatformExists: return "STATION ON ORBIT";
                case PlacementFailure.LaunchInFlight: return "LAUNCH IN FLIGHT";
                case PlacementFailure.OutsideGrid: return "OUTSIDE TRUSS";
                case PlacementFailure.CellOccupied: return "CELL TAKEN";
                case PlacementFailure.NotAttached: return "NOT CONNECTED";
                case PlacementFailure.OverMass: return "OVER MASS LIMIT";
                case PlacementFailure.CopyLimit: return "COPY LIMIT";
                case PlacementFailure.UnknownOrbit: return "UNKNOWN ORBIT";
                case PlacementFailure.WouldStrand: return "WOULD STRAND MODULES";
                case PlacementFailure.EmptyCell: return "EMPTY CELL";
                case PlacementFailure.NeedsPropulsion: return "LOW NEEDS PROPULSION";
                default: return "NOT A MODULE";
            }
        }

        public static string Kilowatts(float kw) =>
            (kw >= 0f ? "+" : "−") + Math.Abs(kw).ToString("0.0", CultureInfo.InvariantCulture) + " KW";

        public static string Tonnes(float t) => t.ToString("0.0", CultureInfo.InvariantCulture) + " T";

        public static string Whole(float value) =>
            Math.Round(value).ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>Countdown clock rounded up, so T-00:00 is only shown when the moment has come.</summary>
        public static string Clock(double seconds) =>
            double.IsNaN(seconds) || double.IsInfinity(seconds) ? "--:--" : TheaterGrid.Clock(Math.Ceiling(Math.Max(0.0, seconds)));

        private static double Outage(OrbitalPlatform platform, ModuleKind kind, double now)
        {
            if (platform == null) return 0.0;
            double shortest = double.MaxValue;
            for (int i = 0; i < OrbitalPlatform.CellCount; i++)
                if (platform.Cell(i) == kind) shortest = Math.Min(shortest, platform.OfflineRemaining(i, now));
            return shortest == double.MaxValue ? 0.0 : shortest;
        }
    }
}
