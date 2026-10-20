using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Space;
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
        private readonly Dictionary<uint, Unit> known = new Dictionary<uint, Unit>(256);
        private readonly List<uint> dropped = new List<uint>(16);
        private int unitCursor, trackCursor;

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
            if (all == null) return;
            // A mission above MaximumUnits is read one rotating slice per pass. What earlier passes found stays in `known` until that unit is seen dead, taken or an anchor,
            // never because the slice moved on, so a partial pass cannot make a node vanish (and a held one be lost). A mission that fits is read whole every pass, as before.
            int count = all.Count, span = Math.Min(count, MaximumUnits), start = count > span ? unitCursor % count : 0;
            if (count > span) unitCursor = start + span; else known.Clear();
            for (int k = 0; k < span; k++)
            {
                Unit unit = all[(start + k) % count];
                if (Candidate(unit, viewer, isAnchor, out _) && unit.persistentID.Id != 0) known[unit.persistentID.Id] = unit;
            }
            dropped.Clear();
            foreach (var pair in known)
            {
                if (!Candidate(pair.Value, viewer, isAnchor, out SourceClass cls)) { dropped.Add(pair.Key); continue; }
                GlobalPosition p = pair.Value.transform.position.ToGlobalPosition();
                byId[pair.Key] = pair.Value;
                units.Add(new SourceUnit(pair.Key, cls, (float)p.x, (float)p.z, FrontDistance(p)));
            }
            for (int i = 0; i < dropped.Count; i++) known.Remove(dropped[i]);
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

        /// <summary>An enemy radar or SAM ground unit the node builder can use (also the check a remembered unit must still pass).</summary>
        private static bool Candidate(Unit unit, FactionHQ viewer, Predicate<Unit> isAnchor, out SourceClass cls)
        {
            cls = SourceClass.Other;
            if (unit == null || unit.disabled || unit.NetworkHQ == null || unit.NetworkHQ == viewer || !(unit is GroundVehicle) ||
                (isAnchor != null && isAnchor(unit)) || !(unit.definition is VehicleDefinition definition)) return false;
            cls = Classify(unit, definition);
            return cls != SourceClass.Other;
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
            if (db == null) return;
            // Above MaximumUnits contacts the sightings are read one rotating slice per pass: a sighting missed this pass only delays a reveal, it never reveals or hides a node.
            int skip = db.Count > MaximumUnits ? trackCursor % db.Count : 0, at = 0;
            if (db.Count > MaximumUnits) trackCursor = skip + MaximumUnits;
            float nativeNow = Time.timeSinceLevelLoad;
            foreach (var pair in db)
            {
                if (at++ < skip) continue;
                if (at > skip + MaximumUnits) break;
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
