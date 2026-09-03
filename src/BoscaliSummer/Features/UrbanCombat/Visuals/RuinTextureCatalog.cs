using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Generates and caches procedural normal (bump) maps, albedo textures, and URP decal
    /// materials for progressive building ruin and destruction effects.
    ///
    /// Performance-oriented:
    /// - Textures (256x256) are generated once procedurally and shared across all buildings.
    /// - Generates authentic tangent-space normal maps using Sobel height gradient filtering.
    /// - Incorporates Voronoi cellular crack networks and multi-octave Fractal Brownian Motion (FBM)
    ///   noise so damage creates chiseled 3D fracture relief under dynamic lighting.
    /// </summary>
    internal static class RuinTextureCatalog
    {
        public enum RuinTier
        {
            Light = 0,    // Bullets, shrapnel, minor spalling
            Medium = 1,   // Cannon shells, rockets, moderate blast
            Heavy = 2     // Heavy missiles, high-yield blast, structural breach
        }

        private const int TexResolution = 256;

        private static bool isInitialized;
        private static readonly Texture2D[] normalMaps = new Texture2D[3];
        private static readonly Texture2D[] albedoMaps = new Texture2D[3];
        private static readonly Material[] decalMaterials = new Material[3];
        private static Texture2D facadeDetailNormal;

        public static Texture2D FacadeDetailNormal
        {
            get
            {
                EnsureInitialized();
                return facadeDetailNormal;
            }
        }

        public static Material GetDecalMaterial(RuinTier tier)
        {
            EnsureInitialized();
            int idx = Mathf.Clamp((int)tier, 0, 2);
            return decalMaterials[idx];
        }

        public static Texture2D GetNormalMap(RuinTier tier)
        {
            EnsureInitialized();
            int idx = Mathf.Clamp((int)tier, 0, 2);
            return normalMaps[idx];
        }

        public static void EnsureInitialized()
        {
            if (isInitialized) return;
            isInitialized = true;

            try
            {
                // Generate procedural normal & albedo textures for 3 tiers
                normalMaps[0] = GenerateNormalMap(RuinTier.Light, 3.2f);
                albedoMaps[0] = GenerateAlbedoMap(RuinTier.Light);

                normalMaps[1] = GenerateNormalMap(RuinTier.Medium, 5.5f);
                albedoMaps[1] = GenerateAlbedoMap(RuinTier.Medium);

                normalMaps[2] = GenerateNormalMap(RuinTier.Heavy, 8.0f);
                albedoMaps[2] = GenerateAlbedoMap(RuinTier.Heavy);

                facadeDetailNormal = GenerateFacadeDetailNormal();

                // Create URP Decal materials using the best available decal shader
                Material baseDecalMat = ResolveBaseDecalMaterial();
                for (int i = 0; i < 3; i++)
                {
                    decalMaterials[i] = CreateDecalMaterial(baseDecalMat, albedoMaps[i], normalMaps[i], (RuinTier)i);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Boscali Summer] Failed to initialize RuinTextureCatalog: {ex}");
            }
        }

        private static Material ResolveBaseDecalMaterial()
        {
            // 1. Check scorchMarkDecal prefab on GameAssets
            if (GameAssets.i != null && GameAssets.i.scorchMarkDecal != null)
            {
                DecalProjector projector = GameAssets.i.scorchMarkDecal.GetComponent<DecalProjector>();
                if (projector != null && projector.material != null)
                {
                    return projector.material;
                }
            }

            // 2. Search Shader Graphs/scorchMark5 or crater5
            string[] preferredShaders = {
                "Shader Graphs/scorchMark5",
                "Shader Graphs/crater5",
                "Shader Graphs/CircleGradientDecal",
                "Universal Render Pipeline/Decal"
            };

            for (int s = 0; s < preferredShaders.Length; s++)
            {
                Shader shader = Shader.Find(preferredShaders[s]);
                if (shader != null)
                {
                    return new Material(shader) { name = "BoscaliSummer.RuinBase" };
                }
            }

            // 3. Search loaded materials in Resources
            Material[] mats = Resources.FindObjectsOfTypeAll<Material>();
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m != null && m.shader != null &&
                    m.shader.name.IndexOf("decal", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return m;
                }
            }

            return null;
        }

        private static Material CreateDecalMaterial(Material template, Texture2D albedo, Texture2D normal, RuinTier tier)
        {
            Material mat;
            if (template != null)
            {
                mat = new Material(template);
            }
            else
            {
                Shader fallback = Shader.Find("Universal Render Pipeline/Decal") ?? Shader.Find("Universal Render Pipeline/Lit");
                mat = new Material(fallback);
            }

            mat.name = $"BoscaliSummer.RuinDecal_{tier}";

            // Assign textures to common URP Decal and ShaderGraph property names
            string[] albedoProps = { "_BaseMap", "_MainTex", "_BaseColorMap", "_ColorMap" };
            for (int i = 0; i < albedoProps.Length; i++)
            {
                if (mat.HasProperty(albedoProps[i])) mat.SetTexture(albedoProps[i], albedo);
            }

            string[] normalProps = { "_NormalMap", "_BumpMap", "_Normal" };
            for (int i = 0; i < normalProps.Length; i++)
            {
                if (mat.HasProperty(normalProps[i]))
                {
                    mat.SetTexture(normalProps[i], normal);
                    mat.EnableKeyword("_NORMALMAP");
                    mat.EnableKeyword("_DECAL_NORMAL_BLEND_HIGH");
                }
            }

            if (mat.HasProperty("_NormalBlend")) mat.SetFloat("_NormalBlend", 0.95f);
            if (mat.HasProperty("_DecalBlend")) mat.SetFloat("_DecalBlend", 0.98f);
            if (mat.HasProperty("_DrawOrder")) mat.SetFloat("_DrawOrder", 10f + (int)tier);

            return mat;
        }

        // =========================================================================
        // PROCEDURAL NOISE & NORMAL MAP GENERATION
        // =========================================================================

        private static Texture2D GenerateNormalMap(RuinTier tier, float bumpScale)
        {
            float[,] heightfield = new float[TexResolution, TexResolution];

            int seedOffset = (int)tier * 37;
            float craterDepth = tier == RuinTier.Light ? 0.35f : (tier == RuinTier.Medium ? 0.65f : 0.92f);
            float crackWeight = tier == RuinTier.Light ? 0.25f : (tier == RuinTier.Medium ? 0.50f : 0.75f);
            float rimRadius = tier == RuinTier.Light ? 0.22f : (tier == RuinTier.Medium ? 0.30f : 0.38f);

            // Compute composite heightfield using Voronoi cracks + radial crater profile + FBM turbulence
            for (int y = 0; y < TexResolution; y++)
            {
                float ny = (y / (float)(TexResolution - 1)) * 2f - 1f;
                for (int x = 0; x < TexResolution; x++)
                {
                    float nx = (x / (float)(TexResolution - 1)) * 2f - 1f;
                    float dist = Mathf.Sqrt(nx * nx + ny * ny);

                    // 1. Radial crater cavity profile with raised beveled rim
                    float craterH = 0f;
                    if (dist < rimRadius)
                    {
                        // Inside crater: deep concave depression
                        float t = dist / rimRadius;
                        craterH = -craterDepth * Mathf.Cos(t * Mathf.PI * 0.5f);
                    }
                    else if (dist < rimRadius * 1.35f)
                    {
                        // Crater rim lip: raised displaced concrete
                        float t = (dist - rimRadius) / (rimRadius * 0.35f);
                        craterH = (craterDepth * 0.22f) * Mathf.Sin(t * Mathf.PI);
                    }

                    // 2. Cellular / Voronoi fracture crack lines
                    float voronoi = GetVoronoiCrack(nx * 3.5f, ny * 3.5f, seedOffset);

                    // 3. Multi-octave FBM surface roughness (pulverized concrete grit)
                    float fbm = GetFbm(nx * 5.0f, ny * 5.0f, 4, seedOffset);

                    // 4. Radial fracture rays branching from center
                    float angle = Mathf.Atan2(ny, nx);
                    float radialCracks = Mathf.Abs(Mathf.Sin(angle * 5f + fbm * 3f)) * Mathf.Clamp01(dist / rimRadius);
                    float crackMask = Mathf.Pow(Mathf.Clamp01(1f - dist * 1.2f), 1.5f);

                    // Combined composite height
                    float totalH = craterH + (voronoi * crackWeight + fbm * 0.18f - radialCracks * 0.22f) * crackMask;
                    heightfield[x, y] = totalH;
                }
            }

            // Convert composite heightfield to tangent-space normal map via Sobel gradient filter
            Texture2D tex = new Texture2D(TexResolution, TexResolution, TextureFormat.RGBA32, true)
            {
                name = $"RuinNormal_{tier}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] colors = new Color[TexResolution * TexResolution];
            for (int y = 0; y < TexResolution; y++)
            {
                int ym1 = Mathf.Max(0, y - 1);
                int yp1 = Mathf.Min(TexResolution - 1, y + 1);

                for (int x = 0; x < TexResolution; x++)
                {
                    int xm1 = Mathf.Max(0, x - 1);
                    int xp1 = Mathf.Min(TexResolution - 1, x + 1);

                    // Sobel filter for dH/dx and dH/dy
                    float dX = (heightfield[xp1, ym1] + 2f * heightfield[xp1, y] + heightfield[xp1, yp1])
                             - (heightfield[xm1, ym1] + 2f * heightfield[xm1, y] + heightfield[xm1, yp1]);

                    float dY = (heightfield[xm1, yp1] + 2f * heightfield[x, yp1] + heightfield[xp1, yp1])
                             - (heightfield[xm1, ym1] + 2f * heightfield[x, ym1] + heightfield[xp1, ym1]);

                    Vector3 normal = new Vector3(-dX * bumpScale, -dY * bumpScale, 1.0f).normalized;

                    // Encode into RGBA normal map [0, 1]
                    colors[y * TexResolution + x] = new Color(
                        normal.x * 0.5f + 0.5f,
                        normal.y * 0.5f + 0.5f,
                        normal.z * 0.5f + 0.5f,
                        1.0f
                    );
                }
            }

            tex.SetPixels(colors);
            tex.Apply(true, true); // Generate mipmaps, make upload static
            return tex;
        }

        private static Texture2D GenerateAlbedoMap(RuinTier tier)
        {
            Texture2D tex = new Texture2D(TexResolution, TexResolution, TextureFormat.RGBA32, true)
            {
                name = $"RuinAlbedo_{tier}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            Color[] colors = new Color[TexResolution * TexResolution];
            int seedOffset = (int)tier * 37 + 101;

            // Authentic war ruin color palette:
            Color charCore = new Color(0.06f, 0.06f, 0.06f, 1f);        // Deep scorched blast center
            Color exposedRebar = new Color(0.18f, 0.14f, 0.10f, 1f);    // Exposed rusty rebar / cavity core
            Color pulverizedConc = new Color(0.48f, 0.46f, 0.44f, 1f);  // Pulverized light gray concrete
            Color plasterSpall = new Color(0.38f, 0.36f, 0.34f, 1f);    // Chipped exterior paint / plaster
            Color outerSoot = new Color(0.12f, 0.12f, 0.12f, 1f);       // Carbon soot dispersion

            float radius = tier == RuinTier.Light ? 0.35f : (tier == RuinTier.Medium ? 0.46f : 0.58f);

            for (int y = 0; y < TexResolution; y++)
            {
                float ny = (y / (float)(TexResolution - 1)) * 2f - 1f;
                for (int x = 0; x < TexResolution; x++)
                {
                    float nx = (x / (float)(TexResolution - 1)) * 2f - 1f;
                    float dist = Mathf.Sqrt(nx * nx + ny * ny);

                    // Fractal edge erosion so the crater boundary is jagged and fractured, not a sphere
                    float fbm = GetFbm(nx * 4.2f, ny * 4.2f, 3, seedOffset);
                    float voronoi = GetVoronoiCrack(nx * 3.5f, ny * 3.5f, seedOffset);

                    float noisyDist = dist + (fbm - 0.5f) * 0.28f + (voronoi - 0.5f) * 0.16f;

                    float alpha;
                    Color color;

                    if (noisyDist < radius * 0.45f)
                    {
                        // Central cavity: scorched carbon & deep cavity
                        float t = noisyDist / (radius * 0.45f);
                        color = Color.Lerp(charCore, exposedRebar, t);
                        alpha = 0.98f;
                    }
                    else if (noisyDist < radius * 0.85f)
                    {
                        // Mid-crater ring: pulverized concrete aggregate & chiseled spall
                        float t = (noisyDist - radius * 0.45f) / (radius * 0.40f);
                        color = Color.Lerp(exposedRebar, pulverizedConc, t);
                        alpha = Mathf.Lerp(0.98f, 0.88f, t);
                    }
                    else if (noisyDist < radius * 1.25f)
                    {
                        // Outer rim: jagged radial soot & plaster chipping
                        float t = (noisyDist - radius * 0.85f) / (radius * 0.40f);
                        color = Color.Lerp(pulverizedConc, outerSoot, t);
                        alpha = Mathf.Lerp(0.88f, 0.0f, t * t);
                    }
                    else
                    {
                        color = outerSoot;
                        alpha = 0.0f;
                    }

                    // Embed alpha into color
                    color.a = Mathf.Clamp01(alpha);
                    colors[y * TexResolution + x] = color;
                }
            }

            tex.SetPixels(colors);
            tex.Apply(true, true);
            return tex;
        }

        private static Texture2D GenerateFacadeDetailNormal()
        {
            // Tiling high-frequency concrete fracture & ruin weathering normal map for building facades
            int size = 128;
            float[,] heightfield = new float[size, size];

            for (int y = 0; y < size; y++)
            {
                float ny = y / (float)size;
                for (int x = 0; x < size; x++)
                {
                    float nx = x / (float)size;
                    // Tiling noise
                    float v = GetVoronoiCrack(nx * 6.0f, ny * 6.0f, 999);
                    float f = GetFbm(nx * 8.0f, ny * 8.0f, 3, 888);
                    heightfield[x, y] = v * 0.6f + f * 0.4f;
                }
            }

            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "FacadeRuinDetailNormal",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };

            Color[] colors = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                int ym1 = (y - 1 + size) % size;
                int yp1 = (y + 1) % size;

                for (int x = 0; x < size; x++)
                {
                    int xm1 = (x - 1 + size) % size;
                    int xp1 = (x + 1) % size;

                    float dX = heightfield[xp1, y] - heightfield[xm1, y];
                    float dY = heightfield[x, yp1] - heightfield[x, ym1];
                    Vector3 n = new Vector3(-dX * 4.5f, -dY * 4.5f, 1.0f).normalized;

                    colors[y * size + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            }

            tex.SetPixels(colors);
            tex.Apply(true, true);
            return tex;
        }

        // =========================================================================
        // NOISE PRIMITIVES: VORONOI & FBM
        // =========================================================================

        private static float GetVoronoiCrack(float x, float y, int seed)
        {
            int xi = Mathf.FloorToInt(x);
            int yi = Mathf.FloorToInt(y);
            float xf = x - xi;
            float yf = y - yi;

            float d1 = 8.0f;
            float d2 = 8.0f;

            for (int j = -1; j <= 1; j++)
            {
                for (int i = -1; i <= 1; i++)
                {
                    Vector2 cell = new Vector2(i, j);
                    Vector2 point = Hash2(new Vector2(xi + i, yi + j) + Vector2.one * seed);
                    Vector2 diff = cell + point - new Vector2(xf, yf);
                    float d = diff.sqrMagnitude;

                    if (d < d1)
                    {
                        d2 = d1;
                        d1 = d;
                    }
                    else if (d < d2)
                    {
                        d2 = d;
                    }
                }
            }

            // (d2 - d1) produces thin, jagged Voronoi boundary ridges (concrete cracks)
            float crack = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);
            return Mathf.Clamp01(1.0f - Mathf.SmoothStep(0.02f, 0.22f, crack));
        }

        private static float GetFbm(float x, float y, int octaves, int seed)
        {
            float total = 0f;
            float amplitude = 0.5f;
            float frequency = 1.0f;
            float max = 0f;

            for (int i = 0; i < octaves; i++)
            {
                total += Mathf.PerlinNoise(x * frequency + seed, y * frequency + seed) * amplitude;
                max += amplitude;
                amplitude *= 0.5f;
                frequency *= 2.0f;
            }

            return total / max;
        }

        private static Vector2 Hash2(Vector2 p)
        {
            float x = Mathf.Sin(Vector2.Dot(p, new Vector2(127.1f, 311.7f))) * 43758.5453f;
            float y = Mathf.Sin(Vector2.Dot(p, new Vector2(269.5f, 183.3f))) * 43758.5453f;
            return new Vector2(x - Mathf.Floor(x), y - Mathf.Floor(y));
        }
    }
}
