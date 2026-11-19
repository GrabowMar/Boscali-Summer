using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BoscaliSummer.Modules.Maps.Domain;
using HarmonyLib;
using NuclearOption.Effects;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace BoscaliSummer.Modules.Maps.Runtime
{
    /// <summary>
    /// Makes a mod map's ground look like the game's own: the vanilla terrain material (its detail textures, normals
    /// and blending) fed with this map's whole-map colour, normal, occlusion, ground-cover weights and ground skins, and
    /// the vanilla grass, tree and blast-scar systems copied from a shipped map and pointed at this one. The method is
    /// NOCustomMaps' (MIT, (c) 2026 BatongMaster). Everything is taken from the running game, so the map follows the
    /// game's look as it changes; a shader property the game no longer has is reported once and skipped.
    /// </summary>
    internal static class MapGround
    {
        private const string Sample = "_SampleTexture2D_", SampleEnd = "_Texture_1_Texture2D";
        private const string BreakupTerrain = "Vector1_8215e5f818784b058d0f93c84222a604", BreakupNormal = "_normalBreakup",
            BreakupSplat = "Vector1_1d51f5d8cc7b4687b932a5b404890ae6", FieldsAngle = "3bd9a1d970ff4fbc9888fa54a498dd5f";

        /// <summary>Whole-map texture slots: the bundle texture, the shader property, and what stands in when the bundle
        /// lacks it (the game material's own texture belongs to another map and must not show through).</summary>
        private static readonly (string texture, string property, int fallback)[] Layers =
        {
            ("macro_normal", "_macro_normal", 2), ("macro_ao", "_macro_ao", 1), ("splat_grass", "_splat_grass", 0),
            ("splat_rock", "_splat_rock", 0), ("splat_lush", "_splat_lush", 0), ("splat_extra", "_splat_fields", 0),
        };

        /// <summary>The detail textures of the game's terrain shader graph per skin slot: albedo ids, then normal ids.</summary>
        private static readonly Dictionary<string, (string[] albedo, string[] normal)> SkinSlots = new Dictionary<string, (string[], string[])>
        {
            ["sand"] = (new[] { "8fd7ac9820c240c383887165ff5b5476" }, new[] { "aea52007ac564f6fbe5b78277e135f0b" }),
            ["dunes"] = (new[] { "dacba2ee34424733acff4f8c92baea27" }, new[] { "a4670c2df1194ad9a22c4dbe817ab097" }),
            ["grass"] = (new[] { "674f68b33e3644b4acf5cea7b75e681f" }, new[] { "838acaebc3fd4aa599cb97870fc87e14" }),
            ["grassDetail"] = (new[] { "92633bd0becc4b749d681a31beaffbaa" }, new[] { "0c6460df7d7a4405aff8c488bbffc050" }),
            ["rock"] = (new[] { "5a49beebfe604041acd648420add582f" }, new[] { "3f967dd12e9c49af9fa80c834d7c5a19", "959d2f3a5ff8423b93bfc1fd0a56dd49" }),
            ["lush"] = (new[] { "f43950d91c9a4f2ea1cfb2f7a9ed314b" }, new[] { "5ed7e9e7492147fe9dbd547a9ad528c1" }),
            ["fields"] = (new[] { "1a4f7e08675c47d2a231a6bf2eacea9b" }, new[] { "a867fd84b3f5415caa454567fa1e5506" }),
        };

        private static readonly HashSet<string> reported = new HashSet<string>(StringComparer.Ordinal);
        private static bool listed;
        private static ConfigEntry<bool> customTrees;

        /// <summary>[Maps] CustomTreeMeshes: off makes every forest use the game's own tree mesh (the way out if a kit tree
        /// mesh breaks in the game's instanced tree shader). Bound on first use, when the plugin's config file exists.</summary>
        private static bool CustomTreeMeshes
        {
            get
            {
                if (customTrees == null && Chainloader.PluginInfos.TryGetValue(Plugin.PluginGuid, out PluginInfo plugin) && plugin.Instance != null)
                    customTrees = plugin.Instance.Config.Bind("Maps", "CustomTreeMeshes", true,
                        "Forests of the mod's own maps use the map kit's tree meshes (conifer clumps). Off: the game's own tree everywhere.");
                return customTrees == null || customTrees.Value;
            }
        }

        /// <summary>When the template is built: swap the bake's fallback material for a copy of the game's terrain material.</summary>
        public static bool Dress(Transform terrain, MapEntry entry, MapSettingsManager manager)
        {
            MapManifest manifest = entry.Manifest;
            Material donor = DonorMaterial(manager);
            var colour = entry.Bundle.LoadAsset<Texture2D>("macro");
            if (donor == null || colour == null)
            {
                Plugin.Logger?.LogWarning("[Maps] " + manifest.displayName + " keeps its plain ground: " +
                    (donor == null ? "no vanilla terrain material was found." : "the bundle has no whole-map colour."));
                return false;
            }
            var material = new Material(donor) { name = "BoscaliMapTerrain", hideFlags = HideFlags.DontUnloadUnusedAsset };
            entry.Owned.Add(material);
            if (!listed)
            {
                listed = true;
                Plugin.Logger?.LogInfo("[Maps] Ground uses the game's " + donor.shader.name + ": textures " + string.Join(", ", donor.GetTexturePropertyNames()) +
                    "; breakup terrain " + Float(donor, BreakupTerrain) + ", normal " + Float(donor, BreakupNormal) + ", splat " + Float(donor, BreakupSplat) + ".");
                var scalars = new List<string>();
                for (int i = 0; i < donor.shader.GetPropertyCount(); i++)
                {
                    ShaderPropertyType type = donor.shader.GetPropertyType(i);
                    string one = donor.shader.GetPropertyName(i);
                    if (type == ShaderPropertyType.Float || type == ShaderPropertyType.Range)
                        scalars.Add(one + "=" + donor.GetFloat(one).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
                    else if (type == ShaderPropertyType.Vector)
                        scalars.Add(one + "=" + donor.GetVector(one).ToString("F3"));
                }
                Plugin.Logger?.LogInfo("[Maps] Terrain material scalars: " + string.Join(", ", scalars) + "; keywords: " + string.Join(",", donor.shaderKeywords) + ".");
            }
            if (Has(material, "_macro_basecolor")) material.SetTexture("_macro_basecolor", colour);
            foreach ((string texture, string property, int fallback) in Layers)
            {
                if (!Has(material, property)) continue;
                var layer = entry.Bundle.LoadAsset<Texture2D>(texture);
                material.SetTexture(property, layer != null ? layer : fallback == 2 ? Texture2D.normalTexture : fallback == 1 ? Texture2D.whiteTexture : Texture2D.blackTexture);
            }
            if (manifest.detailScale > 0f && Has(material, "_detail_scale"))
            {
                Vector4 scale = material.GetVector("_detail_scale");
                scale.x = scale.y = manifest.detailScale;
                material.SetVector("_detail_scale", scale);
            }
            MapManifest.Breakup breakup = manifest.breakup ?? new MapManifest.Breakup();
            SetBreakup(material, BreakupTerrain, breakup.terrain);
            SetBreakup(material, BreakupNormal, breakup.normal);
            SetBreakup(material, BreakupSplat, breakup.splat);
            foreach (KeyValuePair<string, string> skin in manifest.skins ?? new Dictionary<string, string>())
                Skin(material, entry, skin.Key, skin.Value);
            if (SkinSlots.TryGetValue("sand", out (string[] albedo, string[] normal) sandIds) && reported.Add("readback"))
                foreach (string id in sandIds.albedo)
                {
                    string property = Sample + id + SampleEnd;
                    if (Has(material, property) && material.GetTexture(property) is Texture dressed)
                        Plugin.Logger?.LogInfo("[Maps] The sand ground detail is " + dressed.name + " (" + dressed.width + "x" + dressed.height + ").");
                }
            foreach (MeshRenderer renderer in terrain.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name == "ice") continue; // the bake's ice skin, drawn over the ground with its own shader
                renderer.sharedMaterial = material;
                renderer.renderingLayerMask |= 3; // the layers scorch marks and craters are drawn on
            }
            return true;
        }

        /// <summary>One ground skin: the bundle's <c>skin_&lt;name&gt;_b/_n</c> into the slot's detail albedo and normal.</summary>
        private static void Skin(Material material, MapEntry entry, string slot, string name)
        {
            if (string.IsNullOrEmpty(name)) return;
            if (!SkinSlots.TryGetValue(slot, out (string[] albedo, string[] normal) ids))
            {
                if (reported.Add("slot " + slot)) Plugin.Logger?.LogWarning("[Maps] Ground skin slot '" + slot + "' is not one this build knows; skipped.");
                return;
            }
            var albedo = entry.Bundle.LoadAsset<Texture2D>("skin_" + name + "_b");
            var normal = entry.Bundle.LoadAsset<Texture2D>("skin_" + name + "_n");
            if (albedo == null)
            {
                Plugin.Logger?.LogWarning("[Maps] " + entry.Manifest.displayName + " has no skin_" + name + "_b; the " + slot + " ground keeps the game's texture.");
                return;
            }
            foreach (string id in ids.albedo)
                if (Has(material, Sample + id + SampleEnd)) material.SetTexture(Sample + id + SampleEnd, albedo);
            foreach (string id in ids.normal)
                if (Has(material, Sample + id + SampleEnd)) material.SetTexture(Sample + id + SampleEnd, normal != null ? normal : Texture2D.normalTexture);
            // The fields layer turns its texture by a map-wide angle picture made for the vanilla farmland: flatten it.
            if (slot == "fields" && Has(material, Sample + FieldsAngle + SampleEnd)) material.SetTexture(Sample + FieldsAngle + SampleEnd, Texture2D.grayTexture);
        }

        private static void SetBreakup(Material material, string property, float value)
        {
            if (value >= 0f && Has(material, property)) material.SetFloat(property, value);
        }

        private static string Float(Material material, string property) =>
            material.HasProperty(property) ? material.GetFloat(property).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture) : "none";

        /// <summary>True when the material has the property; a missing one is logged once for the whole session.</summary>
        private static bool Has(Material material, string property)
        {
            if (material.HasProperty(property)) return true;
            if (reported.Add(material.shader.name + "." + property))
                Plugin.Logger?.LogWarning("[Maps] The game's " + material.shader.name + " has no " + property + "; that part of the map's look is skipped.");
            return false;
        }

        private static Material DonorMaterial(MapSettingsManager manager)
        {
            PhysicMaterial ground = GameAssets.i != null ? GameAssets.i.terrainMaterial : null;
            foreach (MapSettingsManager.Map map in manager.Maps)
            {
                if (map == null || map.Prefab == null || IsOurs(map.Prefab)) continue;
                foreach (MeshCollider collider in map.Prefab.GetComponentsInChildren<MeshCollider>(true))
                {
                    if (collider.sharedMaterial != ground || !collider.TryGetComponent(out MeshRenderer renderer) || renderer.sharedMaterial == null) continue;
                    if (!listed) Plugin.Logger?.LogInfo("[Maps] The ground is borrowed from the game's " + map.Prefab.name + ".");
                    return renderer.sharedMaterial;
                }
            }
            return null;
        }

        /// <summary>True for a template of one of the mod's maps: never a donor of vanilla parts.</summary>
        internal static bool IsOurs(MapSettings prefab) => MapCatalog.All.Any(e => e.Template == prefab);

        /// <summary>
        /// When the map has been put in the scene: copy the shipped map's grass, tree, height-bake, blast and shader-global
        /// objects onto it. They are copied inactive and rewired before they wake, because they are scene singletons that
        /// register against whatever map they point at when they start.
        /// </summary>
        public static void Attach(MapSettings instance)
        {
            if (instance == null) return;
            MapEntry entry = MapCatalog.All.FirstOrDefault(e => instance.name == e.Manifest.prefabName ||
                instance.name.StartsWith(e.Manifest.prefabName + "(", StringComparison.Ordinal));
            if (entry == null) return;
            entry.Live = instance;
            MapSettingsManager manager = SceneSingleton<MapSettingsManager>.i;
            MapCatalog.CloseOthers(entry, manager); // the map this one replaced has left the scene by now
            if (instance.GetComponentInChildren<TerrainHeightMap>(true) != null) return;
            MapManifest manifest = entry.Manifest;
            MapSettings donor = manager == null ? null : manager.Maps.Where(m => m != null && m.Prefab != null && !IsOurs(m.Prefab))
                .Select(m => m.Prefab).FirstOrDefault(p => p.GetComponentInChildren<DetailRenderer>(true) != null);
            if (donor == null)
            {
                Plugin.Logger?.LogWarning("[Maps] No shipped map to copy grass and trees from; " + manifest.displayName + " stays bare.");
                return;
            }
            var hosts = new List<GameObject>();
            foreach (Component part in donor.GetComponentsInChildren<TerrainHeightMap>(true).Cast<Component>()
                .Concat(donor.GetComponentsInChildren<DetailRenderer>(true)).Concat(donor.GetComponentsInChildren<BlastManager>(true))
                .Concat(donor.GetComponentsInChildren<ShaderGlobalManager>(true)))
                if (!hosts.Contains(part.gameObject)) hosts.Add(part.gameObject);
            hosts.RemoveAll(h => hosts.Any(other => other != h && h.transform.IsChildOf(other.transform))); // a host inside another is copied with it

            var holder = new GameObject("BoscaliMapSystems");
            holder.SetActive(false);
            holder.transform.SetParent(instance.transform, false);
            List<GameObject> copies = hosts.Select(h => Object.Instantiate(h, holder.transform)).ToList();
            for (int i = 0; i < copies.Count; i++) copies[i].name = hosts[i].name;

            var heights = holder.GetComponentInChildren<TerrainHeightMap>(true);
            var detail = holder.GetComponentInChildren<DetailRenderer>(true);
            var blast = holder.GetComponentInChildren<BlastManager>(true);
            if (heights == null || detail == null || blast == null)
            {
                Object.Destroy(holder);
                Plugin.Logger?.LogWarning("[Maps] The shipped map's grass and tree objects were not all found; " + manifest.displayName + " stays bare.");
                return;
            }
            Set(heights, "mapSettings", instance);
            heights.AutoSearchRoot = instance.transform.Find("Terrain");
            heights.AutoFindTerrain = true;
            heights.AutoFindBlockers = false;
            heights.detailRenderer = detail;
            Set(blast, "mapSettings", instance);
            foreach (ShaderGlobalManager globals in holder.GetComponentsInChildren<ShaderGlobalManager>(true)) Set(globals, "mapSettings", instance);
            foreach (TerrainScatter scatter in holder.GetComponentsInChildren<TerrainScatter>(true)) Set(scatter, "mapSettings", instance);
            Set(detail, "blastManager", blast);
            Set(detail, "terrainHeight", heights);

            var mask = entry.Bundle.LoadAsset<Texture2D>("grass_mask");
            MapManifest.Grass cover = manifest.grass ?? new MapManifest.Grass();
            foreach (GrassRenderer grass in holder.GetComponentsInChildren<GrassRenderer>(true))
            {
                Set(grass, "detailRenderer", detail);
                Set(grass, "terrainHeightMap", heights);
                if (mask != null && Get(grass, "lushMaps") is Texture2D[] maps) Set(grass, "lushMaps", maps.Select(_ => mask).ToArray());
                GrassRenderer.GrassConfig[] blades = grass.config ?? new GrassRenderer.GrassConfig[0]; // this copy's own array
                for (int i = 0; i < blades.Length; i++)
                {
                    blades[i].LushMultiplier *= Mathf.Max(0f, cover.density);
                    if (cover.tints != null && i < cover.tints.Length) blades[i].BaseColor = MapCatalog.Rgb(cover.tints[i], blades[i].BaseColor);
                }
            }
            int planted = Forests(entry, detail, out int species);

            foreach (GameObject copy in copies) copy.transform.SetParent(instance.transform, false);
            Object.Destroy(holder);
            Plugin.Logger?.LogInfo("[Maps] " + manifest.displayName + " has the game's grass and " + planted + " forest trees of " + species + " species.");
        }

        /// <summary>
        /// One tree renderer per forest of the manifest: the shipped map's first, copied for the others, each with its
        /// own positions, colours and (when the forest names one) the kit's tree mesh. A forest with no trees gets no
        /// renderer, because a tree renderer without positions throws every frame and takes the grass down with it.
        /// </summary>
        private static int Forests(MapEntry entry, DetailRenderer detail, out int species)
        {
            TreeRenderer[] donors = Get(detail, "treeRenderers") as TreeRenderer[] ?? new TreeRenderer[0];
            TreeRenderer first = donors.FirstOrDefault(t => t != null);
            var kept = new List<TreeRenderer>();
            int planted = 0;
            if (first != null)
            {
                var donorMesh = Get(first, "mesh") as Mesh;
                var donorMaterial = Get(first, "material") as Material;
                object donorRadius = Get(first, "meshRadius");
                var lush = entry.Bundle.LoadAsset<Texture2D>("splat_lush");
                bool custom = CustomTreeMeshes;
                MapManifest.Forest[] forests = entry.Manifest.forests ?? new MapManifest.Forest[0];
                for (int i = 0; i < forests.Length; i++)
                {
                    MapManifest.Forest forest = forests[i];
                    TextAsset positions = forest != null ? entry.Bundle.LoadAsset<TextAsset>("forest_" + i) : null;
                    int bytes = positions != null ? positions.bytes.Length : 0;
                    if (bytes < 12 || bytes % 12 != 0 || donorMaterial == null) continue;
                    TreeRenderer tree = kept.Count == 0 ? first : Copy(first);
                    tree.PositionData = positions;
                    // The game keeps room for a tenth of a forest on screen, enough on its own wide maps. A whole island
                    // is in view at once here; past the room the draw reads beyond the buffer and trees flicker.
                    tree.VisibleBufferSizePercent = 1f;
                    if (lush != null) tree.LushMap = lush;
                    tree.AltitudeRange = new MinMax(forest.altitudeMin, forest.altitudeMax);

                    KitManifest.Kind kind = custom && !string.IsNullOrEmpty(forest.mesh) ? MapKitCatalog.Manifest.Find(forest.mesh) : null;
                    GameObject shape = kind != null ? MapKitCatalog.Prototype(kind.name, 0) : null;
                    MeshFilter filter = shape != null ? shape.GetComponentInChildren<MeshFilter>(true) : null;
                    Mesh mesh = filter != null ? filter.sharedMesh : null;
                    if (mesh == null && custom && !string.IsNullOrEmpty(forest.mesh) && reported.Add("tree " + forest.mesh))
                        Plugin.Logger?.LogWarning("[Maps] The scenery kit has no tree mesh '" + forest.mesh + "'; that forest uses the game's own tree.");
                    Set(tree, "mesh", mesh != null ? mesh : donorMesh);
                    // The kit radius is at scale 1; the shader scales each tree up to scaleMax, and culling must cover that.
                    Set(tree, "meshRadius", mesh != null ? (object)(kind.radius * Mathf.Max(1f, forest.scaleMax)) : donorRadius);
                    Set(tree, "material", TreeMaterial(entry, i, forest, donorMaterial, mesh != null));
                    kept.Add(tree);
                    planted += bytes / 12;
                }
            }
            foreach (TreeRenderer spare in donors)
                if (spare != null && !kept.Contains(spare)) Object.Destroy(spare);
            Set(detail, "treeRenderers", kept.ToArray());
            species = kept.Count;
            return planted;
        }

        /// <summary>A forest's own copy of the game's tree material; made once per map build and reused on every load.</summary>
        private static Material TreeMaterial(MapEntry entry, int index, MapManifest.Forest forest, Material donor, bool custom)
        {
            int key = index * 2 + (custom ? 1 : 0);
            if (entry.TreeMaterials.TryGetValue(key, out Material made) && made != null) return made;
            made = new Material(donor) { name = "BoscaliMapTree_" + index, hideFlags = HideFlags.DontUnloadUnusedAsset };
            entry.Owned.Add(made);
            entry.TreeMaterials[key] = made;
            if (forest.tint != null && Has(made, "_BaseColor")) made.SetColor("_BaseColor", MapCatalog.Rgb(forest.tint, Color.white));
            if (!custom) return made;
            var albedo = MapKitCatalog.Asset<Texture2D>(forest.texture);
            if (albedo != null)
            {
                var normal = MapKitCatalog.Asset<Texture2D>(forest.texture + "_n");
                if (normal == null) normal = MapKitCatalog.Asset<Texture2D>("tree_conifer_n");
                if (Has(made, "_BaseMap")) made.SetTexture("_BaseMap", albedo);
                if (Has(made, "_BumpMap")) made.SetTexture("_BumpMap", normal != null ? normal : Texture2D.normalTexture);
            }
            if (forest.lushColor != null && Has(made, "_ColorLush")) made.SetColor("_ColorLush", MapCatalog.Rgb(forest.lushColor, Color.white));
            if (forest.altitudeColor != null && Has(made, "_ColorAltitude")) made.SetColor("_ColorAltitude", MapCatalog.Rgb(forest.altitudeColor, Color.white));
            if (forest.scaleMax > 0f && Has(made, "_ScaleMinMax")) made.SetVector("_ScaleMinMax", new Vector4(forest.scaleMin, forest.scaleMax, 0f, 0f));
            return made;
        }

        /// <summary>A second tree renderer next to the first, with every setting of the first (they are plain fields; the
        /// GPU buffers are made later, when the detail renderer starts).</summary>
        private static TreeRenderer Copy(TreeRenderer first)
        {
            TreeRenderer copy = first.gameObject.AddComponent<TreeRenderer>();
            foreach (FieldInfo field in typeof(TreeRenderer).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                field.SetValue(copy, field.GetValue(first));
            return copy;
        }

        private static object Get(object target, string field) => AccessTools.Field(target.GetType(), field)?.GetValue(target);

        private static void Set(object target, string field, object value)
        {
            var info = AccessTools.Field(target.GetType(), field);
            if (info == null) throw new MissingFieldException(target.GetType().Name, field);
            info.SetValue(target, value);
        }
    }
}
