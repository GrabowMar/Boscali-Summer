using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BoscaliSummer.Core.Util;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Native camera LOD selection. Each damage island keeps its own renderers after separation.</summary>
    internal static class MegaplaneLod
    {
        private const string Resource = "BoscaliSummer.Megaplane.megaplane.lod.gz";

        private sealed class Levels
        {
            internal readonly List<Renderer> Near = new List<Renderer>();
            internal readonly List<Renderer> Far = new List<Renderer>();
        }

        internal static void Install(Dictionary<string, Transform> nodes)
        {
            byte[] bytes = EmbeddedResources.ReadAll(typeof(MegaplaneLod).Assembly, Resource, 16 * 1024 * 1024);
            if (bytes == null) throw new InvalidDataException("Missing Groza distant mesh.");
            var islands = new Dictionary<AeroPart, Levels>();
            var originals = new Dictionary<string, Levels>();
            var materials = new Dictionary<string, Material>();
            foreach (KeyValuePair<string, Transform> node in nodes)
            {
                if (!node.Value.TryGetComponent(out MeshRenderer renderer)) continue;
                foreach (Material material in renderer.sharedMaterials) materials[material.name] = material;
                // Strobes and nav lights remain visible when viewed from outside the formation.
                if (node.Key.StartsWith("Det_lights_") || node.Key.StartsWith("LiftGlow_")) continue;
                AeroPart owner = node.Value.GetComponentInParent<AeroPart>(true);
                if (owner == null) continue;
                if (!islands.TryGetValue(owner, out Levels levels)) islands.Add(owner, levels = new Levels());
                levels.Near.Add(renderer);
                originals.Add(node.Key, levels);
            }
            using (var input = new BinaryReader(new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress)))
            {
                if (input.ReadUInt32() != 0x314c5a47) throw new InvalidDataException("Invalid Groza distant mesh.");
                int count = input.ReadInt32();
                for (int i = 0; i < count; i++)
                {
                    string name = input.ReadString();
                    if (!originals.TryGetValue(name, out Levels levels)) throw new InvalidDataException("Unowned Groza distant mesh " + name);
                    Transform pivot = nodes[name];
                    GameObject go = new GameObject("GrozaLOD_" + name) { layer = pivot.gameObject.layer };
                    go.transform.SetParent(pivot, false);
                    Mesh mesh = ReadMesh(input, name, materials, out Material[] palette);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = palette;
                    levels.Far.Add(renderer);
                }
            }
            foreach (KeyValuePair<AeroPart, Levels> island in islands)
            {
                // References cross the island's animated pivots but never its detachable neighbours.
                // The same reference size across groups gives contiguous sections a common transition distance.
                Transform pivot = island.Key.transform;
                var group = pivot.gameObject.AddComponent<LODGroup>();
                var levels = island.Value;
                group.SetLODs(new[] { new LOD(.30f, levels.Near.ToArray()), new LOD(.0001f, levels.Far.ToArray()) });
                group.RecalculateBounds();
                group.size = 342f;
            }
        }

        private static Mesh ReadMesh(BinaryReader input, string name, Dictionary<string, Material> materials, out Material[] palette)
        {
            int vertices = input.ReadInt32();
            var points = new Vector3[vertices];
            var normals = new Vector3[vertices];
            var uv = new Vector2[vertices];
            for (int i = 0; i < vertices; i++)
            {
                points[i] = new Vector3(input.ReadSingle(), input.ReadSingle(), input.ReadSingle());
                normals[i] = new Vector3(input.ReadSingle(), input.ReadSingle(), input.ReadSingle());
                uv[i] = new Vector2(input.ReadSingle(), input.ReadSingle());
            }
            var mesh = new Mesh { name = "GrozaLOD_" + name, vertices = points, normals = normals, uv = uv };
            mesh.subMeshCount = input.ReadInt32();
            palette = new Material[mesh.subMeshCount];
            for (int i = 0; i < palette.Length; i++)
            {
                palette[i] = materials[input.ReadString()];
                int[] indices = new int[input.ReadInt32()];
                for (int j = 0; j < indices.Length; j++) indices[j] = input.ReadUInt16();
                mesh.SetTriangles(indices, i, false);
            }
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
