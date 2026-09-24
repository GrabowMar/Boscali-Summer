using System;

namespace BoscaliSummer.Features.Support.Domain.Orbital
{
    internal enum PlatformMission : byte { Recon, PrecisionStrike, Emp }

    internal readonly struct PlatformFitStep
    {
        public readonly ModuleKind Module;
        public readonly int Cell, Fitted, Total;
        public readonly PlacementFailure Failure;
        public bool Complete => Module == ModuleKind.None;

        public PlatformFitStep(ModuleKind module, int cell, int fitted, int total, PlacementFailure failure)
        {
            Module = module;
            Cell = cell;
            Fitted = fitted;
            Total = total;
            Failure = failure;
        }
    }

    /// <summary>Client-side fitting advice; every individual launch still passes host placement and cost checks.</summary>
    internal static class PlatformMissions
    {
        public const int Count = 3;
        private static readonly ModuleKind[][] Fits =
        {
            new[] { ModuleKind.Core, ModuleKind.Imager, ModuleKind.Solar, ModuleKind.Sigint, ModuleKind.Relay, ModuleKind.Battery },
            new[] { ModuleKind.Core, ModuleKind.Rods, ModuleKind.Gyro, ModuleKind.Solar, ModuleKind.Battery },
            new[] { ModuleKind.Core, ModuleKind.Battery, ModuleKind.Emp, ModuleKind.Radiator, ModuleKind.Solar }
        };

        public static string Name(PlatformMission mission) => mission == PlatformMission.PrecisionStrike ? "PRECISION STRIKE"
            : mission == PlatformMission.Emp ? "EMP SUPPORT" : "RECON";

        public static string Brief(PlatformMission mission) => mission == PlatformMission.PrecisionStrike
            ? "ROD + GYRO · HALF SCATTER · 80 KJ + 1 ROD / STRIKE"
            : mission == PlatformMission.Emp ? "EMP + COOLING · 900 KJ / BURST · FRIENDLY FIRE"
            : "IMAGER + ELINT + RELAY · 240 / 180 KJ PER SWEEP";

        public static PlatformAbility Ability(PlatformMission mission) => mission == PlatformMission.PrecisionStrike
            ? PlatformAbility.RodStrike : mission == PlatformMission.Emp ? PlatformAbility.EmpBurst : PlatformAbility.RadarScan;

        public static PlatformFitStep Next(OrbitalPlatform platform, PlatformMission mission, double now)
        {
            ModuleKind[] fit = Fits[Math.Max(0, Math.Min(Count - 1, (int)mission))];
            ModuleKind next = ModuleKind.None;
            int fitted = 0;
            for (int i = 0; i < fit.Length; i++)
            {
                if (Fitted(platform, fit[i])) fitted++;
                else if (next == ModuleKind.None) next = fit[i];
            }
            if (next == ModuleKind.None) return new PlatformFitStep(next, -1, fitted, fit.Length, PlacementFailure.None);
            if (platform == null) return new PlatformFitStep(next, OrbitalPlatform.CoreCell, fitted, fit.Length, PlacementFailure.NoPlatform);
            if (next == ModuleKind.Core) return new PlatformFitStep(next, OrbitalPlatform.CoreCell, fitted, fit.Length,
                platform.CheckPlacement(next, OrbitalPlatform.CoreCell, OrbitRegimes.Standard, now));
            int cell = BestCell(platform, next, now, out PlacementFailure failure);
            return new PlatformFitStep(next, cell, fitted, fit.Length, failure);
        }

        private static bool Fitted(OrbitalPlatform platform, ModuleKind module)
        {
            if (platform == null || platform.Count(module) <= (platform.Pending == module ? 1 : 0)) return false;
            if (module != ModuleKind.Relay && module != ModuleKind.Radiator) return true;
            for (int cell = 0; cell < OrbitalPlatform.CellCount; cell++)
            {
                if (platform.Cell(cell) != module) continue;
                for (int neighbour = 0; neighbour < OrbitalPlatform.CellCount; neighbour++)
                {
                    if (!Adjacent(cell, neighbour)) continue;
                    ModuleKind kind = platform.Cell(neighbour);
                    if (module == ModuleKind.Relay ? Sensor(kind) : kind == ModuleKind.Emp) return true;
                }
            }
            return false;
        }

        /// <summary>Only recommends legal cells; neighbouring cooling/relay/shield utilities win before compactness.</summary>
        public static int BestCell(OrbitalPlatform platform, ModuleKind module, double now, out PlacementFailure failure)
        {
            int best = -1, score = int.MinValue;
            if (platform.Pending != ModuleKind.None) { failure = PlacementFailure.LaunchInFlight; return -1; }
            failure = PlacementFailure.NotAttached;
            for (int cell = 0; cell < OrbitalPlatform.CellCount; cell++)
            {
                if (!platform.CanAttach(cell)) continue;
                PlacementFailure check = platform.CheckPlacement(module, cell, OrbitRegimes.Standard, now);
                if (check != PlacementFailure.None) { failure = check; continue; }
                int candidate = -Math.Abs(OrbitalPlatform.Column(cell) - 2) - Math.Abs(OrbitalPlatform.Row(cell) - 1);
                for (int neighbour = 0; neighbour < OrbitalPlatform.CellCount; neighbour++)
                {
                    if (!Adjacent(cell, neighbour)) continue;
                    ModuleKind kind = platform.Cell(neighbour);
                    if (kind == ModuleKind.None) continue;
                    if ((module == ModuleKind.Radiator && PlatformModules.Info(kind).Hot) ||
                        (PlatformModules.Info(module).Hot && kind == ModuleKind.Radiator)) candidate += 100;
                    if ((module == ModuleKind.Relay && Sensor(kind)) || (Sensor(module) && kind == ModuleKind.Relay)) candidate += 40;
                    if (kind == ModuleKind.Shield) candidate += 10;
                }
                if (candidate <= score) continue;
                best = cell;
                score = candidate;
            }
            if (best >= 0) failure = PlacementFailure.None;
            return best;
        }

        private static bool Sensor(ModuleKind kind) => kind == ModuleKind.Imager || kind == ModuleKind.Sigint;
        private static bool Adjacent(int a, int b) => Math.Abs(OrbitalPlatform.Column(a) - OrbitalPlatform.Column(b)) +
            Math.Abs(OrbitalPlatform.Row(a) - OrbitalPlatform.Row(b)) == 1;
    }
}
