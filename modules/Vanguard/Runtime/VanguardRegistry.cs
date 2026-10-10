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

        // Next REMORA slot on this launcher: the first one no live drone of it holds (a lost wingman's slot refills).
        public static int NextDroneSlot(Unit launcher)
        {
            var used = new HashSet<int>();
            foreach (VanguardFlight flight in Flights.Values)
                if (flight.Role == VanguardRole.Drone && flight.Launcher == launcher) used.Add(flight.Slot);
            return FormationSlot.FirstFree(used);
        }

        public static int DroneCount(Unit launcher)
        {
            int count = 0;
            foreach (VanguardFlight flight in Flights.Values)
                if (flight.Role == VanguardRole.Drone && flight.Launcher == launcher) count++;
            return count;
        }

        // ALE-X: one decoy on the fiber per aircraft. A new deployment cuts the old one loose (it flies on, see CutKite).
        public static void CutTowed(Unit launcher)
        {
            if (launcher == null) return;
            var cut = new List<VanguardFlight>();
            foreach (VanguardFlight flight in Flights.Values)
                if (flight.Role == VanguardRole.Towed && flight.Launcher == launcher) cut.Add(flight);
            foreach (VanguardFlight flight in cut) flight.CutKite(KiteCut.Replaced);
        }

        public static void Clear() => Flights.Clear();

        private static void OnDisabled(Unit unit)
        {
            unit.onDisableUnit -= OnDisabled;
            if (unit is Missile missile) Flights.Remove(missile);
        }
    }

    /// <summary>
    /// Designation bus (radial VANGUARD: DESIGNATE / RELEASE via IDroneCommand -> VanguardNet, or the sim `strike`).
    /// Every Vanguard loadout that follows the pilot's target reads <see cref="StrikeTarget(Unit)"/>. AI launchers
    /// (no Player) designate their current target. Dead launchers and targets drop out on read.
    /// </summary>
    internal static class DroneOrders
    {
        private struct Cue
        {
            public Unit Target;
            public int Seq;
        }

        private static readonly Dictionary<Unit, Cue> Cues = new Dictionary<Unit, Cue>();
        private static readonly List<Unit> Stale = new List<Unit>();
        private static int nextSeq;

        public static Unit StrikeTarget(Unit launcher) => StrikeTarget(launcher, out _);

        /// <summary>
        /// The live designation. <paramref name="cue"/> is &gt; 0 for a pilot order and changes with every new order
        /// (even onto the same target); 0 when the target is an AI launcher's own pick.
        /// </summary>
        public static Unit StrikeTarget(Unit launcher, out int cue)
        {
            cue = 0;
            if (launcher == null) return null;
            if (Cues.TryGetValue(launcher, out Cue order))
            {
                if (Alive(launcher) && Alive(order.Target))
                {
                    cue = order.Seq;
                    return order.Target;
                }
                Cues.Remove(launcher);
            }
            // AI pilots keep their pick at the head of the weapon manager's target list (AIPilotCombatModes).
            if (!(launcher is Aircraft ai) || ai.disabled || ai.Player != null || ai.weaponManager == null) return null;
            List<Unit> picks = ai.weaponManager.GetTargetList();
            Unit pick = picks != null && picks.Count > 0 ? picks[0] : null;
            return Alive(pick) && pick.NetworkHQ != ai.NetworkHQ ? pick : null;
        }

        public static void Order(Unit launcher, Unit strikeTarget)
        {
            if (launcher == null) return;
            Stale.Clear();
            foreach (KeyValuePair<Unit, Cue> pair in Cues)
                if (!Alive(pair.Key) || !Alive(pair.Value.Target)) Stale.Add(pair.Key);
            foreach (Unit gone in Stale) Cues.Remove(gone);
            if (strikeTarget == null) Cues.Remove(launcher);
            else Cues[launcher] = new Cue { Target = strikeTarget, Seq = ++nextSeq };
        }

        public static void Clear() => Cues.Clear();

        private static bool Alive(Unit unit) => unit != null && !unit.disabled;
    }
}
