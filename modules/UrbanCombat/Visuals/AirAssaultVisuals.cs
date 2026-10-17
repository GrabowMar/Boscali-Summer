using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// HALO paratrooper stick and Ibis fast-rope visuals. Client-local and cosmetic:
    /// the server resolves the drop's outcome in AirAssaultController on its own clock.
    /// </summary>
    internal static class AirAssaultVisuals
    {
        private const int MaximumActiveOperations = 8;
        private static readonly List<GameObject> ActiveOperations = new List<GameObject>(MaximumActiveOperations);

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

        public static void SpawnHaloDrop(HaloGlidePlan plan, float startDelay)
        {
            if (!TryHaloTemplates(out GameObject jumper, out GameObject canopy))
            {
                Plugin.Logger.LogWarning("[Air Assault] HALO models missing from the Airborne bundle; the stick drops unseen.");
                return;
            }
            var go = new GameObject("BoscaliSummer.HaloDrop");
            if (Datum.origin != null) go.transform.SetParent(Datum.origin, false);
            if (!Track(go)) return;
            go.AddComponent<HaloDropOperation>().Initialize(plan, startDelay, jumper, canopy);
        }

        /// <summary>
        /// Draws a HALO stick by sampling its <see cref="HaloGlidePlan"/> every frame: no physics,
        /// so every peer shows the same flight. The plan is in global coordinates; samples become
        /// local positions after the floating-origin shift (CameraStateManager.LateUpdate, order 2),
        /// hence the late LateUpdate.
        /// </summary>
        [DefaultExecutionOrder(1000)]
        private sealed class HaloDropOperation : MonoBehaviour
        {
            /// <summary>Seconds between puffs off each jumper's ankles; at glide speed they chain into a continuous trail.</summary>
            private const float PuffInterval = 0.25f;
            /// <summary>Longest stretch a single frame catches up after a hitch; beyond it the trail skips rather than bursts.</summary>
            private const float MaximumCatchUp = 2f;
            /// <summary>Live puffs for a full stick: 16 jumpers x (7 s life / 0.25 s) = 448, with headroom.</summary>
            private const int MaximumTrailPuffs = 512;
            private static readonly string[] BodyShapes = { "Track", "Exit", "Hang", "SteerL", "SteerR", "Flare", "Land" };

            private sealed class Jumper
            {
                public Transform Body, Canopy;
                public SkinnedMeshRenderer BodyMesh, CanopyMesh;
                public int[] Shapes;
                public int Snivel, Packed;
                public HaloPhase LastPhase;
            }

            private HaloGlidePlan plan;
            private float startAt;
            private bool smokePopped;
            private Jumper[] jumpers;
            private HaloFlareVisuals flares;
            private readonly float[] weights = new float[7];
            private ParticleSystem trail;
            private float[] nextPuffAt;

            public void Initialize(HaloGlidePlan haloPlan, float startDelay, GameObject jumperTemplate, GameObject canopyTemplate)
            {
                plan = haloPlan;
                startAt = Time.time + startDelay;
                jumpers = new Jumper[plan.Count];
                flares = new HaloFlareVisuals(transform, GlowFx.Glow);
                trail = CreateTrail();
                nextPuffAt = new float[plan.Count];
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
            }

            /// <summary>
            /// One emitter for the whole stick, silent on its own: <see cref="EmitTrail"/> places every puff.
            /// Local to the drop, so the trail rides the floating origin with the rest of the stick.
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
                main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 7f);
                main.startSpeed = 0f;
                main.startSize = new ParticleSystem.MinMaxCurve(1.1f, 1.6f);
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

                var size = particles.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                    new Keyframe(0f, 0.6f), new Keyframe(0.25f, 1f), new Keyframe(1f, 2.4f)));

                // Warm at the canister, then grey-white as it drifts back and thins out.
                var fade = particles.colorOverLifetime;
                fade.enabled = true;
                var gradient = new Gradient();
                gradient.SetKeys(
                    new[] { new GradientColorKey(new Color(1f, 0.56f, 0.2f), 0f),
                        new GradientColorKey(new Color(0.92f, 0.92f, 0.9f), 0.2f),
                        new GradientColorKey(new Color(0.72f, 0.73f, 0.75f), 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.5f, 0.08f),
                        new GradientAlphaKey(0.38f, 0.5f), new GradientAlphaKey(0f, 1f) });
                fade.color = gradient;

                var renderer = particles.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Billboard;
                renderer.sharedMaterial = SmokeFx.Puff;
                renderer.minParticleSize = 0f;
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
                flares.BeginFrame(t);
                for (int i = 0; i < jumpers.Length; i++)
                {
                    EmitTrail(i, t);
                    Draw(i, jumpers[i], t);
                }
                flares.EndFrame();
            }

            private void OnDestroy() => flares?.Clear();

            private static Vector3 Local(Vector3 global) => new GlobalPosition(global.x, global.y, global.z).ToLocalPosition();

            private void Draw(int index, Jumper j, float t)
            {
                HaloSample s = plan.Sample(index, t);
                bool visible = s.Phase != HaloPhase.Aboard && s.Phase != HaloPhase.Gone;
                if (j.Body.gameObject.activeSelf != visible) j.Body.gameObject.SetActive(visible);
                if (!visible) return;

                if (!smokePopped && s.Phase == HaloPhase.Canopy)
                {
                    smokePopped = true;
                    LandingSmoke.Pop(plan.StickLanding, Mathf.Clamp(plan.TotalSeconds - t + 5f, 12f, 60f));
                }
                if (s.Phase == HaloPhase.Landed && j.LastPhase != HaloPhase.Landed) LandingDust(Local(s.Position));
                j.LastPhase = s.Phase;

                j.Body.position = Local(s.Position);
                // Pitch tips the head forward into the belly-down glide; toggles bank the canopy turn.
                j.Body.rotation = Quaternion.LookRotation(s.Heading, Vector3.up) * Quaternion.Euler(s.Pitch, 0f, -s.Steer * 14f);

                Pose(s);
                if (j.BodyMesh != null)
                    for (int k = 0; k < weights.Length; k++)
                        if (j.Shapes[k] >= 0) j.BodyMesh.SetBlendShapeWeight(j.Shapes[k], weights[k] * 100f);

                if (s.Smoke && j.BodyMesh != null)
                {
                    Vector3 velocity = (s.Position - plan.Sample(index, t - 0.05f).Position) / 0.05f;
                    flares.Draw(j.BodyMesh.transform.TransformPoint(HaloFlareVisuals.TrackLeftNozzle),
                        j.BodyMesh.transform.TransformPoint(HaloFlareVisuals.TrackRightNozzle),
                        velocity, t + index * 0.193f, burning: true);
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
            /// Puffs off each jumper's ankle canisters during the glide. Each puff is placed from the plan at
            /// the time it was made, so the trail is exact at any frame rate and costs one sample per puff.
            /// </summary>
            private void EmitTrail(int index, float t)
            {
                if (t - nextPuffAt[index] > MaximumCatchUp) nextPuffAt[index] = t - MaximumCatchUp;
                while (nextPuffAt[index] <= t)
                {
                    float at = nextPuffAt[index];
                    nextPuffAt[index] += PuffInterval;
                    HaloSample past = plan.Sample(index, at);
                    if (!past.Smoke) continue;
                    // The canisters ride the ankles, a body length behind the pelvis in the glide.
                    Vector3 ankle = Local(past.Position - past.Heading * 0.9f);
                    trail.Emit(new ParticleSystem.EmitParams { position = trail.transform.InverseTransformPoint(ankle) }, 1);
                }
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
