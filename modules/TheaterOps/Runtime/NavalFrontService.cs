using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Modules.TheaterOps.Configuration;
using BoscaliSummer.Modules.TheaterOps.Domain;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Core.Game;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.TheaterOps.Runtime
{
    /// <summary>Host-only destinations for uncommanded ships. ShipAI still owns combat,
    /// pathfinding, steering and the response to explicit UnitCommand orders.</summary>
    internal sealed class NavalFrontService : MonoBehaviour, ISceneService
    {
        internal static NavalFrontService Active { get; private set; }

        private const int MaximumFactions = 8;
        private const int MaximumShips = 32;
        private const float TaskSeconds = 120f;
        private const float RouteSeconds = 30f;
        private const float MaximumSeaLaneOffset = 8000f;

        private sealed class Task
        {
            internal string Key;
            internal GlobalPosition Position;
            internal NavalRole Role;
            internal float Until;
        }

        private readonly Dictionary<FactionHQ, Task> tasks = new Dictionary<FactionHQ, Task>(MaximumFactions);
        private readonly Dictionary<Ship, float> nextRoute = new Dictionary<Ship, float>(MaximumShips);
        private TheaterOpsSettings settings;

        internal void Configure(TheaterOpsSettings config) => settings = config;

        private void Awake() => Active = this;

        private void OnDestroy()
        {
            if (ReferenceEquals(Active, this)) Active = null;
            ResetForScene();
        }

        public void ResetForScene()
        {
            tasks.Clear();
            nextRoute.Clear();
        }

        /// <summary>Refresh at each host review. The task expires if its operation stops reviewing.</summary>
        internal void SetObjective(FactionHQ hq, string key, GlobalPosition position, NavalRole role)
        {
            if (!Enabled || hq == null || hq.faction == null || string.IsNullOrEmpty(key) ||
                !Finite(position.x) || !Finite(position.z)) return;
            if (!tasks.TryGetValue(hq, out Task task))
            {
                if (tasks.Count >= MaximumFactions)
                {
                    FactionHQ expired = null;
                    foreach (KeyValuePair<FactionHQ, Task> entry in tasks)
                        if (entry.Key == null || entry.Value.Until <= Time.timeSinceLevelLoad)
                        { expired = entry.Key; break; }
                    if (!ReferenceEquals(expired, null)) tasks.Remove(expired);
                    if (tasks.Count >= MaximumFactions) return;
                }
                task = new Task();
                tasks.Add(hq, task);
            }
            task.Key = key;
            task.Position = position;
            task.Role = role;
            task.Until = Time.timeSinceLevelLoad + TaskSeconds;
        }

        internal void ClearObjective(FactionHQ hq, string key = null)
        {
            if (hq != null && tasks.TryGetValue(hq, out Task task) &&
                (key == null || string.Equals(task.Key, key, StringComparison.Ordinal)))
                tasks.Remove(hq);
        }

        internal bool TryRoute(Ship ship, bool commanded, bool holding, bool inCombat,
            out GlobalPosition destination)
        {
            destination = default;
            bool host = ship != null && ship.IsServer && !ship.disabled && Enabled;
            FactionHQ hq = host ? ship.NetworkHQ : null;
            Task task = null;
            bool hasTask = hq != null && tasks.TryGetValue(hq, out task) &&
                task.Until > Time.timeSinceLevelLoad;
            if (!NavalTask.CanRedirect(host, commanded, holding, inCombat, hasTask)) return false;
            float now = Time.timeSinceLevelLoad;
            if (nextRoute.TryGetValue(ship, out float next) && now < next) return false;
            if (nextRoute.Count >= MaximumShips && !nextRoute.ContainsKey(ship))
            {
                // Ships despawn during missions; reclaim their entries before refusing new ones.
                var stale = new List<Ship>();
                foreach (KeyValuePair<Ship, float> entry in nextRoute)
                    if (entry.Key == null || now - entry.Value > TaskSeconds) stale.Add(entry.Key);
                foreach (Ship old in stale) nextRoute.Remove(old);
                if (nextRoute.Count >= MaximumShips) return false;
            }

            NavalTask.Position(task.Role, task.Position.x, task.Position.z,
                ship.GetInstanceID(), (int)(now / 120f), out float x, out float z);
            GlobalPosition requested = new GlobalPosition(x, task.Position.y, z);
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            RoadPathfinding.RoadNetwork seaLanes = level == null ? null : level.seaLanes;
            if (seaLanes == null || !seaLanes.TryGetNearestPoint(requested,
                    out GlobalPosition snapped, out _) ||
                !Finite(snapped.x) || !Finite(snapped.z) ||
                FastMath.Distance(requested, snapped) > MaximumSeaLaneOffset)
                return false;

            destination = snapped;
            nextRoute[ship] = now + RouteSeconds;
            return true;
        }

        private bool Enabled => settings != null && settings.Enabled.Value && GameAccess.IsServer();

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    [HarmonyPatch(typeof(ShipAI), "ChooseTarget")]
    internal static class NavalFrontChooseTargetPatch
    {
        private static readonly MethodInfo SetDestination =
            AccessTools.Method(typeof(ShipAI), "SetDestination", new[] { typeof(GlobalPosition) });
        private static bool failed;

        [HarmonyPostfix]
        private static void Postfix(ShipAI __instance, Ship ___ship,
            bool ___commandedDestination, Unit ___currentTarget)
        {
            NavalFrontService service = NavalFrontService.Active;
            if (failed || SetDestination == null || ___ship == null || service == null ||
                ___ship.weaponStations == null || ___ship.weaponStations.Count == 0 ||
                __instance.state == ShipAI.ShipAIState.launching ||
                __instance.state == ShipAI.ShipAIState.returning ||
                __instance.state == ShipAI.ShipAIState.docking ||
                __instance.state == ShipAI.ShipAIState.docked ||
                __instance.state == ShipAI.ShipAIState.unloading ||
                __instance.state == ShipAI.ShipAIState.landing ||
                !service.TryRoute(___ship, ___commandedDestination,
                    ___ship.holdPosition, ___currentTarget != null,
                    out GlobalPosition destination)) return;
            try
            {
                SetDestination.Invoke(__instance, new object[] { destination });
                // SetDestination changes the path only. Steer applies throttle only outside holding;
                // a stale attacking state can also stop at its old standoff distance.
                __instance.state = ShipAI.ShipAIState.navigating;
            }
            catch (Exception exception)
            {
                failed = true;
                Debug.LogWarning("Boscali naval routing disabled: " + exception.GetType().Name);
            }
        }
    }
}
