using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Space;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Turns the real enemy world into <see cref="NodeObservation"/>s for one viewing faction (spec §1.2). Every node is built from an actual
    /// enemy unit or airbase; <see cref="NodeObservation.Sighted"/> is true only for a fresh native sighting in the viewer's own tracking
    /// database (<c>FactionHQ.trackingDatabase</c>, the same fog the SPACE feed uses), never from host truth.
    /// </summary>
    internal sealed class CyberObservations
    {
        private const int MaximumUnits = 4096, MaximumBases = 32, MaximumFresh = 512, MaximumAnchors = 16;
        private const float RelayRevealRadius = 5000f;
        private readonly List<SourceUnit> units = new List<SourceUnit>(256);
        private readonly List<SourcePoint> points = new List<SourcePoint>(32);
        private readonly List<NodeSeed> seeds = new List<NodeSeed>(64);
        private readonly List<Vector2> fresh = new List<Vector2>(MaximumFresh);
        private readonly List<Vector2> ownBases = new List<Vector2>(MaximumBases);
        private readonly Dictionary<uint, Unit> byId = new Dictionary<uint, Unit>(256);
        private readonly Dictionary<uint, bool> sightedUnits = new Dictionary<uint, bool>(256);

        /// <summary>
        /// Fills <paramref name="into"/> with the viewer's observations. <paramref name="isAnchor"/> excludes the mod's own spawned anchors from
        /// the generic RADAR / SAM classification; <paramref name="enemyUplinks"/> and <paramref name="enemyCenters"/> are the other
        /// factions' SPACE uplink and data center units (they become UPLINK and DATA CENTER nodes).
        /// </summary>
        public void Build(FactionHQ viewer, Func<FactionHQ, int> keyOf, Predicate<Unit> isAnchor, IReadOnlyList<Unit> enemyUplinks,
            IReadOnlyList<Unit> enemyCenters, List<NodeObservation> into)
        {
            into.Clear();
            units.Clear(); points.Clear(); seeds.Clear(); fresh.Clear(); ownBases.Clear(); byId.Clear(); sightedUnits.Clear();
            if (viewer == null || keyOf == null) return;
            CollectFresh(viewer);
            CollectBases(viewer);
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null || all.Count > MaximumUnits) return;
            for (int i = 0; i < all.Count; i++)
            {
                Unit unit = all[i];
                if (unit == null || unit.disabled || unit.NetworkHQ == null || unit.NetworkHQ == viewer || !(unit is GroundVehicle) ||
                    (isAnchor != null && isAnchor(unit)) || !(unit.definition is VehicleDefinition definition)) continue;
                SourceClass cls = Classify(unit, definition);
                if (cls == SourceClass.Other) continue;
                uint id = unit.persistentID.Id;
                if (id == 0 || byId.ContainsKey(id)) continue;
                GlobalPosition p = unit.transform.position.ToGlobalPosition();
                byId[id] = unit;
                units.Add(new SourceUnit(id, cls, (float)p.x, (float)p.z, FrontDistance(p)));
            }
            AddUnitPoints(enemyUplinks, NodeKind.Uplink);
            AddUnitPoints(enemyCenters, NodeKind.DataCenter);
            AddRelays(viewer);
            CyberNodeBuilder.Build(units, points, seeds);
            for (int i = 0; i < seeds.Count; i++)
            {
                NodeSeed seed = seeds[i];
                if (seed.Kind == NodeKind.Relay)
                {
                    FactionHQ baseOwner = RelayOwner(seed.Key);
                    if (baseOwner != null) into.Add(new NodeObservation(seed, keyOf(baseOwner), RelaySighted(seed), false));
                    continue;
                }
                if (!byId.TryGetValue(seed.Key, out Unit unit) || unit == null) continue;
                FactionHQ owner = unit.NetworkHQ;
                if (owner == null || owner == viewer) continue;
                // A destroyed uplink or data center dissolves its node; a radar or SAM that dies simply stops appearing (the desk treats that as lost).
                bool gone = unit.disabled || ((seed.Kind == NodeKind.Uplink || seed.Kind == NodeKind.DataCenter) && UplinkSpawner.Down(unit, owner));
                into.Add(new NodeObservation(seed, keyOf(owner), sightedUnits.ContainsKey(unit.persistentID.Id), gone));
            }
        }

        private static SourceClass Classify(Unit unit, VehicleDefinition definition)
        {
            switch (definition.vehicleType)
            {
                case VehicleType.RDR: return unit.radar != null ? SourceClass.Radar : SourceClass.Other;
                case VehicleType.R_SAM: return unit.radar != null ? SourceClass.SamRadar : SourceClass.SamLauncher;
                case VehicleType.IR_SAM: return SourceClass.SamLauncher;
                default: return SourceClass.Other;
            }
        }

        private void AddUnitPoints(IReadOnlyList<Unit> source, NodeKind kind)
        {
            for (int i = 0; source != null && i < source.Count && i < MaximumAnchors; i++)
            {
                Unit unit = source[i];
                if (unit == null || unit.persistentID.Id == 0) continue;
                GlobalPosition p = unit.transform.position.ToGlobalPosition();
                byId[unit.persistentID.Id] = unit;
                points.Add(new SourcePoint(unit.persistentID.Id, kind, (float)p.x, (float)p.z, FrontDistance(p)));
            }
        }

        private readonly Dictionary<uint, Airbase> relayBases = new Dictionary<uint, Airbase>(32);

        private void AddRelays(FactionHQ viewer)
        {
            relayBases.Clear();
            if (FactionRegistry.airbaseLookup == null || FactionRegistry.airbaseLookup.Count > 128) return;
            int taken = 0;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (airbase == null || airbase.disabled || airbase.AttachedAirbase || airbase.center == null || airbase.CurrentHQ == null || airbase.CurrentHQ == viewer) continue;
                if (++taken > MaximumBases) break;
                uint key = unchecked((uint)airbase.GetInstanceID());
                GlobalPosition p = airbase.center.position.ToGlobalPosition();
                relayBases[key] = airbase;
                points.Add(new SourcePoint(key, NodeKind.Relay, (float)p.x, (float)p.z, FrontDistance(p)));
            }
        }

        private FactionHQ RelayOwner(uint key) => relayBases.TryGetValue(key, out Airbase a) && a != null ? a.CurrentHQ : null;

        private bool RelaySighted(in NodeSeed seed)
        {
            float r2 = RelayRevealRadius * RelayRevealRadius;
            for (int i = 0; i < fresh.Count; i++)
            {
                float dx = fresh[i].x - seed.X, dz = fresh[i].y - seed.Z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        /// <summary>The viewer's fresh native sightings (any unit kind): the evidence that an airbase's neighbourhood is being watched.</summary>
        private void CollectFresh(FactionHQ viewer)
        {
            var db = viewer.trackingDatabase;
            if (db == null || db.Count > MaximumUnits) return;
            float nativeNow = Time.timeSinceLevelLoad;
            foreach (var pair in db)
            {
                TrackingInfo t = pair.Value;
                if (t == null || !SpaceRevealWindow.NativeFresh(nativeNow, t.lastSpottedTime)) continue;
                GlobalPosition p = t.lastKnownPosition;
                if (fresh.Count < MaximumFresh) fresh.Add(new Vector2((float)p.x, (float)p.z));
                sightedUnits[pair.Key.Id] = true;
            }
        }

        private void CollectBases(FactionHQ viewer)
        {
            if (FactionRegistry.airbaseLookup == null || FactionRegistry.airbaseLookup.Count > 128) return;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (airbase == null || airbase.disabled || airbase.center == null || airbase.CurrentHQ != viewer || ownBases.Count >= MaximumBases) continue;
                GlobalPosition p = airbase.center.position.ToGlobalPosition();
                ownBases.Add(new Vector2((float)p.x, (float)p.z));
            }
        }

        /// <summary>Distance to the nearest friendly airbase: the viewer's side of the map is the rear, so a node near it is near the front.</summary>
        private float FrontDistance(GlobalPosition p)
        {
            if (ownBases.Count == 0) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < ownBases.Count; i++)
            {
                float dx = ownBases[i].x - (float)p.x, dz = ownBases[i].y - (float)p.z;
                best = Mathf.Min(best, dx * dx + dz * dz);
            }
            return Mathf.Sqrt(best);
        }
    }
}
