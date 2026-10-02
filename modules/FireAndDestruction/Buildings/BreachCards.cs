using System.Collections.Generic;
using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.FireAndDestruction.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Torn breach cards. The mesh is only the mouth and its lip, so the real wall
    /// stays visible around it. Four shared variants, one street bay, no per-hit texture.
    /// </summary>
    internal sealed class BreachCards
    {
        private const int MaxCards = 48;
        private const int Variants = 4;
        private const int TexW = 96;
        private const int TexH = 144;
        private const int Cell = 3;

        private readonly List<GameObject> slots = new List<GameObject>(MaxCards);
        private readonly List<int> owners = new List<int>(MaxCards);
        private int head;

        private static Mesh[] meshes;
        private static Material[] materials;
        private static Mesh bayMesh;
        private static Material bayMaterial;
        private static bool failed;

        internal bool Place(int owner, Vector3 point, Vector3 normal, float breachSize)
        {
            if (GameManager.IsHeadless || breachSize < 0.5f) return false;
            Ensure();
            if (failed || materials == null) return false;
            uint hash = Deterministic.Hash(
                Mathf.RoundToInt(point.x * 10f),
                Mathf.RoundToInt(point.y * 10f),
                Mathf.RoundToInt(point.z * 10f), 91);
            int variant = (int)(Deterministic.UnitFloat(hash) * Variants) % Variants;
            GameObject card = Acquire(owner);
            if (card == null) return false;
            Pose(card, point, normal, breachSize * 1.4f, breachSize * 2.1f, 0.07f);
            card.GetComponent<MeshFilter>().sharedMesh = meshes[variant];
            card.GetComponent<MeshRenderer>().sharedMaterial = materials[variant];
            card.SetActive(true);
            return true;
        }

        internal static void CreateBay(Transform parent, Vector3 point, Vector3 normal, float rad)
        {
            if (parent == null || GameManager.IsHeadless || rad < 1f) return;
            Ensure();
            if (bayMesh == null || bayMaterial == null) return;
            GameObject card = NewCard("BoscaliSummer.RuinBay");
            card.transform.SetParent(parent, false);
            Pose(card, point, normal, rad * 2.1f, rad * 1.3f, 0.12f);
            card.GetComponent<MeshFilter>().sharedMesh = bayMesh;
            card.GetComponent<MeshRenderer>().sharedMaterial = bayMaterial;
        }

        /// <summary>
        /// An extra mouth on a fallen facade. Lives on the shell, so it dies with that
        /// ruin, and stops at three so a strafe cannot fill the ring.
        /// </summary>
        internal void Stamp(Transform parent, Vector3 point, Vector3 normal, float breachSize)
        {
            if (parent == null || GameManager.IsHeadless || breachSize < 0.5f) return;
            int hits = 0;
            for (int i = 0; i < parent.childCount; i++)
                if (parent.GetChild(i).name == "BoscaliSummer.BreachHit") hits++;
            if (hits >= 3) return;
            Ensure();
            if (failed || meshes == null || meshes[0] == null) return;
            uint hash = Deterministic.Hash(
                Mathf.RoundToInt(point.x * 10f),
                Mathf.RoundToInt(point.y * 10f),
                Mathf.RoundToInt(point.z * 10f), 91);
            int variant = (int)(Deterministic.UnitFloat(hash) * Variants) % Variants;
            GameObject card = NewCard("BoscaliSummer.BreachHit");
            card.transform.SetParent(parent, false);
            Pose(card, point, normal, breachSize * 1.4f, breachSize * 2.1f, 0.07f);
            card.GetComponent<MeshFilter>().sharedMesh = meshes[variant];
            card.GetComponent<MeshRenderer>().sharedMaterial = materials[variant];
        }

        internal void Hide(int owner)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (owners[i] != owner || slots[i] == null) continue;
                slots[i].SetActive(false);
                owners[i] = 0;
            }
        }

        internal void Adopt(int owner, Transform parent)
        {
            if (parent == null) return;
            for (int i = 0; i < slots.Count; i++)
            {
                if (owners[i] != owner || slots[i] == null) continue;
                slots[i].transform.SetParent(parent, true);
                slots[i] = null;
                owners[i] = 0;
            }
        }

        internal void Clear()
        {
            for (int i = 0; i < slots.Count; i++)
                if (slots[i] != null) Object.Destroy(slots[i]);
            slots.Clear();
            owners.Clear();
            head = 0;
        }

        private GameObject Acquire(int owner)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (slots[i] != null) continue;
                slots[i] = Spawn();
                owners[i] = owner;
                return slots[i];
            }
            if (slots.Count < MaxCards)
            {
                GameObject created = Spawn();
                slots.Add(created);
                owners.Add(owner);
                return created;
            }
            int index = head;
            head = (head + 1) % slots.Count;
            owners[index] = owner;
            if (slots[index] == null) slots[index] = Spawn();
            return slots[index];
        }

        private static GameObject Spawn()
        {
            GameObject card = NewCard("BoscaliSummer.Breach");
            card.transform.SetParent(Datum.origin, false);
            return card;
        }

        private static GameObject NewCard(string name)
        {
            var card = new GameObject(name);
            card.AddComponent<MeshFilter>();
            var renderer = card.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            return card;
        }

        private static void Pose(GameObject card, Vector3 point, Vector3 normal, float width, float height, float offset)
        {
            if (normal.sqrMagnitude < 0.0001f) normal = Vector3.forward;
            normal.Normalize();
            Vector3 up = Mathf.Abs(normal.y) > 0.85f ? Vector3.forward : Vector3.up;
            card.transform.SetPositionAndRotation(point + normal * offset, Quaternion.LookRotation(normal, up));
            card.transform.localScale = new Vector3(Mathf.Max(0.4f, width), Mathf.Max(0.4f, height), 1f);
        }

        private static void Ensure()
        {
            if (failed || meshes != null) return;
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Legacy Shaders/Diffuse")
                ?? Shader.Find("Diffuse");
            if (shader == null)
            {
                failed = true;
                return;
            }
            meshes = new Mesh[Variants];
            materials = new Material[Variants];
            float[] seeds = { 0.17f, 0.41f, 0.66f, 0.88f };
            for (int i = 0; i < Variants; i++)
                Build(shader, seeds[i], false, out meshes[i], out materials[i]);
            Build(shader, 0.42f, true, out bayMesh, out bayMaterial);
            if (meshes[0] == null) failed = true;
        }

        private static void Build(Shader shader, float seed, bool bay, out Mesh mesh, out Material material)
        {
            mesh = null;
            material = null;
            var pixels = new Color32[TexW * TexH];
            for (int y = 0; y < TexH; y++)
            {
                float v = bay
                    ? y / (float)(TexH - 1) * 1.3f - 0.15f
                    : y / (float)(TexH - 1) * 2f - 1f;
                for (int x = 0; x < TexW; x++)
                {
                    float u = bay
                        ? x / (float)(TexW - 1) * 2.1f - 1.05f
                        : x / (float)(TexW - 1) * 2f - 1f;
                    BreachShape.Texel(u, v, seed, bay, out byte r, out byte g, out byte b, out byte a);
                    pixels[y * TexW + x] = new Color32(r, g, b, a);
                }
            }

            var verts = new List<Vector3>(256);
            var uvs = new List<Vector2>(256);
            var normals = new List<Vector3>(256);
            var tris = new List<int>(384);
            int cellsX = (TexW - 1) / Cell;
            int cellsY = (TexH - 1) / Cell;
            for (int cy = 0; cy < cellsY; cy++)
            {
                for (int cx = 0; cx < cellsX; cx++)
                {
                    int px = cx * Cell;
                    int py = cy * Cell;
                    if (pixels[py * TexW + px].a < 140) continue;
                    int i0 = verts.Count;
                    AddCorner(verts, uvs, normals, px, py);
                    AddCorner(verts, uvs, normals, px + Cell, py);
                    AddCorner(verts, uvs, normals, px + Cell, py + Cell);
                    AddCorner(verts, uvs, normals, px, py + Cell);
                    tris.Add(i0);
                    tris.Add(i0 + 2);
                    tris.Add(i0 + 1);
                    tris.Add(i0);
                    tris.Add(i0 + 3);
                    tris.Add(i0 + 2);
                }
            }
            if (tris.Count == 0) return;

            mesh = new Mesh { name = bay ? "BoscaliSummer.RuinBay" : "BoscaliSummer.Breach" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetNormals(normals);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();

            var tex = new Texture2D(TexW, TexH, TextureFormat.RGBA32, false)
            {
                name = mesh.name,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            tex.SetPixels32(pixels);
            tex.Apply(false, true);

            material = new Material(shader) { name = mesh.name, color = Color.white };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", tex);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", tex);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.1f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
        }

        private static void AddCorner(List<Vector3> verts, List<Vector2> uvs, List<Vector3> normals, int px, int py)
        {
            float u = px / (float)(TexW - 1);
            float v = py / (float)(TexH - 1);
            verts.Add(new Vector3(u - 0.5f, v - 0.5f, 0f));
            uvs.Add(new Vector2(u, v));
            normals.Add(Vector3.forward);
        }
    }
}
