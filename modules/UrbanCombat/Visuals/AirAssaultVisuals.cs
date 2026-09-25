using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Chimera ramp paratrooper drop and Ibis fast-rope visuals. Client-local and cosmetic:
    /// the server resolves the drop's outcome in AirAssaultController on its own clock.
    /// </summary>
    internal static class AirAssaultVisuals
    {
        private static Mesh cachedParachuteMesh;
        private static Material cachedParachuteMat;
        private static Material cachedChuteFabricMat;
        private static Material cachedChuteLineMat;

        private const int MaximumActiveOperations = 8;
        private static readonly List<GameObject> ActiveOperations = new List<GameObject>(MaximumActiveOperations);

        private static bool HasActiveRappel(Aircraft aircraft)
        {
            foreach (GameObject operation in ActiveOperations)
                if (operation != null && operation.GetComponent<FastRopeRappellingOperation>() is FastRopeRappellingOperation rappel &&
                    rappel.transform.IsChildOf(aircraft.transform)) return true;
            return false;
        }

        public static void ResetForScene()
        {
            for (int i = 0; i < ActiveOperations.Count; i++)
                if (ActiveOperations[i] != null) UnityEngine.Object.Destroy(ActiveOperations[i]);
            ActiveOperations.Clear();

            if (cachedParachuteMesh != null) UnityEngine.Object.Destroy(cachedParachuteMesh);
            cachedParachuteMesh = null;
            cachedParachuteMat = null;
            if (cachedChuteFabricMat != null) UnityEngine.Object.Destroy(cachedChuteFabricMat);
            cachedChuteFabricMat = null;
            cachedChuteLineMat = null;
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

        public static void SpawnParatrooperCargoDrop(
            Aircraft aircraft,
            Vector3 rampPos,
            Vector3 exitVelocity,
            int troopCount)
        {
            var dropGo = new GameObject("BoscaliSummer.ParatrooperCargoDrop");
            dropGo.transform.position = rampPos;
            if (!Track(dropGo)) return;

            ParatrooperCargoDropOperation op = dropGo.AddComponent<ParatrooperCargoDropOperation>();
            op.Initialize(aircraft, rampPos, exitVelocity, troopCount);
        }

        public static void SpawnFastRopeRappelling(Aircraft aircraft, Vector3 landingPos, int soldierCount)
        {
            if (aircraft == null || HasActiveRappel(aircraft)) return;
            var opGo = new GameObject("BoscaliSummer.FastRopeRappelling");
            opGo.transform.position = aircraft.transform.position;
            if (!Track(opGo)) return;

            FastRopeRappellingOperation op = opGo.AddComponent<FastRopeRappellingOperation>();
            op.Initialize(aircraft, landingPos, soldierCount);
        }

        public static Mesh GetParachuteMesh()
        {
            if (cachedParachuteMesh == null)
                cachedParachuteMesh = ParachuteMeshBuilder.Build();
            return cachedParachuteMesh;
        }

        public static GameObject CreateParachuteRig()
        {
            var rig = new GameObject("BoscaliSummer.ParachuteRig");
            MeshFilter filter = rig.AddComponent<MeshFilter>();
            filter.sharedMesh = GetParachuteMesh();

            MeshRenderer renderer = rig.AddComponent<MeshRenderer>();
            Material fabric = GetChuteFabricMaterial();
            if (fabric == null) fabric = MaterialProvider.GetConcreteMaterial();
            Material lines = GetChuteLineMaterial();
            if (lines == null) lines = fabric;
            renderer.sharedMaterials = new[] { fabric, lines };
            return rig;
        }

        public static Material GetParachuteMaterial()
        {
            if (cachedParachuteMat != null) return cachedParachuteMat;
            Material[] mats = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && mats[i].name.IndexOf("parachute", StringComparison.OrdinalIgnoreCase) >= 0)
                    return cachedParachuteMat = mats[i];
            }
            // GetSandbagMaterial already falls back to concrete.
            return cachedParachuteMat = MaterialProvider.GetSandbagMaterial();
        }

        public static Material GetChuteFabricMaterial()
        {
            if (cachedChuteFabricMat != null) return cachedChuteFabricMat;
            Material source = GetParachuteMaterial();
            if (source == null) return null;

            cachedChuteFabricMat = new Material(source) { name = "BoscaliSummer.ChuteFabric" };
            if (cachedChuteFabricMat.HasProperty("_wrinkleStrength"))
                cachedChuteFabricMat.SetFloat("_wrinkleStrength", 0.18f);
            if (cachedChuteFabricMat.HasProperty("_wrinkleDisplacment"))
                cachedChuteFabricMat.SetFloat("_wrinkleDisplacment", 0f);
            return cachedChuteFabricMat;
        }

        public static Material GetChuteLineMaterial()
        {
            if (cachedChuteLineMat == null)
                cachedChuteLineMat = MaterialProvider.GetCargoHookRopeMaterial();
            return cachedChuteLineMat;
        }

        private sealed class ParatrooperCargoDropOperation : MonoBehaviour
        {
            private const float ExitInterval = 1.15f;
            private const float ExitRearSpeed = 7f;
            private const float FallGravity = 9.81f;
            private const float ChuteOpenDelayMin = 0.7f;
            private const float ChuteDeploySeconds = 0.5f;
            private const float LandingHoldSeconds = 3.2f;
            private const float SeaMargin = 0.5f;
            private const int MaxParatroopers = 16;

            private static RuntimeAnimatorController chuteParameterSource;
            private static readonly List<int> ChuteParameters = new List<int>(4);

            private Aircraft aircraft;
            private Vector3 exitAnchorLocal;
            private Vector3 aircraftForward = Vector3.forward;
            private int troopCount;
            private float elapsed;
            private float operationTime;

            private readonly List<ParatrooperDrop> droppers = new List<ParatrooperDrop>(MaxParatroopers);

            private sealed class ParatrooperDrop
            {
                public GameObject Soldier;
                public GameObject Rig;
                public Vector3 Velocity;
                public Vector3 ChuteOffset;
                public Vector3 LandingPosition;
                public Vector3 DriftDir;
                public float DriftSpeed;
                public float TerminalSpeed;
                public float ExitAt;
                public float ChuteDelay;
                public float ChuteOpen;
                public float ChuteCollapse = 1f;
                public float ChuteSpin;
                public float CleanupDelay;
                public int Seed;
                public int Slot;
                public bool Exited;
                public bool ChuteDeployed;
                public bool Landed;
                public int Index;
                public Animator Animator;
            }

            public void Initialize(Aircraft aircraftRef, Vector3 exitPos, Vector3 initialVel, int count)
            {
                aircraft = aircraftRef;
                troopCount = Mathf.Max(1, Mathf.Min(MaxParatroopers, count));
                transform.position = exitPos;

                aircraftForward = aircraft != null ? aircraft.transform.forward : Vector3.forward;
                exitAnchorLocal = aircraft != null ? aircraft.transform.InverseTransformPoint(exitPos) : exitPos;

                // Height above the sea bounds the height above any landing spot.
                operationTime = TroopDeploymentMath.ParadropOperationSeconds(
                    exitPos.y - Datum.LocalSeaY, TroopDeploymentMath.ParachuteDescentRate);

                BuildDroppers(initialVel);
                StartCoroutine(FlightRoutine());
            }

            private void BuildDroppers(Vector3 initialVel)
            {
                droppers.Clear();

                for (int i = 0; i < troopCount; i++)
                {
                    ParatrooperDrop drop = new ParatrooperDrop
                    {
                        Seed = i * 37 + 11,
                        Index = i,
                        Slot = i % 2,
                        ExitAt = i * ExitInterval,
                        ChuteDelay = ChuteOpenDelayMin + (i % 3) * 0.14f + UnityEngine.Random.Range(0f, 0.2f),
                        Velocity = initialVel,
                        DriftSpeed = 1f + UnityEngine.Random.Range(0f, 0.5f),
                        // Earlier jumpers hang longer; later ones sink faster, stretching the trail.
                        TerminalSpeed = -(TroopDeploymentMath.ParachuteDescentRate + i * 0.1f + UnityEngine.Random.Range(0f, 0.12f))
                    };

                    drop.Soldier = VanillaSoldierFactory.CreateVisualSoldier(transform.position, Quaternion.identity, transform);
                    if (drop.Soldier == null)
                    {
                        drop.Soldier = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                        UnityEngine.Object.Destroy(drop.Soldier.GetComponent<Collider>());
                        drop.Soldier.transform.SetParent(transform, false);
                    }
                    drop.Soldier.name = $"Paratrooper_{i + 1}";
                    drop.Animator = drop.Soldier.GetComponentInChildren<Animator>();
                    drop.Soldier.SetActive(false);

                    // The whole chute rig hangs from the operation, never the swinging body.
                    drop.Rig = CreateParachuteRig();
                    drop.Rig.transform.SetParent(transform, false);
                    drop.Rig.SetActive(false);

                    droppers.Add(drop);
                }
            }

            private IEnumerator FlightRoutine()
            {
                while (elapsed < operationTime)
                {
                    float dt = Time.deltaTime;
                    if (dt <= 0f)
                    {
                        yield return null;
                        continue;
                    }

                    elapsed += dt;
                    bool anyActive = false;

                    for (int i = 0; i < droppers.Count; i++)
                    {
                        if (droppers[i].Soldier == null)
                            continue;

                        if (AdvanceDrop(droppers[i], dt))
                            anyActive = true;
                    }

                    if (!anyActive)
                        break;

                    yield return null;
                }

                CleanupAll();
                yield return new WaitForSeconds(1f);
                Destroy(gameObject);
            }

            private bool AdvanceDrop(ParatrooperDrop drop, float dt)
            {
                if (drop.Soldier == null)
                    return false;

                if (drop.Landed)
                {
                    // The canopy deflates over the hold, then the rig fades out.
                    if (drop.ChuteCollapse > 0f)
                    {
                        drop.ChuteCollapse = Mathf.Max(0f, drop.ChuteCollapse - dt / 0.3f);
                        UpdateChuteRig(drop, drop.Soldier.transform.position, dt);
                    }

                    drop.CleanupDelay -= dt;
                    if (drop.CleanupDelay > 0f)
                        return true;

                    if (drop.Rig != null) { Destroy(drop.Rig); drop.Rig = null; }
                    Destroy(drop.Soldier);
                    drop.Soldier = null;
                    return false;
                }

                if (!drop.Exited)
                {
                    if (elapsed < drop.ExitAt)
                        return true;

                    ExitDrop(drop);
                }

                float fallTime = elapsed - drop.ExitAt;
                if (!drop.ChuteDeployed)
                {
                    drop.Velocity += Vector3.down * FallGravity * dt;

                    // Slipstream bleeds off the aircraft's speed across the line stretch;
                    // a little early drift keeps the stick from stacking on itself.
                    Vector3 slipstream = (aircraft != null && aircraft.rb != null) ? aircraft.rb.velocity * 0.18f : Vector3.zero;
                    slipstream += drop.DriftDir * (drop.DriftSpeed * 0.45f);
                    drop.Velocity.x = Mathf.MoveTowards(drop.Velocity.x, slipstream.x, 22f * dt);
                    drop.Velocity.z = Mathf.MoveTowards(drop.Velocity.z, slipstream.z, 22f * dt);

                    if (fallTime >= drop.ChuteDelay)
                        DeployChute(drop);
                }

                if (drop.ChuteDeployed)
                {
                    drop.ChuteOpen = Mathf.Min(1f, drop.ChuteOpen + dt / ChuteDeploySeconds);

                    // Mission wind plus a per-jumper drift fans the stick out naturally.
                    float driftBlend = Mathf.Clamp01(fallTime / 1.8f);
                    Vector3 wind = GetWindVector();
                    Vector3 target = new Vector3(
                        wind.x * 0.75f + drop.DriftDir.x * drop.DriftSpeed * driftBlend,
                        drop.TerminalSpeed,
                        wind.z * 0.75f + drop.DriftDir.z * drop.DriftSpeed * driftBlend);
                    drop.Velocity = Vector3.MoveTowards(drop.Velocity, target, (drop.ChuteOpen < 1f ? 30f : 6f) * dt);
                    ApplyCanopySway(drop, dt);
                }

                IntegrateSoldier(drop, dt);
                CheckLanding(drop);
                return true;
            }

            private void ExitDrop(ParatrooperDrop drop)
            {
                Vector3 forward = aircraftForward;
                Vector3 right = Vector3.right;
                Vector3 exitPos = transform.position;
                Vector3 inheritedVelocity = drop.Velocity;

                if (aircraft != null)
                {
                    forward = aircraft.transform.forward;
                    right = aircraft.transform.right;
                    exitPos = aircraft.transform.TransformPoint(exitAnchorLocal);
                    if (aircraft.rb != null)
                        inheritedVelocity = aircraft.rb.velocity;
                }

                exitPos += right * (drop.Slot == 0 ? -0.55f : 0.55f);
                drop.Velocity = inheritedVelocity - forward * ExitRearSpeed + Vector3.up * 0.35f
                    + right * (drop.Slot == 0 ? -0.55f : 0.55f);

                // Ordered fan behind the flight path: a visible jumped-one-after-another trail.
                Vector3 trail = Vector3.ProjectOnPlane(-forward, Vector3.up);
                if (trail.sqrMagnitude < 0.01f) trail = right;
                trail.Normalize();
                Vector3 fanRight = Vector3.Cross(Vector3.up, trail).normalized;
                float side = drop.Slot == 0 ? -1f : 1f;
                float fanAngle = side * (20f + drop.Index * 4f) * Mathf.Deg2Rad;
                drop.DriftDir = (trail * Mathf.Cos(fanAngle) + fanRight * Mathf.Sin(fanAngle)).normalized;

                drop.Exited = true;
                drop.ChuteOffset = Vector3.up * ParachuteMeshBuilder.RimHeight;
                drop.Soldier.SetActive(true);
                drop.Soldier.transform.SetPositionAndRotation(exitPos, Quaternion.LookRotation(-forward, Vector3.up));
                if (drop.Animator != null)
                    SetPilotAnimation(drop.Animator, PilotDismounted.PilotState.parachuting);
            }

            private static void DeployChute(ParatrooperDrop drop)
            {
                drop.ChuteDeployed = true;
                drop.ChuteOpen = 0.01f;
                drop.ChuteCollapse = 1f;
                if (drop.Velocity.y < -12f)
                    drop.Velocity.y = -12f;

                // Static line snaps the canopy open and checks the forward speed.
                drop.Velocity.x *= 0.4f;
                drop.Velocity.z *= 0.4f;

                if (drop.Rig != null) drop.Rig.SetActive(true);
            }

            private void ApplyCanopySway(ParatrooperDrop drop, float dt)
            {
                float phase = elapsed * 1.15f + drop.Seed * 0.17f;
                Vector3 right = aircraft != null ? aircraft.transform.right : Vector3.right;
                Vector3 forward = aircraft != null ? aircraft.transform.forward : Vector3.forward;
                drop.Velocity += (right * Mathf.Sin(phase) + forward * Mathf.Cos(phase * 0.73f)) * 0.35f * dt;
            }

            private static Vector3 GetWindVector()
            {
                LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
                return level != null ? level.GetWind() : Vector3.zero;
            }

            private void IntegrateSoldier(ParatrooperDrop drop, float dt)
            {
                if (drop == null || drop.Soldier == null) return;

                Vector3 pos = drop.Soldier.transform.position + drop.Velocity * dt;
                drop.Soldier.transform.position = pos;

                Vector3 motion = drop.Velocity;
                motion.y = 0f;
                Vector3 facing = motion.sqrMagnitude > 0.04f
                    ? motion.normalized
                    : Vector3.ProjectOnPlane(-aircraftForward, Vector3.up);
                if (facing.sqrMagnitude < 0.01f) facing = Vector3.forward;
                float bank = Mathf.Sin(Time.time * 0.8f + drop.Seed) * (drop.ChuteDeployed ? 5f : 2f);
                drop.Soldier.transform.rotation = Quaternion.LookRotation(facing, Vector3.up) * Quaternion.Euler(0f, 0f, bank);

                UpdateChuteRig(drop, pos, dt);
            }

            private void UpdateChuteRig(ParatrooperDrop drop, Vector3 soldierPos, float dt)
            {
                if (drop.Rig == null) return;

                float scale = Mathf.SmoothStep(0f, 1f, drop.ChuteOpen) * drop.ChuteCollapse;
                if (scale <= 0.02f)
                {
                    if (drop.Rig.activeSelf) drop.Rig.SetActive(false);
                    return;
                }
                if (!drop.Rig.activeSelf) drop.Rig.SetActive(true);

                // The canopy floats on a short pendulum above the harness.
                Vector3 target = Vector3.up * ParachuteMeshBuilder.RimHeight
                    + new Vector3(drop.Velocity.x, 0f, drop.Velocity.z) * 0.06f
                    + new Vector3(
                        Mathf.Sin(elapsed * 1.1f + drop.Seed) * 0.4f,
                        0f,
                        Mathf.Cos(elapsed * 0.83f + drop.Seed) * 0.4f);
                float follow = 1f - Mathf.Exp(-2.6f * dt);
                drop.ChuteOffset = drop.ChuteOffset.sqrMagnitude < 0.01f
                    ? target
                    : Vector3.Lerp(drop.ChuteOffset, target, follow);

                Vector3 dir = drop.ChuteOffset.sqrMagnitude > 0.001f ? drop.ChuteOffset.normalized : Vector3.up;
                drop.ChuteSpin += dt * 2.2f;
                Quaternion yaw = Quaternion.AngleAxis(Mathf.Sin(drop.ChuteSpin + drop.Seed) * 12f, Vector3.up);

                drop.Rig.transform.position = soldierPos + Vector3.up * ParachuteMeshBuilder.HarnessHeight;
                drop.Rig.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir) * yaw;
                drop.Rig.transform.localScale = new Vector3(scale, scale, scale);
            }

            private void CheckLanding(ParatrooperDrop drop)
            {
                if (drop == null || drop.Soldier == null || drop.Landed) return;

                Vector3 pos = drop.Soldier.transform.position;
                float sweep = Mathf.Max(0.6f, drop.Velocity.magnitude * Time.deltaTime + 0.8f);
                if (Physics.Raycast(pos + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, sweep + 0.1f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                {
                    Land(drop, hit, false);
                    return;
                }

                if (pos.y <= Datum.LocalSeaY + SeaMargin)
                {
                    hit.point = new Vector3(pos.x, Datum.LocalSeaY, pos.z);
                    Land(drop, hit, true);
                }
            }

            private void Land(ParatrooperDrop drop, RaycastHit hit, bool inWater)
            {
                if (drop == null || drop.Soldier == null || drop.Landed)
                    return;

                drop.Landed = true;
                drop.CleanupDelay = LandingHoldSeconds + drop.Slot * 0.15f;
                drop.LandingPosition = inWater ? hit.point : SnapToGround(hit.point);
                drop.ChuteCollapse = 1f;

                if (!inWater && GameAssets.i != null && GameAssets.i.contactDust != null)
                {
                    GameObject dust = Instantiate(GameAssets.i.contactDust, drop.LandingPosition + Vector3.up * 0.2f, Quaternion.identity);
                    dust.SetActive(true);
                    Destroy(dust, 3.5f);
                }

                drop.Soldier.transform.position = drop.LandingPosition;
                if (drop.Animator != null)
                    SetPilotAnimation(drop.Animator, PilotDismounted.PilotState.landing);
            }

            private void CleanupAll()
            {
                for (int i = 0; i < droppers.Count; i++)
                {
                    ParatrooperDrop drop = droppers[i];
                    if (drop.Rig != null) Destroy(drop.Rig);
                    if (drop.Soldier != null) Destroy(drop.Soldier);
                }
            }

            private static Vector3 SnapToGround(Vector3 point)
            {
                if (Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 8f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                    return ground.point;
                return point;
            }

            private static void SetPilotAnimation(Animator anim, PilotDismounted.PilotState state)
            {
                if (anim == null) return;
                anim.SetInteger("PilotState", (int)state);
                // Animator.parameters allocates a fresh array; read it once per controller.
                if (anim.runtimeAnimatorController != chuteParameterSource)
                {
                    chuteParameterSource = anim.runtimeAnimatorController;
                    ChuteParameters.Clear();
                    foreach (AnimatorControllerParameter p in anim.parameters)
                        if (p.type == AnimatorControllerParameterType.Bool &&
                            p.name.IndexOf("chute", StringComparison.OrdinalIgnoreCase) >= 0)
                            ChuteParameters.Add(p.nameHash);
                }
                for (int i = 0; i < ChuteParameters.Count; i++)
                    anim.SetBool(ChuteParameters[i], true);
            }
        }



        private sealed class FastRopeRappellingOperation : MonoBehaviour
        {
            private const float RopeHalfSpread = 0.55f;
            private const float FallbackRearDistance = 5f;
            private const float FallbackDoorDrop = 0.6f;
            private const float SlideDescentSpeed = 7.2f;
            private const float DeployWinchSpeed = 24f;
            private const float RetractWinchSpeed = 22f;
            private const float OperationTimeCap = 22f;
            private const int RopePoints = 14;

            private static AudioClip cachedWinchStart;
            private static AudioClip cachedWinchStop;
            private static AudioClip cachedWinchLoop;
            private static bool audioProbed;

            private Aircraft aircraft;
            private Transform helo;
            private Vector3 target;
            private int requestedCount;

            private LineRenderer ropeLeft;
            private LineRenderer ropeRight;
            private AudioSource audioSource;

            private Vector3 rearExitLocal;
            private Vector3 doorLeft;
            private Vector3 doorRight;
            private Vector3 groundLeft;
            private Vector3 groundRight;

            private readonly List<RappellingSoldier> soldiers = new List<RappellingSoldier>();
            private int landedCount;
            private float elapsed;

            private sealed class RappellingSoldier
            {
                public GameObject Root;
                public int Rope;
                public float StartDelay;
                public float Progress;
                public bool Landed;
                public Vector3 FanDir;
                public Vector3 LandingPosition;
                public float LandedAt;
            }

            public void Initialize(Aircraft currentAircraft, Vector3 targetPos, int soldierCount)
            {
                aircraft = currentAircraft;
                helo = aircraft != null ? aircraft.transform : transform;
                target = targetPos;
                requestedCount = Mathf.Max(1, soldierCount);

                transform.SetParent(helo, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;

                SetupAudio();

                // GetCargoHookRopeMaterial already falls back to concrete.
                Material ropeMat = MaterialProvider.GetCargoHookRopeMaterial();
                ropeLeft = CreateRopeLine("Rope_Left", ropeMat);
                ropeRight = CreateRopeLine("Rope_Right", ropeMat);

                rearExitLocal = helo.InverseTransformPoint(ComputeRearExit());
                ComputeInitialAnchors();
                BuildSoldiers();

                StartCoroutine(RappellingRoutine());
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

            private LineRenderer CreateRopeLine(string name, Material mat)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                if (mat != null) lr.sharedMaterial = mat;
                lr.startWidth = 0.045f;
                lr.endWidth = 0.035f;
                lr.useWorldSpace = true;
                lr.positionCount = RopePoints;
                lr.alignment = LineAlignment.View;
                lr.textureMode = LineTextureMode.Tile;
                lr.numCapVertices = 2;
                lr.numCornerVertices = 2;
                return lr;
            }

            private GameObject CreateSoldier(string name)
            {
                GameObject soldier = VanillaSoldierFactory.CreateVisualSoldier(transform.position, Quaternion.identity, transform);
                if (soldier != null)
                {
                    soldier.name = name;
                    soldier.SetActive(false);
                    return soldier;
                }
                var fallback = new GameObject(name);
                fallback.transform.SetParent(transform, false);
                fallback.SetActive(false);
                return fallback;
            }

            private void BuildSoldiers()
            {
                int count = Mathf.Clamp(requestedCount, 1, 16);
                for (int i = 0; i < count; i++)
                {
                    int rope = i % 2;
                    int onRope = i / 2;
                    float angle = (i * 60f + 15f) * Mathf.Deg2Rad;
                    soldiers.Add(new RappellingSoldier
                    {
                        Root = CreateSoldier($"Rappeller_{i}"),
                        Rope = rope,
                        StartDelay = onRope * 0.75f + UnityEngine.Random.Range(0f, 0.18f) + rope * 0.08f,
                        Progress = 0f,
                        FanDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)).normalized
                    });
                }
            }

            private void ComputeInitialAnchors()
            {
                UpdateDoorAnchors();

                groundLeft = target - helo.right * 1.2f;
                groundRight = target + helo.right * 1.2f;

                if (Physics.Raycast(groundLeft + Vector3.up * 4f, Vector3.down, out RaycastHit hitL, 10f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                    groundLeft = hitL.point;
                if (Physics.Raycast(groundRight + Vector3.up * 4f, Vector3.down, out RaycastHit hitR, 10f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                    groundRight = hitR.point;
            }

            private void UpdateDoorAnchors()
            {
                Vector3 rearCenter = helo.TransformPoint(rearExitLocal);
                doorLeft = rearCenter - helo.right * RopeHalfSpread;
                doorRight = rearCenter + helo.right * RopeHalfSpread;
            }

            private Vector3 ComputeRearExit()
            {
                if (aircraft == null)
                    return helo.position - helo.forward * FallbackRearDistance - helo.up * FallbackDoorDrop;

                BayDoor[] doors = aircraft.GetComponentsInChildren<BayDoor>(true);
                Transform best = null;
                float bestRear = float.MaxValue;
                for (int i = 0; i < doors.Length; i++)
                {
                    if (doors[i] == null) continue;
                    Transform t = doors[i].transform;
                    float rear = Vector3.Dot(t.position - helo.position, helo.forward);
                    if (rear < bestRear) { bestRear = rear; best = t; }
                }

                if (best != null)
                    return best.position - helo.up * 0.25f;

                return helo.position - helo.forward * FallbackRearDistance - helo.up * FallbackDoorDrop;
            }

            private Vector3 EvaluateRopeCurve(int ropeIndex, float u, float lengthRatio, float time)
            {
                Vector3 door = ropeIndex == 0 ? doorLeft : doorRight;
                Vector3 ground = ropeIndex == 0 ? groundLeft : groundRight;
                Vector3 targetEnd = Vector3.Lerp(door, ground, lengthRatio);

                // Baseline straight segment
                Vector3 basePos = Vector3.Lerp(door, targetEnd, u);

                // Gravity / Catenary sag (4u(1-u) has maximum 1.0 at u = 0.5)
                float sagFactor = 4f * u * (1f - u);
                float fullDist = Mathf.Max(1f, Vector3.Distance(door, ground));
                float sagAmount = Mathf.Clamp(fullDist * 0.035f, 0.45f, 1.2f) * lengthRatio;
                Vector3 sag = Vector3.down * (sagFactor * sagAmount);

                // Aerodynamic drag swing (trails opposite to horizontal airspeed)
                Vector3 heloVel = (aircraft != null && aircraft.rb != null) ? aircraft.rb.velocity : Vector3.zero;
                Vector3 horizVel = Vector3.ProjectOnPlane(heloVel, Vector3.up);
                float dragShape = Mathf.Sin(u * Mathf.PI);
                Vector3 drag = -horizVel * 0.08f * dragShape * lengthRatio;

                // Rotor downwash deflection and lateral spread
                float side = ropeIndex == 0 ? -1f : 1f;
                Vector3 wash = (Vector3.down * 0.2f + helo.right * (0.12f * side)) * dragShape * lengthRatio;

                // Dynamic pendulum sway oscillation
                Vector3 sway = (helo.right * Mathf.Sin(time * 3.2f + ropeIndex * 1.5f) +
                                helo.forward * Mathf.Cos(time * 2.6f)) * (0.05f * dragShape * lengthRatio);

                return basePos + sag + drag + wash + sway;
            }

            private void UpdateRopeRenderer(LineRenderer lr, int ropeIndex, float lengthRatio, float time)
            {
                if (lr == null) return;
                for (int i = 0; i < RopePoints; i++)
                {
                    float u = (float)i / (RopePoints - 1);
                    lr.SetPosition(i, EvaluateRopeCurve(ropeIndex, u, lengthRatio, time));
                }
            }

            private IEnumerator RappellingRoutine()
            {
                float totalDist = Mathf.Max(1f, Vector3.Distance(helo.position, target));

                // -------------------------------------------------------------
                // Phase 1: Winch Cable Deployment Animation (ropes reel down)
                // -------------------------------------------------------------
                PlayWinchAudio(isDeploy: true);
                float deployProgress = 0f;

                while (deployProgress < 1f)
                {
                    float dt = Time.deltaTime;
                    elapsed += dt;
                    UpdateDoorAnchors();

                    deployProgress += (DeployWinchSpeed / totalDist) * dt;
                    if (deployProgress > 1f) deployProgress = 1f;

                    if (audioSource != null)
                        audioSource.pitch = 0.95f + 0.1f * deployProgress;

                    UpdateRopeRenderer(ropeLeft, 0, deployProgress, elapsed);
                    UpdateRopeRenderer(ropeRight, 1, deployProgress, elapsed);

                    // Break safety check if helo moves out of range; the squad's outcome is the server's.
                    if (Vector3.Distance(helo.position, target) > 65f)
                    {
                        Plugin.Logger.LogInfo("[IBIS] Fast-rope visual cut short: the helicopter drifted more than 65 m off the LZ.");
                        StopWinchAudio();
                        Destroy(gameObject);
                        yield break;
                    }

                    yield return null;
                }

                StopWinchAudio();
                SpawnDust(groundLeft);
                SpawnDust(groundRight);

                // -------------------------------------------------------------
                // Phase 2: Rappelling Descent along Physics Cable
                // -------------------------------------------------------------
                float rappellingStartTime = elapsed;

                while (elapsed < OperationTimeCap && landedCount < soldiers.Count)
                {
                    float dt = Time.deltaTime;
                    elapsed += dt;
                    UpdateDoorAnchors();

                    UpdateRopeRenderer(ropeLeft, 0, 1f, elapsed);
                    UpdateRopeRenderer(ropeRight, 1, 1f, elapsed);

                    float descentElapsed = elapsed - rappellingStartTime;

                    for (int i = 0; i < soldiers.Count; i++)
                    {
                        RappellingSoldier s = soldiers[i];
                        if (s.Root == null) continue;
                        if (s.Landed)
                        {
                            s.Root.transform.position = Vector3.Lerp(s.LandingPosition,
                                s.LandingPosition + s.FanDir * 3f, Mathf.Clamp01((elapsed - s.LandedAt) / 2f));
                            continue;
                        }
                        if (descentElapsed < s.StartDelay) continue;

                        if (!s.Root.activeSelf)
                        {
                            s.Root.SetActive(true);
                            Animator anim = s.Root.GetComponentInChildren<Animator>();
                            if (anim != null)
                                anim.SetInteger("PilotState", (int)PilotDismounted.PilotState.parachuting);
                        }

                        if (!s.Landed)
                        {
                            s.Progress += (SlideDescentSpeed / totalDist) * dt;
                            if (s.Progress < 1f)
                            {
                                Vector3 pos = EvaluateRopeCurve(s.Rope, s.Progress, 1f, elapsed);
                                Vector3 faceDir = -helo.forward;

                                s.Root.transform.position = pos;
                                s.Root.transform.rotation = Quaternion.LookRotation(faceDir.normalized, Vector3.up);
                                continue;
                            }

                            // Touchdown!
                            s.Landed = true;
                            landedCount++;
                            Vector3 touchGround = s.Rope == 0 ? groundLeft : groundRight;
                            SpawnDust(touchGround);

                            Animator landedAnim = s.Root.GetComponentInChildren<Animator>();
                            if (landedAnim != null)
                                landedAnim.SetInteger("PilotState", (int)PilotDismounted.PilotState.landing);

                            s.Root.transform.SetParent(Datum.origin, true);
                            s.LandingPosition = touchGround;
                            s.LandedAt = elapsed;
                            s.Root.transform.position = touchGround;
                            s.Root.transform.rotation = Quaternion.LookRotation(s.FanDir, Vector3.up);

                            // Soldiers dismount and cleanly despawn into the established encampment/building
                            Destroy(s.Root, 2.5f);
                        }
                    }

                    yield return null;
                }

                // -------------------------------------------------------------
                // Phase 3: Winch Retraction Animation (ropes reel back into door)
                // -------------------------------------------------------------
                yield return new WaitForSeconds(0.4f);
                PlayWinchAudio(isDeploy: false);
                float retractProgress = 0f;

                while (retractProgress < 1f)
                {
                    float dt = Time.deltaTime;
                    elapsed += dt;
                    UpdateDoorAnchors();

                    retractProgress += (RetractWinchSpeed / totalDist) * dt;
                    if (retractProgress > 1f) retractProgress = 1f;

                    if (audioSource != null)
                        audioSource.pitch = 1.0f - 0.1f * retractProgress;

                    float remainingRatio = 1f - retractProgress;
                    UpdateRopeRenderer(ropeLeft, 0, remainingRatio, elapsed);
                    UpdateRopeRenderer(ropeRight, 1, remainingRatio, elapsed);

                    yield return null;
                }

                StopWinchAudio();
                if (ropeLeft != null) ropeLeft.enabled = false;
                if (ropeRight != null) ropeRight.enabled = false;

                // Ensure all soldier GameObjects are cleaned up
                for (int i = 0; i < soldiers.Count; i++)
                {
                    if (soldiers[i]?.Root != null)
                        Destroy(soldiers[i].Root);
                }

                Destroy(gameObject, 0.5f);
            }

            private void OnDestroy()
            {
                StopWinchAudio();
                for (int i = 0; i < soldiers.Count; i++)
                {
                    if (soldiers[i]?.Root != null)
                        Destroy(soldiers[i].Root);
                }
            }

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
