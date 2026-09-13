using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Visuals
{
    /// <summary>
    /// Builds native URP materials for trench earthworks. The earthwork material carries a
    /// procedurally baked palette texture whose horizontal axis follows the cross-section
    /// profile (grass fringe, earth berm, timber revetment, duckboard floor, sandbag crest),
    /// so a trench reads as constructed fieldworks without any external assets.
    /// </summary>
    internal static class TrenchMaterialResolver
    {
        private static Material cachedConcrete;
        private static Material cachedSandbag;
        private static Material cachedEarth;
        private static Texture2D earthTexture;
        private static readonly List<Material> owned = new List<Material>(4);
        private static readonly List<Texture2D> ownedTextures = new List<Texture2D>(2);

        public static void ResetForScene()
        {
            foreach (var material in owned) if (material != null) UnityEngine.Object.Destroy(material);
            owned.Clear();
            foreach (var texture in ownedTextures) if (texture != null) UnityEngine.Object.Destroy(texture);
            ownedTextures.Clear();
            cachedConcrete = null;
            cachedSandbag = null;
            cachedEarth = null;
            earthTexture = null;
        }

        public static Material GetConcreteMaterial()
        {
            if (cachedConcrete != null) return cachedConcrete;

            BuildingDefinition pillbox = ResolveBuildingDef("pillbox");
            if (pillbox?.unitPrefab != null)
            {
                Renderer r = pillbox.unitPrefab.GetComponentInChildren<Renderer>();
                if (r != null && r.sharedMaterial != null)
                    return cachedConcrete = r.sharedMaterial;
            }

            BuildingDefinition bunker = ResolveBuildingDef("gabionBunker1");
            if (bunker?.unitPrefab != null)
            {
                Renderer r = bunker.unitPrefab.GetComponentInChildren<Renderer>();
                if (r != null && r.sharedMaterial != null)
                    return cachedConcrete = r.sharedMaterial;
            }

            return cachedConcrete = CreateFlatMaterial(new Color(0.48f, 0.47f, 0.45f), "BoscaliSummer.TrenchConcrete");
        }

        public static Material GetSandbagMaterial()
        {
            if (cachedSandbag != null) return cachedSandbag;

            BuildingDefinition bunker = ResolveBuildingDef("gabionBunker1");
            if (bunker?.unitPrefab != null)
            {
                Renderer[] renderers = bunker.unitPrefab.GetComponentsInChildren<Renderer>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i]?.sharedMaterial != null)
                        return cachedSandbag = renderers[i].sharedMaterial;
                }
            }

            return cachedSandbag = CreateFlatMaterial(new Color(0.55f, 0.48f, 0.38f), "BoscaliSummer.TrenchSandbag");
        }

        public static Material GetEarthBermMaterial()
        {
            if (cachedEarth != null) return cachedEarth;

            Shader shader = FindLitShader();
            if (shader == null) return null;

            earthTexture = BuildEarthworkTexture();
            var mat = new Material(shader) { name = "BoscaliSummer.TrenchEarthwork" };
            if (earthTexture != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", earthTexture);
                else mat.mainTexture = earthTexture;
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            else mat.color = Color.white;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.08f);
            owned.Add(mat);
            cachedEarth = mat;
            return mat;
        }

        private static Shader FindLitShader()
        {
            return Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Unlit/Color");
        }

        private static Material CreateFlatMaterial(Color albedo, string name)
        {
            Shader shader = FindLitShader();
            if (shader == null) return null;

            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", albedo);
            else mat.color = albedo;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.1f);
            owned.Add(mat);
            return mat;
        }

        /// <summary>
        /// 256x256 palette texture: U follows the trench cross-section, V runs along the trench.
        /// Bands match TrenchMeshBuilder.ProfileU.
        /// </summary>
        private static Texture2D BuildEarthworkTexture()
        {
            const int size = 256;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = y / (float)(size - 1);
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)(size - 1);
                    Color color = BandColor(u, v);
                    float fine = Hash(x, y);
                    float coarse = Hash(x >> 4, y >> 4);
                    float shade = 0.88f + 0.20f * fine + 0.10f * (coarse - 0.5f);
                    pixels[y * size + x] = new Color(color.r * shade, color.g * shade, color.b * shade, 1f);
                }
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "TrenchEarthworkPalette",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            ownedTextures.Add(texture);
            return texture;
        }

        private static Color BandColor(float u, float v)
        {
            Color earth = new Color(0.40f, 0.29f, 0.17f);
            Color grass = new Color(0.28f, 0.35f, 0.16f);
            Color sandbag = new Color(0.67f, 0.58f, 0.42f);
            Color timber = new Color(0.45f, 0.33f, 0.20f);
            Color plank = new Color(0.54f, 0.44f, 0.29f);

            if (u < 0.08f) return Color.Lerp(grass, earth, u / 0.08f);
            if (u < 0.21f) return earth;
            if (u < 0.34f) return Timber(timber, v, 5f);
            if (u < 0.41f) return Timber(plank, v, 4f);
            if (u < 0.47f) return sandbag;
            if (u < 0.63f) return Timber(timber, v, 5f);
            if (u < 0.73f) return Sandbag(sandbag, v);
            if (u < 0.94f) return earth;
            return Color.Lerp(earth, grass, (u - 0.94f) / 0.06f);
        }

        private static Color Timber(Color baseColor, float v, float count)
        {
            float seam = Mathf.Abs(Mathf.Repeat(v * count, 1f) - 0.5f) * 2f;
            float darken = seam > 0.86f ? 0.72f : 1f;
            return baseColor * darken;
        }

        private static Color Sandbag(Color baseColor, float v)
        {
            float seam = Mathf.Abs(Mathf.Repeat(v * 3f, 1f) - 0.5f) * 2f;
            return baseColor * (0.94f + 0.06f * seam);
        }

        private static float Hash(float x, float y)
        {
            float value = Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
            return value - Mathf.Floor(value);
        }

        private static BuildingDefinition ResolveBuildingDef(string key)
        {
            if (Encyclopedia.i?.buildings == null) return null;
            for (int i = 0; i < Encyclopedia.i.buildings.Count; i++)
            {
                BuildingDefinition def = Encyclopedia.i.buildings[i];
                if (def != null && string.Equals(def.jsonKey, key, StringComparison.OrdinalIgnoreCase))
                    return def;
            }
            return null;
        }
    }
}
