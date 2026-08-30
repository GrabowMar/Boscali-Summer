using System.Collections.Generic;
using BoscaliSummer.Runtime;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Fire
{
    [HarmonyPatch(typeof(MapBuilding), "TakeDamage")]
    internal static class MapBuildingDamagePatch
    {
        private struct DamageState
        {
            public float HitPoints;
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
            if (__state.HitPoints > 0f && hp <= 0f && IsServer())
                RuinAftermathManager.Instance?.RegisterRuin(
                    __state.RuinPosition, __state.HalfExtents, 0f, true, true);
            if (!Plugin.Settings.BuildingDamageEnabled.Value || __instance == null) return;
            float threshold = Plugin.Settings.BuildingDamagedHitPoints;
            if (hp > 0f && hp <= threshold)
            {
                float severity = Mathf.Lerp(0.38f, 0.96f,
                    Mathf.Clamp01(1f - hp / Mathf.Max(threshold, 1f)));
                BuildingDamageVisual.Apply(__instance, severity);
                if (__state.HitPoints > threshold)
                    ModNet.BroadcastBuildingDamage(__instance.transform.GlobalPosition());
            }
        }

        private static DamageState CaptureState(MapBuilding building)
        {
            var state = new DamageState
            {
                HitPoints = GameAccess.GetMapBuildingHitPoints(building),
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
                state.HalfExtents = new Vector2(
                    Mathf.Max(3f, bounds.extents.x), Mathf.Max(3f, bounds.extents.z));
                Vector3 anchor = bounds.center;
                anchor.y = bounds.min.y + 0.5f;
                state.RuinPosition = anchor.ToGlobalPosition();
            }
            return state;
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

        private float appliedSeverity;
        private float predictedHitPoints = 100f;
        private readonly Dictionary<Material, Color> originalColors = new Dictionary<Material, Color>();
        private readonly Dictionary<Material, Color> originalEmissions = new Dictionary<Material, Color>();

        public static void Apply(MapBuilding building, float severity = 0.62f)
        {
            if (building == null) return;
            BuildingDamageVisual state = building.GetComponent<BuildingDamageVisual>();
            if (state == null) state = building.gameObject.AddComponent<BuildingDamageVisual>();
            state.ApplyVisual(building, severity);
        }

        internal static bool ApplyNearest(GlobalPosition position)
        {
            MapBuilding[] buildings = Resources.FindObjectsOfTypeAll<MapBuilding>();
            MapBuilding nearest = null;
            float nearestSq = 100f;
            for (int i = 0; i < buildings.Length; i++)
            {
                MapBuilding candidate = buildings[i];
                if (candidate == null || !candidate.gameObject.scene.IsValid()) continue;
                float distance = (candidate.transform.GlobalPosition() - position).sqrMagnitude;
                if (distance < nearestSq) { nearestSq = distance; nearest = candidate; }
            }
            if (nearest == null) return false;
            Apply(nearest, 0.62f);
            return true;
        }

        public static void PredictImpact(MapBuilding building, float pierceDamage, float blastDamage)
        {
            if (!Plugin.Settings.BuildingDamageEnabled.Value || building == null) return;
            BuildingDamageVisual state = building.GetComponent<BuildingDamageVisual>();
            if (state == null) state = building.gameObject.AddComponent<BuildingDamageVisual>();
            ArmorProperties armor = building.GetArmorProperties();
            float pierce = Mathf.Max(pierceDamage - armor.pierceArmor, 0f) / Mathf.Max(armor.pierceTolerance, 0.01f);
            float blast = Mathf.Max(blastDamage - armor.blastArmor, 0f) / Mathf.Max(armor.blastTolerance, 0.01f);
            state.predictedHitPoints -= pierce + blast;
            if (state.predictedHitPoints <= Plugin.Settings.BuildingDamagedHitPoints)
                state.ApplyVisual(building, 0.72f);
        }

        private void ApplyVisual(MapBuilding building, float severity)
        {
            severity = Mathf.Clamp01(severity);
            if (severity <= appliedSeverity + 0.025f) return;
            appliedSeverity = severity;
            Renderer[] target = building.GetComponentsInChildren<Renderer>(true);
            int nativeSlots = 0;
            int sootSlots = 0;
            int totalSlots = 0;
            for (int i = 0; i < target.Length; i++)
            {
                Renderer renderer = target[i];
                if (renderer == null || renderer is ParticleSystemRenderer) continue;

                // Match Nuclear Option's native UnitPart/ShipPart damage path: it writes
                // _HitPoints directly to instantiated renderer materials. Property blocks do
                // not reliably reach the shaders used by streamed MapBuilding facades.
                Material[] materials = renderer.materials;
                Vector3 center = renderer.bounds.center;
                float variation = Mathf.PerlinNoise(center.x * 0.031f, center.z * 0.031f);
                for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
                {
                    Material material = materials[materialIndex];
                    if (material == null) continue;
                    totalSlots++;
                    float slotVariation = Mathf.Repeat(variation + materialIndex * 0.371f, 1f);
                    bool native = material.HasProperty(HitPointsId) || material.HasProperty(DamageId);
                    if (material.HasProperty(HitPointsId))
                        material.SetFloat(HitPointsId, Mathf.Lerp(58f, 8f,
                            Mathf.Clamp01(severity * Mathf.Lerp(0.86f, 1.12f, slotVariation))));
                    if (material.HasProperty(DamageId))
                        material.SetFloat(DamageId, Mathf.Clamp01(
                            severity * Mathf.Lerp(0.82f, 1.16f, slotVariation)));
                    if (native)
                    {
                        nativeSlots++;
                        continue;
                    }

                    // Many scenery facades use simple shaders with no damage mask. Give only
                    // scattered renderer/material sections a restrained soot treatment so the
                    // intact geometry and most original facade colour remain visible.
                    float coverage = Mathf.Lerp(0.34f, 0.68f, severity);
                    if (slotVariation > coverage && (nativeSlots > 0 || sootSlots > 0 || totalSlots > 1)) continue;
                    int colorProperty = material.HasProperty(BaseColorId) ? BaseColorId :
                        (material.HasProperty(ColorId) ? ColorId : -1);
                    if (colorProperty < 0) continue;
                    Color original;
                    if (!originalColors.TryGetValue(material, out original))
                    {
                        original = material.GetColor(colorProperty);
                        originalColors.Add(material, original);
                    }
                    float luminance = original.r * 0.2126f + original.g * 0.7152f + original.b * 0.0722f;
                    Color ash = Color.Lerp(original, new Color(luminance, luminance, luminance, original.a), 0.18f);
                    float darken = Mathf.Lerp(0.78f, 0.48f,
                        severity * Mathf.Lerp(0.72f, 1.08f, slotVariation));
                    ash.r *= darken;
                    ash.g *= darken;
                    ash.b *= darken;
                    ash.a = original.a;
                    material.SetColor(colorProperty, ash);
                    if (material.HasProperty(EmissionColorId))
                    {
                        Color emission;
                        if (!originalEmissions.TryGetValue(material, out emission))
                        {
                            emission = material.GetColor(EmissionColorId);
                            originalEmissions.Add(material, emission);
                        }
                        material.SetColor(EmissionColorId,
                            emission * Mathf.Lerp(0.45f, 0.08f, severity));
                    }
                    sootSlots++;
                }
            }

            if (Plugin.Settings.VerboseLogging.Value)
                Plugin.Logger.LogInfo($"Applied damaged visual to map building at {building.transform.GlobalPosition()}: " +
                    $"native damage slots={nativeSlots}/{totalSlots}, soot fallback slots={sootSlots}.");
        }
    }
}
