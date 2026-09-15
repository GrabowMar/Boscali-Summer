using System.Collections.Generic;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    // Local decoration follows the vanilla networked emplacement, including late join.
    // No colliders, weapon scripts, lights, or replicated cosmetic objects.
    internal sealed class OccupiedBuildingMarking : MonoBehaviour
    {
        private GameObject root;
        private readonly List<Mesh> meshes = new List<Mesh>(8);
        private readonly List<Material> materials = new List<Material>(6);
        private Material flagMaterial;
        private Material bandMaterial;
        private Building building;
        private float nextCheck;
        private string bannerIdentity;

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
            // White base: the baked banner texture already carries field/device/border colour.
            flagMaterial = CreateMaterial(Color.white);
            if (sand == null || steel == null || flagMaterial == null) { CleanUp(); return; }
            ApplyBanner(owner);
            flagMaterial.EnableKeyword("_EMISSION");
            flagMaterial.SetColor("_EmissionColor", FactionColor(owner) * 0.18f);
            root = new GameObject("BoscaliSummer.OccupiedRoof");
            root.transform.SetParent(transform, false);
            var definition = building.definition as BuildingDefinition;
            float halfX = Mathf.Max(2.5f, (definition?.width ?? 4f) * 0.5f + 0.5f);
            float halfZ = Mathf.Max(2.5f, (definition?.length ?? 4f) * 0.5f + 0.5f);

            AddSandbagNest(sand, halfX, halfZ);

            if (GarrisonMarkerInfo.TryParse(building.NetworkUniqueName,
                out float minX, out float maxX, out float minZ, out float maxZ))
                AddShellMarking(owner, sand, steel, minX, maxX, minZ, maxZ);
            else
                AddNestMarking(sand, steel, halfX, halfZ);
        }

        private void AddSandbagNest(Material sand, float halfX, float halfZ)
        {
            var vertices = new List<Vector3>(2048);
            var triangles = new List<int>(4096);
            var uvs = new List<Vector2>(2048);
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
        }

        private void AddNestMarking(Material sand, Material steel, float halfX, float halfZ)
        {
            Vector3 pole = new Vector3(-halfX, 0f, -halfZ);
            var vertices = new List<Vector3>(64);
            var triangles = new List<int>(128);
            var uvs = new List<Vector2>(64);
            AddPole(vertices, triangles, uvs, pole, 5.1f, 0.065f);
            AddMesh("Flagpole", vertices, triangles, uvs, steel);
            vertices.Clear(); triangles.Clear(); uvs.Clear();
            AddFlag(vertices, triangles, uvs, pole + Vector3.up * 4.8f, 2.8f, 1.6f);
            AddMesh("FactionFlag", vertices, triangles, uvs, flagMaterial);
            // A pale hoist stripe remains legible even for dark faction colours.
            vertices.Clear(); triangles.Clear(); uvs.Clear();
            AddFlag(vertices, triangles, uvs, pole + Vector3.up * 4.8f, 0.35f, 1.6f, 0.016f);
            AddMesh("FlagHoist", vertices, triangles, uvs, sand);
        }

        private void AddShellMarking(FactionHQ owner, Material sand, Material steel,
            float minX, float maxX, float minZ, float maxZ)
        {
            float spanX = Mathf.Max(6f, maxX - minX);
            float spanZ = Mathf.Max(6f, maxZ - minZ);
            float longSpan = Mathf.Max(spanX, spanZ);
            float mastHeight = Mathf.Clamp(longSpan * 0.22f, 6f, 12f);
            float flagWidth = Mathf.Clamp(longSpan * 0.16f, 3f, 7f);
            float flagHeight = flagWidth * 0.57f;
            Vector3 center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            float cornerX = Mathf.Max(1.2f, spanX * 0.5f - 0.4f);
            float cornerZ = Mathf.Max(1.2f, spanZ * 0.5f - 0.4f);
            bool twin = longSpan >= 24f;
            Vector3 first = center + new Vector3(-cornerX, 0f, -cornerZ);
            Vector3 second = center + new Vector3(cornerX, 0f, cornerZ);

            var vertices = new List<Vector3>(256);
            var triangles = new List<int>(512);
            var uvs = new List<Vector2>(256);
            AddPole(vertices, triangles, uvs, first, mastHeight);
            if (twin) AddPole(vertices, triangles, uvs, second, mastHeight);
            AddMesh("Flagpoles", vertices, triangles, uvs, steel);

            vertices.Clear(); triangles.Clear(); uvs.Clear();
            AddFlag(vertices, triangles, uvs, first + Vector3.up * (mastHeight - 0.3f), flagWidth, flagHeight);
            if (twin) AddFlag(vertices, triangles, uvs, second + Vector3.up * (mastHeight - 0.3f), flagWidth, flagHeight);
            AddMesh("FactionFlags", vertices, triangles, uvs, flagMaterial);

            vertices.Clear(); triangles.Clear(); uvs.Clear();
            AddFlag(vertices, triangles, uvs, first + Vector3.up * (mastHeight - 0.3f), 0.35f, flagHeight, 0.016f);
            if (twin) AddFlag(vertices, triangles, uvs, second + Vector3.up * (mastHeight - 0.3f), 0.35f, flagHeight, 0.016f);
            AddMesh("FlagHoists", vertices, triangles, uvs, sand);

            Color faction = FactionColor(owner);
            bandMaterial = CreateMaterial(faction);
            var accentMaterial = CreateMaterial(ViewerAccent(owner));
            if (bandMaterial == null) return;
            bandMaterial.EnableKeyword("_EMISSION");
            bandMaterial.SetColor("_EmissionColor", faction * 0.35f);
            if (accentMaterial != null)
            {
                accentMaterial.EnableKeyword("_EMISSION");
                accentMaterial.SetColor("_EmissionColor", ViewerAccent(owner) * 0.25f);
            }

            bool alongX = spanX >= spanZ;
            const float bandHeight = 0.9f;
            const float inset = 0.35f;
            float bandY = bandHeight * 0.5f + 0.05f;
            float bandLength = Mathf.Max(4f, (alongX ? spanX : spanZ) - inset * 2f);
            Vector3 bandSize = alongX ? new Vector3(bandLength, bandHeight, 0.09f) : new Vector3(0.09f, bandHeight, bandLength);
            Vector3 stripeSize = alongX ? new Vector3(bandLength, 0.28f, 0.15f) : new Vector3(0.15f, 0.28f, bandLength);
            float edgeX = Mathf.Max(0.6f, spanX * 0.5f - inset);
            float edgeZ = Mathf.Max(0.6f, spanZ * 0.5f - inset);
            Vector3 edgeA = center + (alongX ? new Vector3(0f, 0f, -edgeZ) : new Vector3(-edgeX, 0f, 0f));
            Vector3 edgeB = center + (alongX ? new Vector3(0f, 0f, edgeZ) : new Vector3(edgeX, 0f, 0f));

            vertices.Clear(); triangles.Clear(); uvs.Clear();
            AddBox(vertices, triangles, uvs, edgeA + Vector3.up * bandY, bandSize);
            AddBox(vertices, triangles, uvs, edgeB + Vector3.up * bandY, bandSize);
            AddMesh("RoofBand", vertices, triangles, uvs, bandMaterial);

            if (accentMaterial == null) return;
            vertices.Clear(); triangles.Clear(); uvs.Clear();
            AddBox(vertices, triangles, uvs, edgeA + Vector3.up * bandY, stripeSize);
            AddBox(vertices, triangles, uvs, edgeB + Vector3.up * bandY, stripeSize);
            AddMesh("RoofBandStripe", vertices, triangles, uvs, accentMaterial);
        }

        private static Color ViewerAccent(FactionHQ owner)
        {
            if (GameAssets.i == null) return new Color(0.92f, 0.9f, 0.78f);
            if (owner != null && GameManager.GetLocalPlayer<Player>(out Player local) &&
                local != null && local.HQ != null)
                return local.HQ == owner ? GameAssets.i.HUDFriendly : GameAssets.i.HUDHostile;
            return GameAssets.i.HUDNeutral;
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
                FactionHQ owner = building.NetworkHQ;
                Color color = FactionColor(owner);
                if (FactionBannerTexture.Identity(owner) != bannerIdentity) ApplyBanner(owner);
                flagMaterial.SetColor("_EmissionColor", color * 0.18f);
                if (bandMaterial != null)
                {
                    bandMaterial.color = color;
                    bandMaterial.SetColor("_EmissionColor", color * 0.35f);
                }
            }
        }

        /// <summary>Assigns the cached per-faction banner texture (field + device + border) to
        /// the flag material; re-applied on capture flips when the identity actually changes.</summary>
        private void ApplyBanner(FactionHQ owner)
        {
            if (flagMaterial == null) return;
            bannerIdentity = FactionBannerTexture.Identity(owner);
            Texture2D texture = FactionBannerTexture.Get(bannerIdentity, FactionColor(owner));
            if (flagMaterial.HasProperty("_BaseMap")) flagMaterial.SetTexture("_BaseMap", texture);
            if (flagMaterial.HasProperty("_MainTex")) flagMaterial.SetTexture("_MainTex", texture);
            flagMaterial.mainTexture = texture;
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

        private static void AddPole(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs,
            Vector3 basePosition, float height, float radius = 0.075f)
        {
            int start = vertices.Count;
            for (int i = 0; i <= 8; i++)
            {
                float angle = i * Mathf.PI / 4f;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                vertices.Add(basePosition + radial); vertices.Add(basePosition + radial + Vector3.up * height);
                uvs.Add(new Vector2(i / 8f, 0f)); uvs.Add(new Vector2(i / 8f, 1f));
                if (i == 8) continue;
                int a = start + i * 2;
                triangles.Add(a); triangles.Add(a + 1); triangles.Add(a + 2);
                triangles.Add(a + 1); triangles.Add(a + 3); triangles.Add(a + 2);
            }
        }

        private static void AddBox(List<Vector3> vertices, List<int> triangles, List<Vector2> uvs,
            Vector3 center, Vector3 size)
        {
            int start = vertices.Count;
            Vector3 half = size * 0.5f;
            vertices.Add(center + new Vector3(-half.x, -half.y, -half.z));
            vertices.Add(center + new Vector3(half.x, -half.y, -half.z));
            vertices.Add(center + new Vector3(half.x, -half.y, half.z));
            vertices.Add(center + new Vector3(-half.x, -half.y, half.z));
            vertices.Add(center + new Vector3(-half.x, half.y, -half.z));
            vertices.Add(center + new Vector3(half.x, half.y, -half.z));
            vertices.Add(center + new Vector3(half.x, half.y, half.z));
            vertices.Add(center + new Vector3(-half.x, half.y, half.z));
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 0f)); uvs.Add(new Vector2(0f, 0f));
            uvs.Add(new Vector2(0f, 1f)); uvs.Add(new Vector2(1f, 1f));
            uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
            int[] faces =
            {
                0, 4, 1, 1, 4, 5,
                2, 6, 3, 3, 6, 7,
                4, 7, 5, 5, 7, 6,
                0, 3, 4, 4, 3, 7,
                1, 5, 2, 2, 5, 6,
                0, 1, 3, 3, 1, 2
            };
            for (int i = 0; i < faces.Length; i++) triangles.Add(start + faces[i]);
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
            meshes.Clear(); materials.Clear(); flagMaterial = null; bandMaterial = null; bannerIdentity = null;
        }

        private void OnDestroy() => CleanUp();
    }
}
