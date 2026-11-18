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
        private static GameObject root;
        private static KitManifest manifest;
        private static string[] names = new string[0];
        private static bool tried;

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

        /// <summary>When the mod is torn down: the lookups read as empty again; objects already made from the kit keep
        /// their meshes and textures.</summary>
        public static void Close()
        {
            if (bundle != null) bundle.Unload(false);
            bundle = null;
            root = null;
            manifest = null;
            names = new string[0];
            tried = false;
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
