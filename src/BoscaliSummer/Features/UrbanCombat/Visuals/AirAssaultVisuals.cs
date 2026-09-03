using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Advanced visual animation for air assault insertions:
    /// - Fast-Rope Rappelling Animation: Tactical ropes deploy from helicopter side doors,
    ///   animated infantry soldiers slide down with rappelling postures, hit the ground,
    ///   fan out to establish security, and ropes disconnect.
    /// - Paratrooper Airdrop: Cargo chute canopy deployment and descent from transport planes.
    /// </summary>
    internal static class AirAssaultVisuals
    {
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

        public static void SpawnParatrooperDrop(
            Vector3 releasePos,
            Vector3 landingPos,
            Quaternion forwardRot,
            FactionHQ owner,
            Action onLanded)
        {
            var dropGo = new GameObject("BoscaliSummer.ParatrooperDrop");
            dropGo.transform.position = releasePos;
            dropGo.transform.rotation = forwardRot;

            ParatrooperDescent descent = dropGo.AddComponent<ParatrooperDescent>();
            descent.Initialize(releasePos, landingPos, owner, onLanded);
        }

        private sealed class FastRopeRappellingOperation : MonoBehaviour
        {
            private Transform helo;
            private Vector3 target;
            private Action callback;
            private FactionHQ faction;

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
                faction = owner;
                callback = onLanded;

                Material ropeMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                ropeMat.color = new Color(0.15f, 0.15f, 0.14f, 1f);

                ropeLeft = CreateRopeLine("Rope_Port", ropeMat);
                ropeRight = CreateRopeLine("Rope_Starboard", ropeMat);

                // Spawn 4 rappelling soldiers (2 per rope, staggered)
                Material soldierMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                Color factionColor = owner != null && owner.faction != null ? owner.faction.color : new Color(0.25f, 0.35f, 0.25f, 1f);
                soldierMat.color = Color.Lerp(factionColor, new Color(0.2f, 0.22f, 0.2f, 1f), 0.6f);

                soldiers.Add(new RappellingSoldier { Root = CreateSoldierModel("Rappeller_L1", soldierMat), LeftRope = true, StartDelay = 0.2f });
                soldiers.Add(new RappellingSoldier { Root = CreateSoldierModel("Rappeller_R1", soldierMat), LeftRope = false, StartDelay = 0.35f });
                soldiers.Add(new RappellingSoldier { Root = CreateSoldierModel("Rappeller_L2", soldierMat), LeftRope = true, StartDelay = 0.75f });
                soldiers.Add(new RappellingSoldier { Root = CreateSoldierModel("Rappeller_R2", soldierMat), LeftRope = false, StartDelay = 0.9f });

                StartCoroutine(RappellingRoutine());
            }

            private LineRenderer CreateRopeLine(string name, Material mat)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = mat;
                lr.startWidth = 0.07f;
                lr.endWidth = 0.07f;
                lr.positionCount = 3;
                return lr;
            }

            private GameObject CreateSoldierModel(string name, Material mat)
            {
                var root = new GameObject(name);
                root.transform.SetParent(transform, false);

                // 1. Torso / tactical vest
                var torso = new GameObject("Torso");
                torso.transform.SetParent(root.transform, false);
                torso.transform.localPosition = new Vector3(0f, 0.45f, 0f);
                MeshFilter tmf = torso.AddComponent<MeshFilter>();
                MeshRenderer tmr = torso.AddComponent<MeshRenderer>();
                tmf.sharedMesh = CreateBoxMesh(0.45f, 0.6f, 0.3f);
                tmr.sharedMaterial = mat;

                // 2. Helmet / Head
                var head = new GameObject("Head");
                head.transform.SetParent(root.transform, false);
                head.transform.localPosition = new Vector3(0f, 0.85f, 0f);
                MeshFilter hmf = head.AddComponent<MeshFilter>();
                MeshRenderer hmr = head.AddComponent<MeshRenderer>();
                hmf.sharedMesh = CreateBoxMesh(0.28f, 0.28f, 0.28f);
                hmr.sharedMaterial = mat;

                // 3. Legs in rappelling brake posture
                var legs = new GameObject("Legs");
                legs.transform.SetParent(root.transform, false);
                legs.transform.localPosition = new Vector3(0f, 0.05f, -0.15f);
                legs.transform.localRotation = Quaternion.Euler(25f, 0f, 0f);
                MeshFilter lmf = legs.AddComponent<MeshFilter>();
                MeshRenderer lmr = legs.AddComponent<MeshRenderer>();
                lmf.sharedMesh = CreateBoxMesh(0.35f, 0.5f, 0.25f);
                lmr.sharedMaterial = mat;

                root.SetActive(false);
                return root;
            }

            private IEnumerator RappellingRoutine()
            {
                float duration = 4.2f;
                float elapsed = 0f;
                bool triggered = false;

                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;

                    Vector3 heloPos = helo != null ? helo.position : transform.position;
                    Vector3 heloRight = helo != null ? helo.right : Vector3.right;
                    Vector3 heloFwd = helo != null ? helo.forward : Vector3.forward;

                    Vector3 doorLeft = heloPos - heloRight * 1.3f - heloFwd * 0.4f - Vector3.up * 0.2f;
                    Vector3 doorRight = heloPos + heloRight * 1.3f - heloFwd * 0.4f - Vector3.up * 0.2f;

                    Vector3 groundLeft = target - heloRight * 1.1f;
                    Vector3 groundRight = target + heloRight * 1.1f;

                    // Dynamic catenary curve on ropes
                    Vector3 midLeft = Vector3.Lerp(doorLeft, groundLeft, 0.5f) + Vector3.down * 0.35f;
                    Vector3 midRight = Vector3.Lerp(doorRight, groundRight, 0.5f) + Vector3.down * 0.35f;

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
                        if (elapsed < s.StartDelay) continue;

                        if (!s.Root.activeSelf) s.Root.SetActive(true);

                        Vector3 door = s.LeftRope ? doorLeft : doorRight;
                        Vector3 ground = s.LeftRope ? groundLeft : groundRight;

                        float descentTime = elapsed - s.StartDelay;
                        s.Progress = Mathf.Clamp01(descentTime / 1.6f);

                        if (s.Progress < 1f)
                        {
                            // Sliding down the rope
                            Vector3 pos = Vector3.Lerp(door, ground, s.Progress);
                            // Slight swinging motion during rapid descent
                            pos += Mathf.Sin(elapsed * 12f + i) * 0.08f * (s.LeftRope ? -heloRight : heloRight);
                            s.Root.transform.position = pos;
                            s.Root.transform.rotation = Quaternion.LookRotation(heloFwd, Vector3.up);
                        }
                        else
                        {
                            // Landed on roof / ground, fanning out for perimeter security
                            if (!s.Landed)
                            {
                                s.Landed = true;
                                if (GameAssets.i != null && GameAssets.i.contactDust != null)
                                {
                                    GameObject dust = Instantiate(GameAssets.i.contactDust, ground + Vector3.up * 0.1f, Quaternion.identity);
                                    dust.SetActive(true);
                                    Destroy(dust, 2f);
                                }
                            }

                            landedCount++;

                            // Fan out outward into defensive perimeter
                            float fanProgress = Mathf.Clamp01((descentTime - 1.6f) / 0.8f);
                            Vector3 fanDir = Quaternion.Euler(0f, i * 90f, 0f) * heloFwd;
                            Vector3 finalPos = ground + fanDir * 2.5f;
                            s.Root.transform.position = Vector3.Lerp(ground, finalPos, fanProgress);
                            s.Root.transform.rotation = Quaternion.LookRotation(fanDir, Vector3.up);
                        }
                    }

                    // Once 2+ soldiers touch down, trigger the objective capture/encampment!
                    if (!triggered && landedCount >= 2)
                    {
                        triggered = true;
                        callback?.Invoke();
                    }

                    yield return null;
                }

                // Disconnect ropes
                if (ropeLeft != null) Destroy(ropeLeft.gameObject);
                if (ropeRight != null) Destroy(ropeRight.gameObject);

                for (int i = 0; i < soldiers.Count; i++)
                {
                    if (soldiers[i]?.Root != null) Destroy(soldiers[i].Root);
                }

                Destroy(gameObject, 0.2f);
            }
        }

        private sealed class ParatrooperDescent : MonoBehaviour
        {
            private Vector3 start;
            private Vector3 target;
            private Action callback;
            private float descentSpeed = 16f;
            private GameObject canopy;
            private GameObject payload;

            public void Initialize(Vector3 startPos, Vector3 targetPos, FactionHQ owner, Action onLanded)
            {
                start = startPos;
                target = targetPos;
                callback = onLanded;

                BuildParatrooperMesh(owner);
                StartCoroutine(DescentRoutine());
            }

            private void BuildParatrooperMesh(FactionHQ owner)
            {
                canopy = new GameObject("Canopy");
                canopy.transform.SetParent(transform, false);
                canopy.transform.localPosition = Vector3.up * 4.5f;

                MeshFilter mf = canopy.AddComponent<MeshFilter>();
                MeshRenderer mr = canopy.AddComponent<MeshRenderer>();
                mf.sharedMesh = CreateCanopyMesh(4.5f, 2.8f);

                Material canopyMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                canopyMat.color = owner != null && owner.faction != null ? owner.faction.color : new Color(0.85f, 0.85f, 0.8f, 1f);
                mr.sharedMaterial = canopyMat;

                payload = new GameObject("Payload");
                payload.transform.SetParent(transform, false);
                payload.transform.localPosition = Vector3.zero;

                MeshFilter pmf = payload.AddComponent<MeshFilter>();
                MeshRenderer pmr = payload.AddComponent<MeshRenderer>();
                pmf.sharedMesh = CreateBoxMesh(1.5f, 1.2f, 1.5f);

                Material payMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                payMat.color = new Color(0.28f, 0.32f, 0.26f, 1f);
                pmr.sharedMaterial = payMat;
            }

            private IEnumerator DescentRoutine()
            {
                Vector3 current = start;
                while (current.y > target.y + 0.5f)
                {
                    current.y = Mathf.MoveTowards(current.y, target.y, descentSpeed * Time.deltaTime);
                    current.x = Mathf.MoveTowards(current.x, target.x, 3f * Time.deltaTime);
                    current.z = Mathf.MoveTowards(current.z, target.z, 3f * Time.deltaTime);
                    transform.position = current;
                    yield return null;
                }

                transform.position = target;

                if (GameAssets.i != null && GameAssets.i.contactDust != null)
                {
                    GameObject dust = Instantiate(GameAssets.i.contactDust, target + Vector3.up * 0.2f, Quaternion.identity);
                    dust.SetActive(true);
                    Destroy(dust, 3f);
                }

                callback?.Invoke();
                Destroy(gameObject, 0.2f);
            }
        }

        private static Mesh CreateCanopyMesh(float radius, float height)
        {
            Mesh mesh = new Mesh();
            mesh.name = "ParachuteCanopy";

            int segments = 12;
            var verts = new List<Vector3>();
            var tris = new List<int>();

            verts.Add(new Vector3(0f, height, 0f));
            for (int i = 0; i < segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                verts.Add(new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius));
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
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateBoxMesh(float w, float h, float d)
        {
            Mesh mesh = new Mesh();
            mesh.name = "Box";

            float hw = w * 0.5f;
            float hh = h * 0.5f;
            float hd = d * 0.5f;

            Vector3[] vertices = new Vector3[]
            {
                new Vector3(-hw, -hh, -hd),
                new Vector3( hw, -hh, -hd),
                new Vector3( hw,  hh, -hd),
                new Vector3(-hw,  hh, -hd),
                new Vector3(-hw, -hh,  hd),
                new Vector3( hw, -hh,  hd),
                new Vector3( hw,  hh,  hd),
                new Vector3(-hw,  hh,  hd)
            };

            int[] triangles = new int[]
            {
                0, 2, 1,  0, 3, 2,
                4, 5, 6,  4, 6, 7,
                0, 1, 5,  0, 5, 4,
                2, 3, 7,  2, 7, 6,
                0, 4, 7,  0, 7, 3,
                1, 2, 6,  1, 6, 5
            };

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}