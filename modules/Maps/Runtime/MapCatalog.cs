using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.Maps.Domain;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuclearOption.SceneLoading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Maps.Runtime
{
    /// <summary>One map the mod ships: its manifest, and once it has been built its open bundle and game objects.</summary>
    /// <summary>A game mode of a map, from <c>&lt;id&gt;.modes.json</c> (written by <c>nomod map modes</c>): a piece of mission
    /// laid over the game's default one, who owns the map's red and blue structures in it (a faction name, or "" for
    /// nobody), and what to add when the BOTE ships mod is installed.</summary>
    internal sealed class MapMode
    {
        public string name = "";
        public Dictionary<string, string> sides = null;
        public JObject mission = null, bote = null;
    }

    internal sealed class MapEntry
    {
        public MapManifest Manifest;
        public MapMode[] Modes = new MapMode[0];
        /// <summary>First 8 hex digits of the bundle's SHA-256, from the sidecar: the build id of the handshake.</summary>
        public string Hash;
        public MapDetails Details;
        /// <summary>The small preview shown while the bundle is closed; the bundle's chart replaces it while it is open.</summary>
        public Sprite Card;
        public AssetBundle Bundle;
        public MapSettings Template;
        /// <summary>The game's copy of the template that is in the scene now (a destroyed object when the map is not loaded).</summary>
        public MapSettings Live;
        public bool BuildFailed;
        /// <summary>Meshes, materials and assets made for the template; destroyed with it.</summary>
        public readonly List<Object> Owned = new List<Object>();
        /// <summary>The tree material of each forest, made at the first load of the map.</summary>
        public readonly Dictionary<int, Material> TreeMaterials = new Dictionary<int, Material>();
    }

    /// <summary>
    /// The mod's maps and their registration with the game's two map lists. A map is listed from its small sidecar
    /// (<c>BoscaliSummer.Maps.&lt;id&gt;.map.json</c> and <c>.card.png</c>); its bundle is opened only when the game switches
    /// to it, from <c>&lt;plugin folder&gt;/Maps/&lt;id&gt;.bsmap</c> when that file exists, else from the embedded copy. One
    /// built map is kept: building another closes the ones that are not in the scene. Both registrations are idempotent
    /// and run from the game's own lookups, so a dedicated server (which never opens the main menu) has the maps too.
    /// </summary>
    internal static class MapCatalog
    {
        private const string Prefix = "BoscaliSummer.Maps.", Sidecar = ".map.json";
        private const int MaxBundleBytes = 96 * 1024 * 1024, MaxSidecarBytes = 4 * 1024 * 1024;
        private static List<MapEntry> entries;

        public static IReadOnlyList<MapEntry> All => entries ?? (entries = Scan(typeof(MapCatalog).Assembly));

        /// <summary>First 8 hex digits of the bundle's SHA-256, or "" when the map is not one of ours.</summary>
        public static string HashFor(string prefabName) => Find(prefabName)?.Hash ?? "";

        public static MapEntry Find(string prefabName) =>
            All.FirstOrDefault(e => string.Equals(e.Manifest.prefabName, prefabName, StringComparison.OrdinalIgnoreCase));

        /// <summary>The entry of the map that is loaded now; null on a vanilla map and outside a mission.</summary>
        public static MapEntry Loaded()
        {
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            MapSettings loaded = level != null ? level.LoadedMapSettings : null;
            if (loaded == null) return null;
            IReadOnlyList<MapEntry> all = All;
            for (int i = 0; i < all.Count; i++)
                if (ReferenceEquals(all[i].Live, loaded)) return all[i];
            return null;
        }

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
            if (manager.Maps.Any(m => m != null && m.Details == entry.Details && m.Prefab != null)) return;
            if (entry.Template == null)
            {
                CloseOthers(entry, manager);
                entry.Template = Load(entry) ? MapBuilder.Build(entry, manager) : null;
            }
            if (entry.Template == null)
            {
                entry.BuildFailed = true;
                Unload(entry, manager);
                return;
            }
            manager.Maps = manager.Maps.Where(m => m == null || m.Details != entry.Details)
                .Concat(new[] { new MapSettingsManager.Map { Details = entry.Details, Prefab = entry.Template } }).ToArray();
        }

        /// <summary>One built map is kept: closes every other map of the mod, except one whose copy is still in the scene
        /// (it draws from its bundle; it is closed on the next call after it has gone). Called before a map is built and
        /// again once a map of the mod has replaced the previous one in the scene.</summary>
        public static void CloseOthers(MapEntry keep, MapSettingsManager manager)
        {
            foreach (MapEntry other in All)
                if (other != keep && other.Live == null) Unload(other, manager);
        }

        /// <summary>A file of the mod's maps: <c>&lt;plugin folder&gt;/Maps/&lt;file&gt;</c> when it exists, else the embedded
        /// resource; null when neither can be read.</summary>
        public static byte[] ReadAsset(string file, int maxBytes, out string source)
        {
            Assembly assembly = typeof(MapCatalog).Assembly;
            try
            {
                string loose = Path.Combine(Path.GetDirectoryName(assembly.Location) ?? "", "Maps", file);
                if (File.Exists(loose))
                    using (FileStream stream = File.OpenRead(loose))
                    {
                        byte[] bytes = EmbeddedResources.ReadAll(stream, maxBytes);
                        source = loose;
                        if (bytes != null) return bytes;
                        Plugin.Logger?.LogWarning("[Maps] " + loose + " is empty or over " + maxBytes / (1024 * 1024) + " MiB; using the built-in copy.");
                    }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is NotSupportedException)
            {
                Plugin.Logger?.LogWarning("[Maps] The loose " + file + " could not be read (" + error.Message + "); using the built-in copy.");
            }
            source = "the mod";
            return EmbeddedResources.ReadAll(assembly, Prefix + file, maxBytes);
        }

        /// <summary>Opens the map's bundle after checking it is the build the sidecar names.</summary>
        private static bool Load(MapEntry entry)
        {
            if (entry.Bundle != null) return true;
            MapManifest manifest = entry.Manifest;
            byte[] bytes = ReadAsset(manifest.id + ".bsmap", MaxBundleBytes, out string source);
            string problem = manifest.BundleProblem(bytes);
            if (problem == null && (entry.Bundle = AssetBundle.LoadFromMemory(bytes)) == null) problem = "Unity could not open the bundle";
            if (problem != null)
            {
                Plugin.Logger?.LogError("[Maps] " + manifest.displayName + " cannot be loaded from " + source + ": " + problem + ".");
                return false;
            }
            var chart = entry.Bundle.LoadAsset<Sprite>("bs_" + manifest.id + "_map");
            if (chart != null) entry.Details.MapImage = chart;
            Plugin.Logger?.LogInfo("[Maps] " + manifest.displayName + " build " + entry.Hash + " opened from " + source + " (" + bytes.Length / (1024 * 1024) + " MiB).");
            return true;
        }

        /// <summary>Closes a map that is not in the scene: the list shows its card again, the manager forgets its prefab,
        /// and the template, everything made for it and the bundle's assets are destroyed.</summary>
        private static void Unload(MapEntry entry, MapSettingsManager manager)
        {
            if (entry.Bundle == null && entry.Template == null && entry.Owned.Count == 0) return;
            entry.Details.MapImage = entry.Card;
            if (manager != null && manager.Maps != null && manager.Maps.Any(m => m != null && m.Details == entry.Details))
                manager.Maps = manager.Maps.Where(m => m == null || m.Details != entry.Details).ToArray();
            if (entry.Template != null) Object.Destroy(entry.Template.gameObject);
            entry.Template = null;
            foreach (Object made in entry.Owned)
                if (made != null) Object.Destroy(made);
            entry.Owned.Clear();
            entry.TreeMaterials.Clear();
            if (entry.Bundle != null) entry.Bundle.Unload(true);
            entry.Bundle = null;
            Plugin.Logger?.LogInfo("[Maps] " + entry.Manifest.displayName + " closed.");
        }

        private static List<MapEntry> Scan(Assembly assembly)
        {
            var found = new List<MapEntry>();
            foreach (string name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith(Prefix, StringComparison.Ordinal) || !name.EndsWith(Sidecar, StringComparison.Ordinal)) continue;
                try
                {
                    MapEntry entry = Open(assembly, name);
                    if (entry != null) found.Add(entry);
                }
                catch (Exception error)
                {
                    Plugin.Logger?.LogError("[Maps] " + name + " could not be read: " + error.Message);
                }
            }
            return found;
        }

        /// <summary>Lists a map from its sidecar and card alone; no bundle is opened here.</summary>
        private static MapEntry Open(Assembly assembly, string resource)
        {
            byte[] bytes = EmbeddedResources.ReadAll(assembly, resource, MaxSidecarBytes);
            MapManifest manifest = bytes != null ? MapManifest.Parse(Encoding.UTF8.GetString(bytes)) : null;
            string problem = manifest == null ? "it is empty" : manifest.Problem() ??
                (string.IsNullOrEmpty(manifest.bundleHash) ? "it has no bundleHash (the bake did not write it)" : null);
            if (problem != null)
            {
                Plugin.Logger?.LogError("[Maps] " + resource + " is not listed: " + problem + ".");
                return null;
            }
            Color tag = Rgb(manifest.tagColor, new Color(.35f, .85f, .8f));
            var details = ScriptableObject.CreateInstance<MapDetails>();
            details.name = manifest.prefabName;
            details.hideFlags = HideFlags.DontUnloadUnusedAsset;
            details.PrefabName = manifest.prefabName;
            details.WorkshopTag = manifest.workshopTag;
            details.MapName = manifest.displayName;
            details.TagColor = tag;
            Sprite card = CardSprite(assembly, manifest.id, tag);
            details.MapImage = card;
            Plugin.Logger?.LogInfo("[Maps] " + manifest.displayName + " (" + manifest.prefabName + ", build " + manifest.bundleHash + ") is available.");
            var modes = new MapMode[0];
            byte[] extra = EmbeddedResources.ReadAll(assembly, Prefix + manifest.id + ".modes.json", MaxSidecarBytes);
            if (extra != null)
                try
                {
                    modes = JsonConvert.DeserializeObject<MapMode[]>(Encoding.UTF8.GetString(extra).TrimStart('\uFEFF'),
                        new JsonSerializerSettings { DateParseHandling = DateParseHandling.None }) ?? modes;
                }
                catch (JsonException error)
                {
                    Plugin.Logger?.LogError("[Maps] The modes of " + manifest.displayName + " could not be read: " + error.Message);
                }
            return new MapEntry { Manifest = manifest, Hash = manifest.bundleHash, Details = details, Card = card, Modes = modes.Where(m => m != null && !string.IsNullOrEmpty(m.name)).ToArray() };
        }

        /// <summary>The map's lit preview; a plain tile in the map's tag colour when the card is missing.</summary>
        private static Sprite CardSprite(Assembly assembly, string id, Color tag)
        {
            var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            byte[] png = EmbeddedResources.ReadAll(assembly, Prefix + id + ".card.png", MaxSidecarBytes);
            if (png == null || !texture.LoadImage(png, true))
            {
                Object.Destroy(texture); // a failed decode leaves it in an unknown state
                texture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                texture.SetPixels(Enumerable.Repeat(tag, 16).ToArray());
                texture.Apply(false, true);
            }
            texture.name = "bs_" + id + "_card";
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(.5f, .5f), 100f);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        /// <summary>A manifest colour ([r, g, b], 0..1 sRGB) as a Unity colour.</summary>
        internal static Color Rgb(float[] rgb, Color fallback) =>
            rgb != null && rgb.Length >= 3 ? new Color(rgb[0], rgb[1], rgb[2], fallback.a) : fallback;
    }
}
