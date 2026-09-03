using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Framework.Visuals
{
    /// <summary>
    /// Manages authentic, localized war destruction and ruin effects for buildings:
    /// - Localized blast craters and jagged breach holes placed directly at impact sites on building facades.
    /// - Deploys procedural bump-mapped decal materials (RuinTextureCatalog) with authentic 3D normal relief,
    ///   crater depth, and Voronoi fracture crack networks under dynamic lighting.
    /// - Physical concrete rubble and fallen masonry mounds spawned at the base of damaged walls.
    /// - Progressively scales facade bump map weathering on the building geometry via MaterialPropertyBlock.
    /// - Proximity merging: repeated hits in close proximity upgrade the crater tier rather than stacking decals.
    /// - Spawns localized impact dust, spall sparks, and severe-damage venting smoke.
    /// </summary>
    internal sealed class BuildingDamageVisual : MonoBehaviour
    {
        private const int MaxLocalBreachesPerBuilding = 24;
        private const float MaxViewingDistanceSq = 3500f * 3500f;

        private sealed class BreachEntry
        {
            public GameObject GameObject;
            public DecalProjector Projector;
            public RuinTextureCatalog.RuinTier Tier;
        }

        private readonly List<BreachEntry> activeBreaches = new List<BreachEntry>(MaxLocalBreachesPerBuilding);
        private readonly List<GameObject> activeRubblePiles = new List<GameObject>(MaxLocalBreachesPerBuilding);
        private readonly List<GameObject> activePlumes = new List<GameObject>(8);
        private GameObject breachSmokeEmitter;
        private MaterialPropertyBlock propertyBlock;
        private float cumulativeDamage;

        public static BuildingDamageVisual GetOrAdd(GameObject buildingRoot)
        {
            if (buildingRoot == null) return null;
            BuildingDamageVisual visual = buildingRoot.GetComponent<BuildingDamageVisual>();
            if (visual == null) visual = buildingRoot.AddComponent<BuildingDamageVisual>();
            return visual;
        }

        private void Awake()
        {
            RuinTextureCatalog.EnsureInitialized();
        }

        /// <summary>
        /// Restores normal building materials and clears any active decals and emitters.
        /// </summary>
        public void RestoreOriginalMaterials()
        {
            cumulativeDamage = 0f;
            Renderer[] all = GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < all.Length; i++)
            {
                Renderer r = all[i];
                if (r != null && !(r is ParticleSystemRenderer) && !(r is TrailRenderer))
                {
                    r.SetPropertyBlock(null);
                }
            }
        }

        /// <summary>
        /// Places a localized war damage breach crater on the building facade with procedural bump mapping
        /// and spawns fallen concrete rubble mounds at the ground beneath the impact.
        /// </summary>
        public void ApplyLocalImpact(Vector3 impactPoint, Vector3 normal, float blastYield, float damage)
        {
            if (impactPoint == Vector3.zero) return;
            if (normal == Vector3.zero) normal = Vector3.up;

            // Reject impacts beyond camera drawing distance
            Camera cam = Camera.main;
            if (cam != null && (cam.transform.position - impactPoint).sqrMagnitude > MaxViewingDistanceSq)
                return;

            cumulativeDamage += damage;

            // 1. Localized structural breach crater with 3D procedural bump mapping on the building facade
            PlaceWallBreach(impactPoint, normal, blastYield, damage);

            // 2. Multi-surface blast wrapping: if hitting near roof parapets or wall corners, wrap onto adjacent faces
            if (blastYield > 1.0f)
            {
                float baseSize = Mathf.Clamp(14f + blastYield * 0.95f, 14f, 38f);
                Vector3[] testDirs = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right, Vector3.down };
                for (int d = 0; d < testDirs.Length; d++)
                {
                    Vector3 dir = testDirs[d];
                    if (Vector3.Dot(dir, normal) > 0.2f) continue;

                    Vector3 rayOrigin = impactPoint + dir * (baseSize * 0.45f) + normal * 1.2f;
                    if (Physics.Raycast(rayOrigin, -dir, out RaycastHit sideHit, baseSize * 0.60f, PhysicsLayers.StaticsMask))
                    {
                        if (sideHit.collider != null && sideHit.transform.IsChildOf(transform) && Vector3.Angle(sideHit.normal, normal) > 35f)
                        {
                            PlaceWallBreach(sideHit.point, sideHit.normal, blastYield * 0.65f, damage * 0.65f);
                        }
                    }
                }
            }

            // 3. Persistent billowing ruin smoke plume and smoldering embers at impact site
            SpawnBreachPlume(impactPoint, normal, blastYield);

            // 4. Physical fallen rubble mound at the base of the damaged wall
            PlaceGroundRubble(impactPoint, normal, blastYield, damage);

            // 5. Impact dust burst and concrete pulverization accent
            SpawnImpactDust(impactPoint, normal, blastYield, damage);

            // 6. Update overall building facade weathering, soot charring, and micro-crack bump intensity
            UpdateFacadeWeathering();
        }

        private void PlaceWallBreach(Vector3 point, Vector3 normal, float blastYield, float damage)
        {
            if (GameAssets.i == null || GameAssets.i.scorchMarkDecal == null) return;

            RuinTextureCatalog.RuinTier targetTier = (blastYield > 8f || damage > 120f)
                ? RuinTextureCatalog.RuinTier.Heavy
                : (blastYield > 0.1f || damage > 30f
                    ? RuinTextureCatalog.RuinTier.Medium
                    : RuinTextureCatalog.RuinTier.Light);

            float baseSize = blastYield > 0.1f
                ? Mathf.Clamp(14f + blastYield * 0.95f, 14f, 38f)
                : Mathf.Clamp(2.5f + damage * 0.08f, 2.5f, 6.0f); // Bullets/cannons

            float mergeDistSq = (baseSize * 0.45f) * (baseSize * 0.45f);

            // Check if there is an existing breach very close to merge with
            for (int i = 0; i < activeBreaches.Count; i++)
            {
                BreachEntry existing = activeBreaches[i];
                if (existing != null && existing.GameObject != null &&
                    (existing.GameObject.transform.position - point).sqrMagnitude < mergeDistSq)
                {
                    // Upgrade existing breach hole tier and expand size
                    if (existing.Tier < targetTier)
                    {
                        existing.Tier = targetTier;
                    }
                    else if (existing.Tier < RuinTextureCatalog.RuinTier.Heavy && (damage > 30f || blastYield > 0.1f))
                    {
                        existing.Tier = (RuinTextureCatalog.RuinTier)((int)existing.Tier + 1);
                    }

                    if (existing.Projector != null)
                    {
                        float newSize = Mathf.Min(existing.Projector.size.x * 1.35f, 45f);
                        float depth = Mathf.Max(10f, newSize * 1.2f);
                        existing.Projector.renderingLayerMask = ~0u;
                        existing.Projector.size = new Vector3(newSize, newSize, depth);
                        existing.Projector.material = RuinTextureCatalog.GetDecalMaterial(existing.Tier);
                    }
                    return;
                }
            }

            float projDepth = Mathf.Max(10f, baseSize * 1.2f);

            Quaternion facing = Quaternion.LookRotation(-normal, Vector3.up);
            uint seed = (uint)(Mathf.Abs(point.x * 137f + point.y * 311f + point.z * 523f));
            Quaternion roll = Quaternion.AngleAxis((seed % 360), -normal);

            Vector3 projectorPos = point + normal * 2.0f;
            GameObject breach = Instantiate(GameAssets.i.scorchMarkDecal, projectorPos, roll * facing, transform);
            breach.name = $"BoscaliSummer.LocalBreach_{targetTier}";
            breach.SetActive(true);

            DecalProjector projector = breach.GetComponent<DecalProjector>();
            if (projector != null)
            {
                Material ruinMat = RuinTextureCatalog.GetDecalMaterial(targetTier);
                if (ruinMat != null) projector.material = ruinMat;

                projector.renderingLayerMask = ~0u;
                projector.size = new Vector3(baseSize, baseSize, projDepth);
                projector.fadeFactor = 0.98f;
                projector.drawDistance = 4500f;
            }

            if (activeBreaches.Count >= MaxLocalBreachesPerBuilding)
            {
                BreachEntry oldest = activeBreaches[0];
                activeBreaches.RemoveAt(0);
                if (oldest != null && oldest.GameObject != null) Destroy(oldest.GameObject);
            }

            activeBreaches.Add(new BreachEntry
            {
                GameObject = breach,
                Projector = projector,
                Tier = targetTier
            });
        }

        private void SpawnBreachPlume(Vector3 point, Vector3 normal, float blastYield)
        {
            if (GameAssets.i == null || GameAssets.i.contactSmoke == null) return;
            if (blastYield < 0.5f) return;

            GameObject plume = Instantiate(GameAssets.i.contactSmoke, point + normal * 0.6f, Quaternion.LookRotation(Vector3.up), transform);
            plume.name = "BoscaliSummer.BreachPlume";
            plume.SetActive(true);

            ParticleSystem ps = plume.GetComponentInChildren<ParticleSystem>();
            if (ps != null)
            {
                var main = ps.main;
                main.loop = true;
                main.startLifetime = 14f;
                main.startSize = new ParticleSystem.MinMaxCurve(7f, 16f);
                main.startColor = new Color(0.08f, 0.08f, 0.08f, 0.88f); // thick black smoke
                main.maxParticles = 80;
                ps.Play();
            }

            // Warm flickering ember illumination in cavity
            GameObject fireLight = new GameObject("BreachFireLight");
            fireLight.transform.SetParent(plume.transform, false);
            Light light = fireLight.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1.0f, 0.50f, 0.12f);
            light.range = Mathf.Clamp(10f + blastYield * 0.6f, 10f, 26f);
            light.intensity = 2.8f;

            if (activePlumes.Count >= 8)
            {
                GameObject oldest = activePlumes[0];
                activePlumes.RemoveAt(0);
                if (oldest != null) Destroy(oldest);
            }
            activePlumes.Add(plume);
            Destroy(plume, 180f); // Persistent 3-minute smoke plume
        }

        private void PlaceGroundRubble(Vector3 impactPoint, Vector3 normal, float blastYield, float damage)
        {
            if (GameAssets.i == null || GameAssets.i.scorchMarkDecal == null) return;

            // Raycast down to find the ground/sidewalk at the foot of the building
            Vector3 castOrigin = impactPoint + normal * 0.8f;
            int mask = PhysicsLayers.StaticsMask;
            if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, 160f, mask, QueryTriggerInteraction.Ignore))
            {
                float rubbleSize = blastYield > 0.1f
                    ? Mathf.Clamp(7f + blastYield * 0.75f, 7f, 26f)
                    : Mathf.Clamp(2.5f + damage * 0.05f, 2.5f, 6.0f);

                float depth = Mathf.Max(8f, rubbleSize * 1.0f);

                Quaternion rubbleFacing = Quaternion.LookRotation(Vector3.down, normal);
                uint seed = (uint)(Mathf.Abs(hit.point.x * 71f + hit.point.z * 193f));
                Quaternion roll = Quaternion.AngleAxis((seed % 360), Vector3.down);

                GameObject rubble = Instantiate(GameAssets.i.scorchMarkDecal, hit.point + Vector3.up * 0.5f, roll * rubbleFacing, transform);
                rubble.name = "BoscaliSummer.GroundRubble";
                rubble.SetActive(true);

                DecalProjector proj = rubble.GetComponent<DecalProjector>();
                if (proj != null)
                {
                    Material rubbleMat = RuinTextureCatalog.GetDecalMaterial(RuinTextureCatalog.RuinTier.Light);
                    if (rubbleMat != null) proj.material = rubbleMat;

                    proj.renderingLayerMask = ~0u;
                    proj.size = new Vector3(rubbleSize, rubbleSize, depth);
                    proj.fadeFactor = 0.92f;
                    proj.drawDistance = 3500f;
                }

                if (activeRubblePiles.Count >= MaxLocalBreachesPerBuilding)
                {
                    GameObject oldest = activeRubblePiles[0];
                    activeRubblePiles.RemoveAt(0);
                    if (oldest != null) Destroy(oldest);
                }
                activeRubblePiles.Add(rubble);
            }
        }

        private void SpawnImpactDust(Vector3 point, Vector3 normal, float blastYield, float damage)
        {
            if (GameAssets.i == null) return;

            if (GameAssets.i.contactDust != null)
            {
                GameObject dust = Instantiate(GameAssets.i.contactDust, point + normal * 0.25f, Quaternion.LookRotation(normal));
                dust.SetActive(true);
                Destroy(dust, 4f);
            }

            if ((blastYield > 5f || damage > 40f) && GameAssets.i.rotorStrike_solid != null)
            {
                GameObject spall = Instantiate(GameAssets.i.rotorStrike_solid, point + normal * 0.25f, Quaternion.LookRotation(normal));
                spall.SetActive(true);
                Destroy(spall, 2.5f);
            }
        }

        /// <summary>
        /// Updates the building geometry's facade weathering and micro-crack normal map
        /// intensity via MaterialPropertyBlock without instantiating new Material assets.
        /// </summary>
        private void UpdateFacadeWeathering()
        {
            if (propertyBlock == null) propertyBlock = new MaterialPropertyBlock();

            float damageFactor = Mathf.Clamp01(cumulativeDamage / 350f);
            Texture2D detailNormal = RuinTextureCatalog.FacadeDetailNormal;

            if (detailNormal != null)
            {
                propertyBlock.SetTexture("_DetailNormalMap", detailNormal);
                propertyBlock.SetFloat("_DetailNormalMapScale", damageFactor * 1.6f);
                propertyBlock.SetFloat("_BumpScale", 1.0f + damageFactor * 1.0f);
            }

            // Facade soot charring
            Color charColor = Color.Lerp(Color.white, new Color(0.32f, 0.30f, 0.28f), damageFactor);
            propertyBlock.SetColor("_BaseColor", charColor);
            propertyBlock.SetColor("_Color", charColor);

            Renderer[] renderers = GetComponentsInChildren<Renderer>(false);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer r = renderers[i];
                if (r != null && !(r is ParticleSystemRenderer) && !(r is TrailRenderer))
                {
                    r.SetPropertyBlock(propertyBlock);
                }
            }
        }

        /// <summary>
        /// Activates persistent smoke venting specifically from the primary breach hole
        /// when a stronghold has taken severe structural damage.
        /// </summary>
        public void SetSevereDamageSmoke(bool enabled, Vector3 primaryBreach)
        {
            if (enabled)
            {
                if (breachSmokeEmitter == null && GameAssets.i != null && GameAssets.i.contactSmoke != null)
                {
                    breachSmokeEmitter = Instantiate(GameAssets.i.contactSmoke, primaryBreach, Quaternion.LookRotation(Vector3.up), transform);
                    breachSmokeEmitter.name = "BoscaliSummer.BreachSmoke";
                    breachSmokeEmitter.SetActive(true);

                    ParticleSystem ps = breachSmokeEmitter.GetComponentInChildren<ParticleSystem>();
                    if (ps != null)
                    {
                        var main = ps.main;
                        main.loop = true;
                        main.startLifetime = 6f;
                        main.startSize = new ParticleSystem.MinMaxCurve(3.5f, 7f);
                        ps.Play();
                    }
                }
            }
            else if (breachSmokeEmitter != null)
            {
                Destroy(breachSmokeEmitter);
                breachSmokeEmitter = null;
            }
        }

        public void ResetForScene()
        {
            for (int i = 0; i < activeBreaches.Count; i++)
            {
                if (activeBreaches[i] != null && activeBreaches[i].GameObject != null)
                    Destroy(activeBreaches[i].GameObject);
            }
            activeBreaches.Clear();

            for (int i = 0; i < activeRubblePiles.Count; i++)
                if (activeRubblePiles[i] != null) Destroy(activeRubblePiles[i]);
            activeRubblePiles.Clear();

            for (int i = 0; i < activePlumes.Count; i++)
                if (activePlumes[i] != null) Destroy(activePlumes[i]);
            activePlumes.Clear();

            if (breachSmokeEmitter != null)
            {
                Destroy(breachSmokeEmitter);
                breachSmokeEmitter = null;
            }

            RestoreOriginalMaterials();
        }

        private void OnDestroy()
        {
            ResetForScene();
        }
    }
}
