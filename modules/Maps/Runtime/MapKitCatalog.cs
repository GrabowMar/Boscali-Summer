using System;
using System.Linq;
using BoscaliSummer.Modules.Maps.Domain;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Maps.Runtime
{
    /// <summary>
    /// The scenery kit <c>mapkit.bskit</c> (rocks, ice, trees, huts, towers ...): opened on first use, from
    /// <c>&lt;plugin folder&gt;/Maps/mapkit.bskit</c> when that file exists, else from the embedded copy. It serves every
    /// map of the mod and, through <c>Core.Contracts.MapKit</c>, any module on any map. A missing or broken kit is
    /// reported once and then reads as empty.
    /// </summary>
    internal static class MapKitCatalog
    {
        private const int MaxBytes = 16 * 1024 * 1024;
        private static readonly KitManifest none = new KitManifest();
        private static AssetBundle bundle;
        private static AssetBundle instanceBundle;
        private static Shader instanceShader;
        private static Texture2D palmFoliage, scrubFoliage, nativeTreeSurface;
        internal static Texture2D NativeTreeSurface => nativeTreeSurface;
        private static GameObject root;
        private static KitManifest manifest;
        private static string[] names = new string[0];
        private static bool tried, dressed;
        private static bool triedInstanceShader;

        public static Shader InstanceShader
        {
            get
            {
                if (triedInstanceShader) return instanceShader;
                triedInstanceShader = true;
                byte[] bytes = MapCatalog.ReadAsset("scenery-instancing.bundle", MaxBytes, out _);
                if (bytes == null) return null;
                instanceBundle = AssetBundle.LoadFromMemory(bytes);
                if (instanceBundle == null) return null;
                Shader shader = instanceBundle.LoadAsset<Shader>("Assets/Map/SceneryInstancing/MapSceneryInstanced.shader");
                instanceShader = shader != null && shader.isSupported ? shader : null;
                if (instanceShader != null)
                {
                    palmFoliage = instanceBundle.LoadAsset<Texture2D>("Assets/Map/SceneryInstancing/PalmFoliage.png");
                    scrubFoliage = instanceBundle.LoadAsset<Texture2D>("Assets/Map/SceneryInstancing/ScrubFoliage.png");
                }
                return instanceShader;
            }
        }

        internal static Texture2D FoliageTexture(string kind, Mesh mesh)
        {
            Texture2D atlas = kind == "palm" ? palmFoliage : kind == "acacia" ? scrubFoliage : null;
            if (atlas == null || !mesh.isReadable) return null;
            // Older loose kits have tiling UVs. Only the authored cutout meshes carry this wood-channel marker.
            Vector2[] uv = mesh.uv;
            return uv.Length > 0 && uv[0].x >= -3f && uv[0].x <= -2f && uv[0].y == 17f ? atlas : null;
        }

        /// <summary>The kit's kinds; never null, empty when there is no kit.</summary>
        public static KitManifest Manifest
        {
            get
            {
                Open();
                return manifest ?? none;
            }
        }

        /// <summary>Every kind name in the kit.</summary>
        public static string[] Kinds()
        {
            Open();
            return names;
        }

        /// <summary>The inactive template of a kind's variant, to Instantiate; null when the kit lacks it.</summary>
        public static GameObject Prototype(string kind, int variant)
        {
            Open();
            Transform found = root != null ? root.transform.Find("kit_" + kind + "_" + variant) : null;
            return found != null ? found.gameObject : null;
        }

        /// <summary>A texture or material of the kit by name; null when the kit lacks it.</summary>
        public static T Asset<T>(string name) where T : Object
        {
            Open();
            return bundle != null && !string.IsNullOrEmpty(name) ? bundle.LoadAsset<T>(name) : null;
        }

        /// <summary>Lays the game's own surfaces over the modelled shapes: concrete paving, hangar sheet, gabion,
        /// rock albedo and normal textures of the running game on the kit's KitModel material (its vertex alpha picks
        /// one per face). Missing textures are skipped gracefully so the kit degrades cleanly across game updates.</summary>
        public static void Dress()
        {
            if (dressed) return;
            Open();
            Material material = root != null ? root.GetComponentsInChildren<MeshRenderer>(true).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null && m.name == "KitModel") : null;
            if (material == null) return;
            Plugin.Logger?.LogInfo("[Maps] Scenery kit surfaces: detail " +
                (material.GetTexture("_DetailTex") != null ? "on" : "off") +
                ", normal " + (material.GetTexture("_DetailNormal") != null ? "on" : "off") +
                ", decals " + (material.GetTexture("_DecalTex") != null ? "on" : "off") +
                ", textured " + (material.IsKeywordEnabled("_TEXTURED") ? "on" : "off") +
                ", detailKw " + (material.IsKeywordEnabled("_DETAIL") ? "on" : "off") + ".");
            string[] wanted = { "concrete_paving_b", "hangar_b", "gabion_b", "rocky_b" };
            string[] slots = { "_TexA", "_TexB", "_TexC", "_TexD" };
            float[] gain = { 2.45f, 2.1f, 2.3f, 2.2f }; // one over each texture's mean, so the model keeps its colours
            string[] wantedNormals = { "concrete_paving_n", "hangar_n", "gabion_n", "rocky_n" };
            string[] normalSlots = { "_TexAN", "_TexBN", "_TexCN", "_TexDN" };
            var found = new Texture2D[4];
            var foundNormals = new Texture2D[4];
            foreach (Texture2D texture in Resources.FindObjectsOfTypeAll<Texture2D>())
            {
                if (nativeTreeSurface == null && texture.name == "Tree_deciduous") nativeTreeSurface = texture;
                int at = Array.IndexOf(wanted, texture.name);
                if (at >= 0 && found[at] == null) found[at] = texture;
                int nat = Array.IndexOf(wantedNormals, texture.name);
                if (nat >= 0 && foundNormals[nat] == null) foundNormals[nat] = texture;
            }
            if (found[0] == null && found[1] == null && found[2] == null && found[3] == null)
            {
                Plugin.Logger?.LogInfo("[Maps] Scenery kit keeps its plain colours for now: the game has not loaded base textures yet.");
                return; // looked for again with the next map
            }
            Vector4 gains = Vector4.one;
            for (int i = 0; i < 4; i++)
            {
                Texture2D tex = found[i];
                if (i == 3 && tex == null) tex = found[0]; // rock falls back to concrete if rocky_b isn't resident
                if (tex != null && material.HasProperty(slots[i]))
                {
                    material.SetTexture(slots[i], tex);
                    gains[i] = gain[i];
                }
                else gains[i] = 1f;
                Texture2D nrm = foundNormals[i];
                if (nrm != null && material.HasProperty(normalSlots[i])) material.SetTexture(normalSlots[i], nrm);
            }
            material.SetVector("_TexGain", gains);
            dressed = found[0] != null && found[1] != null && found[2] != null;
            Plugin.Logger?.LogInfo("[Maps] Scenery kit dressed: albedo [" +
                string.Join(", ", wanted.Where((w, i) => found[i] != null)) + "], normals [" +
                string.Join(", ", wantedNormals.Where((w, i) => foundNormals[i] != null)) + "].");
        }

        /// <summary>When the mod is torn down: the lookups read as empty again; objects already made from the kit keep
        /// their meshes and textures.</summary>
        public static void Close()
        {
            if (bundle != null) bundle.Unload(false);
            if (instanceBundle != null) instanceBundle.Unload(false);
            bundle = null;
            instanceBundle = null;
            instanceShader = null;
            palmFoliage = scrubFoliage = nativeTreeSurface = null;
            triedInstanceShader = false;
            root = null;
            manifest = null;
            names = new string[0];
            tried = dressed = false;
        }

        private static void Open()
        {
            if (tried) return;
            tried = true;
            try
            {
                byte[] bytes = MapCatalog.ReadAsset("mapkit.bskit", MaxBytes, out string source);
                bundle = bytes != null ? AssetBundle.LoadFromMemory(bytes) : null;
                root = bundle != null ? bundle.LoadAsset<GameObject>("MapKit") : null;
                var text = bundle != null ? bundle.LoadAsset<TextAsset>("kit") : null;
                manifest = text != null ? KitManifest.Parse(text.text) : null;
                if (root == null || manifest == null || manifest.kinds == null)
                {
                    Plugin.Logger?.LogWarning("[Maps] No scenery kit: " + (bytes == null ? "mapkit.bskit is not in this build (run nomod map kit)."
                        : bundle == null ? "Unity could not open mapkit.bskit from " + source + "."
                        : "mapkit.bskit from " + source + " has no MapKit prefab or kit list."));
                    Close();
                    tried = true;
                    return;
                }
                manifest.kinds = manifest.kinds.Where(k => k != null && !string.IsNullOrEmpty(k.name)).ToArray();
                names = manifest.kinds.Select(k => k.name).ToArray();
                Plugin.Logger?.LogInfo("[Maps] Scenery kit opened from " + source + ": " + names.Length + " kinds, " + manifest.kinds.Sum(k => k.variants) + " shapes.");
            }
            catch (Exception error)
            {
                Plugin.Logger?.LogError("[Maps] The scenery kit could not be opened: " + error.Message);
                Close();
                tried = true;
            }
        }
    }
}
