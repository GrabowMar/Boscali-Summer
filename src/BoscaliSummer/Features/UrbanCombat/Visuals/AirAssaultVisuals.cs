using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Advanced visual animations for air assault insertions:
    /// - Paratrooper Cargo Hold Drop: Deploys out the rear cargo ramp like vehicle/cargo drops,
    ///   deploys the authentic ejected pilot parachute canopy & lines, decelerates realistically,
    ///   and lands to capture buildings or establish combat encampments.
    /// - Fast-Rope Rappelling: Realistic, slower slide (~3.5s) down port & starboard ropes from helicopters.
    /// </summary>
    internal static class AirAssaultVisuals
    {
        private static Mesh cachedCanopyMesh;
        private static Mesh cachedLinesMesh;
        private static Material cachedParachuteMat;

        public static void SpawnParatrooperCargoDrop(
            Aircraft aircraft,
            Vector3 rampPos,
            Vector3 exitVelocity,
            FactionHQ owner,
            Airbase airbase)
        {
            var dropGo = new GameObject("BoscaliSummer.ParatrooperCargoDrop");
            dropGo.transform.position = rampPos;

            ParatrooperCargoDropOperation op = dropGo.AddComponent<ParatrooperCargoDropOperation>();
            op.Initialize(aircraft, rampPos, exitVelocity, owner, airbase);
        }

        public static void SpawnFastRopeRappelling(
            Transform heloTransform,
            Vector3 landingPos,
            FactionHQ owner,
            Action onLanded)
        {
            var opGo = new GameObject("BoscaliSummer.FastRopeRappelling");
            opGo.transform.position = heloTransform.position;

            FastRopeRappellingOperation op = opGo.AddComponent<FastRopeRappellingOperation>();
            op.Initialize(heloTransform, landingPos, owner, onLanded);
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
                    ZoneGarrisonManager.Instance?.TryDeployEncampment(hit.point, owner, airbase);
                }
            }
        }

        private sealed class FastRopeRappellingOperation : MonoBehaviour
        {
            private Transform helo;
            private Vector3 target;
            private Action callback;
            private LineRenderer ropeLeft;
            private LineRenderer ropeRight;
            private readonly List<RappellingSoldier> soldiers = new List<RappellingSoldier>();

            private sealed class RappellingSoldier
            {
                public GameObject Root;
                public bool LeftRope;
                public float StartDelay;
                public float Progress;
                public bool Landed;
            }

            public void Initialize(Transform helicopter, Vector3 targetPos, FactionHQ owner, Action onLanded)
            {
                helo = helicopter;
                target = targetPos;
                callback = onLanded;

                Material ropeMat = MaterialProvider.GetCargoHookRopeMaterial() ?? MaterialProvider.GetConcreteMaterial();

                ropeLeft = CreateRopeLine("Rope_Port", ropeMat);
                ropeRight = CreateRopeLine("Rope_Starboard", ropeMat);

                soldiers.Add(new RappellingSoldier { Root = CreateSoldier("Rappeller_L1"), LeftRope = true, StartDelay = 0.2f });
                soldiers.Add(new RappellingSoldier { Root = CreateSoldier("Rappeller_R1"), LeftRope = false, StartDelay = 0.5f });
                soldiers.Add(new RappellingSoldier { Root = CreateSoldier("Rappeller_L2"), LeftRope = true, StartDelay = 1.1f });
                soldiers.Add(new RappellingSoldier { Root = CreateSoldier("Rappeller_R2"), LeftRope = false, StartDelay = 1.4f });

                StartCoroutine(RappellingRoutine());
            }

            private LineRenderer CreateRopeLine(string name, Material mat)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                if (mat != null) lr.sharedMaterial = mat;
                lr.startWidth = 0.08f;
                lr.endWidth = 0.08f;
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

            private IEnumerator RappellingRoutine()
            {
                float duration = 6.0f;
                float elapsed = 0f;
                bool triggered = false;

                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;

                    Vector3 heloPos = helo != null ? helo.position : transform.position;
                    Vector3 heloRight = helo != null ? helo.right : Vector3.right;
                    Vector3 heloFwd = helo != null ? helo.forward : Vector3.forward;
                    Vector3 heloUp = helo != null ? helo.up : Vector3.up;

                    // Lines deploy directly from the back of the cargo hold at the rear of the model
                    Vector3 rearRampPos = heloPos - heloFwd * 3.8f - heloUp * 0.45f;
                    Vector3 doorLeft = rearRampPos - heloRight * 0.55f;
                    Vector3 doorRight = rearRampPos + heloRight * 0.55f;

                    Vector3 groundLeft = target - heloRight * 1.1f;
                    Vector3 groundRight = target + heloRight * 1.1f;

                    Vector3 midLeft = Vector3.Lerp(doorLeft, groundLeft, 0.5f) + Vector3.down * 0.4f;
                    Vector3 midRight = Vector3.Lerp(doorRight, groundRight, 0.5f) + Vector3.down * 0.4f;

                    if (ropeLeft != null)
                    {
                        ropeLeft.SetPosition(0, doorLeft);
                        ropeLeft.SetPosition(1, midLeft);
                        ropeLeft.SetPosition(2, groundLeft);
                    }

                    if (ropeRight != null)
                    {
                        ropeRight.SetPosition(0, doorRight);
                        ropeRight.SetPosition(1, midRight);
                        ropeRight.SetPosition(2, groundRight);
                    }

                    int landedCount = 0;

                    for (int i = 0; i < soldiers.Count; i++)
                    {
                        RappellingSoldier s = soldiers[i];
                        if (s.Root == null) continue;
                        if (elapsed < s.StartDelay) continue;

                        if (!s.Root.activeSelf) s.Root.SetActive(true);

                        Vector3 door = s.LeftRope ? doorLeft : doorRight;
                        Vector3 ground = s.LeftRope ? groundLeft : groundRight;

                        float descentTime = elapsed - s.StartDelay;
                        s.Progress = Mathf.Clamp01(descentTime / 3.4f);

                        if (s.Progress < 1f)
                        {
                            Vector3 pos = Vector3.Lerp(door, ground, s.Progress);
                            pos += Mathf.Sin(elapsed * 8f + i) * 0.05f * (s.LeftRope ? -heloRight : heloRight);
                            s.Root.transform.position = pos;
                            s.Root.transform.rotation = Quaternion.LookRotation(heloFwd, Vector3.up);
                        }
                        else
                        {
                            if (!s.Landed)
                            {
                                s.Landed = true;
                                if (GameAssets.i != null && GameAssets.i.contactDust != null)
                                {
                                    GameObject dust = Instantiate(GameAssets.i.contactDust, ground + Vector3.up * 0.2f, Quaternion.identity);
                                    dust.SetActive(true);
                                    Destroy(dust, 3f);
                                }
                            }

                            float fanAngle = (i * 90f + 45f) * Mathf.Deg2Rad;
                            Vector3 fanDir = new Vector3(Mathf.Cos(fanAngle), 0f, Mathf.Sin(fanAngle));
                            float fanDist = Mathf.Min((elapsed - s.StartDelay - 3.4f) * 1.8f, 3.5f);

                            Vector3 perimeterPos = ground + fanDir * fanDist;
                            s.Root.transform.position = perimeterPos;
                            s.Root.transform.rotation = Quaternion.LookRotation(fanDir, Vector3.up);
                            landedCount++;
                        }
                    }

                    if (landedCount >= 2 && !triggered)
                    {
                        triggered = true;
                        callback?.Invoke();
                    }

                    yield return null;
                }

                if (!triggered) callback?.Invoke();

                float dropTime = 0f;
                while (dropTime < 0.8f)
                {
                    dropTime += Time.deltaTime;
                    if (ropeLeft != null) ropeLeft.transform.position += Vector3.down * 4f * Time.deltaTime;
                    if (ropeRight != null) ropeRight.transform.position += Vector3.down * 4f * Time.deltaTime;
                    yield return null;
                }

                Destroy(gameObject, 4f);
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