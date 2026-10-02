using System;

namespace BoscaliSummer.Modules.Support.Domain.SpecOps
{
    /// <summary>The bounded result of one route choice; passive pressure remains a separate clock.</summary>
    internal readonly struct FieldRouteForecast
    {
        public readonly int Preparation, Intel, Exposure, Quality, Charges, RouteStep;
        public readonly bool CanExecute;
        public readonly float CooldownSeconds;

        public FieldRouteForecast(FieldTeam team, float cooldown, bool inWindow)
        {
            Preparation = team.Preparation; Intel = team.Intel; Exposure = team.Exposure;
            Quality = SpecOpsDetachment.ExecutionQuality(team);
            Charges = FieldCatalog.PostCharges(Quality); RouteStep = team.RouteStep;
            CanExecute = inWindow && team.State == TeamState.Deciding && team.RouteStep >= 3 && team.Preparation >= FieldCatalog.MinimumPreparation &&
                team.Exposure <= FieldCatalog.MaximumExecuteExposure;
            CooldownSeconds = cooldown;
        }
    }

    internal sealed partial class SpecOpsDetachment
    {
        // Host-only run memory. The two low bits remain independent per-leg contributions;
        // this bit survives route changes and is deliberately excluded from wire snapshots.
        private const byte ReconHandoff = 128;
        public static int AssistanceCost(int order) => order == 5 ? 100 : order == 6 ? 150 : 0;

        public static int ExecutionQuality(FieldTeam team) => team.Friendly ? 0 :
            FieldCatalog.Quality(team.Preparation, team.Intel, team.Exposure);

        public static FieldRouteForecast RouteForecast(FieldTeam team, bool fast, double now)
        {
            float cooldown = fast ? FieldCatalog.FastRouteSeconds : FieldCatalog.CoveredRouteSeconds;
            if (team.State == TeamState.Deciding && team.RouteStep < 3 &&
                !double.IsNaN(now) && !double.IsInfinity(now) && now < team.PhaseEnd)
            {
                team.Exposure = Percent(team.Exposure + RouteExposure(team, fast));
                team.Preparation = Percent(team.Preparation + (fast ? 34 : 28));
                team.RouteStep++;
            }
            return new FieldRouteForecast(team, cooldown,
                !double.IsNaN(now) && !double.IsInfinity(now) && now + cooldown < team.PhaseEnd);
        }

        public static int ExposurePerPulse(FieldTeam team)
        {
            int pressure = FieldCatalog.Pressure(team.CurrentThreat, team.CurrentRadars);
            return team.State == TeamState.Holding ? pressure == 0 ? 0 : Math.Max(1, pressure / 15) : pressure / 5;
        }

        /// <summary>Maximum remaining use window under the current verified hostile pressure.</summary>
        public static double PostPressureWindow(FieldTeam team, double now)
        {
            if (team.State != TeamState.Holding || double.IsNaN(now) || double.IsInfinity(now)) return 0;
            double remaining = Math.Max(0, team.PhaseEnd - now);
            // A mirrored undisclosed threat picture cannot support a numeric pressure claim.
            if (team.CurrentThreat == byte.MaxValue || team.CurrentRadars == byte.MaxValue) return remaining;
            int gain = ExposurePerPulse(team);
            if (team.Exposure >= 100) return 0;
            if (gain == 0) return remaining;
            int pulses = (100 - team.Exposure + gain - 1) / gain;
            double next = Math.Max(0, team.PressureAt + FieldCatalog.OrderSeconds - now);
            return Math.Min(remaining, next + (pulses - 1) * FieldCatalog.OrderSeconds);
        }

        public static string Encounter(FieldTeam team)
        {
            int kind = EncounterKind(team);
            return team.RouteStep >= 3 ? "ON OBJECTIVE / EXECUTION WINDOW" : kind == 0 ? "PATROL CROSSING"
                : kind == 1 ? "EXPOSED APPROACH" : "SERVICE ENTRANCE";
        }
        private static int EncounterKind(FieldTeam team) => (int)(((uint)team.Anchor + team.RouteStep * 5u + (uint)team.Mission) % 3);
        public static string RouteName(FieldTeam team, bool fast) => fast
            ? (EncounterKind(team) == 0 ? "CROSS IN THE GAP" : EncounterKind(team) == 1 ? "SPRINT THE OPEN GROUND" : "FORCE THE ACCESS")
            : (EncounterKind(team) == 0 ? "SHADOW THE PATROL" : EncounterKind(team) == 1 ? "TAKE THE COVERED ROUTE" : "USE THE SERVICE TUNNEL");
        public static int RouteExposure(FieldTeam team, bool fast)
        {
            int pressure = FieldCatalog.Pressure(team.CurrentThreat, team.CurrentRadars);
            int risk = fast ? 22 + pressure / 2 : 9 + pressure / 3;
            if ((team.CrewSupport & 1) != 0) risk -= EncounterKind(team) == 0 ? 16 : 8;
            if ((team.CrewSupport & 2) != 0) risk -= EncounterKind(team) == 1 ? 20 : 10;
            return Math.Max(0, risk);
        }
        public SpecOpsDenial CrewDirective(int slot, int order, double now, uint revision)
        {
            if (slot < 0 || slot >= TeamCount) return SpecOpsDenial.BadTeam;
            if (!Enabled) return SpecOpsDenial.Disabled;
            FieldTeam team = teams[slot];
            if (double.IsNaN(now) || double.IsInfinity(now) || team.State != TeamState.Deciding || now >= team.PhaseEnd)
                return SpecOpsDenial.NotAtDecision;
            if (revision != team.Revision) return SpecOpsDenial.StaleOrder;
            if (order < 5 || order > 8 || team.RouteStep >= 3) return SpecOpsDenial.BadDirective;
            if (order < 7)
            {
                int flag = order == 5 ? 1 : 2;
                if ((team.CrewSupport & flag) != 0) return SpecOpsDenial.Busy;
                team.CrewSupport |= (byte)flag;
                if (order == 5) team.Intel = Percent(team.Intel + 22);
                // Independent support contributions don't invalidate the other player's snapshot.
                teams[slot] = team;
                Notify(order == 5 ? FieldNotice.Observed : FieldNotice.Concealed, slot, (byte)team.Mission);
                return SpecOpsDenial.None;
            }
            if (now < team.OrderReadyAt) return SpecOpsDenial.OrderCoolingDown;
            bool fast = order == 7;
            FieldRouteForecast forecast = RouteForecast(team, fast, now);
            team.Exposure = (byte)forecast.Exposure;
            team.Preparation = (byte)forecast.Preparation;
            team.RouteStep = (byte)forecast.RouteStep;
            team.CrewSupport &= ReconHandoff;
            team.Revision = NextRevision();
            team.OrderReadyAt = now + forecast.CooldownSeconds;
            teams[slot] = team;
            Notify(FieldNotice.Advanced, slot, (byte)team.Mission);
            if (team.Exposure >= 100) Withdraw(slot, now);
            return SpecOpsDenial.None;
        }
    }
}
