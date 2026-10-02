using UnityEngine;
using UnityEngine.Rendering;
using BoscaliSummer.Core.Fx;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Support.Visuals
{
    /// <summary>
    /// The orbital platform's live optical sensor. A dedicated camera renders
    /// the scene into a target texture, positioned on the station's true line-of-sight vector
    /// but with an adaptive standoff distance dynamically scaled to the framed footprint. This
    /// maintains a stable, natural focal length (10° to 50° FOV) across all zoom levels down to
    /// deep 60 m close-ups, eliminating floating-point depth buffer collapse, shadow clipping
    /// and terrain occlusion.
    ///
    /// The camera provides the live optical view. Radar products are formed separately by
    /// SarCollector when the host accepts a scan; the live view does no GPU readback.
    /// </summary>
    internal sealed class SatelliteImager : MonoBehaviour
    {
        public const float MaxStandoff = 24000f;
        public const float MinStandoff = 250f;

        private int width = 640;
        private int height = 400;
        private Camera cam;
        private RenderTexture colour;
        private float nextFrame;
        private int enabledFrame = -1;
        private bool fogWas = true;

        public Texture Output => colour;
        public Camera Camera => cam;
        public int FramesRendered { get; private set; }

        public static SatelliteImager Create(int pixelsWide, int pixelsHigh)
        {
            var go = new GameObject("BoscaliStationImager");
            DontDestroyOnLoad(go);
            go.SetActive(false);
            SatelliteImager imager = go.AddComponent<SatelliteImager>();
            float scale = FxBus.Scales.RenderTargets;
            int width = Mathf.RoundToInt(pixelsWide * scale);
            int height = Mathf.RoundToInt(pixelsHigh * scale);
            imager.width = Mathf.Clamp(width - width % 2, 64, 2048);
            imager.height = Mathf.Clamp(height - height % 2, 64, 2048);
            go.SetActive(true);
            return imager;
        }

        private void Awake()
        {
            colour = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "BoscaliStationEO" };
            colour.Create();
            if (!FxRtPool.Own(colour))
            {
                colour.Release();
                Destroy(colour);
                colour = null;
            }
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
            int excluded = (1 << PhysicsLayers.UI) | (1 << PhysicsLayers.HUD) | (1 << PhysicsLayers.Cockpit);
            // The optional EO view may show terrain and static structures only. Dynamic scene
            // units are host intel, not camera-only discoveries on a remote client.
            cam.cullingMask = (int)PhysicsLayers.StaticsMask & ~excluded;

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
            if (cam == null || colour == null) return;
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
            if (Time.unscaledTime >= nextFrame)
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
        }

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
            FxRtPool.Disown(colour);
        }
    }
}
