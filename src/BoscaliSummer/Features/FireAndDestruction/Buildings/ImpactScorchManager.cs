using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core;
using BoscaliSummer.Features.FireAndDestruction.Configuration;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Infrastructure.Diagnostics;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Stamps a single black scorch decal on a building wall at the point an explosive hit
    /// landed. Purely local cosmetic: no HP tracking, no damage tiers, no per-building state
    /// and no networking. Every client that runs the impact patch places its own mark.
    /// </summary>
    internal sealed class ImpactScorchManager : MonoBehaviour, ISceneService
    {
        private struct PendingExplosion
        {
            public GlobalPosition Position;
            public float BlastYield;
        }

        public static ImpactScorchManager Instance { get; private set; }

        private static readonly int SeedSalt = 0x5c04c1;

        private readonly Queue<PendingExplosion> pending = new Queue<PendingExplosion>(32);
        private readonly Collider[] overlapBuffer = new Collider[32];
        private readonly RaycastHit[] rayBuffer = new RaycastHit[16];
        private readonly List<GameObject> marks = new List<GameObject>(64);
        private int ringHead;
        private Material scorchMaterial;
        private bool scorchMaterialSearched;
        private bool loggedFirstMark;

        private static FireAndDestructionSettings Fire => Plugin.Settings.FireAndDestruction;
        private static DiagnosticSettings Diagnostics => Plugin.Settings.Diagnostics;

        private static int QueueCapacity => Fire.ImpactScorchQueue;
        private static int PerFrame => Fire.ImpactScorchesPerFrame;
        private static int MaximumMarks => Fire.MaximumImpactScorches;

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            Clear();
            if (Instance == this) Instance = null;
        }

        public void ResetForScene() => Clear();

        /// <summary>
        /// Queue an explosive impact for a scorch mark. Called from the missile/bomb impact
        /// patches on every client; server authority is irrelevant for a local decal.
        /// </summary>
        internal void SubmitExplosion(GlobalPosition position, float blastYield)
        {
            if (GameManager.IsHeadless ||
                !Fire.ImpactScorchEnabled.Value) return;
            // A missile salvo cannot burst-cast: the queue is bounded and drained a couple
            // of impacts per frame.
            if (pending.Count >= QueueCapacity) return;
            pending.Enqueue(new PendingExplosion { Position = position, BlastYield = blastYield });
        }

        private void Update()
        {
            int budget = PerFrame;
            while (budget-- > 0 && pending.Count > 0)
                ProcessExplosion(pending.Dequeue());
        }

        private void ProcessExplosion(PendingExplosion explosion)
        {
            if (GameAssets.i == null || GameAssets.i.scorchMarkDecal == null) return;

            Vector3 local = explosion.Position.ToLocalPosition();
            float searchRadius = Mathf.Clamp(8f + explosion.BlastYield * 0.8f, 8f, 40f);
            int count = Physics.OverlapSphereNonAlloc(
                local, searchRadius, overlapBuffer, PhysicsLayers.StaticsMask,
                QueryTriggerInteraction.Collide);

            Collider nearest = null;
            float nearestSq = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider collider = overlapBuffer[i];
                if (collider == null) continue;
                Transform root = BuildingRootOf(collider);
                if (root == null) continue;
                float distance = (collider.bounds.center - local).sqrMagnitude;
                if (distance >= nearestSq) continue;
                nearestSq = distance;
                nearest = collider;
            }
            if (nearest == null) return;

            Vector3 buildingCenter = nearest.bounds.center;
            Vector3 toCenter = (buildingCenter - local).normalized;
            if (toCenter.sqrMagnitude < 0.01f) toCenter = Vector3.forward;

            Vector3 point = local;
            Vector3 normal = -toCenter;

            // Cast ray from blast towards building center to find wall surface
            if (Physics.Raycast(local - toCenter * 1.5f, toCenter, out RaycastHit hit, toCenter.magnitude + 40f, PhysicsLayers.StaticsMask))
            {
                point = hit.point;
                normal = hit.normal;
            }
            else
            {
                // Cast from building perimeter back towards the blast
                Vector3 toBlast = (local - buildingCenter).normalized;
                if (Physics.Raycast(buildingCenter + toBlast * 40f, -toBlast, out RaycastHit hitBack, 55f, PhysicsLayers.StaticsMask))
                {
                    point = hitBack.point;
                    normal = hitBack.normal;
                }
            }

            PlaceMark(point, normal, explosion.BlastYield);
        }

        private void PlaceMark(Vector3 point, Vector3 normal, float blastYield)
        {
            GameObject mark = AcquireMark();
            if (mark == null) return;
            DecalProjector projector = mark.GetComponent<DecalProjector>();
            if (projector == null) return;

            uint seed = Deterministic.Hash(
                Mathf.RoundToInt(point.x), Mathf.RoundToInt(point.y), Mathf.RoundToInt(point.z),
                SeedSalt);

            float size = ImpactScorchPolicy.DecalSize(blastYield);
            float depth = Mathf.Clamp(size * 0.22f, 1.6f, 4.0f);

            Quaternion facing = Quaternion.LookRotation(-normal, Vector3.up);
            Quaternion roll = Quaternion.AngleAxis(ImpactScorchPolicy.RollDegrees(seed), -normal);
            Transform t = mark.transform;
            t.rotation = roll * facing;

            Vector3 position = point + normal * (depth * 0.45f);
            position += t.right * ImpactScorchPolicy.JitterOffset(
                size, ImpactScorchPolicy.TangentJitter(seed));
            position += t.up * ImpactScorchPolicy.JitterOffset(
                size, ImpactScorchPolicy.BitangentJitter(seed));
            t.position = position;

            projector.renderingLayerMask = ~0u;
            projector.size = new Vector3(size, size, depth);
            projector.startAngleFade = 45f;
            projector.endAngleFade = 70f;
            projector.fadeFactor = 0.98f;
            projector.drawDistance = 3500f;

            Material scorch = ResolveScorchMaterial();
            if (scorch != null) projector.material = scorch;

            if (!loggedFirstMark && Diagnostics.VerboseLogging.Value)
            {
                loggedFirstMark = true;
                Plugin.Logger.LogInfo(
                    $"Impact scorch: first mark placed at {point} (size {size:0.#}m, " +
                    $"material '{(projector.material != null ? projector.material.name : "null")}').");
            }
        }

        private GameObject AcquireMark()
        {
            if (marks.Count < MaximumMarks)
            {
                GameObject prefab = GameAssets.i != null ? GameAssets.i.scorchMarkDecal : null;
                if (prefab == null) return null;
                GameObject created = Object.Instantiate(prefab, Datum.origin, false);
                created.name = "BoscaliSummer.ImpactScorch";
                created.SetActive(true);
                marks.Add(created);
                return created;
            }
            // Pool full: recycle the oldest mark in ring order for this newest hit.
            GameObject oldest = marks[ringHead];
            ringHead = (ringHead + 1) % marks.Count;
            if (oldest != null) oldest.SetActive(true);
            return oldest;
        }

        private static Transform BuildingRootOf(Collider collider)
        {
            MapBuilding map = collider.GetComponentInParent<MapBuilding>();
            if (map != null) return map.transform;
            Building network = collider.GetComponentInParent<Building>();
            return network != null ? network.transform : null;
        }

        private Material ResolveScorchMaterial()
        {
            if (scorchMaterialSearched) return scorchMaterial;
            scorchMaterialSearched = true;

            // Prefer a material actually built on the vanilla scorch-mark decal shader, then
            // fall back to whatever a DecalSpawner carries, scoring names so a crater or
            // shockwave decal never wins over real soot.
            int bestScore = 0;
            Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < materials.Length; i++)
                ScoreScorchCandidate(materials[i], ref bestScore);

            FieldInfo field = AccessTools.Field(typeof(DecalSpawner), "decalMaterial");
            if (field != null)
            {
                DecalSpawner[] spawners = Resources.FindObjectsOfTypeAll<DecalSpawner>();
                for (int i = 0; i < spawners.Length; i++)
                    if (spawners[i] != null)
                        ScoreScorchCandidate(field.GetValue(spawners[i]) as Material, ref bestScore);
            }
            if (Diagnostics.VerboseLogging.Value)
                Plugin.Logger.LogInfo(scorchMaterial != null
                    ? $"Impact scorch decal material resolved: '{scorchMaterial.name}' " +
                      $"(shader '{scorchMaterial.shader.name}', score {bestScore})."
                    : "Impact scorch decal material unavailable; keeping prefab default.");
            return scorchMaterial;
        }

        private void ScoreScorchCandidate(Material candidate, ref int bestScore)
        {
            if (candidate == null || candidate.shader == null) return;
            string shader = candidate.shader.name.ToLowerInvariant();
            string name = candidate.name.ToLowerInvariant();
            int score = 0;
            if (shader.Contains("scorchmark")) score += 400;
            if (name.Contains("scorch")) score += 200;
            if (name.Contains("soot") || name.Contains("burn") || name.Contains("char")) score += 120;
            if (name.Contains("crater") || shader.Contains("crater")) score -= 300;
            if (shader.Contains("shockwave") || name.Contains("shockwave")) score -= 300;
            if (score <= bestScore) return;
            bestScore = score;
            scorchMaterial = candidate;
        }

        private void Clear()
        {
            for (int i = 0; i < marks.Count; i++)
                if (marks[i] != null) Object.Destroy(marks[i]);
            marks.Clear();
            pending.Clear();
            ringHead = 0;
            scorchMaterial = null;
            scorchMaterialSearched = false;
            loggedFirstMark = false;
        }
    }
}
