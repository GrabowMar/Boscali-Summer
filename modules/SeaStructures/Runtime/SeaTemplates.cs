using System;
using System.Collections.Generic;
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
    /// Meshes and the one material the sections share. The models come from the embedded .seamodel files
    /// (modules/SeaStructures/Assets, built in Blender by Assets/Source). Their paint is a palette: the mod draws it as a
    /// ramp texture, one column per paint, dark at the bottom, and each vertex's UV picks its paint and its baked shade.
    /// </summary>
    internal static class SeaArt
    {
        private const int Column = 4, Rows = 32;
        private static Material material;
        private static readonly Dictionary<int, Mesh> Full = new Dictionary<int, Mesh>();

        /// <summary>Point the domain at the embedded model files (idempotent).</summary>
        public static void Bind()
        {
            if (SeaLayouts.Source == null)
                SeaLayouts.Source = file => EmbeddedResources.ReadAll(typeof(SeaArt).Assembly, "BoscaliSummer.Sea." + file, 64 * 1024 * 1024);
        }

        public static Material Material(SeaLayout layout)
        {
            if (material != null) return material;
            // Both models carry the same palette (sealib.PALETTE), so one ramp and one material serve every structure.
            var ramp = new Texture2D(layout.Paints * Column, Rows, TextureFormat.RGB24, false, true)
            {
                name = "BoscaliSummer.Sea.Ramp", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            for (int p = 0; p < layout.Paints; p++)
                for (int row = 0; row < Rows; row++)
                {
                    float k = (0.2f + 0.8f * row / (Rows - 1)) / 255f;
                    var colour = new Color(layout.Palette[p * 3] * k, layout.Palette[p * 3 + 1] * k, layout.Palette[p * 3 + 2] * k);
                    for (int x = 0; x < Column; x++) ramp.SetPixel(p * Column + x, row, colour);
                }
            ramp.Apply(false, true);
            Shader lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            material = new Material(lit) { name = "BoscaliSummer.Sea.Paint", hideFlags = HideFlags.HideAndDontSave };
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", ramp);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", ramp);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.22f);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.1f);
            return material;
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

        public static Mesh ToMesh(SeaMesh.Data data, string name)
        {
            var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            if (data.Vertices.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.SetUVs(0, data.Uv);
            mesh.SetTriangles(data.Triangles, 0);
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
    /// clone keeps the donor's definition (definitions go over the network by Encyclopedia index).
    /// </summary>
    internal static class SeaTemplates
    {
        public const string MeshPrefix = "BSS";
        private static readonly int[] Hashes = { 0x42535231, 0x42534631 }; // "BSR1", "BSF1": identical on every peer
        private static readonly FieldInfo Disintegrate = AccessTools.Field(typeof(UnitPart), "disintegrateObjects");
        private static readonly FieldInfo CriticalPart = AccessTools.Field(typeof(UnitPart), "criticalPart");
        private static readonly FieldInfo Structural = AccessTools.Field(typeof(UnitPart), "structuralThreshold");
        private static readonly FieldInfo HangarAircraft =AccessTools.Field(typeof(Hangar), "availableAircraft");

        private static readonly GameObject[] Templates = new GameObject[2];
        private static readonly string[] DonorKeys = new string[2];
        private static bool tried;
        private static GameObject holder;
        private static ClientObjectManager registeredWith;

        public static GameObject Of(SeaKind kind)
        {
            Ensure();
            return Templates[(int)kind];
        }

        public static string Donor(SeaKind kind) => DonorKeys[(int)kind];

        /// <summary>Register both prefabs with this peer's client (cheap to call often). <paramref name="force"/>
        /// re-registers after the game rebuilt its own prefab table.</summary>
        public static void EnsureRegistered(bool force = false)
        {
            NetworkManagerNuclearOption manager = GameAccess.NetworkManagerOrNull;
            ClientObjectManager objects = manager != null ? manager.ClientObjectManager : null;
            if (objects == null || (objects == registeredWith && !force)) return;
            Ensure();
            if (!tried) return;
            foreach (GameObject template in Templates)
                if (template != null) objects.RegisterPrefab(template.GetComponent<NetworkIdentity>());
            registeredWith = objects;
        }

        private static void Ensure()
        {
            if (tried || GameAccess.EncyclopediaOrNull == null) return;
            tried = true;
            SeaArt.Bind();
            holder = new GameObject("SeaStructureTemplates");
            holder.SetActive(false); // children never wake: spawning instantiates live copies
            UnityEngine.Object.DontDestroyOnLoad(holder);
            foreach (SeaKind kind in new[] { SeaKind.OilRig, SeaKind.SeaFort })
            {
                try
                {
                    // A donor with a hull part for most sections, so each breaks off on its own; any armed ship failing that.
                    ShipDefinition donor = Pick(kind == SeaKind.SeaFort, 10) ?? Pick(kind == SeaKind.SeaFort, 6);
                    if (donor == null) { Debug.LogWarning("[SeaStructures] no warship to refit as " + kind); continue; }
                    SeaLayout layout = SeaLayouts.Of(kind);
                    if (layout == null) { Debug.LogWarning("[SeaStructures] the model of " + kind + " is not embedded; run modules/SeaStructures/Assets/Source"); continue; }
                    Templates[(int)kind] = Refit(layout, donor, Hashes[(int)kind]);
                    DonorKeys[(int)kind] = donor.jsonKey;
                }
                catch (Exception error) { Debug.LogError("[SeaStructures] refit " + kind + ": " + error); }
            }
        }

        /// <summary>The fort takes the frigate (else the most heavily armed escort); the rig the most lightly armed ship.</summary>
        private static ShipDefinition Pick(bool fort, int minParts)
        {
            ShipDefinition best = null;
            int bestScore = int.MaxValue;
            foreach (ShipDefinition def in Encyclopedia.i.ships)
            {
                GameObject p = def != null ? def.unitPrefab : null;
                if (p == null || p.GetComponent<Ship>() == null || p.GetComponent<ShipPart>() == null) continue;
                int turrets = p.GetComponentsInChildren<Turret>(true).Length, parts = p.GetComponentsInChildren<ShipPart>(true).Length;
                if (turrets == 0 || parts < minParts || p.GetComponentsInChildren<Hangar>(true).Length > 1) continue; // unarmed, a boat, a carrier
                if (fort && def.jsonKey == "Frigate1") return def;
                int score = (fort ? -turrets : turrets) * 100 - parts;
                if (score < bestScore) { bestScore = score; best = def; }
            }
            return best;
        }

        private static GameObject Refit(SeaLayout layout, ShipDefinition donor, int hash)
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
            foreach (Turret turret in go.GetComponentsInChildren<Turret>(true))
            {
                Transform r = turret.transform;
                if (r == root) continue;
                while (r.parent != root && r.parent.GetComponent<ShipPart>() == null) r = r.parent;
                if (Under(r, mounts)) continue;
                mounts.RemoveAll(m => m.IsChildOf(r));
                mounts.Add(r);
                turretOf[r] = turret.transform;
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

            // A section with no donor part left for it is built onto the nearest section below it that has one.
            var node = new Transform[n];
            var offset = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                SeaSection section = layout.Sections[i];
                if (assigned[i] != null)
                {
                    Transform t = assigned[i].transform;
                    if (i > 0) t.SetParent(node[section.Parent], false);
                    t.position = section.At;
                    t.rotation = Quaternion.identity;
                    t.localScale = Vector3.one;
                    node[i] = t;
                    Disintegrate?.SetValue(assigned[i], new GameObject[0]);
                }
                else
                {
                    node[i] = node[section.Parent];
                    offset[i] = section.At - node[i].position;
                }
                foreach (SeaBox box in section.Boxes) Box(node[i].gameObject, offset[i] + box.Centre, box.Size, surface);
                var look = new GameObject(MeshPrefix + i);
                look.layer = node[i].gameObject.layer;
                look.transform.SetParent(node[i], false);
                look.transform.localPosition = offset[i];
                look.AddComponent<MeshFilter>().sharedMesh = SeaArt.Whole(layout, i);
                look.AddComponent<MeshRenderer>().sharedMaterial = SeaArt.Material(layout);
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
                MountKind kind = mount.GetComponentInChildren<MissileLauncher>(true) != null ? MountKind.Missile : MountKind.Gun;
                int slot = free.FindIndex(i => layout.Mounts[i].Kind == kind);
                if (slot < 0) slot = free.FindIndex(i => layout.Mounts[i].Kind != MountKind.Store);
                SeaMount at;
                if (slot >= 0) { at = layout.Mounts[free[slot]]; free.RemoveAt(slot); }
                else
                {
                    // More turrets than hardpoints: double up beside the weapon hardpoints, 7 m along.
                    SeaMount b = layout.Mounts[spare++ % layout.Mounts.Length];
                    at = new SeaMount(b.Section, b.At.x, b.At.y, b.At.z + (b.At.z > 0f ? -7f : 7f), b.Yaw, kind);
                }
                Place(mount, turretOf[mount], at, node[at.Section]);
                Dress(mount, layout);
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

            // Static: pinned for good; no aircraft spawn from it.
            Rigidbody body = go.GetComponent<Rigidbody>();
            if (body != null) body.constraints = RigidbodyConstraints.FreezeAll;
            foreach (Hangar hangar in go.GetComponentsInChildren<Hangar>(true)) HangarAircraft?.SetValue(hangar, new AircraftDefinition[0]);
            go.AddComponent<SeaStructure>().Kind = (int)layout.Kind;
            go.GetComponent<NetworkIdentity>().PrefabHash = hash;

            int placed = 0;
            for (int i = 1; i < n; i++) if (assigned[i] != null) placed++;
            Debug.Log("[SeaStructures] " + layout.Key + " refitted from " + donor.jsonKey + ": " + placed + "/" + (n - 1) + " sections, " +
                      mounts.Count + " turrets, " + stores.Count + " stores, " + hull.Count + " hull parts stowed");
            return go;
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
        private static void Place(Transform mount, Transform pivot, SeaMount at, Transform section)
        {
            mount.SetParent(section, true);
            mount.rotation = Quaternion.Euler(0f, at.Yaw, 0f);
            float low = float.MaxValue;
            foreach (MeshFilter filter in mount.GetComponentsInChildren<MeshFilter>(true))
            {
                Renderer r = filter.GetComponent<Renderer>();
                if (filter.sharedMesh == null || r == null || r.forceRenderingOff) continue;
                low = Mathf.Min(low, Lowest(filter.transform, filter.sharedMesh.bounds));
            }
            if (low == float.MaxValue)
                foreach (BoxCollider box in mount.GetComponentsInChildren<BoxCollider>(true))
                    low = Mathf.Min(low, Lowest(box.transform, new Bounds(box.center, box.size)));
            if (low == float.MaxValue) low = pivot.position.y;
            mount.position += new Vector3(at.At.x - pivot.position.x, at.At.y - low, at.At.z - pivot.position.z);
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
