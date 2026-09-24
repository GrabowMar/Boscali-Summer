using System.Collections.Generic;
using BoscaliSummer.Core;
using BoscaliSummer.Features.FireAndDestruction.Configuration;
using BoscaliSummer.Features.FireAndDestruction.Domain;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Infrastructure.Diagnostics;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Client-local visible building damage: every counted explosive or gun hit stamps a
    /// breach decal on the wall plus a dust burst, the second hit adds a smoke wisp and
    /// the third makes it a heavier plume. Collapses lay a ground scar. No HP tracking,
    /// no networking; the server never sees any of this.
    /// </summary>
    internal sealed class BuildingHitLedger : MonoBehaviour, ISceneService
    {
        private sealed class HitRecord
        {
            public int Hits;
            public float LastHitAt;
            public float WispBorn;
            public FuelDepotSmokePool.Visual Wisp;
        }

        public static BuildingHitLedger Instance { get; private set; }

        private const int MaxTracked = 128;
        private const int MaxBreachDecals = 48;
        private const int MaxGroundScars = 32;
        private const int MaxDustBursts = 6;
        private const int MaxWisps = 8;
        private const int GunQueueCap = 32;
        private const int GunPerFrame = 2;
        private const int SeedSalt = 0xb171e5;

        private readonly Dictionary<int, HitRecord> records = new Dictionary<int, HitRecord>(MaxTracked);
        private readonly List<GameObject> breachMarks = new List<GameObject>(MaxBreachDecals);
        private readonly List<int> breachOwners = new List<int>(MaxBreachDecals);
        private int breachHead;
        private readonly List<GameObject> scarMarks = new List<GameObject>(MaxGroundScars);
        private int scarHead;
        private readonly List<GameObject> dustBursts = new List<GameObject>(MaxDustBursts);
        private readonly List<ParticleSystem[]> dustSystems = new List<ParticleSystem[]>(MaxDustBursts);
        private readonly List<float> dustExpiry = new List<float>(MaxDustBursts);
        private readonly FuelDepotSmokePool wispPool = new FuelDepotSmokePool(MaxWisps);
        private readonly BurnScarPool ashPool = new BurnScarPool();
        private readonly Queue<GlobalPosition> gunHits = new Queue<GlobalPosition>(GunQueueCap);
        private readonly Collider[] overlapBuffer = new Collider[8];
        private static readonly List<Collider> colliderBuffer = new List<Collider>(8);
        private float nextWispTick;
        private bool loggedFirstBreach;
        private bool loggedFirstScar;

        private static FireAndDestructionSettings Fire => Plugin.Settings.FireAndDestruction;
        private static DiagnosticSettings Diagnostics => Plugin.Settings.Diagnostics;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            Clear();
            if (Instance == this) Instance = null;
        }

        public void ResetForScene() => Clear();

        /// <summary>
        /// Records a frag-trace shockwave on a building. Called from the TakeShockwave
        /// postfix on every peer; vanilla's method body is empty, so this call is purely
        /// the mod's per-hit notification with vanilla's origin and blast power.
        /// </summary>
        internal void SubmitShockwave(MapBuilding building, Vector3 origin, float blastPower)
        {
            if (GameManager.IsHeadless || !Fire.ImpactScorchEnabled.Value) return;
            if (building == null || blastPower < HitEscalation.MinBlastPower) return;
            int id = building.GetInstanceID();
            if (!ShouldCount(id)) return;

            Vector3 point;
            Vector3 normal;
            if (!TryResolveHit(building, origin, out point, out normal)) return;
            RecordHit(id);
            CountHit(id, point, normal, HitEscalation.BreachSize(blastPower));
        }

        /// <summary>
        /// Queues a gun impact for a small breach mark. Bullets outnumber blasts by two
        /// orders of magnitude, so the wall lookup is deferred to the bounded per-frame
        /// drain instead of running inside the bullet patch.
        /// </summary>
        internal void SubmitGunHit(GlobalPosition position)
        {
            if (GameManager.IsHeadless || !Fire.ImpactScorchEnabled.Value) return;
            if (gunHits.Count >= GunQueueCap) return;
            gunHits.Enqueue(position);
        }

        /// <summary>
        /// Releases everything a destroyed building owned: its ledger record and wisp,
        /// its breach decals and its catch-all scorch marks. Called from the Destruct
        /// path on every peer, including late-join replay.
        /// </summary>
        internal void Forget(int buildingId)
        {
            if (records.TryGetValue(buildingId, out HitRecord record))
            {
                if (record.Wisp != null) wispPool.Release(record.Wisp);
                records.Remove(buildingId);
            }
            for (int i = 0; i < breachMarks.Count; i++)
            {
                if (i < breachOwners.Count && breachOwners[i] == buildingId)
                {
                    breachOwners[i] = 0;
                    if (breachMarks[i] != null) breachMarks[i].SetActive(false);
                }
            }
            ImpactScorchManager.Instance?.ReleaseForBuilding(buildingId);
        }

        /// <summary>Lays a ground scar where walls fell. Ring of 32, oldest recycled.</summary>
        internal void StampGroundScar(Vector3 localPosition, float footprintX, float footprintZ)
        {
            if (GameManager.IsHeadless || !Fire.ImpactScorchEnabled.Value) return;
            GameObject mark = AcquireRingMark(scarMarks, ref scarHead, MaxGroundScars);
            if (mark == null) return;
            float size = HitEscalation.GroundScarDiameter(footprintX, footprintZ);
            ConfigureProjector(mark, localPosition, Vector3.up, size,
                Mathf.Clamp(size * 0.12f, 2f, 6f), CraterDecalMaterialResolver.Resolve());
            if (!loggedFirstScar && Diagnostics.VerboseLogging.Value)
            {
                loggedFirstScar = true;
                Plugin.Logger.LogInfo($"Ruin scar: first ground scar stamped at {localPosition} (size {size:0.#}m).");
            }
        }

        /// <summary>Tree rows burn to an ash bed, not a building scar.</summary>
        internal void StampTreeRowAsh(GlobalPosition position, float footprintX, float footprintZ)
        {
            if (GameManager.IsHeadless || !Fire.ImpactScorchEnabled.Value) return;
            ashPool.Stamp(position, HitEscalation.TreeRowAshDiameter(footprintX, footprintZ));
        }

        private void Update()
        {
            int budget = GunPerFrame;
            while (budget-- > 0 && gunHits.Count > 0)
                ProcessGunHit(gunHits.Dequeue());

            float now = Time.timeSinceLevelLoad;
            for (int i = dustBursts.Count - 1; i >= 0; i--)
            {
                if (dustBursts[i] == null)
                {
                    dustBursts.RemoveAt(i);
                    dustSystems.RemoveAt(i);
                    dustExpiry.RemoveAt(i);
                }
                else if (now >= dustExpiry[i] && dustBursts[i].activeSelf)
                    dustBursts[i].SetActive(false);
            }

            if (now < nextWispTick) return;
            nextWispTick = now + 0.25f;
            if (records.Count == 0) return;
            Vector3 wind = NetworkSceneSingleton<LevelInfo>.i != null
                ? NetworkSceneSingleton<LevelInfo>.i.GetWind()
                : Vector3.zero;
            foreach (KeyValuePair<int, HitRecord> entry in records)
            {
                HitRecord record = entry.Value;
                if (record.Wisp == null) continue;
                record.Wisp.SetPhase(Mathf.Max(0f, now - record.WispBorn), 1f, wind);
            }
        }

        private void ProcessGunHit(GlobalPosition position)
        {
            Vector3 local = position.ToLocalPosition();
            int count = Physics.OverlapSphereNonAlloc(
                local, 4f, overlapBuffer, PhysicsLayers.StaticsMask,
                QueryTriggerInteraction.Collide);
            MapBuilding building = null;
            Collider shell = null;
            for (int i = 0; i < count; i++)
            {
                if (overlapBuffer[i] == null) continue;
                MapBuilding candidate = overlapBuffer[i].GetComponentInParent<MapBuilding>();
                if (candidate == null) continue;
                building = candidate;
                shell = overlapBuffer[i];
                break;
            }
            if (building == null || shell == null) return;
            int id = building.GetInstanceID();
            if (!ShouldCount(id)) return;
            RecordHit(id);

            // The impact point sits on the surface; cast a short ray at the shell to
            // recover the wall normal for the decal.
            Vector3 toCenter = (shell.bounds.center - local).normalized;
            if (toCenter.sqrMagnitude < 0.01f) toCenter = Vector3.forward;
            Vector3 normal = -toCenter;
            if (Physics.Raycast(local - toCenter * 0.5f, toCenter, out RaycastHit hit,
                    30f, PhysicsLayers.StaticsMask))
                normal = hit.normal;
            CountHit(id, local, normal, HitEscalation.GunBreachSize);
        }

        private bool ShouldCount(int id)
        {
            return !records.TryGetValue(id, out HitRecord record) ||
                HitEscalation.ShouldCount(record.LastHitAt, Time.timeSinceLevelLoad);
        }

        private void RecordHit(int id)
        {
            if (!records.TryGetValue(id, out HitRecord record))
            {
                if (records.Count >= MaxTracked) EvictOldest();
                record = new HitRecord();
                records[id] = record;
            }
            record.LastHitAt = Time.timeSinceLevelLoad;
            record.Hits++;
        }

        /// <summary>
        /// Releases the oldest wisp so the newest hit still smokes. Only runs when the
        /// 8-visual pool is exhausted.
        /// </summary>
        private bool StealOldestWisp(int exceptId)
        {
            int oldest = 0;
            float oldestAt = float.MaxValue;
            foreach (KeyValuePair<int, HitRecord> entry in records)
            {
                if (entry.Key == exceptId || entry.Value.Wisp == null) continue;
                if (entry.Value.WispBorn < oldestAt)
                {
                    oldestAt = entry.Value.WispBorn;
                    oldest = entry.Key;
                }
            }
            if (oldest == 0 || !records.TryGetValue(oldest, out HitRecord record)) return false;
            wispPool.Release(record.Wisp);
            record.Wisp = null;
            return true;
        }

        private void EvictOldest()
        {
            int oldest = 0;
            float oldestAt = float.MaxValue;
            bool found = false;
            foreach (KeyValuePair<int, HitRecord> entry in records)
            {
                if (entry.Value.LastHitAt < oldestAt)
                {
                    oldestAt = entry.Value.LastHitAt;
                    oldest = entry.Key;
                    found = true;
                }
            }
            if (found) Forget(oldest);
        }

        private void CountHit(int id, Vector3 point, Vector3 normal, float size)
        {
            HitRecord record = records[id];
            GameObject mark = AcquireRingMark(breachMarks, ref breachHead, MaxBreachDecals);
            if (mark != null)
            {
                int slot = breachMarks.IndexOf(mark);
                if (slot >= 0)
                {
                    while (breachOwners.Count <= slot) breachOwners.Add(0);
                    breachOwners[slot] = id;
                }
                ConfigureProjector(mark, point, normal, size,
                    Mathf.Clamp(size * 0.22f, 1.6f, 4f), CraterDecalMaterialResolver.Resolve());
            }
            EmitDust(point, normal);
            if (HitEscalation.HasWisp(record.Hits))
            {
                if (record.Wisp == null)
                {
                    record.Wisp = wispPool.Acquire(point.ToGlobalPosition(),
                        new Vector2(2.5f, 2.5f), FuelDepotSmokePool.SmokeProfile.Ruin);
                    if (record.Wisp == null && StealOldestWisp(id))
                        record.Wisp = wispPool.Acquire(point.ToGlobalPosition(),
                            new Vector2(2.5f, 2.5f), FuelDepotSmokePool.SmokeProfile.Ruin);
                    if (record.Wisp != null) record.WispBorn = Time.timeSinceLevelLoad;
                }
                if (record.Wisp != null)
                {
                    // The wisp always rises from the latest breach.
                    record.Wisp.SetPosition(point.ToGlobalPosition());
                    record.Wisp.ExternalIntensity = HitEscalation.WispIntensity(record.Hits);
                }
            }
            if (!loggedFirstBreach && Diagnostics.VerboseLogging.Value && mark != null)
            {
                loggedFirstBreach = true;
                Plugin.Logger.LogInfo(
                    $"Building hit: first breach mark placed at {point} (size {size:0.#}m).");
            }
        }

        private void EmitDust(Vector3 point, Vector3 normal)
        {
            if (GameAssets.i == null || GameAssets.i.contactDust == null) return;
            GameObject burst = null;
            int slot = -1;
            float oldest = float.MaxValue;
            int oldestSlot = -1;
            for (int i = 0; i < dustBursts.Count; i++)
            {
                if (dustBursts[i] == null) continue;
                if (!dustBursts[i].activeSelf) { slot = i; break; }
                if (dustExpiry[i] < oldest) { oldest = dustExpiry[i]; oldestSlot = i; }
            }
            if (slot < 0)
            {
                if (dustBursts.Count < MaxDustBursts)
                {
                    burst = Object.Instantiate(GameAssets.i.contactDust, Datum.origin, false);
                    burst.name = "BoscaliSummer.HitDust";
                    dustBursts.Add(burst);
                    dustSystems.Add(burst.GetComponentsInChildren<ParticleSystem>(true));
                    dustExpiry.Add(0f);
                    slot = dustBursts.Count - 1;
                }
                else if (oldestSlot >= 0) slot = oldestSlot;
                else return;
                burst = dustBursts[slot];
            }
            else burst = dustBursts[slot];
            if (burst == null) return;
            burst.transform.position = point + normal * 0.5f;
            burst.SetActive(true);
            ParticleSystem[] systems = dustSystems[slot];
            if (systems != null)
                for (int i = 0; i < systems.Length; i++)
                    if (systems[i] != null)
                    {
                        systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        systems[i].Play(true);
                    }
            dustExpiry[slot] = Time.timeSinceLevelLoad + HitEscalation.DustSeconds;
        }

        /// <summary>
        /// Re-casts vanilla's frag ray against the building's own colliders to recover the
        /// wall point and normal FragTrace saw. Falls back to the closest surface point so
        /// a hit is never dropped because the ray started inside the collider.
        /// </summary>
        private static bool TryResolveHit(
            MapBuilding building, Vector3 origin, out Vector3 point, out Vector3 normal)
        {
            point = building.transform.position;
            normal = Vector3.up;
            colliderBuffer.Clear();
            building.GetComponentsInChildren(false, colliderBuffer);
            bool found = false;
            float nearest = float.MaxValue;
            for (int i = 0; i < colliderBuffer.Count; i++)
            {
                Collider collider = colliderBuffer[i];
                if (collider == null || collider.isTrigger) continue;
                Vector3 toShell = collider.transform.position - origin;
                float distance = toShell.magnitude;
                if (distance < 0.01f) continue;
                Ray ray = new Ray(origin, toShell / distance);
                RaycastHit hit;
                if (collider.Raycast(ray, out hit, distance + 5f) && hit.distance < nearest)
                {
                    nearest = hit.distance;
                    point = hit.point;
                    normal = hit.normal;
                    found = true;
                }
            }
            colliderBuffer.Clear();
            if (found) return true;
            Collider fallback = building.GetComponentInChildren<Collider>();
            if (fallback == null) return false;
            point = fallback.ClosestPoint(origin);
            Vector3 away = point - origin;
            normal = away.sqrMagnitude > 0.01f ? away.normalized * -1f : Vector3.up;
            return true;
        }

        private static GameObject AcquireRingMark(List<GameObject> ring, ref int head, int maximum)
        {
            if (GameAssets.i == null || GameAssets.i.scorchMarkDecal == null) return null;
            if (ring.Count < maximum)
            {
                GameObject created = Object.Instantiate(
                    GameAssets.i.scorchMarkDecal, Datum.origin, false);
                created.name = "BoscaliSummer.BuildingHit";
                created.SetActive(true);
                ring.Add(created);
                return created;
            }
            GameObject oldest = ring[head];
            head = (head + 1) % ring.Count;
            if (oldest != null) oldest.SetActive(true);
            return oldest;
        }

        private static void ConfigureProjector(
            GameObject mark, Vector3 point, Vector3 normal, float size, float depth, Material material)
        {
            DecalProjector projector = mark.GetComponent<DecalProjector>();
            if (projector == null) return;
            uint seed = Deterministic.Hash(
                Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y), Mathf.RoundToInt(point.z),
                SeedSalt);
            Quaternion facing = Quaternion.LookRotation(-normal,
                Mathf.Abs(normal.y) > 0.9f ? Vector3.forward : Vector3.up);
            Quaternion roll = Quaternion.AngleAxis(
                Deterministic.UnitFloat(seed ^ 0x85ebca6bu) * 360f, -normal);
            Transform t = mark.transform;
            t.rotation = roll * facing;
            Vector3 position = point + normal * (depth * 0.45f);
            float tangentJitter = Deterministic.UnitFloat(seed) * 2f - 1f;
            float bitangentJitter = Deterministic.UnitFloat(seed ^ 0x9e3779b9u) * 2f - 1f;
            position += t.right * tangentJitter * size * 0.25f;
            position += t.up * bitangentJitter * size * 0.25f;
            t.position = position;
            projector.renderingLayerMask = ~0u;
            projector.size = new Vector3(size, size, depth);
            projector.startAngleFade = 45f;
            projector.endAngleFade = 70f;
            projector.fadeFactor = 0.98f;
            projector.drawDistance = 3500f;
            if (material != null) projector.material = material;
        }

        private void Clear()
        {
            foreach (KeyValuePair<int, HitRecord> entry in records)
                if (entry.Value.Wisp != null) wispPool.Release(entry.Value.Wisp);
            records.Clear();
            for (int i = 0; i < breachMarks.Count; i++)
                if (breachMarks[i] != null) Object.Destroy(breachMarks[i]);
            breachMarks.Clear();
            breachOwners.Clear();
            breachHead = 0;
            for (int i = 0; i < scarMarks.Count; i++)
                if (scarMarks[i] != null) Object.Destroy(scarMarks[i]);
            scarMarks.Clear();
            scarHead = 0;
            for (int i = 0; i < dustBursts.Count; i++)
                if (dustBursts[i] != null) Object.Destroy(dustBursts[i]);
            dustBursts.Clear();
            dustSystems.Clear();
            dustExpiry.Clear();
            gunHits.Clear();
            wispPool.Clear();
            ashPool.Clear();
            nextWispTick = 0f;
            loggedFirstBreach = loggedFirstScar = false;
            CraterDecalMaterialResolver.ResetForScene();
        }
    }
}
