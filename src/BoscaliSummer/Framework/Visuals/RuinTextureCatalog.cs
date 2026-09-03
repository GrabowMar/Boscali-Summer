using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Framework.Visuals
{
    /// <summary>
    /// Shared procedural ruin textures and URP decal materials, generated once per session.
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
            float craterDepth = tier == RuinTier.Light ? 0.65f : (tier == RuinTier.Medium ? 1.45f : 2.20f);
            float rimRadius = tier == RuinTier.Light ? 0.35f : (tier == RuinTier.Medium ? 0.50f : 0.65f);

            // Compute composite heightfield: stepped floor slabs, broken pillars, rebar grid, and cellular cracks
            for (int y = 0; y < TexResolution; y++)
            {
                float ny = (y / (float)(TexResolution - 1)) * 2f - 1f;
                for (int x = 0; x < TexResolution; x++)
                {
                    float nx = (x / (float)(TexResolution - 1)) * 2f - 1f;
                    float dist = Mathf.Sqrt(nx * nx + ny * ny);

                    float voronoi = GetVoronoiCrack(nx * 3.8f, ny * 3.8f, seedOffset);
                    float fbm = GetFbm(nx * 4.5f, ny * 4.5f, 4, seedOffset);
                    float noisyDist = dist + (fbm - 0.5f) * 0.30f + (voronoi - 0.5f) * 0.18f;

                    float totalH = 0f;

                    if (noisyDist < rimRadius)
                    {
                        // Inside the breach: stepped architectural damage (gutted floors & columns)
                        float depthFraction = Mathf.Clamp01((rimRadius - noisyDist) / (rimRadius * 0.5f));
                        float baseCavity = -craterDepth * Mathf.Pow(depthFraction, 0.6f);

                        // Horizontal concrete floor slabs (stepped ledges repeating vertically)
                        float floorSlab = Mathf.Clamp01(Mathf.Sin(ny * 9.0f * Mathf.PI) * 4f) * 0.35f * craterDepth;

                        // Vertical sheared pillars / columns
                        float column = Mathf.Clamp01(Mathf.Sin(nx * 6.0f * Mathf.PI) * 3f) * 0.25f * craterDepth;

                        // Exposed corrugated steel decking / rebar grid in deep cavity
                        float rebarGrid = (Mathf.Sin(nx * 36f) * Mathf.Sin(ny * 36f)) * 0.08f;

                        // Combine into stepped architectural interior ruin
                        totalH = baseCavity + floorSlab + column + rebarGrid;
                    }
                    else if (noisyDist < rimRadius * 1.35f)
                    {
                        // Blown-out wall perimeter: raised jagged shattered concrete lips
                        float t = (noisyDist - rimRadius) / (rimRadius * 0.35f);
                        totalH = (craterDepth * 0.32f) * Mathf.Sin(t * Mathf.PI) * (voronoi * 0.8f + fbm * 0.4f);
                    }
                    else
                    {
                        // Exterior facade surface: spiderweb fracture cracks
                        float radialCrack = Mathf.Abs(Mathf.Sin(Mathf.Atan2(ny, nx) * 7f + fbm * 3f));
                        float crackMask = Mathf.Pow(Mathf.Clamp01(1f - dist * 1.05f), 1.5f);
                        totalH = (voronoi * 0.25f - radialCrack * 0.20f) * crackMask;
                    }

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
            float effectiveBump = bumpScale * 2.2f;
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

                    Vector3 normal = new Vector3(-dX * effectiveBump, -dY * effectiveBump, 1.0f).normalized;

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

            // Authentic architectural war ruin color palette:
            Color roomVoid = new Color(0.02f, 0.02f, 0.02f, 1f);        // Deep shadow of gutted interior rooms
            Color concreteFloor = new Color(0.38f, 0.36f, 0.34f, 1f);   // Exposed concrete floor slab edges
            Color shearedPillar = new Color(0.44f, 0.42f, 0.40f, 1f);   // Broken concrete columns / masonry
            Color steelRebar = new Color(0.18f, 0.14f, 0.10f, 1f);      // Charred rusty steel rebar & girders
            Color glowingEmber = new Color(1.0f, 0.45f, 0.08f, 1f);     // Smoldering hot crack embers
            Color sootBurst = new Color(0.04f, 0.04f, 0.04f, 1f);       // Carbon soot dispersion

            float rimRadius = tier == RuinTier.Light ? 0.35f : (tier == RuinTier.Medium ? 0.50f : 0.65f);

            for (int y = 0; y < TexResolution; y++)
            {
                float ny = (y / (float)(TexResolution - 1)) * 2f - 1f;
                for (int x = 0; x < TexResolution; x++)
                {
                    float nx = (x / (float)(TexResolution - 1)) * 2f - 1f;
                    float dist = Mathf.Sqrt(nx * nx + ny * ny);

                    float fbm = GetFbm(nx * 4.5f, ny * 4.5f, 3, seedOffset);
                    float voronoi = GetVoronoiCrack(nx * 3.8f, ny * 3.8f, seedOffset);

                    // Vertical upward soot drift (smoke plumes staining the wall upwards)
                    float upwardSmoke = Mathf.Clamp01((ny + 0.3f) * 0.7f) * Mathf.Pow(Mathf.Abs(Mathf.Sin(nx * 5f + fbm * 2f)), 2f);

                    float noisyDist = dist + (fbm - 0.5f) * 0.30f + (voronoi - 0.5f) * 0.18f;

                    float alpha;
                    Color color;

                    if (noisyDist < rimRadius * 0.85f)
                    {
                        // Inside the gutted building breach:
                        float floorSlab = Mathf.Clamp01(Mathf.Sin(ny * 9.0f * Mathf.PI) * 4f);
                        float column = Mathf.Clamp01(Mathf.Sin(nx * 6.0f * Mathf.PI) * 3f);

                        if (floorSlab > 0.4f)
                        {
                            // Horizontal floor slabs
                            color = Color.Lerp(concreteFloor, steelRebar, (fbm + 0.2f) * 0.5f);
                        }
                        else if (column > 0.5f)
                        {
                            // Vertical pillars
                            color = Color.Lerp(shearedPillar, roomVoid, 0.35f);
                        }
                        else
                        {
                            // Gutted dark room void
                            color = roomVoid;

                            // Glowing ember fissures in the depths
                            if (tier >= RuinTier.Medium && voronoi < 0.18f && noisyDist < rimRadius * 0.45f)
                            {
                                float emberT = (0.18f - voronoi) / 0.18f;
                                color = Color.Lerp(color, glowingEmber, emberT * 0.90f);
                            }
                        }

                        alpha = 0.99f;
                    }
                    else if (noisyDist < rimRadius * 1.25f)
                    {
                        // Shattered wall perimeter: jagged spall, chipped plaster & heavy soot
                        float t = (noisyDist - rimRadius * 0.85f) / (rimRadius * 0.40f);
                        color = Color.Lerp(shearedPillar, sootBurst, t);
                        alpha = Mathf.Lerp(0.99f, 0.55f, t);
                    }
                    else if (dist < 0.94f)
                    {
                        // Soot feathers and upward smoke plume stains
                        color = sootBurst;
                        float streak = (upwardSmoke * 0.6f + Mathf.Pow(Mathf.Abs(Mathf.Sin(Mathf.Atan2(ny, nx) * 7f)), 3f) * 0.4f);
                        alpha = streak * Mathf.Clamp01((0.94f - dist) / 0.35f) * 0.75f;
                    }
                    else
                    {
                        color = sootBurst;
                        alpha = 0f;
                    }

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
