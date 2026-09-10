using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Visuals
{
    /// <summary>
    /// Executes the 15-second tactical flare barrage directly at the rocket impact point:
    /// 1. Initiates an immediate detonation flash, acoustic airburst snap, and primary flare bloom.
    /// 2. Continuously dispenses sequential mortar waves of authentic vanilla <c>IRFlare</c> pyrotechnics
    ///    over the full 15-second duration, keeping a thick canopy of glowing flares in the sky.
    /// 3. Throughout the entire 15 seconds, actively monitors and completely misguides all IR-seeking missiles:
    ///    - Breaks tracking locks via <c>IRSeeker.LoseLock()</c>.
    ///    - Injects maximum optical/IR dazzle (<c>dazzleAmount = 1500f</c>).
    ///    - Continuously binds missile seekers onto active burning flares in the cluster.
    ///    - Registers active flare infrared signatures onto all nearby aircraft.
    /// </summary>
    internal sealed class FlareMissileBurstVisuals : MonoBehaviour
    {
        private const int SampleRate = 44100;
        private const int DeduplicationLimit = 16;
        private static readonly List<(Vector3 Position, float Time)> RecentBarrages =
            new List<(Vector3 Position, float Time)>(DeduplicationLimit);

        private static GameObject cachedFlarePrefab;
        private static bool hasSearchedPrefab;
        private static AudioClip burstAudioClip;

        // Reflection caches for IRSeeker and IRFlare internals
        private static readonly FieldInfo SeekerIrTargetField = AccessTools.Field(typeof(IRSeeker), "IRTarget");
        private static readonly FieldInfo SeekerDazzleField = AccessTools.Field(typeof(IRSeeker), "dazzleAmount");
        private static readonly MethodInfo SeekerLoseLockMethod = AccessTools.Method(typeof(IRSeeker), "LoseLock");
        private static readonly FieldInfo MissileTargetField = AccessTools.Field(typeof(Missile), "target");
        private static readonly FieldInfo FlareIrField = AccessTools.Field(typeof(IRFlare), "IR");
        private static readonly FieldInfo FlareVelocityField = AccessTools.Field(typeof(IRFlare), "velocity");
        private static readonly FieldInfo FlareBurnTimeField = AccessTools.Field(typeof(IRFlare), "burnTime");
        private static readonly FieldInfo FlareDragField = AccessTools.Field(typeof(IRFlare), "drag");
        private static readonly FieldInfo FlareGravityField = AccessTools.Field(typeof(IRFlare), "gravityVector");

        public static void TriggerBarrage(Vector3 impactPoint, float radius = 4000f, float duration = 15f, int initialFlares = 36)
        {
            float now = Time.time;

            // 1. Spatial/temporal deduplication guard: drop duplicate barrage triggers within 3.0s and 400m
            for (int i = RecentBarrages.Count - 1; i >= 0; i--)
            {
                if (now - RecentBarrages[i].Time > 3.0f)
                {
                    RecentBarrages.RemoveAt(i);
                }
                else if (Vector3.Distance(RecentBarrages[i].Position, impactPoint) < 400f)
                {
                    return;
                }
            }

            if (RecentBarrages.Count >= DeduplicationLimit)
            {
                RecentBarrages.RemoveAt(0);
            }
            RecentBarrages.Add((impactPoint, now));

            if (GameManager.IsHeadless)
            {
                MisguideVicinity(impactPoint, radius, null);
                return;
            }

            var barrageObj = new GameObject("FlareBarrage_" + Time.frameCount);
            barrageObj.transform.position = impactPoint;
            if (Datum.origin != null)
            {
                barrageObj.transform.SetParent(Datum.origin.transform, true);
            }

            var barrage = barrageObj.AddComponent<FlareMissileBurstVisuals>();
            barrage.StartCoroutine(barrage.BarrageRoutine(impactPoint, radius, duration, initialFlares));
        }

        private IEnumerator BarrageRoutine(Vector3 impactPoint, float radius, float duration, int initialFlares)
        {
            EnsureAudio();
            GameObject flarePrefab = ResolveFlarePrefab();
            float endTime = Time.time + duration;
            var activeSources = new List<IRSource>(64);

            // 1. Initial Impact Flash & Lighting
            var lightObj = new GameObject("ImpactFlashLight");
            lightObj.transform.SetParent(transform, false);
            lightObj.transform.position = impactPoint + Vector3.up * 8f;

            Light flashLight = lightObj.AddComponent<Light>();
            flashLight.type = LightType.Point;
            flashLight.color = new Color(1f, 0.94f, 0.82f);
            flashLight.range = 1800f;
            flashLight.intensity = 180f;
            flashLight.shadows = LightShadows.None;
            StartCoroutine(FlashDecayRoutine(flashLight));

            // 2. Initial Explosion Acoustics
            if (GameAssets.i != null && GameAssets.i.sonicBoom != null)
            {
                var snapSource = gameObject.AddComponent<AudioSource>();
                snapSource.clip = GameAssets.i.sonicBoom;
                snapSource.spatialBlend = 1f;
                snapSource.minDistance = 300f;
                snapSource.maxDistance = 15000f;
                snapSource.rolloffMode = AudioRolloffMode.Logarithmic;
                snapSource.volume = 1f;
                snapSource.pitch = UnityEngine.Random.Range(1.1f, 1.25f);
                snapSource.Play();
            }

            if (burstAudioClip != null)
            {
                if (SceneSingleton<ExplosionAudioManager>.i != null)
                {
                    var boomObj = new GameObject("BurstAudioBoom");
                    boomObj.transform.position = impactPoint;
                    boomObj.transform.SetParent(transform, false);
                    var boomSrc = boomObj.AddComponent<AudioSource>();
                    boomSrc.clip = burstAudioClip;
                    boomSrc.spatialBlend = 1f;
                    boomSrc.minDistance = 300f;
                    boomSrc.maxDistance = 18000f;
                    var filter = boomObj.AddComponent<AudioLowPassFilter>();
                    SceneSingleton<ExplosionAudioManager>.i.AddExplosionAudio(boomSrc, filter, 0.25f);
                }
            }

            // Dedicated wave mortar pop audio source
            AudioSource waveAudioSource = gameObject.AddComponent<AudioSource>();
            waveAudioSource.spatialBlend = 1f;
            waveAudioSource.minDistance = 200f;
            waveAudioSource.maxDistance = 12000f;

            // 3. Initial Flare Salvo directly from impact point
            Aircraft targetAircraft = FindCandidateAircraft(impactPoint);
            SpawnFlares(impactPoint, flarePrefab, targetAircraft, initialFlares, 45f, 115f, activeSources);

            float nextWaveTime = Time.time + 1.25f;

            // 4. Continuous 15-Second Barrage Loop
            while (Time.time < endTime)
            {
                // Trigger sustained sequential mortar flare ejections
                if (Time.time >= nextWaveTime)
                {
                    nextWaveTime = Time.time + 1.25f;

                    if (burstAudioClip != null && waveAudioSource != null)
                    {
                        waveAudioSource.pitch = UnityEngine.Random.Range(0.92f, 1.12f);
                        waveAudioSource.PlayOneShot(burstAudioClip, 0.85f);
                    }

                    // Each wave launches 4 to 6 new pyrotechnic flares high into the sky
                    int waveCount = UnityEngine.Random.Range(4, 7);
                    SpawnFlares(impactPoint, flarePrefab, targetAircraft, waveCount, 55f, 95f, activeSources);
                }

                // Prune dead/destroyed flare IR sources
                for (int s = activeSources.Count - 1; s >= 0; s--)
                {
                    if (activeSources[s] == null || activeSources[s].transform == null)
                    {
                        activeSources.RemoveAt(s);
                    }
                }

                // Actively misguide any incoming or in-flight IR missiles every frame
                MisguideVicinity(impactPoint, radius, activeSources);

                yield return null;
            }

            // 5. Post-barrage residual protection: flares continue to burn for another ~8 seconds
            float residualEndTime = Time.time + 8.0f;
            while (Time.time < residualEndTime)
            {
                for (int s = activeSources.Count - 1; s >= 0; s--)
                {
                    if (activeSources[s] == null || activeSources[s].transform == null)
                    {
                        activeSources.RemoveAt(s);
                    }
                }

                if (activeSources.Count > 0)
                {
                    MisguideVicinity(impactPoint, radius, activeSources);
                }

                yield return new WaitForSeconds(0.1f);
            }

            Destroy(gameObject, 5f);
        }

        private IEnumerator FlashDecayRoutine(Light flashLight)
        {
            float elapsed = 0f;
            float duration = 0.4f;
            float initialIntensity = flashLight != null ? flashLight.intensity : 0f;

            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float decay = Mathf.Clamp01(1f - (elapsed / duration));
                if (flashLight != null)
                {
                    flashLight.intensity = initialIntensity * Mathf.Pow(decay, 2.2f);
                }
                yield return null;
            }

            if (flashLight != null)
            {
                Destroy(flashLight.gameObject);
            }
        }

        private static void SpawnFlares(
            Vector3 originPoint,
            GameObject flarePrefab,
            Aircraft candidateAircraft,
            int count,
            float minSpeed,
            float maxSpeed,
            List<IRSource> activeSources)
        {
            Transform originParent = Datum.origin != null ? Datum.origin.transform : null;

            for (int i = 0; i < count; i++)
            {
                // Upward-biased fountain arc from the impact point
                Vector3 spherical = UnityEngine.Random.onUnitSphere;
                spherical.y = Mathf.Abs(spherical.y) * 0.85f + 0.35f;
                Vector3 dir = spherical.normalized;

                float speed = UnityEngine.Random.Range(minSpeed, maxSpeed);
                Vector3 launchVel = dir * speed;
                Vector3 spawnPos = originPoint + Vector3.up * UnityEngine.Random.Range(2f, 18f) +
                                   new Vector3(UnityEngine.Random.Range(-5f, 5f), 0f, UnityEngine.Random.Range(-5f, 5f));
                Quaternion rot = Quaternion.LookRotation(dir);

                if (flarePrefab != null)
                {
                    GameObject flareObj = UnityEngine.Object.Instantiate(flarePrefab, spawnPos, rot, originParent);
                    if (flareObj == null) continue;

                    var irFlare = flareObj.GetComponent<IRFlare>();
                    if (irFlare != null)
                    {
                        if (candidateAircraft != null)
                        {
                            irFlare.LaunchFlare(candidateAircraft, flareObj.transform, launchVel);
                        }
                        else
                        {
                            var irSource = new IRSource(flareObj.transform, 4.5f, true);
                            FlareIrField?.SetValue(irFlare, irSource);
                            FlareVelocityField?.SetValue(irFlare, launchVel);
                            FlareBurnTimeField?.SetValue(irFlare, UnityEngine.Random.Range(8.5f, 12.5f));
                            FlareDragField?.SetValue(irFlare, 0.0085f);
                            FlareGravityField?.SetValue(irFlare, Physics.gravity * 0.38f);
                        }

                        // Extend burn time so flares persist during the barrage
                        FlareBurnTimeField?.SetValue(irFlare, UnityEngine.Random.Range(8.5f, 12.5f));

                        if (FlareIrField?.GetValue(irFlare) is IRSource ir && ir != null)
                        {
                            activeSources.Add(ir);
                        }
                    }
                }
                else
                {
                    // Procedural flare fallback
                    var flareObj = new GameObject("ProceduralFlare_" + Time.frameCount + "_" + i);
                    flareObj.transform.position = spawnPos;
                    if (originParent != null) flareObj.transform.SetParent(originParent, true);

                    var light = flareObj.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = new Color(1f, 0.85f, 0.4f);
                    light.range = 65f;
                    light.intensity = 18f;

                    var irSource = new IRSource(flareObj.transform, 5.0f, true);
                    activeSources.Add(irSource);

                    var runner = flareObj.AddComponent<ProceduralFlareRunner>();
                    runner.Initialize(launchVel, UnityEngine.Random.Range(8.5f, 12.5f), light);
                }
            }
        }

        private static void MisguideVicinity(Vector3 impactPoint, float radius, List<IRSource> flareSources)
        {
            float radiusSquared = radius * radius;
            var allUnits = UnitRegistry.allUnits;
            if (allUnits == null) return;

            for (int i = 0; i < allUnits.Count; i++)
            {
                Unit unit = allUnits[i];
                if (unit == null || unit.disabled) continue;

                // 1. Check for in-flight missiles
                if (unit is Missile missile)
                {
                    float missileDistSq = (missile.transform.position - impactPoint).sqrMagnitude;
                    bool targetInRange = false;

                    Unit targetUnit = MissileTargetField?.GetValue(missile) as Unit;
                    if (targetUnit != null)
                    {
                        targetInRange = (targetUnit.transform.position - impactPoint).sqrMagnitude <= radiusSquared;
                    }

                    if (missileDistSq <= radiusSquared || targetInRange)
                    {
                        MisguideMissile(missile, impactPoint, flareSources);
                    }
                }
                // 2. Check for vicinity aircraft to feed flare IR signatures
                else if (unit is Aircraft ac)
                {
                    float acDistSq = (ac.transform.position - impactPoint).sqrMagnitude;
                    if (acDistSq <= radiusSquared && flareSources != null)
                    {
                        for (int f = 0; f < flareSources.Count; f++)
                        {
                            if (flareSources[f] != null)
                            {
                                ac.AddIRSource(flareSources[f]);
                            }
                        }
                    }
                }
            }
        }

        private static void MisguideMissile(Missile missile, Vector3 impactPoint, List<IRSource> flareSources)
        {
            if (missile == null || missile.disabled) return;

            var seeker = missile.GetComponentInChildren<IRSeeker>();
            if (seeker != null)
            {
                // Break tracking lock on the aircraft
                SeekerLoseLockMethod?.Invoke(seeker, null);

                // Inject overwhelming dazzle to defeat countermeasure rejection filters
                SeekerDazzleField?.SetValue(seeker, 1500f);

                // Re-target onto a random flare heat source in the active canopy
                if (flareSources != null && flareSources.Count > 0)
                {
                    IRSource decoy = flareSources[UnityEngine.Random.Range(0, flareSources.Count)];
                    SeekerIrTargetField?.SetValue(seeker, decoy);

                    if (decoy != null && decoy.transform != null)
                    {
                        missile.SetAimpoint(
                            decoy.transform.position.ToGlobalPosition(),
                            UnityEngine.Random.insideUnitSphere * 80f);
                        return;
                    }
                }

                // If no flare source available, force erratic ballistic divert
                Vector3 spoofAim = impactPoint + Vector3.up * 150f + UnityEngine.Random.insideUnitSphere * 250f;
                missile.SetAimpoint(spoofAim.ToGlobalPosition(), UnityEngine.Random.insideUnitSphere * 120f);
            }
        }

        private static Aircraft FindCandidateAircraft(Vector3 impactPoint)
        {
            if (GameManager.GetLocalPlayer<Player>(out Player player) && player?.Aircraft != null)
            {
                return player.Aircraft;
            }

            var allAircraft = UnitRegistry.allAircraft;
            if (allAircraft != null && allAircraft.Count > 0)
            {
                Aircraft closest = null;
                float minDistance = float.MaxValue;
                for (int i = 0; i < allAircraft.Count; i++)
                {
                    Aircraft ac = allAircraft[i];
                    if (ac == null || ac.disabled) continue;

                    float d = Vector3.Distance(ac.transform.position, impactPoint);
                    if (d < minDistance)
                    {
                        minDistance = d;
                        closest = ac;
                    }
                }
                if (closest != null) return closest;
            }

            return null;
        }

        private static GameObject ResolveFlarePrefab()
        {
            if (cachedFlarePrefab != null) return cachedFlarePrefab;
            if (hasSearchedPrefab) return null;
            hasSearchedPrefab = true;

            FieldInfo flarePrefabField = AccessTools.Field(typeof(FlareEjector), "flarePrefab");

            // 1. Search through Encyclopedia aircraft definitions
            if (Encyclopedia.i?.aircraft != null)
            {
                for (int i = 0; i < Encyclopedia.i.aircraft.Count; i++)
                {
                    var acDef = Encyclopedia.i.aircraft[i];
                    if (acDef == null || acDef.unitPrefab == null) continue;

                    var ejector = acDef.unitPrefab.GetComponentInChildren<FlareEjector>(true);
                    if (ejector != null)
                    {
                        cachedFlarePrefab = flarePrefabField?.GetValue(ejector) as GameObject;
                        if (cachedFlarePrefab != null)
                        {
                            return cachedFlarePrefab;
                        }
                    }
                }
            }

            // 2. Fallback: Find in all loaded resources
            var allEjectors = Resources.FindObjectsOfTypeAll<FlareEjector>();
            if (allEjectors != null)
            {
                for (int i = 0; i < allEjectors.Length; i++)
                {
                    if (allEjectors[i] != null)
                    {
                        cachedFlarePrefab = flarePrefabField?.GetValue(allEjectors[i]) as GameObject;
                        if (cachedFlarePrefab != null) return cachedFlarePrefab;
                    }
                }
            }

            return null;
        }

        private static void EnsureAudio()
        {
            if (burstAudioClip != null) return;

            int length = (int)(SampleRate * 3.0f);
            float[] samples = new float[length];

            for (int i = 0; i < length; i++)
            {
                float t = i / (float)SampleRate;

                // Pyrotechnic mortar ejection fracture crack (0.0s - 0.09s)
                float popEnvelope = Mathf.Exp(-t * 30f);
                float pop = (UnityEngine.Random.value * 2f - 1f) * popEnvelope * 0.9f;

                // Resonant hollow explosive mortar thud (85 Hz dropping to 45 Hz)
                float thudFreq = Mathf.Lerp(85f, 45f, Mathf.Clamp01(t / 0.45f));
                float thudEnvelope = Mathf.Exp(-t * 9.5f);
                float thud = Mathf.Sin(2f * Mathf.PI * thudFreq * t) * thudEnvelope * 0.75f;

                // Burning magnesium sizzling hiss
                float sizzleEnvelope = Mathf.Pow(Mathf.Clamp01(1f - (t / 2.8f)), 1.5f);
                float sizzle = (UnityEngine.Random.value * 2f - 1f) * sizzleEnvelope * 0.28f;

                samples[i] = Mathf.Clamp(pop + thud + sizzle, -1f, 1f);
            }

            burstAudioClip = AudioClip.Create("FlareMissileBurstPop", length, 1, SampleRate, false);
            burstAudioClip.SetData(samples, 0);
        }
    }

    /// <summary>
    /// Fallback runner for procedural flares when vanilla flare prefab cannot be resolved.
    /// </summary>
    internal sealed class ProceduralFlareRunner : MonoBehaviour
    {
        private Vector3 velocity;
        private float remainingBurnTime;
        private Light flareLight;

        public void Initialize(Vector3 initialVelocity, float burnDuration, Light light)
        {
            velocity = initialVelocity;
            remainingBurnTime = burnDuration;
            flareLight = light;
        }

        private void Update()
        {
            transform.position += velocity * Time.deltaTime;
            velocity += Physics.gravity * 0.38f * Time.deltaTime;
            velocity *= Mathf.Clamp01(1f - (0.009f * Time.deltaTime));

            remainingBurnTime -= Time.deltaTime;
            if (flareLight != null)
            {
                flareLight.intensity = UnityEngine.Random.Range(14f, 22f);
            }

            if (remainingBurnTime <= 0f)
            {
                Destroy(gameObject);
            }
        }
    }
}
