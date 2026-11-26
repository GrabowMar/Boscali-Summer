using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Domain;
using BoscaliSummer.Modules.Vanguard.Runtime;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>Affected-zone charring and simplified vanilla burns/smoke after the SA-74N burst.
    /// Ground charring touches only the affected blast zone; fires clone vanilla DamageParticles and
    /// Fuel Depot smoke systems over a bounded camera-nearest pool.</summary>
    [DefaultExecutionOrder(1900)]
    internal sealed class Sa74Smolder : MonoBehaviour, ISceneService
    {
        private struct Candidate
        {
            internal Vector3 Position;
            internal float Start, Scale;
            internal uint Seed;
        }

        private sealed class BurnSystem
        {
            internal ParticleSystem System;
            internal float BaseRate, VelXMin, VelXMax, VelZMin, VelZMax;
            internal bool IsFlame, HasVelCurve;
        }

        private sealed class BurnRig
        {
            internal GameObject Root;
            internal readonly List<BurnSystem> Systems = new List<BurnSystem>();
            internal int SiteIndex = -1;
            internal bool NeedsPrewarm, WindDirty = true;
        }

        private sealed class Ground
        {
            internal Material Material;
            internal string Property;
            internal Texture Original;
            internal RenderTexture Burned;
            internal bool Cover;
        }

        private static readonly string[] GroundMaps = { "_macro_basecolor", "_macro_ao", "_splat_fields", "_splat_grass", "_splat_lush" };
        private static readonly BindingFlags InstanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private static readonly FieldInfo DamageEffectsField = typeof(UnitPart).GetField("damageEffects", InstanceFields);
        private static readonly FieldInfo DisintegrationEffectsField = typeof(UnitPart).GetField("disintegrationEffects", InstanceFields);
        private static readonly FieldInfo WreckageField = typeof(Building).GetField("wreckage", InstanceFields);
        private static readonly FieldInfo FuelFireField = typeof(FuelTank).GetField("fireEffect", InstanceFields);

        private readonly Candidate[] candidates = new Candidate[Sa74Firestorm.CandidateSites];
        private readonly List<BurnRig> pool = new List<BurnRig>(Sa74Firestorm.ActivePool);
        private readonly List<int> selected = new List<int>(Sa74Firestorm.ActivePool);
        private readonly List<Ground> grounds = new List<Ground>();

        private Sa74Fallout.Site site;
        private GameObject root;
        private Material charMaterial;
        private GameObject burnTemplate, smokeTemplate;
        private readonly HashSet<string> burnFlamePaths = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> burnSmokePaths = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> smokePaths = new HashSet<string>(StringComparer.Ordinal);
        private bool templatesSearched;
        private int count;
        private int charStep = -1;
        private float lastCharReach = -1f;
        private float lastAge = float.NaN;
        private float nextSelect, nextTick;
        private Vector3 appliedWind = new Vector3(float.NaN, 0f, 0f);

        internal static int ActivePlumes { get; private set; }
        internal static int ActiveFires { get; private set; }
        internal static int ActiveEmbers { get; private set; }
        internal static int ActiveDeck { get; private set; }
        internal static int CharMaterials { get; private set; }
        internal static int CharDetails => 0;
        internal static int CharStep { get; private set; }
        internal static float CharReach { get; private set; }
        internal static int CharWrites { get; private set; }
        internal static bool VanillaBurnReady { get; private set; }
        internal static bool VanillaSmokeReady { get; private set; }

        public void ResetForScene() => Clear();

        private void LateUpdate()
        {
            if (Application.isBatchMode || Datum.origin == null) return;
            Sa74Fallout.Site newest = Sa74Fallout.Sites.Count > 0 ? Sa74Fallout.Sites[Sa74Fallout.Sites.Count - 1] : null;
            if (!ReferenceEquals(newest, site)) { Clear(); site = newest; }
            if (site == null) return;
            float age = Mathf.Max(0f, Time.time - site.Started);
            if (age < Sa74Burn.StartSeconds || (root == null && !Build())) return;
            root.transform.position = site.Position.ToLocalPosition();
            Char(age);
            TickBurns(age);
        }

        private bool Build()
        {
            ResolveTemplates();
            root = new GameObject("SA-74N burned land / " + site.Id);
            root.transform.SetParent(Datum.origin, false);
            root.transform.position = site.Position.ToLocalPosition();
            Vector3 centre = site.Position.ToLocalPosition();
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            MapSettings map = level != null ? level.LoadedMapSettings : null;
            Vector2 span = map != null ? map.MapSize : Core.Game.TheaterFrame.Resolve();
            float reach = Sa74Burn.EffectiveRadius(Sa74Firestorm.Reach(span.x, span.y), Sa74Fallout.ShockRadius, site.GroundClearance);
            count = 0;
            if (reach <= 1f) return true;
            if (Physics.Raycast(centre + Vector3.up * 20000f, Vector3.down, out RaycastHit gz, 45000f,
                PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) && gz.point.y > Datum.LocalSeaY + 1f)
            {
                candidates[count++] = new Candidate
                {
                    Position = Datum.origin.InverseTransformPoint(gz.point),
                    Scale = 2.8f,
                    Start = Sa74BlastShape.ArrivalSeconds(site.GroundClearance) + 1f,
                    Seed = unchecked(site.Id * 7919u + 5741u)
                };
            }
            float minR = Mathf.Min(Sa74Fallout.CraterRadius * .6f, reach * .15f), maxR = reach * .92f;
            for (int i = 1; i < Sa74Firestorm.CandidateSites && maxR > minR; i++)
            {
                uint seed = unchecked(site.Id * 7919u + (uint)i * 104729u + 5741u);
                float distance = minR + Sa74Firestorm.Hash(seed) * (maxR - minR);
                float angle = i * 2.39996323f + Sa74Firestorm.Hash(seed + 7u);
                Vector3 above = centre + new Vector3(Mathf.Cos(angle) * distance, 20000f, Mathf.Sin(angle) * distance);
                // Only affected land burns: misses over open water are skipped.
                if (!Physics.Raycast(above, Vector3.down, out RaycastHit hit, 45000f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) ||
                    hit.point.y <= Datum.LocalSeaY + 1f) continue;
                candidates[count++] = new Candidate
                {
                    Position = Datum.origin.InverseTransformPoint(hit.point),
                    Scale = Sa74Firestorm.Scale(distance, reach),
                    Start = Sa74BlastShape.ArrivalSeconds(distance) + Sa74Firestorm.IgnitionDelay(seed, distance, reach),
                    Seed = seed
                };
            }
            return true;
        }

        /// <summary>Chars only the affected ground in the terrain's whole-map colour, AO and vegetation splats.</summary>
        private void Char(float age)
        {
            if (count == 0) return;
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            MapSettings map = level != null ? level.LoadedMapSettings : null;
            Vector2 span = map != null ? map.MapSize : Core.Game.TheaterFrame.Resolve();
            if (span.x <= 0f || span.y <= 0f) return;
            float maxReach = Sa74Burn.Reach(span.x, span.y);
            float reach = Sa74Burn.EffectiveRadius(maxReach, Sa74Fallout.FrontRadius(age), site.GroundClearance);
            float strength = Sa74Burn.Strength(age);
            int step = Sa74Burn.Step(age);
            if (reach <= 1f || (step == charStep && Mathf.Abs(reach - lastCharReach) < 500f)) return;
            Shader shader = Sa74Mushroom.BlastShader();
            if (shader == null || shader.passCount < 3) return;
            if (charMaterial == null) charMaterial = new Material(shader) { name = "SA-74N terrain char" };
            if (grounds.Count == 0)
                foreach (MeshRenderer renderer in FindObjectsOfType<MeshRenderer>())
                {
                    Material material = renderer.sharedMaterial;
                    if (material == null || !material.HasProperty("_macro_basecolor") || grounds.Exists(ground => ground.Material == material)) continue;
                    // The sea shares the macro slot; the firestorm chars land, never the water texture.
                    if (level != null && level.ocean != null && renderer.transform.IsChildOf(level.ocean.transform)) continue;
                    if (grounds.Count >= 20) break;
                    foreach (string property in GroundMaps)
                    {
                        Texture original = material.HasProperty(property) ? material.GetTexture(property) : null;
                        if (original == null || original is RenderTexture || original.width < 16) continue;
                        bool cover = property != "_macro_basecolor" && property != "_macro_ao";
                        // ponytail: colour up to 4096 square, AO and cover weights up to 2048, while burned land exists.
                        int limit = cover || property == "_macro_ao" ? 2048 : 4096;
                        var burned = new RenderTexture(Mathf.Min(limit, original.width), Mathf.Min(limit, original.height), 0, RenderTextureFormat.ARGB32,
                            cover || property == "_macro_ao" ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB)
                        {
                            name = "SA-74N burned " + original.name, useMipMap = true, autoGenerateMips = true,
                            wrapMode = original.wrapMode, filterMode = FilterMode.Trilinear, anisoLevel = Mathf.Max(original.anisoLevel, 4)
                        };
                        if (!burned.Create()) { Destroy(burned); continue; }
                        grounds.Add(new Ground { Material = material, Property = property, Original = original, Burned = burned, Cover = cover });
                    }
                }
            if (grounds.Count == 0) return;
            charStep = step;
            lastCharReach = reach;
            GlobalPosition at = site.Position;
            foreach (Ground ground in grounds)
            {
                charMaterial.SetTexture("_Sa74Macro", ground.Original);
                charMaterial.SetFloat("_Sa74CharMode", ground.Cover ? 1f : 0f);
                charMaterial.SetVector("_Sa74Char", new Vector4(at.x / span.x + .5f, at.z / span.y + .5f, reach / span.x, strength));
                charMaterial.SetVector("_Sa74CharSize", new Vector4(1f / ground.Burned.width, 1f / ground.Burned.height, span.y / span.x, 0f));
                var commands = new CommandBuffer { name = "SA-74N terrain char" };
                commands.SetRenderTarget(ground.Burned);
                commands.SetViewport(new Rect(0f, 0f, ground.Burned.width, ground.Burned.height));
                commands.DrawProcedural(Matrix4x4.identity, charMaterial, 2, MeshTopology.Triangles, 3);
                Graphics.ExecuteCommandBuffer(commands);
                commands.Release();
                ground.Material.SetTexture(ground.Property, ground.Burned);
            }
            CharMaterials = grounds.Count; CharStep = charStep; CharReach = reach; CharWrites++;
        }

        private void TickBurns(float age)
        {
            if (count == 0 || (burnTemplate == null && smokeTemplate == null))
            {
                ActivePlumes = ActiveFires = ActiveEmbers = ActiveDeck = 0;
                return;
            }
            bool ageJumped = !float.IsNaN(lastAge) && Mathf.Abs(age - lastAge) > 5f;
            lastAge = age;
            float now = Time.unscaledTime;
            if (ageJumped || now >= nextSelect)
            {
                nextSelect = now + .5f;
                SelectSites(age);
            }
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            Vector3 wind = level != null ? Vector3.ClampMagnitude(level.windVelocity, 18f) : Vector3.zero;
            wind.y = 0f;
            bool windChanged = float.IsNaN(appliedWind.x) || (wind - appliedWind).sqrMagnitude > .25f;
            if (windChanged) appliedWind = wind;
            bool updateRates = ageJumped || now >= nextTick;
            if (updateRates) nextTick = now + .25f;
            float settle = Sa74Firestorm.Settle(age);
            int plumes = 0, fires = 0, smokes = 0;
            foreach (BurnRig rig in pool)
            {
                if (rig.SiteIndex < 0) continue;
                plumes++;
                Candidate c = candidates[rig.SiteIndex];
                float growth = Sa74Firestorm.Growth(age, c.Start);
                bool prewarm = rig.NeedsPrewarm || ageJumped;
                if (updateRates || prewarm || windChanged || rig.WindDirty)
                {
                    float flameMul = growth * Mathf.Lerp(1f, .45f, settle) * FxBus.Scales.Particles;
                    float smokeMul = growth * Mathf.Lerp(1f, .70f, settle) * FxBus.Scales.Particles;
                    foreach (BurnSystem s in rig.Systems)
                    {
                        if (s.System == null) continue;
                        if (updateRates || prewarm)
                        {
                            var emission = s.System.emission;
                            emission.rateOverTimeMultiplier = s.BaseRate * (s.IsFlame ? flameMul : smokeMul);
                        }
                        if (!s.IsFlame && (windChanged || rig.WindDirty) && s.HasVelCurve)
                        {
                            var velocity = s.System.velocityOverLifetime;
                            velocity.enabled = true;
                            velocity.space = ParticleSystemSimulationSpace.World;
                            velocity.x = new ParticleSystem.MinMaxCurve(s.VelXMin + wind.x * .45f, s.VelXMax + wind.x * .45f);
                            velocity.z = new ParticleSystem.MinMaxCurve(s.VelZMin + wind.z * .45f, s.VelZMax + wind.z * .45f);
                        }
                        if (prewarm)
                        {
                            s.System.Simulate(12f, true, true, false);
                            s.System.Play(true);
                        }
                        else if (!s.System.isPlaying) s.System.Play(true);
                    }
                    rig.NeedsPrewarm = false;
                    rig.WindDirty = false;
                }
                foreach (BurnSystem s in rig.Systems)
                {
                    if (s.System == null) continue;
                    if (s.IsFlame) fires += s.System.particleCount;
                    else smokes += s.System.particleCount;
                }
            }
            ActivePlumes = plumes;
            ActiveFires = fires;
            ActiveEmbers = 0;
            ActiveDeck = smokes;
        }

        private void SelectSites(float age)
        {
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Camera camera = cameras != null ? cameras.mainCamera : null;
            if (camera == null) camera = Camera.main;
            Vector3 eye = camera != null ? camera.transform.position : Vector3.zero;
            float rangeSq = Sa74Firestorm.ActiveRange * Sa74Firestorm.ActiveRange;
            selected.Clear();
            for (int i = 0; i < count; i++)
            {
                if (Sa74Firestorm.Growth(age, candidates[i].Start) <= .001f) continue;
                if (camera != null && (Datum.origin.TransformPoint(candidates[i].Position) - eye).sqrMagnitude > rangeSq) continue;
                selected.Add(i);
            }
            if (camera != null && selected.Count > Sa74Firestorm.ActivePool)
                selected.Sort((a, b) =>
                    (Datum.origin.TransformPoint(candidates[a].Position) - eye).sqrMagnitude.CompareTo(
                        (Datum.origin.TransformPoint(candidates[b].Position) - eye).sqrMagnitude));
            if (selected.Count > Sa74Firestorm.ActivePool)
                selected.RemoveRange(Sa74Firestorm.ActivePool, selected.Count - Sa74Firestorm.ActivePool);
            // shortcut: cap active vanilla particle emitters at 24 camera-nearest sites; raise ActivePool if wide scenes look sparse.
            foreach (BurnRig rig in pool)
                if (rig.SiteIndex >= 0 && !selected.Contains(rig.SiteIndex)) ReleaseRig(rig);
            foreach (int idx in selected)
            {
                if (pool.Exists(r => r.SiteIndex == idx)) continue;
                BurnRig rig = AcquireRig();
                if (rig == null) break;
                rig.SiteIndex = idx;
                rig.NeedsPrewarm = true;
                rig.WindDirty = true;
                Candidate c = candidates[idx];
                rig.Root.transform.position = Datum.origin.TransformPoint(c.Position);
                rig.Root.transform.rotation = Quaternion.Euler(0f, Sa74Firestorm.Hash(c.Seed + 19u) * 360f, 0f);
                rig.Root.transform.localScale = Vector3.one * (1.5f + 1.0f * c.Scale);
                rig.Root.SetActive(true);
            }
        }

        private BurnRig AcquireRig()
        {
            foreach (BurnRig existing in pool)
                if (existing.SiteIndex < 0) return existing;
            if (pool.Count >= Sa74Firestorm.ActivePool) return null;
            BurnRig created = CreateRig();
            if (created != null) pool.Add(created);
            return created;
        }

        private static void ReleaseRig(BurnRig rig)
        {
            rig.SiteIndex = -1;
            rig.NeedsPrewarm = false;
            foreach (BurnSystem s in rig.Systems)
                if (s.System != null) s.System.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (rig.Root != null) rig.Root.SetActive(false);
        }

        private BurnRig CreateRig()
        {
            var rigRoot = new GameObject("SA-74N vanilla burn");
            rigRoot.SetActive(false);
            rigRoot.transform.SetParent(root.transform, false);
            var rig = new BurnRig { Root = rigRoot };
            if (burnTemplate != null)
            {
                GameObject clone = Instantiate(burnTemplate, rigRoot.transform, false);
                StripAndCollect(clone, burnFlamePaths, smokeTemplate == null ? burnSmokePaths : null, rig.Systems);
            }
            if (smokeTemplate != null)
            {
                GameObject clone = Instantiate(smokeTemplate, rigRoot.transform, false);
                StripAndCollect(clone, null, smokePaths, rig.Systems);
            }
            if (rig.Systems.Count == 0) { Destroy(rigRoot); return null; }
            return rig;
        }

        private static void StripAndCollect(GameObject clone, HashSet<string> flamePaths, HashSet<string> extraSmokePaths, List<BurnSystem> collected)
        {
            foreach (AudioSource audio in clone.GetComponentsInChildren<AudioSource>(true))
            {
                audio.Stop(); audio.enabled = false; Destroy(audio);
            }
            foreach (Light light in clone.GetComponentsInChildren<Light>(true))
            {
                light.enabled = false;
                if (light.TryGetComponent(out UniversalAdditionalLightData urp)) DestroyImmediate(urp);
                Destroy(light);
            }
            foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true))
            {
                collider.enabled = false; Destroy(collider);
            }
            foreach (Rigidbody rb in clone.GetComponentsInChildren<Rigidbody>(true))
            {
                rb.detectCollisions = false; rb.isKinematic = true; Destroy(rb);
            }
            foreach (Renderer renderer in clone.GetComponentsInChildren<Renderer>(true))
                if (!(renderer is ParticleSystemRenderer)) { renderer.enabled = false; Destroy(renderer); }
            foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != null) Destroy(behaviour);

            foreach (ParticleSystem system in clone.GetComponentsInChildren<ParticleSystem>(true))
            {
                string path = GetPath(clone.transform, system.transform);
                bool keepFlame = flamePaths != null && flamePaths.Contains(path);
                bool keepSmoke = extraSmokePaths != null && extraSmokePaths.Contains(path);
                if (!keepFlame && !keepSmoke)
                {
                    var em = system.emission; em.enabled = false;
                    system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    ParticleSystemRenderer r = system.GetComponent<ParticleSystemRenderer>();
                    if (r != null) { r.enabled = false; Destroy(r); }
                    Destroy(system);
                    continue;
                }
                var main = system.main;
                main.playOnAwake = false; main.loop = true; main.stopAction = ParticleSystemStopAction.None;
                main.simulationSpace = ParticleSystemSimulationSpace.Custom; main.customSimulationSpace = Datum.origin;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.maxParticles = Mathf.Min(main.maxParticles, keepFlame ? 40 : 96);
                var emission = system.emission; emission.enabled = true;
                var velocity = system.velocityOverLifetime;
                var vy = velocity.y;
                bool hasVel = vy.mode == ParticleSystemCurveMode.Constant || vy.mode == ParticleSystemCurveMode.TwoConstants;
                if (hasVel && vy.mode == ParticleSystemCurveMode.Constant)
                    velocity.y = new ParticleSystem.MinMaxCurve(vy.constant, vy.constant);
                if (!keepFlame)
                {
                    var force = system.forceOverLifetime;
                    force.enabled = true;
                    force.space = ParticleSystemSimulationSpace.World;
                    force.x = new ParticleSystem.MinMaxCurve(0f);
                    force.y = new ParticleSystem.MinMaxCurve(1.2f);
                    force.z = new ParticleSystem.MinMaxCurve(0f);
                }
                collected.Add(new BurnSystem
                {
                    System = system,
                    BaseRate = emission.rateOverTimeMultiplier * (keepFlame ? 1f : .55f),
                    IsFlame = keepFlame,
                    HasVelCurve = hasVel,
                    VelXMin = velocity.x.constantMin, VelXMax = velocity.x.constantMax,
                    VelZMin = velocity.z.constantMin, VelZMax = velocity.z.constantMax
                });
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private void ResolveTemplates()
        {
            if (templatesSearched) return;
            templatesSearched = true;
            burnFlamePaths.Clear(); burnSmokePaths.Clear(); smokePaths.Clear();
            ResolveSmokeTemplate();
            ResolveBurnTemplate();
            VanillaBurnReady = burnTemplate != null && burnFlamePaths.Count > 0;
            VanillaSmokeReady = (smokeTemplate != null && smokePaths.Count > 0) || (burnTemplate != null && burnSmokePaths.Count > 0);
        }

        private void ResolveSmokeTemplate()
        {
            Encyclopedia e = GameAccess.EncyclopediaOrNull;
            if (e == null || e.buildings == null) return;
            BuildingDefinition fuelDepot = null;
            foreach (BuildingDefinition d in e.buildings)
            {
                if (d == null) continue;
                string key = d.jsonKey ?? string.Empty, name = d.unitName ?? string.Empty;
                if (key.Equals("FuelContainer2x1", StringComparison.OrdinalIgnoreCase) || name.Equals("Fuel Depot", StringComparison.OrdinalIgnoreCase))
                { fuelDepot = d; break; }
                string combined = (key + " " + name).ToLowerInvariant();
                if (combined.Contains("fuel") && (combined.Contains("depot") || combined.Contains("container") || combined.Contains("silo")))
                    fuelDepot = d;
            }
            if (fuelDepot == null || fuelDepot.unitPrefab == null) return;
            var candidatesList = new HashSet<GameObject>();
            foreach (UnitPart part in fuelDepot.unitPrefab.GetComponentsInChildren<UnitPart>(true))
            {
                if (DamageEffectsField?.GetValue(part) is IEnumerable damageEffects)
                    foreach (object item in damageEffects)
                        if (item is DamageEffect fx && fx.prefab != null) candidatesList.Add(fx.prefab);
                if (DisintegrationEffectsField?.GetValue(part) is GameObject[] dis)
                    foreach (GameObject go in dis) if (go != null) candidatesList.Add(go);
            }
            Building building = fuelDepot.unitPrefab.GetComponentInChildren<Building>(true);
            GameObject wreckage = building != null ? WreckageField?.GetValue(building) as GameObject : null;
            if (wreckage != null) candidatesList.Add(wreckage);

            int bestScore = int.MinValue;
            ParticleSystem bestSystem = null;
            foreach (GameObject candidate in candidatesList)
            {
                if (candidate == null) continue;
                foreach (ParticleSystem system in candidate.GetComponentsInChildren<ParticleSystem>(true))
                {
                    int score = ScoreSmokeSystem(candidate.transform, system);
                    if (score > bestScore) { bestScore = score; smokeTemplate = candidate; bestSystem = system; }
                }
            }
            if (smokeTemplate == null || bestSystem == null) { smokeTemplate = null; return; }
            foreach (ParticleSystem system in smokeTemplate.GetComponentsInChildren<ParticleSystem>(true))
            {
                string desc = DescribeLayer(smokeTemplate.transform, system);
                if (IsExplicitSmoke(desc) && !IsTransientExplosion(desc) && !desc.Contains("fire") && !desc.Contains("flame"))
                    smokePaths.Add(GetPath(smokeTemplate.transform, system.transform));
            }
            if (smokePaths.Count == 0) smokePaths.Add(GetPath(smokeTemplate.transform, bestSystem.transform));
        }

        private void ResolveBurnTemplate()
        {
            int bestScore = int.MinValue;
            ParticleSystem bestFlame = null;
            foreach (DamageParticles fx in Resources.FindObjectsOfTypeAll<DamageParticles>())
            {
                if (fx == null) continue;
                foreach (ParticleSystem system in fx.GetComponentsInChildren<ParticleSystem>(true))
                {
                    string desc = (fx.name + "/" + DescribeLayer(fx.transform, system)).ToLowerInvariant();
                    if (!desc.Contains("fire") && !desc.Contains("flame")) continue;
                    int score = ScoreFlameSystem(desc);
                    if (score > bestScore) { bestScore = score; burnTemplate = fx.gameObject; bestFlame = system; }
                }
            }
            if (burnTemplate == null)
            {
                Encyclopedia e = GameAccess.EncyclopediaOrNull;
                if (e != null && e.aircraft != null)
                    foreach (AircraftDefinition ac in e.aircraft)
                    {
                        if (ac == null || ac.unitPrefab == null) continue;
                        foreach (FuelTank tank in ac.unitPrefab.GetComponentsInChildren<FuelTank>(true))
                        {
                            GameObject fireFx = FuelFireField?.GetValue(tank) as GameObject;
                            if (fireFx == null) continue;
                            foreach (ParticleSystem system in fireFx.GetComponentsInChildren<ParticleSystem>(true))
                            {
                                string desc = (fireFx.name + "/" + DescribeLayer(fireFx.transform, system)).ToLowerInvariant();
                                if (!desc.Contains("fire") && !desc.Contains("flame")) continue;
                                int score = ScoreFlameSystem(desc);
                                if (score > bestScore) { bestScore = score; burnTemplate = fireFx; bestFlame = system; }
                            }
                        }
                    }
            }
            if (burnTemplate == null || bestFlame == null) { burnTemplate = null; return; }
            foreach (ParticleSystem system in burnTemplate.GetComponentsInChildren<ParticleSystem>(true))
            {
                string desc = (burnTemplate.name + "/" + DescribeLayer(burnTemplate.transform, system)).ToLowerInvariant();
                if ((desc.Contains("fire") || desc.Contains("flame")) && !IsTransientExplosion(desc))
                    burnFlamePaths.Add(GetPath(burnTemplate.transform, system.transform));
                else if (IsExplicitSmoke(desc) && !IsTransientExplosion(desc))
                    burnSmokePaths.Add(GetPath(burnTemplate.transform, system.transform));
            }
            if (burnFlamePaths.Count == 0) burnFlamePaths.Add(GetPath(burnTemplate.transform, bestFlame.transform));
        }

        private static int ScoreSmokeSystem(Transform prefabRoot, ParticleSystem system)
        {
            string desc = (prefabRoot.name + "/" + DescribeLayer(prefabRoot, system)).ToLowerInvariant();
            bool explicitSmoke = IsExplicitSmoke(desc);
            int score = explicitSmoke ? 1200 : 0;
            if (desc.Contains("fuel")) score += 120;
            if (desc.Contains("dark") || desc.Contains("black") || desc.Contains("soot")) score += 180;
            if (IsTransientExplosion(desc)) score -= 1400;
            if (desc.Contains("explosion") && !explicitSmoke) score -= 1000;
            if ((desc.Contains("fire") || desc.Contains("flame")) && !explicitSmoke) score -= 700;
            var main = system.main;
            score += Mathf.RoundToInt(Mathf.Min(main.startLifetime.constantMax, 40f) * 8f);
            score += Mathf.RoundToInt(Mathf.Min(main.startSize.constantMax, 60f) * 3f);
            if (main.loop) score += 140;
            return score;
        }

        private static int ScoreFlameSystem(string desc)
        {
            int score = desc.Contains("flame") ? 60 : 40;
            if (desc.Contains("damage") || desc.Contains("burn")) score += 25;
            if (desc.Contains("engine")) score += 8;
            if (IsTransientExplosion(desc) || desc.Contains("explosion") || desc.Contains("impact") || desc.Contains("muzzle"))
                score -= 80;
            return score;
        }

        private static string DescribeLayer(Transform root, ParticleSystem system)
        {
            ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
            string material = renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : string.Empty;
            return (GetPath(root, system.transform) + "/" + material).ToLowerInvariant();
        }

        private static bool IsExplicitSmoke(string desc) => desc.Contains("smoke") || desc.Contains("soot") || desc.Contains("plume");

        private static bool IsTransientExplosion(string desc) =>
            desc.Contains("fireball") || desc.Contains("flash") || desc.Contains("spark") ||
            desc.Contains("debris") || desc.Contains("fragment") || desc.Contains("shrapnel");

        private static string GetPath(Transform root, Transform target)
        {
            if (target == root) return string.Empty;
            var names = new List<string>(8);
            Transform current = target;
            while (current != null && current != root) { names.Add(current.name); current = current.parent; }
            names.Reverse();
            return string.Join("/", names.ToArray());
        }

        private void Clear()
        {
            foreach (BurnRig rig in pool) if (rig.Root != null) Destroy(rig.Root);
            pool.Clear(); selected.Clear();
            if (root != null) Destroy(root);
            if (charMaterial != null) Destroy(charMaterial);
            foreach (Ground ground in grounds)
            {
                // The terrain material is a shared asset: always hand its own texture back.
                if (ground.Material != null) ground.Material.SetTexture(ground.Property, ground.Original);
                if (ground.Burned != null) { ground.Burned.Release(); Destroy(ground.Burned); }
            }
            grounds.Clear(); charStep = -1; lastCharReach = -1f; lastAge = float.NaN;
            nextSelect = nextTick = 0f; appliedWind = new Vector3(float.NaN, 0f, 0f);
            root = null; charMaterial = null; site = null;
            burnTemplate = smokeTemplate = null; templatesSearched = false;
            burnFlamePaths.Clear(); burnSmokePaths.Clear(); smokePaths.Clear();
            count = 0; ActivePlumes = ActiveFires = ActiveEmbers = ActiveDeck = 0;
            CharMaterials = 0; CharStep = 0; CharReach = 0f; CharWrites = 0;
            VanillaBurnReady = VanillaSmokeReady = false;
        }

        private void OnDestroy() => Clear();
    }
}
