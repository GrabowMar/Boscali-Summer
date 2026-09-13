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

        public static bool HasOperationCapacity
        {
            get
            {
                ActiveOperations.RemoveAll(operation => operation == null);
                return ActiveOperations.Count < MaximumActiveOperations;
            }
        }

        public static bool HasActiveRappel(Aircraft aircraft)
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
            private const float ChuteOpenDelayMin = 0.42f;
            private const float ChuteOpenDelayMax = 0.94f;
            private const float FallGravity = 9.6f;
            private const float ChuteTerminalSpeed = -6.9f;
            private const float MaxOperationTime = 27f;
            private const float LandingHoldSeconds = 2.8f;
            private const float SeaMargin = 0.5f;
            private const int MaxParatroopers = 16;

            private FactionHQ owner;
            private Airbase airbase;
            private Vector3 exitVelocity;
            private int troopCount;
            private bool insertionResolved;
            private float elapsed;
            private Vector3 aircraftRight = Vector3.right;
            private Vector3 aircraftForward = Vector3.forward;
            private Vector3 aircraftUp = Vector3.up;

            private readonly List<ParatrooperDrop> droppers = new List<ParatrooperDrop>(MaxParatroopers);

            private sealed class ParatrooperDrop
            {
                public GameObject Soldier;
                public GameObject Canopy;
                public GameObject Lines;
                public Vector3 Velocity;
                public Vector3 SpawnOffset;
                public int Seed;
                public float SpawnDelay;
                public float ChuteDelay;
                public float InFlightTime;
                public float CleanupDelay;
                public bool Activated;
                public bool ChuteOpen;
                public bool Landed;
                public Animator Animator;
            }

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
                exitVelocity = initialVel;
                troopCount = Mathf.Max(1, Mathf.Min(MaxParatroopers, count));
                transform.position = exitPos;
                BuildDroppers(aircraft);
                StartCoroutine(FlightRoutine(aircraft));
            }

            private void BuildDroppers(Aircraft aircraft)
            {
                droppers.Clear();

                Mesh canopyMesh = GetParachuteCanopyMesh();
                Mesh linesMesh = GetParachuteLinesMesh();
                Material parachuteMat = GetParachuteMaterial();
                aircraftRight = Vector3.right;
                aircraftForward = Vector3.forward;
                aircraftUp = Vector3.up;

                if (aircraft != null)
                {
                    Transform mount = aircraft.transform;
                    aircraftRight = mount.right;
                    aircraftForward = mount.forward;
                    aircraftUp = mount.up;
                }

                for (int i = 0; i < troopCount; i++)
                {
                    Vector3 spawnOffset = Vector3.zero;
                    if (troopCount <= 1)
                    {
                        spawnOffset = Vector3.zero;
                    }
                    else if (troopCount <= 4)
                    {
                        float angle = (i / (float)troopCount) * Mathf.PI * 2f;
                        spawnOffset = (aircraftRight * Mathf.Cos(angle) + aircraftForward * Mathf.Sin(angle)) * 0.6f;
                    }
                    else
                    {
                        float side = (i % 2 == 0 ? -1f : 1f) * 0.42f;
                        float rear = Mathf.Floor(i / 2f) * 0.24f;
                        float upOffset = ((i % 3) - 1) * 0.08f;
                        spawnOffset = (aircraftRight * side) + (-aircraftForward * rear) + (aircraftUp * upOffset);
                    }

                    Vector3 initialVelocity = exitVelocity
                        + aircraftRight * ((i % 2 == 0 ? -1f : 1f) * 0.4f)
                        - aircraftForward * 0.5f;
                    initialVelocity.y -= 0.6f;

                    ParatrooperDrop drop = new ParatrooperDrop
                    {
                        Seed = i * 17,
                        SpawnOffset = spawnOffset,
                        SpawnDelay = i * 0.12f + UnityEngine.Random.Range(0f, 0.08f),
                        ChuteDelay = UnityEngine.Random.Range(ChuteOpenDelayMin, ChuteOpenDelayMax),
                        Velocity = initialVelocity
                    };

                    Vector3 spawnPos = transform.position + spawnOffset;
                    Quaternion facing = Quaternion.LookRotation(
                        (exitVelocity == Vector3.zero) ? -aircraftForward : exitVelocity.normalized,
                        aircraftUp);
                    drop.Soldier = VanillaSoldierFactory.CreateVisualSoldier(spawnPos, facing, transform);

                    if (drop.Soldier == null)
                    {
                        drop.Soldier = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                        drop.Soldier.name = "BoscaliSummer.TrooperFallback";
                        drop.Soldier.transform.SetParent(transform, false);
                        drop.Soldier.transform.position = spawnPos;
                    }
                    drop.Animator = drop.Soldier.GetComponentInChildren<Animator>();

                    if (canopyMesh != null)
                    {
                        drop.Canopy = CreateParachuteMesh(canopyMesh, parachuteMat, "ParachuteCanopy");
                        drop.Canopy.transform.SetParent(drop.Soldier.transform, false);
                        drop.Canopy.transform.localPosition = aircraftUp * 2.9f + aircraftForward * 0.18f;
                        drop.Canopy.SetActive(false);
                    }

                    if (linesMesh != null)
                    {
                        drop.Lines = CreateParachuteMesh(linesMesh, parachuteMat, "ParachuteLines");
                        drop.Lines.transform.SetParent(drop.Soldier.transform, false);
                        drop.Lines.transform.localPosition = aircraftUp * 1.5f + aircraftForward * 0.05f;
                        drop.Lines.SetActive(false);
                    }

                    drop.Soldier.name = $"Paratrooper_{i + 1}";
                    drop.Soldier.SetActive(false);
                    droppers.Add(drop);
                }
            }

            private static GameObject CreateParachuteMesh(Mesh mesh, Material parachuteMaterial, string meshName)
            {
                GameObject parachute = new GameObject(meshName);
                MeshFilter filter = parachute.AddComponent<MeshFilter>();
                MeshRenderer renderer = parachute.AddComponent<MeshRenderer>();
                filter.sharedMesh = mesh;
                renderer.sharedMaterial = parachuteMaterial;
                return parachute;
            }

            private IEnumerator FlightRoutine(Aircraft aircraft)
            {
                while (elapsed < MaxOperationTime)
                {
                    float dt = Time.deltaTime;
                    if (dt <= 0f)
                    {
                        yield return null;
                        continue;
                    }

                    elapsed += dt;
                    bool allCleared = true;

                    for (int i = 0; i < droppers.Count; i++)
                    {
                        ParatrooperDrop drop = droppers[i];
                        if (drop.Soldier == null)
                            continue;

                        if (AdvanceDrop(drop, dt, aircraft))
                            allCleared = false;
                    }

                    if (allCleared)
                        break;

                    yield return null;
                }

                if (!insertionResolved)
                {
                    insertionResolved = true;
                }

                for (int i = 0; i < droppers.Count; i++)
                {
                    if (droppers[i].Soldier != null)
                        Destroy(droppers[i].Soldier);
                    if (droppers[i].Canopy != null)
                        Destroy(droppers[i].Canopy);
                    if (droppers[i].Lines != null)
                        Destroy(droppers[i].Lines);
                }

                yield return new WaitForSeconds(1f);
                Destroy(gameObject);
            }

            private bool AdvanceDrop(ParatrooperDrop drop, float dt, Aircraft aircraft)
            {
                if (drop.Soldier == null)
                    return false;

                if (drop.Landed)
                {
                    drop.CleanupDelay -= dt;
                    if (drop.CleanupDelay > 0f)
                        return true;

                    if (drop.Canopy != null) Destroy(drop.Canopy);
                    if (drop.Lines != null) Destroy(drop.Lines);
                    Destroy(drop.Soldier);
                    drop.Soldier = null;
                    return false;
                }

                if (!drop.Activated)
                {
                    drop.InFlightTime += dt;
                    if (drop.InFlightTime < drop.SpawnDelay)
                        return true;

                    drop.Activated = true;
                    drop.InFlightTime = 0f;
                    drop.Soldier.SetActive(true);
                }

                drop.InFlightTime += dt;
                if (!drop.ChuteOpen && drop.InFlightTime >= drop.ChuteDelay)
                {
                    drop.ChuteOpen = true;
                    if (drop.Canopy != null) drop.Canopy.SetActive(true);
                    if (drop.Lines != null) drop.Lines.SetActive(true);
                    if (drop.Animator != null)
                    {
                        SetPilotAnimation(drop.Animator, PilotDismounted.PilotState.parachuting);
                    }
                    Plugin.Logger.LogInfo("[Paratroopers] Static-line parachute fully deployed.");
                }

                Vector3 wind = Vector3.zero;
                if (aircraft != null && aircraft.rb != null)
                    wind = aircraft.rb.velocity * 0.2f;

                if (!drop.ChuteOpen)
                {
                    drop.Velocity += Vector3.down * FallGravity * dt;
                    drop.Velocity.x = Mathf.Lerp(drop.Velocity.x, wind.x, dt * 0.3f);
                    drop.Velocity.z = Mathf.Lerp(drop.Velocity.z, wind.z, dt * 0.3f);
                }
                else
                {
                    Vector3 target = new Vector3(wind.x * 0.25f, ChuteTerminalSpeed, wind.z * 0.25f);
                    drop.Velocity = Vector3.MoveTowards(drop.Velocity, target, 11f * dt);
                    float sway = Mathf.Sin(elapsed * 2.3f + iFromDrop(drop)) * 0.8f;
                    float side = Mathf.Cos(elapsed * 1.6f + iFromDrop(drop)) * 0.22f;
                    drop.Velocity += (aircraftUp * side + aircraftForward * sway * 0.25f) * dt;
                }

                float sweep = Mathf.Max(0.4f, drop.Velocity.magnitude * dt + 0.7f);
                Vector3 nextPos = drop.Soldier.transform.position + drop.Velocity * dt;
                if (Physics.Raycast(drop.Soldier.transform.position, Vector3.down, out RaycastHit hit, sweep, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                {
                    Land(drop, hit);
                    return true;
                }

                if (nextPos.y <= Datum.LocalSeaY + SeaMargin)
                {
                    hit.point = new Vector3(nextPos.x, Datum.LocalSeaY, nextPos.z);
                    Land(drop, hit, true);
                    return true;
                }

                drop.Soldier.transform.position = nextPos;
                Vector3 look = Vector3.ProjectOnPlane(drop.Velocity, aircraftUp);
                if (look.sqrMagnitude < 0.001f)
                    look = -aircraftUp;
                drop.Soldier.transform.rotation = Quaternion.LookRotation(look.normalized, aircraftUp);
                return true;
            }

            private static int iFromDrop(ParatrooperDrop drop)
            {
                return drop?.Soldier == null ? 0 : drop.Seed;
            }

            private void Land(ParatrooperDrop drop, RaycastHit hit, bool inWater = false)
            {
                if (drop == null || drop.Soldier == null || drop.Landed)
                    return;

                drop.Landed = true;
                drop.CleanupDelay = LandingHoldSeconds;

                Vector3 finalPos = drop.Soldier.transform.position;
                if (!inWater && Physics.Raycast(drop.Soldier.transform.position + Vector3.up * 0.2f, Vector3.down, out RaycastHit fallback, 8f, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                {
                    finalPos = fallback.point;
                }

                if (inWater)
                {
                    Plugin.Logger.LogInfo("[Paratroopers] Paratroopers touched down in water.");
                }

                if (drop.Canopy != null)
                    drop.Canopy.SetActive(false);
                if (drop.Lines != null)
                    drop.Lines.SetActive(false);

                if (GameAssets.i != null && GameAssets.i.contactDust != null && !inWater)
                {
                    GameObject dust = Instantiate(GameAssets.i.contactDust, finalPos + Vector3.up * 0.2f, Quaternion.identity);
                    dust.SetActive(true);
                    Destroy(dust, 3.5f);
                }

                if (!insertionResolved)
                {
                    insertionResolved = true;
                    ResolveInsertion(hit, inWater);
                }

                if (drop.Animator != null)
                    SetPilotAnimation(drop.Animator, PilotDismounted.PilotState.landing);
                drop.Soldier.transform.position = finalPos;
            }

            private void ResolveInsertion(RaycastHit hit, bool inWater)
            {
                if (inWater)
                {
                    return;
                }

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

            private static void SetPilotAnimation(Animator anim, PilotDismounted.PilotState state)
            {
                if (anim == null) return;
                anim.SetInteger("PilotState", (int)state);
                var paramCount = anim.parameters?.Length ?? 0;
                for (int i = 0; i < paramCount; i++)
                {
                    var p = anim.parameters[i];
                    if (p.type == AnimatorControllerParameterType.Bool &&
                        (p.name.IndexOf("parachute", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         p.name.IndexOf("chute", StringComparison.OrdinalIgnoreCase) >= 0))
                    {
                        anim.SetBool(p.name, true);
                    }
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

            private Vector3 rearExitLocal;
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
                public Vector3 LandingPosition;
                public float LandedAt;
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

                    if (landedCount == soldiers.Count && !callbackFired)
                    {
                        callbackFired = true;
                        callback?.Invoke();
                    }

                    yield return null;
                }

                if (!callbackFired && landedCount == soldiers.Count)
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
