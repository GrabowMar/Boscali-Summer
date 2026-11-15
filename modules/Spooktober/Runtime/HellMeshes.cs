using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BoscaliSummer.Core.Util;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>Original Blender meshes, loaded once. HELM v1 format is documented by build_hell_assets.py.</summary>
    internal static class HellMeshes
    {
        private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        private static bool attempted;

        internal static Mesh Mesh(string key)
        {
            if (!attempted) Load();
            return key != null && meshes.TryGetValue(key, out Mesh mesh) ? mesh : null;
        }

        private static void Load()
        {
            attempted = true;
            Mesh pending = null;
            try
            {
                byte[] data = EmbeddedResources.ReadAll(typeof(HellMeshes).Assembly, "BoscaliSummer.Spooktober.hell-meshes.bin", 24 * 1024 * 1024);
                if (data == null) throw new InvalidDataException("hell-meshes.bin missing; run assets/HellInvasion/Source/build_hell_assets.py");
                using var reader = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
                if (reader.ReadUInt32() != 0x4D4C4548 || reader.ReadInt32() != 1) throw new InvalidDataException("HELM header/version");
                int count = Count(reader.ReadInt32(), 1, 32);
                for (int model = 0; model < count; model++)
                {
                    int nameLength = Count(reader.ReadUInt16(), 1, 128);
                    string name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                    if (meshes.ContainsKey(name)) throw new InvalidDataException("Duplicate HELM model: " + name);
                    int verticesCount = Count(reader.ReadInt32(), 3, 200000);
                    int submeshes = Count(reader.ReadInt32(), 3, 3);
                    var vertices = new Vector3[verticesCount];
                    var normals = new Vector3[verticesCount];
                    for (int i = 0; i < verticesCount; i++) { vertices[i] = Vector(reader); normals[i] = Vector(reader); }
                    var indices = new int[submeshes][];
                    for (int s = 0; s < submeshes; s++)
                    {
                        int length = Count(reader.ReadInt32(), 0, 600000);
                        if (length % 3 != 0) throw new InvalidDataException("Non-triangle HELM mesh");
                        indices[s] = new int[length];
                        for (int i = 0; i < length; i++) indices[s][i] = Count(reader.ReadInt32(), 0, verticesCount - 1);
                    }
                    var mesh = new Mesh { name = "Hell: " + name, indexFormat = verticesCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                    pending = mesh;
                    mesh.vertices = vertices;
                    mesh.normals = normals;
                    mesh.subMeshCount = submeshes;
                    for (int s = 0; s < submeshes; s++) mesh.SetTriangles(indices[s], s, false);
                    mesh.RecalculateBounds();
                    meshes.Add(name, mesh);
                    pending = null;
                }
                if (reader.BaseStream.Position != data.Length) throw new InvalidDataException("Trailing HELM bytes");
                Plugin.Logger.LogInfo("[Spooktober] Loaded " + meshes.Count + " original Hell meshes.");
            }
            catch (Exception e)
            {
                if (pending != null) UnityEngine.Object.Destroy(pending);
                foreach (Mesh mesh in meshes.Values) if (mesh != null) UnityEngine.Object.Destroy(mesh);
                meshes.Clear();
                Plugin.Logger.LogError("[Spooktober] Hell mesh catalogue: " + e.Message);
            }
        }

        private static int Count(int value, int min, int max) => value >= min && value <= max ? value : throw new InvalidDataException("HELM bounds");
        private static Vector3 Vector(BinaryReader reader)
        {
            var value = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z) || value.sqrMagnitude > 100000000f)
                throw new InvalidDataException("HELM non-finite or oversized vertex");
            return value;
        }

        internal static void Release()
        {
            foreach (Mesh mesh in meshes.Values) if (mesh != null) UnityEngine.Object.Destroy(mesh);
            meshes.Clear();
            attempted = false;
        }
    }
}
