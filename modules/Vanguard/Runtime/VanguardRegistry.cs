using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Server-side table of live Vanguard missiles. Entries leave when the missile is disabled.</summary>
    internal static class VanguardRegistry
    {
        private static readonly Dictionary<Missile, VanguardFlight> Flights = new Dictionary<Missile, VanguardFlight>();

        public static bool TryGet(Missile missile, out VanguardFlight flight) => Flights.TryGetValue(missile, out flight);

        public static void Add(Missile missile, VanguardFlight flight)
        {
            Flights[missile] = flight;
            missile.onDisableUnit += OnDisabled;
        }

        // Next REMORA slot on this launcher: drones already flying for it take the inner slots.
        public static int NextDroneSlot(Unit launcher)
        {
            int used = 0;
            foreach (VanguardFlight flight in Flights.Values)
                if (flight.Role == VanguardRole.Drone && flight.Launcher == launcher) used++;
            return used;
        }

        // ALE-X: one decoy on the fiber per aircraft. A new deployment cuts the old one.
        public static void CutTowed(Unit launcher)
        {
            if (launcher == null) return;
            var cut = new List<VanguardFlight>();
            foreach (VanguardFlight flight in Flights.Values)
                if (flight.Role == VanguardRole.Towed && flight.Launcher == launcher) cut.Add(flight);
            foreach (VanguardFlight flight in cut) flight.Scuttle();
        }

        public static void Clear() => Flights.Clear();

        private static void OnDisabled(Unit unit)
        {
            unit.onDisableUnit -= OnDisabled;
            if (unit is Missile missile) Flights.Remove(missile);
        }
    }

    /// <summary>REMORA STRIKE orders keyed by launcher; no entry means SCREEN.</summary>
    internal static class DroneOrders
    {
        private static readonly Dictionary<Unit, Unit> Strikes = new Dictionary<Unit, Unit>();

        public static Unit StrikeTarget(Unit launcher) =>
            launcher != null && Strikes.TryGetValue(launcher, out Unit target) ? target : null;

        public static void Order(Unit launcher, Unit strikeTarget)
        {
            if (launcher == null) return;
            if (strikeTarget == null) Strikes.Remove(launcher);
            else Strikes[launcher] = strikeTarget;
        }

        public static void Clear() => Strikes.Clear();
    }
}
