using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// Turns the real enemy world into what the SOF desk needs for one faction: the scene counts around a point (exposure and odds) and the mission target seeds.
    /// Every target is a real enemy unit, building or airbase; <see cref="SofSeed.Sighted"/> is true only for a fresh native sighting in the viewer's own
    /// <c>trackingDatabase</c> (the same fog the SPACE feed and the CYBER nodes use), never host truth. The scene counts are host-only numbers the client never sees.
    /// </summary>
    internal sealed class SofObservations
    {
        private const int MaximumUnits = 4096, MaximumFresh = 512, MaximumEnemies = 1024, MaximumBases = 32, GroundTargets = 12, BuildingTargets = 8, RelayTargets = 4;
        private const float RelayRevealRadius = 5000f, BuildingBaseRadius = 4000f;
        private struct Enemy { public float X, Z; public bool Armored; }
        private readonly List<Enemy> enemies = new List<Enemy>(256);
        private readonly List<Vector2> fresh = new List<Vector2>(MaximumFresh);
        private readonly List<Vector2> ownBases = new List<Vector2>(MaximumBases);
        private readonly List<Vector2> enemyBases = new List<Vector2>(MaximumBases);
        private readonly Dictionary<uint, Unit> byKey = new Dictionary<uint, Unit>(64);
        private readonly Dictionary<uint, Airbase> relays = new Dictionary<uint, Airbase>(8);
        private readonly HashSet<uint> sighted = new HashSet<uint>();
        private readonly List<SofSeed> grounds = new List<SofSeed>(64), buildings = new List<SofSeed>(32), kept = new List<SofSeed>(8);
        private float enemiesAt = -100f;

        /// <summary>Rebuilds the enemy ground list at most once a second per faction (the scene queries are cheap lookups on it).</summary>
        public void Snapshot(FactionHQ viewer, float now)
        {
            if (viewer == null || now - enemiesAt < 1f) return;
            enemiesAt = now;
            enemies.Clear();
            List<Unit> all = UnitRegistry.allUnits;
            if (all == null || all.Count > MaximumUnits) return;
            for (int i = 0; i < all.Count && enemies.Count < MaximumEnemies; i++)
            {
                Unit unit = all[i];
                if (unit == null || unit.disabled || unit.NetworkHQ == null || unit.NetworkHQ == viewer || !(unit is GroundVehicle)) continue;
                GlobalPosition p = unit.transform.position.ToGlobalPosition();
                bool armored = unit.definition is VehicleDefinition d && (d.vehicleType == VehicleType.AFV || d.vehicleType == VehicleType.MBT);
                enemies.Add(new Enemy { X = (float)p.x, Z = (float)p.z, Armored = armored });
            }
        }

        /// <summary>Enemy counts around a point from the last snapshot. <paramref name="stared"/> is the caller's own knowledge of an enemy bird looking there.</summary>
        public SofScene Scene(float x, float z, bool stared)
        {
            int w300 = 0, w1000 = 0, w2000 = 0, armored = 0;
            for (int i = 0; i < enemies.Count; i++)
            {
                float d = SofRules.Distance(enemies[i].X, enemies[i].Z, x, z);
                if (d > SofRules.ExposureRadius) continue;
                w2000++;
                if (d <= 1000f) { w1000++; if (enemies[i].Armored) armored++; }
                if (d <= 300f) w300++;
            }
            return new SofScene(w300, w1000, w2000, armored, stared);
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the viewer's target seeds. <paramref name="isAnchor"/> excludes the mod's own anchors from the generic ground list.
        /// <paramref name="keep"/> are the unit keys a team is engaged on or a held building stands on: they keep resolving (and keep a seed, <see cref="SofSeed.Sighted"/>
        /// false once the sighting lapses) until the unit is dead or disabled, so "alive" never depends on the fog.
        /// </summary>
        public void Build(FactionHQ viewer, Predicate<Unit> isAnchor, IReadOnlyList<KeyValuePair<AnchorSub, Unit>> enemyAnchors, HashSet<uint> keep, List<SofSeed> into)
        {
            List<Unit> all = UnitRegistry.allUnits;
            if (viewer == null) { into.Clear(); return; }
            if (all == null || all.Count > MaximumUnits) return; // too big to scan: keep the last map and seeds rather than dropping every engaged target
            into.Clear(); byKey.Clear(); relays.Clear(); sighted.Clear(); fresh.Clear(); ownBases.Clear(); enemyBases.Clear(); grounds.Clear(); buildings.Clear(); kept.Clear();
            CollectFresh(viewer);
            CollectBases(viewer);
            for (int i = 0; i < all.Count; i++)
            {
                Unit unit = all[i];
                if (unit == null || unit.disabled || unit.NetworkHQ == null || unit.NetworkHQ == viewer || unit is Aircraft || unit is Missile) continue;
                uint id = unit.persistentID.Id;
                if (id == 0 || (isAnchor != null && isAnchor(unit))) continue;
                bool isSighted = sighted.Contains(id), engagedKey = keep != null && keep.Contains(id);
                if (!isSighted && !engagedKey) continue;
                GlobalPosition p = unit.transform.position.ToGlobalPosition();
                if (unit is GroundVehicle)
                {
                    byKey[id] = unit;
                    var seed = new SofSeed(TargetKind.Ground, AnchorSub.Uplink, id, (float)p.x, (float)p.z, FrontDistance((float)p.x, (float)p.z), isSighted, false);
                    if (engagedKey) kept.Add(seed); else grounds.Add(seed);
                }
                else if (unit is Building building && !(building.definition is BuildingDefinition def && def.buildingType == BuildingType.CIV) && (engagedKey || NearEnemyBase((float)p.x, (float)p.z)))
                {
                    byKey[id] = unit;
                    var seed = new SofSeed(TargetKind.Building, AnchorSub.Uplink, id, (float)p.x, (float)p.z, FrontDistance((float)p.x, (float)p.z), isSighted, false);
                    if (engagedKey) kept.Add(seed); else buildings.Add(seed);
                }
            }
            grounds.Sort((a, b) => a.Front.CompareTo(b.Front));
            buildings.Sort((a, b) => a.Front.CompareTo(b.Front));
            for (int i = 0; i < grounds.Count && i < GroundTargets; i++) into.Add(grounds[i]);
            for (int i = 0; i < buildings.Count && i < BuildingTargets; i++) into.Add(buildings[i]);
            for (int i = 0; i < kept.Count; i++) into.Add(kept[i]); // engaged and held units are never cut by the list caps
            for (int i = 0; enemyAnchors != null && i < enemyAnchors.Count && i < 16; i++)
            {
                Unit unit = enemyAnchors[i].Value;
                if (unit == null || unit.persistentID.Id == 0) continue;
                GlobalPosition p = unit.transform.position.ToGlobalPosition();
                uint id = unit.persistentID.Id;
                byKey[id] = unit;
                bool gone = unit.disabled || UplinkSpawner.Down(unit, unit.NetworkHQ); // a DOWN camp is no more a target than a DOWN truck
                into.Add(new SofSeed(TargetKind.Anchor, enemyAnchors[i].Key, id, (float)p.x, (float)p.z, FrontDistance((float)p.x, (float)p.z), sighted.Contains(id), gone));
            }
            AddRelays(viewer, into);
        }

        /// <summary>The target still stands (its unit or airbase exists and is not destroyed).</summary>
        public bool Alive(TargetKind kind, uint key)
        {
            if (kind == TargetKind.Relay) return relays.TryGetValue(key, out Airbase a) && a != null && !a.disabled;
            return byKey.TryGetValue(key, out Unit unit) && unit != null && !unit.disabled;
        }

        public bool TryUnit(uint key, out Unit unit) => byKey.TryGetValue(key, out unit) && unit != null;

        private void AddRelays(FactionHQ viewer, List<SofSeed> into)
        {
            if (FactionRegistry.airbaseLookup == null || FactionRegistry.airbaseLookup.Count > 128) return;
            int taken = 0;
            float r2 = RelayRevealRadius * RelayRevealRadius;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (airbase == null || airbase.disabled || airbase.AttachedAirbase || airbase.center == null || airbase.CurrentHQ == null || airbase.CurrentHQ == viewer) continue;
                if (++taken > MaximumBases || relays.Count >= RelayTargets) break;
                GlobalPosition p = airbase.center.position.ToGlobalPosition();
                bool seen = false;
                for (int i = 0; i < fresh.Count && !seen; i++)
                {
                    float dx = fresh[i].x - (float)p.x, dz = fresh[i].y - (float)p.z;
                    seen = dx * dx + dz * dz <= r2;
                }
                uint key = unchecked((uint)airbase.GetInstanceID());
                relays[key] = airbase;
                into.Add(new SofSeed(TargetKind.Relay, AnchorSub.Uplink, key, (float)p.x, (float)p.z, FrontDistance((float)p.x, (float)p.z), seen, false));
            }
        }

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
                sighted.Add(pair.Key.Id);
            }
        }

        private void CollectBases(FactionHQ viewer)
        {
            if (FactionRegistry.airbaseLookup == null || FactionRegistry.airbaseLookup.Count > 128) return;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (airbase == null || airbase.disabled || airbase.AttachedAirbase || airbase.center == null || airbase.CurrentHQ == null) continue;
                GlobalPosition p = airbase.center.position.ToGlobalPosition();
                if (airbase.CurrentHQ == viewer) { if (ownBases.Count < MaximumBases) ownBases.Add(new Vector2((float)p.x, (float)p.z)); }
                else if (enemyBases.Count < MaximumBases) enemyBases.Add(new Vector2((float)p.x, (float)p.z));
            }
        }

        private bool NearEnemyBase(float x, float z)
        {
            float r2 = BuildingBaseRadius * BuildingBaseRadius;
            for (int i = 0; i < enemyBases.Count; i++)
            {
                float dx = enemyBases[i].x - x, dz = enemyBases[i].y - z;
                if (dx * dx + dz * dz <= r2) return true;
            }
            return false;
        }

        private float FrontDistance(float x, float z)
        {
            if (ownBases.Count == 0) return 0f;
            float best = float.MaxValue;
            for (int i = 0; i < ownBases.Count; i++)
            {
                float dx = ownBases[i].x - x, dz = ownBases[i].y - z;
                best = Mathf.Min(best, dx * dx + dz * dz);
            }
            return Mathf.Sqrt(best);
        }
    }
}
