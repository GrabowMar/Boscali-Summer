using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    // Local decoration follows the vanilla networked emplacement, including late join.
    // No colliders, weapon scripts, lights, or replicated cosmetic objects.
    internal sealed class OccupiedBuildingMarking : MonoBehaviour
    {
        private GameObject root;
        private readonly List<Mesh> meshes = new List<Mesh>(4);
        private readonly List<Material> materials = new List<Material>(3);
        private Material flagMaterial;
        private Building building;
        private float nextCheck;

        public static OccupiedBuildingMarking Apply(GameObject target, FactionHQ owner)
        {
            if (target == null) return null;
            var marking = target.GetComponent<OccupiedBuildingMarking>();
            if (marking == null) marking = target.AddComponent<OccupiedBuildingMarking>();
            if (marking.root == null) marking.Setup(owner);
            return marking;
        }

        private Material CreateMaterial(Color color)
        {
            Shader shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return null;
            var material = new Material(shader) { color = color, name = "BoscaliSummer.Fortification" };
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.05f);
            materials.Add(material);
            return material;
        }

        private void Setup(FactionHQ owner)
        {
            building = GetComponent<Building>();
            if (building == null || building.disabled) return;
            var sand = CreateMaterial(new Color(0.48f, 0.43f, 0.30f));
            var steel = CreateMaterial(new Color(0.18f, 0.21f, 0.19f));
            flagMaterial = CreateMaterial(FactionColor(owner));
            if (sand == null || steel == null || flagMaterial == null) { CleanUp(); return; }
            flagMaterial.EnableKeyword("_EMISSION");
            flagMaterial.SetColor("_EmissionColor", FactionColor(owner) * 0.18f);
            root = new GameObject("BoscaliSummer.OccupiedRoof");
            root.transform.SetParent(transform, false);
            var definition = building.definition as BuildingDefinition;
            float halfX = Mathf.Max(2.5f, (definition?.width ?? 4f) * 0.5f + 0.5f);
            float halfZ = Mathf.Max(2.5f, (definition?.length ?? 4f) * 0.5f + 0.5f);

            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var uvs = new List<Vector2>();
            // Three short walls leave the forward firing sector clear. Two staggered
            // courses, 36 rounded bags, combined into one renderer per emplacement.
            for (int course = 0; course < 2; course++)
                for (int wall = 0; wall < 3; wall++)
                    for (int bag = 0; bag < 6; bag++)
                    {
                        float along = (bag - 2.5f) * 0.76f + course * 0.18f;
                        Vector3 center = wall == 0 ? new Vector3(along, 0f, -halfZ) :
                            new Vector3(wall == 1 ? -halfX : halfX, 0f, along);
                        center.y = 0.22f + course * 0.36f;
                        Vector3 size = wall == 0 ? new Vector3(0.48f, 0.24f, 0.31f) :
                            new Vector3(0.31f, 0.24f, 0.48f);
                        AddBag(vertices, triangles, uvs, center, size);
                    }
            AddMesh("SandbagCover", vertices, triangles, uvs, sand);

            Vector3 pole = new Vector3(-halfX, 0f, -halfZ);
            vertices.Clear(); triangles.Clear(); uvs.Clear();
            for (int i = 0; i <= 8; i++)
            {
                float angle = i * Mathf.PI / 4f;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 0.065f;
                vertices.Add(pole + radial); vertices.Add(pole + radial + Vector3.up * 5.1f);
                uvs.Add(new Vector2(i / 8f, 0f)); uvs.Add(new Vector2(i / 8f, 1f));
                if (i == 8) continue;
                int a = i * 2;
                triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + 2);
                triangles.Add(a + 1); triangles.Add(a + 3); triangles.Add(a + 2);
            }
            AddMesh("Flagpole", vertices, triangles, uvs, steel);
            vertices.Clear(); triangles.Clear(); uvs.Clear();
            AddFlag(vertices, triangles, uvs, pole + Vector3.up * 4.8f, 2.8f, 1.6f);
            AddMesh("FactionFlag", vertices, triangles, uvs, flagMaterial);
            // A pale hoist stripe remains legible even for dark faction colours.
            vertices.Clear(); triangles.Clear(); uvs.Clear();
            AddFlag(vertices, triangles, uvs, pole + Vector3.up * 4.8f, 0.35f, 1.6f, 0.016f);
            AddMesh("FlagHoist", vertices, triangles, uvs, sand);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + 0.5f;
            if (building == null || building.disabled)
            {
                CleanUp();
                enabled = false;
                return;
            }
            if (flagMaterial != null)
            {
                Color color = FactionColor(building.NetworkHQ);
                flagMaterial.color = color;
                flagMaterial.SetColor("_EmissionColor", color * 0.18f);
            }
        }

        private static Color FactionColor(FactionHQ owner)
        {
            Color color = owner != null && owner.faction != null ? owner.faction.color : new Color(0.75f, 0.65f, 0.3f);
            color.a = 1f;
            return color;
        }

        private void AddMesh(string name, List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, Material material)
        {
            var mesh = new Mesh { name = "BoscaliSummer." + name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void AddBag(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs, Vector3 center, Vector3 size)
        {
            int start = vertices.Count;
            const int sides = 8, rings = 6;
            for (int y = 0; y <= rings; y++)
                for (int x = 0; x <= sides; x++)
                {
                    float latitude = Mathf.PI * y / rings;
                    float longitude = 2f * Mathf.PI * x / sides;
                    Vector3 sphere = new Vector3(Mathf.Sin(latitude) * Mathf.Cos(longitude),
                        Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(longitude));
                    // Rounded rectangular sacks rather than stones or perfect spheres.
                    for (int axis = 0; axis < 3; axis++)
                        sphere[axis] = Mathf.Sign(sphere[axis]) * Mathf.Pow(Mathf.Abs(sphere[axis]), 0.55f);
                    vertices.Add(center + Vector3.Scale(size, sphere));
                    uvs.Add(new Vector2((float)x / sides, (float)y / rings));
                    if (y == rings || x == sides) continue;
                    int a = start + y * (sides + 1) + x;
                    triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + sides + 1);
                    triangles.Add(a + 1); triangles.Add(a + sides + 2); triangles.Add(a + sides + 1);
                }
        }

        private static void AddFlag(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs,
            Vector3 top, float width, float height, float thickness = 0.008f)
        {
            // Static folded cloth; double-sided triangles make it readable from either approach.
            for (int side = 0; side < 2; side++)
            {
                int start = vertices.Count;
                for (int x = 0; x <= 6; x++)
                {
                    float t = x / 6f;
                    float clothT = t * width / 2.8f;
                    float fold = Mathf.Sin(clothT * Mathf.PI * 3f) * 0.12f + (side == 0 ? thickness : -thickness);
                    vertices.Add(top + new Vector3(t * width, -clothT * 0.16f, fold));
                    vertices.Add(top + new Vector3(t * width, -height - clothT * 0.16f, fold));
                    uvs.Add(new Vector2(t, 1f)); uvs.Add(new Vector2(t, 0f));
                    if (x == 6) continue;
                    int a = start + x * 2;
                    triangles.Add(a); triangles.Add(a + (side == 0 ? 1 : 2)); triangles.Add(a + (side == 0 ? 2 : 1));
                    triangles.Add(a + 1); triangles.Add(a + (side == 0 ? 3 : 2)); triangles.Add(a + (side == 0 ? 2 : 3));
                }
            }
        }

        public void CleanUp()
        {
            if (root != null) { root.SetActive(false); Destroy(root); root = null; }
            foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh);
            foreach (Material material in materials) if (material != null) Destroy(material);
            meshes.Clear(); materials.Clear(); flagMaterial = null;
        }

        private void OnDestroy() => CleanUp();
    }
}
