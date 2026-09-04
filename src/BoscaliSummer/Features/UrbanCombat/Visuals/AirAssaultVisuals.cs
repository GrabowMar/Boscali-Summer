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
            Airbase airbase)
        {
            var dropGo = new GameObject("BoscaliSummer.ParatrooperCargoDrop");
            dropGo.transform.position = rampPos;
            if (!Track(dropGo)) return;

            ParatrooperCargoDropOperation op = dropGo.AddComponent<ParatrooperCargoDropOperation>();
            op.Initialize(aircraft, rampPos, exitVelocity, owner, airbase);
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
                Airbase baseObj)
            {
                owner = faction;
                airbase = baseObj;
                velocity = initialVel;
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
                    Plugin.Logger.LogInfo($"[AIR ASSAULT] Paratrooper squad secured and fortified building: {shell.name}!");
                    ZoneGarrisonManager.Instance?.TryOccupyBuilding(shell, owner, airbase);
                }
                else
                {
                    Plugin.Logger.LogInfo($"[AIR ASSAULT] Paratroopers established combat encampment at ({hit.point.x:0}, {hit.point.z:0})!");
                    ZoneGarrisonManager.Instance?.TryDeployEncampment(hit.point, owner, airbase, 1);
                }
            }
        }

        private sealed class FastRopeRappellingOperation : MonoBehaviour
        {
            private const float RopeHalfSpread = 0.55f;
            private const float FallbackRearDistance = 5f;
            private const float FallbackDoorDrop = 0.6f;
            private const float MinDescent = 2.5f;
            private const float MaxDescent = 4.6f;
            private const float OperationTimeCap = 16f;

            private Aircraft aircraft;
            private Transform helo;
            private Vector3 target;
            private Action callback;
            private int requestedCount;

            private LineRenderer ropeLeft;
            private LineRenderer ropeRight;

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
                public float DescentDuration;
                public float Progress;
                public bool Landed;
                public Vector3 FanDir;
                public float SwayPhase;
            }

            public void Initialize(Aircraft currentAircraft, Vector3 targetPos, FactionHQ owner, int soldierCount, Action onLanded)
            {
                aircraft = currentAircraft;
                helo = aircraft != null ? aircraft.transform : transform;
                target = targetPos;
                callback = onLanded;
                requestedCount = Mathf.Max(1, soldierCount);

                // Anchor the whole operation to the aircraft so the ropes and rappellers
                // track it exactly — they can never drift detached when the helo moves.
                transform.SetParent(helo, false);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;

                Material ropeMat = MaterialProvider.GetCargoHookRopeMaterial() ?? MaterialProvider.GetConcreteMaterial();
                ropeLeft = CreateRopeLine("Rope_Left", ropeMat);
                ropeRight = CreateRopeLine("Rope_Right", ropeMat);

                ComputeAnchors();
                BuildSoldiers();

                StartCoroutine(RappellingRoutine());
            }

            private LineRenderer CreateRopeLine(string name, Material mat)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                if (mat != null) lr.sharedMaterial = mat;
                lr.startWidth = 0.045f;
                lr.endWidth = 0.03f;
                lr.useWorldSpace = true;
                lr.positionCount = 3;
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
                        StartDelay = 0.2f + onRope * 0.9f + UnityEngine.Random.Range(0f, 0.35f) + rope * 0.12f,
                        DescentDuration = UnityEngine.Random.Range(MinDescent, MaxDescent),
                        SwayPhase = UnityEngine.Random.Range(0f, Mathf.PI * 2f),
                        FanDir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)).normalized
                    });
                }
            }

            private void ComputeAnchors()
            {
                Vector3 rearCenter = ComputeRearExit();
                doorLeft = rearCenter - helo.right * RopeHalfSpread;
                doorRight = rearCenter + helo.right * RopeHalfSpread;

                groundLeft = target - helo.right * 1.1f;
                groundRight = target + helo.right * 1.1f;
            }

            // Anchors the ropes to the rear cargo door / ramp opening, not the middle of
            // the fuselage. Falls back to a scaled rear-of-aircraft offset when no door
            // transform is present on the model.
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

            private IEnumerator RappellingRoutine()
            {
                while (elapsed < OperationTimeCap && landedCount < soldiers.Count)
                {
                    float dt = Time.deltaTime;
                    elapsed += dt;

                    // Recomputed every frame so the rope tops follow the moving helicopter.
                    ComputeAnchors();

                    if (ropeLeft != null)
                    {
                        ropeLeft.SetPosition(0, doorLeft);
                        ropeLeft.SetPosition(1, Vector3.Lerp(doorLeft, groundLeft, 0.5f) + Vector3.down * 0.45f);
                        ropeLeft.SetPosition(2, groundLeft);
                    }
                    if (ropeRight != null)
                    {
                        ropeRight.SetPosition(0, doorRight);
                        ropeRight.SetPosition(1, Vector3.Lerp(doorRight, groundRight, 0.5f) + Vector3.down * 0.45f);
                        ropeRight.SetPosition(2, groundRight);
                    }

                    for (int i = 0; i < soldiers.Count; i++)
                    {
                        RappellingSoldier s = soldiers[i];
                        if (s.Root == null) continue;
                        if (elapsed < s.StartDelay) continue;

                        if (!s.Root.activeSelf) s.Root.SetActive(true);

                        Vector3 door = s.Rope == 0 ? doorLeft : doorRight;
                        Vector3 ground = s.Rope == 0 ? groundLeft : groundRight;

                        float descentTime = elapsed - s.StartDelay;
                        s.Progress = Mathf.Clamp01(descentTime / s.DescentDuration);

                        if (s.Progress < 1f)
                        {
                            float eased = Mathf.SmoothStep(0f, 1f, s.Progress);
                            Vector3 pos = Vector3.Lerp(door, ground, eased);
                            pos += helo.right * Mathf.Sin(elapsed * 7f + s.SwayPhase) * 0.055f * (s.Rope == 0 ? -1f : 1f);

                            s.Root.transform.position = pos;

                            Vector3 facing = Vector3.ProjectOnPlane(helo.position - pos, Vector3.up);
                            if (facing.sqrMagnitude < 0.001f) facing = -helo.forward;
                            s.Root.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
                            continue;
                        }

                        if (!s.Landed)
                        {
                            s.Landed = true;
                            landedCount++;
                            SpawnDust(ground);
                        }

                        float spread = Mathf.Min((descentTime - s.DescentDuration) * 1.9f, 3.6f);
                        Vector3 perimeterPos = ground + s.FanDir * spread;
                        s.Root.transform.position = perimeterPos;
                        s.Root.transform.rotation = Quaternion.LookRotation(s.FanDir, Vector3.up);
                    }

                    // Trigger the capture/encampment as soon as the lead soldier is down so
                    // the landing zone starts being secured while the tail of the squad finishes.
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

                // Pull the ropes up into the aircraft after the squad has cleared.
                float retract = 0f;
                const float retractTime = 0.9f;
                while (retract < retractTime)
                {
                    retract += Time.deltaTime;
                    float t = Mathf.Clamp01(retract / retractTime);
                    if (ropeLeft != null)
                    {
                        ropeLeft.SetPosition(0, doorLeft);
                        ropeLeft.SetPosition(1, Vector3.Lerp(doorLeft, groundLeft, (1f - t) * 0.5f) + Vector3.down * 0.45f * (1f - t));
                        ropeLeft.SetPosition(2, Vector3.Lerp(doorLeft, groundLeft, 1f - t));
                    }
                    if (ropeRight != null)
                    {
                        ropeRight.SetPosition(0, doorRight);
                        ropeRight.SetPosition(1, Vector3.Lerp(doorRight, groundRight, (1f - t) * 0.5f) + Vector3.down * 0.45f * (1f - t));
                        ropeRight.SetPosition(2, Vector3.Lerp(doorRight, groundRight, 1f - t));
                    }
                    yield return null;
                }

                Destroy(gameObject, 1.5f);
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
