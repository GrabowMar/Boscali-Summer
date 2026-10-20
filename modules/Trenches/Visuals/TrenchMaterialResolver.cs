using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Trenches.Visuals
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
        private static Material cachedWire;
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
            cachedWire = null;
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

        public static Material GetWireMaterial()
        {
            if (cachedWire != null) return cachedWire;
            Shader shader = FindLitShader();
            if (shader == null) return null;
            var mat = new Material(shader) { name = "BoscaliSummer.TrenchWire" };
            Color steel = new Color(0.18f, 0.20f, 0.19f);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", steel);
            else mat.color = steel;
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.35f);
            // Pickets and strands are single quads: culled from behind, half the belt vanished
            // depending on which way the path ran. Both faces render.
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            owned.Add(mat);
            cachedWire = mat;
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
                    float fine = Hash(x, y);
                    float coarse = Hash(x >> 4, y >> 4);
                    Color color = BandColor(u, v, Hash(x >> 3, y >> 3));
                    float shade = 0.88f + 0.20f * fine + 0.10f * (coarse - 0.5f);
                    pixels[y * size + x] = new Color(color.r * shade, color.g * shade, color.b * shade, 1f);
                }
            }

            // Mipmapped: the far LODs draw this palette kilometres out, and without a mip chain
            // the per-texel grain shimmered along every ridge as the camera moved.
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "TrenchEarthworkPalette",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, false);
            ownedTextures.Add(texture);
            return texture;
        }

        private static Color BandColor(float u, float v, float grain)
        {
            // Sun-baked turned earth, not garden soil: freshly dug fieldworks match the dry
            // ground around them (BALTOPS 2025: "lines of freshly turned earth"), and the old
            // dark-temperate bands read as a black scar on arid ground from the air. The fringe
            // is dry summer growth rather than green turf, so the skirts meet either biome.
            Color earth = new Color(0.52f, 0.42f, 0.28f);
            Color darkSoil = new Color(0.38f, 0.30f, 0.19f);
            Color grass = new Color(0.55f, 0.50f, 0.30f);
            Color spoil = new Color(0.58f, 0.47f, 0.31f);
            Color duckboard = new Color(0.40f, 0.31f, 0.20f);

            if (u < 0.07f) return Color.Lerp(grass, earth, u / 0.07f);
            if (u < 0.21f) return Tufts(spoil, grass, grain);  // outer spoil berm
            if (u < 0.34f) return darkSoil;                    // parados inner slope
            if (u < 0.41f) return Duckboard(duckboard, v);     // ditch floor
            if (u < 0.47f) return Color.Lerp(darkSoil, earth, 0.6f); // firing step
            if (u < 0.63f) return Revetment(Color.Lerp(earth, darkSoil, 0.25f), duckboard, (u - 0.47f) / 0.16f, v);
            if (u < 0.73f) return spoil;                       // packed earth crest
            if (u < 0.94f) return Tufts(earth, grass, grain);  // outer parapet slope
            return Color.Lerp(earth, grass, (u - 0.94f) / 0.06f);
        }

        /// <summary>Barely-there duckboards: one thin seam every few metres of ditch floor.</summary>
        private static Color Duckboard(Color floor, float v)
        {
            float seam = Mathf.Abs(Mathf.Repeat(v * 0.7f, 1f) - 0.5f) * 2f;
            return floor * (seam > 0.94f ? 0.82f : 1f);
        }

        /// <summary>
        /// The fire-side wall under the parapet: horizontal planks held by a post every metre and a
        /// half, so the close view reads as a revetted wall a man stands against, not a clay slope.
        /// </summary>
        private static Color Revetment(Color wall, Color timber, float across, float v)
        {
            float post = Mathf.Abs(Mathf.Repeat(v * 2f, 1f) - 0.5f) * 2f;
            if (post > 0.93f) return timber * 0.7f;
            if (across > 0.88f) return wall;                    // spoil spilling over the top board
            float seam = Mathf.Abs(Mathf.Repeat(across * 4f, 1f) - 0.5f) * 2f;
            return Color.Lerp(wall, timber, 0.65f) * (seam > 0.88f ? 0.72f : 1f);
        }

        /// <summary>Dry grass already creeping back over the outer slopes, in sparse tufts.</summary>
        private static Color Tufts(Color soil, Color grass, float grain)
            => grain > 0.82f ? Color.Lerp(soil, grass * 0.85f, 0.7f) : soil;

        private static float Hash(float x, float y)
        {
            float value = Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
            return value - Mathf.Floor(value);
        }
    }
}
