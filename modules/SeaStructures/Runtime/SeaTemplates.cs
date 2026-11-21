using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Util;
using BoscaliSummer.Modules.SeaStructures.Domain;
using HarmonyLib;
using Mirage;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.SeaStructures.Runtime
{
    /// <summary>
    /// Meshes and one shared material per model. Version 2 models use embedded albedo, tangent normal and surface
    /// atlases; legacy models retain their palette/shade ramp. Damaged sections keep the same UVs and material.
    /// </summary>
    internal static class SeaArt
    {
        private const int Column = 4, Rows = 32;
        private static readonly Dictionary<SeaKind, Material> Materials = new Dictionary<SeaKind, Material>();
        private static readonly Dictionary<int, Mesh> Full = new Dictionary<int, Mesh>();
        private static Texture2D detailAlbedo, detailNormal;

        /// <summary>Point the domain at the embedded model files (idempotent).</summary>
        public static void Bind()
        {
            if (SeaLayouts.Source == null)
                SeaLayouts.Source = file => EmbeddedResources.ReadAll(typeof(SeaArt).Assembly, "BoscaliSummer.Sea." + file, 64 * 1024 * 1024);
        }

        public static Material Material(SeaLayout layout)
        {
            if (Materials.TryGetValue(layout.Kind, out Material known) && known != null) return known;
            Texture2D albedo = null, normal = null, surface = null;
            Material material = null;
            try
            {
                if (layout.Version >= 2)
                {
                    albedo = Atlas(layout.Key, "albedo", false);
                    normal = Atlas(layout.Key, "normal", true);
                    surface = Atlas(layout.Key, "surface", true);
                }
                else LegacyTextures(layout, out albedo, out surface);
                Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(lit) { name = "BoscaliSummer.Sea." + layout.Key, hideFlags = HideFlags.HideAndDontSave };
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", albedo);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", albedo);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
                if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
                if (normal != null && material.HasProperty("_BumpMap"))
                {
                    material.SetTexture("_BumpMap", normal);
                    material.SetFloat("_BumpScale", 1f);
                    material.EnableKeyword("_NORMALMAP");
                }
                if (material.HasProperty("_MetallicGlossMap"))
                {
                    material.SetTexture("_MetallicGlossMap", surface);
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                    material.EnableKeyword("_METALLICGLOSSMAP");
                }
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1f);
                if (material.HasProperty("_GlossMapScale")) material.SetFloat("_GlossMapScale", 1f);
                if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
                if (layout.Version >= 2)
                {
                    if (detailAlbedo == null) detailAlbedo = Atlas("Sea", "detail_albedo", true, true);
                    if (detailNormal == null) detailNormal = Atlas("Sea", "detail_normal", true, true);
                    // Opaque base alpha stores the material mask: glass and tiny palette fittings receive no grain.
                    material.SetTexture("_DetailMask", albedo);
                    material.SetTexture("_DetailAlbedoMap", detailAlbedo);
                    material.SetTexture("_DetailNormalMap", detailNormal);
                    material.SetTextureScale("_DetailAlbedoMap", new Vector2(32f, 32f));
                    material.SetFloat("_DetailNormalMapScale", 0.45f);
                    if (material.HasProperty("_DetailAlbedoMapScale"))
                    {
                        material.SetFloat("_DetailAlbedoMapScale", 0.30f);
                        material.EnableKeyword("_DETAIL_SCALED");
                    }
                    else
                    {
                        material.SetFloat("_UVSec", 0f); // Standard uses UV0 and its unscaled multiply-by-two variant.
                        material.EnableKeyword("_DETAIL_MULX2");
                    }
                    if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 0f);
                    if (material.HasProperty("_AlphaClip")) material.SetFloat("_AlphaClip", 0f);
                }
                Materials[layout.Kind] = material;
                return material;
            }
            catch
            {
                if (material != null) UnityEngine.Object.Destroy(material);
                if (albedo != null) UnityEngine.Object.Destroy(albedo);
                if (normal != null) UnityEngine.Object.Destroy(normal);
                if (surface != null) UnityEngine.Object.Destroy(surface);
                throw;
            }
        }

        private static Texture2D Atlas(string key, string map, bool linear, bool repeat = false)
        {
            string resource = "BoscaliSummer.Sea.Textures." + key + "_" + map + ".png";
            byte[] bytes = EmbeddedResources.ReadAll(typeof(SeaArt).Assembly, resource, 64 * 1024 * 1024);
            if (bytes == null) throw new InvalidDataException("missing sea texture " + resource);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear)
            {
                name = key + "_" + map, hideFlags = HideFlags.HideAndDontSave,
                wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8
            };
            int size = repeat ? 1024 : 2048;
            if (!ImageConversion.LoadImage(texture, bytes, false) || texture.width != size || texture.height != size)
            {
                UnityEngine.Object.Destroy(texture);
                throw new InvalidDataException("invalid " + size + "px sea texture " + resource);
            }
            texture.Apply(true, false);
            // Native GPU compression keeps the colour/mask atlases small. Preserve normal gradients instead
            // of feeding an RGB normal map through colour-block compression; its alpha stays 1 for URP RG/AG unpacking.
            if (!map.EndsWith("normal") && SystemInfo.SupportsTextureFormat(TextureFormat.DXT5)) texture.Compress(true);
            texture.Apply(false, true);
            return texture;
        }

        private static void LegacyTextures(SeaLayout layout, out Texture2D ramp, out Texture2D surface)
        {
            ramp = new Texture2D(layout.Paints * Column, Rows, TextureFormat.RGB24, false, true)
            {
                name = "BoscaliSummer.Sea.Ramp", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            // Use the same palette UVs for surface response: glazing, bare steel and matte paint remain one draw material.
            surface = new Texture2D(layout.Paints * Column, 1, TextureFormat.RGBA32, false, true)
            {
                name = "BoscaliSummer.Sea.Surface", hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            for (int p = 0; p < layout.Paints; p++)
            {
                float smooth = p == 7 ? 0.68f : p == 2 ? 0.38f : p == 5 ? 0.08f : p == 6 || p == 15 ? 0.14f : 0.24f;
                var response = new Color(p == 2 ? 1f : 0f, 0f, 0f, smooth);
                for (int x = 0; x < Column; x++) surface.SetPixel(p * Column + x, 0, response);
            }
            surface.Apply(false, true);
            for (int p = 0; p < layout.Paints; p++)
                for (int row = 0; row < Rows; row++)
                {
                    float k = (0.2f + 0.8f * row / (Rows - 1)) / 255f;
                    var colour = new Color(layout.Palette[p * 3] * k, layout.Palette[p * 3 + 1] * k, layout.Palette[p * 3 + 2] * k);
                    for (int x = 0; x < Column; x++) ramp.SetPixel(p * Column + x, row, colour);
                }
            ramp.Apply(false, true);
        }

        /// <summary>The undamaged mesh of a section, shared.</summary>
        public static Mesh Whole(SeaLayout layout, int section)
        {
            int key = (int)layout.Kind * 1000 + section;
            if (Full.TryGetValue(key, out Mesh mesh) && mesh != null) return mesh;
            mesh = ToMesh(SeaMesh.Build(layout.Sections[section], 0, 100f), layout.Key + ":" + section);
            Full[key] = mesh;
            return mesh;
        }

        /// <summary>The undamaged mesh of one damage point.</summary>
        public static Mesh Point(SeaLayout layout, SeaSection section, SeaPointPlan point, int index)
        {
            return ToMesh(SeaMesh.Point(section, point.Pieces, 1f), layout.Key + ":p" + index);
        }

        public static Mesh ToMesh(SeaMesh.Data data, string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            if (data.Vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetUVs(0, data.Uv);
            mesh.SetTriangles(data.Triangles, 0);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// The two structure prefabs. Each is a vanilla warship cloned at runtime and refitted: its hull parts become the
    /// structure's sections (moved, re-boxed, given a mesh built in code), its turrets move onto the hardpoints and keep
    /// their own models, and the hull is pinned in place. Everything that matters in a fight stays the game's own: weapons,
    /// fire control, per-part damage, sections breaking off, flooding and sinking, and their replication. Every peer builds
    /// the same clone and registers it under a fixed prefab hash, so the host's spawn messages resolve on clients. The
    /// clone has its own definition (definitions go over the network by Encyclopedia index).
    /// </summary>
    internal static class SeaTemplates
    {
        public const string MeshPrefix = "BSS";
        private static readonly int[] Hashes = { 0x42535231, 0x42534631 }; // "BSR1", "BSF1": identical on every peer
        private static readonly FieldInfo Disintegrate = AccessTools.Field(typeof(UnitPart), "disintegrateObjects");
        private static readonly FieldInfo CriticalPart = AccessTools.Field(typeof(UnitPart), "criticalPart");
        private static readonly FieldInfo Structural = AccessTools.Field(typeof(UnitPart), "structuralThreshold");
        private static readonly FieldInfo Armor = AccessTools.Field(typeof(UnitPart), "armorProperties");
        private static readonly FieldInfo DamageMaterialField = AccessTools.Field(typeof(UnitPart), "damageMaterial");
        private static readonly FieldInfo TurretStations = AccessTools.Field(typeof(Turret), "weaponStations");
        private static readonly FieldInfo TurretParts = AccessTools.Field(typeof(Turret), "criticalParts");
        private static readonly FieldInfo LaunchPoints = AccessTools.Field(typeof(MissileLauncher), "launchTransforms");

        private static readonly GameObject[] Templates = new GameObject[2];
        private static readonly string[] DonorKeys = new string[2];
        private static bool tried;
        private static GameObject holder;
        private static ClientObjectManager registeredWith;

        public static GameObject Of(SeaKind kind)
        {
            EnsureDefinitions(GameAccess.EncyclopediaOrNull);
            return Templates[(int)kind];
        }

        public static string Donor(SeaKind kind) => DonorKeys[(int)kind];

        /// <summary>Register both prefabs with this peer's client (cheap to call often). <paramref name="force"/>
        /// re-registers after the game rebuilt its own prefab table.</summary>
        public static void EnsureRegistered(bool force = false)
        {
            NetworkManagerNuclearOption manager = GameAccess.NetworkManagerOrNull;
            ClientObjectManager objects = manager != null ? manager.ClientObjectManager : null;
            if (objects == null) return;
            EnsureDefinitions(GameAccess.EncyclopediaOrNull);
            if (!tried) return;
            if (objects == registeredWith && !force) return;
            foreach (GameObject template in Templates)
                if (template != null) objects.RegisterPrefab(template.GetComponent<NetworkIdentity>());
            registeredWith = objects;
        }

        internal static void EnsureDefinitions(Encyclopedia encyclopedia)
        {
            if (encyclopedia == null) return;
            if (!tried) Build(encyclopedia);
            SeaFacility.RegisterDefinitions(encyclopedia, Templates);
        }

        private static void Build(Encyclopedia encyclopedia)
        {
            tried = true;
            SeaArt.Bind();
            Debug.Log("[SeaStructures] building templates from " + encyclopedia.ships.Count + " ships");
            holder = new GameObject("SeaStructureTemplates");
            holder.SetActive(false); // children never wake: spawning instantiates live copies
            UnityEngine.Object.DontDestroyOnLoad(holder);
            foreach (SeaKind kind in new[] { SeaKind.OilRig, SeaKind.SeaFort })
            {
                try
                {
                    // A donor with a hull part for most sections, so each breaks off on its own; any armed ship failing that.
                    ShipDefinition donor = Pick(encyclopedia.ships, kind == SeaKind.SeaFort, 10) ?? Pick(encyclopedia.ships, kind == SeaKind.SeaFort, 6);
                    if (donor == null) { Debug.LogWarning("[SeaStructures] no warship to refit as " + kind); continue; }
                    SeaLayout layout = SeaLayouts.Of(kind);
                    if (layout == null) { Debug.LogWarning("[SeaStructures] the model of " + kind + " is not embedded; run modules/SeaStructures/Assets/Source"); continue; }
                    Templates[(int)kind] = Refit(encyclopedia, layout, donor, Hashes[(int)kind]);
                    DonorKeys[(int)kind] = donor.jsonKey;
                }
                catch (Exception error) { Debug.LogError("[SeaStructures] refit " + kind + ": " + error); }
            }
        }

        /// <summary>The fort takes the frigate (else the most heavily armed escort); the rig the most lightly armed ship.</summary>
        private static ShipDefinition Pick(List<ShipDefinition> ships, bool fort, int minParts)
        {
            ShipDefinition best = null;
            int bestScore = int.MaxValue;
            foreach (ShipDefinition def in ships)
            {
                GameObject p = def != null ? def.unitPrefab : null;
                if (p == null || p.GetComponent<Ship>() == null || p.GetComponent<ShipPart>() == null) continue;
                int turrets = p.GetComponentsInChildren<Turret>(true).Length, parts = p.GetComponentsInChildren<ShipPart>(true).Length;
                if (turrets == 0 || parts < minParts || p.GetComponent<Airbase>() == null ||
                    p.GetComponentsInChildren<Hangar>(true).Length != 1 || def.shipType == SeaFacility.PlatformType) continue;
                if (fort && def.jsonKey == "Frigate1") return def;
                int score = (fort ? -turrets : turrets) * 100 - parts;
                if (score < bestScore) { bestScore = score; best = def; }
            }
            return best;
        }

        private static GameObject Refit(Encyclopedia encyclopedia, SeaLayout layout, ShipDefinition donor, int hash)
        {
            GameObject go = UnityEngine.Object.Instantiate(donor.unitPrefab, holder.transform);
            go.name = layout.Key;
            Transform root = go.transform;
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            Ship ship = go.GetComponent<Ship>();
            ShipPart rootPart = go.GetComponent<ShipPart>();

            // What the donor carries: turret mounts (a turret with whatever sits between it and its hull part), other
            // equipment parts (magazines, cell blocks) and the hull parts themselves.
            var mounts = new List<Transform>();
            var turretOf = new Dictionary<Transform, Transform>();
            var kinds = new Dictionary<Transform, MountKind>();
            var controllers = new HashSet<Transform>();
            var fixedLaunchers = new HashSet<Transform>();
            foreach (Turret turret in go.GetComponentsInChildren<Turret>(true))
            {
                Transform r = turret.transform;
                if (r == root) continue;
                while (r.parent != root && r.parent.GetComponent<ShipPart>() == null) r = r.parent;
                if (Under(r, mounts)) continue;
                mounts.RemoveAll(m => m.IsChildOf(r));
                mounts.Add(r);
                turretOf[r] = turret.transform;
                bool missile = r.GetComponentInChildren<MissileLauncher>(true) != null;
                // Frigate CMLauncher controls launchers on separate hull parts. Its transform is only fire control,
                // so keep the controller but do not spend a physical hardpoint on it or classify it as a gun.
                if (TurretStations?.GetValue(turret) is WeaponStation[] stations)
                    foreach (WeaponStation station in stations)
                        foreach (Weapon weapon in station.Weapons)
                            missile |= weapon is MissileLauncher;
                kinds[r] = missile ? MountKind.Missile : MountKind.Gun;
                if (missile && r.GetComponentInChildren<Weapon>(true) == null) controllers.Add(r);
            }
            // Preserve the actual fixed cruise launch assemblies too: these are referenced by WeaponStation rather
            // than parented below its Turret. Leaving them on the donor hull moves their launch points into the island.
            foreach (MissileLauncher launcher in go.GetComponentsInChildren<MissileLauncher>(true))
            {
                if (Under(launcher.transform, mounts)) continue;
                Transform r = launcher.transform;
                if (LaunchPoints?.GetValue(launcher) is Transform[] points)
                    foreach (Transform point in points)
                        if (point != null && !point.IsChildOf(r)) point.SetParent(r, true);
                mounts.Add(r);
                turretOf[r] = r;
                kinds[r] = MountKind.Missile;
                fixedLaunchers.Add(r);
            }
            var stores = new List<Transform>();
            var hull = new List<ShipPart>();
            foreach (UnitPart part in go.GetComponentsInChildren<UnitPart>(true))
            {
                if (part == rootPart || Under(part.transform, mounts)) continue;
                if (part is ShipPart shipPart) hull.Add(shipPart);
                else if (!Under(part.transform, stores)) stores.Add(part.transform);
            }
            var kept = new List<Transform>(mounts);
            kept.AddRange(stores);

            // Looks: the hull goes, the turrets stay (their top level of detail only).
            var low = new HashSet<Renderer>();
            var top = new HashSet<Renderer>();
            foreach (LODGroup group in go.GetComponentsInChildren<LODGroup>(true))
            {
                LOD[] lods = group.GetLODs();
                for (int i = 0; i < lods.Length; i++)
                    foreach (Renderer r in lods[i].renderers)
                        if (r != null) (i == 0 ? top : low).Add(r);
                UnityEngine.Object.DestroyImmediate(group);
            }
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                if (Under(r.transform, mounts) && (!low.Contains(r) || top.Contains(r))) continue;
                // The flag alone does not hold (the game drives it on live units), so the renderer also loses its mesh. The
                // renderer itself stays: the part's damage material still writes to it.
                r.forceRenderingOff = true;
                MeshFilter filter = r.GetComponent<MeshFilter>();
                if (filter != null) filter.sharedMesh = null;
            }

            // No wake: it never moves.
            foreach (WaterEffect wake in go.GetComponentsInChildren<WaterEffect>(true)) wake.gameObject.SetActive(false);

            // Flatten: every mount, store and hull part hangs off the root, so "its children" means its own fittings.
            PhysicMaterial surface = null;
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                if (c.sharedMaterial != null) { surface = c.sharedMaterial; break; }
            foreach (Transform t in kept) t.SetParent(root, true);
            foreach (ShipPart part in hull) part.transform.SetParent(root, true);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true))
                if (!Under(c.transform, kept)) UnityEngine.Object.DestroyImmediate(c);

            // Hull parts to sections: the hangar to the pad, radar and lookouts up high, critical parts to what holds the
            // structure up, the rest in order.
            int n = layout.Sections.Length;
            var assigned = new ShipPart[n];
            assigned[0] = rootPart;
            var critical = new HashSet<ShipPart>(ship.criticalParts);
            Take(layout, assigned, hull, p => Has<Hangar>(p), s => s.Pad);
            Take(layout, assigned, hull, p => Has<Radar>(p), s => s.Sensor);
            Take(layout, assigned, hull, p => Has<TargetDetector>(p), s => s.Sensor);
            Take(layout, assigned, hull, critical.Contains, s => s.Critical);
            Take(layout, assigned, hull, critical.Contains, s => true);
            Take(layout, assigned, hull, p => true, s => true);

            // Damage plan: one spine per section plus local detail points, identical on every peer.
            var plan = SeaDamagePlans.Of(layout.Kind);
            Debug.Log("[SeaStructures] " + layout.Key + " plan: " + plan.Count + " points");
            var spinePlan = new SeaPointPlan[n];
            var spineIndex = new int[n];
            for (int i = 0; i < plan.Count; i++)
                if (plan[i].Spine) { spinePlan[plan[i].Section] = plan[i]; spineIndex[plan[i].Section] = i; }

            var node = new Transform[n];
            for (int i = 0; i < n; i++)
            {
                SeaSection section = layout.Sections[i];
                SeaPointPlan spine = spinePlan[i];
                UnitPart spinePart;
                if (assigned[i] != null)
                {
                    Transform t = assigned[i].transform;
                    if (i > 0) t.SetParent(node[section.Parent], false);
                    t.position = section.At;
                    t.rotation = Quaternion.identity;
                    t.localScale = Vector3.one;
                    node[i] = t;
                    spinePart = assigned[i];
                    Disintegrate?.SetValue(assigned[i], new GameObject[0]);
                }
                else
                {
                    // A section with no donor hull part of its own gets a plain spine part under the section
                    // below it. parentUnit is set explicitly so the game never has to infer it from the tree.
                    var holder = new GameObject("BSPINE" + i);
                    holder.transform.SetParent(node[section.Parent], false);
                    holder.transform.position = section.At;
                    holder.transform.rotation = Quaternion.identity;
                    node[i] = holder.transform;
                    spinePart = holder.AddComponent<UnitPart>();
                    ConfigurePoint(spinePart, node[section.Parent].GetComponent<UnitPart>(), spine, ship);
                }
                if (section.Pad)
                    foreach (SeaBox box in section.Boxes) PointVolume(node[i].gameObject, box.Centre, box.Size, surface);
                else
                    PointVolume(node[i].gameObject, spine.Centre, PointSize(spine), surface);
                PointMesh(node[i], spineIndex[i], "BSS", spine, section, layout, Vector3.zero);
                node[i].gameObject.AddComponent<SeaPointTag>().Index = spineIndex[i];

                for (int p = 0; p < plan.Count; p++)
                {
                    SeaPointPlan detail = plan[p];
                    if (detail.Spine || detail.Section != i) continue;
                    var part = new GameObject("BSP" + p);
                    part.layer = node[i].gameObject.layer;
                    part.transform.SetParent(node[i], false);
                    part.transform.localPosition = detail.Centre;
                    part.transform.localRotation = Quaternion.identity;
                    part.transform.localScale = Vector3.one;
                    PointVolume(part, Vector3.zero, PointSize(detail), surface);
                    var unit = part.AddComponent<UnitPart>();
                    ConfigurePoint(unit, spinePart, detail, ship);
                    part.AddComponent<SeaPointTag>().Index = p;
                    PointMesh(part.transform, p, "BSD", detail, section, layout, -detail.Centre);
                }
            }
            // Hull parts beyond the layout's sections sit inside the root's core, out of reach but still registered.
            for (int i = 0; i < hull.Count; i++)
            {
                Transform t = hull[i].transform;
                t.position = new Vector3(0f, -8f - 2f * i, 0f);
                t.rotation = Quaternion.identity;
                Box(t.gameObject, Vector3.zero, Vector3.one, surface);
            }

            // Turrets and magazines onto the hardpoints.
            var free = new List<int>();
            for (int i = 0; i < layout.Mounts.Length; i++) free.Add(i);
            int spare = 0;
            foreach (Transform mount in mounts)
            {
                MountKind kind = kinds[mount];
                int slot = free.FindIndex(i => layout.Mounts[i].Kind == kind);
                if (slot < 0) slot = free.FindIndex(i => layout.Mounts[i].Kind != MountKind.Store);
                SeaMount at;
                if (slot >= 0)
                {
                    at = layout.Mounts[free[slot]];
                    if (!controllers.Contains(mount)) free.RemoveAt(slot);
                }
                else
                {
                    // More turrets than hardpoints: double up beside the weapon hardpoints, 7 m along.
                    var weapons = new List<SeaMount>();
                    foreach (SeaMount point in layout.Mounts) if (point.Kind != MountKind.Store) weapons.Add(point);
                    SeaMount b = weapons[spare++ % weapons.Count];
                    at = new SeaMount(b.Section, b.At + new Vector3(0f, 0f, b.At.z > 0f ? -7f : 7f), b.Yaw, kind);
                }
                Place(mount, turretOf[mount], at, node[at.Section], fixedLaunchers.Contains(mount));
                // VLS/cruise apertures are part of the authored structure; their donor hull mesh was not a separate
                // weapon model. Keep native firing code and hit boxes without drawing an opaque magazine over it.
            }
            for (int i = 0; i < stores.Count; i++)
            {
                int slot = free.FindIndex(k => layout.Mounts[k].Kind == MountKind.Store);
                if (slot >= 0)
                {
                    SeaMount at = layout.Mounts[free[slot]];
                    free.RemoveAt(slot);
                    Place(stores[i], stores[i], at, node[at.Section]);
                    Dress(stores[i], layout);
                }
                else stores[i].position = new Vector3(4f + 2f * i, -8f, 0f);
            }

            // A control-only turret owns launchers elsewhere in the hierarchy. Keep its native magazine dependencies
            // and subscribe to each launcher's new supporting section when the serialized clone wakes on every peer.
            foreach (Transform controller in controllers)
            {
                Turret turret = turretOf[controller].GetComponent<Turret>();
                var dependencies = new List<UnitPart>((UnitPart[])TurretParts.GetValue(turret));
                foreach (WeaponStation station in turret.GetWeaponStations())
                    foreach (Weapon weapon in station.Weapons)
                    {
                        UnitPart owner = weapon.GetComponentInParent<UnitPart>(true); // the template holder is inactive
                        if (owner != null && !dependencies.Contains(owner)) dependencies.Add(owner);
                    }
                TurretParts.SetValue(turret, dependencies.ToArray());
            }

            // A part whose owner the game would look up through a grandparent that is no longer a part.
            foreach (UnitPart part in go.GetComponentsInChildren<UnitPart>(true))
                if (part.parentUnit == null && (part.transform.parent == null || part.transform.parent.parent == null ||
                                                part.transform.parent.parent.GetComponent<UnitPart>() == null))
                    part.parentUnit = ship;

            // The structure's own rules replace the ship's: no part is critical, nothing breaks off as a floating body
            // (SeaStructure removes a destroyed section in place) and the unit dies only when every section has.
            ship.criticalParts.Clear();
            foreach (UnitPart part in go.GetComponentsInChildren<UnitPart>(true))
            {
                CriticalPart?.SetValue(part, false);
                Structural?.SetValue(part, -1e9f);
            }

            // Static, with one native vertical flight facility on the actual authored deck.
            Rigidbody body = go.GetComponent<Rigidbody>();
            if (body != null) body.constraints = RigidbodyConstraints.FreezeAll;
            int padIndex = Array.FindIndex(layout.Sections, s => s.Pad);
            SeaFacility.Configure(encyclopedia, go, layout, donor, node[padIndex], assigned[padIndex] ?? node[padIndex].GetComponent<UnitPart>());
            go.AddComponent<SeaStructure>().Kind = (int)layout.Kind;
            go.GetComponent<NetworkIdentity>().PrefabHash = hash;

            int placed = 0;
            for (int i = 1; i < n; i++) if (assigned[i] != null) placed++;
            int units = go.GetComponentsInChildren<UnitPart>(true).Length;
            if (units > 200) Debug.LogWarning("[SeaStructures] " + layout.Key + " has " + units + " native parts; UnitPart ids are bytes");
            Debug.Log("[SeaStructures] " + layout.Key + " refitted from " + donor.jsonKey + ": " + placed + "/" + (n - 1) + " sections, " +
                      plan.Count + " damage points, " + mounts.Count + " turrets, " + stores.Count + " stores, " + hull.Count + " hull parts stowed");
            return go;
        }

        /// <summary>A new damage part: owner, armor and the game's damage material plumbing.</summary>
        private static void ConfigurePoint(UnitPart part, UnitPart template, SeaPointPlan point, Ship ship)
        {
            part.parentUnit = ship;
            part.mass = 5f;
            CriticalPart?.SetValue(part, false);
            Structural?.SetValue(part, -1e9f);
            Disintegrate?.SetValue(part, new GameObject[0]);
            DamageMaterialField?.SetValue(part, new DamageMaterial { renderers = new List<Renderer>(), indices = Array.Empty<byte>() });
            if (template == null) return;
            ArmorProperties armor = template.GetArmorProperties();
            float k = Mathf.Max(0.2f, point.Toughness);
            armor.pierceTolerance = Mathf.Max(0.01f, armor.pierceTolerance * k);
            armor.blastTolerance = Mathf.Max(0.01f, armor.blastTolerance * k);
            armor.fireTolerance = Mathf.Max(0.01f, armor.fireTolerance * k);
            Armor?.SetValue(part, armor);
        }

        private static void PointVolume(GameObject go, Vector3 centre, Vector3 size, PhysicMaterial surface)
        {
            BoxCollider box = go.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
            box.sharedMaterial = surface;
        }

        private static Vector3 PointSize(SeaPointPlan point)
        {
            Vector3 size = point.Size;
            return new Vector3(Mathf.Max(size.x, 0.6f), Mathf.Max(size.y, 0.6f), Mathf.Max(size.z, 0.6f));
        }

        private static void PointMesh(Transform parent, int index, string prefix, SeaPointPlan point, SeaSection section,
            SeaLayout layout, Vector3 local)
        {
            var mesh = new GameObject(prefix + index);
            mesh.layer = parent.gameObject.layer;
            mesh.transform.SetParent(parent, false);
            mesh.transform.localPosition = local;
            mesh.AddComponent<MeshFilter>().sharedMesh = SeaArt.Point(layout, section, point, index);
            mesh.AddComponent<MeshRenderer>().sharedMaterial = SeaArt.Material(layout);
        }

        private static void Take(SeaLayout layout, ShipPart[] assigned, List<ShipPart> pool, Predicate<ShipPart> part, Predicate<SeaSection> section)
        {
            for (int i = 1; i < assigned.Length; i++)
            {
                if (assigned[i] != null || !section(layout.Sections[i])) continue;
                int at = pool.FindIndex(part);
                if (at < 0) return;
                assigned[i] = pool[at];
                pool.RemoveAt(at);
            }
        }

        private static bool Has<T>(ShipPart part) where T : Component => part.GetComponentInChildren<T>(true) != null;

        private static bool Under(Transform t, List<Transform> roots)
        {
            foreach (Transform r in roots)
                if (t == r || t.IsChildOf(r)) return true;
            return false;
        }

        private static void Box(GameObject on, Vector3 centre, Vector3 size, PhysicMaterial surface)
        {
            BoxCollider box = on.AddComponent<BoxCollider>();
            box.center = centre;
            box.size = size;
            box.sharedMaterial = surface; // the game's blast traces stop at a collider with no material
        }

        /// <summary>Stand a mount on its hardpoint: pivot over the point, lowest mesh on the deck, facing the yaw.</summary>
        private static void Place(Transform mount, Transform pivot, SeaMount at, Transform section, bool preserveTilt = false)
        {
            mount.SetParent(section, true);
            if (preserveTilt)
            {
                float heading = Mathf.Atan2(mount.forward.x, mount.forward.z) * Mathf.Rad2Deg;
                mount.rotation = Quaternion.AngleAxis(at.Yaw - heading, Vector3.up) * mount.rotation;
            }
            else mount.rotation = Quaternion.Euler(0f, at.Yaw, 0f);
            float low = float.MaxValue;
            bool hatchPlane = false;
            foreach (MeshFilter filter in mount.GetComponentsInChildren<MeshFilter>(true))
            {
                Renderer r = filter.GetComponent<Renderer>();
                if (filter.sharedMesh == null || r == null || r.forceRenderingOff) continue;
                low = Mathf.Min(low, Lowest(filter.transform, filter.sharedMesh.bounds));
            }
            if (low == float.MaxValue && !preserveTilt)
                foreach (MissileLauncher launcher in mount.GetComponentsInChildren<MissileLauncher>(true))
                {
                    hatchPlane = true;
                    // A renderless VLS hardpoint denotes its hatch plane. The frigate's launch points are 1 m below
                    // its controller, below even the magazine hit-box bottom; aligning that box would bury launches.
                    if (LaunchPoints?.GetValue(launcher) is Transform[] points && points.Length > 0)
                    {
                        foreach (Transform point in points) if (point != null) low = Mathf.Min(low, point.position.y);
                    }
                    else low = Mathf.Min(low, launcher.transform.position.y);
                }
            if (low == float.MaxValue)
                foreach (BoxCollider box in mount.GetComponentsInChildren<BoxCollider>(true))
                    low = Mathf.Min(low, Lowest(box.transform, new Bounds(box.center, box.size)));
            if (low == float.MaxValue) low = pivot.position.y;
            mount.position += new Vector3(at.At.x - pivot.position.x, at.At.y - low, at.At.z - pivot.position.z);
            if (hatchPlane)
                foreach (BoxCollider box in mount.GetComponentsInChildren<BoxCollider>(true))
                {
                    // The native VLS damage volume is the magazine below the hatches, not an invisible obstacle
                    // above them after aligning the launch plane to the authored deck.
                    float top = box.transform.TransformPoint(box.center + Vector3.up * box.size.y * 0.5f).y;
                    box.center += box.transform.InverseTransformVector(Vector3.up * (at.At.y - top));
                }
        }

        private static float Lowest(Transform t, Bounds b)
        {
            float low = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                low = Mathf.Min(low, t.TransformPoint(corner).y);
            }
            return low;
        }

        /// <summary>A mount or store with nothing left to see (its model was part of the hull) gets a plain dark box per hit box.</summary>
        private static void Dress(Transform mount, SeaLayout layout)
        {
            foreach (Renderer r in mount.GetComponentsInChildren<Renderer>(true))
                if ((r is MeshRenderer || r is SkinnedMeshRenderer) && !r.forceRenderingOff) return;
            foreach (BoxCollider box in mount.GetComponentsInChildren<BoxCollider>(true))
            {
                var look = new GameObject("BSB");
                look.layer = box.gameObject.layer;
                look.transform.SetParent(box.transform, false);
                look.AddComponent<MeshFilter>().sharedMesh = SeaArt.ToMesh(SeaMesh.Box(box.center, box.size, layout.PaintUv(0, 0.8f)), "BoscaliSummer.Sea.Store");
                look.AddComponent<MeshRenderer>().sharedMaterial = SeaArt.Material(layout);
            }
        }
    }
}
