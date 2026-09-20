using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Features.Support.Visuals
{
    /// <summary>
    /// The orbital platform's Synthetic Aperture Radar (SAR) sensor. A dedicated camera renders
    /// the scene into a target texture, positioned on the station's true line-of-sight vector
    /// but with an adaptive standoff distance dynamically scaled to the framed footprint. This
    /// maintains a stable, natural focal length (10° to 50° FOV) across all zoom levels down to
    /// deep 60 m close-ups, eliminating floating-point depth buffer collapse, shadow clipping
    /// and terrain occlusion.
    ///
    /// Every rendered frame is processed via an asynchronous SAR microwave imaging pipeline:
    /// dynamic contrast stretching, specular corner reflector blooming for metallic structures,
    /// vehicles and antennas, radar shadow / water attenuation, coherent Rayleigh speckle noise,
    /// and tactical phosphor radar monochrome formatting. Microwaves penetrate cloud decks and
    /// operate 24/7 day and night.
    /// </summary>
    internal sealed class SatelliteImager : MonoBehaviour
    {
        public const float Standoff = 24000f;
        public const float MaxStandoff = 24000f;
        public const float MinStandoff = 250f;

        private int width = 768;
        private int height = 480;
        private int sarWidth = 384;
        private int sarHeight = 240;

        private Camera cam;
        private RenderTexture colour;
        private Texture2D sarTexture;
        private Color32[] sarPixels;
        private bool readbackPending;
        private float nextFrame;
        private int enabledFrame = -1;
        private bool fogWas = true;
        private uint noise = 0x9E3779B9u;

        public Texture Output => sarTexture != null && FramesRendered > 0 ? (Texture)sarTexture : colour;
        public Camera Camera => cam;
        public bool InfraredMode => false;
        public int FramesRendered { get; private set; }

        public int Width => width;
        public float Aspect => width / (float)height;

        public static SatelliteImager Create(int pixelsWide, int pixelsHigh)
        {
            var go = new GameObject("BoscaliStationImager");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            SatelliteImager imager = go.AddComponent<SatelliteImager>();
            imager.width = Mathf.Clamp(pixelsWide - pixelsWide % 2, 64, 2048);
            imager.height = Mathf.Clamp(pixelsHigh - pixelsHigh % 2, 64, 2048);
            imager.sarWidth = imager.width / 2;
            imager.sarHeight = imager.height / 2;
            go.SetActive(true);
            return imager;
        }

        private void Awake()
        {
            colour = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "BoscaliStationEO" };
            colour.Create();
            sarTexture = new Texture2D(sarWidth, sarHeight, TextureFormat.RGBA32, false)
            {
                name = "BoscaliStationSAR",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            sarPixels = new Color32[sarWidth * sarHeight];

            cam = gameObject.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = colour;
            cam.aspect = width / (float)height;
            cam.nearClipPlane = 20f;
            cam.farClipPlane = MaxStandoff * 3.5f;
            cam.clearFlags = CameraClearFlags.Skybox;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.depth = -10f;
            Camera main = Camera.main;
            int excluded = (1 << PhysicsLayers.UI) | (1 << PhysicsLayers.HUD) | (1 << PhysicsLayers.Cockpit);
            cam.cullingMask = (main != null ? main.cullingMask : ~0) & ~excluded;

            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.renderPostProcessing = false;
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = true;
            }

            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        }

        /// <summary>
        /// Point the SAR radar sensor. <paramref name="lineOfSight"/> is the unit vector from the
        /// aim point up to the orbital platform; <paramref name="footprint"/> is the ground width framed.
        /// Standoff distance adapts to footprint to maintain a stable, non-collapsing field of view.
        /// </summary>
        public void Aim(Vector3 aimLocal, Vector3 lineOfSight, Vector3 along, float footprint, bool night, float framesPerSecond)
        {
            if (cam == null) return;
            Vector3 los = lineOfSight.sqrMagnitude > 1e-6f ? lineOfSight.normalized : Vector3.up;

            // Adaptive standoff: scale camera distance proportional to footprint
            float standoff = Mathf.Clamp(footprint * 2.8f, MinStandoff, MaxStandoff);
            Vector3 camPos = aimLocal + los * standoff;

            // Terrain clearance guard: ensure camera does not clip through mountains or hills
            float minAlt = aimLocal.y + Mathf.Max(50f, standoff * 0.12f);
            if (camPos.y < minAlt) camPos.y = minAlt;
            transform.position = camPos;

            Vector3 up = Vector3.ProjectOnPlane(along, los);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Vector3.forward, los);
            transform.rotation = Quaternion.LookRotation((aimLocal - camPos).normalized, up.normalized);

            // Dynamic FOV and clipping planes
            float tall = footprint / Mathf.Max(0.1f, cam.aspect);
            cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(tall * 0.5f / standoff) * Mathf.Rad2Deg, 8f, 55f);
            cam.nearClipPlane = Mathf.Max(2f, standoff * 0.015f);
            cam.farClipPlane = standoff * 4.0f;

            float interval = 1f / Mathf.Clamp(framesPerSecond, 0.5f, 15f);
            if (Time.unscaledTime >= nextFrame && !readbackPending)
            {
                nextFrame = Time.unscaledTime + interval;
                cam.enabled = true;
                enabledFrame = Time.frameCount;
            }
        }

        private void LateUpdate()
        {
            // A camera enabled for one frame renders once; switch it off again afterwards.
            if (cam != null && cam.enabled && Time.frameCount > enabledFrame) cam.enabled = false;
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != cam) return;
            fogWas = RenderSettings.fog;
            RenderSettings.fog = false;
        }

        private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (camera != cam) return;
            RenderSettings.fog = fogWas;
            FramesRendered++;
            if (!readbackPending && SystemInfo.supportsAsyncGPUReadback)
            {
                readbackPending = true;
                AsyncGPUReadback.Request(colour, 0, TextureFormat.RGBA32, OnReadback);
            }
        }

        private void OnReadback(AsyncGPUReadbackRequest request)
        {
            readbackPending = false;
            if (this == null || sarTexture == null || request.hasError) return;
            NativeArray<Color32> source = request.GetData<Color32>();
            if (source.Length < width * height) return;
            const int step = 2;

            // Sample dynamic range: find min and max luminance for adaptive radar contrast stretch
            float low = 1f, high = 0f;
            for (int y = 0; y < height; y += step * 4)
            {
                for (int x = 0; x < width; x += step * 4)
                {
                    float l = Luminance(source[y * width + x]);
                    if (l < low) low = l;
                    if (l > high) high = l;
                }
            }
            float span = Mathf.Max(0.02f, high - low);

            for (int y = 0; y < sarHeight; y++)
            {
                for (int x = 0; x < sarWidth; x++)
                {
                    float l = (Luminance(source[y * step * width + x * step]) - low) / span;
                    l = Mathf.Clamp01(l);

                    // Corner reflector blooming: vehicles, buildings and antennas reflect microwaves intensely
                    if (l > 0.65f)
                    {
                        float boost = (l - 0.65f) / 0.35f;
                        l = 0.65f + boost * 0.45f;
                    }
                    // Specular radar shadow / calm water attenuation: forward-scattered microwaves return zero backscatter
                    else if (l < 0.14f)
                    {
                        l *= 0.3f;
                    }

                    // Coherent radar speckle noise (Rayleigh-distributed multiplicative noise)
                    noise ^= noise << 13;
                    noise ^= noise >> 17;
                    noise ^= noise << 5;
                    float speckle = ((noise & 0xff) / 255f - 0.5f) * 0.16f;
                    float finalVal = Mathf.Clamp01(Mathf.Pow(l, 0.82f) + speckle);

                    // Tactical radar phosphor green palette
                    byte g = (byte)Mathf.Clamp(finalVal * 242f + 12f, 0f, 255f);
                    byte r = (byte)(g * 0.32f);
                    byte b = (byte)(g * 0.42f);
                    sarPixels[y * sarWidth + x] = new Color32(r, g, b, 255);
                }
            }
            sarTexture.SetPixels32(sarPixels);
            sarTexture.Apply(false);
        }

        private static float Luminance(Color32 c) => (0.299f * c.r + 0.587f * c.g + 0.114f * c.b) / 255f;

        private void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            if (cam != null) cam.targetTexture = null;
            if (colour != null)
            {
                colour.Release();
                Destroy(colour);
            }
            if (sarTexture != null) Destroy(sarTexture);
        }
    }
}
