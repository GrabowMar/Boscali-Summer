using System;
using System.IO;
using System.IO.Compression;
using BoscaliSummer.Core.Util;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>Authored, shared SA-74N artwork. Blender source and runtime export use +Z nose, metres and the body centre as origin.</summary>
    internal static class Sa74Model
    {
        private const string Prefix = "BoscaliSummer.Vanguard.";
        private static GameObject template;

        public static GameObject Attach(Transform parent)
        {
            if (template == null) template = Load();
            GameObject copy = UnityEngine.Object.Instantiate(template, parent, false);
            copy.name = "VanguardVisual";
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = Vector3.one;
            foreach (Transform child in copy.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = parent.gameObject.layer;
            return copy;
        }

        private static GameObject Load()
        {
            byte[] bytes = EmbeddedResources.ReadAll(typeof(Sa74Model).Assembly, Prefix + "sa74n.mesh.gz", 8 * 1024 * 1024);
            if (bytes == null) throw new InvalidDataException("SA-74N artwork is missing.");
            var root = new GameObject("SA74N_Art");
            root.transform.SetParent(RuntimeStores.Holder, false);
            using (var input = new BinaryReader(new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress)))
            {
                if (input.ReadInt32() != 0x34374153 || input.ReadInt32() != 1) throw new InvalidDataException("Invalid SA-74N mesh.");
                int count = input.ReadInt32();
                if (count < 1 || count > 16) throw new InvalidDataException("Invalid SA-74N material count.");
                for (int part = 0; part < count; part++)
                {
                    string name = input.ReadString();
                    var color = new Color(input.ReadSingle(), input.ReadSingle(), input.ReadSingle(), 1f);
                    float metallic = input.ReadSingle(), smoothness = input.ReadSingle();
                    bool skin = input.ReadBoolean();
                    int vertices = input.ReadInt32(), indices = input.ReadInt32();
                    if (vertices < 3 || vertices > 200000 || indices < 3 || indices > 600000 || indices % 3 != 0)
                        throw new InvalidDataException("Invalid SA-74N geometry budget.");
                    var points = new Vector3[vertices];
                    var normals = new Vector3[vertices];
                    var uv = new Vector2[vertices];
                    var triangles = new int[indices];
                    for (int i = 0; i < vertices; i++)
                    {
                        points[i] = new Vector3(input.ReadSingle(), input.ReadSingle(), input.ReadSingle());
                        normals[i] = new Vector3(input.ReadSingle(), input.ReadSingle(), input.ReadSingle());
                        uv[i] = new Vector2(input.ReadSingle(), input.ReadSingle());
                    }
                    for (int i = 0; i < indices; i++)
                    {
                        triangles[i] = input.ReadInt32();
                        if (triangles[i] < 0 || triangles[i] >= vertices) throw new InvalidDataException("Invalid SA-74N triangle.");
                    }
                    var mesh = new Mesh { name = "SA74N_" + name, indexFormat = vertices > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                    mesh.vertices = points;
                    mesh.normals = normals;
                    mesh.uv = uv;
                    mesh.triangles = triangles;
                    mesh.RecalculateBounds();
                    mesh.RecalculateTangents();
                    mesh.UploadMeshData(true);
                    var go = new GameObject(name);
                    go.transform.SetParent(root.transform, false);
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterial = Material(name, color, metallic, smoothness, skin);
                }
            }
            return root;
        }

        private static Material Material(string name, Color color, float metallic, float smoothness, bool skin)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = "SA74N_" + name, hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", smoothness);
            if (skin)
            {
                Texture2D albedo = Texture("albedo", false), surface = Texture("surface", true);
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", albedo);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", albedo);
                if (material.HasProperty("_MetallicGlossMap")) material.SetTexture("_MetallicGlossMap", surface);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 1f);
                if (material.HasProperty("_GlossMapScale")) material.SetFloat("_GlossMapScale", 1f);
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.EnableKeyword("_METALLICGLOSSMAP");
            }
            return material;
        }

        private static Texture2D Texture(string map, bool linear)
        {
            string name = "SA74N_" + map;
            byte[] bytes = EmbeddedResources.ReadAll(typeof(Sa74Model).Assembly, Prefix + "Textures." + name + ".png", 32 * 1024 * 1024);
            if (bytes == null) throw new InvalidDataException("SA-74N texture " + map + " is missing.");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear)
            {
                name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4,
                hideFlags = HideFlags.DontUnloadUnusedAsset
            };
            if (!ImageConversion.LoadImage(texture, bytes, false)) throw new InvalidDataException("Invalid SA-74N texture " + map + ".");
            if (SystemInfo.SupportsTextureFormat(TextureFormat.DXT5)) texture.Compress(true);
            texture.Apply(true, true);
            return texture;
        }
    }
}
