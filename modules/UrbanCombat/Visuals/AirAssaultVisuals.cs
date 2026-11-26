using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Fx;
using BoscaliSummer.Core.Math;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// HALO paratrooper stick and Ibis fast-rope visuals. Client-local and cosmetic:
    /// the server resolves the drop's outcome in AirAssaultController on its own clock.
    /// </summary>
    internal static class AirAssaultVisuals
    {
        internal readonly struct HaloStickSnapshot
        {
            public readonly GlobalPosition Centroid, Landing;
            public readonly FactionHQ Faction;
            public readonly int Count;
            public readonly HaloPhase Phase;
            public readonly float EtaSeconds;

            public HaloStickSnapshot(GlobalPosition centroid, GlobalPosition landing, FactionHQ faction, int count, HaloPhase phase, float etaSeconds)
            {
                Centroid = centroid;
                Landing = landing;
                Faction = faction;
                Count = count;
                Phase = phase;
                EtaSeconds = etaSeconds;
            }
        }

        private const int MaximumActiveOperations = 8;
        private static readonly List<GameObject> ActiveOperations = new List<GameObject>(MaximumActiveOperations);
        private static readonly List<HaloDropOperation> ActiveHaloDrops = new List<HaloDropOperation>(MaximumActiveOperations);

        internal static void CopyActiveHaloSticks(List<HaloStickSnapshot> destination)
        {
            destination.Clear();
            for (int i = ActiveHaloDrops.Count - 1; i >= 0; i--)
            {
                HaloDropOperation op = ActiveHaloDrops[i];
                if (op == null)
                {
                    ActiveHaloDrops.RemoveAt(i);
                    continue;
                }
                if (op.TrySnapshot(out HaloStickSnapshot snap)) destination.Add(snap);
            }
        }

        private static bool HasActiveRappel(Aircraft aircraft)
        {
            foreach (GameObject operation in ActiveOperations)
                if (operation != null && operation.GetComponent<RopeOperation>() is RopeOperation rope && rope.Aircraft == aircraft) return true;
            return false;
        }

        public static void ResetForScene()
        {
            for (int i = 0; i < ActiveOperations.Count; i++)
                if (ActiveOperations[i] != null) UnityEngine.Object.Destroy(ActiveOperations[i]);
            ActiveOperations.Clear();
            ActiveHaloDrops.Clear();
        }

        private static bool Track(GameObject operation)
        {
            for (int i = ActiveOperations.Count - 1; i >= 0; i--)
                if (ActiveOperations[i] == null) ActiveOperations.RemoveAt(i);
            if (ActiveOperations.Count >= MaximumActiveOperations)
            {
                UnityEngine.Object.Destroy(operation);
                Plugin.Logger.LogInfo($"[Air Assault] Drop visual skipped: {MaximumActiveOperations} drops already in view.");
                return false;
            }
            ActiveOperations.Add(operation);
            return true;
        }

        /// <summary>Jumper and canopy templates riding inactive inside the bundled mount prefab.</summary>
        private static bool TryHaloTemplates(out GameObject jumper, out GameObject canopy)
        {
            jumper = canopy = null;
            if (Encyclopedia.WeaponLookup == null ||
                !Encyclopedia.WeaponLookup.TryGetValue(AirAssaultController.HaloMountKey, out WeaponMount mount) ||
                mount == null || mount.prefab == null)
                return false;
            jumper = mount.prefab.transform.Find("AirborneJumper")?.gameObject;
            canopy = mount.prefab.transform.Find("AirborneCanopy")?.gameObject;
            return jumper != null && canopy != null;
        }

        public static void SpawnHaloDrop(HaloGlidePlan plan, float startDelay, Aircraft aircraft = null)
        {
            if (!TryHaloTemplates(out GameObject jumper, out GameObject canopy))
            {
                Plugin.Logger.LogWarning("[Air Assault] HALO models missing from the Airborne bundle; the stick drops unseen.");
                return;
            }
            var go = new GameObject("BoscaliSummer.HaloDrop");
            if (Datum.origin != null) go.transform.SetParent(Datum.origin, false);
            if (!Track(go)) return;
            go.AddComponent<HaloDropOperation>().Initialize(plan, startDelay, jumper, canopy, aircraft);
        }

        /// <summary>
        /// Draws a HALO stick by sampling its <see cref="HaloGlidePlan"/> every frame: no physics,
        /// so every peer shows the same flight. When the carrier aircraft is present, each pair
        /// runs down the open cargo bay ramp, leaps off the lip and smoothly blends into the plan's
        /// wingsuit formation so the drop stays anchored to the aircraft even while manoeuvring.
        /// The plan is in global coordinates; samples become local positions after the floating-origin
        /// shift (CameraStateManager.LateUpdate, order 2), hence the late LateUpdate.
        /// </summary>
        [DefaultExecutionOrder(1000)]
        private sealed class HaloDropOperation : MonoBehaviour
        {
            /// <summary>Seconds between smoke puffs; at ~48 m/s glide speed (~0.75 m spacing) overlapping puffs form a seamless volumetric ribbon.</summary>
            private const float PuffInterval = 0.016f;
            /// <summary>Longest stretch a single frame catches up after a hitch; beyond it the trail skips rather than bursts.</summary>
            private const float MaximumCatchUp = 0.35f;
            /// <summary>Live puffs for a full stick: 16 jumpers x (6.5 s life / 0.016 s) ~ 6500, with headroom.</summary>
            private const int MaximumTrailPuffs = 8192;
            /// <summary>Time spent running down the open cargo bay ramp before leaping off the lip.</summary>
            private const float RampRunSeconds = 0.85f;
            /// <summary>Seconds over which a live-ramp exit Hermite-blends onto the deterministic glide plan.</summary>
            private const float ExitBlendSeconds = 5.5f;
            /// <summary>Time after leaving the lip to rotate from the aft-facing dive into the plan heading.</summary>
            private const float ExitTurnSeconds = 1.15f;
            private static readonly string[] BodyShapes = { "Track", "Exit", "Hang", "SteerL", "SteerR", "Flare", "Land" };

            private static readonly FieldInfo RampPartField = typeof(CargoRamp).GetField("rampPart", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo RampLatchField = typeof(CargoRamp).GetField("rampLatch", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo FuselageLatchField = typeof(CargoRamp).GetField("fuselageLatch", BindingFlags.Instance | BindingFlags.NonPublic);
            private static readonly FieldInfo RampSurfaceNormalField = typeof(CargoRamp).GetField("rampSurfaceNormal", BindingFlags.Instance | BindingFlags.NonPublic);

            private sealed class Jumper
            {
                public Transform Body, Canopy;
                public SkinnedMeshRenderer BodyMesh, CanopyMesh;
                public int[] Shapes;
                public int Snivel, Packed;
                public HaloPhase LastPhase;
                public float ExitAt;
                public bool LiveExitCaptured;
                public Vector3 ExitPosOffset;
                public Vector3 ExitVelDelta;
                public Quaternion ExitDiveRotation;
                public float FlareIntensity;
                public bool WasBurning;
                public float IgnitionFlash;
                public bool HasLastNozzles;
                public Vector3 LastLeftLocal, LastRightLocal;
                public float PuffAccumulator;
                public bool NextRightNozzle;
            }

            private HaloGlidePlan plan;
            private FactionHQ carrierHq;
            private float startAt;
            private bool smokePopped, touchdownSfxPlayed;
            private Aircraft aircraft;
            private CargoRamp cargoRamp;
            private AeroPart rampPart;
            private Transform rampLatch;
            private Transform fuselageLatch;
            private Transform rampSurfaceNormal;
            private Transform fallbackBayAnchor;
            private Jumper[] jumpers;
            private HaloFlareVisuals flares;
            private AudioSource sfx;
            private readonly float[] weights = new float[7];
            private ParticleSystem trail;
            private float lastSampleTime;
            private uint rng = 0xA511E9B3u;
            private Vector3 frameSumGlobal;
            private int frameVis, frameExit, frameGlide, frameDeploy, frameCanopy;
            private GlobalPosition stickCentroid;
            private int activeJumpers;
            private HaloPhase stickPhase;
            private float stickEta;
            private bool hasSnapshot;

            public void Initialize(HaloGlidePlan haloPlan, float startDelay, GameObject jumperTemplate, GameObject canopyTemplate, Aircraft carrier)
            {
                plan = haloPlan;
                carrierHq = carrier != null ? carrier.NetworkHQ : null;
                startAt = Time.time + startDelay;
                lastSampleTime = -startDelay;
                aircraft = carrier;
                ResolveRampRefs(carrier);
                jumpers = new Jumper[plan.Count];
                flares = new HaloFlareVisuals(transform, GlowFx.Glow);
                trail = CreateTrail();
                if (!Application.isBatchMode)
                {
                    var sfxGo = new GameObject("HALO sfx");
                    sfxGo.transform.SetParent(transform, false);
                    sfx = sfxGo.AddComponent<AudioSource>();
                    sfx.spatialBlend = 1f;
                    sfx.minDistance = 20f;
                    sfx.maxDistance = 450f;
                    sfx.rolloffMode = AudioRolloffMode.Logarithmic;
                    sfx.dopplerLevel = 0f;
                    sfx.playOnAwake = false;
                }
                for (int i = 0; i < plan.Count; i++)
                {
                    GameObject body = Instantiate(jumperTemplate, transform);
                    body.name = "Jumper_" + (i + 1);
                    GameObject chute = Instantiate(canopyTemplate, body.transform);
                    chute.name = "Canopy";
                    chute.transform.localPosition = Vector3.zero;
                    chute.transform.localRotation = Quaternion.identity;
                    chute.SetActive(true);
                    var j = new Jumper
                    {
                        Body = body.transform, Canopy = chute.transform,
                        BodyMesh = body.GetComponentInChildren<SkinnedMeshRenderer>(true),
                        CanopyMesh = chute.GetComponentInChildren<SkinnedMeshRenderer>(true),
                        Shapes = new int[BodyShapes.Length],
                        LastPhase = HaloPhase.Aboard,
                        ExitAt = (i / 2) * HaloGlidePlan.ExitInterval,
                        NextRightNozzle = (i % 2) == 1,
                    };
                    Mesh bodyMesh = j.BodyMesh != null ? j.BodyMesh.sharedMesh : null;
                    for (int s = 0; s < BodyShapes.Length; s++)
                        j.Shapes[s] = bodyMesh != null ? bodyMesh.GetBlendShapeIndex(BodyShapes[s]) : -1;
                    Mesh canopyMesh = j.CanopyMesh != null ? j.CanopyMesh.sharedMesh : null;
                    j.Snivel = canopyMesh != null ? canopyMesh.GetBlendShapeIndex("Snivel") : -1;
                    j.Packed = canopyMesh != null ? canopyMesh.GetBlendShapeIndex("Packed") : -1;
                    body.SetActive(false);
                    jumpers[i] = j;
                }
                ActiveHaloDrops.Add(this);
            }

            public bool TrySnapshot(out HaloStickSnapshot snap)
            {
                if (!hasSnapshot || plan == null)
                {
                    snap = default;
                    return false;
                }
                snap = new HaloStickSnapshot(
                    stickCentroid,
                    new GlobalPosition(plan.StickLanding.x, plan.StickLanding.y, plan.StickLanding.z),
                    carrierHq, activeJumpers, stickPhase, stickEta);
                return true;
            }

            private void ResolveRampRefs(Aircraft carrier)
            {
                if (carrier == null) return;
                cargoRamp = carrier.GetComponentInChildren<CargoRamp>(true);
                if (cargoRamp != null)
                {
                    rampPart = RampPartField != null ? RampPartField.GetValue(cargoRamp) as AeroPart : null;
                    rampLatch = RampLatchField != null ? RampLatchField.GetValue(cargoRamp) as Transform : null;
                    fuselageLatch = FuselageLatchField != null ? FuselageLatchField.GetValue(cargoRamp) as Transform : null;
                    rampSurfaceNormal = RampSurfaceNormalField != null ? RampSurfaceNormalField.GetValue(cargoRamp) as Transform : null;
                    return;
                }

                BayDoor[] doors = carrier.GetComponentsInChildren<BayDoor>(true);
                if (doors != null && doors.Length > 0)
                {
                    float best = float.MaxValue;
                    Vector3 acPos = carrier.transform.position;
                    Vector3 acFwd = carrier.transform.forward;
                    for (int i = 0; i < doors.Length; i++)
                    {
                        if (doors[i] == null) continue;
                        Transform candidate = doors[i].transform;
                        float score = Vector3.Dot(candidate.position - acPos, acFwd);
                        if (score < best) { best = score; fallbackBayAnchor = candidate; }
                    }
                }
            }

            private bool TryGetLiveRamp(out Vector3 bayInside, out Vector3 rampLip, out Vector3 rampUp, out Vector3 acForward, out Vector3 acRight, out Vector3 acVelocity)
            {
                bayInside = rampLip = rampUp = acForward = acRight = acVelocity = Vector3.zero;
                if (aircraft == null || aircraft.disabled) return false;
                Transform ac = aircraft.transform;
                acForward = ac.forward;
                acRight = ac.right;
                Vector3 acUp = ac.up;
                acVelocity = aircraft.rb != null ? aircraft.rb.velocity : acForward * 60f;

                if (cargoRamp != null)
                {
                    rampUp = rampSurfaceNormal != null && Vector3.Dot(rampSurfaceNormal.forward, acUp) > 0.25f
                        ? rampSurfaceNormal.forward.normalized
                        : acUp;
                    if (rampLatch != null)
                    {
                        Vector3 lipBase = rampLatch.position;
                        // If the ramp hasn't swung down yet (e.g. instant automation drop), drop slightly below the closed latch.
                        bool stillClosed = fuselageLatch != null && (lipBase - fuselageLatch.position).sqrMagnitude < 0.35f;
                        rampLip = lipBase + rampUp * (stillClosed ? -0.45f : HaloGlidePlan.PelvisHeight * 0.9f) - acForward * 0.25f;
                        bayInside = rampLip + acForward * 4.2f + acUp * 0.45f;
                    }
                    else
                    {
                        Transform pivot = rampPart != null ? rampPart.transform : cargoRamp.transform;
                        rampLip = pivot.position - acForward * 2.0f + acUp * (HaloGlidePlan.PelvisHeight - 0.35f);
                        bayInside = pivot.position + acForward * 2.2f + acUp * HaloGlidePlan.PelvisHeight;
                    }
                    return true;
                }

                rampUp = acUp;
                if (fallbackBayAnchor != null)
                {
                    rampLip = fallbackBayAnchor.position - acForward * 1.6f - acUp * 0.35f;
                    bayInside = fallbackBayAnchor.position + acForward * 2.4f + acUp * 0.2f;
                    return true;
                }

                rampLip = ac.position - acForward * 9.0f - acUp * 0.8f;
                bayInside = ac.position - acForward * 4.8f - acUp * 0.3f;
                return true;
            }

            /// <summary>
            /// One emitter for the whole stick, silent on its own: <see cref="EmitNozzleTrail"/> places puffs
            /// directly along each burning ankle nozzle's path. Local to the drop, so the trail rides the
            /// floating origin with the rest of the stick.
            /// </summary>
            private ParticleSystem CreateTrail()
            {
                var go = new GameObject("HALO smoke trail");
                go.transform.SetParent(transform, false);
                go.SetActive(false);
                var particles = go.AddComponent<ParticleSystem>();
                var main = particles.main;
                main.playOnAwake = false;
                main.loop = true;
                main.duration = 1f;
                main.startLifetime = new ParticleSystem.MinMaxCurve(5.5f, 7.5f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(1.65f, 2.25f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = Color.white;
                main.gravityModifier = 0f;
                main.maxParticles = MaximumTrailPuffs;
                main.simulationSpace = ParticleSystemSimulationSpace.Local;
                main.cullingMode = ParticleSystemCullingMode.PauseAndCatchup;

                var emission = particles.emission;
                emission.enabled = false;
                var shape = particles.shape;
                shape.enabled = false;

                // Overlapping plume right off the ankle canister that billows smoothly into a wide ribbon.
                var size = particles.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.72f),
                    new Keyframe(0.08f, 1.35f),
                    new Keyframe(0.38f, 2.55f),
                    new Keyframe(1f, 4.4f)));

                // Flare-lit orange at the nozzle -> vivid crimson tactical HALO smoke -> dusky rose-grey as it thins.
                var fade = particles.colorOverLifetime;
                fade.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] {
                        new GradientColorKey(new Color(1f, 0.56f, 0.16f), 0f),
                        new GradientColorKey(new Color(0.88f, 0.18f, 0.10f), 0.05f),
                        new GradientColorKey(new Color(0.72f, 0.16f, 0.13f), 0.42f),
                        new GradientColorKey(new Color(0.46f, 0.28f, 0.28f), 1f)
                    },
                    new[] {
                        new GradientAlphaKey(0f, 0f),
                        new GradientAlphaKey(0.60f, 0.012f),
                        new GradientAlphaKey(0.44f, 0.28f),
                        new GradientAlphaKey(0.20f, 0.68f),
                        new GradientAlphaKey(0f, 1f)
                    });
                fade.color = gradient;

                // Slow billowing roll so puffs read as volumetric smoke rather than static stamps.
                var rot = particles.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-0.55f, 0.55f);

                // Exhaust velocity bursts off the canister then decelerates smoothly into the ambient air.
                var drag = particles.limitVelocityOverLifetime;
                drag.enabled = true;
                drag.limit = 1.4f;
                drag.dampen = 0.22f;

                // Gentle high-altitude curling and eddies so the ribbon breaks up organically.
                var noise = particles.noise;
                noise.enabled = true;
                noise.strength = 0.7f;
                noise.frequency = 0.22f;
                noise.scrollSpeed = 0.25f;
                noise.damping = true;
                noise.quality = ParticleSystemNoiseQuality.Low;

                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = SmokeFx.Puff;
                renderer.minParticleSize = 0f;
                renderer.maxParticleSize = 0.65f;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                go.SetActive(true);
                particles.Play();
                return particles;
            }

            private void LateUpdate()
            {
                float t = Time.time - startAt;
                if (t > plan.TotalSeconds)
                {
                    Destroy(gameObject);
                    return;
                }
                float dt = Mathf.Clamp(t - lastSampleTime, 0.0001f, MaximumCatchUp);
                lastSampleTime = t;

                bool hasRamp = TryGetLiveRamp(out Vector3 bayInside, out Vector3 rampLip, out Vector3 rampUp,
                    out Vector3 acForward, out Vector3 acRight, out Vector3 acVelocity);

                frameSumGlobal = Vector3.zero;
                frameVis = frameExit = frameGlide = frameDeploy = frameCanopy = 0;

                flares.BeginFrame(t);
                for (int i = 0; i < jumpers.Length; i++)
                    Draw(i, jumpers[i], t, dt, hasRamp, bayInside, rampLip, rampUp, acForward, acRight, acVelocity);
                flares.EndFrame();

                if (frameVis > 0)
                {
                    Vector3 avg = frameSumGlobal / frameVis;
                    stickCentroid = new GlobalPosition(avg.x, avg.y, avg.z);
                    activeJumpers = frameVis;
                    stickPhase = frameDeploy > 0 ? HaloPhase.Deploy
                        : frameCanopy > 0 ? HaloPhase.Canopy
                        : frameGlide > 0 ? HaloPhase.Glide
                        : frameExit > 0 ? HaloPhase.Exit
                        : HaloPhase.Landed;
                    stickEta = Mathf.Max(0f, plan.FirstLandingSeconds - t);
                    hasSnapshot = true;
                }
                else
                {
                    hasSnapshot = false;
                }
            }

            private void OnDestroy()
            {
                ActiveHaloDrops.Remove(this);
                flares?.Clear();
            }

            private static Vector3 Local(Vector3 global) => new GlobalPosition(global.x, global.y, global.z).ToLocalPosition();
            private static Vector3 Global(Vector3 local) => local.ToGlobalPosition().AsVector3();

            private void Draw(int index, Jumper j, float t, float dt, bool hasRamp,
                Vector3 bayInside, Vector3 rampLip, Vector3 rampUp, Vector3 acForward, Vector3 acRight, Vector3 acVelocity)
            {
                // Pre-exit ramp run: paratroopers step out of the cargo bay in pairs and sprint off the open ramp lip.
                if (hasRamp && t < j.ExitAt && t >= j.ExitAt - RampRunSeconds)
                {
                    if (!j.Body.gameObject.activeSelf) j.Body.gameObject.SetActive(true);
                    if (j.Canopy.gameObject.activeSelf) j.Canopy.gameObject.SetActive(false);

                    float u = Mathf.Clamp01((t - (j.ExitAt - RampRunSeconds)) / RampRunSeconds);
                    float travel = u * (0.65f + 0.35f * u);
                    float stride = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 3.5f));
                    float hop = u > 0.78f ? Mathf.Sin(((u - 0.78f) / 0.22f) * Mathf.PI) * 0.28f : 0f;
                    float side = (index % 2 == 0 ? -0.58f : 0.58f) * Mathf.Lerp(1f, 1.22f, u * u);

                    j.Body.position = Vector3.Lerp(bayInside, rampLip, travel) + acRight * side + rampUp * (stride * 0.09f + hop);
                    float lean = Mathf.Lerp(12f, 42f, u * u);
                    float sway = Mathf.Sin(u * Mathf.PI * 3.5f) * 4f * (1f - u * 0.5f);
                    j.Body.rotation = Quaternion.LookRotation(-acForward, rampUp) * Quaternion.Euler(lean, 0f, sway);

                    Array.Clear(weights, 0, weights.Length);
                    float dive = Scalar.SmoothStep(0.35f, 1f, u);
                    weights[1] = dive;                 // Exit
                    weights[6] = (1f - dive) * 0.55f;  // Land (crouched athletic run)
                    weights[5] = (1f - dive) * 0.45f;  // Flare (arms braced)
                    if (j.BodyMesh != null)
                        for (int k = 0; k < weights.Length; k++)
                            if (j.Shapes[k] >= 0) j.BodyMesh.SetBlendShapeWeight(j.Shapes[k], weights[k] * 100f);

                    j.HasLastNozzles = false;
                    j.LastPhase = HaloPhase.Aboard;
                    frameSumGlobal += Global(j.Body.position);
                    frameVis++;
                    frameExit++;
                    return;
                }

                HaloSample s = plan.Sample(index, t);
                bool visible = s.Phase != HaloPhase.Aboard && s.Phase != HaloPhase.Gone;
                if (j.Body.gameObject.activeSelf != visible) j.Body.gameObject.SetActive(visible);
                if (!visible)
                {
                    j.HasLastNozzles = false;
                    return;
                }

                // Capture the exact live ramp lip position & velocity the moment this jumper steps off,
                // then C1-Hermite blend onto the deterministic glide plan over ExitBlendSeconds.
                if (!j.LiveExitCaptured && hasRamp && t >= j.ExitAt && t < j.ExitAt + ExitBlendSeconds)
                {
                    j.LiveExitCaptured = true;
                    float side = (index % 2 == 0 ? -0.58f : 0.58f) * 1.22f;
                    Vector3 liveLipGlobal = Global(rampLip + acRight * side);
                    Vector3 liveExitVel = acVelocity - acForward * 4.5f - rampUp * 1.6f + acRight * (side * 1.4f);
                    HaloSample atExit = plan.Sample(index, j.ExitAt);
                    Vector3 planExitVel = (plan.Sample(index, j.ExitAt + 0.05f).Position - atExit.Position) / 0.05f;
                    j.ExitPosOffset = liveLipGlobal - atExit.Position;
                    j.ExitVelDelta = liveExitVel - planExitVel;
                    j.ExitDiveRotation = Quaternion.LookRotation(-acForward, rampUp) * Quaternion.Euler(42f, 0f, 0f);
                }

                Vector3 globalPos = s.Position;
                if (j.LiveExitCaptured)
                {
                    float sinceExit = t - j.ExitAt;
                    if (sinceExit < ExitBlendSeconds)
                    {
                        float tau = Mathf.Clamp01(sinceExit / ExitBlendSeconds);
                        float tau2 = tau * tau, tau3 = tau2 * tau;
                        float h00 = 2f * tau3 - 3f * tau2 + 1f;
                        float h10 = tau3 - 2f * tau2 + tau;
                        globalPos += j.ExitPosOffset * h00 + j.ExitVelDelta * (ExitBlendSeconds * h10);
                    }
                }

                frameSumGlobal += globalPos;
                frameVis++;
                switch (s.Phase)
                {
                    case HaloPhase.Exit: frameExit++; break;
                    case HaloPhase.Glide: frameGlide++; break;
                    case HaloPhase.Deploy: frameDeploy++; break;
                    case HaloPhase.Canopy: frameCanopy++; break;
                }

                Vector3 localPos = Local(globalPos);
                if (!smokePopped && s.Phase == HaloPhase.Canopy)
                {
                    smokePopped = true;
                    LandingSmoke.Pop(plan.StickLanding, Mathf.Clamp(plan.TotalSeconds - t + 5f, 12f, 60f));
                }
                if (s.Phase == HaloPhase.Deploy && j.LastPhase != HaloPhase.Deploy)
                {
                    if (trail != null)
                    {
                        Vector3 canopyLocal = trail.transform.InverseTransformPoint(localPos + Vector3.up * 2.4f);
                        for (int p = 0; p < 3; p++)
                            trail.Emit(new ParticleSystem.EmitParams
                            {
                                position = canopyLocal,
                                velocity = new Vector3(NextSigned(), 0.4f + NextSigned() * 0.25f, NextSigned()) * 2.2f,
                                startSize = 2.3f,
                                startLifetime = 1.15f,
                                startColor = new Color(0.86f, 0.92f, 0.98f, 0.42f),
                            }, 1);
                    }
                    if ((index % HaloGlidePlan.TeamSize) == 0 && sfx != null)
                    {
                        AudioClip clip = SfxClips.Get("pilot-suit-vent") ?? SfxClips.Get("flare-burst-pop");
                        if (clip != null) { sfx.transform.position = localPos; sfx.PlayOneShot(clip, 0.7f); }
                    }
                }
                if (s.Phase == HaloPhase.Landed && j.LastPhase != HaloPhase.Landed)
                {
                    LandingDust(localPos);
                    if (!touchdownSfxPlayed && sfx != null)
                    {
                        touchdownSfxPlayed = true;
                        AudioClip clip = SfxClips.Get("hull-thud");
                        if (clip != null) { sfx.transform.position = localPos; sfx.PlayOneShot(clip, 0.55f); }
                    }
                }
                j.LastPhase = s.Phase;

                j.Body.position = localPos;
                if ((index % HaloGlidePlan.TeamSize) == 0 &&
                    (s.Phase == HaloPhase.Deploy || s.Phase == HaloPhase.Canopy || s.Phase == HaloPhase.Landed))
                    flares.DrawStrobe(localPos + Vector3.up * 0.85f, t + index * 0.37f, s.Phase == HaloPhase.Landed);
                // Pitch tips the head forward into the belly-down glide; toggles bank the canopy turn.
                Quaternion planRot = Quaternion.LookRotation(s.Heading, Vector3.up) * Quaternion.Euler(s.Pitch, 0f, -s.Steer * 14f);
                if (j.LiveExitCaptured && t - j.ExitAt < ExitTurnSeconds)
                {
                    float turn = Scalar.SmoothStep(0f, 1f, (t - j.ExitAt) / ExitTurnSeconds);
                    j.Body.rotation = Quaternion.Slerp(j.ExitDiveRotation, planRot, turn);
                }
                else
                {
                    j.Body.rotation = planRot;
                }

                Pose(s);
                if (j.BodyMesh != null)
                    for (int k = 0; k < weights.Length; k++)
                        if (j.Shapes[k] >= 0) j.BodyMesh.SetBlendShapeWeight(j.Shapes[k], weights[k] * 100f);

                // Smooth flare ignition flash & burnout envelope.
                bool ignitionBurst = s.Smoke && !j.WasBurning;
                if (ignitionBurst) j.IgnitionFlash = 1f;
                j.WasBurning = s.Smoke;
                if (j.IgnitionFlash > 0f) j.IgnitionFlash = Mathf.Max(0f, j.IgnitionFlash - dt * 3.5f);
                float targetIntensity = s.Smoke ? 1f : 0f;
                float rate = s.Smoke ? 4.5f : 2.2f;
                j.FlareIntensity = Mathf.MoveTowards(j.FlareIntensity, targetIntensity, dt * rate);

                if (j.FlareIntensity > 0.01f && j.BodyMesh != null)
                {
                    Vector3 leftWorld = j.BodyMesh.transform.TransformPoint(HaloFlareVisuals.TrackLeftNozzle);
                    Vector3 rightWorld = j.BodyMesh.transform.TransformPoint(HaloFlareVisuals.TrackRightNozzle);
                    Vector3 velocity = (s.Position - plan.Sample(index, t - 0.05f).Position) / 0.05f;
                    float visualIntensity = j.FlareIntensity * (1f + 0.45f * j.IgnitionFlash);
                    bool wantLight = (index % HaloGlidePlan.TeamSize) == 0;
                    flares.Draw(leftWorld, rightWorld, velocity, t + index * 0.193f, visualIntensity, ignitionBurst, wantLight);
                    EmitNozzleTrail(j, leftWorld, rightWorld, velocity, dt, s.Smoke);
                }
                else
                {
                    j.HasLastNozzles = false;
                }

                bool chute = s.Canopy > 0.001f;
                if (j.Canopy.gameObject.activeSelf != chute) j.Canopy.gameObject.SetActive(chute);
                if (chute && j.CanopyMesh != null)
                {
                    // Packed -> snivel (slider holds it narrow) -> open.
                    float packed = Mathf.Clamp01(1f - s.Canopy * 2f);
                    float snivel = s.Canopy < 0.5f ? s.Canopy * 2f : 2f - s.Canopy * 2f;
                    if (j.Packed >= 0) j.CanopyMesh.SetBlendShapeWeight(j.Packed, packed * 100f);
                    if (j.Snivel >= 0) j.CanopyMesh.SetBlendShapeWeight(j.Snivel, snivel * 100f);
                }
            }

            /// <summary>Crossfade weights over the baked poses; they always sum to one.</summary>
            private void Pose(HaloSample s)
            {
                Array.Clear(weights, 0, weights.Length);
                const int track = 0, exit = 1, hang = 2, left = 3, right = 4, flare = 5, land = 6;
                switch (s.Phase)
                {
                    case HaloPhase.Exit:
                        // Arch out of the tumble into the track as the body levels.
                        float level = Mathf.Clamp01((s.Pitch - 10f) / 70f);
                        weights[exit] = 1f - level;
                        weights[plan.LowDrop ? hang : track] += level;
                        break;
                    case HaloPhase.Glide:
                        weights[track] = 1f;
                        break;
                    case HaloPhase.Deploy:
                        weights[hang] = 1f - s.Pitch / 80f;
                        weights[plan.LowDrop ? hang : track] += s.Pitch / 80f;
                        break;
                    case HaloPhase.Canopy:
                        float turn = Mathf.Abs(s.Steer) * (1f - s.Flare);
                        weights[s.Steer < 0f ? left : right] = turn;
                        weights[flare] = s.Flare;
                        weights[hang] = 1f - turn - s.Flare;
                        break;
                    case HaloPhase.Landed:
                        weights[flare] = s.Flare;
                        weights[land] = 1f - s.Flare;
                        break;
                }
            }

            /// <summary>
            /// Emits dense, continuous smoke puffs directly along each burning ankle nozzle's frame path,
            /// alternating left and right boots so dual jets merge into a billowing sky-ribbon.
            /// </summary>
            private void EmitNozzleTrail(Jumper j, Vector3 leftWorld, Vector3 rightWorld, Vector3 velocity, float dt, bool active)
            {
                Vector3 curLeft = trail.transform.InverseTransformPoint(leftWorld);
                Vector3 curRight = trail.transform.InverseTransformPoint(rightWorld);
                if (!j.HasLastNozzles)
                {
                    j.LastLeftLocal = curLeft;
                    j.LastRightLocal = curRight;
                    j.HasLastNozzles = true;
                }
                if (!active)
                {
                    j.LastLeftLocal = curLeft;
                    j.LastRightLocal = curRight;
                    return;
                }

                Vector3 trailDir = velocity.sqrMagnitude > 1f ? -velocity.normalized : Vector3.up;
                Vector3 localExhaust = trail.transform.InverseTransformDirection(trailDir) * 4.5f;

                j.PuffAccumulator += dt;
                int steps = Mathf.Min(24, (int)(j.PuffAccumulator / PuffInterval));
                if (steps > 0)
                {
                    j.PuffAccumulator -= steps * PuffInterval;
                    for (int k = 1; k <= steps; k++)
                    {
                        float u = Mathf.Clamp01(((float)k + NextSigned() * 0.35f) / steps);
                        Vector3 pos = j.NextRightNozzle
                            ? Vector3.Lerp(j.LastRightLocal, curRight, u)
                            : Vector3.Lerp(j.LastLeftLocal, curLeft, u);
                        j.NextRightNozzle = !j.NextRightNozzle;
                        Vector3 spread = new Vector3(NextSigned(), NextSigned(), NextSigned()) * 1.05f;
                        trail.Emit(new ParticleSystem.EmitParams
                        {
                            position = pos,
                            velocity = localExhaust + spread,
                        }, 1);
                    }
                }

                j.LastLeftLocal = curLeft;
                j.LastRightLocal = curRight;
            }

            private float NextSigned()
            {
                rng ^= rng << 13;
                rng ^= rng >> 17;
                rng ^= rng << 5;
                return (rng & 0x00FFFFFFu) * (2f / 16777216f) - 1f;
            }

            private static void LandingDust(Vector3 pelvis)
            {
                if (GameAssets.i == null || GameAssets.i.contactDust == null) return;
                GameObject dust = Instantiate(GameAssets.i.contactDust, pelvis - Vector3.up * (HaloGlidePlan.PelvisHeight - 0.2f), Quaternion.identity);
                dust.SetActive(true);
                Destroy(dust, 3.5f);
            }
        }

        private static bool TryTrooperTemplate(out GameObject trooper)
        {
            trooper = null;
            if (Encyclopedia.WeaponLookup == null ||
                !Encyclopedia.WeaponLookup.TryGetValue(AirAssaultController.HaloMountKey, out WeaponMount mount) ||
                mount == null || mount.prefab == null)
                return false;
            trooper = mount.prefab.transform.Find("AirborneTrooper")?.gameObject;
            return trooper != null;
        }

        public static void SpawnRopeInsertion(Aircraft aircraft, RopeMode mode, float height, Vector3 landing, GameObject shell, int count)
        {
            if (aircraft == null || HasActiveRappel(aircraft)) return;
            var go = new GameObject("BoscaliSummer.RopeInsertion");
            if (!Track(go)) return;
            go.AddComponent<RopeOperation>().Insertion(aircraft, mode, height, landing, shell, count);
        }

        public static void SpawnRopeExtraction(Aircraft aircraft, Vector3 site, int count, float height)
        {
            if (aircraft == null || HasActiveRappel(aircraft)) return;
            var go = new GameObject("BoscaliSummer.RopeExtraction");
            if (!Track(go)) return;
            go.AddComponent<RopeOperation>().Extraction(aircraft, site, count, height);
        }

        /// <summary>
        /// Ibis rope work drawn from a <see cref="FastRopePlan"/>: two ropes off the side doors,
        /// troopers posed from the plan's phases, insertion ropes cut away to fall once the squad is
        /// down, extraction ropes reeled in with the riders. Plan points are global; the doors are
        /// read from the live helicopter every frame after the floating-origin shift.
        /// </summary>
        [DefaultExecutionOrder(1000)]
        private sealed class RopeOperation : MonoBehaviour
        {
            private const float DoorHalfWidth = 1.35f;
            private const float DoorDrop = 0.45f;
            private const int RopePoints = 12;
            private static readonly string[] ShapeNames = { "Rope", "Rappel", "Kneel", "WalkA", "WalkB", "Ride", "Land" };
            private const int Rope = 0, Rappel = 1, Kneel = 2, WalkA = 3, WalkB = 4, Ride = 5, Land = 6;

            public Aircraft Aircraft { get; private set; }

            private FastRopePlan plan;
            private bool extraction;
            private float startAt;
            private Vector3 cabinLocal;
            private Transform[] bodies;
            private SkinnedMeshRenderer[] meshes;
            private int[] shapes;
            private LineRenderer[] ropes;
            private readonly Vector3[] feet = new Vector3[2];
            private readonly Vector3[] cutTop = new Vector3[2];
            private readonly Vector3[] line = new Vector3[RopePoints];
            private readonly float[] weights = new float[ShapeNames.Length];
            private RopePhase[] lastPhase;
            private float cutAt = -1f;
            private AudioSource audioSource;

            private static AudioClip cachedWinchStart;
            private static AudioClip cachedWinchStop;
            private static AudioClip cachedWinchLoop;
            private static bool audioProbed;

            private static Vector3 Global(Vector3 local) => local.ToGlobalPosition().AsVector3();
            private static Vector3 Local(Vector3 global) => new GlobalPosition(global.x, global.y, global.z).ToLocalPosition();

            public void Insertion(Aircraft aircraft, RopeMode mode, float height, Vector3 landing, GameObject shell, int count)
            {
                Setup(aircraft, count);
                Vector3[] doors = Doors();
                for (int k = 0; k < 2; k++) feet[k] = Global(Foot(doors[k], landing + aircraft.transform.right * (k == 0 ? -DoorHalfWidth : DoorHalfWidth)));
                Vector3[] destinations = Destinations(landing, shell, bodies.Length, out bool enter);
                for (int i = 0; i < destinations.Length; i++) destinations[i] = Global(destinations[i]);
                plan = FastRopePlan.Insertion(mode, height, feet, destinations, enter);
            }

            public void Extraction(Aircraft aircraft, Vector3 site, int count, float height)
            {
                Setup(aircraft, count);
                extraction = true;
                Vector3[] doors = Doors();
                for (int k = 0; k < 2; k++) feet[k] = Global(Foot(doors[k], doors[k] + Vector3.down * height));
                var sources = new Vector3[bodies.Length];
                for (int i = 0; i < sources.Length; i++)
                {
                    float a = i * Mathf.PI * 2f / sources.Length;
                    sources[i] = Global(Ground(site + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 3f));
                }
                plan = FastRopePlan.Extraction(height, feet, sources);
                PlayWinchAudio(isDeploy: false);
            }

            private void Setup(Aircraft aircraft, int count)
            {
                Aircraft = aircraft;
                startAt = Time.time;
                // The troop bench sits in the cabin; the side doors are either side of it.
                MountedTroops bench = aircraft.GetComponentInChildren<MountedTroops>();
                Vector3 cabin = bench != null ? bench.transform.position : aircraft.transform.position;
                cabinLocal = aircraft.transform.InverseTransformPoint(cabin);

                Material ropeMat = MaterialProvider.GetCargoHookRopeMaterial();
                ropes = new[] { CreateRopeLine("Rope_Left", ropeMat), CreateRopeLine("Rope_Right", ropeMat) };
                TryTrooperTemplate(out GameObject template);
                count = Mathf.Clamp(count, 1, FastRopePlan.MaxTroops);
                bodies = new Transform[count];
                meshes = new SkinnedMeshRenderer[count];
                shapes = new int[ShapeNames.Length];
                lastPhase = new RopePhase[count];
                for (int i = 0; i < count; i++)
                {
                    GameObject body = template != null
                        ? Instantiate(template, transform)
                        : VanillaSoldierFactory.CreateVisualSoldier(transform.position, Quaternion.identity, transform) ?? new GameObject();
                    body.transform.SetParent(transform, true);
                    body.name = "Trooper_" + (i + 1);
                    body.SetActive(false);
                    bodies[i] = body.transform;
                    meshes[i] = template != null ? body.GetComponentInChildren<SkinnedMeshRenderer>(true) : null;
                }
                Mesh mesh = meshes[0] != null ? meshes[0].sharedMesh : null;
                for (int s = 0; s < ShapeNames.Length; s++) shapes[s] = mesh != null ? mesh.GetBlendShapeIndex(ShapeNames[s]) : -1;
                SetupAudio();
            }

            private Vector3[] Doors()
            {
                Transform helo = Aircraft.transform;
                Vector3 cabin = helo.TransformPoint(cabinLocal) - helo.up * DoorDrop;
                return new[] { cabin - helo.right * DoorHalfWidth, cabin + helo.right * DoorHalfWidth };
            }

            private static Vector3 Foot(Vector3 door, Vector3 fallback) =>
                Physics.Raycast(door, Vector3.down, out RaycastHit hit, FastRopePlan.RappelMaxHeight + 20f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore)
                    ? hit.point : fallback;

            private static Vector3 Ground(Vector3 point) =>
                Physics.Raycast(point + Vector3.up * 6f, Vector3.down, out RaycastHit hit, 30f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore)
                    ? hit.point : point;

            /// <summary>
            /// On a roof: overwatch along its edges. Beside a building: stack up on the nearest wall
            /// and go in. In the open: a kneeling perimeter.
            /// </summary>
            private static Vector3[] Destinations(Vector3 landing, GameObject shell, int count, out bool enter)
            {
                enter = false;
                var d = new Vector3[count];
                if (shell != null)
                {
                    for (int k = 0; k < count; k++)
                    {
                        float a = k * Mathf.PI * 2f / count + 0.4f;
                        var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                        Vector3 best = landing + dir * 1.5f;
                        for (float r = 2f; r <= 18f; r += 1.5f)
                        {
                            Vector3 probe = landing + dir * r;
                            if (!Physics.Raycast(probe + Vector3.up * 4f, Vector3.down, out RaycastHit hit, 8f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore) ||
                                Mathf.Abs(hit.point.y - landing.y) > 1.2f || AirAssaultController.ResolveCivilianBuilding(hit.collider) != shell)
                                break;
                            best = hit.point - dir;
                        }
                        d[k] = best;
                    }
                    return d;
                }

                Bounds? wall = null;
                float nearest = float.MaxValue;
                foreach (Collider c in Physics.OverlapSphere(landing, 35f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                {
                    if (AirAssaultController.ResolveCivilianBuilding(c) == null) continue;
                    float dist = (c.bounds.ClosestPoint(landing) - landing).sqrMagnitude;
                    if (dist < nearest) { nearest = dist; wall = c.bounds; }
                }
                if (wall.HasValue && nearest > 4f)
                {
                    Vector3 entry = wall.Value.ClosestPoint(landing);
                    entry.y = landing.y;
                    Vector3 inward = entry - landing;
                    inward.y = 0f;
                    inward.Normalize();
                    Vector3 side = Vector3.Cross(Vector3.up, inward);
                    // A file along the wall, point man at the corner of the entry.
                    for (int k = 0; k < count; k++)
                        d[k] = Ground(entry - inward * 0.8f + side * ((k % 2 == 0 ? 1f : -1f) * (0.6f + (k / 2) * 1.1f)));
                    enter = true;
                    return d;
                }

                for (int k = 0; k < count; k++)
                {
                    float a = k * Mathf.PI * 2f / count;
                    d[k] = Ground(landing + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * 7f);
                }
                return d;
            }

            private void LateUpdate()
            {
                float t = Time.time - startAt;
                if (Aircraft == null || plan == null || t > plan.TotalSeconds + 4f)
                {
                    Destroy(gameObject);
                    return;
                }
                Vector3[] doors = Doors();
                Vector3 left = Global(doors[0]), right = Global(doors[1]);
                for (int i = 0; i < bodies.Length; i++)
                    DrawTrooper(i, plan.Sample(i, t, left, right));
                DrawRopes(doors, t);
                if (extraction && plan.RopeOut(t) <= 0f && audioSource != null && audioSource.isPlaying) StopWinchAudio();
            }

            private void DrawTrooper(int i, RopeSample s)
            {
                bool visible = s.Phase != RopePhase.Aboard && s.Phase != RopePhase.Gone;
                Transform body = bodies[i];
                if (body.gameObject.activeSelf != visible) body.gameObject.SetActive(visible);
                if (!visible) return;
                if (s.Phase == RopePhase.Landing && lastPhase[i] == RopePhase.Sliding) SpawnDust(Local(s.Position));
                lastPhase[i] = s.Phase;

                // Plan points are where the boots are; the model pivots at the pelvis. Riders hang under the clip.
                float lift = s.Phase == RopePhase.Riding ? -0.2f : HaloGlidePlan.PelvisHeight;
                body.position = Local(s.Position) + Vector3.up * lift;
                body.rotation = Quaternion.LookRotation(s.Heading, Vector3.up);

                SkinnedMeshRenderer mesh = meshes[i];
                if (mesh == null) return;
                Array.Clear(weights, 0, weights.Length);
                switch (s.Phase)
                {
                    case RopePhase.Sliding: weights[plan.Mode == RopeMode.FastRope ? Rope : Rappel] = 1f; break;
                    case RopePhase.Landing: weights[Land] = 1f - s.Recover; weights[Kneel] = s.Recover; break;
                    case RopePhase.Moving:
                        float a = 0.5f + 0.5f * Mathf.Cos(s.Stride * Mathf.PI * 2f);
                        weights[WalkA] = a;
                        weights[WalkB] = 1f - a;
                        break;
                    case RopePhase.Holding: weights[Kneel] = 1f; break;
                    case RopePhase.Riding: weights[Ride] = 1f; break;
                }
                for (int k = 0; k < weights.Length; k++)
                    if (shapes[k] >= 0) mesh.SetBlendShapeWeight(shapes[k], weights[k] * 100f);
            }

            private void DrawRopes(Vector3[] doors, float t)
            {
                bool cut = plan.RopesCut(t);
                if (cut && cutAt < 0f)
                {
                    cutAt = t;
                    for (int k = 0; k < 2; k++) cutTop[k] = Global(doors[k]);
                }
                for (int k = 0; k < 2; k++)
                {
                    Vector3 foot = Local(feet[k]);
                    Vector3 top;
                    if (cut)
                    {
                        // Released at the door: the free end falls and the rope piles at its foot.
                        float fall = t - cutAt;
                        if (fall > 6f)
                        {
                            ropes[k].enabled = false;
                            continue;
                        }
                        Vector3 start = Local(cutTop[k]);
                        top = start + Vector3.down * Mathf.Min(Mathf.Max(0f, start.y - foot.y), 4.9f * fall * fall);
                    }
                    else
                    {
                        top = doors[k];
                        if (extraction) foot = Vector3.Lerp(top, foot, plan.RopeOut(t));
                    }
                    ropes[k].enabled = true;
                    float length = Mathf.Max(0.5f, (top - foot).magnitude);
                    Vector3 wash = Aircraft.transform.right * (k == 0 ? -0.012f : 0.012f);
                    for (int p = 0; p < RopePoints; p++)
                    {
                        float u = (float)p / (RopePoints - 1);
                        float belly = 4f * u * (1f - u);
                        Vector3 sway = (Vector3.right * Mathf.Sin(t * 3.1f + k * 1.7f) + Vector3.forward * Mathf.Cos(t * 2.4f + k)) * 0.006f;
                        line[p] = Vector3.Lerp(top, foot, u) + (wash + sway + Vector3.down * 0.02f) * (length * belly);
                    }
                    ropes[k].SetPositions(line);
                }
            }

            private LineRenderer CreateRopeLine(string name, Material mat)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                if (mat != null) lr.sharedMaterial = mat;
                lr.startWidth = 0.055f;
                lr.endWidth = 0.045f;
                lr.useWorldSpace = true;
                lr.positionCount = RopePoints;
                lr.alignment = LineAlignment.View;
                lr.textureMode = LineTextureMode.Tile;
                lr.numCapVertices = 2;
                lr.numCornerVertices = 2;
                lr.enabled = false;
                return lr;
            }

            private static void EnsureWinchAudio()
            {
                if (audioProbed) return;
                audioProbed = true;

                try
                {
                    SlingloadHook[] hooks = Resources.FindObjectsOfTypeAll<SlingloadHook>();
                    for (int i = 0; i < hooks.Length; i++)
                    {
                        if (hooks[i] == null) continue;
                        var t = typeof(SlingloadHook);
                        if (cachedWinchStart == null)
                        {
                            var f = t.GetField("winchStartSound", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                            if (f != null) cachedWinchStart = f.GetValue(hooks[i]) as AudioClip;
                        }
                        if (cachedWinchStop == null)
                        {
                            var f = t.GetField("winchStopSound", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                            if (f != null) cachedWinchStop = f.GetValue(hooks[i]) as AudioClip;
                        }
                        if (cachedWinchLoop == null)
                        {
                            var f = t.GetField("winchAudioSource", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                            if (f != null && f.GetValue(hooks[i]) is AudioSource src && src.clip != null)
                                cachedWinchLoop = src.clip;
                        }
                        if (cachedWinchStart != null && cachedWinchStop != null && cachedWinchLoop != null)
                            break;
                    }
                }
                catch { }

                if (cachedWinchStart != null && cachedWinchStop != null && cachedWinchLoop != null)
                    return;

                try
                {
                    AudioClip[] clips = Resources.FindObjectsOfTypeAll<AudioClip>();
                    for (int i = 0; i < clips.Length; i++)
                    {
                        if (clips[i] == null) continue;
                        string n = clips[i].name.ToLowerInvariant();
                        if (cachedWinchStart == null && n.Contains("winchstart")) cachedWinchStart = clips[i];
                        else if (cachedWinchStop == null && n.Contains("winchstop")) cachedWinchStop = clips[i];
                        else if (cachedWinchLoop == null && n.Contains("winchloop")) cachedWinchLoop = clips[i];
                    }
                }
                catch { }
            }

            private void SetupAudio()
            {
                EnsureWinchAudio();
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.spatialBlend = 1f;
                audioSource.minDistance = 6f;
                audioSource.maxDistance = 250f;
                audioSource.rolloffMode = AudioRolloffMode.Logarithmic;
                audioSource.dopplerLevel = 0f;
                audioSource.playOnAwake = false;
            }

            private void PlayWinchAudio(bool isDeploy)
            {
                if (audioSource == null) return;
                if (cachedWinchStart != null)
                    audioSource.PlayOneShot(cachedWinchStart, 0.7f);

                if (cachedWinchLoop != null)
                {
                    audioSource.clip = cachedWinchLoop;
                    audioSource.loop = true;
                    audioSource.volume = 0.45f;
                    audioSource.pitch = isDeploy ? 1.0f : 0.92f;
                    audioSource.Play();
                }
            }

            private void StopWinchAudio()
            {
                if (audioSource == null) return;
                if (audioSource.isPlaying)
                    audioSource.Stop();
                if (cachedWinchStop != null)
                    audioSource.PlayOneShot(cachedWinchStop, 0.65f);
            }

            private void OnDestroy() => StopWinchAudio();

            private static void SpawnDust(Vector3 pos)
            {
                if (GameAssets.i == null || GameAssets.i.contactDust == null) return;
                GameObject dust = Instantiate(GameAssets.i.contactDust, pos + Vector3.up * 0.2f, Quaternion.identity);
                dust.SetActive(true);
                Destroy(dust, 3f);
            }
        }
    }
}
