using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BoscaliSummer.Core.Util;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Paint loss on the real exterior triangles of one native damage island. Near and distant
    /// footprints were clipped separately to their matching intact meshes. Geometry and materials are immutable.</summary>
    internal static class MegaplaneSkinDamage
    {
        private const string Resource = "BoscaliSummer.Megaplane.megaplane.skin.gz";
        private const string MaterialName = "BS_Megaplane_DamageMarks";

        private sealed class Entry
        {
            internal Vector3 Center;
            internal int Offset;
            internal Mesh Mesh;
        }

        private static byte[] bytes;
        private static Dictionary<string, Entry> entries;
        private static Dictionary<string, List<string>> contactSites;
        private static Dictionary<string, Material> palette;

        internal static int Stage(float hp) => hp <= 55f ? 2 : hp < 99.99f ? 1 : 0;

        private static void Load()
        {
            if (entries != null) return;
            byte[] packed = EmbeddedResources.ReadAll(typeof(MegaplaneSkinDamage).Assembly, Resource, 16 * 1024 * 1024);
            if (packed == null) throw new InvalidDataException("Missing Groza surface damage artwork.");
            using (var zipped = new GZipStream(new MemoryStream(packed), CompressionMode.Decompress))
            using (var stream = new MemoryStream())
            {
                zipped.CopyTo(stream);
                bytes = stream.ToArray();
            }
            var index = new Dictionary<string, Entry>();
            contactSites = new Dictionary<string, List<string>>();
            using (var stream = new MemoryStream(bytes, false))
            using (var input = new BinaryReader(stream))
            {
                if (input.ReadUInt32() != 0x31445347) throw new InvalidDataException("Invalid Groza surface damage artwork.");
                int count = input.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string key = input.ReadString();
                    var center = new Vector3(input.ReadSingle(), input.ReadSingle(), input.ReadSingle());
                    int length = input.ReadInt32();
                    index.Add(key, new Entry { Center = center, Offset = (int)stream.Position });
                    stream.Seek(length, SeekOrigin.Current);
                    int suffix = key.IndexOf("_3_", StringComparison.Ordinal);
                    if (suffix < 0 || !key.EndsWith("_Near", StringComparison.Ordinal)) continue;
                    string owner = key.Substring(0, suffix);
                    if (!contactSites.TryGetValue(owner, out var list)) contactSites.Add(owner, list = new List<string>());
                    list.Add(key.Substring(0, key.Length - 5));
                }
                if (stream.Position != stream.Length) throw new InvalidDataException("Invalid Groza damage patch table.");
            }
            palette = new Dictionary<string, Material> { { MaterialName, MegaplaneArt.DamageMarks() } };
            entries = index;
        }

        internal sealed class Live
        {
            private readonly Transform art;
            private readonly AeroPart owner;
            private readonly Renderer[] near = new Renderer[2], far = new Renderer[2];
            private LODGroup group;
            private int previous;

            internal Live(Transform art, AeroPart owner)
            {
                this.art = art;
                this.owner = owner;
                owner.onApplyDamage += e => Apply(Stage(e.hitPoints));
                Sync();
            }

            internal void Sync()
            {
                if (owner != null) Apply(Stage(owner.hitPoints));
            }

            private void Apply(int stage)
            {
                if (stage == previous || art == null) return;
                previous = stage;
                if (group == null)
                {
                    var holder = new GameObject("GrozaSurfaceDamage") { layer = art.gameObject.layer };
                    holder.transform.SetParent(art, false);
                    group = holder.AddComponent<LODGroup>();
                }
                if (stage == 0)
                {
                    group.gameObject.SetActive(false);
                    return;
                }
                group.gameObject.SetActive(true);
                var close = new List<Renderer>(2);
                var distant = new List<Renderer>(2);
                for (int i = 0; i < 2; i++)
                {
                    bool active = i < stage;
                    if (active && near[i] == null && far[i] == null)
                    {
                        string key = art.name + "_" + (i + 1);
                        near[i] = Attach(group.transform, key, key, "Near");
                        far[i] = Attach(group.transform, key, key, "Far");
                    }
                    if (near[i] != null)
                    {
                        near[i].gameObject.SetActive(active);
                        if (active) close.Add(near[i]);
                    }
                    if (far[i] != null)
                    {
                        far[i].gameObject.SetActive(active);
                        if (active) distant.Add(far[i]);
                    }
                }
                group.SetLODs(new[] { new LOD(.30f, close.ToArray()), new LOD(.0001f, distant.ToArray()) });
                var native = owner.GetComponent<LODGroup>();
                group.size = native == null ? 342f : native.size;
                group.localReferencePoint = native == null ? Vector3.zero :
                    group.transform.InverseTransformPoint(native.transform.TransformPoint(native.localReferencePoint));
            }
        }

        internal static List<Live> Bind(GameObject airframe)
        {
            var result = new List<Live>();
            foreach (MeshFilter skin in airframe.GetComponentsInChildren<MeshFilter>(true))
            {
                int group = Array.IndexOf(MegaplaneCrash.GroupNames, skin.name);
                // The last three groups are machinery inside the reactor bay.
                if (group < 0 || group >= MegaplaneCrash.GroupNames.Length - 3) continue;
                var owner = skin.GetComponentInParent<AeroPart>(true);
                if (owner != null) result.Add(new Live(skin.transform, owner));
            }
            return result;
        }

        internal static string ContactSite(Transform prefab, ulong surviving, Vector3 at)
        {
            Load();
            string best = null;
            float nearest = 30f * 30f;
            foreach (MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!contactSites.TryGetValue(filter.name, out var sites)) continue;
                int group = Array.IndexOf(MegaplaneCrash.GroupNames, filter.name);
                if ((surviving & (1UL << group)) == 0) continue;
                Matrix4x4 frame = prefab.worldToLocalMatrix * filter.transform.localToWorldMatrix;
                foreach (string site in sites)
                {
                    Vector3 center = frame.MultiplyPoint3x4(entries[site + "_Near"].Center);
                    float distance = (center - at).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance;
                    best = site;
                }
            }
            return best;
        }

        internal static void Wreck(Transform art, int stage, string contact, List<Renderer> near, List<Renderer> far)
        {
            for (int i = 1; i <= stage; i++) AddPair(art, art.name + "_" + i, art.name + "_" + i, near, far);
            if (contact != null && contact.StartsWith(art.name + "_3_", StringComparison.Ordinal))
                AddPair(art, contact, art.name + "_3", near, far);
        }

        private static void AddPair(Transform art, string key, string label, List<Renderer> near, List<Renderer> far)
        {
            Renderer close = Attach(art, key, label, "Near");
            Renderer distant = Attach(art, key, label, "Far");
            if (close != null) near.Add(close);
            if (distant != null) far.Add(distant);
        }

        private static Renderer Attach(Transform parent, string key, string label, string level)
        {
            Load();
            if (!entries.TryGetValue(key + "_" + level, out Entry entry)) return null;
            if (entry.Mesh == null)
            {
                using (var stream = new MemoryStream(bytes, false))
                using (var input = new BinaryReader(stream))
                {
                    stream.Position = entry.Offset;
                    entry.Mesh = MegaplaneLod.ReadMesh(input, key, palette, out _);
                }
                entry.Mesh.name = "GrozaDamageMesh_" + key + "_" + level;
                entry.Mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
            }
            var go = new GameObject("GrozaSkinDamage_" + label + "_" + level) { layer = parent.gameObject.layer };
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = entry.Mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = palette[MaterialName];
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return renderer;
        }
    }
}

