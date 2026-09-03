using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Manages authentic, localized war destruction and ruin effects for buildings:
    /// - Localized blast craters and jagged breach holes placed directly at impact sites on building facades.
    /// - Physical concrete rubble and fallen masonry mounds spawned at the base of damaged walls.
    /// - Localized impact dust and smoke plumes venting from breach holes.
    /// - Does NOT globally tint buildings black or cause windows to glow neon orange.
    /// </summary>
    internal sealed class BuildingDamageVisual : MonoBehaviour
    {
        private const int MaxLocalBreachesPerBuilding = 16;
        private const float MaxViewingDistanceSq = 3500f * 3500f;

        private readonly List<GameObject> activeBreaches = new List<GameObject>(MaxLocalBreachesPerBuilding);
        private readonly List<GameObject> activeRubblePiles = new List<GameObject>(MaxLocalBreachesPerBuilding);
        private GameObject breachSmokeEmitter;
        private bool isRestored;

        public static BuildingDamageVisual GetOrAdd(GameObject buildingRoot)
        {
            if (buildingRoot == null) return null;
            BuildingDamageVisual visual = buildingRoot.GetComponent<BuildingDamageVisual>();
            if (visual == null) visual = buildingRoot.AddComponent<BuildingDamageVisual>();
            return visual;
        }

        private void Awake()
        {
            RestoreOriginalMaterials();
        }

        /// <summary>
        /// Restores normal building materials, clearing any global soot tint or emission
        /// so buildings never appear completely grayed out or have glowing neon windows.
        /// </summary>
        public void RestoreOriginalMaterials()
        {
            if (isRestored) return;
            isRestored = true;

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
        /// Places a localized war damage breach crater on the building facade and spawns
        /// fallen concrete rubble mounds at the ground beneath the impact.
        /// </summary>
        public void ApplyLocalImpact(Vector3 impactPoint, Vector3 normal, float blastYield, float damage)
        {
            RestoreOriginalMaterials();

            if (impactPoint == Vector3.zero) return;
            if (normal == Vector3.zero) normal = Vector3.up;

            // Reject impacts beyond camera drawing distance
            Camera cam = Camera.main;
            if (cam != null && (cam.transform.position - impactPoint).sqrMagnitude > MaxViewingDistanceSq)
                return;

            // 1. Localized structural breach crater on the building facade
            PlaceWallBreach(impactPoint, normal, blastYield);

            // 2. Physical fallen rubble mound at the base of the damaged wall
            PlaceGroundRubble(impactPoint, normal, blastYield);

            // 3. Impact dust burst and concrete pulverization accent
            SpawnImpactDust(impactPoint, normal, blastYield);
        }

        private void PlaceWallBreach(Vector3 point, Vector3 normal, float blastYield)
        {
            if (GameAssets.i == null || GameAssets.i.scorchMarkDecal == null) return;

            // Check if there is an existing breach very close to merge with
            for (int i = 0; i < activeBreaches.Count; i++)
            {
                GameObject existing = activeBreaches[i];
                if (existing != null && (existing.transform.position - point).sqrMagnitude < 4f)
                {
                    // Expand existing breach hole
                    DecalProjector proj = existing.GetComponent<DecalProjector>();
                    if (proj != null)
                    {
                        float newSize = Mathf.Min(proj.size.x * 1.35f, 14f);
                        proj.size = new Vector3(newSize, newSize, newSize * 0.35f);
                    }
                    return;
                }
            }

            float size = Mathf.Clamp(2.8f + blastYield * 0.42f, 2.5f, 12f);
            Quaternion facing = Quaternion.LookRotation(-normal, Vector3.up);
            uint seed = (uint)(Mathf.Abs(point.x * 137f + point.y * 311f + point.z * 523f));
            Quaternion roll = Quaternion.AngleAxis((seed % 360), -normal);

            GameObject breach = Instantiate(GameAssets.i.scorchMarkDecal, point + normal * 0.06f, roll * facing, transform);
            breach.name = "BoscaliSummer.LocalBreach";
            breach.SetActive(true);

            DecalProjector projector = breach.GetComponent<DecalProjector>();
            if (projector != null)
            {
                projector.size = new Vector3(size, size, size * 0.3f);
                projector.fadeFactor = 0.96f; // High contrast charred crater
                projector.drawDistance = 2600f;
            }

            if (activeBreaches.Count >= MaxLocalBreachesPerBuilding)
            {
                GameObject oldest = activeBreaches[0];
                activeBreaches.RemoveAt(0);
                if (oldest != null) Destroy(oldest);
            }
            activeBreaches.Add(breach);
        }

        private void PlaceGroundRubble(Vector3 impactPoint, Vector3 normal, float blastYield)
        {
            if (GameAssets.i == null || GameAssets.i.scorchMarkDecal == null) return;

            // Raycast down to find the ground/sidewalk at the foot of the building
            Vector3 castOrigin = impactPoint + normal * 0.8f;
            int mask = PhysicsLayers.StaticsMask;
            if (Physics.Raycast(castOrigin, Vector3.down, out RaycastHit hit, 160f, mask, QueryTriggerInteraction.Ignore))
            {
                float rubbleSize = Mathf.Clamp(3.2f + blastYield * 0.5f, 3f, 12f);
                Quaternion rubbleFacing = Quaternion.LookRotation(Vector3.down, normal);
                uint seed = (uint)(Mathf.Abs(hit.point.x * 71f + hit.point.z * 193f));
                Quaternion roll = Quaternion.AngleAxis((seed % 360), Vector3.down);

                GameObject rubble = Instantiate(GameAssets.i.scorchMarkDecal, hit.point + Vector3.up * 0.04f, roll * rubbleFacing, transform);
                rubble.name = "BoscaliSummer.GroundRubble";
                rubble.SetActive(true);

                DecalProjector proj = rubble.GetComponent<DecalProjector>();
                if (proj != null)
                {
                    proj.size = new Vector3(rubbleSize, rubbleSize, rubbleSize * 0.25f);
                    proj.fadeFactor = 0.90f;
                    proj.drawDistance = 2400f;
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

        private void SpawnImpactDust(Vector3 point, Vector3 normal, float blastYield)
        {
            if (GameAssets.i == null) return;

            if (GameAssets.i.contactDust != null)
            {
                GameObject dust = Instantiate(GameAssets.i.contactDust, point + normal * 0.2f, Quaternion.LookRotation(normal));
                dust.SetActive(true);
                Destroy(dust, 4.5f);
            }

            if (blastYield > 10f && GameAssets.i.rotorStrike_solid != null)
            {
                GameObject spall = Instantiate(GameAssets.i.rotorStrike_solid, point + normal * 0.2f, Quaternion.LookRotation(normal));
                spall.SetActive(true);
                Destroy(spall, 3f);
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
                        main.startSize = new ParticleSystem.MinMaxCurve(3f, 6f);
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
                if (activeBreaches[i] != null) Destroy(activeBreaches[i]);
            activeBreaches.Clear();

            for (int i = 0; i < activeRubblePiles.Count; i++)
                if (activeRubblePiles[i] != null) Destroy(activeRubblePiles[i]);
            activeRubblePiles.Clear();

            if (breachSmokeEmitter != null)
            {
                Destroy(breachSmokeEmitter);
                breachSmokeEmitter = null;
            }

            isRestored = false;
            RestoreOriginalMaterials();
        }

        private void OnDestroy()
        {
            ResetForScene();
        }
    }
}
