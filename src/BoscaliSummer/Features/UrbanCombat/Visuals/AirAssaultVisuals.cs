using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Visual presentation for air assault operations:
    /// - Paratrooper parachute canopy drops trailing smoke for the Chimera transport plane.
    /// - Fast-rope deployment lines and descending infantry for the Ibis helicopter.
    /// </summary>
    internal static class AirAssaultVisuals
    {
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

        public static void SpawnFastRopeDeployment(
            Transform heloTransform,
            Vector3 landingPos,
            FactionHQ owner,
            Action onLanded)
        {
            var ropeGo = new GameObject("BoscaliSummer.FastRopeOperation");
            ropeGo.transform.position = heloTransform.position;

            FastRopeOperation op = ropeGo.AddComponent<FastRopeOperation>();
            op.Initialize(heloTransform, landingPos, owner, onLanded);
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
                // 1. Parachute canopy (dome shape)
                canopy = new GameObject("Canopy");
                canopy.transform.SetParent(transform, false);
                canopy.transform.localPosition = Vector3.up * 4.5f;

                MeshFilter mf = canopy.AddComponent<MeshFilter>();
                MeshRenderer mr = canopy.AddComponent<MeshRenderer>();
                mf.sharedMesh = CreateCanopyMesh(4.5f, 2.8f);

                Material canopyMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                canopyMat.color = owner != null && owner.faction != null ? owner.faction.color : new Color(0.85f, 0.85f, 0.8f, 1f);
                mr.sharedMaterial = canopyMat;

                // 2. Payload pallet / soldier squad crate
                payload = new GameObject("Payload");
                payload.transform.SetParent(transform, false);
                payload.transform.localPosition = Vector3.zero;

                MeshFilter pmf = payload.AddComponent<MeshFilter>();
                MeshRenderer pmr = payload.AddComponent<MeshRenderer>();
                pmf.sharedMesh = CreateCubeMesh(1.5f, 1.2f, 1.5f);

                Material payMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                payMat.color = new Color(0.28f, 0.32f, 0.26f, 1f); // Military olive
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

                // Dust burst upon impact
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

        private sealed class FastRopeOperation : MonoBehaviour
        {
            private Transform helo;
            private Vector3 target;
            private Action callback;
            private LineRenderer ropeLeft;
            private LineRenderer ropeRight;
            private GameObject soldierLeft;
            private GameObject soldierRight;

            public void Initialize(Transform helicopter, Vector3 targetPos, FactionHQ owner, Action onLanded)
            {
                helo = helicopter;
                target = targetPos;
                callback = onLanded;

                Material ropeMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                ropeMat.color = new Color(0.12f, 0.12f, 0.12f, 1f);

                ropeLeft = CreateRopeLine("RopeLeft", ropeMat);
                ropeRight = CreateRopeLine("RopeRight", ropeMat);

                soldierLeft = CreateSoldierFigure("Soldier1");
                soldierRight = CreateSoldierFigure("Soldier2");

                StartCoroutine(FastRopeRoutine());
            }

            private LineRenderer CreateRopeLine(string name, Material mat)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                LineRenderer lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = mat;
                lr.startWidth = 0.08f;
                lr.endWidth = 0.08f;
                lr.positionCount = 2;
                return lr;
            }

            private GameObject CreateSoldierFigure(string name)
            {
                var go = new GameObject(name);
                go.transform.SetParent(transform, false);
                MeshFilter mf = go.AddComponent<MeshFilter>();
                MeshRenderer mr = go.AddComponent<MeshRenderer>();
                mf.sharedMesh = CreateCubeMesh(0.5f, 1.2f, 0.5f);

                Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                mat.color = new Color(0.2f, 0.24f, 0.2f, 1f);
                mr.sharedMaterial = mat;
                return go;
            }

            private IEnumerator FastRopeRoutine()
            {
                float duration = 2.5f;
                float elapsed = 0f;
                bool deployed = false;

                while (elapsed < duration)
                {
                    elapsed += Time.deltaTime;
                    float progress = Mathf.Clamp01(elapsed / 1.8f);

                    Vector3 heloPos = helo != null ? helo.position : transform.position;
                    Vector3 leftDoor = heloPos - (helo != null ? helo.right * 1.2f : Vector3.right * 1.2f);
                    Vector3 rightDoor = heloPos + (helo != null ? helo.right * 1.2f : Vector3.right * 1.2f);

                    ropeLeft.SetPosition(0, leftDoor);
                    ropeLeft.SetPosition(1, target + Vector3.left * 0.8f);

                    ropeRight.SetPosition(0, rightDoor);
                    ropeRight.SetPosition(1, target + Vector3.right * 0.8f);

                    soldierLeft.transform.position = Vector3.Lerp(leftDoor, target + Vector3.left * 0.8f, progress);
                    soldierRight.transform.position = Vector3.Lerp(rightDoor, target + Vector3.right * 0.8f, Mathf.Clamp01(progress * 1.15f));

                    if (!deployed && progress >= 0.95f)
                    {
                        deployed = true;
                        callback?.Invoke();
                    }

                    yield return null;
                }

                Destroy(soldierLeft);
                Destroy(soldierRight);
                Destroy(ropeLeft.gameObject);
                Destroy(ropeRight.gameObject);
                Destroy(gameObject, 0.5f);
            }
        }

        private static Mesh CreateCanopyMesh(float radius, float height)
        {
            Mesh mesh = new Mesh();
            mesh.name = "ParachuteCanopy";

            int segments = 12;
            var verts = new List<Vector3>();
            var tris = new List<int>();

            // Apex
            verts.Add(new Vector3(0f, height, 0f));

            // Ring
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

        private static Mesh CreateCubeMesh(float w, float h, float d)
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