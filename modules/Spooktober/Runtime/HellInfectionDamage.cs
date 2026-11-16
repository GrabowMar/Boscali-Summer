using System;
using System.Collections.Generic;
using System.Diagnostics;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Spooktober.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>Native, host-owned demolition at the advancing infection front. Discovery and footprints are cached.</summary>
    internal static class HellInfectionDamage
    {
        private sealed class Field
        {
            internal HellTerritory.Zone Zone;
            internal HellInfection Infection;
            internal Collider[] Scan;
            internal int Cursor;
            internal readonly HashSet<int> Seen = new HashSet<int>();
        }
        private sealed class Target
        {
            internal Component Subject;
            internal int Id;
            internal Collider[] Colliders;
            internal UnitPart[] Parts;
            internal Bounds GlobalBounds;
        }
        private sealed class Job
        {
            internal Field Field;
            internal Component Subject;
            internal Target Target;
            internal Footprint Cursor;
        }
        private sealed class Footprint
        {
            internal float MinX, MaxX, MinZ, MaxZ, NextX, NextZ, Step, Earliest = float.PositiveInfinity;
            internal int Anchor;
        }

        private static readonly AccessTools.FieldRef<Building, float> RepairTime = AccessTools.FieldRefAccess<Building, float>("repairTime");
        private static readonly List<Field> Fields = new List<Field>();
        private static readonly Queue<Field> Discovery = new Queue<Field>();
        private static readonly Queue<Job> Preparation = new Queue<Job>();
        private static readonly Dictionary<int, Target> Targets = new Dictionary<int, Target>();
        private static readonly Dictionary<int, Job> DueJobs = new Dictionary<int, Job>();
        private static readonly SortedSet<(float due, int serial)> Due = new SortedSet<(float due, int serial)>();
        private static readonly HashSet<int> Attempted = new HashSet<int>(), Scheduled = new HashSet<int>();
        private static int serial;
        private static float nextUnits;
        internal static int DestroyedBuildings { get; private set; }
        internal static int DestroyedMapBuildings { get; private set; }
        internal static int DestroyedFacilities { get; private set; }
        internal static int FailedDemolitions { get; private set; }
        internal static int MaxFootprintProbes { get; private set; }
        internal static int ScheduledTargets => Scheduled.Count;
        internal static int DiscoveryPending => Discovery.Count + Preparation.Count;
        internal static int TouchedPending
        {
            get
            {
                int count = 0;
                foreach (var entry in Due)
                {
                    if (entry.due > Time.time) break;
                    Job job = DueJobs[entry.serial];
                    if (Live(job.Field) && job.Target.Subject != null && !Attempted.Contains(job.Target.Id)) count++;
                }
                return count;
            }
        }
        internal static int SurvivingTouched
        {
            get
            {
                int count = 0;
                foreach (int id in Attempted)
                    if (Targets.TryGetValue(id, out Target target) && Alive(target.Subject)) count++;
                return count;
            }
        }

        internal static void Register(HellTerritory.Zone zone)
        {
            if (!GameAccess.IsServer() || zone.Infection == null || zone.SurfaceValid == null) return;
            var field = new Field { Zone = zone, Infection = zone.Infection };
            Vector3 center = zone.Center.ToLocalPosition();
            field.Scan = Physics.OverlapBox(center + Vector3.up * 100f, new Vector3(zone.Radius, 600f, zone.Radius),
                Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
            Fields.Add(field);
            if (field.Scan.Length > 0) Discovery.Enqueue(field);
            else field.Scan = null;
            AddUnits(field);
        }

        internal static void Frame(float now)
        {
            if (!GameAccess.IsServer()) return;
            long end = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 500; // soft 2 ms, at most one native collapse per frame
            for (int budget = 32; budget > 0 && Due.Count > 0 && Due.Min.due <= now; budget--)
            {
                var key = Due.Min;
                Due.Remove(key);
                Job job = DueJobs[key.serial];
                DueJobs.Remove(key.serial);
                if (!Live(job.Field) || job.Target.Subject == null || !Attempted.Add(job.Target.Id)) continue;
                Demolish(job.Target);
                break;
            }
            if (now >= nextUnits)
            {
                nextUnits = now + 2f;
                for (int i = Fields.Count - 1; i >= 0; i--)
                    if (Live(Fields[i])) AddUnits(Fields[i]);
                    else Fields.RemoveAt(i);
            }
            for (int budget = 48; budget > 0 && Discovery.Count > 0 && Stopwatch.GetTimestamp() < end; budget--)
            {
                Field field = Discovery.Dequeue();
                if (!Live(field)) { field.Scan = null; continue; }
                Collider collider = field.Scan[field.Cursor++];
                if (collider != null)
                {
                    Component subject = collider.GetComponentInParent<MapBuilding>();
                    if (subject == null) subject = collider.GetComponentInParent<Building>();
                    if (subject == null && collider.GetComponentInParent<UnitPart>() is UnitPart part) subject = part.GetUnit() as Building;
                    Enqueue(field, subject);
                }
                if (field.Cursor < field.Scan.Length) Discovery.Enqueue(field);
                else field.Scan = null;
            }
            int probes = 64; // shared by every queued footprint; unfinished cursors resume next frame
            for (int budget = 4; budget > 0 && probes > 0 && Preparation.Count > 0 && Stopwatch.GetTimestamp() < end; budget--)
            {
                Job job = Preparation.Dequeue();
                if (!Live(job.Field) || job.Subject == null || Attempted.Contains(job.Subject.GetInstanceID())) continue;
                int id = job.Subject.GetInstanceID();
                if (job.Target == null)
                {
                    if (!Targets.TryGetValue(id, out Target target)) Targets.Add(id, target = Cache(job.Subject));
                    job.Target = target;
                    job.Cursor = BeginContact(job.Field.Zone, target);
                }
                if (!Contact(job, ref probes, end)) { Preparation.Enqueue(job); continue; }
                float when = job.Cursor.Earliest;
                if (!float.IsFinite(when)) continue;
                int key = ++serial;
                DueJobs.Add(key, job);
                Due.Add((when, key));
                Scheduled.Add(id);
            }
            MaxFootprintProbes = Math.Max(MaxFootprintProbes, 64 - probes);
        }

        private static bool Live(Field field) => ReferenceEquals(field.Zone.Infection, field.Infection) && field.Zone.Ground != null;
        private static void Enqueue(Field field, Component subject)
        {
            if (subject == null || Attempted.Contains(subject.GetInstanceID()) || !field.Seen.Add(subject.GetInstanceID())) return;
            Preparation.Enqueue(new Job { Field = field, Subject = subject });
        }
        private static void AddUnits(Field field)
        {
            foreach (Unit unit in UnitRegistry.allUnits) if (unit is Building building) Add(building);
            if (field.Zone.Base?.buildings != null) foreach (Building building in field.Zone.Base.buildings) Add(building);
            void Add(Building building)
            {
                if (building == null) return;
                GlobalPosition at = building.transform.position.ToGlobalPosition();
                float extra = building.definition != null ? Mathf.Max(256f, Mathf.Max(building.definition.width, building.definition.length)) : 512f;
                if (Mathf.Abs(at.x - field.Zone.Center.x) <= field.Zone.Radius + extra &&
                    Mathf.Abs(at.z - field.Zone.Center.z) <= field.Zone.Radius + extra) Enqueue(field, building);
            }
        }

        private static Target Cache(Component subject)
        {
            var colliders = new List<Collider>();
            subject.GetComponentsInChildren(true, colliders);
            UnitPart[] parts = subject is Building building && building.partLookup != null ? building.partLookup.ToArray() : Array.Empty<UnitPart>();
            foreach (UnitPart part in parts)
                if (part != null && !part.transform.IsChildOf(subject.transform))
                    foreach (Collider collider in part.GetComponentsInChildren<Collider>(true)) if (!colliders.Contains(collider)) colliders.Add(collider);
            Bounds bounds = new Bounds(subject.transform.position, Vector3.one);
            bool found = false;
            foreach (Collider collider in colliders)
                if (collider != null && !collider.isTrigger)
                {
                    Bounds own = collider.bounds;
                    if (own.size.sqrMagnitude < .01f) continue;
                    if (!found) { bounds = own; found = true; } else bounds.Encapsulate(own);
                }
            if (!found)
                foreach (Renderer renderer in subject.GetComponentsInChildren<Renderer>(true))
                    if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    {
                        if (!found) { bounds = renderer.bounds; found = true; } else bounds.Encapsulate(renderer.bounds);
                    }
            bounds.center = bounds.center.ToGlobalPosition().AsVector3();
            return new Target { Subject = subject, Id = subject.GetInstanceID(), Colliders = colliders.ToArray(), Parts = parts, GlobalBounds = bounds };
        }

        private static Footprint BeginContact(HellTerritory.Zone zone, Target target)
        {
            Bounds bounds = target.GlobalBounds;
            float minX = Mathf.Max(bounds.min.x, zone.Center.x - zone.Radius), maxX = Mathf.Min(bounds.max.x, zone.Center.x + zone.Radius);
            float minZ = Mathf.Max(bounds.min.z, zone.Center.z - zone.Radius), maxZ = Mathf.Min(bounds.max.z, zone.Center.z + zone.Radius);
            return new Footprint { MinX = minX, MaxX = maxX, MinZ = minZ, MaxZ = maxZ, NextX = minX, NextZ = minZ,
                Step = Mathf.Clamp(zone.Radius / (HellInfection.Side - 1), 6f, 16f) };
        }

        private static bool Contact(Job job, ref int probes, long end)
        {
            HellTerritory.Zone zone = job.Field.Zone;
            Target target = job.Target;
            Footprint cursor = job.Cursor;
            Bounds bounds = target.GlobalBounds;
            if (cursor.MinX > cursor.MaxX || cursor.MinZ > cursor.MaxZ) return true;
            while (probes > 0 && Stopwatch.GetTimestamp() < end)
            {
                if (cursor.Anchor < 9)
                {
                    int anchor = cursor.Anchor++;
                    Probe(Mathf.Lerp(cursor.MinX, cursor.MaxX, anchor % 3 * .5f), Mathf.Lerp(cursor.MinZ, cursor.MaxZ, anchor / 3 * .5f));
                }
                else
                {
                    if (cursor.NextZ > cursor.MaxZ) return true;
                    Probe(cursor.NextX, cursor.NextZ);
                    cursor.NextX += cursor.Step;
                    if (cursor.NextX > cursor.MaxX) { cursor.NextX = cursor.MinX; cursor.NextZ += cursor.Step; }
                }
                probes--;
            }
            return cursor.Anchor == 9 && cursor.NextZ > cursor.MaxZ;

            void Probe(float x, float z)
            {
                float insetX = Mathf.Min(.15f, bounds.extents.x * .1f), insetZ = Mathf.Min(.15f, bounds.extents.z * .1f);
                x = Mathf.Clamp(x, bounds.min.x + insetX, bounds.max.x - insetX);
                z = Mathf.Clamp(z, bounds.min.z + insetZ, bounds.max.z - insetZ);
                float nx = (x - zone.Center.x) / zone.Radius, nz = (z - zone.Center.z) / zone.Radius;
                if (!Surface(zone, nx, nz, out float height, out HellInfection.SampleValue point)) return;
                float delay = HellInfection.ContactDelay(point);
                if (!float.IsFinite(delay) || zone.InfectionBorn + delay >= cursor.Earliest ||
                    bounds.min.y > height + 40f || bounds.max.y < height - 10f) return;
                bool hit = target.Subject is Building facility && facility.disabled;
                var ray = new Ray(new GlobalPosition(x, bounds.max.y + 3f, z).ToLocalPosition(), Vector3.down);
                foreach (Collider collider in target.Colliders)
                    if (collider != null && !collider.isTrigger && collider.Raycast(ray, out _, bounds.size.y + 10f)) { hit = true; break; }
                if (hit) cursor.Earliest = zone.InfectionBorn + delay;
            }
        }

        private static bool Surface(HellTerritory.Zone zone, float x, float z, out float height, out HellInfection.SampleValue point)
        {
            height = 0f;
            point = default;
            if (x < -1f || x > 1f || z < -1f || z > 1f) return false;
            int side = zone.SurfaceSide;
            float scale = (side - 1) * .5f, gx = (x + 1f) * scale, gz = (z + 1f) * scale;
            int ix = Mathf.Min(side - 2, (int)gx), iz = Mathf.Min(side - 2, (int)gz), a = iz * side + ix, b = a + side;
            float tx = gx - ix, tz = gz - iz;
            int i0 = tx + tz <= 1f ? a : a + 1, i1 = b, i2 = tx + tz <= 1f ? a + 1 : b + 1;
            if (!zone.SurfaceValid[i0] || !zone.SurfaceValid[i1] || !zone.SurfaceValid[i2]) return false;
            float y0 = zone.SurfaceVertices[i0].y, y1 = zone.SurfaceVertices[i1].y, y2 = zone.SurfaceVertices[i2].y;
            if (Mathf.Max(y0, Mathf.Max(y1, y2)) - Mathf.Min(y0, Mathf.Min(y1, y2)) > 60f) return false;
            float w0 = tx + tz <= 1f ? 1f - tx - tz : 1f - tz;
            float w1 = tx + tz <= 1f ? tz : 1f - tx, w2 = 1f - w0 - w1;
            height = zone.Center.y + y0 * w0 + y1 * w1 + y2 * w2;
            // Match the GPU's triangle interpolation, including clean gaps and their later edge arrival.
            Vector2 uv = zone.SurfaceArrival[i0] * w0 + zone.SurfaceArrival[i1] * w1 + zone.SurfaceArrival[i2] * w2;
            point = new HellInfection.SampleValue(uv.x / (HellInfection.Duration - 2f), uv.y);
            return true;
        }

        private static bool Alive(Component subject) => subject != null && (subject is Building building ? !building.disabled :
            subject is MapBuilding map && GameAccess.GetMapBuildingHitPoints(map) > 0f);
        private static void Demolish(Target target)
        {
            try
            {
                if (target.Subject is MapBuilding map)
                {
                    map.TakeDamage(0f, 0f, 1f, 0f, 1000000f, PersistentID.None);
                    if (map != null && GameAccess.GetMapBuildingHitPoints(map) > 0f)
                        throw new InvalidOperationException("native MapBuilding damage did not demolish the touched building");
                    DestroyedMapBuildings++;
                }
                else if (target.Subject is Building building)
                {
                    RepairTime(building) = 1000000f; // native IsRepairable/airbase WaitRepair must never resurrect infection casualties
                    if (building.disabled) return;
                    foreach (UnitPart part in target.Parts)
                        if (part != null) part.TakeDamage(0f, 0f, 1f, 0f, 1000000f, PersistentID.None);
                    if (!building.disabled) building.Networkdisabled = true;
                    DestroyedFacilities++;
                }
                DestroyedBuildings++;
            }
            catch (Exception e) { FailedDemolitions++; Plugin.Logger.LogError("[Spooktober] infection demolition " + target.Subject + ": " + e); }
        }

        internal static void Clear(bool resetHistory)
        {
            Fields.Clear(); Discovery.Clear(); Preparation.Clear(); Due.Clear(); DueJobs.Clear();
            nextUnits = 0f;
            if (!resetHistory) return;
            Targets.Clear(); Attempted.Clear(); Scheduled.Clear();
            DestroyedBuildings = DestroyedMapBuildings = DestroyedFacilities = FailedDemolitions = 0;
            MaxFootprintProbes = 0;
        }
    }
}
