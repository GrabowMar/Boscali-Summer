using System;
using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Trenches.Visuals
{
    /// <summary>
    /// Builds the native URP material for the carved ditch. The earthwork material carries a
    /// procedurally baked palette texture whose horizontal axis follows the cross-section
    /// profile (grass fringe, excavated spoil, duckboard floor, timber revetment, packed
    /// earth crest), so a trench reads as fieldworks without any external assets.
    /// </summary>
    internal static class TrenchMaterialResolver
    {
        private static Material cachedEarth;
        private static Texture2D earthTexture;
        private static readonly List<Material> owned = new List<Material>(2);
        private static readonly List<Texture2D> ownedTextures = new List<Texture2D>(2);

        public static void ResetForScene()
        {
            foreach (var material in owned) if (material != null) UnityEngine.Object.Destroy(material);
            owned.Clear();
            foreach (var texture in ownedTextures) if (texture != null) UnityEngine.Object.Destroy(texture);
            ownedTextures.Clear();
            cachedEarth = null;
            earthTexture = null;
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
            Color earth = new Color(0.37f, 0.29f, 0.19f);
            Color darkSoil = new Color(0.26f, 0.19f, 0.12f);
            Color grass = new Color(0.29f, 0.35f, 0.16f);
            Color spoil = new Color(0.43f, 0.34f, 0.22f);
            Color duckboard = new Color(0.32f, 0.24f, 0.15f);

            if (u < 0.07f) return Color.Lerp(grass, earth, u / 0.07f);
            if (u < 0.21f) return spoil;                       // outer spoil berm
            if (u < 0.34f) return darkSoil;                    // parados inner slope
            if (u < 0.41f) return Duckboard(duckboard, v);     // ditch floor
            if (u < 0.47f) return Color.Lerp(darkSoil, earth, 0.6f); // firing step
            if (u < 0.63f) return Color.Lerp(earth, darkSoil, 0.25f); // packed inner wall
            if (u < 0.73f) return spoil;                       // packed earth crest
            if (u < 0.94f) return earth;                       // outer parapet slope
            return Color.Lerp(earth, grass, (u - 0.94f) / 0.06f);
        }

        /// <summary>Barely-there duckboards: one thin seam every few metres of ditch floor.</summary>
        private static Color Duckboard(Color floor, float v)
        {
            float seam = Mathf.Abs(Mathf.Repeat(v * 0.7f, 1f) - 0.5f) * 2f;
            return floor * (seam > 0.94f ? 0.82f : 1f);
        }

        private static float Hash(float x, float y)
        {
            float value = Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
            return value - Mathf.Floor(value);
        }
    }
}
