using System;
using System.Collections.Generic;
using System.Linq;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Baked art of the five runtime-built stores: the embedded vanguard-stores.bundle (tools/Compile-VanguardStoreArt.ps1,
    /// generator Assets/Editor/VanguardStoreArtBuilder.cs). Each prefab "&lt;Name&gt;" is the model at its own origin in Unity
    /// space (+Z nose, +Y up; pods' top at y = 0), FBX joint names kept, one LODGroup on the root, MANTA deployed (wings flat).
    /// Visual only and the same on every peer. Loaded once; a missing or broken bundle leaves every caller on its vanilla look.
    /// </summary>
    internal static class StoreArt
    {
        public const string Torch = "Torch", Sunspear = "Sunspear", Stormcell = "Stormcell", Watchtower = "Watchtower", Manta = "Manta";
        private const string Resource = "BoscaliSummer.Vanguard.vanguard-stores.bundle";
        private static readonly string[] Names = { Torch, Sunspear, Stormcell, Watchtower, Manta };
        private static Dictionary<string, GameObject> prefabs;

        /// <summary>The bundle prefab (do not modify it), or null when the bundle is missing/broken: callers then keep
        /// today's vanilla-part look.</summary>
        public static GameObject Prefab(string name)
        {
            if (prefabs == null) prefabs = Load();
            return name != null && prefabs.TryGetValue(name, out GameObject prefab) && prefab != null ? prefab : null;
        }

        /// <summary>A copy of the prefab under <paramref name="parent"/> at local identity, named "StoreArt_" + name, on the
        /// parent's layer; null when <see cref="Prefab"/> is null.</summary>
        public static GameObject Spawn(string name, Transform parent)
        {
            GameObject prefab = Prefab(name);
            if (prefab == null) return null;
            GameObject copy = UnityEngine.Object.Instantiate(prefab, parent, false);
            copy.name = "StoreArt_" + name;
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            if (parent != null)
                foreach (Transform t in copy.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = parent.gameObject.layer;
            return copy;
        }

        /// <summary>Bounds of the meshes under <paramref name="of"/> (minus <paramref name="skip"/>) in <paramref name="space"/>'s frame.</summary>
        public static bool Bounds(Transform space, Transform of, out Bounds bounds, Func<MeshFilter, bool> skip = null)
        {
            bounds = new Bounds();
            bool any = false;
            foreach (MeshFilter f in of.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null || (skip != null && skip(f))) continue;
                Matrix4x4 m = space.worldToLocalMatrix * f.transform.localToWorldMatrix;
                Bounds b = f.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1) * 2f - Vector3.one));
                    if (any) bounds.Encapsulate(corner); else { bounds = new Bounds(corner, Vector3.zero); any = true; }
                }
            }
            return any;
        }

        /// <summary>
        /// Swaps a template's donor meshes under <paramref name="root"/> for the model: every mesh renderer and LOD group goes
        /// (except <paramref name="keep"/>), and the model sits where the old shape sat, top to top and centred on its footprint,
        /// the shipped props' seating rule. Null (nothing touched) when the bundle has no such model.
        /// </summary>
        public static GameObject Replace(Transform root, string name, Func<MeshFilter, bool> keep = null)
        {
            if (Prefab(name) == null) return null;
            bool had = Bounds(root, root, out Bounds old, keep);
            foreach (LODGroup group in root.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(group);
            foreach (MeshFilter f in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (keep != null && keep(f)) continue;
                if (f.TryGetComponent(out MeshRenderer r)) UnityEngine.Object.DestroyImmediate(r);
                UnityEngine.Object.DestroyImmediate(f);
            }
            GameObject art = Spawn(name, root);
            if (had && Bounds(root, art.transform, out Bounds now))
                art.transform.localPosition = new Vector3(old.center.x - now.center.x, old.max.y - now.max.y, old.center.z - now.center.z);
            return art;
        }

        /// <summary>Joint by name under a model copy, or null.</summary>
        public static Transform Joint(Transform art, string name)
        {
            foreach (Transform t in art.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t;
            return null;
        }

        private static Dictionary<string, GameObject> Load()
        {
            var found = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            try
            {
                byte[] bytes = EmbeddedResources.ReadAll(typeof(StoreArt).Assembly, Resource, 64 * 1024 * 1024);
                AssetBundle bundle = bytes != null ? AssetBundle.LoadFromMemory(bytes) : null;
                if (bundle == null)
                {
                    Debug.LogWarning("[Vanguard] vanguard-stores.bundle is not embedded or unreadable; stores keep their vanilla parts (tools/Compile-VanguardStoreArt.ps1)");
                    return found;
                }
                foreach (GameObject go in bundle.LoadAllAssets<GameObject>())
                {
                    if (Array.IndexOf(Names, go.name) < 0) continue;
                    // Point bundled materials at the game's own URP shaders; the bundle's copies may miss its variants.
                    foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                        foreach (Material m in r.sharedMaterials)
                            if (m != null && m.shader != null && Shader.Find(m.shader.name) is Shader native) m.shader = native;
                    found[go.name] = go;
                }
                string missing = string.Join(", ", Names.Where(n => !found.ContainsKey(n)));
                Debug.Log("[Vanguard] vanguard-stores.bundle loaded: " + string.Join(", ", found.Keys) + (missing.Length > 0 ? "; missing " + missing : ""));
            }
            catch (Exception error) { Debug.LogError("[Vanguard] vanguard-stores.bundle: " + error.Message); }
            return found;
        }
    }
}
