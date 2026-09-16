using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Features.Support.Visuals
{
    /// <summary>
    /// The station's EO/IR camera. A dedicated camera renders into its own texture the way the
    /// game's targeting camera does, placed on the station's true line of sight to the aim
    /// point but only a standoff distance away, with the field of view that frames the same
    /// ground footprint — near-orthographic, so the look angle, lean and parallax match the
    /// real geometry while staying inside render range. Frames are rendered on a cadence, not
    /// every frame; fog is suppressed for this camera only. At night the frame is read back
    /// asynchronously and turned into a stretched grayscale IR image at half resolution.
    /// Client-local; one instance, created by the uplink and destroyed when it closes.
    /// </summary>
    internal sealed class SatelliteImager : MonoBehaviour
    {
        public const float Standoff = 24000f;

        private int width = 768;
        private int height = 480;
        private int irWidth = 384;
        private int irHeight = 240;

        private Camera cam;
        private RenderTexture colour;
        private Texture2D infrared;
        private Color32[] infraredPixels;
        private bool infraredMode;
        private bool readbackPending;
        private float nextFrame;
        private int enabledFrame = -1;
        private bool fogWas = true;
        private uint noise = 0x9E3779B9u;

        public Texture Output => infraredMode ? (Texture)infrared : colour;
        public bool InfraredMode => infraredMode;
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
            imager.irWidth = imager.width / 2;
            imager.irHeight = imager.height / 2;
            go.SetActive(true);
            return imager;
        }

        private void Awake()
        {
            colour = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "BoscaliStationEO" };
            colour.Create();
            infrared = new Texture2D(irWidth, irHeight, TextureFormat.RGBA32, false) { name = "BoscaliStationIR" };
            infraredPixels = new Color32[irWidth * irHeight];

            cam = gameObject.AddComponent<Camera>();
            cam.enabled = false;
            cam.targetTexture = colour;
            cam.aspect = width / (float)height;
            cam.nearClipPlane = 200f;
            cam.farClipPlane = Standoff * 2.5f;
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
        /// Point the camera. <paramref name="lineOfSight"/> is the unit vector from the aim
        /// point up to the station; <paramref name="footprint"/> is the ground width framed.
        /// </summary>
        public void Aim(Vector3 aimLocal, Vector3 lineOfSight, Vector3 along, float footprint, bool night, float framesPerSecond)
        {
            if (cam == null) return;
            Vector3 los = lineOfSight.sqrMagnitude > 1e-6f ? lineOfSight.normalized : Vector3.up;
            transform.position = aimLocal + los * Standoff;
            Vector3 up = Vector3.ProjectOnPlane(along, los);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(Vector3.forward, los);
            transform.rotation = Quaternion.LookRotation(-los, up.normalized);
            // Unity's field of view is vertical; the footprint is the frame's width.
            float tall = footprint / Mathf.Max(0.1f, cam.aspect);
            cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(tall * 0.5f / Standoff) * Mathf.Rad2Deg, 0.05f, 60f);

            infraredMode = night;
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
            if (infraredMode && !readbackPending && SystemInfo.supportsAsyncGPUReadback)
            {
                readbackPending = true;
                AsyncGPUReadback.Request(colour, 0, TextureFormat.RGBA32, OnReadback);
            }
        }

        private void OnReadback(AsyncGPUReadbackRequest request)
        {
            readbackPending = false;
            if (this == null || infrared == null || request.hasError) return;
            NativeArray<Color32> source = request.GetData<Color32>();
            if (source.Length < width * height) return;
            const int step = 2;

            // Percentile-free stretch: min and max of a sparse sample, then a white-hot curve.
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

            for (int y = 0; y < irHeight; y++)
            {
                for (int x = 0; x < irWidth; x++)
                {
                    float l = (Luminance(source[y * step * width + x * step]) - low) / span;
                    noise ^= noise << 13;
                    noise ^= noise >> 17;
                    noise ^= noise << 5;
                    float grain = ((noise & 0xff) / 255f - 0.5f) * 0.08f;
                    byte v = (byte)Mathf.Clamp(Mathf.Pow(Mathf.Clamp01(l), 0.7f) * 235f + grain * 255f + 10f, 0f, 255f);
                    infraredPixels[y * irWidth + x] = new Color32(v, v, v, 255);
                }
            }
            infrared.SetPixels32(infraredPixels);
            infrared.Apply(false);
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
            if (infrared != null) Destroy(infrared);
        }
    }
}
