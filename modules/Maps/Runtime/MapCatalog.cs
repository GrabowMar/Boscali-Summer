using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.Maps.Domain;
using NuclearOption.SceneLoading;
using UnityEngine;

namespace BoscaliSummer.Modules.Maps.Runtime
{
    /// <summary>One map the mod ships: its manifest, its open bundle and the game objects made from it.</summary>
    internal sealed class MapEntry
    {
        public MapManifest Manifest;
        public string Hash;
        public AssetBundle Bundle;
        public MapDetails Details;
        public MapSettings Template;
        public bool BuildFailed;
    }

    /// <summary>
    /// The maps embedded in the mod as <c>BoscaliSummer.Maps.*.bsmap</c>, and their registration with the game's two map
    /// lists. Both registrations are idempotent and run from the game's own lookups, so a dedicated server (which never
    /// opens the main menu) has the maps too.
    /// </summary>
    internal static class MapCatalog
    {
        private const string Prefix = "BoscaliSummer.Maps.", Suffix = ".bsmap";
        private const int MaxBundleBytes = 96 * 1024 * 1024;
        private static List<MapEntry> entries;

        public static IReadOnlyList<MapEntry> All => entries ?? (entries = Scan(typeof(MapCatalog).Assembly));

        /// <summary>First 8 hex digits of the bundle's SHA-256, or "" when the map is not one of ours.</summary>
        public static string HashFor(string prefabName) => Find(prefabName)?.Hash ?? "";

        public static MapEntry Find(string prefabName) =>
            All.FirstOrDefault(e => string.Equals(e.Manifest.prefabName, prefabName, StringComparison.OrdinalIgnoreCase));

        public static void EnsureListed(MapLoader loader)
        {
            if (loader == null || loader.Maps == null) return;
            foreach (MapEntry entry in All)
                if (Array.IndexOf(loader.Maps, entry.Details) < 0)
                    loader.Maps = loader.Maps.Concat(new[] { entry.Details }).ToArray();
        }

        /// <summary>Puts the map's prefab in the manager's list, building it on first use. A map that cannot be built is
        /// left out, so the game reports "Could not find map" instead of failing half way.</summary>
        public static void EnsureEnabled(MapSettingsManager manager, string mapName)
        {
            MapEntry entry = Find(mapName);
            if (entry == null || manager == null || manager.Maps == null || entry.BuildFailed) return;
            if (manager.Maps.Any(m => m != null && m.Details == entry.Details)) return;
            if (entry.Template == null) entry.Template = MapBuilder.Build(entry, manager);
            if (entry.Template == null)
            {
                entry.BuildFailed = true;
                return;
            }
            manager.Maps = manager.Maps.Concat(new[] { new MapSettingsManager.Map { Details = entry.Details, Prefab = entry.Template } }).ToArray();
        }

        private static List<MapEntry> Scan(Assembly assembly)
        {
            var found = new List<MapEntry>();
            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(Suffix, StringComparison.Ordinal)) continue;
                try
                {
                    MapEntry entry = Open(assembly, name);
                    if (entry != null) found.Add(entry);
                }
                catch (Exception error)
                {
                    Plugin.Logger?.LogError("[Maps] " + name + " could not be opened: " + error.Message);
                }
            }
            return found;
        }

        private static MapEntry Open(Assembly assembly, string resource)
        {
            byte[] bytes = EmbeddedResources.ReadAll(assembly, resource, MaxBundleBytes);
            if (bytes == null)
            {
                Plugin.Logger?.LogError("[Maps] " + resource + " is empty or over " + MaxBundleBytes / (1024 * 1024) + " MiB.");
                return null;
            }
            string hash;
            using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes), 0, 4).Replace("-", "").ToLowerInvariant();
            AssetBundle bundle = AssetBundle.LoadFromMemory(bytes);
            TextAsset text = bundle != null ? bundle.LoadAsset<TextAsset>("map") : null;
            MapManifest manifest = text != null ? Newtonsoft.Json.JsonConvert.DeserializeObject<MapManifest>(text.text) : null;
            string problem = manifest == null ? "it has no map manifest" : manifest.Problem();
            if (problem != null)
            {
                Plugin.Logger?.LogError("[Maps] " + resource + " is not loaded: " + problem + ".");
                if (bundle != null) bundle.Unload(true);
                return null;
            }
            var details = ScriptableObject.CreateInstance<MapDetails>();
            details.name = manifest.prefabName;
            details.hideFlags = HideFlags.DontUnloadUnusedAsset;
            details.PrefabName = manifest.prefabName;
            details.WorkshopTag = manifest.workshopTag;
            details.MapName = manifest.displayName;
            details.MapImage = bundle.LoadAsset<Sprite>("bs_" + manifest.id + "_map");
            details.TagColor = new Color(.35f, .85f, .8f);
            Plugin.Logger?.LogInfo("[Maps] " + manifest.displayName + " (" + manifest.prefabName + ", build " + hash + ") is available.");
            return new MapEntry { Manifest = manifest, Hash = hash, Bundle = bundle, Details = details };
        }
    }
}
