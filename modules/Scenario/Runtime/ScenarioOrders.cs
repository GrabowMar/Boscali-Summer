using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Scenario.Runtime
{
    /// <summary>
    /// Keeps the script's aircraft flying. The game's AI with nothing to attack falls back to landing after 15 s, and a side with no airbase of its own (PMC and
    /// Pirates on a map that gave them none, as on Tassara) has nowhere to land: <c>AIPilotLandingState</c> then ejects the pilot and the airframe goes down.
    /// So every few frames this holds that timer at zero for a scripted aircraft and writes the combat state's own <c>destination</c> field: an orbit, a
    /// formation slot on another unit, or (with no order and no airbase) a circle round where the aircraft was made. Enemies are still engaged: the combat
    /// state's targeting overrides the destination while it has one. Host only. Planes use AIPilotCombatModes; helicopters and tiltwings (the VL-49) use AIHeloCombatState, whose idle behaviour also sinks 20 m a second and steers at the map origin until given a destination.
    /// </summary>
    internal sealed class ScenarioOrders
    {
        private sealed class Order { public string Mode; public string Target; public float X, Z, Radius, Height = -1f; }

        private const float Period = 0.4f, LeadRadians = 0.7f;
        private static readonly AccessTools.FieldRef<AIPilotCombatModes, float> Idle = AccessTools.FieldRefAccess<AIPilotCombatModes, float>("timeWithoutTarget");
        private static readonly AccessTools.FieldRef<AIPilotCombatModes, Unit> Target = AccessTools.FieldRefAccess<AIPilotCombatModes, Unit>("currentTarget");
        private static readonly AccessTools.FieldRef<AIHeloCombatState, float> HeloIdle = AccessTools.FieldRefAccess<AIHeloCombatState, float>("timeWithoutTarget");
        private static readonly AccessTools.FieldRef<AIHeloCombatState, float> HeloHeight = AccessTools.FieldRefAccess<AIHeloCombatState, float>("desiredHeight");
        private static readonly AccessTools.FieldRef<AIHeloCombatState, Unit> HeloTarget = AccessTools.FieldRefAccess<AIHeloCombatState, Unit>("currentTarget");
        private static readonly AccessTools.FieldRef<PilotBaseState, GlobalPosition> Destination = AccessTools.FieldRefAccess<PilotBaseState, GlobalPosition>("destination");

        private readonly Dictionary<string, Order> orders = new Dictionary<string, Order>(StringComparer.Ordinal), defaults = new Dictionary<string, Order>(StringComparer.Ordinal);
        private readonly Func<string, Unit> find;
        private readonly Func<IEnumerable<string>> scripted;
        private float next;

        public ScenarioOrders(Func<string, Unit> find, Func<IEnumerable<string>> scripted)
        {
            this.find = find;
            this.scripted = scripted;
        }

        public void Clear() { orders.Clear(); defaults.Clear(); }

        public void Give(string unit, string mode, string target, float x, float z, float radius)
        {
            if (mode == "free") orders.Remove(unit);
            else orders[unit] = new Order { Mode = mode, Target = target, X = x, Z = z, Radius = radius };
        }

        /// <summary>The unit names that get flown: the ones with an order, and every aircraft the script made.</summary>
        public void Tick(float now)
        {
            if (now < next) return;
            next = now + Period;
            var names = new HashSet<string>(orders.Keys, StringComparer.Ordinal);
            foreach (string name in scripted()) names.Add(name);
            foreach (string name in names) Fly(name, orders.TryGetValue(name, out Order order) ? order : null);
        }

        private void Fly(string name, Order order)
        {
            if (!(find(name) is Aircraft aircraft) || aircraft.disabled || aircraft.pilots == null || aircraft.pilots.Length == 0) return;
            Pilot pilot = aircraft.pilots[0];
            if (pilot == null || aircraft.NetworkHQ == null) return;
            AIPilotCombatModes plane = pilot.currentState as AIPilotCombatModes;
            AIHeloCombatState heli = pilot.currentState as AIHeloCombatState;
            if (plane == null && heli == null) return;
            // The default for an aircraft with no order is only for a side that cannot land anywhere; with an airbase the game's own routine is fine.
            if (order == null)
            {
                if (aircraft.NetworkHQ.GetAirbases().Any()) return;
                if (!defaults.TryGetValue(name, out order))
                {
                    GlobalPosition first = aircraft.GlobalPosition();
                    defaults[name] = order = new Order { Mode = "orbit", X = first.x, Z = first.z, Radius = 3000f };
                }
            }
            GlobalPosition at = aircraft.GlobalPosition();
            if (order.Height < 0f) order.Height = Mathf.Clamp(aircraft.radarAlt, 30f, 900f);
            GlobalPosition slot;
            if (order.Mode == "follow")
            {
                if (!Slot(aircraft, order, out slot))
                {
                    // Nobody to follow any more: circle where it is.
                    order.Mode = "orbit"; order.X = at.x; order.Z = at.z; order.Radius = 3000f;
                    return;
                }
            }
            else slot = OnCircle(at, order);
            // A target in view is the combat state's to chase; only write when it has nothing of its own.
            if (plane != null)
            {
                Idle(plane) = 0f;
                if (Target(plane) == null || Target(plane).disabled) Destination(plane) = slot;
            }
            else
            {
                // A helicopter or tiltwing sinks 20 m a second with nothing to shoot at, and its first destination is the map's origin.
                HeloIdle(heli) = 0f;
                if (HeloTarget(heli) == null || HeloTarget(heli).disabled)
                {
                    Destination(heli) = slot;
                    HeloHeight(heli) = Mathf.Max(HeloHeight(heli), order.Height);
                }
            }
        }

        private static GlobalPosition OnCircle(GlobalPosition at, Order order)
        {
            float dx = at.x - order.X, dz = at.z - order.Z;
            float distance = Mathf.Sqrt(dx * dx + dz * dz);
            // Far outside the circle: head for it. Otherwise a point a little ahead along it.
            if (distance > order.Radius * 2.5f) return new GlobalPosition(order.X, at.y, order.Z);
            float angle = Mathf.Atan2(dz, dx) + LeadRadians;
            return new GlobalPosition(order.X + Mathf.Cos(angle) * order.Radius, at.y, order.Z + Mathf.Sin(angle) * order.Radius);
        }

        private bool Slot(Aircraft aircraft, Order order, out GlobalPosition slot)
        {
            slot = default;
            Unit leader = null;
            if (order.Target == "player") { if (GameManager.GetLocalAircraft(out Aircraft own)) leader = own; }
            else leader = find(order.Target);
            if (leader == null || leader.disabled) return false;
            // A slot behind the leader's wing, on alternate sides for different followers.
            float side = (aircraft.UniqueName ?? "").GetHashCode() % 2 == 0 ? 1f : -1f;
            Transform t = leader.transform;
            slot = t.GlobalPosition() + (-t.forward * 250f + t.right * side * 180f);
            return true;
        }
    }
}
