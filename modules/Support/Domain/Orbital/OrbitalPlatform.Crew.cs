using System;

namespace BoscaliSummer.Modules.Support.Domain.Orbital
{
    /// <summary>A proposed circuit's complete instrument reading. Prediction never spends energy or changes a shared project.</summary>
    internal readonly struct PlatformCircuitForecast
    {
        public readonly float Alignment, Capacitor, Heat, Quality, EnergyCost;
        public readonly PlatformWorkDenial Denial;
        public readonly bool Complete;

        public PlatformCircuitForecast(float alignment, float capacitor, float heat, float energyCost,
            PlatformWorkDenial denial, bool complete)
        {
            Alignment = alignment;
            Capacitor = capacitor;
            Heat = heat;
            Quality = OrbitalPlatform.CircuitQuality(alignment, capacitor, heat);
            EnergyCost = energyCost;
            Denial = denial;
            Complete = complete;
        }
    }

    internal sealed partial class OrbitalPlatform
    {
        // Three independently selectable circuits; 0 is open, 1..3 are routes.
        // A project revision changes only at commit/retask. Different circuits can be
        // prepared from the same snapshot by different clients without losing an order.
        public byte CrewBus { get; private set; }
        public bool ObserverPackage => (CrewBus & 128) != 0;
        public bool PackageAppliesTo(PlatformAbility ability, double now) => BoostRemaining(now) > 0 &&
            ((BoostFocus == PlatformFocus.Screen && ability == PlatformAbility.EmpBurst) ||
             (BoostFocus == PlatformFocus.Strike && ability == PlatformAbility.RodStrike));
        public bool PackageCoversTarget(float x, float z, double now)
        {
            if (!Finite(x) || !Finite(z) || !ClockValid(now)) return false;
            if (!ObserverPackage || BoostRemaining(now) <= 0) return true;
            double dx = x - SolutionX, dz = z - SolutionZ;
            return SolutionRemaining(now) > 0 && dx * dx + dz * dz <= SolutionRadius * SolutionRadius;
        }
        public int Circuit(int channel) => channel >= 0 && channel < 3 ? (CrewBus >> (channel * 2)) & 3 : 0;
        public static string CircuitName(int channel) => channel == 0 ? "GUIDANCE" : channel == 1 ? "PAYLOAD" : "THERMAL";
        public static string RouteName(int channel, int route) => channel == 0
            ? (route == 1 ? "AREA SEARCH" : route == 2 ? "PRECISION LOCK" : "OBSERVER HANDOFF")
            : channel == 1 ? (route == 1 ? "ECONOMY PULSE" : route == 2 ? "SUSTAINED PULSE" : "SURGE DISCHARGE")
            : (route == 1 ? "PASSIVE RADIATION" : route == 2 ? "ACTIVE COOLING" : "EMERGENCY QUENCH");
        public static float RouteCost(int channel, int route) => channel == 0 ? (route == 2 ? 40f : 20f)
            : channel == 1 ? (route == 1 ? 50f : route == 2 ? 100f : 160f) : (route == 1 ? 0f : route == 2 ? 35f : 65f);
        public static string RouteEffect(int channel, int route) => channel == 0
            ? (route == 1 ? "55 guidance / wide acquisition" : route == 2 ? "80 guidance / +20 heat" : "100 guidance / needs team recon")
            : channel == 1 ? (route == 1 ? "50 charge / +15 heat" : route == 2 ? "80 charge / +45 heat" : "100 charge / +75 heat")
            : (route == 1 ? "-20 heat / full capacitor" : route == 2 ? "-55 heat / -5 charge" : "-90 heat / -20 charge");

        public PlatformWorkDenial CheckCircuit(int channel, int route, int previous, int revision, double now)
        {
            if (!ClockValid(now) || channel < 0 || channel > 2 || route < 1 || route > 3 || revision != WorkRevision ||
                previous != Circuit(channel) || route == previous) return PlatformWorkDenial.Invalid;
            var denial = CheckCrewProject(now);
            if (denial != PlatformWorkDenial.None) return denial;
            if (channel == 0 && route == 3 && SolutionRemaining(now) <= 0) return PlatformWorkDenial.Incomplete;
            return Energy + 0.001f < RouteCost(channel, route) ? PlatformWorkDenial.LowEnergy : PlatformWorkDenial.None;
        }

        private PlatformWorkDenial CheckCrewProject(double now)
        {
            if (!Exists) return PlatformWorkDenial.NoPlatform;
            if (Brownout || !IsOnline(CoreCell, now)) return PlatformWorkDenial.LowEnergy;
            if (now < CycleStart || now < RetaskUntil) return PlatformWorkDenial.Holding;
            if (Focus == PlatformFocus.Survey) return PlatformWorkDenial.SurveyFocus;
            if (BoostRemaining(now) > 0) return PlatformWorkDenial.AlreadyBanked;
            ModuleKind payload = Focus == PlatformFocus.Strike ? ModuleKind.Rods : ModuleKind.Emp;
            if (!Fitted(payload)) return PlatformWorkDenial.NotFitted;
            if (!FittedOnline(payload, now)) return PlatformWorkDenial.Offline;
            return PlatformWorkDenial.None;
        }

        public PlatformWorkDenial RouteCircuit(int channel, int route, int previous, int revision, double now)
        {
            var denial = CheckCircuit(channel, route, previous, revision, now);
            if (denial != PlatformWorkDenial.None) return denial;
            Energy = Math.Max(0f, Energy - RouteCost(channel, route));
            // A request can arrive at expiry before the next host tick. New preparation
            // must never carry the previous package marker into the replicated circuit bus.
            CrewBus = (byte)((CrewBus & 63 & ~(3 << (channel * 2))) | (route << (channel * 2)));
            ReadCircuits();
            return PlatformWorkDenial.None;
        }

        private void ReadCircuits()
        {
            CircuitReadings(Circuit(0), Circuit(1), Circuit(2), out float alignment, out float capacitor, out float heat);
            Alignment = alignment;
            Capacitor = capacitor;
            Heat = heat;
        }

        private static void CircuitReadings(int guidance, int payload, int cooling,
            out float alignment, out float capacitor, out float heat)
        {
            alignment = guidance == 0 ? 0 : guidance == 1 ? 55 : guidance == 2 ? 80 : 100;
            capacitor = Math.Max(0, (payload == 0 ? 0 : payload == 1 ? 50 : payload == 2 ? 80 : 100) -
                (cooling == 2 ? 5 : cooling == 3 ? 20 : 0));
            heat = Math.Max(0, (guidance == 2 ? 20 : 0) + (payload == 0 ? 0 : payload == 1 ? 15 : payload == 2 ? 45 : 75) -
                (cooling == 0 ? 0 : cooling == 1 ? 20 : cooling == 2 ? 55 : 90));
        }

        internal static float CircuitQuality(float alignment, float capacitor, float heat) =>
            Math.Max(0f, Math.Min(1f, (alignment * 0.6f + capacitor * 0.4f) * 0.01f * (1f - heat * 0.005f)));

        public PlatformCircuitForecast ForecastCircuit(int channel, int route, double now)
        {
            int guidance = Circuit(0), payload = Circuit(1), cooling = Circuit(2);
            bool valid = channel >= 0 && channel < 3 && route >= 1 && route <= 3;
            if (valid)
            {
                if (channel == 0) guidance = route;
                else if (channel == 1) payload = route;
                else cooling = route;
            }
            CircuitReadings(guidance, payload, cooling, out float alignment, out float capacitor, out float heat);
            return new PlatformCircuitForecast(alignment, capacitor, heat, valid ? RouteCost(channel, route) : 0f,
                valid ? CheckCircuit(channel, route, Circuit(channel), WorkRevision, now) : PlatformWorkDenial.Invalid,
                guidance > 0 && payload > 0 && cooling > 0 && (guidance != 3 || SolutionRemaining(now) > 0));
        }

        public PlatformWorkDenial CheckCommitCircuits(int revision, double now)
        {
            if (revision != WorkRevision || !ClockValid(now)) return PlatformWorkDenial.Invalid;
            var denial = CheckCrewProject(now);
            if (denial != PlatformWorkDenial.None) return denial;
            if (Circuit(0) == 0 || Circuit(1) == 0 || Circuit(2) == 0 ||
                (Circuit(0) == 3 && SolutionRemaining(now) <= 0)) return PlatformWorkDenial.Incomplete;
            if (Heat > 50) return PlatformWorkDenial.TooHot;
            return CheckWork(PlatformWork.Commit, now, revision);
        }

        public PlatformWorkDenial CommitCircuits(int revision, double now)
        {
            var denial = CheckCommitCircuits(revision, now);
            if (denial != PlatformWorkDenial.None) return denial;
            bool observed = Circuit(0) == 3;
            var result = TryWork(PlatformWork.Commit, now, revision);
            if (result == PlatformWorkDenial.None)
            {
                CrewBus = observed ? (byte)128 : (byte)0;
                if (observed) boostUntil = Math.Min(boostUntil, solutionUntil);
            }
            return result;
        }
    }
}
