using System.Collections.Generic;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.FireAndDestruction.Domain;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Fire
{
    /// <summary>Bounded local impact residue and concrete bursts. Never edits native meshes or damage.</summary>
    internal sealed class BuildingHitLedger : MonoBehaviour, ISceneService
    {
        private struct Mark { internal GameObject Object; internal int Owner; }
        public static BuildingHitLedger Instance { get; private set; }
        internal GlobalPosition LastImpact { get; private set; }
        internal Vector3 LastImpactNormal { get; private set; }
        internal int ImpactMarks { get { int count = 0; foreach (Mark mark in marks)
            if (mark.Object != null && mark.Object.activeSelf) count++; return count; } }
        private const int MaxTracked = 128, MaxMarks = 48, MaxGroundScars = 32, GunQueueCap = 32, SeedSalt = 0xb171e5;
        private readonly Dictionary<int, float> hits = new Dictionary<int, float>(MaxTracked);
        private readonly List<Mark> marks = new List<Mark>(MaxMarks);
        private readonly List<GameObject> scarMarks = new List<GameObject>(MaxGroundScars);
        private readonly Queue<GlobalPosition> gunHits = new Queue<GlobalPosition>(GunQueueCap);
        private readonly Collider[] overlaps = new Collider[8];
        private readonly List<Collider> colliders = new List<Collider>(8);
        private readonly CollapseBurstPool dust = new CollapseBurstPool(6);
        private readonly BurnScarPool ashPool = new BurnScarPool();
        private int markHead, scarHead;
        private static BoscaliSummer.Modules.FireAndDestruction.Configuration.FireAndDestructionSettings Fire => Plugin.Settings.FireAndDestruction;
        private void Awake() => Instance = this;
        private void Start() { if (Fire.ImpactScorchEnabled.Value && !GameManager.IsHeadless) dust.Warm(); }
        private void OnDestroy() { Clear(); if (Instance == this) Instance = null; }
        public void ResetForScene() { Clear(); if (isActiveAndEnabled) Start(); }
        internal void EmitDebrisDust(GlobalPosition position) => dust.EmitImpact(position, Vector3.up, 3f);

        internal void SubmitShockwave(MapBuilding building, Vector3 origin, float power)
        {
            if (GameManager.IsHeadless || !Fire.ImpactScorchEnabled.Value || power < HitEscalation.MinBlastPower) return;
            RuinAftermathManager.Instance?.Poke(origin, power);
            if (building == null) return;
            colliders.Clear(); building.GetComponentsInChildren(false, colliders);
            bool found = false; float nearest = float.MaxValue; RaycastHit surface = default;
            foreach (Collider collider in colliders)
            {
                if (collider == null || collider.isTrigger) continue;
                // Native collider rays give a real point and normal. Aim at the nearest point of the
                // collider's box, not its centre, so a blast high on a tower marks the tower up there.
                Vector3 direction = collider.ClosestPointOnBounds(origin) - origin;
                if (direction.sqrMagnitude < 0.25f) direction = collider.bounds.center - origin;
                if (direction.sqrMagnitude < 0.001f) continue;
                if (collider.Raycast(new Ray(origin, direction.normalized), out RaycastHit hit, direction.magnitude + collider.bounds.size.magnitude)
                    && hit.distance < nearest) { surface = hit; nearest = hit.distance; found = true; }
            }
            colliders.Clear();
            if (found) Impact(building, surface.point, surface.normal, HitEscalation.BreachSize(power));
        }

        internal void SubmitGunHit(GlobalPosition position)
        {
            if (!GameManager.IsHeadless && Fire.ImpactScorchEnabled.Value && gunHits.Count < GunQueueCap) gunHits.Enqueue(position);
        }
        private void Update()
        {
            for (int budget = 0; budget < 2 && gunHits.Count > 0; budget++)
            {
                Vector3 point = gunHits.Dequeue().ToLocalPosition();
                RuinAftermathManager.Instance?.Poke(point, 0f);
                int count = Physics.OverlapSphereNonAlloc(point, 4f, overlaps, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < count; i++)
                {
                    Collider collider = overlaps[i];
                    MapBuilding building = collider != null ? collider.GetComponentInParent<MapBuilding>() : null;
                    if (building == null) continue;
                    Vector3 direction = (collider.bounds.center - point).normalized;
                    if (collider.Raycast(new Ray(point - direction * 0.5f, direction), out RaycastHit hit, 5f))
                        Impact(building, hit.point, hit.normal, HitEscalation.GunBreachSize);
                    break;
                }
            }
            dust.Update(Time.timeSinceLevelLoad);
        }
        private void Impact(MapBuilding building, Vector3 point, Vector3 normal, float size)
        {
            int owner = building.GetInstanceID(); float now = Time.timeSinceLevelLoad;
            if (hits.TryGetValue(owner, out float previous) && !HitEscalation.ShouldCount(previous, now)) return;
            if (!hits.ContainsKey(owner) && hits.Count >= MaxTracked)
            {
                int oldest = 0; float age = float.MaxValue;
                foreach (var entry in hits) if (entry.Value < age) { age = entry.Value; oldest = entry.Key; }
                Forget(oldest);
            }
            hits[owner] = now; LastImpact = point.ToGlobalPosition(); LastImpactNormal = normal;
            BuildingCarver.Instance?.Hit(building, point, normal, size);
            dust.EmitImpact(LastImpact, normal, size);
            Material material = ScorchDecalMaterialResolver.Resolve();
            if (material == null || GameAssets.i == null || GameAssets.i.scorchMarkDecal == null) return;
            Mark mark;
            int slot;
            if (marks.Count < MaxMarks)
            {
                mark = new Mark { Object = Object.Instantiate(GameAssets.i.scorchMarkDecal, Datum.origin, false) };
                slot = marks.Count; marks.Add(mark);
            }
            else { slot = markHead; markHead = (markHead + 1) % MaxMarks; mark = marks[slot]; }
            mark.Owner = owner; marks[slot] = mark;
            if (mark.Object == null) return;
            mark.Object.name = "BoscaliSummer.ImpactResidue"; mark.Object.SetActive(true);
            // Thin projection prevents a front-wall impact staining floors or the opposite wall.
            ConfigureProjector(mark.Object, point, normal, Mathf.Clamp(size * 0.65f, 0.35f, 7f), 0.6f, material);
            mark.Object.GetComponent<DecalProjector>().fadeFactor = 0.48f;
        }
        internal void Forget(int owner)
        {
            hits.Remove(owner);
            foreach (Mark mark in marks) if (mark.Owner == owner && mark.Object != null) mark.Object.SetActive(false);
            ImpactScorchManager.Instance?.ReleaseForBuilding(owner);
        }
        internal void StampGroundScar(Vector3 localPosition, float footprintX, float footprintZ)
        {
            if (GameManager.IsHeadless || !Fire.ImpactScorchEnabled.Value) return;
            GameObject mark = AcquireRingMark(scarMarks, ref scarHead, MaxGroundScars);
            if (mark == null) return;
            float size = HitEscalation.GroundScarDiameter(footprintX, footprintZ);
            ConfigureProjector(mark, localPosition, Vector3.up, size,
                Mathf.Clamp(size * 0.12f, 2f, 6f), ScorchDecalMaterialResolver.Resolve());
            DecalProjector residue = mark.GetComponent<DecalProjector>();
            if (residue != null) residue.fadeFactor = 0.28f;

        }

        /// <summary>Tree rows burn to an ash bed, not a building scar.</summary>
        internal void StampTreeRowAsh(GlobalPosition position, float footprintX, float footprintZ)
        {
            if (GameManager.IsHeadless || !Fire.ImpactScorchEnabled.Value) return;
            ashPool.Stamp(position, HitEscalation.TreeRowAshDiameter(footprintX, footprintZ));
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
            foreach (Mark mark in marks) if (mark.Object != null) Object.Destroy(mark.Object);
            foreach (GameObject scar in scarMarks) if (scar != null) Object.Destroy(scar);
            marks.Clear(); scarMarks.Clear(); hits.Clear(); gunHits.Clear(); colliders.Clear();
            dust.Clear(); ashPool.Clear(); markHead = scarHead = 0;
        }
    }
}
