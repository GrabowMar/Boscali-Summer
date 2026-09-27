using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;
using BoscaliSummer.Framework.Contracts;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// A client-local display of baked, real mission elevation. DynamicMap still owns the
    /// map image, pan/zoom, symbols and every game action. A missing or mismatched asset
    /// leaves the native map untouched.
    /// </summary>
    internal static class MfdTerrainRelief
    {
        private const string HeightResource = "BoscaliSummer.Command.terrain2_map.bmap";
        private const string StyleResource = "BoscaliSummer.Command.terrain2_intel.png";
        private const string SupportedMap = "terrain2_map";
        private const int Samples = 513;
        private const int TileCells = 128;
        private const int TerrainLayer = 31;
        private const int TextureSize = 1536;
        private const float SeaLevel = -200f;
        private const float ModelWidth = 900f;
        private const float VerticalScale = 3.6f;
        private const float RenderInterval = 0.2f;
        private const int MaximumStems = 64;
        private const int MaximumClusters = 64;
        private const int MaximumClusteredIcons = 2048;
        private const int MinimumStack = 5;
        private const float DefaultYaw = 0f;
        private const float DefaultPitch = 40f;

        private static DynamicMap owner;
        private static MapSettings source;
        private static Sprite sprite;
        private static string mapAssetName;
        private static float[] heights;
        private static bool[] land;
        private static bool ready;
        private static bool unavailable;
        private static float nextRender;
        private static GameObject sceneRoot;
        private static readonly List<Mesh> meshes = new List<Mesh>(18);
        private static readonly List<MeshCollider> colliders = new List<MeshCollider>(16);
        private static readonly List<MeshRenderer> controlRenderers = new List<MeshRenderer>(16);
        private static readonly List<MeshRenderer> threatRenderers = new List<MeshRenderer>(16);
        private static readonly Dictionary<GameObject, bool> nativeGrid = new Dictionary<GameObject, bool>();
        private sealed class StemMark { internal Image Line, Foot; }
        private static readonly Dictionary<MapIcon, StemMark> stems = new Dictionary<MapIcon, StemMark>();
        private sealed class HeadingMark { internal float Native, Projected; }
        private static readonly Dictionary<UnitMapIcon, HeadingMark> headings =
            new Dictionary<UnitMapIcon, HeadingMark>();
        private sealed class IconFix { internal float X, Z, Depth; internal Vector3 NativeScale; }
        private static readonly Dictionary<MapIcon, IconFix> iconFixes =
            new Dictionary<MapIcon, IconFix>();
        private static readonly List<MapIcon> staleIconFixes = new List<MapIcon>(64);
        private sealed class ClusterMark
        {
            internal readonly List<Image> Members = new List<Image>(8);
            internal Vector2 Screen;
        }
        private sealed class CountMark { internal RectTransform Root; internal Text Text; }
        private static readonly Dictionary<(int, int, int), int> clusterIndex =
            new Dictionary<(int, int, int), int>();
        private static readonly List<ClusterMark> clusters = new List<ClusterMark>(MaximumClusters);
        private static readonly List<CountMark> counts = new List<CountMark>(MaximumClusters);
        private static readonly List<Image> hiddenIcons = new List<Image>(MaximumClusteredIcons);
        private static Material groundMaterial;
        private static Material controlMaterial;
        private static Material threatMaterial;
        private static Material gridMaterial;
        private static Texture2D styleTexture;
        private static Camera camera;
        private static RenderTexture texture;
        private static RawImage view;
        private static Image nativeImage;
        private static RawImage controlImage;
        private static bool controlWasEnabled;
        private static RawImage threatImage;
        private static bool threatWasEnabled;
        private static float yaw = DefaultYaw;
        private static float pitch = DefaultPitch;
        private static int viewRevision;
        private static int hoveredStackCount;
        private static Vector2 fittedViewport;
        private static float coverageScale;

        private static bool Requested => DynamicMap.mapMaximized && MfdRailPatch.IsApplied &&
            Plugin.Settings?.Command?.MapRelief3D?.Value == true &&
            Plugin.Settings?.Command?.MapTerrainImage?.Value == true;

        internal static bool IsDrawing => ready && camera != null && view != null && view.isActiveAndEnabled;
        internal static float Yaw => yaw;
        internal static float Pitch => pitch;
        internal static int ViewRevision => viewRevision;
        internal static int HoveredStackCount => hoveredStackCount;

        internal static void Rotate(float yawDelta, float pitchDelta)
        {
            float nextYaw = Mathf.Repeat(yaw + yawDelta + 180f, 360f) - 180f;
            float nextPitch = Mathf.Clamp(pitch + pitchDelta, 25f, 70f);
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, nextYaw)) < .01f &&
                Mathf.Abs(pitch - nextPitch) < .01f) return;
            yaw = nextYaw;
            pitch = nextPitch;
            PlaceCamera();
            viewRevision++;
            nextRender = 0f;
        }

        internal static void ResetOrbit() => Rotate(-yaw, DefaultPitch - pitch);

        private static void PlaceCamera()
        {
            if (camera == null || sceneRoot == null) return;
            const float radius = 1122f;
            float angle = pitch * Mathf.Deg2Rad;
            camera.transform.localPosition = Quaternion.Euler(0f, yaw, 0f) *
                new Vector3(0f, Mathf.Sin(angle) * radius, -Mathf.Cos(angle) * radius);
            camera.transform.LookAt(sceneRoot.transform.position);
        }

        internal static void Tick()
        {
            if (!Requested)
            {
                Restore();
                return;
            }

            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || map.mapImage == null) return;
            Image image = map.mapImage.GetComponent<Image>();
            if (image == null || image.sprite == null)
            {
                Restore();
                return;
            }
            if (owner != null && (owner != map || sprite != image.sprite)) Reset();
            owner = map;
            sprite = image.sprite;
            if (unavailable) return;

            if (!ready && !LoadHeightfield()) return;
            if (sceneRoot == null && !BuildScene()) return;
            if (view == null || view.transform.parent != map.mapImage.transform)
            {
                var ui = new GameObject("NOAvionics.IntelligenceTerrain", typeof(RectTransform), typeof(RawImage));
                ui.transform.SetParent(map.mapImage.transform, false);
                view = ui.GetComponent<RawImage>();
                AvKit.Stretch(view.rectTransform);
                view.raycastTarget = false;
                view.texture = texture;
                view.transform.SetAsFirstSibling();
            }

            nativeImage = image;
            Color imageColor = image.color;
            imageColor.a = 0f;
            image.color = imageColor;
            image.enabled = true;
            view.color = new Color(1f, 1f, 1f,
                Mathf.Clamp01(Plugin.Settings.Command.MapTerrainOpacity.Value));
            if (view.transform.GetSiblingIndex() != 0) view.transform.SetAsFirstSibling();

            FitToViewport(map);
            FitTerrainCoverage(map);
            HideNativeGrid();
            MfdMapOrbitControls.Tick(map);
            CaptureFieldLayers();
            ReprojectCachedIcons();
            DeclutterIcons();
            if (Time.unscaledTime < nextRender) return;
            nextRender = Time.unscaledTime + RenderInterval;
            camera.Render();
        }

        private static void FitToViewport(DynamicMap map)
        {
            if (map?.mapBackground == null || map.mapImage == null) return;
            RectTransform viewport = map.mapBackground.rectTransform;
            RectTransform imageRect = map.mapImage.GetComponent<RectTransform>();
            if (imageRect == null || imageRect.rect.width <= 0f || imageRect.rect.height <= 0f) return;
            Vector2 size = viewport.rect.size;
            if (size.x <= 0f || size.y <= 0f ||
                (size - fittedViewport).sqrMagnitude < 64f) return;
            fittedViewport = size;

            // The expanded frame is wider than the native 900-unit image. Zoom that image
            // through DynamicMap so the relief, icons and native cursor keep one transform.
            // Only fit on mount or a real viewport resize; player zoom remains free afterward.
            float cover = Mathf.Max((size.x - 20f) / imageRect.rect.width,
                (size.y - 20f) / imageRect.rect.height) * 1.04f;
            float currentImageScale = map.mapImage.transform.localScale.x;
            if (cover > 1.05f && currentImageScale > 0f &&
                currentImageScale < cover - 0.01f)
            {
                float target = Mathf.Clamp(map.GetZoomLevel() * cover / currentImageScale, 1f, 4f);
                map.SetZoomLevel(target);
            }
            coverageScale = map.mapImage.transform.localScale.x;
        }

        private static void FitTerrainCoverage(DynamicMap map)
        {
            if (camera == null || coverageScale <= 0f || map?.mapBackground == null) return;
            RectTransform imageRect = map.mapImage.GetComponent<RectTransform>();
            if (imageRect == null || imageRect.rect.width <= 0f || imageRect.rect.height <= 0f) return;

            // A rotated square of terrain otherwise leaves sharp empty wedges inside the
            // expanded map. Frame the projected ground so its four viewport corners land
            // on terrain. Keep the initial image scale as the reference so later player
            // zoom is not cancelled by the relief camera.
            float visibleX = map.mapBackground.rectTransform.rect.width /
                (imageRect.rect.width * coverageScale);
            float visibleY = map.mapBackground.rectTransform.rect.height /
                (imageRect.rect.height * coverageScale * Mathf.Sin(pitch * Mathf.Deg2Rad));
            float c = Mathf.Abs(Mathf.Cos(yaw * Mathf.Deg2Rad));
            float s = Mathf.Abs(Mathf.Sin(yaw * Mathf.Deg2Rad));
            float span = Mathf.Max(visibleX * c + visibleY * s,
                visibleX * s + visibleY * c);
            float size = Mathf.Clamp((ModelWidth * .5f - 12f) / span, 180f, 440f);
            if (Mathf.Abs(camera.orthographicSize - size) < .25f) return;
            camera.orthographicSize = size;
            viewRevision++;
            nextRender = 0f;
        }

        private static bool LoadHeightfield()
        {
            source = Object.FindObjectOfType<MapSettings>();
            string textureName = sprite.texture != null ? sprite.texture.name : null;
            mapAssetName = string.Equals(textureName, SupportedMap, StringComparison.OrdinalIgnoreCase)
                ? SupportedMap : SafeAssetName(sprite.name) ? sprite.name :
                SafeAssetName(textureName) ? textureName : null;
            if (source == null || source.MapSize.x <= 0f || source.MapSize.y <= 0f ||
                mapAssetName == null)
                return Unavailable("No matching baked terrain; using the native map.");
            string external = MapAssetPath(mapAssetName + ".bmap");
            bool custom = File.Exists(external);
            bool bundled = string.Equals(mapAssetName, SupportedMap, StringComparison.OrdinalIgnoreCase);
            if (!custom && !bundled)
                return Unavailable("No matching baked terrain; using the native map.");
            try
            {
                using (Stream stream = custom ? File.OpenRead(external) :
                    typeof(MfdTerrainRelief).Assembly.GetManifestResourceStream(HeightResource))
                {
                    if (stream == null || stream.Length != 16 + Samples * Samples * 2)
                        return Unavailable("Terrain asset missing or invalid; using the native map.");
                    using (var input = new BinaryReader(stream))
                    {
                        uint magic = input.ReadUInt32();
                        ushort version = input.ReadUInt16(), side = input.ReadUInt16();
                        float width = input.ReadSingle(), height = input.ReadSingle();
                        if (magic != 0x50414D42 || version != 1 || side != Samples ||
                            float.IsNaN(width) || float.IsInfinity(width) ||
                            float.IsNaN(height) || float.IsInfinity(height) ||
                            Mathf.Abs(width - source.MapSize.x) > 1f ||
                            Mathf.Abs(height - source.MapSize.y) > 1f)
                            return Unavailable("Terrain asset map size differs; using the native map.");
                        heights = new float[Samples * Samples];
                        land = new bool[heights.Length];
                        int landCount = 0;
                        for (int i = 0; i < heights.Length; i++)
                        {
                            ushort encoded = input.ReadUInt16();
                            land[i] = encoded != 0;
                            heights[i] = encoded == 0 ? SeaLevel : SeaLevel + (encoded - 1) * 0.25f;
                            if (land[i]) landCount++;
                        }
                        if (landCount < 10000) return Unavailable("Terrain asset has too little land.");
                    }
                }
            }
            catch (IOException error) { return Unavailable("Terrain asset could not be read: " + error.Message); }
            catch (UnauthorizedAccessException) { return Unavailable("Terrain asset access denied."); }
            ready = true;
            return true;
        }

        private static bool SafeAssetName(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length > 64) return false;
            foreach (char c in name)
                if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') || c == '_' || c == '-')) return false;
            return true;
        }

        private static string MapAssetPath(string file) =>
            Path.Combine(Paths.ConfigPath, "BoscaliSummer", "Maps", file);

        private static bool Unavailable(string reason)
        {
            unavailable = true;
            Plugin.Logger.LogWarning("[MAP] " + reason);
            return false;
        }

        private static bool BuildScene()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return Unavailable("Display shader unavailable; using the native map.");
            if (styleTexture == null) LoadStyle();
            sceneRoot = new GameObject("NOAvionics.IntelligenceTerrainScene");
            sceneRoot.transform.position = new Vector3(0f, -100000f, 0f);
            sceneRoot.layer = TerrainLayer;

            groundMaterial = new Material(shader) { mainTexture = styleTexture != null ? styleTexture : sprite.texture,
                color = new Color(1.25f, 1.28f, 1.27f, 1f) };
            controlMaterial = new Material(shader) { color = new Color(1f, 1f, 1f, 0.22f) };
            threatMaterial = new Material(shader) { color = new Color(1f, 1f, 1f, 0.35f) };
            gridMaterial = new Material(shader) { mainTexture = Texture2D.whiteTexture,
                color = new Color(0.21f, 0.54f, 0.6f, 0.24f) };

            for (int tz = 0; tz < 4; tz++)
            for (int tx = 0; tx < 4; tx++)
            {
                Mesh mesh = MakeTile(tx, tz);
                meshes.Add(mesh);
                var tile = new GameObject("Terrain", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
                tile.layer = TerrainLayer;
                tile.transform.SetParent(sceneRoot.transform, false);
                tile.GetComponent<MeshFilter>().sharedMesh = mesh;
                tile.GetComponent<MeshRenderer>().sharedMaterial = groundMaterial;
                MeshCollider collider = tile.GetComponent<MeshCollider>();
                collider.sharedMesh = mesh;
                colliders.Add(collider);
                controlRenderers.Add(AddFieldTile(mesh, "Control", controlMaterial, 0.12f));
                threatRenderers.Add(AddFieldTile(mesh, "Threat", threatMaterial, 0.22f));
            }

            Mesh grid = MakeGroundGrid();
            meshes.Add(grid);
            var lines = new GameObject("GroundGrid", typeof(MeshFilter), typeof(MeshRenderer));
            lines.layer = TerrainLayer;
            lines.transform.SetParent(sceneRoot.transform, false);
            lines.GetComponent<MeshFilter>().sharedMesh = grid;
            lines.GetComponent<MeshRenderer>().sharedMaterial = gridMaterial;

            var cameraObject = new GameObject("Camera", typeof(Camera));
            cameraObject.transform.SetParent(sceneRoot.transform, false);
            camera = cameraObject.GetComponent<Camera>();
            PlaceCamera();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 440f;
            camera.cullingMask = 1 << TerrainLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.015f, 0.029f, 0.039f, 1f);
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 3000f;
            camera.allowHDR = false;
            camera.allowMSAA = false;
            texture = new RenderTexture(TextureSize, TextureSize, 16, RenderTextureFormat.ARGB32)
            {
                name = "NOAvionics.IntelligenceTerrain",
                filterMode = FilterMode.Bilinear
            };
            camera.targetTexture = texture;
            return true;
        }

        private static void LoadStyle()
        {
            string custom = mapAssetName != null ? MapAssetPath(mapAssetName + "_intel.png") : null;
            bool bundled = string.Equals(mapAssetName, SupportedMap, StringComparison.OrdinalIgnoreCase);
            try
            {
                using (Stream stream = custom != null && File.Exists(custom) ? File.OpenRead(custom) :
                    bundled ? typeof(MfdTerrainRelief).Assembly.GetManifestResourceStream(StyleResource) : null)
                {
                    if (stream == null || stream.Length <= 0 || stream.Length > 8 * 1024 * 1024) return;
                    var bytes = new byte[stream.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int count = stream.Read(bytes, read, bytes.Length - read);
                        if (count <= 0) return;
                        read += count;
                    }
                    var loaded = new Texture2D(2, 2, TextureFormat.RGB24, false);
                    if (!loaded.LoadImage(bytes, true) || loaded.width > 4096 || loaded.height > 4096)
                    { Object.Destroy(loaded); return; }
                    loaded.name = "NOAvionics.IntelligenceTerrainStyle";
                    loaded.filterMode = FilterMode.Bilinear;
                    styleTexture = loaded;
                }
            }
            catch (IOException error) { Plugin.Logger.LogWarning("[MAP] Style asset could not be read: " + error.Message); }
            catch (UnauthorizedAccessException) { Plugin.Logger.LogWarning("[MAP] Style asset access denied."); }
        }

        private static MeshRenderer AddFieldTile(Mesh mesh, string name, Material material, float lift)
        {
            var field = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            field.layer = TerrainLayer;
            field.transform.SetParent(sceneRoot.transform, false);
            field.transform.localPosition = Vector3.up * lift;
            field.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = field.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.enabled = false;
            return renderer;
        }

        private static Mesh MakeTile(int tileX, int tileZ)
        {
            const int side = TileCells + 1;
            float cell = ModelWidth / (Samples - 1);
            Vector4 atlas = styleTexture != null ? new Vector4(0f, 0f, 1f, 1f) :
                DataUtility.GetOuterUV(sprite);
            var vertices = new Vector3[side * side];
            var uv = new Vector2[vertices.Length];
            var colors = new Color32[vertices.Length];
            var triangles = new int[TileCells * TileCells * 6];
            Vector3 light = new Vector3(-0.45f, 0.82f, -0.35f).normalized;
            for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++)
            {
                int gx = tileX * TileCells + x, gz = tileZ * TileCells + z;
                int at = z * side + x;
                vertices[at] = new Vector3(-ModelWidth * .5f + gx * cell,
                    ModelHeight(gx, gz), -ModelWidth * .5f + gz * cell);
                uv[at] = new Vector2(Mathf.Lerp(atlas.x, atlas.z, gx / (float)(Samples - 1)),
                    Mathf.Lerp(atlas.y, atlas.w, gz / (float)(Samples - 1)));
                float dx = (ModelHeight(gx + 1, gz) - ModelHeight(gx - 1, gz)) / (2f * cell);
                float dz = (ModelHeight(gx, gz + 1) - ModelHeight(gx, gz - 1)) / (2f * cell);
                Vector3 normal = new Vector3(-dx, 1f, -dz).normalized;
                byte shade = (byte)Mathf.Clamp(165f + 55f * Vector3.Dot(normal, light), 100f, 230f);
                colors[at] = new Color32(shade, shade, shade, 255);
                if (x == TileCells || z == TileCells) continue;
                int t = (z * TileCells + x) * 6;
                triangles[t] = at;
                triangles[t + 1] = at + side;
                triangles[t + 2] = at + 1;
                triangles[t + 3] = at + 1;
                triangles[t + 4] = at + side;
                triangles[t + 5] = at + side + 1;
            }
            var result = new Mesh { name = "NOAvionics.TerrainTile" };
            result.vertices = vertices;
            result.uv = uv;
            result.colors32 = colors;
            result.triangles = triangles;
            result.RecalculateNormals();
            result.RecalculateBounds();
            return result;
        }

        private static Mesh MakeGroundGrid()
        {
            var vertices = new List<Vector3>(3000);
            var indices = new List<int>(3000);
            for (int grid = -4; grid <= 4; grid++)
            {
                int fixedSample = Mathf.Clamp(Samples / 2 + grid * 56, 0, Samples - 1);
                for (int i = 0; i < Samples - 1; i += 4)
                {
                    Add(fixedSample, i, fixedSample, i + 4);
                    Add(i, fixedSample, i + 4, fixedSample);
                }
            }
            var mesh = new Mesh { name = "NOAvionics.TerrainGrid" };
            mesh.SetVertices(vertices);
            mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;

            void Add(int x0, int z0, int x1, int z1)
            {
                indices.Add(vertices.Count);
                vertices.Add(GridPoint(x0, z0));
                indices.Add(vertices.Count);
                vertices.Add(GridPoint(x1, z1));
            }
        }

        private static Vector3 GridPoint(int x, int z) => new Vector3(
            (x / (float)(Samples - 1) - .5f) * ModelWidth,
            ModelHeight(x, z) + .35f,
            (z / (float)(Samples - 1) - .5f) * ModelWidth);

        private static float ModelHeight(int x, int z) =>
            (heights[Mathf.Clamp(z, 0, Samples - 1) * Samples +
                Mathf.Clamp(x, 0, Samples - 1)] - SeaLevel) *
            VerticalScale * ModelWidth / source.MapSize.x;

        private static float GroundHeight(float worldX, float worldZ)
        {
            float sx = Mathf.Clamp((worldX / source.MapSize.x + .5f) * (Samples - 1), 0f, Samples - 1f);
            float sz = Mathf.Clamp((worldZ / source.MapSize.y + .5f) * (Samples - 1), 0f, Samples - 1f);
            int x = Mathf.Min(Samples - 2, Mathf.FloorToInt(sx));
            int z = Mathf.Min(Samples - 2, Mathf.FloorToInt(sz));
            float south = Mathf.Lerp(ModelHeight(x, z), ModelHeight(x + 1, z), sx - x);
            float north = Mathf.Lerp(ModelHeight(x, z + 1), ModelHeight(x + 1, z + 1), sx - x);
            return Mathf.Lerp(south, north, sz - z);
        }

        internal static bool TryProject(float worldX, float worldZ, Rect rect, out Vector2 point)
        {
            point = default;
            if (!IsDrawing || source == null) return false;
            Vector3 world = sceneRoot.transform.position + new Vector3(
                worldX / source.MapSize.x * ModelWidth,
                GroundHeight(worldX, worldZ) + 1f,
                worldZ / source.MapSize.y * ModelWidth);
            Vector3 screen = camera.WorldToViewportPoint(world);
            if (screen.z <= 0f) return false;
            point = new Vector2(rect.xMin + screen.x * rect.width,
                rect.yMin + screen.y * rect.height);
            return true;
        }

        internal static void ProjectIcon(MapIcon icon, float mapDisplayFactor)
        {
            if (!IsDrawing || icon == null || icon.iconImage == null || mapDisplayFactor <= 0f ||
                owner == null || owner.mapImage == null) return;
            Transform symbol = icon.iconImage.transform;
            Vector3 native = symbol.localPosition;
            if (!iconFixes.TryGetValue(icon, out IconFix fix))
            {
                fix = new IconFix();
                if (iconFixes.Count < MaximumClusteredIcons) iconFixes.Add(icon, fix);
            }
            fix.X = native.x / mapDisplayFactor;
            fix.Z = native.y / mapDisplayFactor;
            fix.Depth = native.z;
            fix.NativeScale = symbol.localScale;
            if (icon is UnitMapIcon unitIcon && unitIcon.unit?.definition?.mapOrient == true)
            {
                // Rotate the native, faction-known heading through the same terrain view.
                float nativeAngle = symbol.eulerAngles.z;
                if (!headings.TryGetValue(unitIcon, out HeadingMark headingMark))
                {
                    headingMark = new HeadingMark();
                    if (headings.Count < MaximumClusteredIcons) headings.Add(unitIcon, headingMark);
                }
                if (Mathf.Abs(Mathf.DeltaAngle(nativeAngle, headingMark.Projected)) < .01f)
                    nativeAngle = headingMark.Native;
                else headingMark.Native = nativeAngle;
            }
            PlaceProjectedIcon(icon, fix, owner.mapImage.GetComponent<RectTransform>().rect);
        }

        private static void ReprojectCachedIcons()
        {
            if (owner?.mapImage == null) return;
            RectTransform imageRect = owner.mapImage.GetComponent<RectTransform>();
            if (imageRect == null) return;
            Rect rect = imageRect.rect;
            staleIconFixes.Clear();
            foreach (KeyValuePair<MapIcon, IconFix> entry in iconFixes)
            {
                if (entry.Key == null || entry.Key.iconImage == null)
                {
                    if (staleIconFixes.Count < 64) staleIconFixes.Add(entry.Key);
                    continue;
                }
                PlaceProjectedIcon(entry.Key, entry.Value, rect);
            }
            foreach (MapIcon icon in staleIconFixes)
            {
                iconFixes.Remove(icon);
                if (icon is UnitMapIcon unitIcon) headings.Remove(unitIcon);
                if (stems.TryGetValue(icon, out StemMark stem))
                {
                    if (stem.Line != null) Object.Destroy(stem.Line.gameObject);
                    if (stem.Foot != null) Object.Destroy(stem.Foot.gameObject);
                }
                stems.Remove(icon);
            }
        }

        private static void PlaceProjectedIcon(MapIcon icon, IconFix fix, Rect rect)
        {
            if (!TryProject(fix.X, fix.Z, rect, out Vector2 point)) return;
            Transform symbol = icon.iconImage.transform;
            Vector3 screenPoint = owner.mapImage.transform.TransformPoint(new Vector3(point.x, point.y, 0f));
            Vector3 parentPoint = symbol.parent.InverseTransformPoint(screenPoint);
            symbol.localPosition = new Vector3(parentPoint.x, parentPoint.y, fix.Depth);
            symbol.localScale = fix.NativeScale;
            RectTransform symbolRect = symbol as RectTransform;
            float pixels = symbolRect != null
                ? Mathf.Max(symbolRect.rect.width, symbolRect.rect.height) : 15f;
            float size = pixels * Mathf.Max(Mathf.Abs(symbol.lossyScale.x),
                Mathf.Abs(symbol.lossyScale.y));
            float cap = icon is AirbaseMapIcon ? 18f :
                owner.selectedIcons.Contains(icon) ? 17f : 11f;
            if (size > cap && size > .01f) symbol.localScale *= cap / size;
            if (icon is UnitMapIcon unitIcon && headings.TryGetValue(unitIcon, out HeadingMark mark))
            {
                float heading = mark.Native * Mathf.Deg2Rad;
                if (TryProject(fix.X - Mathf.Sin(heading) * 1000f,
                        fix.Z + Mathf.Cos(heading) * 1000f, rect, out Vector2 ahead))
                {
                    Vector2 direction = ahead - point;
                    if (direction.sqrMagnitude > .01f)
                    {
                        float projected = Mathf.Atan2(-direction.x, direction.y) * Mathf.Rad2Deg;
                        symbol.eulerAngles = new Vector3(0f, 0f, projected);
                        mark.Projected = projected;
                    }
                }
            }
            PlaceStem(icon, symbol, parentPoint);
        }

        internal static void ProjectMarker(Transform marker, float mapDisplayFactor)
        {
            if (!IsDrawing || marker == null || mapDisplayFactor <= 0f ||
                owner?.mapImage == null) return;
            Vector3 native = marker.localPosition;
            RectTransform rect = owner.mapImage.GetComponent<RectTransform>();
            if (rect == null || !TryProject(native.x / mapDisplayFactor,
                    native.y / mapDisplayFactor, rect.rect, out Vector2 point)) return;
            Vector3 screen = rect.TransformPoint(new Vector3(point.x, point.y));
            Vector3 local = marker.parent.InverseTransformPoint(screen);
            marker.localPosition = new Vector3(local.x, local.y, native.z);
        }

        internal static void FollowIcon(TargetMarker marker)
        {
            if (!IsDrawing || marker?.Icon?.iconImage == null) return;
            marker.transform.position = marker.Icon.iconImage.transform.position;
        }

        private static void PlaceStem(MapIcon icon, Transform symbol, Vector3 ground)
        {
            bool show = icon is AirbaseMapIcon || owner.selectedIcons.Contains(icon);
            if (!stems.TryGetValue(icon, out StemMark mark))
            {
                if (!show || stems.Count >= MaximumStems) return;
                mark = new StemMark();
                mark.Line = MakeStemPart(symbol.parent, "NOAvionics.MapStem", 0);
                mark.Foot = MakeStemPart(symbol.parent, "NOAvionics.MapFoot", 1);
                stems.Add(icon, mark);
            }
            mark.Line.gameObject.SetActive(show);
            mark.Foot.gameObject.SetActive(show);
            if (!show) return;
            float scale = Mathf.Max(.001f, Mathf.Abs(symbol.parent.lossyScale.x));
            float lift = 12f / scale;
            symbol.localPosition = ground + Vector3.up * lift;
            RectTransform line = mark.Line.rectTransform;
            line.localPosition = ground + Vector3.up * (lift * .5f);
            line.sizeDelta = new Vector2(1.25f / scale, lift);
            RectTransform foot = mark.Foot.rectTransform;
            foot.localPosition = ground;
            foot.sizeDelta = Vector2.one * (3.5f / scale);
            Color tone = icon.iconImage.color;
            mark.Line.color = new Color(tone.r, tone.g, tone.b, .72f);
            mark.Foot.color = new Color(tone.r, tone.g, tone.b, .95f);
        }

        private static Image MakeStemPart(Transform parent, string name, int sibling)
        {
            var part = new GameObject(name, typeof(RectTransform), typeof(Image));
            part.transform.SetParent(parent, false);
            part.transform.SetSiblingIndex(sibling);
            Image image = part.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private static bool TryCursor(DynamicMap map, out GlobalPosition position)
        {
            position = default;
            if (!IsDrawing || map != owner) return false;
            RectTransform rect = map.mapImage.GetComponent<RectTransform>();
            Vector3 local = rect.InverseTransformPoint(Input.mousePosition);
            return TryUnproject(local, rect.rect, out position);
        }

        internal static bool TryUnproject(Vector2 local, Rect rect, out GlobalPosition position)
        {
            position = default;
            if (!IsDrawing || source == null || rect.width <= 0f || rect.height <= 0f) return false;
            float u = (local.x - rect.xMin) / rect.width;
            float v = (local.y - rect.yMin) / rect.height;
            if (u < 0f || u > 1f || v < 0f || v > 1f) return false;
            Ray ray = camera.ViewportPointToRay(new Vector3(u, v, 0f));
            bool found = false;
            float nearest = float.MaxValue;
            Vector3 hitPoint = default;
            foreach (MeshCollider collider in colliders)
            {
                if (collider == null || !collider.Raycast(ray, out RaycastHit hit, camera.farClipPlane) ||
                    hit.distance >= nearest) continue;
                nearest = hit.distance;
                hitPoint = hit.point;
                found = true;
            }
            if (!found) return false;
            Vector3 point = hitPoint - sceneRoot.transform.position;
            position = new GlobalPosition(point.x / ModelWidth * source.MapSize.x, 0f,
                point.z / ModelWidth * source.MapSize.y);
            return true;
        }

        internal static bool CursorInTerrain(DynamicMap map) => TryCursor(map, out _);
        internal static bool TryMapCursor(DynamicMap map, out GlobalPosition position) =>
            TryCursor(map, out position);

        internal static bool TryInspectCursor(out GlobalPosition position, out float elevation)
        {
            position = default;
            elevation = 0f;
            if (owner == null || MapUiPointer.OverControls() || !TryCursor(owner, out position))
                return false;
            elevation = SeaLevel + GroundHeight(position.x, position.z) *
                source.MapSize.x / (VerticalScale * ModelWidth);
            return true;
        }

        private static void HideNativeGrid()
        {
            if (owner?.gridLabels == null) return;
            Transform labels = owner.gridLabels.transform;
            for (int i = 0; i < labels.childCount; i++)
            {
                GameObject child = labels.GetChild(i).gameObject;
                if (!child.name.StartsWith("mapGrid_", StringComparison.Ordinal) &&
                    child.name != "MajorParent" && child.name != "MinorParent") continue;
                if (!nativeGrid.ContainsKey(child)) nativeGrid.Add(child, child.activeSelf);
                child.SetActive(false);
            }
        }

        private static void DeclutterIcons()
        {
            foreach (Image image in hiddenIcons) if (image != null) image.enabled = true;
            hiddenIcons.Clear();
            foreach (CountMark mark in counts) if (mark.Root != null) mark.Root.gameObject.SetActive(false);
            clusterIndex.Clear();
            hoveredStackCount = 0;
            if (owner?.mapBackground == null || owner.mapIcons == null) return;
            float zoom = owner.mapImage.transform.localScale.x;
            if (zoom >= 2.4f) return;
            RectTransform viewport = owner.mapBackground.rectTransform;
            Canvas canvas = viewport.GetComponentInParent<Canvas>();
            Camera uiCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            int activeClusters = 0;
            int limit = Mathf.Min(owner.mapIcons.Count, MaximumClusteredIcons);
            for (int i = 0; i < limit; i++)
            {
                MapIcon icon = owner.mapIcons[i];
                if (icon == null || !icon.gameObject.activeInHierarchy ||
                    owner.selectedIcons.Contains(icon)) continue;
                Image image = icon.iconImage;
                if (image == null || !image.enabled || !image.raycastTarget) continue;
                Vector2 screen = RectTransformUtility.WorldToScreenPoint(uiCamera, image.transform.position);
                if (!MapUiPointer.Contains(viewport, screen)) continue;
                Color32 tone = image.color;
                int colorKey = ((tone.r >> 4) << 8) | ((tone.g >> 4) << 4) | (tone.b >> 4);
                var key = (Mathf.FloorToInt(screen.x / 30f), Mathf.FloorToInt(screen.y / 30f), colorKey);
                if (clusterIndex.TryGetValue(key, out int at))
                {
                    clusters[at].Members.Add(image);
                }
                else if (activeClusters < MaximumClusteredIcons)
                {
                    if (activeClusters == clusters.Count) clusters.Add(new ClusterMark());
                    ClusterMark cluster = clusters[activeClusters];
                    cluster.Members.Clear();
                    cluster.Members.Add(image);
                    cluster.Screen = screen;
                    clusterIndex.Add(key, activeClusters++);
                }
            }
            int badges = 0;
            for (int i = 0; i < activeClusters; i++)
            {
                ClusterMark cluster = clusters[i];
                int total = cluster.Members.Count;
                if (total < MinimumStack || badges == MaximumClusters) continue;
                bool hovered = (cluster.Screen - (Vector2)Input.mousePosition).sqrMagnitude < 35f * 35f;
                if (hovered) hoveredStackCount = total;
                else for (int member = 1; member < total; member++)
                {
                    Image image = cluster.Members[member];
                    image.enabled = false;
                    hiddenIcons.Add(image);
                }
                if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport,
                        cluster.Screen, uiCamera, out Vector2 local)) continue;
                CountMark mark = Badge(viewport, badges++);
                mark.Root.anchoredPosition = local + new Vector2(14f, 10f);
                mark.Text.text = total > 99 ? "99+" : total.ToString();
                mark.Text.color = hovered ? AvTheme.Accent : AvTheme.TextPrimary;
                mark.Root.gameObject.SetActive(true);
            }
        }

        private static CountMark Badge(RectTransform viewport, int index)
        {
            if (index < counts.Count) return counts[index];
            var go = new GameObject("NOAvionics.ContactCount", typeof(RectTransform), typeof(Image));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(viewport, false);
            rect.sizeDelta = new Vector2(24f, 17f);
            Image fill = go.GetComponent<Image>();
            fill.color = new Color(.025f, .075f, .095f, .95f);
            fill.raycastTarget = false;
            var labelObject = new GameObject("Count", typeof(RectTransform), typeof(Text));
            RectTransform labelRect = labelObject.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            AvKit.Stretch(labelRect);
            Text label = labelObject.GetComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.fontSize = 11;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = AvTheme.TextPrimary;
            label.raycastTarget = false;
            var mark = new CountMark { Root = rect, Text = label };
            counts.Add(mark);
            return mark;
        }

        internal static void RehideNativeGrid()
        {
            if (IsDrawing) HideNativeGrid();
        }

        private static void CaptureFieldLayers()
        {
            if (owner == null || owner.mapImage == null) return;
            if (controlImage == null)
            {
                Transform child = owner.mapImage.transform.Find("ComSectorGridOverlay");
                if (child != null)
                {
                    controlImage = child.GetComponent<RawImage>();
                    if (controlImage != null) controlWasEnabled = controlImage.enabled;
                }
            }
            bool controlOn = controlImage != null && controlImage.texture != null &&
                controlImage.gameObject.activeInHierarchy && Plugin.Settings.Command.FrontlinesOverlay.Value;
            foreach (MeshRenderer renderer in controlRenderers) renderer.enabled = controlOn;
            if (controlOn) controlMaterial.mainTexture = controlImage.texture;
            if (controlImage != null) controlImage.enabled = false;

            if (threatImage == null)
            {
                Transform child = owner.mapImage.transform.Find("BoscaliThreatHeat");
                if (child != null)
                {
                    threatImage = child.GetComponent<RawImage>();
                    if (threatImage != null) threatWasEnabled = threatImage.enabled;
                }
            }
            bool threatOn = threatImage != null && threatImage.texture != null &&
                threatImage.gameObject.activeInHierarchy && Plugin.Settings.Command.ThreatHeat.Value;
            foreach (MeshRenderer renderer in threatRenderers) renderer.enabled = threatOn;
            if (threatOn) threatMaterial.mainTexture = threatImage.texture;
            if (threatImage != null) threatImage.enabled = false;
        }

        internal static void Restore()
        {
            MfdMapOrbitControls.Restore();
            foreach (Image image in hiddenIcons) if (image != null) image.enabled = true;
            hiddenIcons.Clear();
            foreach (CountMark mark in counts) if (mark.Root != null) Object.Destroy(mark.Root.gameObject);
            counts.Clear();
            clusterIndex.Clear();
            clusters.Clear();
            headings.Clear();
            iconFixes.Clear();
            staleIconFixes.Clear();
            hoveredStackCount = 0;
            foreach (StemMark mark in stems.Values)
            {
                if (mark.Line != null) Object.Destroy(mark.Line.gameObject);
                if (mark.Foot != null) Object.Destroy(mark.Foot.gameObject);
            }
            stems.Clear();
            foreach (KeyValuePair<GameObject, bool> entry in nativeGrid)
                if (entry.Key != null) entry.Key.SetActive(entry.Value);
            nativeGrid.Clear();
            if (controlImage != null)
                controlImage.enabled = controlWasEnabled &&
                    (Plugin.Settings?.Command?.FrontlinesOverlay?.Value ?? false);
            controlImage = null;
            if (threatImage != null)
                threatImage.enabled = threatWasEnabled &&
                    (Plugin.Settings?.Command?.ThreatHeat?.Value ?? false);
            threatImage = null;
            if (nativeImage != null)
            {
                Color color = nativeImage.color;
                color.a = Plugin.Settings?.Command?.MapTerrainOpacity?.Value ?? 1f;
                nativeImage.color = color;
                nativeImage.enabled = Plugin.Settings?.Command?.MapTerrainImage?.Value ?? true;
            }
            nativeImage = null;
            if (view != null) Object.Destroy(view.gameObject);
            view = null;
            if (sceneRoot != null) Object.Destroy(sceneRoot);
            sceneRoot = null;
            foreach (Mesh mesh in meshes) if (mesh != null) Object.Destroy(mesh);
            meshes.Clear();
            colliders.Clear();
            controlRenderers.Clear();
            threatRenderers.Clear();
            if (groundMaterial != null) Object.Destroy(groundMaterial);
            if (controlMaterial != null) Object.Destroy(controlMaterial);
            if (threatMaterial != null) Object.Destroy(threatMaterial);
            if (gridMaterial != null) Object.Destroy(gridMaterial);
            groundMaterial = controlMaterial = threatMaterial = gridMaterial = null;
            if (texture != null) { texture.Release(); Object.Destroy(texture); }
            texture = null;
            camera = null;
            nextRender = 0f;
            fittedViewport = Vector2.zero;
            coverageScale = 0f;
        }

        internal static void Reset()
        {
            Restore();
            owner = null;
            source = null;
            sprite = null;
            mapAssetName = null;
            heights = null;
            land = null;
            if (styleTexture != null) Object.Destroy(styleTexture);
            styleTexture = null;
            ready = false;
            unavailable = false;
            yaw = DefaultYaw;
            pitch = DefaultPitch;
            viewRevision = 0;
        }
    }

    internal sealed class MfdMapProjection : IMapProjection
    {
        public bool IsActive => MfdTerrainRelief.IsDrawing;
        public int Revision => MfdTerrainRelief.ViewRevision;

        public bool TryProject(float worldX, float worldZ, out float mapX, out float mapY)
        {
            mapX = mapY = 0f;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (!IsActive || map?.mapImage == null) return false;
            RectTransform rect = map.mapImage.GetComponent<RectTransform>();
            if (rect == null || !MfdTerrainRelief.TryProject(worldX, worldZ, rect.rect, out Vector2 at))
                return false;
            mapX = at.x;
            mapY = at.y;
            return true;
        }
    }

    [HarmonyPatch(typeof(GridLabels), "LateUpdate")]
    internal static class MfdReliefGridLabelsPatch
    {
        [HarmonyPostfix]
        private static void Postfix() => MfdTerrainRelief.RehideNativeGrid();
    }

    [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.GetCursorCoordinates))]
    internal static class MfdReliefMapCursorPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(DynamicMap __instance, ref GlobalPosition __result)
        {
            if (!MfdTerrainRelief.TryMapCursor(__instance, out GlobalPosition point)) return true;
            __result = point;
            return false;
        }
    }

    [HarmonyPatch(typeof(DynamicMap), nameof(DynamicMap.IsCursorInMapRectangle))]
    internal static class MfdReliefMapBoundsPatch
    {
        [HarmonyPostfix]
        private static void Postfix(DynamicMap __instance, ref bool __result)
        {
            if (MfdTerrainRelief.IsDrawing) __result &= MfdTerrainRelief.CursorInTerrain(__instance);
        }
    }

    [HarmonyPatch(typeof(UnitMapIcon), nameof(UnitMapIcon.UpdateIcon))]
    internal static class MfdReliefUnitIconPatch
    {
        [HarmonyPostfix]
        private static void Postfix(UnitMapIcon __instance, float mapDisplayFactor) =>
            MfdTerrainRelief.ProjectIcon(__instance, mapDisplayFactor);
    }

    [HarmonyPatch(typeof(AirbaseMapIcon), nameof(AirbaseMapIcon.UpdateIcon))]
    internal static class MfdReliefAirbaseIconPatch
    {
        [HarmonyPostfix]
        private static void Postfix(AirbaseMapIcon __instance, float mapDisplayFactor) =>
            MfdTerrainRelief.ProjectIcon(__instance, mapDisplayFactor);
    }

    [HarmonyPatch(typeof(ObjectiveMarker), nameof(ObjectiveMarker.UpdateMarker))]
    internal static class MfdReliefObjectivePatch
    {
        [HarmonyPostfix]
        private static void Postfix(ObjectiveMarker __instance) =>
            MfdTerrainRelief.ProjectMarker(__instance.transform,
                SceneSingleton<DynamicMap>.i?.mapDisplayFactor ?? 0f);
    }

    [HarmonyPatch(typeof(TargetMarker), "Update")]
    internal static class MfdReliefSelectedInfoPatch
    {
        [HarmonyPostfix]
        private static void Postfix(TargetMarker __instance) => MfdTerrainRelief.FollowIcon(__instance);
    }
}
