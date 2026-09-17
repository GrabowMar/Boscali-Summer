using System;
using System.Reflection;
using BepInEx.Logging;
using BoscaliSummer.Features.Weather.Domain;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// Rain locked to the cockpit glass: one rain mesh per <c>Canopy.glassRenderers</c> entry,
    /// parented to the pane it copies so droplets stay on the glass when the pilot moves their
    /// head or the aircraft banks, and a runtime sheet that encodes <see cref="RainDrops"/> as
    /// normals the shader refracts the world through.
    ///
    /// <para>Everything here is created by this class and destroyed by <see cref="Release"/>:
    /// meshes, material, texture and one GameObject per pane. A vanilla mesh is only ever borrowed
    /// whole — a mesh this class did not create is never destroyed — and the vanilla glass material
    /// is never touched. Refraction needs the URP particle shader's
    /// <c>_DISTORTION_ON</c>+<c>_NORMALMAP</c> keywords (the inspector float alone does nothing),
    /// so when that shader is missing this drops to <c>Sprites/Default</c>: the rain still draws,
    /// it just does not refract.</para>
    ///
    /// <para>Owner: <see cref="WeatherRain"/>. This is one cockpit's effect, client-local and
    /// cosmetic; nothing here is networked and nothing reads world state.</para>
    /// </summary>
    internal sealed class GlassRain
    {
        /// <summary>Hard ceiling on the panes one canopy may claim; a canopy has a handful.</summary>
        private const int MaxPanes = 8;

        /// <summary>
        /// Sheet resolution and repaint budget. 256 px reads the droplets and re-uploads a quarter
        /// of a megabyte; repainting every frame would be 6 MB/s for no visible gain.
        /// </summary>
        private const int TextureSize = 256;
        private const float RepaintInterval = 0.1f;

        /// <summary>Resolves are retried for under a second after a scene or a seat change, then latched.</summary>
        private const int MaxAttempts = 8;

        /// <summary>A hitch must not jump every droplet across the pane in one step.</summary>
        private const float MaxAdvanceStep = 0.1f;

        /// <summary>Bounds on how much of a run's tail one repaint walks.</summary>
        private const int TailSteps = 6;
        private const float TailWidth = 0.6f;
        private const int MaxLensPixels = 24;

        /// <summary>Refraction amount. Unverified in this build: this is the knob to retune first.</summary>
        private const float DistortionStrength = 0.35f;
        private const float DistortionBlend = 0.5f;

        /// <summary>Pane units a second that one metre a second of crosswind drags a full-size droplet.</summary>
        private const float WindCoupling = 0.004f;

        /// <summary>~120 kt: above this the airflow starts stripping the glass, gone by ~400 kt.</summary>
        private const float SpeedFadeStart = 62f;
        private const float BlowOffSpeed = 206f;

        /// <summary>Above this it is ice, not rain: the layer fades out over the taper.</summary>
        private const float IceCeilingMetres = 12000f;
        private const float IceTaperMetres = 1800f;

        /// <summary>How far a lightning flash lifts the droplets. Cheap pop, no second thunder source.</summary>
        private const float FlashEmission = 0.8f;

        private const float DropletOpacity = 0.9f;
        private const float WetFilmFloor = 0.04f;
        private const float WetFilmSlope = 0.6f;

        private sealed class Pane
        {
            public GameObject Root;
            public Renderer Source;

            /// <summary>The mesh we render. Owned — and therefore destroyed — only when we copied it.</summary>
            public Mesh Mesh;
            public bool Owned;
        }

        private readonly RainDrops drops;
        private readonly Pane[] panes = new Pane[MaxPanes];

        private Material material;
        private Texture2D sheet;
        private Color32[] pixels;
        private bool alphaSheet;
        private int paneCount;
        private int attempts;
        private bool failed;
        private bool announced;

        private Lightning lightning;
        private int lightningAttempts;

        private float intensity;
        private PrecipitationKind kind;
        private float opacity;
        private float crossWind;
        private float blow;
        private float flash;
        private float repaintAt;

        private static AccessTools.FieldRef<Canopy, Renderer[]> glassRef;
        private static AccessTools.FieldRef<Lightning, Light> flashLightRef;
        private static AccessTools.FieldRef<Lightning, float> flashPeakRef;
        private static bool glassBound;
        private static bool flashBound;

        public GlassRain(int seed)
        {
            drops = new RainDrops(seed);
        }

        /// <summary>The layer exists and is drawing. False means the caller falls back.</summary>
        public bool Created => paneCount > 0 && material != null;

        /// <summary>Building is not worth retrying until something about the cockpit changes.</summary>
        public bool Failed => failed;

        /// <summary>The refracting shader resolved; false means the drawing-only fallback.</summary>
        public bool UsesDistortion { get; private set; }

        public int PaneCount => paneCount;

        /// <summary>
        /// Build the layer off the local cockpit's glass. False while nothing has resolved yet or
        /// the aircraft has no canopy; the owner retries for a few ticks and then stops asking.
        /// </summary>
        public bool TryCreate(ManualLogSource log)
        {
            if (Created) return true;
            if (failed) return false;

            if (attempts >= MaxAttempts)
            {
                failed = true;
                return false;
            }
            attempts++;

            try
            {
                Canopy canopy = LocalCanopy();
                if (canopy == null || !BindGlass()) return false;

                if (!BuildMaterial(log))
                {
                    failed = true;
                    return false;
                }
                if (!BuildPanes(canopy))
                {
                    Release();
                    return false;
                }
            }
            catch (Exception e)
            {
                // Nothing this class makes is worth a broken cockpit: give it all back and fall
                // back to the overlay for the rest of the scene.
                failed = true;
                Release();
                log?.LogWarning("Weather: canopy rain could not be built (" + e.Message + ").");
                return false;
            }

            if (!announced)
            {
                announced = true;
                log?.LogInfo(UsesDistortion
                    ? "Weather: canopy rain locked to " + paneCount + " glass pane(s) with URP " +
                      "particles (distortion + normal map keywords enabled)."
                    : "Weather: canopy rain locked to " + paneCount + " glass pane(s) with " +
                      "Sprites/Default — the URP particle shader is missing, so it draws without refraction.");
            }
            return true;
        }

        /// <summary>
        /// One 10 Hz update from scalars the owner already read from the snapshot. Visibility is
        /// the caller's call (cockpit view, canopy still attached); everything else is the sky.
        /// </summary>
        public void Apply(float intensity, float density, PrecipitationKind kind, bool visible,
                          float altitude, float airspeed, Vector3 wind)
        {
            this.intensity = Mathf.Clamp01(intensity);
            this.kind = kind;
            this.opacity = visible ? Mathf.Clamp01(density) * AltitudeFade(altitude) : 0f;

            float speed = Mathf.Clamp(airspeed, 0f, BlowOffSpeed);
            blow = speed / BlowOffSpeed;
            float speedFade = Mathf.InverseLerp(SpeedFadeStart, BlowOffSpeed, speed);
            crossWind = PaneCrossWind(wind);
            flash = Flash();
            if (float.IsNaN(crossWind)) crossWind = 0f;

            if (material == null || paneCount == 0) return;
            float alpha = intensity * opacity * (1f - speedFade) * DropletOpacity;
            SetColour("_Color", alpha);
            SetColour("_BaseColor", alpha);
            if (material.HasProperty("_EmissionColor"))
            {
                float lift = flash * FlashEmission;
                material.SetColor("_EmissionColor", new Color(lift, lift, lift));
            }

            for (int i = 0; i < paneCount; i++)
            {
                Pane pane = panes[i];
                pane.Root.SetActive(alpha > 0.01f && pane.Source != null && pane.Source.enabled);
            }
        }

        /// <summary>
        /// Advance the droplets every frame so the runs are smooth, and re-encode the sheet on the
        /// 10 Hz budget. Nothing is uploaded while the layer is not drawing.
        /// </summary>
        public void Advance(float deltaTime)
        {
            if (opacity <= 0f || paneCount == 0) return;
            float dt = Mathf.Clamp(deltaTime, 0f, MaxAdvanceStep);
            if (dt <= 0f) return;

            drops.Step(dt, intensity, kind, crossWind, blow);
            repaintAt -= dt;
            if (repaintAt > 0f) return;
            repaintAt = RepaintInterval;
            Repaint();
        }

        /// <summary>Destroy everything this class created. Safe to call repeatedly and at any time.</summary>
        public void Release()
        {
            for (int i = 0; i < panes.Length; i++)
            {
                Pane pane = panes[i];
                if (pane == null) continue;
                if (pane.Root != null) UnityEngine.Object.Destroy(pane.Root);
                if (pane.Owned && pane.Mesh != null) UnityEngine.Object.Destroy(pane.Mesh);
                panes[i] = null;
            }
            paneCount = 0;
            if (material != null) UnityEngine.Object.Destroy(material);
            material = null;
            if (sheet != null) UnityEngine.Object.Destroy(sheet);
            sheet = null;
            pixels = null;
            lightning = null;
            lightningAttempts = 0;
            opacity = 0f;
            flash = 0f;
            repaintAt = 0f;
        }

        /// <summary>A new scene or a new aircraft: release and forget every latch, then probe again.</summary>
        public void Reset()
        {
            Release();
            attempts = 0;
            failed = false;
            announced = false;
            drops.Reset();
        }

        // ---- Building ------------------------------------------------------------------------

        private bool BuildMaterial(ManualLogSource log)
        {
            if (material != null) return true;

            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            UsesDistortion = shader != null;
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                log?.LogWarning("Weather: neither the URP particle shader nor Sprites/Default resolved; " +
                                "canopy rain is disabled.");
                return false;
            }

            alphaSheet = !UsesDistortion;
            var created = new Material(shader) { name = "BoscaliSummer.GlassRain" };
            created.SetOverrideTag("RenderType", "Transparent");
            // Just after the glass draws, so the drops sit on the pane rather than behind it.
            created.renderQueue = (int)RenderQueue.Transparent + 1;
            Set("_Surface", 1f);
            Set("_Blend", 0f);
            Set("_SrcBlend", (float)BlendMode.SrcAlpha);
            Set("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            Set("_ZWrite", 0f);
            // Seen from inside the cockpit, so the glass's front faces point away from us.
            Set("_Cull", (float)CullMode.Off);
            created.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            // A mesh has no particle vertex streams: soft particles and camera fading must stay off.
            Set("_SoftParticlesEnabled", 0f);
            Set("_CameraFadingEnabled", 0f);
            created.DisableKeyword("_SOFTPARTICLES_ON");
            created.DisableKeyword("_CAMERAFADING_ON");
            if (UsesDistortion)
            {
                // Both keywords are load-bearing; the float is only the inspector's switch.
                created.EnableKeyword("_DISTORTION_ON");
                created.EnableKeyword("_NORMALMAP");
                Set("_DistortionEnabled", 1f);
                Set("_DistortionStrength", DistortionStrength);
                Set("_DistortionBlend", DistortionBlend);
            }
            if (created.HasProperty("_EmissionColor")) created.EnableKeyword("_EMISSION");

            sheet = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                name = "BoscaliSummer.GlassRainSheet",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear
            };
            pixels = new Color32[TextureSize * TextureSize];
            Repaint();

            if (created.HasProperty("_BaseMap")) created.SetTexture("_BaseMap", sheet);
            if (created.HasProperty("_MainTex")) created.SetTexture("_MainTex", sheet);
            created.mainTexture = sheet;
            material = created;
            return true;

            void Set(string property, float value)
            {
                if (created.HasProperty(property)) created.SetFloat(property, value);
            }
        }

        private bool BuildPanes(Canopy canopy)
        {
            Renderer[] glass = GlassRenderers(canopy);
            if (glass == null) return false;

            int built = 0;
            for (int i = 0; i < glass.Length && built < MaxPanes; i++)
            {
                Renderer source = glass[i];
                if (source == null) continue;
                Mesh mesh = SourceMesh(source);
                if (mesh == null) continue;

                var pane = new Pane { Source = source };
                pane.Mesh = RainMesh(mesh, out pane.Owned);
                // Registered before anything can throw, so a failed build is still releasable.
                panes[built] = pane;

                var root = new GameObject("BoscaliSummer.GlassRain" + built);
                pane.Root = root;
                root.transform.SetParent(source.transform, false);
                if (source is SkinnedMeshRenderer skinned) BuildSkinned(root, skinned, pane);
                else BuildStatic(root, pane);

                root.SetActive(false);
                built++;
            }
            paneCount = built;
            return built > 0;
        }

        private void BuildStatic(GameObject root, Pane pane)
        {
            var filter = root.AddComponent<MeshFilter>();
            filter.sharedMesh = pane.Mesh;

            var renderer = root.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        /// <summary>
        /// A skinned pane keeps the canopy's own bones, so the copy deforms exactly as the glass
        /// does when the canopy moves, without touching the vanilla renderer.
        /// </summary>
        private void BuildSkinned(GameObject root, SkinnedMeshRenderer source, Pane pane)
        {
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = pane.Mesh;
            renderer.bones = source.bones;
            renderer.rootBone = source.rootBone;
            renderer.quality = source.quality;
            renderer.updateWhenOffscreen = source.updateWhenOffscreen;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        // ---- Glass and sky access ------------------------------------------------------------

        private static Canopy LocalCanopy()
        {
            GameManager.GetLocalAircraft(out Aircraft local);
            return local != null ? local.GetComponentInChildren<Canopy>(true) : null;
        }

        private static bool BindGlass()
        {
            if (glassBound) return true;
            try
            {
                glassRef = FieldRef<Canopy, Renderer[]>("glassRenderers");
                glassBound = true;
            }
            catch (Exception)
            {
                glassBound = false;
            }
            return glassBound;
        }

        private static bool BindFlash()
        {
            if (flashBound) return true;
            bool light;
            bool peak;
            try
            {
                flashLightRef = FieldRef<Lightning, Light>("flashLight");
                light = true;
            }
            catch (Exception)
            {
                light = false;
            }
            try
            {
                flashPeakRef = FieldRef<Lightning, float>("flashIntensity");
                peak = true;
            }
            catch (Exception)
            {
                peak = false;
            }
            flashBound = light && peak;
            return flashBound;
        }

        private static Renderer[] GlassRenderers(Canopy canopy)
        {
            try
            {
                return glassRef(canopy);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static Mesh SourceMesh(Renderer source)
        {
            if (source is SkinnedMeshRenderer skinned) return skinned.sharedMesh;
            var filter = source.GetComponent<MeshFilter>();
            return filter != null ? filter.sharedMesh : null;
        }

        /// <summary>
        /// The pane's own geometry with UVs we choose, so the droplets are round and evenly scaled
        /// whatever the vanilla UV layout is. A mesh the game does not expose for reading is
        /// borrowed whole and keeps its own UVs — worse tiling, but still rain on the glass — and
        /// it is never ours to destroy.
        /// </summary>
        private static Mesh RainMesh(Mesh source, out bool owned)
        {
            owned = false;
            if (!source.isReadable) return source;

            try
            {
                Vector3[] vertices = source.vertices;
                Vector3[] normals = source.normals;
                var uv = new Vector2[vertices.Length];

                int axis = DominantAxis(normals);
                int u = axis == 0 ? 2 : 0;
                int v = axis == 1 ? 2 : 1;

                Bounds bounds = source.bounds;
                Vector3 center = bounds.center;
                Vector3 size = bounds.size;
                // One scale for both axes keeps the droplets round; the smaller axis simply
                // occupies the middle of the sheet.
                float scale = Mathf.Max(Mathf.Max(Mathf.Abs(Component(size, u)),
                                                  Mathf.Abs(Component(size, v))), 1e-3f);

                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 vertex = vertices[i];
                    uv[i] = new Vector2(
                        (Component(vertex, u) - Component(center, u)) / scale + 0.5f,
                        (Component(vertex, v) - Component(center, v)) / scale + 0.5f);
                }

                var mesh = new Mesh { name = source.name + " (Boscali rain)" };
                mesh.SetVertices(vertices);
                mesh.subMeshCount = source.subMeshCount;
                for (int submesh = 0; submesh < source.subMeshCount; submesh++)
                    mesh.SetTriangles(source.GetTriangles(submesh), submesh);
                mesh.SetUVs(0, uv);
                mesh.RecalculateBounds();
                owned = true;
                return mesh;
            }
            catch (Exception)
            {
                // A mesh we cannot read becomes one we cannot copy; borrow it instead.
                return source;
            }
        }

        /// <summary>The axis the pane mostly faces along, in the mesh's own local space.</summary>
        private static int DominantAxis(Vector3[] normals)
        {
            Vector3 average = Vector3.zero;
            for (int i = 0; i < normals.Length; i++) average += normals[i];
            float x = Mathf.Abs(average.x);
            float y = Mathf.Abs(average.y);
            float z = Mathf.Abs(average.z);
            if (x >= y && x >= z) return 0;
            return y >= z ? 1 : 2;
        }

        private static float Component(Vector3 vector, int axis)
        {
            switch (axis)
            {
                case 0: return vector.x;
                case 1: return vector.y;
                default: return vector.z;
            }
        }

        /// <summary>
        /// Sideways drift in pane units a second. The pane's own right axis, not the camera's: the
        /// droplets are on the glass and must bank with it.
        /// </summary>
        private float PaneCrossWind(Vector3 wind)
        {
            if (paneCount == 0 || panes[0].Source == null) return 0f;
            return Vector3.Dot(wind, panes[0].Source.transform.right) * WindCoupling;
        }

        /// <summary>
        /// The vanilla lightning light's own decay envelope, 0..1. Read only: the game owns the
        /// flash and its thunder, this only tints the droplets while it lasts.
        /// </summary>
        private float Flash()
        {
            if (!BindFlash()) return 0f;
            if (lightning == null)
            {
                if (lightningAttempts >= MaxAttempts) return 0f;
                lightningAttempts++;
                lightning = UnityEngine.Object.FindObjectOfType<Lightning>(true);
                if (lightning == null) return 0f;
            }

            try
            {
                Light light = flashLightRef(lightning);
                if (light == null || !light.enabled) return 0f;
                float peak = Mathf.Max(flashPeakRef(lightning), 1e-3f);
                return Mathf.Clamp01(light.intensity / peak);
            }
            catch (Exception)
            {
                lightningAttempts = MaxAttempts;
                return 0f;
            }
        }

        // ---- The sheet ------------------------------------------------------------------------

        /// <summary>
        /// Re-encode the pane. Every pixel is rewritten from <see cref="RainDrops"/>. The normal
        /// layout is R = x*0.5+0.5, G = y*0.5+0.5, B = 1, A = 1 — exactly what the shipped
        /// shader's decode (<c>x = sampled.r * sampled.a</c>, <c>y = sampled.g</c>) reads back.
        /// Do not swizzle for DXT5nm.
        /// </summary>
        private void Repaint()
        {
            if (pixels == null || sheet == null) return;

            Color32 flat = alphaSheet
                ? new Color32(255, 255, 255, 0)
                : new Color32(128, 128, 255, 255);
            for (int i = 0; i < pixels.Length; i++) pixels[i] = flat;

            PaintWetFilm();
            for (int i = 0; i < drops.Count; i++) PaintDrop(i);

            sheet.SetPixels32(pixels);
            sheet.Apply(false);
        }

        private void PaintDrop(int index)
        {
            float x = drops.X(index);
            float y = drops.Y(index);
            float radius = Mathf.Clamp(drops.Radius(index) * TextureSize, 1f, MaxLensPixels);
            PaintLens(x, y, radius);

            float tailX = drops.TailX(index) * TextureSize;
            float tailY = drops.TailY(index) * TextureSize;
            float length = (float)Math.Sqrt(tailX * tailX + tailY * tailY);
            if (length < 1f) return;

            // One lens per lens-width of tail: the work tracks the streak's visible area, not
            // its pixel length.
            int steps = Mathf.Clamp(Mathf.CeilToInt(length / radius), 1, TailSteps);
            float stepX = tailX / steps;
            float stepY = tailY / steps;
            for (int step = 1; step <= steps; step++)
            {
                float t = step / (float)steps;
                PaintLens(x + stepX * step, y + stepY * step, radius * TailWidth * (1f - t * 0.7f));
            }
        }

        /// <summary>The water film between the droplets, as the slope of its own field.</summary>
        private void PaintWetFilm()
        {
            int cellWidth = TextureSize / RainDrops.WetColumns;
            int cellHeight = TextureSize / RainDrops.WetRows;
            for (int row = 0; row < RainDrops.WetRows; row++)
            {
                for (int column = 0; column < RainDrops.WetColumns; column++)
                {
                    float wet = drops.Wetness(column, row);
                    if (wet < WetFilmFloor) continue;
                    int nextColumn = column + 1 < RainDrops.WetColumns ? column + 1 : column;
                    int nextRow = row + 1 < RainDrops.WetRows ? row + 1 : row;
                    float nx = (drops.Wetness(nextColumn, row) - wet) * WetFilmSlope;
                    float ny = (drops.Wetness(column, nextRow) - wet) * WetFilmSlope;
                    PaintPatch(column * cellWidth, row * cellHeight, cellWidth, cellHeight, nx, ny);
                }
            }
        }

        private void PaintPatch(int left, int top, int width, int height, float nx, float ny)
        {
            Color32 pixel = Encode(nx, ny);
            for (int y = top; y < top + height; y++)
            {
                int row = Wrap(y) * TextureSize;
                for (int x = left; x < left + width; x++) pixels[row + Wrap(x)] = pixel;
            }
        }

        /// <summary>A dome of surface normal in pane space — y positive down the pane.</summary>
        private void PaintLens(float x, float y, float radius)
        {
            if (radius < 0.5f) return;
            float inverse = 1f / radius;
            int reach = Mathf.CeilToInt(radius);
            int centerX = Mathf.RoundToInt(x * TextureSize);
            int centerY = Mathf.RoundToInt((1f - y) * TextureSize);

            for (int dy = -reach; dy <= reach; dy++)
            {
                float ny = dy * inverse;
                int row = Wrap(centerY + dy) * TextureSize;
                for (int dx = -reach; dx <= reach; dx++)
                {
                    float nx = dx * inverse;
                    if (nx * nx + ny * ny > 1f) continue;
                    pixels[row + Wrap(centerX + dx)] = Encode(nx, ny);
                }
            }
        }

        /// <summary>Pane-space normal to a sheet pixel: the pane's down is the sheet's -v.</summary>
        private Color32 Encode(float nx, float ny)
        {
            if (alphaSheet)
            {
                float coverage = Mathf.Clamp01(1f - (nx * nx + ny * ny));
                return new Color32(255, 255, 255, (byte)(coverage * 255f));
            }

            return new Color32(
                (byte)(Mathf.Clamp01(nx * 0.5f + 0.5f) * 255f),
                (byte)(Mathf.Clamp01(0.5f - ny * 0.5f) * 255f),
                255,
                255);
        }

        private static int Wrap(int value)
        {
            value %= TextureSize;
            return value < 0 ? value + TextureSize : value;
        }

        private void SetColour(string property, float alpha)
        {
            if (material.HasProperty(property)) material.SetColor(property, new Color(1f, 1f, 1f, alpha));
        }

        private static float AltitudeFade(float altitude) =>
            Mathf.Clamp01((IceCeilingMetres - altitude) / IceTaperMetres);

        private static AccessTools.FieldRef<TInstance, TField> FieldRef<TInstance, TField>(string name)
        {
            FieldInfo field = AccessTools.Field(typeof(TInstance), name) ??
                throw new MissingFieldException(typeof(TInstance).FullName, name);
            return AccessTools.FieldRefAccess<TInstance, TField>(field);
        }
    }
}
