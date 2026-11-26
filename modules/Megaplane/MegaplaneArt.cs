using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using BoscaliSummer.Core.Util;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Loads the Blender-built KY-44 assembly in Unity coordinates, metres, nose +Z. The exporter lives in Artwork/.</summary>
    internal static class MegaplaneArt
    {
        private const string Resource = "BoscaliSummer.Megaplane.megaplane.mesh.gz";

        internal static Transform Build(Transform parent) => Load(parent, Resource);
        internal static Transform BuildWreck(Transform parent) => Load(parent, "BoscaliSummer.Megaplane.megaplane.wreck.gz");

        private static Transform Load(Transform parent, string resource)
        {
            byte[] bytes = EmbeddedResources.ReadAll(typeof(MegaplaneArt).Assembly, resource, 32 * 1024 * 1024);
            if (bytes == null) throw new InvalidDataException("Missing aircraft artwork " + resource);
            using (var input = new BinaryReader(new GZipStream(new MemoryStream(bytes), CompressionMode.Decompress)))
            {
                if (input.ReadInt32() != 0x504D5342 || input.ReadInt32() != 2) throw new InvalidDataException("Invalid Megaplane mesh.");
                var materials = new Material[input.ReadInt32()];
                for (int i = 0; i < materials.Length; i++)
                    materials[i] = Material(input.ReadString(), new Color(input.ReadSingle(), input.ReadSingle(), input.ReadSingle()).gamma,   // Blender colours are linear; SetColor takes sRGB
                        input.ReadSingle(), input.ReadSingle(), input.ReadSingle(), input.ReadBoolean());
                var nodes = new List<Transform>();
                Transform root = null;
                int count = input.ReadInt32();
                for (int n = 0; n < count; n++)
                {
                    var go = new GameObject(input.ReadString());
                    int parentIndex = input.ReadInt32();
                    go.transform.SetParent(parentIndex < 0 ? parent : nodes[parentIndex], false);
                    go.transform.localPosition = new Vector3(input.ReadSingle(), input.ReadSingle(), input.ReadSingle());
                    if (parentIndex < 0) root = go.transform;
                    nodes.Add(go.transform);
                    int vertices = input.ReadInt32();
                    if (vertices == 0) continue;
                    var points = new Vector3[vertices];
                    var normals = new Vector3[vertices];
                    var uv = new Vector2[vertices];
                    for (int i = 0; i < vertices; i++)
                    {
                        points[i] = new Vector3(input.ReadSingle(), input.ReadSingle(), input.ReadSingle());
                        normals[i] = new Vector3(input.ReadSingle(), input.ReadSingle(), input.ReadSingle());
                        uv[i] = new Vector2(input.ReadSingle(), input.ReadSingle());
                    }
                    var mesh = new Mesh
                    {
                        name = "BS_Megaplane_" + go.name,
                        indexFormat = vertices <= ushort.MaxValue ? IndexFormat.UInt16 : IndexFormat.UInt32,
                        vertices = points, normals = normals, uv = uv
                    };
                    int subs = input.ReadInt32();
                    mesh.subMeshCount = subs;
                    var used = new Material[subs];
                    for (int s = 0; s < subs; s++)
                    {
                        used[s] = materials[input.ReadInt32()];
                        var tris = new int[input.ReadInt32()];
                        for (int i = 0; i < tris.Length; i++) tris[i] = input.ReadInt32();
                        mesh.SetTriangles(tris, s, calculateBounds: false);
                    }
                    mesh.RecalculateBounds();
                    mesh.RecalculateTangents();
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;
                    go.AddComponent<MeshRenderer>().sharedMaterials = used;
                }
                return root;
            }
        }

        private static Texture2D albedo, normal, mask;
        private static bool maskMissing;
        private static readonly Dictionary<string, Material> palette = new Dictionary<string, Material>();

        private static Material Material(string name, Color color, float metallic, float smoothness, float alpha, bool textured)
        {
            if (palette.TryGetValue(name, out Material existing) && existing != null) return existing;
            if (name == "BS_Megaplane_CrewCRT_Emit") return DisplayMaterial(name, name + "_Tex");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name, hideFlags = HideFlags.DontUnloadUnusedAsset };
            if (textured)
            {
                // Tiling panel detail (world-scale box UVs, 16 m per repeat): albedo multiplies the paint colour.
                if (albedo == null) albedo = Texture("BS_Megaplane_Panels", false);
                if (normal == null) normal = Texture("BS_Megaplane_Panels_N", true);
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", albedo);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", albedo);
                if (material.HasProperty("_BumpMap")) { material.SetTexture("_BumpMap", normal); material.EnableKeyword("_NORMALMAP"); }
                if (mask == null && !maskMissing) { mask = Texture("BS_Megaplane_Panels_M", true, true); maskMissing = mask == null; }
                if (mask != null && material.HasProperty("_MetallicGlossMap"))
                {   // R metallic, G occlusion, A smoothness (scaled by _Smoothness)
                    material.SetTexture("_MetallicGlossMap", mask); material.EnableKeyword("_METALLICSPECGLOSSMAP");
                    material.SetTexture("_OcclusionMap", mask); material.EnableKeyword("_OCCLUSIONMAP");
                }
            }
            else if (Texture(name + "_Tex", false, true) is Texture2D own)
            {   // a material with its own painted map (break faces: assets/.../damage_textures.py, 4 m UVs from breaks.py)
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", own);
                if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", own);
                if (name.EndsWith("_Emit") && material.HasProperty("_EmissionMap"))
                    material.SetTexture("_EmissionMap", own);
                color = new Color(1f, 1f, 1f, color.a);   // the map carries the colour
            }
            else if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 0f);   // fans, bores, glass: seen from both sides
            if (alpha < 1f && material.HasProperty("_Surface"))
            {   // flight-deck glass: URP transparent, alpha blended, the instrument panel shows through
                material.SetFloat("_Surface", 1f); material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
                color.a = alpha;
            }
            if (name.EndsWith("_Emit") || name.EndsWith("_Lights") || name.EndsWith("_NavRed") || name.EndsWith("_NavGreen"))
            {   // lamps and displays glow (same rule as build_megaplane.material)
                material.SetColor("_EmissionColor", color * 2f);
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            palette[name] = material;
            return material;
        }

        internal static Material DamageMarks()
        {
            const string name = "BS_Megaplane_DamageMarks";
            var mat = Material(name, Color.white, .10f, .28f, .999f, false);
            Texture2D atlas = Texture(name, false);
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", atlas);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", atlas);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", (float)CullMode.Back);
            return mat;
        }

        internal static Material SystemsDisplay() => DisplayMaterial("BS_Megaplane_Systems", "BS_Megaplane_Systems");

        private static Material DisplayMaterial(string name, string textureName)
        {
            if (palette.TryGetValue(name, out Material cached) && cached != null) return cached;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Texture");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Lit");
            var screen = new Material(shader) { name = name, hideFlags = HideFlags.DontUnloadUnusedAsset };
            Texture2D atlas = Texture(textureName, false, compress: false);
            atlas.wrapMode = TextureWrapMode.Clamp;
            if (screen.HasProperty("_BaseMap")) screen.SetTexture("_BaseMap", atlas);
            if (screen.HasProperty("_MainTex")) screen.SetTexture("_MainTex", atlas);
            if (screen.HasProperty("_EmissionMap"))
            {
                screen.SetTexture("_EmissionMap", atlas);
                screen.SetColor("_EmissionColor", Color.white);
                screen.EnableKeyword("_EMISSION");
            }
            palette[name] = screen;
            return screen;
        }

        private static Texture2D Texture(string name, bool linear, bool optional = false, bool compress = true)
        {
            byte[] bytes = EmbeddedResources.ReadAll(typeof(MegaplaneArt).Assembly, "BoscaliSummer.Megaplane." + name + ".png", 32 * 1024 * 1024);
            if (bytes == null && optional) return null;
            if (bytes == null) throw new InvalidDataException("Megaplane texture " + name + " is missing.");
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true, linear)
            {
                name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8,
                hideFlags = HideFlags.DontUnloadUnusedAsset
            };
            if (!ImageConversion.LoadImage(texture, bytes, false)) throw new InvalidDataException("Invalid Megaplane texture " + name + ".");
            // Normal maps stay uncompressed: URP reads RGB normals from RGBA32 (alpha 1), DXT5 would need the AG layout.
            if (compress && !linear && SystemInfo.SupportsTextureFormat(TextureFormat.DXT5)) texture.Compress(true);
            texture.Apply(true, true);
            return texture;
        }
    }
}
