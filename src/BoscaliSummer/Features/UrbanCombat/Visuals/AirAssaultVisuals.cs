using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Chimera ramp paratrooper drop and Ibis fast-rope visuals.
    /// </summary>
    internal static class AirAssaultVisuals
    {
        private static Mesh cachedCanopyMesh;
        private static Mesh cachedLinesMesh;
        private static Material cachedParachuteMat;
        private const int MaximumActiveOperations = 8;
        private static readonly List<GameObject> ActiveOperations = new List<GameObject>(MaximumActiveOperations);

        public static void ResetForScene()
        {
            for (int i = 0; i < ActiveOperations.Count; i++)
                if (ActiveOperations[i] != null) UnityEngine.Object.Destroy(ActiveOperations[i]);
            ActiveOperations.Clear();

            cachedCanopyMesh = null;
            cachedLinesMesh = null;
            cachedParachuteMat = null;
        }

        private static bool Track(GameObject operation)
        {
            for (int i = ActiveOperations.Count - 1; i >= 0; i--)
                if (ActiveOperations[i] == null) ActiveOperations.RemoveAt(i);
            if (ActiveOperations.Count >= MaximumActiveOperations)
            {
                UnityEngine.Object.Destroy(operation);
                return false;
            }
            ActiveOperations.Add(operation);
            return true;
        }

        public static void SpawnParatrooperCargoDrop(
            Aircraft aircraft,
            Vector3 rampPos,
            Vector3 exitVelocity,
            FactionHQ owner,
            Airbase airbase,
            int troopCount)
        {
            var dropGo = new GameObject("BoscaliSummer.ParatrooperCargoDrop");
            dropGo.transform.position = rampPos;
            if (!Track(dropGo)) return;

            ParatrooperCargoDropOperation op = dropGo.AddComponent<ParatrooperCargoDropOperation>();
            op.Initialize(aircraft, rampPos, exitVelocity, owner, airbase, troopCount);
        }

        public static void SpawnFastRopeRappelling(
            Aircraft aircraft,
            Vector3 landingPos,
            FactionHQ owner,
            int soldierCount,
            Action onLanded)
        {
            var opGo = new GameObject("BoscaliSummer.FastRopeRappelling");
            opGo.transform.position = aircraft != null ? aircraft.transform.position : landingPos;
            if (!Track(opGo)) return;

            FastRopeRappellingOperation op = opGo.AddComponent<FastRopeRappellingOperation>();
            op.Initialize(aircraft, landingPos, owner, soldierCount, onLanded);
        }

        public static Mesh GetParachuteCanopyMesh()
        {
            if (cachedCanopyMesh != null) return cachedCanopyMesh;
            Mesh[] all = Resources.FindObjectsOfTypeAll<Mesh>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name.IndexOf("canopy", StringComparison.OrdinalIgnoreCase) >= 0)
                    return cachedCanopyMesh = all[i];
            }
            return cachedCanopyMesh = CreateFallbackCanopyMesh();
        }

        public static Mesh GetParachuteLinesMesh()
        {
            if (cachedLinesMesh != null) return cachedLinesMesh;
            Mesh[] all = Resources.FindObjectsOfTypeAll<Mesh>();
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name.IndexOf("lines", StringComparison.OrdinalIgnoreCase) >= 0)
                    return cachedLinesMesh = all[i];
            }
            return null;
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
            return cachedParachuteMat = MaterialProvider.GetSandbagMaterial() ?? MaterialProvider.GetConcreteMaterial();
        }

        private sealed class ParatrooperCargoDropOperation : MonoBehaviour
        {
            private FactionHQ owner;
            private Airbase airbase;
            private Vector3 velocity;
            private int troopCount;
            private GameObject soldier;
            private GameObject canopyObj;
            private GameObject linesObj;
            private bool chuteOpened;
            private bool landed;

            public void Initialize(
                Aircraft aircraft,
                Vector3 exitPos,
                Vector3 initialVel,
                FactionHQ faction,
                Airbase baseObj,
                int count)
            {
                owner = faction;
                airbase = baseObj;
                velocity = initialVel;
                troopCount = Mathf.Max(1, count);
                transform.position = exitPos;

                // 1. Spawn authentic vanilla soldier model
                soldier = VanillaSoldierFactory.CreateVisualSoldier(exitPos, Quaternion.LookRotation(initialVel), transform);
                if (soldier != null) soldier.transform.localPosition = Vector3.zero;

                // 2. Prepare parachute canopy (same as ejected pilot)
                canopyObj = new GameObject("ParachuteCanopy");
                canopyObj.transform.SetParent(transform, false);
                canopyObj.transform.localPosition = new Vector3(0f, 3.4f, 0f);

                MeshFilter cmf = canopyObj.AddComponent<MeshFilter>();
                MeshRenderer cmr = canopyObj.AddComponent<MeshRenderer>();
                cmf.sharedMesh = GetParachuteCanopyMesh();
                cmr.sharedMaterial = GetParachuteMaterial();
                canopyObj.SetActive(false);

                // 3. Prepare parachute lines
                Mesh linesMesh = GetParachuteLinesMesh();
                if (linesMesh != null)
                {
                    linesObj = new GameObject("ParachuteLines");
                    linesObj.transform.SetParent(transform, false);
                    linesObj.transform.localPosition = new Vector3(0f, 1.7f, 0f);
                    MeshFilter lmf = linesObj.AddComponent<MeshFilter>();
                    MeshRenderer lmr = linesObj.AddComponent<MeshRenderer>();
                    lmf.sharedMesh = linesMesh;
                    lmr.sharedMaterial = GetParachuteMaterial();
                    linesObj.SetActive(false);
                }

                StartCoroutine(FlightRoutine());
            }

            private IEnumerator FlightRoutine()
            {
                float timeInAir = 0f;
                float gravity = 9.81f;

                while (!landed)
                {
                    float dt = Time.deltaTime;
                    timeInAir += dt;

                    // Phase 1: Freefall separation from cargo hold (0.6s)
                    if (timeInAir < 0.6f)
                    {
                        velocity.y -= gravity * dt;
                        velocity.x = Mathf.Lerp(velocity.x, 0f, dt * 0.4f);
                        velocity.z = Mathf.Lerp(velocity.z, 0f, dt * 0.4f);
                    }
                    else
                    {
                        // Phase 2: Parachute opens!
                        if (!chuteOpened)
                        {
                            chuteOpened = true;
                            if (canopyObj != null) canopyObj.SetActive(true);
                            if (linesObj != null) linesObj.SetActive(true);
                            Plugin.Logger.LogInfo("[Paratroopers] Static-line parachute deployed behind aircraft.");
                        }

                        // Aerodynamic parachute deceleration
                        // Decelerate forward airspeed rapidly to ~2-4 m/s drift
                        velocity.x = Mathf.Lerp(velocity.x, 0f, dt * 1.8f);
                        velocity.z = Mathf.Lerp(velocity.z, 0f, dt * 1.8f);

                        // Settle vertical speed to stable ~6.5 m/s descent
                        velocity.y = Mathf.MoveTowards(velocity.y, -6.8f, dt * 12f);

                        // Gentle wind sway
                        float sway = Mathf.Sin(timeInAir * 2.2f) * 0.35f;
                        transform.rotation = Quaternion.Euler(sway * 5f, 0f, sway * 3f);
                    }

                    Vector3 nextPos = transform.position + velocity * dt;

                    // Check surface collision (terrain or building)
                    if (Physics.Raycast(transform.position, velocity.normalized, out RaycastHit hit, velocity.magnitude * dt + 0.6f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                    {
                        landed = true;
                        transform.position = hit.point;
                        OnTouchdown(hit);
                        break;
                    }

                    // Fallback sea level check
                    if (nextPos.y <= Datum.LocalSeaY + 0.5f)
                    {
                        landed = true;
                        Plugin.Logger.LogInfo("[Paratroopers] Paratroopers touched down in water.");
                        break;
                    }

                    transform.position = nextPos;
                    yield return null;
                }

                yield return new WaitForSeconds(3f);
                Destroy(gameObject);
            }

            private void OnTouchdown(RaycastHit hit)
            {
                // Touchdown dust effect
                if (GameAssets.i != null && GameAssets.i.contactDust != null)
                {
                    GameObject dust = Instantiate(GameAssets.i.contactDust, hit.point + Vector3.up * 0.2f, Quaternion.identity);
                    dust.SetActive(true);
                    Destroy(dust, 3.5f);
                }

                // Collapse parachute
                if (canopyObj != null) canopyObj.SetActive(false);
                if (linesObj != null) linesObj.SetActive(false);

                // Check if hit a building
                GameObject shell = ResolveCivilianBuilding(hit.collider);
                if (shell != null)
                {
                    Plugin.Logger.LogInfo($"[AIR ASSAULT] Paratrooper squad ({troopCount} troops) secured and fortified building: {shell.name}!");
                    ZoneGarrisonManager.Instance?.TryOccupyBuilding(shell, owner, airbase);
                }
                else
                {
                    Plugin.Logger.LogInfo($"[AIR ASSAULT] Paratroopers ({troopCount} troops) established combat encampment at ({hit.point.x:0}, {hit.point.z:0})!");
                    ZoneGarrisonManager.Instance?.TryDeployEncampment(hit.point, owner, airbase, troopCount);
                }
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
            private Action callback;
            private int requestedCount;

            private LineRenderer ropeLeft;
            private LineRenderer ropeRight;
            private AudioSource audioSource;

            private Vector3 doorLeft;
            private Vector3 doorRight;
            private Vector3 groundLeft;
            private Vector3 groundRight;

            private readonly List<RappellingSoldier> soldiers = new List<RappellingSoldier>();
            private int landedCount;
            private bool callbackFired;
            private float elapsed;

            private sealed class RappellingSoldier
            {
                public GameObject Root;
                public int Rope;
                public float StartDelay;
                public float Progress;
                public bool Landed;
                public Vector3 FanDir;
            }

            public void Initialize(Aircraft currentAircraft, Vector3 targetPos, FactionHQ owner, int soldierCount, Action onLanded)
            {
                aircraft = currentAircraft;
                helo = aircraft != null ? aircraft.transform : transform;
                target = targetPos;
                callback = onLanded;
                requestedCount = Mathf.Max(1, soldierCount);

                transform.SetParent(helo, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;

                SetupAudio();

                Material ropeMat = MaterialProvider.GetCargoHookRopeMaterial() ?? MaterialProvider.GetConcreteMaterial();
                ropeLeft = CreateRopeLine("Rope_Left", ropeMat);
                ropeRight = CreateRopeLine("Rope_Right", ropeMat);

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
                Vector3 rearCenter = ComputeRearExit();
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

                    // Break safety check if helo moves out of range
                    if (Vector3.Distance(helo.position, target) > 65f)
                    {
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
                                Vector3 nextPos = EvaluateRopeCurve(s.Rope, Mathf.Min(1f, s.Progress + 0.04f), 1f, elapsed);
                                Vector3 descentDir = (nextPos - pos).normalized;
                                Vector3 faceDir = Vector3.ProjectOnPlane(descentDir, Vector3.up);
                                if (faceDir.sqrMagnitude < 0.001f) faceDir = -helo.forward;

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

                            float spread = UnityEngine.Random.Range(2.4f, 4.2f);
                            Vector3 perimeterPos = touchGround + s.FanDir * spread;
                            if (Physics.Raycast(perimeterPos + Vector3.up * 4f, Vector3.down, out RaycastHit pHit, 10f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                                perimeterPos = pHit.point;

                            s.Root.transform.position = perimeterPos;
                            s.Root.transform.rotation = Quaternion.LookRotation(s.FanDir, Vector3.up);

                            // Soldiers dismount and cleanly despawn into the established encampment/building
                            Destroy(s.Root, 2.5f);
                        }
                    }

                    if (landedCount > 0 && !callbackFired)
                    {
                        callbackFired = true;
                        callback?.Invoke();
                    }

                    yield return null;
                }

                if (!callbackFired)
                {
                    callbackFired = true;
                    callback?.Invoke();
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

        private static GameObject ResolveCivilianBuilding(Collider col)
        {
            if (col == null) return null;
            MapBuilding mb = col.GetComponentInParent<MapBuilding>();
            if (mb != null) return mb.gameObject;

            Building b = col.GetComponentInParent<Building>();
            if (b != null && b.definition is BuildingDefinition bDef && bDef.buildingType == BuildingType.CIV)
                return b.gameObject;

            return null;
        }

        private static Mesh CreateFallbackCanopyMesh()
        {
            Mesh mesh = new Mesh();
            mesh.name = "FallbackCanopy";

            int segments = 16;
            var verts = new List<Vector3>();
            var tris = new List<int>();

            verts.Add(new Vector3(0f, 1.4f, 0f));
            for (int i = 0; i < segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a) * 2.8f, 0f, Mathf.Sin(a) * 2.8f));
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                tris.Add(0);
                tris.Add(i + 1);
                tris.Add(next + 1);
            }

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            return mesh;
        }
    }
}
