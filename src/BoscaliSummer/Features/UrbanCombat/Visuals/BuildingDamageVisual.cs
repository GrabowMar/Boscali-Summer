using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// High-performance building surface damage visualizer inspired by Damage FX.
    /// Modulates standard URP Lit shader properties directly via MaterialPropertyBlock:
    /// - Progressive soot/charring and surface roughness as structural integrity declines.
    /// - Molten incandescent heat flash (_EmissionColor) on explosive/kinetic impact that cools over time.
    /// - Zero material cloning, zero GC allocations per frame, full SRP batching/GPU instancing.
    /// - Automatically sleeps when thermal heat has dissipated to eliminate idle CPU overhead.
    /// </summary>
    internal sealed class BuildingDamageVisual : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

        private static readonly Color SootColor = new Color(0.22f, 0.20f, 0.19f, 1f);
        private static readonly Color MoltenHeatColor = new Color(1.85f, 0.65f, 0.15f, 1f);

        private const float CoolingRate = 0.65f; // Dissipates in ~2.5s
        private const float MaxViewingDistanceSq = 3500f * 3500f;

        private readonly List<Renderer> targetRenderers = new List<Renderer>(8);
        private MaterialPropertyBlock propertyBlock;
        private float damageFraction; // 0 = pristine, 1 = destroyed
        private float heatGlow;       // 0 = cool, >0 = molten heat flash
        private bool isDirty;
        private bool isInitialized;

        public static BuildingDamageVisual GetOrAdd(GameObject buildingRoot)
        {
            if (buildingRoot == null) return null;
            BuildingDamageVisual visual = buildingRoot.GetComponent<BuildingDamageVisual>();
            if (visual == null) visual = buildingRoot.AddComponent<BuildingDamageVisual>();
            return visual;
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void EnsureInitialized()
        {
            if (isInitialized) return;
            isInitialized = true;
            propertyBlock = new MaterialPropertyBlock();

            Renderer[] all = GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r == null || !r.enabled || r is ParticleSystemRenderer || r is TrailRenderer)
                    continue;
                targetRenderers.Add(r);
            }
        }

        public void ApplyDamage(float currentHp, float maxHp, Vector3 impactPoint, float blastYield, float rawDamage)
        {
            if (!Plugin.Settings.UrbanCombat.DamageShaderEnabled.Value) return;
            EnsureInitialized();
            if (targetRenderers.Count == 0) return;

            float newFraction = StrongholdDamagePolicy.CalculateDamageFraction(currentHp, maxHp);
            damageFraction = Mathf.Max(damageFraction, newFraction);

            if (Plugin.Settings.UrbanCombat.DamageHeatGlowEnabled.Value)
            {
                float heatAddition = StrongholdDamagePolicy.CalculateThermalGlowAddition(rawDamage, blastYield);
                heatGlow = Mathf.Clamp(heatGlow + heatAddition, 0f, 2.2f);
            }

            isDirty = true;
            enabled = true; // Wake up component if sleeping
            UpdateProperties();

            // Spawn localized surface breach / scorch at impact point if provided
            if (impactPoint != Vector3.zero && blastYield > 0.5f)
            {
                PlaceLocalizedBreach(impactPoint, blastYield);
            }
        }

        private void Update()
        {
            if (!isDirty)
            {
                enabled = false;
                return;
            }

            if (heatGlow > 0f)
            {
                heatGlow = StrongholdDamagePolicy.CoolThermalGlow(heatGlow, CoolingRate, Time.deltaTime);
                UpdateProperties();

                if (heatGlow <= 0.001f)
                {
                    heatGlow = 0f;
                    UpdateProperties();
                    isDirty = false;
                    enabled = false; // Go to sleep: 0 CPU cost when cool
                }
            }
            else
            {
                isDirty = false;
                enabled = false;
            }
        }

        private void UpdateProperties()
        {
            // Calculate progressive soot charring from policy
            var (sr, sg, sb) = StrongholdDamagePolicy.CalculateSootTint(damageFraction);
            Color charTint = new Color(sr, sg, sb, 1f);

            // Calculate molten thermal glow
            Color emission = heatGlow > 0.001f ? MoltenHeatColor * heatGlow : Color.black;
            // Concrete pulverization / smoothness loss
            float smoothness = Mathf.Lerp(0.5f, 0.02f, damageFraction);

            propertyBlock.SetColor(BaseColorId, charTint);
            propertyBlock.SetColor(ColorId, charTint);
            propertyBlock.SetColor(EmissionColorId, emission);
            propertyBlock.SetFloat(SmoothnessId, smoothness);

            for (int i = 0; i < targetRenderers.Count; i++)
            {
                Renderer r = targetRenderers[i];
                if (r != null) r.SetPropertyBlock(propertyBlock);
            }
        }

        private void PlaceLocalizedBreach(Vector3 impactPoint, float blastYield)
        {
            if (GameAssets.i == null || GameAssets.i.scorchMarkDecal == null) return;
            // Reject impacts beyond camera drawing range
            Camera cam = Camera.main;
            if (cam != null && (cam.transform.position - impactPoint).sqrMagnitude > MaxViewingDistanceSq)
                return;

            Vector3 outward = (impactPoint - transform.position);
            outward.y = 0f;
            Vector3 normal = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.up;

            float size = Mathf.Clamp(3.5f + blastYield * 0.45f, 3f, 15f);
            Quaternion facing = Quaternion.LookRotation(-normal, Vector3.up);

            GameObject mark = Instantiate(GameAssets.i.scorchMarkDecal, impactPoint + normal * 0.08f, facing, transform);
            mark.name = "BoscaliSummer.StrongholdBreach";
            mark.SetActive(true);

            DecalProjector projector = mark.GetComponent<DecalProjector>();
            if (projector != null)
            {
                projector.size = new Vector3(size, size, size * 0.25f);
                projector.fadeFactor = 0.9f;
                projector.drawDistance = 2400f;
            }
        }

        public void ResetForScene()
        {
            if (propertyBlock != null)
            {
                for (int i = 0; i < targetRenderers.Count; i++)
                {
                    Renderer r = targetRenderers[i];
                    if (r != null) r.SetPropertyBlock(null);
                }
            }
            targetRenderers.Clear();
            damageFraction = 0f;
            heatGlow = 0f;
            isDirty = false;
            isInitialized = false;
            enabled = false;
        }

        private void OnDestroy()
        {
            ResetForScene();
        }
    }
}
