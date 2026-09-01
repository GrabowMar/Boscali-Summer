using System.Collections.Generic;
using BoscaliSummer.Runtime;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Fire
{
    [HarmonyPatch(typeof(MapBuilding), "TakeDamage")]
    internal static class MapBuildingDamagePatch
    {
        private struct DamageState
        {
            public float HitPoints;
        }

        private struct RuinGeometry
        {
            public GlobalPosition RuinPosition;
            public Vector2 HalfExtents;
        }

        private static bool Prepare() => GameAccess.MapBuildingHitPointsAvailable;
        private static void Prefix(MapBuilding __instance, out DamageState __state)
        {
            __state = CaptureState(__instance);
        }

        private static void Postfix(MapBuilding __instance, DamageState __state)
        {
            float hp = __instance != null ? GameAccess.GetMapBuildingHitPoints(__instance) : 0f;
            if (__state.HitPoints > 0f && hp <= 0f)
            {
                ModNet.ForgetBuildingDamage(__instance.transform.GlobalPosition());
                if (IsServer())
                {
                    RuinGeometry ruin = CaptureRuinGeometry(__instance);
                    RuinAftermathManager.Instance?.RegisterRuin(
                        ruin.RuinPosition, ruin.HalfExtents, 0f, true, true);
                }
            }
            if (!Plugin.Settings.BuildingDamageEnabled.Value || __instance == null) return;
            if (hp > 0f)
            {
                float severity = BuildingDamageVisual.ObserveDamage(
                    __instance, __state.HitPoints, hp);
                if (severity <= 0f) return;
                ModNet.BroadcastBuildingDamage(__instance.transform.GlobalPosition(), severity);
            }
        }

        private static DamageState CaptureState(MapBuilding building) => new DamageState
        {
            HitPoints = GameAccess.GetMapBuildingHitPoints(building)
        };

        private static RuinGeometry CaptureRuinGeometry(MapBuilding building)
        {
            var geometry = new RuinGeometry
            {
                RuinPosition = building.transform.GlobalPosition(),
                HalfExtents = new Vector2(8f, 8f)
            };
            Renderer[] renderers = building.GetComponentsInChildren<Renderer>(false);
            Bounds bounds = default(Bounds);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer ||
                    !renderer.gameObject.activeInHierarchy) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            if (found)
            {
                geometry.HalfExtents = new Vector2(
                    Mathf.Max(3f, bounds.extents.x), Mathf.Max(3f, bounds.extents.z));
                Vector3 anchor = bounds.center;
                anchor.y = bounds.min.y + 0.5f;
                geometry.RuinPosition = anchor.ToGlobalPosition();
            }
            return geometry;
        }

        private static bool IsServer()
        {
            try { return NetworkManagerNuclearOption.i != null && NetworkManagerNuclearOption.i.Server.Active; }
            catch { return false; }
        }
    }

    internal sealed class BuildingDamageVisual : MonoBehaviour
    {
        private static readonly int HitPointsId = Shader.PropertyToID("_HitPoints");
        private static readonly int DamageId = Shader.PropertyToID("_Damage");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly int GlossinessId = Shader.PropertyToID("_Glossiness");
        private static readonly Collider[] LookupColliders = new Collider[32];

        private float appliedSeverity;
        private float observedPeakHitPoints;
        private Renderer[] cachedRenderers;
        private Bounds cachedBounds;
        private bool hasCachedBounds;
        private readonly List<GameObject> decals = new List<GameObject>(2);
        private readonly MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();

        public static void Apply(MapBuilding building, float severity = 0.62f)
        {
            if (building == null) return;
            BuildingDamagePresentationManager manager = BuildingDamagePresentationManager.Instance;
            if (manager != null)
            {
                manager.Enqueue(building, severity);
                return;
            }
            ApplyImmediate(building, severity, null);
        }

        internal static void ApplyStage(MapBuilding building, BuildingDamageStage stage)
        {
            float severity = BuildingDamagePolicy.Severity(stage);
            if (severity > 0f) Apply(building, severity);
        }

        internal static float ObserveDamage(MapBuilding building, float beforeHitPoints, float hitPoints)
        {
            if (building == null || hitPoints <= 0f) return 0f;
            BuildingDamageVisual state = building.GetComponent<BuildingDamageVisual>();
            if (state == null) state = building.gameObject.AddComponent<BuildingDamageVisual>();
            state.observedPeakHitPoints = Mathf.Max(
                state.observedPeakHitPoints,
                Mathf.Max(BuildingDamagePolicy.MinimumEstimatedHitPoints, beforeHitPoints));
            BuildingDamageStage stage = BuildingDamagePolicy.FromDamage(
                beforeHitPoints, hitPoints, state.observedPeakHitPoints);
            float severity = BuildingDamagePolicy.Severity(stage);
            if (severity > 0f) Apply(building, severity);
            return severity;
        }

        internal static void ApplyImmediate(
            MapBuilding building, float severity, BuildingDamagePresentationManager manager)
        {
            if (building == null) return;
            BuildingDamageVisual state = building.GetComponent<BuildingDamageVisual>();
            if (state == null) state = building.gameObject.AddComponent<BuildingDamageVisual>();
            state.ApplyVisual(building, severity);
            manager?.Register(state);
        }

        internal static bool ApplyNearest(GlobalPosition position, float severity = 0.62f)
        {
            Vector3 local = position.ToLocalPosition();
            MapBuilding nearest = null;
            float nearestSq = 28f * 28f;
            int count = Physics.OverlapSphereNonAlloc(
                local, 28f, LookupColliders, PhysicsLayers.StaticsMask,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider collider = LookupColliders[i];
                MapBuilding candidate = collider != null
                    ? collider.GetComponentInParent<MapBuilding>()
                    : null;
                if (candidate == null) continue;
                float distance = (candidate.transform.position - local).sqrMagnitude;
                if (distance < nearestSq) { nearestSq = distance; nearest = candidate; }
            }
            if (nearest == null) return false;
            Apply(nearest, severity);
            return true;
        }

        internal static float QuantizeSeverity(float severity) =>
            BuildingDamagePolicy.Severity(BuildingDamagePolicy.FromSeverity(severity));

        internal bool DecalSelection { get; set; }
        internal int SelectedDamageDecals { get; set; }
        internal int DesiredDamageDecals => appliedSeverity >= 0.72f ? 2 : appliedSeverity > 0f ? 1 : 0;
        internal Vector3 WorldCenter => hasCachedBounds ? cachedBounds.center : transform.position;

        private void ApplyVisual(MapBuilding building, float severity)
        {
            // Three visual tiers are enough to communicate progression and prevent every
            // small HP change from walking every facade renderer again during a barrage.
            severity = QuantizeSeverity(severity);
            if (severity <= appliedSeverity) return;
            appliedSeverity = severity;
            if (cachedRenderers == null)
            {
                cachedRenderers = building.GetComponentsInChildren<Renderer>(true);
                hasCachedBounds = TryGetBounds(cachedRenderers, out cachedBounds);
            }
            Renderer[] target = cachedRenderers;
            int nativeSlots = 0;
            int sootSlots = 0;
            int totalSlots = 0;
            for (int i = 0; i < target.Length; i++)
            {
                Renderer renderer = target[i];
                if (renderer == null || renderer is ParticleSystemRenderer) continue;

                Material[] materials = renderer.sharedMaterials;
                Vector3 center = renderer.bounds.center;
                float variation = Mathf.PerlinNoise(center.x * 0.031f, center.z * 0.031f);
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null) continue;
                    totalSlots++;
                    float slotVariation = Mathf.Repeat(variation + materialIndex * 0.371f, 1f);
                    bool native = material.HasProperty(HitPointsId) || material.HasProperty(DamageId);
                    if (native)
                    {
                        // Property blocks drive the native damage shader without cloning
                        // every material on the building (a major source of memory churn).
                        propertyBlock.Clear();
                        renderer.GetPropertyBlock(propertyBlock, materialIndex);
                        if (material.HasProperty(HitPointsId))
                            propertyBlock.SetFloat(HitPointsId, Mathf.Lerp(58f, 8f,
                            Mathf.Clamp01(severity * Mathf.Lerp(0.86f, 1.12f, slotVariation))));
                        if (material.HasProperty(DamageId))
                            propertyBlock.SetFloat(DamageId, Mathf.Clamp01(
                            severity * Mathf.Lerp(0.82f, 1.16f, slotVariation)));
                        renderer.SetPropertyBlock(propertyBlock, materialIndex);
                        nativeSlots++;
                        continue;
                    }

                    // Scenery shaders have no damage mask. Preserve their hue and detail;
                    // decals provide the obvious blast damage while this restrained warm
                    // soot treatment only knocks back clean paint and glowing windows.
                    float coverage = Mathf.Lerp(0.24f, 0.52f, severity);
                    if (slotVariation > coverage && (nativeSlots > 0 || sootSlots > 0 || totalSlots > 1)) continue;
                    int colorProperty = material.HasProperty(BaseColorId) ? BaseColorId :
                        (material.HasProperty(ColorId) ? ColorId : -1);
                    if (colorProperty < 0) continue;
                    Color original = material.GetColor(colorProperty);
                    float amount = severity * Mathf.Lerp(0.18f, 0.38f, slotVariation);
                    Color soot = Color.Lerp(original,
                        new Color(original.r * 0.24f, original.g * 0.20f,
                            original.b * 0.17f, original.a), amount);
                    propertyBlock.Clear();
                    renderer.GetPropertyBlock(propertyBlock, materialIndex);
                    propertyBlock.SetColor(colorProperty, soot);
                    if (material.HasProperty(EmissionColorId))
                        propertyBlock.SetColor(EmissionColorId,
                            material.GetColor(EmissionColorId) * Mathf.Lerp(0.58f, 0.16f, severity));
                    if (material.HasProperty(SmoothnessId))
                        propertyBlock.SetFloat(SmoothnessId,
                            material.GetFloat(SmoothnessId) * Mathf.Lerp(0.78f, 0.42f, severity));
                    if (material.HasProperty(GlossinessId))
                        propertyBlock.SetFloat(GlossinessId,
                            material.GetFloat(GlossinessId) * Mathf.Lerp(0.78f, 0.42f, severity));
                    renderer.SetPropertyBlock(propertyBlock, materialIndex);
                    sootSlots++;
                }
            }

            RefreshDamageDecals(severity);

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"Applied damaged visual to map building at {building.transform.GlobalPosition()}: " +
                    $"native damage slots={nativeSlots}/{totalSlots}, soot fallback slots={sootSlots}.");
        }

        internal void SetDamageDecalCount(
            int desired, BuildingDamagePresentationManager manager)
        {
            desired = Mathf.Clamp(desired, 0, DesiredDamageDecals);
            while (decals.Count > desired)
            {
                int last = decals.Count - 1;
                manager.ReleaseDamageDecal(decals[last]);
                decals.RemoveAt(last);
            }
            while (decals.Count < desired && hasCachedBounds)
            {
                GameObject decal = manager.AcquireDamageDecal();
                if (decal == null) break;
                decals.Add(decal);
                ConfigureDamageDecal(decal, decals.Count - 1, appliedSeverity);
            }
        }

        internal void ReleaseDamageDecals(BuildingDamagePresentationManager manager)
        {
            for (int i = 0; i < decals.Count; i++) manager.ReleaseDamageDecal(decals[i]);
            decals.Clear();
        }

        internal void ForgetDamageDecals() => decals.Clear();

        private void RefreshDamageDecals(float severity)
        {
            for (int i = 0; i < decals.Count; i++)
                if (decals[i] != null) ConfigureDamageDecal(decals[i], i, severity);
        }

        private void ConfigureDamageDecal(GameObject decal, int index, float severity)
        {
            if (decal == null || !hasCachedBounds) return;
            DecalProjector projector = decal.GetComponent<DecalProjector>();
            if (projector == null) return;

            Bounds bounds = cachedBounds;
            uint seed = BoscaliSummer.Core.Deterministic.Hash(
                Mathf.RoundToInt(bounds.center.x), Mathf.RoundToInt(bounds.center.z), index, 0x5d3a91);
            float offsetA = BoscaliSummer.Core.Deterministic.UnitFloat(seed) * 2f - 1f;
            float offsetB = BoscaliSummer.Core.Deterministic.UnitFloat(seed ^ 0x9e3779b9u) * 2f - 1f;
            int surface = index == 0 ? (int)(seed & 3u) : (int)((seed >> 3) % 5u);
            Vector3 normal;
            Vector3 position;
            Vector2 size;
            if (surface == 4)
            {
                normal = Vector3.up;
                position = new Vector3(
                    bounds.center.x + offsetA * bounds.extents.x * 0.42f,
                    bounds.max.y + 0.08f,
                    bounds.center.z + offsetB * bounds.extents.z * 0.42f);
                size = new Vector2(
                    Mathf.Clamp(bounds.size.x * 0.48f, 5f, 18f),
                    Mathf.Clamp(bounds.size.z * 0.48f, 5f, 18f));
                decal.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
            }
            else
            {
                bool xSide = surface < 2;
                float sign = (surface & 1) == 0 ? -1f : 1f;
                normal = xSide ? Vector3.right * sign : Vector3.forward * sign;
                position = bounds.center + normal * (xSide ? bounds.extents.x : bounds.extents.z);
                position.y += offsetB * bounds.extents.y * 0.24f;
                if (xSide) position.z += offsetA * bounds.extents.z * 0.42f;
                else position.x += offsetA * bounds.extents.x * 0.42f;
                size = new Vector2(
                    Mathf.Clamp((xSide ? bounds.size.z : bounds.size.x) * 0.44f, 5f, 16f),
                    Mathf.Clamp(bounds.size.y * 0.46f, 5f, 18f));
                decal.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);
            }
            decal.transform.position = position + normal * 0.06f;
            projector.size = new Vector3(size.x, size.y, 2.4f);
            projector.fadeFactor = Mathf.Lerp(0.58f, 0.92f, severity);
            projector.drawDistance = 2600f;
        }

        private static bool TryGetBounds(Renderer[] renderers, out Bounds bounds)
        {
            bounds = default(Bounds);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || renderer is ParticleSystemRenderer) continue;
                if (!found) { bounds = renderer.bounds; found = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return found;
        }

        private void OnDestroy()
        {
            BuildingDamagePresentationManager.Instance?.Unregister(this);
        }
    }
}
