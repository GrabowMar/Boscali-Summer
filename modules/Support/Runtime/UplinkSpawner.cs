using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>Native radar site plus a SAM and two AAA; only complete legal groups count.</summary>
    internal sealed class UplinkSpawner
    {
        private const int MaximumOwned = 64;
        internal static readonly FieldInfo CriticalPart = AccessTools.Field(typeof(UnitPart), "criticalPart");
        private readonly List<Unit> owned = new List<Unit>(MaximumOwned);
        private readonly List<Unit> pendingCleanup = new List<Unit>(MaximumOwned);
        private int serial;
        internal bool Owns(Unit unit) => unit != null && owned.Contains(unit);

        internal bool TryPlan(GlobalPosition anchor, Airbase parent, out GlobalPosition[] positions,
            out Quaternion rotation)
        {
            positions = null;
            rotation = Quaternion.identity;
            if (CriticalPart == null || !TryDefinitions(out VehicleDefinition site, out VehicleDefinition sam,
                out VehicleDefinition aaa)) return false;
            Vector3 center = anchor.ToLocalPosition();
            var definitions = new[] { site, sam, aaa, aaa };
            var offsets = new[] { Vector3.zero, new Vector3(36f, 0f, 0f),
                new Vector3(-36f, 0f, 28f), new Vector3(-36f, 0f, -28f) };
            var planned = new GlobalPosition[4];
            for (int i = 0; i < planned.Length; i++)
            {
                Vector3 desired = center + offsets[i];
                if (!Legal(desired, parent) || !GroundPlacement.TryPlace(definitions[i], desired, rotation,
                    out Vector3 point) || !Legal(point, parent)) return false;
                planned[i] = point.ToGlobalPosition();
            }
            positions = planned;
            return true;
        }

        public bool TryCreate(FactionHQ owner, int ordinal, GlobalPosition anchor, Airbase parent, out Unit uplink)
        {
            uplink = null;
            Spawner spawner = NetworkSceneSingleton<Spawner>.i;
            if (owner == null || spawner == null || !spawner.IsServer || owned.Count > MaximumOwned - 4 ||
                (parent != null && (parent.disabled || parent.CurrentHQ != owner)) ||
                !TryPlan(anchor, parent, out GlobalPosition[] positions, out Quaternion rotation) ||
                !TryDefinitions(out VehicleDefinition site, out VehicleDefinition sam, out VehicleDefinition aaa)) return false;
            var definitions = new[] { site, sam, aaa, aaa };
            var group = new List<Unit>(4);
            try
            {
                for (int i = 0; i < definitions.Length; i++)
                {
                    // Recheck immediately before spawn; other native spawners can occupy a planned footprint.
                    Vector3 raw = positions[i].ToLocalPosition() - rotation * definitions[i].spawnOffset;
                    if (!GroundPlacement.TryPlace(definitions[i], raw, rotation, out Vector3 point) || !Legal(point, parent))
                        throw new InvalidOperationException("Uplink footprint became occupied");
                    Unit unit = spawner.SpawnVehicle(definitions[i].unitPrefab, point.ToGlobalPosition(), rotation,
                        Vector3.zero, owner, SupportNaming.Prefix + "Uplink:" + owner.name + ":" + ordinal + ":" + (++serial),
                        1f, true, null);
                    if (unit == null) throw new InvalidOperationException("Native uplink group spawn refused");
                    owned.Add(unit);
                    group.Add(unit);
                    if (!unit.IsServer || unit.NetworkHQ != owner || unit.disabled || Health(unit) <= 0f)
                        throw new InvalidOperationException("Native uplink group is not operational");
                    if (unit is GroundVehicle vehicle) vehicle.SetHoldPosition(true);
                    if (i == 0) uplink = unit;
                }
                return true;
            }
            catch (Exception e)
            {
                Plugin.Logger?.LogWarning("[Support.Space] Site placement refused: " + e.Message);
                for (int i = group.Count - 1; i >= 0; i--) Remove(group[i]);
                uplink = null;
                return false;
            }
        }

        public void ResetForScene()
        {
            for (int i = owned.Count - 1; i >= 0; i--) Remove(owned[i]);
        }

        internal void RetryCleanup()
        {
            for (int i = pendingCleanup.Count - 1; i >= 0; i--) Remove(pendingCleanup[i]);
        }

        private void Remove(Unit unit)
        {
            try
            {
                if (unit != null)
                {
                    Spawner spawner = NetworkSceneSingleton<Spawner>.i;
                    try
                    {
                        unit.Networkdisabled = true;
                        unit.gameObject.SetActive(false);
                        if (spawner != null && spawner.IsServer && spawner.ServerObjectManager != null)
                            spawner.ServerObjectManager.Destroy(unit.gameObject);
                    }
                    finally
                    {
                        // Native Destroy silently returns for an unregistered NetId0 identity.
                        if (unit != null) UnityEngine.Object.Destroy(unit.gameObject);
                    }
                }
                for (int i = owned.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(owned[i], unit)) { owned.RemoveAt(i); break; }
                for (int i = pendingCleanup.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(pendingCleanup[i], unit)) { pendingCleanup.RemoveAt(i); break; }
            }
            catch (Exception e)
            {
                Retry(unit);
                Plugin.Logger?.LogWarning("[Support.Space] Native site cleanup will retry: " + e.Message);
            }
        }

        private void Retry(Unit unit)
        {
            if (!pendingCleanup.Contains(unit) && pendingCleanup.Count < MaximumOwned) pendingCleanup.Add(unit);
        }

        internal static float Health(Unit unit)
        {
            if (unit == null) return 0f;
            List<UnitPart> parts = unit.GetAllParts();
            if (parts == null || parts.Count == 0 || parts.Count > 256) return 0f;
            float total = 0f;
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] != null && !parts[i].IsDetached() && SpaceFinite(parts[i].hitPoints))
                    total += Mathf.Max(0f, parts[i].hitPoints);
            return SpaceFinite(total) ? total : 0f;
        }

        internal static bool Down(Unit unit, FactionHQ owner)
        {
            if (unit == null || unit.disabled || unit.NetworkHQ != owner || CriticalPart == null) return true;
            List<UnitPart> parts = unit.GetAllParts();
            if (parts == null || parts.Count == 0 || parts.Count > 256) return true;
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] != null && (bool)CriticalPart.GetValue(parts[i]) &&
                    (parts[i].IsDetached() || parts[i].hitPoints <= 0f)) return true;
            return Health(unit) <= 0f;
        }

        private static bool TryDefinitions(out VehicleDefinition site, out VehicleDefinition sam, out VehicleDefinition aaa)
        {
            site = Find("RadarContainer1") ?? Find("RadarSAM1"); // Free Flight disallows the container; use an honest armed radar site.
            sam = Find("RadarSAM1");
            aaa = Find("SPAAG1");
            return site != null && sam != null && aaa != null;
        }

        private static VehicleDefinition Find(string key)
        {
            List<VehicleDefinition> definitions = Encyclopedia.i?.vehicles;
            if (definitions == null) return null;
            if (definitions.Count > 256) return null;
            for (int i = 0; i < definitions.Count && i < 256; i++)
            {
                VehicleDefinition definition = definitions[i];
                if (definition != null && definition.jsonKey == key && GroundPlacement.Usable(definition) &&
                    definition.unitPrefab.GetComponent<GroundVehicle>() != null) return definition;
            }
            return null;
        }

        internal static bool Legal(Vector3 local, Airbase parent)
        {
            Vector2 span = TheaterFrame.Resolve();
            GlobalPosition global = local.ToGlobalPosition();
            float diagonal = span.magnitude;
            float exclusion = Mathf.Clamp(500f * Mathf.Clamp(diagonal / 150000f, 0.4f, 2.5f), 250f, 1000f);
            if (!SpaceFinite(local.x) || !SpaceFinite(local.y) || !SpaceFinite(local.z) ||
                Math.Abs(global.x) > span.x * 0.5f - exclusion || Math.Abs(global.z) > span.y * 0.5f - exclusion) return false;
            if (FactionRegistry.airbaseLookup != null)
            {
                if (FactionRegistry.airbaseLookup.Count > 128) return false;
                int examined = 0;
                foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
                {
                    if (++examined > 128) return false;
                    if (airbase == null || airbase.center == null || airbase.AttachedAirbase) continue;
                    float radius = airbase.GetRadius() + exclusion;
                    if (HorizontalSquared(local, airbase.center.position) < radius * radius) return false;
                    if (airbase.runways == null) continue;
                    if (airbase.runways.Length > 32) return false;
                    for (int i = 0; i < airbase.runways.Length && i < 32; i++)
                    {
                        Airbase.Runway runway = airbase.runways[i];
                        if (runway?.Start == null || runway.End == null) continue;
                        Vector3 a = runway.Start.position, b = runway.End.position;
                        Vector3 delta = b - a; delta.y = 0f;
                        float t = delta.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(local - a, delta) / delta.sqrMagnitude) : 0f;
                        float clearance = runway.GetWidth() * 0.5f + exclusion;
                        if (HorizontalSquared(local, a + delta * t) < clearance * clearance) return false;
                    }
                }
            }
            // Native aircraft origins keep parking/spawn approaches clear, including unattached field origins.
            List<Unit> units = UnitRegistry.allUnits;
            if (units != null && units.Count > 4096) return false;
            if (units != null)
                for (int i = 0; i < units.Count && i < 4096; i++)
                    if (units[i] is Aircraft aircraft && HorizontalSquared(local, aircraft.startPosition.ToLocalPosition()) < exclusion * exclusion)
                        return false;
            var hqs = FactionRegistry.GetAllHQs();
            if (hqs != null)
            {
                if (hqs.Count > 8) return false;
                foreach (FactionHQ hq in hqs)
                {
                    if (hq == null) continue;
                    var zones = hq.GetExclusionZones(); // Native nuclear-launch exclusion, additional to our capture/runway rules.
                    if (zones == null) continue;
                    if (zones.Count > 128) return false;
                    for (int i = 0; i < zones.Count && i < 128; i++)
                        if (HorizontalSquared(local, zones[i].position.ToLocalPosition()) < zones[i].radius * zones[i].radius) return false;
                }
            }
            return true;
        }

        private static float HorizontalSquared(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return x * x + z * z;
        }
        private static bool SpaceFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
