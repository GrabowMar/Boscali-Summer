using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.ExceptionServices;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Spooktober.Domain;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>Temporary, reversible occupation: win the ground fight, corrupt the base, keep a garrison to hold it.</summary>
    internal static class HellTerritory
    {
        internal sealed class Zone
        {
            internal Airbase Base;
            internal GlobalPosition Center;
            internal GlobalPosition Position => Center;
            internal float Radius, Progress;
            internal bool Controlled;
            internal string Label;
            internal FactionHQ Original;
            internal float LastGarrison;
            internal GameObject Ground;
            internal Mesh Mesh;
            internal Material Material;
            internal float DrawnRadius;
            internal HellGrowth.Handle Growth;
            internal int GrowthRevision;
            internal HellInfection Infection;
            internal float InfectionBorn;
            internal int MeshBuilds;
        }

        private const int MaxZones = 32, MaxDressed = 64;
        private static readonly FieldInfo DepotList = AccessTools.Field(typeof(FactionHQ), "depotSorted");
        private static readonly List<Zone> Active = new List<Zone>();
        private static readonly Dictionary<Airbase, Zone> Bases = new Dictionary<Airbase, Zone>();
        private static readonly Dictionary<Airbase, FactionHQ> Owners = new Dictionary<Airbase, FactionHQ>();
        private static readonly Dictionary<Building, RadGhostLook.Handle> Dressed = new Dictionary<Building, RadGhostLook.Handle>();
        private static readonly List<Building> Released = new List<Building>();
        private static readonly RadGhostLook.Profile StructureLook = new RadGhostLook.Profile
            { Hell = true, NoiseScale = .07f, FarDistance = 12000f, EmberRate = 14f, EmberScale = 3f };
        private static Zone breach;
        private static float next, previous;
        private static int infectionMeshBuilds;

        internal static IReadOnlyList<Zone> Zones => Active;
        internal static int Count { get; private set; }
        internal static int Captured { get; private set; }
        internal static int Liberated { get; private set; }
        internal static int Textured => Dressed.Count;
        internal static int InfectionFields
        {
            get { int count = 0; foreach (Zone zone in Active) if (zone.Infection != null && zone.Ground != null) count++; return count; }
        }
        internal static int InfectionGrowing
        {
            get { int count = 0; foreach (Zone zone in Active) if (zone.Infection != null && Age(zone) < HellInfection.Duration) count++; return count; }
        }
        internal static int InfectionMeshBuilds => infectionMeshBuilds;
        internal static float BreachAge => Age(breach);
        internal static bool BreachGrowing => breach?.Infection != null && BreachAge < HellInfection.Duration;
        internal static float BreachRadius => breach?.Radius ?? 0f;
        internal static int BreachMeshBuilds => breach?.MeshBuilds ?? 0;
        internal static int BreachMeshId => breach?.Mesh != null ? breach.Mesh.GetInstanceID() : 0;
        internal static float BreachCoverage
        {
            get
            {
                if (breach?.Infection == null) return 0f;
                float sum = 0f, age = BreachAge;
                for (int z = 0; z < HellInfection.Side; z++)
                    for (int x = 0; x < HellInfection.Side; x++)
                    {
                        var sample = breach.Infection.Sample(x * 2f / (HellInfection.Side - 1) - 1f, z * 2f / (HellInfection.Side - 1) - 1f);
                        float shown = Mathf.Clamp01((age - sample.Arrival * (HellInfection.Duration - 2f)) * .5f);
                        sum += sample.Coverage * shown * shown * (3f - 2f * shown);
                    }
                return sum / (HellInfection.Side * HellInfection.Side);
            }
        }
        private static float Age(Zone zone) => zone?.Infection == null ? 0f : HellInfection.Phase(Time.time, zone.InfectionBorn) * HellInfection.Duration;

        internal static void Frame(float now)
        {
            if (now < next || !GameAccess.IsServer() || !HellGate.Alive || HellSide.Side.HQ == null) return;
            float elapsed = previous > 0f ? Mathf.Clamp(now - previous, 0f, 2f) : 1f;
            previous = now;
            next = now + 1f;
            Breach();
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (airbase == null || airbase.center == null || airbase.AttachedAirbase) continue;
                if (!HellSide.Side.Owns(airbase.CurrentHQ)) Owners[airbase] = airbase.CurrentHQ;
                float radius = Mathf.Clamp(airbase.GetRadius(), 650f, 1600f);
                Presence(airbase.center.position, radius, out int attackers, out int defenders);
                if (!Bases.TryGetValue(airbase, out Zone zone))
                {
                    if (attackers == 0 && !HellSide.Side.Owns(airbase.CurrentHQ) || Active.Count >= MaxZones) continue;
                    Owners.TryGetValue(airbase, out FactionHQ original);
                    zone = new Zone { Base = airbase, Original = original, Label = airbase.name,
                        Radius = radius, Center = airbase.center.position.ToGlobalPosition(), LastGarrison = now };
                    Bases.Add(airbase, zone);
                    Active.Add(zone);
                }
                zone.Center = airbase.center.position.ToGlobalPosition();
                bool owned = HellSide.Side.Owns(airbase.CurrentHQ);
                if (zone.Controlled && !owned) Release(zone, false); // native recapture already chose its new owner
                if (owned && !zone.Controlled)
                {
                    ReconcileDepots(zone);
                    zone.Controlled = true;
                    zone.Progress = 1f;
                    zone.LastGarrison = now;
                }
                if (attackers > 0) zone.LastGarrison = now;
                zone.Progress = HellControl.Advance(zone.Progress, attackers, defenders, elapsed);
                if (!zone.Controlled && attackers > defenders && zone.Progress >= .999f)
                {
                    zone.Original = airbase.CurrentHQ;
                    try
                    {
                        ((ICapturable)airbase).OnCapture(HellSide.Side.HQ);
                        ReconcileDepots(zone);
                    }
                    catch
                    {
                        // Native capture changes CurrentHQ before notifying buildings; callbacks can fail after that change.
                        try { Restore(zone); }
                        catch (Exception rollback) { Plugin.Logger.LogError("[Spooktober] Hell capture rollback " + zone.Label + ": " + rollback); }
                        zone.Controlled = false;
                        zone.Progress = 0f;
                        throw; // preserve the original native failure for the director and its normal cleanup
                    }
                    if (HellSide.Side.Owns(airbase.CurrentHQ))
                    {
                        zone.Controlled = true;
                        zone.Progress = 1f;
                        Captured++;
                        HellArmy.RecolourIcons();
                        FallenAce.Announce("HELL CLAIMS " + zone.Label.ToUpperInvariant());
                    }
                }
                if (HellControl.Lost(zone.Controlled, attackers, defenders, now - zone.LastGarrison, zone.Progress))
                    Release(zone, true);
                if (zone.Controlled) Ground(zone);
            }
            Count = 0;
            for (int i = Active.Count - 1; i >= 0; i--)
            {
                Zone zone = Active[i];
                if (zone == breach) continue;
                if (zone.Base == null || !zone.Controlled && zone.Progress <= 0f && now - zone.LastGarrison > 2f)
                {
                    RemoveGround(zone);
                    if (zone.Base != null) Bases.Remove(zone.Base);
                    Active.RemoveAt(i);
                    continue;
                }
                if (zone.Controlled) Count++;
            }
            DressStructures();
        }

        private static void Breach()
        {
            HellPortal portal = HellGate.Portal;
            if (portal == null) return;
            if (breach == null)
            {
                breach = new Zone { Label = "HELL BREACH", Controlled = true, Progress = 1f, Radius = 760f };
                Active.Add(breach);
            }
            breach.Center = portal.Root.transform.position.ToGlobalPosition();
            Ground(breach);
        }

        private static void Presence(Vector3 center, float radius, out int attackers, out int defenders)
        {
            attackers = defenders = 0;
            float square = radius * radius;
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is GroundVehicle) || unit.disabled || unit.NetworkHQ == null) continue;
                Vector3 delta = unit.transform.position - center;
                if (Mathf.Abs(delta.y) > 120f) continue; // the pocket dimension above a base has its own ground forces
                delta.y = 0f;
                if (delta.sqrMagnitude > square) continue;
                if (HellSide.Side.Owns(unit.NetworkHQ)) attackers++;
                else defenders++;
            }
        }

        private static void Release(Zone zone, bool restore)
        {
            if (restore) Restore(zone);
            else ReconcileDepots(zone); // another faction may have recaptured it through the native capture service
            if (zone.Controlled)
            {
                Liberated++;
                FallenAce.Announce("THE CORRUPTION RECEDES: " + zone.Label.ToUpperInvariant());
            }
            zone.Controlled = false;
            zone.Progress = 0f;
            RemoveGround(zone);
            HellArmy.RecolourIcons();
        }

        private static void Restore(Zone zone)
        {
            if (zone.Base == null) return;
            Exception failure = null;
            if (HellSide.Side.Owns(zone.Base.CurrentHQ))
                try { ((ICapturable)zone.Base).OnCapture(zone.Original); }
                catch (Exception error) { failure = error; }
            // A null owner returns before native building assignment. Also finish partial rollback if a building callback failed.
            FactionHQ owner = zone.Base.CurrentHQ;
            if (HellSide.Side.Owns(owner)) owner = zone.Original;
            if (zone.Base.buildings != null)
                foreach (Building building in zone.Base.buildings)
                    if (building != null && HellSide.Side.Owns(building.NetworkHQ))
                        try { RestoreBuilding(building, owner); }
                        catch (Exception error) { if (failure == null) failure = error; }
            try { ReconcileDepots(zone); }
            catch (Exception error) { if (failure == null) failure = error; }
            if (failure != null) ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static void RestoreBuilding(Building building, FactionHQ owner)
        {
            if (owner != null) { building.NetworkHQ = owner; return; }
            FactionHQ previousOwner = building.NetworkHQ;
            // Native Building.HQChanged dereferences the new HQ while damaged, even when restoring a neutral place.
            // Keep the damage state, but suppress that invalid notification during the synchronous null-owner assignment.
            bool damaged = building.needsRepair;
            building.needsRepair = false;
            try { building.NetworkHQ = null; }
            finally { building.needsRepair = damaged; }
            if (building.NetworkHQ == null && previousOwner != null)
            {
                previousOwner.NotifyRepaired(building);
                // Unit.HQChanged(null) skips faction callbacks; native rearmer searches do not recheck ownership.
                foreach (Rearmer rearmer in building.GetComponentsInChildren<Rearmer>(true))
                    previousOwner.RearmMissionController?.DeregisterRearmer(rearmer);
            }
        }

        private static List<(VehicleDepot depot, float distance)> Depots(FactionHQ hq)
        {
            if (DepotList == null) throw new MissingFieldException(typeof(FactionHQ).FullName, "depotSorted");
            return DepotList.GetValue(hq) as List<(VehicleDepot depot, float distance)> ??
                throw new InvalidOperationException("FactionHQ.depotSorted has an unsupported type");
        }

        private static HashSet<FactionHQ> DepotOwners()
        {
            var owners = new HashSet<FactionHQ>();
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs()) if (hq != null) owners.Add(hq);
            if (HellSide.Side.HQ != null) owners.Add(HellSide.Side.HQ);
            if (GhostSide.Side.HQ != null) owners.Add(GhostSide.Side.HQ);
            foreach (Zone zone in Active)
            {
                if (zone.Original != null) owners.Add(zone.Original);
                if (zone.Base?.buildings == null) continue;
                foreach (Building building in zone.Base.buildings)
                    if (building != null && building is VehicleDepot && building.NetworkHQ != null) owners.Add(building.NetworkHQ);
            }
            return owners;
        }

        private static void ReconcileDepots(Zone zone)
        {
            if (zone.Base?.buildings == null) return;
            bool hasDepot = false;
            foreach (Building building in zone.Base.buildings) if (building is VehicleDepot) { hasDepot = true; break; }
            if (!hasDepot) return;
            // Native HQChanged does not move depotSorted; otherwise the old side spends its supply spawning Hell vehicles.
            // Resolve every list before mutation so a game-layout mismatch leaves existing registrations intact for rollback.
            var lists = new List<List<(VehicleDepot depot, float distance)>>();
            foreach (FactionHQ hq in DepotOwners()) lists.Add(Depots(hq));
            foreach (Building building in zone.Base.buildings)
                if (building is VehicleDepot depot && depot != null)
                {
                    foreach (List<(VehicleDepot depot, float distance)> list in lists) list.RemoveAll(entry => entry.depot == depot);
                    if (depot.NetworkHQ != null) depot.NetworkHQ.AddDepot(depot);
                }
        }

        internal static void DepotReadout(out int owned, out int stale)
        {
            owned = stale = 0;
            var counted = new HashSet<VehicleDepot>();
            foreach (FactionHQ hq in DepotOwners())
                foreach ((VehicleDepot depot, float distance) entry in Depots(hq))
                {
                    VehicleDepot depot = entry.depot;
                    if (depot == null) continue;
                    if (depot.NetworkHQ != hq) stale++;
                    else if (HellSide.Side.Owns(hq) && counted.Add(depot)) owned++;
                }
        }

        private static void DressStructures()
        {
            Released.Clear();
            foreach (KeyValuePair<Building, RadGhostLook.Handle> pair in Dressed)
                if (pair.Key == null || !HellSide.Side.Owns(pair.Key.NetworkHQ) || !pair.Value.Alive) Released.Add(pair.Key);
            foreach (Building building in Released)
            {
                Dressed[building].Remove();
                Dressed.Remove(building);
            }
            foreach (Unit unit in UnitRegistry.allUnits)
            {
                if (!(unit is Building building) || building.disabled || !HellSide.Side.Owns(building.NetworkHQ) || Dressed.ContainsKey(building)) continue;
                if (Dressed.Count >= MaxDressed) break;
                RadGhostLook.Handle look = RadGhostLook.Apply(building.gameObject, StructureLook);
                if (look != null) Dressed.Add(building, look);
            }
        }

        /// <summary>Build once. Ant arrival/coverage is vertex data; the shader owns the finite reveal.</summary>
        private static void Ground(Zone zone)
        {
            if (zone.Ground != null) return;
            Material ground = HellGroundSurface.InfectionMaterial();
            if (ground == null || Datum.origin == null) return;
            if (zone.Infection == null)
            {
                int seed = unchecked(Mathf.RoundToInt(zone.Center.x * .05f) * 73856093 ^ Mathf.RoundToInt(zone.Center.z * .05f) * 19349663);
                zone.Infection = new HellInfection(seed);
                zone.InfectionBorn = zone == breach ? HellGate.OpenedAt : Time.time;
            }
            if (zone.Growth == null && Datum.origin != null)
            {
                int seed = unchecked(Mathf.RoundToInt(zone.Center.x * .05f) * 73856093 ^ Mathf.RoundToInt(zone.Center.z * .05f) * 19349663);
                Vector3 centerLocal = Datum.origin.InverseTransformPoint(zone.Center.ToLocalPosition());
                zone.Growth = HellGrowth.Create(Datum.origin, seed, centerLocal, zone.Radius, 1f, 1f, zone.Infection, zone.InfectionBorn);
            }
            const int side = HellInfection.Side;
            var vertices = new Vector3[side * side];
            var uv = new Vector2[vertices.Length];
            var arrival = new Vector2[vertices.Length];
            var valid = new bool[vertices.Length];
            var triangles = new List<int>((side - 1) * (side - 1) * 6);
            Vector3 center = zone.Center.ToLocalPosition();
            for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                {
                    int index = z * side + x;
                    float dx = (x / (float)(side - 1) * 2f - 1f) * zone.Radius;
                    float dz = (z / (float)(side - 1) * 2f - 1f) * zone.Radius;
                    uv[index] = HellGroundSurface.Uv(new GlobalPosition(zone.Center.x + dx, zone.Center.y, zone.Center.z + dz));
                    float nx = dx / zone.Radius, nz = dz / zone.Radius;
                    var sample = zone.Infection.Sample(nx, nz);
                    arrival[index] = new Vector2(sample.Arrival * (HellInfection.Duration - 2f), sample.Coverage);
                    bool near = sample.Coverage > 0f;
                    // Include one empty cell round the field so alpha reaches zero before a mesh boundary.
                    float step = 2f / (side - 1);
                    for (int oz = -1; oz <= 1 && !near; oz++)
                        for (int ox = -1; ox <= 1 && !near; ox++)
                            near = zone.Infection.Sample(nx + ox * step, nz + oz * step).Coverage > 0f;
                    if (!near) continue;
                    Vector3 origin = new Vector3(center.x + dx, center.y + 500f, center.z + dz);
                    if (!Surface(origin, out RaycastHit hit) || hit.point.y <= Datum.LocalSeaY + .5f || hit.normal.y < .5f) continue;
                    vertices[index] = hit.point - center + Vector3.up * .18f;
                    valid[index] = true;
                }
            for (int z = 0; z < side - 1; z++)
                for (int x = 0; x < side - 1; x++)
                {
                    int a = z * side + x, b = a + side;
                    Add(a, b, a + 1);
                    Add(a + 1, b, b + 1);
                }
            if (triangles.Count == 0) return;
            zone.Mesh = new Mesh { name = "Hell sooted ash surface" };
            zone.Mesh.vertices = vertices;
            zone.Mesh.uv = uv;
            zone.Mesh.uv2 = arrival;
            zone.Mesh.SetTriangles(triangles, 0);
            zone.Mesh.RecalculateNormals();
            zone.Mesh.RecalculateBounds();
            zone.Material = ground;
            zone.Ground = new GameObject("Hell territory: " + zone.Label, typeof(MeshFilter), typeof(MeshRenderer));
            zone.Ground.transform.SetParent(Datum.origin, false);
            zone.Ground.transform.position = center;
            zone.Ground.GetComponent<MeshFilter>().sharedMesh = zone.Mesh;
            MeshRenderer renderer = zone.Ground.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = zone.Material;
            var clock = new MaterialPropertyBlock();
            clock.SetFloat("_GrowthStart", zone.InfectionBorn);
            clock.SetFloat("_GrowthDuration", HellInfection.Duration);
            renderer.SetPropertyBlock(clock);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            zone.DrawnRadius = zone.Radius;
            zone.MeshBuilds++;
            infectionMeshBuilds++;

            void Add(int a, int b, int c)
            {
                if (!valid[a] || !valid[b] || !valid[c]) return;
                float min = Mathf.Min(vertices[a].y, Mathf.Min(vertices[b].y, vertices[c].y));
                float max = Mathf.Max(vertices[a].y, Mathf.Max(vertices[b].y, vertices[c].y));
                if (max - min > 60f) return; // do not bridge a cliff or roof with a sheet
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }
        }

        private static bool Surface(Vector3 from, out RaycastHit hit)
        {
            hit = default;
            for (int i = 0; i < 8 && Physics.Raycast(from, Vector3.down, out hit, 2000f, PhysicsLayers.StaticsMask,
                     QueryTriggerInteraction.Ignore); i++)
            {
                if (hit.collider != null && (GameAssets.i == null || hit.collider.sharedMaterial == GameAssets.i.terrainMaterial)) return true;
                from = hit.point + Vector3.down * .05f; // buildings wear their own hell material; the crust belongs on terrain
            }
            return false;
        }

        private static void RemoveGround(Zone zone)
        {
            zone.Growth?.Dispose();
            zone.Growth = null;
            zone.Infection = null;
            RemoveSkin(zone);
        }

        private static void RemoveSkin(Zone zone)
        {
            if (zone.Ground != null) UnityEngine.Object.Destroy(zone.Ground);
            if (zone.Mesh != null) UnityEngine.Object.Destroy(zone.Mesh);
            zone.Ground = null;
            zone.Mesh = null;
            zone.Material = null;
        }

        internal static void Clear(bool restoreOwnership = true)
        {
            foreach (Zone zone in Active)
            {
                // A native callback can throw after changing ownership, before Controlled is committed.
                if (restoreOwnership)
                    try { Restore(zone); }
                    catch (Exception error) { Plugin.Logger.LogError("[Spooktober] Hell territory restore " + zone.Label + ": " + error); }
                RemoveGround(zone);
            }
            foreach (RadGhostLook.Handle handle in Dressed.Values) handle.Remove();
            Dressed.Clear();
            Released.Clear();
            Active.Clear();
            Bases.Clear();
            Owners.Clear();
            breach = null;
            next = previous = 0f;
            Count = Captured = Liberated = 0;
            infectionMeshBuilds = 0;
            HellArmy.RecolourIcons();
        }
    }
}
