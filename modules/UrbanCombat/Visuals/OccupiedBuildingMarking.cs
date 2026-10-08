using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    // Local decoration follows the vanilla networked emplacement, including late join.
    // No colliders, weapon scripts, lights, or replicated cosmetic objects. Reads as a
    // dug-in rooftop position: faction banners hung down the long facades, sandbag
    // parapets on the roof corners, a ring gun pit under camo netting, crates and a mast.
    internal sealed class OccupiedBuildingMarking : MonoBehaviour
    {
        private GameObject root;
        private readonly List<Mesh> meshes = new List<Mesh>(8);
        private readonly List<Material> materials = new List<Material>(2);
        private readonly List<GameObject> banners = new List<GameObject>(2);
        private Material bannerMaterial;
        private Material netMaterial;
        private float shellDamage;
        private Building building;
        private UnitPart dugoutPart;
        private Transform shellRoot;
        private float nextCheck;
        private string bannerIdentity;
        private FactionHQ bannerOwner;
        private float bannerIdentityAt;
        private int lastStage = -1;

        private static readonly Color Sand = new Color(0.50f, 0.45f, 0.32f);
        private static readonly Color Steel = new Color(0.17f, 0.19f, 0.18f);
        private static readonly Color Crate = new Color(0.26f, 0.29f, 0.19f);
        private static readonly Color Net = new Color(0.15f, 0.17f, 0.10f);
        private static readonly Color Char = new Color(0.12f, 0.11f, 0.10f);

        public static OccupiedBuildingMarking Apply(GameObject target, FactionHQ owner)
        {
            if (target == null) return null;
            var marking = target.GetComponent<OccupiedBuildingMarking>();
            if (marking == null) marking = target.AddComponent<OccupiedBuildingMarking>();
            if (marking.root == null) marking.Setup(owner);
            return marking;
        }

        private static Shader cachedShader;
        private static Material sharedSand, sharedSteel, sharedCrate;

        private static Shader SharedShader
        {
            get
            {
                // URP Lit only when URP is the active pipeline; it renders magenta otherwise.
                if (cachedShader == null && UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null)
                    cachedShader = Shader.Find("Universal Render Pipeline/Lit");
                if (cachedShader == null) cachedShader = Shader.Find("Standard");
                return cachedShader;
            }
        }

        private static Material Shared(ref Material slot, Color color)
        {
            if (slot == null) slot = NewMaterial(color);
            return slot;
        }

        private static Material NewMaterial(Color color)
        {
            Shader shader = SharedShader;
            if (shader == null) return null;
            var material = new Material(shader) { color = color, name = "BoscaliSummer.Fortification" };
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.05f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.05f);
            return material;
        }

        private Material OwnedMaterial(Color color)
        {
            Material material = NewMaterial(color);
            if (material != null) materials.Add(material);
            return material;
        }

        public void SetShellDamage(float fraction)
        {
            shellDamage = Mathf.Clamp01(fraction);
        }

        private void Setup(FactionHQ owner)
        {
            if (Application.isBatchMode) return;
            building = GetComponent<Building>();
            if (building == null || building.disabled) return;
            dugoutPart = ResolveDugout(building);
            Material sand = Shared(ref sharedSand, Sand);
            Material steel = Shared(ref sharedSteel, Steel);
            Material crate = Shared(ref sharedCrate, Crate);
            // White base: the baked banner texture already carries field/device/border colour.
            bannerMaterial = OwnedMaterial(Color.white);
            netMaterial = OwnedMaterial(Net);
            if (sand == null || steel == null || crate == null || bannerMaterial == null || netMaterial == null)
            {
                CleanUp();
                return;
            }
            ApplyBanner(owner);
            root = new GameObject("BoscaliSummer.OccupiedRoof");
            root.transform.SetParent(transform, false);
            var definition = building.definition as BuildingDefinition;
            float pit = Mathf.Max(2.3f, Mathf.Max(definition?.width ?? 4f, definition?.length ?? 4f) * 0.5f + 0.4f);
            uint seed = Hash(building.NetworkUniqueName);

            var bags = new Geometry();
            var hardware = new Geometry();
            var crates = new Geometry();
            AddGunPit(bags, pit);
            AddCamoNet(hardware, seed, pit);
            AddSupplies(hardware, crates, seed, pit);

            if (GarrisonMarkerInfo.TryParse(building.NetworkUniqueName,
                out float minX, out float maxX, out float minZ, out float maxZ))
            {
                if (Probe(Vector3.up, Vector3.down, 4f, out RaycastHit roof)) shellRoot = roof.transform.root;
                AddRoofWorks(bags, minX, maxX, minZ, maxZ);
            }
            else
                AddPoleFlag(hardware, pit);

            AddMesh("Sandbags", bags, sand);
            AddMesh("Hardware", hardware, steel);
            AddMesh("Crates", crates, crate);
            ApplyStage(0);
        }

        // Ring of staggered bags around the weapon, open toward +Z (the firing sector).
        private static void AddGunPit(Geometry bags, float radius)
        {
            const float gap = 0.9f;
            for (int course = 0; course < 3; course++)
            {
                float r = radius - course * 0.06f;
                int count = Mathf.Max(8, Mathf.RoundToInt((2f * Mathf.PI - 2f * gap) * r / 0.74f));
                float step = (2f * Mathf.PI - 2f * gap) / count;
                float start = Mathf.PI * 0.5f + gap + step * (0.5f + (course % 2) * 0.5f);
                int n = count - (course % 2);
                for (int i = 0; i < n; i++)
                {
                    float angle = start + i * step;
                    var center = new Vector3(Mathf.Cos(angle) * r, 0.14f + course * 0.26f, Mathf.Sin(angle) * r);
                    bags.AddBag(center, BagSize, Mathf.PI * 0.5f - angle);
                }
            }
        }

        private static readonly Vector3 BagSize = new Vector3(0.37f, 0.14f, 0.22f);

        // A sagging net over the rear of the pit, tied down behind and propped at the front.
        private void AddCamoNet(Geometry hardware, uint seed, float radius)
        {
            var net = new Geometry();
            const int cells = 8;
            float minX = -radius - 0.7f, maxX = radius + 0.7f;
            float minZ = -radius - 1.4f, maxZ = radius * 0.35f;
            for (int face = 0; face < 2; face++)
            {
                int start = net.Vertices.Count;
                for (int z = 0; z <= cells; z++)
                    for (int x = 0; x <= cells; x++)
                    {
                        float u = x / (float)cells, v = z / (float)cells;
                        // Flat canopy at prop height, slumping between its ties, pulled down at the back.
                        float middle = 1f - (2f * u - 1f) * (2f * u - 1f);
                        float height = 2.2f - 0.45f * middle * Mathf.Sin(v * Mathf.PI) - 1.5f * (1f - v) * (1f - v) * (1f - v);
                        height += Noise(seed, x, z) * 0.18f + (face == 0 ? 0.01f : -0.01f);
                        net.Vertices.Add(new Vector3(Mathf.Lerp(minX, maxX, u), height, Mathf.Lerp(minZ, maxZ, v)));
                        net.Uvs.Add(new Vector2(u, v));
                        if (x == cells || z == cells) continue;
                        int a = start + z * (cells + 1) + x, b = a + 1, c = a + cells + 1, d = c + 1;
                        if (face == 0) net.Quad(a, c, d, b); else net.Quad(a, b, d, c);
                    }
            }
            AddMesh("CamoNet", net, netMaterial);
            hardware.AddPole(new Vector3(minX + 0.15f, 0f, maxZ - 0.1f), 2.35f, 0.045f);
            hardware.AddPole(new Vector3(maxX - 0.15f, 0f, maxZ - 0.1f), 2.35f, 0.045f);
        }

        private static void AddSupplies(Geometry hardware, Geometry crates, uint seed, float radius)
        {
            float back = -radius - 0.9f;
            for (int i = 0; i < 4; i++)
            {
                float jitter = Noise(seed, i, 7);
                bool stacked = i == 3;
                var size = stacked ? new Vector3(0.8f, 0.4f, 0.5f) : new Vector3(1.1f, 0.48f, 0.62f);
                float y = stacked ? 0.68f : size.y * 0.5f;
                float x = stacked ? -0.9f : (i - 1) * 1.25f + jitter * 0.2f;
                crates.AddBox(new Vector3(x, y, back + jitter * 0.15f), size, jitter * 0.35f);
            }
            // Field radio and its whip mast beside the crates.
            var radio = new Vector3(radius + 0.5f, 0f, back + 0.3f);
            hardware.AddBox(radio + new Vector3(0f, 0.25f, 0f), new Vector3(0.45f, 0.5f, 0.3f), 0.4f);
            hardware.AddPole(radio + new Vector3(0.12f, 0.5f, 0f), 4.2f, 0.022f);
        }

        // Shells with a measured roof patch: sandbag corners, mid-edge firing steps and a
        // faction banner hung down each long facade, weighted by bags on the parapet.
        private void AddRoofWorks(Geometry bags, float minX, float maxX, float minZ, float maxZ)
        {
            float spanX = Mathf.Max(6f, maxX - minX);
            float spanZ = Mathf.Max(6f, maxZ - minZ);
            var center = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            const float inset = 0.55f;
            float hx = spanX * 0.5f - inset, hz = spanZ * 0.5f - inset;
            float armX = Mathf.Min(3.2f, spanX * 0.22f), armZ = Mathf.Min(3.2f, spanZ * 0.22f);
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var corner = center + new Vector3(sx * hx, 0f, sz * hz);
                    AddWall(bags, corner, corner - new Vector3(sx * armX, 0f, 0f), 2);
                    AddWall(bags, corner - new Vector3(0f, 0f, sz * 0.45f), corner - new Vector3(0f, 0f, sz * armZ), 2);
                }

            bool alongX = spanX >= spanZ;
            float longSpan = alongX ? spanX : spanZ;
            float width = Mathf.Clamp(longSpan * 0.1f, 2.5f, 5.5f);
            float drop = Mathf.Clamp(width * 2.8f, 6f, 15f);
            int perSide = longSpan >= 30f ? 2 : 1;
            Vector3 along = alongX ? Vector3.right : Vector3.forward;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 outward = alongX ? new Vector3(0f, 0f, side) : new Vector3(side, 0f, 0f);
                Vector3 edge = center + outward * ((alongX ? spanZ : spanX) * 0.5f);
                if (longSpan >= 14f)
                {
                    Vector3 step = edge - outward * inset + along * (longSpan * 0.25f * side);
                    AddWall(bags, step - along * 1.1f, step + along * 1.1f, 1);
                }
                for (int k = 0; k < perSide; k++)
                {
                    Vector3 slot = edge + along * (perSide == 1 ? 0f : (k == 0 ? -0.22f : 0.22f) * longSpan);
                    Vector3 weight = slot - outward * 0.4f;
                    AddWall(bags, weight - along * (width * 0.5f), weight + along * (width * 0.5f), 1);
                    Vector3 face = FacadePoint(slot, outward);
                    AddBanner(face, outward, along, width, WallDrop(face, outward, drop), side);
                }
            }
        }

        // The patch edge sits on or just inside the facade; probe for the wall below it so
        // the banner hangs on the face instead of floating off it or sinking into it.
        private Vector3 FacadePoint(Vector3 edge, Vector3 outward)
        {
            if (Probe(edge + outward * 6f + Vector3.down * 1.5f, -outward, 9f, out RaycastHit hit) &&
                (shellRoot == null || hit.transform.root == shellRoot))
            {
                Vector3 local = transform.InverseTransformPoint(hit.point);
                return new Vector3(local.x, edge.y, local.z) + outward * 0.12f;
            }
            return edge + outward * 0.5f;
        }

        // Never let a banner reach the street: keep it to the upper 60 % of the wall.
        private float WallDrop(Vector3 face, Vector3 outward, float drop)
        {
            if (Probe(face + outward * 0.6f + Vector3.down * 0.5f, Vector3.down, 200f, out RaycastHit hit))
                drop = Mathf.Min(drop, Mathf.Max(2.5f, hit.distance * 0.6f));
            return drop;
        }

        // Setup-only raycast in nest-local space that skips the nest's own colliders.
        private bool Probe(Vector3 localOrigin, Vector3 localDirection, float distance, out RaycastHit nearest)
        {
            nearest = default;
            RaycastHit[] hits = Physics.RaycastAll(transform.TransformPoint(localOrigin),
                transform.TransformDirection(localDirection), distance, ~0, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < hits.Length; i++)
                if (!hits[i].transform.IsChildOf(transform) && (!found || hits[i].distance < nearest.distance))
                {
                    nearest = hits[i];
                    found = true;
                }
            return found;
        }

        private void AddBanner(Vector3 top, Vector3 outward, Vector3 along, float width, float drop, int side)
        {
            var cloth = new Geometry();
            const int columns = 6, rows = 10;
            Vector3 origin = top + Vector3.up * 0.55f - along * (width * 0.5f);
            for (int face = 0; face < 2; face++)
            {
                int start = cloth.Vertices.Count;
                for (int r = 0; r <= rows; r++)
                    for (int c = 0; c <= columns; c++)
                    {
                        float u = c / (float)columns, v = r / (float)rows;
                        float ripple = Mathf.Sin(u * Mathf.PI * 2.5f + side) * 0.06f * v + 0.04f * v * v
                            + (face == 0 ? 0.008f : -0.008f);
                        cloth.Vertices.Add(origin + along * (u * width) + Vector3.down * (v * drop) + outward * ripple);
                        // The banner texture is landscape; turn it so its hoist runs along the top.
                        cloth.Uvs.Add(new Vector2(v, u));
                        if (r == rows || c == columns) continue;
                        int a = start + r * (columns + 1) + c, b = a + 1, d = a + columns + 1, e = d + 1;
                        if (face == 0) cloth.Quad(a, b, e, d); else cloth.Quad(a, d, e, b);
                    }
            }
            banners.Add(AddMesh("Banner", cloth, bannerMaterial));
        }

        // Nests without a measured roof keep a short mast and flag beside the pit.
        private void AddPoleFlag(Geometry hardware, float radius)
        {
            var pole = new Vector3(-radius - 0.4f, 0f, -radius - 0.4f);
            hardware.AddPole(pole, 4.6f, 0.05f);
            var cloth = new Geometry();
            for (int face = 0; face < 2; face++)
            {
                int start = cloth.Vertices.Count;
                for (int x = 0; x <= 6; x++)
                {
                    float t = x / 6f;
                    float fold = Mathf.Sin(t * Mathf.PI * 2f) * 0.1f * t + (face == 0 ? 0.008f : -0.008f);
                    cloth.Vertices.Add(pole + new Vector3(t * 1.9f, 4.5f - t * 0.12f, fold));
                    cloth.Vertices.Add(pole + new Vector3(t * 1.9f, 3.4f - t * 0.12f, fold));
                    cloth.Uvs.Add(new Vector2(t, 1f)); cloth.Uvs.Add(new Vector2(t, 0f));
                    if (x == 6) continue;
                    int a = start + x * 2;
                    if (face == 0) cloth.Quad(a, a + 2, a + 3, a + 1); else cloth.Quad(a, a + 1, a + 3, a + 2);
                }
            }
            banners.Add(AddMesh("Flag", cloth, bannerMaterial));
        }

        private static void AddWall(Geometry bags, Vector3 from, Vector3 to, int courses)
        {
            Vector3 run = to - from;
            float length = run.magnitude;
            if (length < 0.5f) return;
            float yaw = Mathf.Atan2(run.x, run.z) - Mathf.PI * 0.5f;
            int count = Mathf.Max(1, Mathf.RoundToInt(length / 0.74f));
            for (int course = 0; course < courses; course++)
                for (int i = 0; i < count - course; i++)
                {
                    Vector3 center = from + run * ((i + 0.5f + course * 0.5f) / count);
                    center.y = 0.14f + course * 0.26f;
                    bags.AddBag(center, BagSize, yaw);
                }
        }

        private static uint Hash(string text)
        {
            uint h = 2166136261;
            if (text != null) for (int i = 0; i < text.Length; i++) h = (h ^ text[i]) * 16777619;
            return h;
        }

        private static float Noise(uint seed, int x, int z)
        {
            uint h = seed ^ (uint)(x * 73856093) ^ (uint)(z * 19349663);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xFFFF) / 32767.5f - 1f;
        }

        private static readonly List<OccupiedBuildingMarking> live = new List<OccupiedBuildingMarking>();
        private static MarkingDriver driver;
        private static int nextSlot;
        private bool ticked;

        // No per-instance Update: one static driver ticks every live marking (each still runs
        // its 0.5 s check; a native-to-managed call per building per frame bought nothing).
        private void OnEnable()
        {
            if (!live.Contains(this)) live.Add(this);
            if (driver == null)
            {
                var go = new GameObject("OccupiedMarkingDriver") { hideFlags = HideFlags.HideAndDontSave };
                DontDestroyOnLoad(go);
                driver = go.AddComponent<MarkingDriver>();
            }
        }

        private void OnDisable() => live.Remove(this);

        private sealed class MarkingDriver : MonoBehaviour
        {
            private void Update()
            {
                float now = Time.unscaledTime;
                for (int i = 0; i < live.Count; i++)
                {
                    OccupiedBuildingMarking marking = live[i];
                    if (marking == null) { live.RemoveAt(i--); continue; }
                    int before = live.Count;
                    marking.Tick(now);
                    if (live.Count < before) i -= before - live.Count;
                }
            }
        }

        private void Tick(float now)
        {
            if (now < nextCheck) return;
            // Buildings marked in the same burst would all tick on one frame every 0.5 s; the
            // first tick pushes each into one of ten 50 ms bins so the cohort spreads out.
            nextCheck = now + 0.5f + (ticked ? 0f : (nextSlot++ % 10) * 0.05f);
            ticked = true;
            if (building == null || building.disabled)
            {
                CleanUp();
                enabled = false;
                return;
            }
            if (bannerMaterial == null) return;
            // Every peer derives the same battle state from the replicated dugout-carrier HP;
            // nests without a carrier keep the server-pushed shell damage instead.
            if (dugoutPart != null)
                shellDamage = StrongpointHitPolicy.DugoutStage(dugoutPart.hitPoints) / 3f;
            FactionHQ owner = building.NetworkHQ;
            if (owner != bannerOwner || (now >= bannerIdentityAt && FactionBannerTexture.Identity(owner) != bannerIdentity))
                ApplyBanner(owner);
            ApplyStage(shellDamage >= 0.75f ? 3 : shellDamage >= 0.5f ? 2 : shellDamage >= 0.25f ? 1 : 0);
        }

        // Each strongpoint hit chars the cloth further; the last one cuts the banners down.
        private void ApplyStage(int stage)
        {
            if (stage == lastStage) return;
            lastStage = stage;
            float scorch = stage / 3f;
            bannerMaterial.color = Color.Lerp(Color.white, Char, scorch * 0.7f);
            netMaterial.color = Color.Lerp(Net, Char, scorch * 0.85f);
            for (int i = 0; i < banners.Count; i++)
                if (banners[i] != null) banners[i].SetActive(stage < 3);
        }

        /// <summary>Assigns the cached per-faction banner texture (field + device + border) to
        /// the banner material; re-applied on capture flips when the identity actually changes.</summary>
        private void ApplyBanner(FactionHQ owner)
        {
            if (bannerMaterial == null) return;
            bannerOwner = owner;
            bannerIdentityAt = Time.unscaledTime + 5f;
            bannerIdentity = FactionBannerTexture.Identity(owner);
            Texture2D texture = FactionBannerTexture.Get(bannerIdentity, FactionColor(owner));
            if (bannerMaterial.HasProperty("_BaseMap")) bannerMaterial.SetTexture("_BaseMap", texture);
            if (bannerMaterial.HasProperty("_MainTex")) bannerMaterial.SetTexture("_MainTex", texture);
            bannerMaterial.mainTexture = texture;
        }

        private static UnitPart ResolveDugout(Building nest)
        {
            Transform dugout = nest.transform.Find("dugout");
            if (dugout != null)
            {
                UnitPart part = dugout.GetComponent<UnitPart>();
                if (part != null) return part;
            }
            UnitPart[] parts = nest.GetComponentsInChildren<UnitPart>(true);
            for (int i = 0; i < parts.Length; i++)
                if (parts[i] != null &&
                    parts[i].name.IndexOf("dugout", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return parts[i];
            return null;
        }

        private static Color FactionColor(FactionHQ owner)
        {
            Color color = owner != null && owner.faction != null ? owner.faction.color : new Color(0.75f, 0.65f, 0.3f);
            color.a = 1f;
            return color;
        }

        private GameObject AddMesh(string name, Geometry geometry, Material material)
        {
            if (geometry.Triangles.Count == 0) return null;
            var mesh = new Mesh { name = "BoscaliSummer." + name };
            mesh.SetVertices(geometry.Vertices);
            mesh.SetTriangles(geometry.Triangles, 0);
            mesh.SetUVs(0, geometry.Uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            meshes.Add(mesh);
            var go = new GameObject(name);
            go.transform.SetParent(root.transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = material;
            return go;
        }

        private sealed class Geometry
        {
            public readonly List<Vector3> Vertices = new List<Vector3>(512);
            public readonly List<int> Triangles = new List<int>(1024);
            public readonly List<Vector2> Uvs = new List<Vector2>(512);

            public void Quad(int a, int b, int c, int d)
            {
                Triangles.Add(a); Triangles.Add(b); Triangles.Add(c);
                Triangles.Add(a); Triangles.Add(c); Triangles.Add(d);
            }

            public void AddPole(Vector3 basePosition, float height, float radius)
            {
                int start = Vertices.Count;
                for (int i = 0; i <= 8; i++)
                {
                    float angle = i * Mathf.PI / 4f;
                    Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    Vertices.Add(basePosition + radial); Vertices.Add(basePosition + radial + Vector3.up * height);
                    Uvs.Add(new Vector2(i / 8f, 0f)); Uvs.Add(new Vector2(i / 8f, 1f));
                    if (i == 8) continue;
                    int a = start + i * 2;
                    Quad(a, a + 1, a + 3, a + 2);
                }
            }

            public void AddBox(Vector3 center, Vector3 size, float yaw)
            {
                Quaternion turn = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
                Vector3 half = size * 0.5f;
                // Per-face vertices so RecalculateNormals keeps the edges hard; each (u, v)
                // pair satisfies Cross(u, v) == -normal, which the winding below relies on.
                AddFace(center, turn, half, Vector3.up, Vector3.right, Vector3.forward);
                AddFace(center, turn, half, Vector3.down, Vector3.forward, Vector3.right);
                AddFace(center, turn, half, Vector3.right, Vector3.forward, Vector3.up);
                AddFace(center, turn, half, Vector3.left, Vector3.up, Vector3.forward);
                AddFace(center, turn, half, Vector3.forward, Vector3.up, Vector3.right);
                AddFace(center, turn, half, Vector3.back, Vector3.right, Vector3.up);
            }

            private void AddFace(Vector3 center, Quaternion turn, Vector3 half, Vector3 normal, Vector3 u, Vector3 v)
            {
                int start = Vertices.Count;
                for (int i = 0; i < 4; i++)
                {
                    float su = i == 1 || i == 2 ? 1f : -1f, sv = i >= 2 ? 1f : -1f;
                    Vertices.Add(center + turn * Vector3.Scale(normal + u * su + v * sv, half));
                    Uvs.Add(new Vector2(su * 0.5f + 0.5f, sv * 0.5f + 0.5f));
                }
                Quad(start, start + 3, start + 2, start + 1);
            }

            public void AddBag(Vector3 center, Vector3 size, float yaw)
            {
                Quaternion turn = Quaternion.Euler(0f, yaw * Mathf.Rad2Deg, 0f);
                int start = Vertices.Count;
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
                        Vertices.Add(center + turn * Vector3.Scale(size, sphere));
                        Uvs.Add(new Vector2((float)x / sides, (float)y / rings));
                        if (y == rings || x == sides) continue;
                        int a = start + y * (sides + 1) + x;
                        Triangles.Add(a); Triangles.Add(a + 1); Triangles.Add(a + sides + 1);
                        Triangles.Add(a + 1); Triangles.Add(a + sides + 2); Triangles.Add(a + sides + 1);
                    }
            }
        }

        public void CleanUp()
        {
            if (root != null) { root.SetActive(false); Destroy(root); root = null; }
            foreach (Mesh mesh in meshes) if (mesh != null) Destroy(mesh);
            foreach (Material material in materials) if (material != null) Destroy(material);
            meshes.Clear(); materials.Clear(); banners.Clear();
            bannerMaterial = null; netMaterial = null; bannerIdentity = null; lastStage = -1;
        }

        private void OnDestroy() => CleanUp();
    }
}
