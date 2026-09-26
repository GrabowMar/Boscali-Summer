using System;
using BepInEx.Logging;
using BoscaliSummer.Features.Weather.Configuration;
using BoscaliSummer.Features.Weather.Domain;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Features.Weather.Runtime
{
    /// <summary>
    /// Falling rain (and hail) around the camera: toroidally wrapped drop fields drawn by the
    /// bundle's <c>Boscali/RainField</c> shader, so the field never runs out at any airspeed and
    /// costs nothing per drop on the CPU.
    ///
    /// <para>Two nested rain layers — a dense near cube and a wider far cube that fades in where
    /// the near one fades out — plus a small hail layer used only under hail cores. The camera's
    /// position and the rain's drift are combined in double precision into each layer's wrap
    /// offset in the render callback for the main camera, so a drop stays still in the world
    /// while the aircraft moves through it and falls with the wind while time passes.</para>
    ///
    /// <para>Without the bundle a single Shuriken box of stretched billboards stands in (world
    /// velocity wind + fall, stretched by camera velocity); it is the old look done right, and
    /// the log says it is the fallback.</para>
    /// </summary>
    internal sealed class RainField : MonoBehaviour, ISceneService
    {
        private sealed class Layer
        {
            public string Name;
            public float Size;
            public int Drops;
            public float NearFade;
            public float FarStart;
            public float Width;
            public float MinPixels;
            public float MaxScreen;
            public float Alpha;
            public float StretchScale;
            public float LengthFalloff;
            public float HeadBoost;
            public bool Hail;

            public GameObject Object;
            public MeshRenderer Renderer;
            public Material Material;
            public double DriftX, DriftY, DriftZ;
            public float Density;
            public Vector3 Velocity;
        }

        private static readonly int OffsetId = Shader.PropertyToID("_RWOffset");
        private static readonly int FieldId = Shader.PropertyToID("_RWField");
        private static readonly int RelVelId = Shader.PropertyToID("_RWRelVel");
        private static readonly int ShapeId = Shader.PropertyToID("_RWShape");
        private static readonly int LookId = Shader.PropertyToID("_RWLook");
        private static readonly int ScreenId = Shader.PropertyToID("_RWScreen");
        private static readonly int TintId = Shader.PropertyToID("_RWTint");
        private static readonly int FlashId = Shader.PropertyToID("_RWFlash");

        private readonly Layer[] layers =
        {
            new Layer { Name = "Near", Size = 20f, Drops = 14000, NearFade = 1.2f, FarStart = 0.65f, Width = 0.005f,
                MinPixels = 1.1f, MaxScreen = 0.12f, Alpha = 0.32f, StretchScale = 1f, LengthFalloff = 0.9f, HeadBoost = 0.35f },
            new Layer { Name = "Far", Size = 84f, Drops = 40000, NearFade = 6.5f, FarStart = 0.55f, Width = 0.009f,
                MinPixels = 1.0f, MaxScreen = 0.08f, Alpha = 0.2f, StretchScale = 1f, LengthFalloff = 0.35f, HeadBoost = 0.2f },
            new Layer { Name = "Hail", Size = 26f, Drops = 3000, NearFade = 1.5f, FarStart = 0.6f, Width = 0.011f,
                MinPixels = 1.4f, MaxScreen = 0.05f, Alpha = 0.65f, StretchScale = 0.18f, LengthFalloff = 0.2f, HeadBoost = 0.6f, Hail = true },
        };

        private WeatherSettings settings;
        private WeatherManager manager;
        private ManualLogSource log;
        private bool built;
        private bool fallback;
        private bool subscribed;
        private Camera renderCamera;
        private FallbackRain fallbackRain;

        public void Configure(WeatherSettings weatherSettings, WeatherManager owner, ManualLogSource logger)
        {
            settings = weatherSettings;
            manager = owner;
            log = logger;
        }

        public void ResetForScene()
        {
            for (int i = 0; i < layers.Length; i++)
            {
                layers[i].DriftX = layers[i].DriftY = layers[i].DriftZ = 0.0;
                layers[i].Density = 0f;
                if (layers[i].Renderer != null) layers[i].Renderer.enabled = false;
            }
            fallbackRain?.Stop();
        }

        private void OnEnable()
        {
            if (subscribed) return;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            subscribed = true;
        }

        private void OnDisable()
        {
            if (!subscribed) return;
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            subscribed = false;
        }

        private void OnDestroy()
        {
            OnDisable();
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].Object != null) Destroy(layers[i].Object);
                if (layers[i].Material != null) Destroy(layers[i].Material);
            }
            fallbackRain?.Dispose();
        }

        private void Update()
        {
            if (Application.isBatchMode || manager == null || !manager.Ready || !settings.Enabled.Value)
            {
                Hide();
                return;
            }

            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            renderCamera = cameras != null ? cameras.mainCamera : null;
            if (renderCamera == null)
            {
                Hide();
                return;
            }

            if (!built) Build();

            WeatherPoint local = manager.Local;
            float altitude = manager.CameraGlobal.y;
            float aloft = 1f - WeatherMath.Smoothstep(local.CloudTop - 400f, local.CloudTop + 200f, altitude);
            float rain = local.RainRate * aloft;
            float quality = QualityScale(settings.Quality.Value);
            float fall = Mathf.Lerp(4f, 9f, WeatherMath.Smoothstep(0f, 20f, rain));
            Vector3 wind = new Vector3(local.WindX, local.WindUp * 0.5f, local.WindZ);
            Vector3 rainVelocity = wind + Vector3.down * fall;
            Vector3 hailVelocity = wind * 0.6f + Vector3.down * 18f;
            float dt = Mathf.Min(Time.deltaTime, 0.1f);

            float rainDensity = RainDensity(rain) * quality;
            float hailDensity = local.Hail ? WeatherMath.Smoothstep(20f, 60f, rain) * quality : 0f;

            if (fallback)
            {
                fallbackRain.Tick(renderCamera, cameras.cameraVelocity, rainVelocity, rainDensity, Tint(0.9f));
                return;
            }

            bool cockpit = cameras.currentState == cameras.cockpitState;
            Color flash = manager.Flash;
            float screenHeight = Mathf.Max(renderCamera.pixelHeight, 1);
            float tanHalf = Mathf.Tan(renderCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            for (int i = 0; i < layers.Length; i++)
            {
                Layer layer = layers[i];
                layer.Velocity = layer.Hail ? hailVelocity : rainVelocity;
                layer.Density = layer.Hail ? hailDensity : rainDensity;
                layer.DriftX += layer.Velocity.x * dt;
                layer.DriftY += layer.Velocity.y * dt;
                layer.DriftZ += layer.Velocity.z * dt;

                bool visible = layer.Density > 0.002f;
                layer.Renderer.enabled = visible;
                if (!visible) continue;

                Material m = layer.Material;
                Vector3 relative = layer.Velocity - cameras.cameraVelocity;
                float nearFade = layer.Hail || i > 0 ? layer.NearFade : cockpit ? 2.5f : layer.NearFade;
                m.SetVector(FieldId, new Vector4(layer.Size, nearFade, layer.FarStart, Mathf.Clamp01(layer.Density)));
                m.SetVector(RelVelId, relative);
                m.SetVector(ShapeId, new Vector4(1f / 60f, layer.Width, layer.MinPixels, layer.MaxScreen));
                m.SetVector(LookId, new Vector4(layer.LengthFalloff, 0.6f, layer.StretchScale, layer.HeadBoost));
                m.SetVector(ScreenId, new Vector4(screenHeight, tanHalf, 0f, 0f));
                float intensity = WeatherMath.Smoothstep(0f, 40f, rain);
                Color tint = Tint(layer.Hail ? 1.3f : 1f);
                tint.a = layer.Alpha * (layer.Hail ? 1f : Mathf.Lerp(0.6f, 1f, intensity));
                m.SetColor(TintId, tint);
                m.SetColor(FlashId, flash * (layer.Hail ? 1.2f : 0.8f));
            }
        }

        /// <summary>Snaps every visible layer to the camera being rendered, with its wrap offset.</summary>
        private void OnBeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (!built || fallback || camera == null || camera != renderCamera) return;
            Vector3 position = camera.transform.position;
            GlobalPosition global = position.ToGlobalPosition();
            for (int i = 0; i < layers.Length; i++)
            {
                Layer layer = layers[i];
                if (layer.Renderer == null || !layer.Renderer.enabled) continue;
                layer.Object.transform.SetPositionAndRotation(position, Quaternion.identity);
                double inv = 1.0 / layer.Size;
                layer.Material.SetVector(OffsetId, new Vector4(
                    Frac((global.x - layer.DriftX) * inv),
                    Frac((global.y - layer.DriftY) * inv),
                    Frac((global.z - layer.DriftZ) * inv),
                    0f));
            }
        }

        private void Build()
        {
            built = true;
            Shader shader = WeatherShaders.Get(WeatherShaders.RainField, log);
            if (shader == null)
            {
                fallback = true;
                fallbackRain = new FallbackRain(transform, log);
                log?.LogWarning("[Weather] Rain is using the fallback particle system (no bundle shader).");
                return;
            }

            for (int i = 0; i < layers.Length; i++)
            {
                Layer layer = layers[i];
                layer.Object = new GameObject("BoscaliRain" + layer.Name);
                DontDestroyOnLoad(layer.Object);
                layer.Object.hideFlags = HideFlags.DontSave;
                var filter = layer.Object.AddComponent<MeshFilter>();
                filter.sharedMesh = BuildMesh(layer.Drops, layer.Size, (uint)(0x9e3779b9u * (i + 1)));
                layer.Renderer = layer.Object.AddComponent<MeshRenderer>();
                layer.Material = new Material(shader) { name = "BoscaliRain" + layer.Name };
                layer.Renderer.sharedMaterial = layer.Material;
                layer.Renderer.shadowCastingMode = ShadowCastingMode.Off;
                layer.Renderer.receiveShadows = false;
                layer.Renderer.lightProbeUsage = LightProbeUsage.Off;
                layer.Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                layer.Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                layer.Renderer.allowOcclusionWhenDynamic = false;
                layer.Renderer.enabled = false;
            }
            log?.LogInfo("[Weather] Rain field ready: " + layers[0].Drops + " near, " + layers[1].Drops + " far, " + layers[2].Drops + " hail drops.");
        }

        private void Hide()
        {
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].Renderer != null) layers[i].Renderer.enabled = false;
            }
            fallbackRain?.Stop();
        }

        /// <summary>The environment's light on a drop: fog and sky ambient, a little sun.</summary>
        private static Color Tint(float gain)
        {
            Color fog = RenderSettings.fogColor;
            Color sky = RenderSettings.ambientSkyColor;
            Color c = Color.Lerp(fog, sky, 0.45f) * 1.15f * gain;
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            if (max > 1.2f) c *= 1.2f / max;
            c.r = Mathf.Max(c.r, 0.035f);
            c.g = Mathf.Max(c.g, 0.04f);
            c.b = Mathf.Max(c.b, 0.05f);
            c.a = 1f;
            return c;
        }

        /// <summary>Share of the field drawn for a rain rate: drizzle is sparse, a downpour is all of it.</summary>
        internal static float RainDensity(float rain)
        {
            if (rain < 0.05f) return 0f;
            return Mathf.Clamp01(0.12f + 0.88f * Mathf.Sqrt(Mathf.Clamp01(rain / 60f)));
        }

        private static float QualityScale(RainQuality quality)
        {
            switch (quality)
            {
                case RainQuality.Low: return 0.35f;
                case RainQuality.Medium: return 0.6f;
                default: return 1f;
            }
        }

        private static float Frac(double v) => (float)(v - Math.Floor(v));

        private static Mesh BuildMesh(int drops, float size, uint seed)
        {
            var vertices = new Vector3[drops * 4];
            var uvs = new Vector4[drops * 4];
            var indices = new int[drops * 6];
            uint state = seed | 1u;
            for (int i = 0; i < drops; i++)
            {
                var p = new Vector3(Next(ref state), Next(ref state), Next(ref state));
                float rank = (i + 0.5f) / drops;
                float jitter = Next(ref state);
                int v = i * 4;
                vertices[v] = vertices[v + 1] = vertices[v + 2] = vertices[v + 3] = p;
                uvs[v] = new Vector4(-1f, 0f, rank, jitter);
                uvs[v + 1] = new Vector4(1f, 0f, rank, jitter);
                uvs[v + 2] = new Vector4(1f, 1f, rank, jitter);
                uvs[v + 3] = new Vector4(-1f, 1f, rank, jitter);
                int t = i * 6;
                indices[t] = v;
                indices[t + 1] = v + 1;
                indices[t + 2] = v + 2;
                indices[t + 3] = v;
                indices[t + 4] = v + 2;
                indices[t + 5] = v + 3;
            }

            var mesh = new Mesh { name = "BoscaliRainField" };
            mesh.indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = vertices;
            mesh.SetUVs(0, uvs);
            mesh.SetIndices(indices, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * size * 2f);
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static float Next(ref uint state)
        {
            unchecked
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                return (state & 0x00ffffffu) / 16777216f;
            }
        }
    }
}
