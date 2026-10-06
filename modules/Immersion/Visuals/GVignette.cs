using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Physiological visual effects for high G-forces:
    /// - Positive G (&gt; 4G): Greyout and peripheral tunnel vision (oxygen loss to retinal cones/rods).
    /// - Negative G (&lt; -1G): Redout (venous blood pooling in ocular vessels).
    /// Implemented via a URP Volume with Vignette and ColorAdjustments.
    /// Performance friendly:
    /// When outside the cockpit or in normal flight, volume.weight is 0f, so the URP post-processing
    /// pass incurs zero processing overhead.
    /// </summary>
    internal sealed class GVignette
    {
        private GameObject host;
        private Volume volume;
        private VolumeProfile profile;
        private Vignette vignette;
        private ColorAdjustments colorAdjust;
        private float currentWeight;
        private float targetWeight;
        private Camera flightCamera;

        public float Weight => currentWeight;

        public void Tick(float positive, float negative, bool cockpitView, bool enabled, float dt)
        {
            if (!enabled || !cockpitView)
            {
                currentWeight = 0f;
                if (volume != null) volume.weight = 0f;
                return;
            }

            float weight = Mathf.Max(positive, negative);
            float intensity = 0.45f;
            float saturation = -positive * 55f;
            float redout = negative;
            targetWeight = weight;

            if (targetWeight <= 0.001f && currentWeight <= 0.001f)
            {
                if (volume != null && volume.weight > 0f)
                {
                    volume.weight = 0f;
                    currentWeight = 0f;
                }
                return;
            }

            EnsureVolume();
            if (volume == null) return;

            // Slew weight smoothly
            currentWeight = targetWeight;
            volume.weight = currentWeight;

            if (vignette != null)
            {
                vignette.intensity.value = intensity;
                if (redout > 0.01f)
                {
                    // Blood-red vision under negative G
                    vignette.color.value = Color.Lerp(Color.black, new Color(0.85f, 0.05f, 0.05f), redout);
                }
                else
                {
                    // Black tunnel vision under positive G
                    vignette.color.value = Color.black;
                }
            }

            if (colorAdjust != null)
            {
                colorAdjust.saturation.value = saturation;
                if (redout > 0.01f)
                {
                    colorAdjust.colorFilter.value = Color.Lerp(Color.white, new Color(1f, 0.65f, 0.65f), redout);
                }
                else
                {
                    colorAdjust.colorFilter.value = Color.white;
                }
            }
        }

        public void Release()
        {
            RenderPipelineManager.beginCameraRendering -= ScopeForCamera;
            if (volume != null)
            {
                volume.weight = 0f;
            }
            if (profile != null)
            {
                if (vignette != null) Object.Destroy(vignette);
                if (colorAdjust != null) Object.Destroy(colorAdjust);
                Object.Destroy(profile);
                profile = null;
            }
            if (host != null)
            {
                Object.Destroy(host);
                host = null;
            }
            vignette = null;
            colorAdjust = null;
            volume = null;
            currentWeight = targetWeight = 0f;
            flightCamera = null;
        }

        private void EnsureVolume()
        {
            if (volume != null && host != null) return;

            host = new GameObject("BoscaliSummer.GVignette");
            Object.DontDestroyOnLoad(host);

            volume = host.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 100f;
            volume.weight = 0f;

            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            profile.name = "BoscaliSummer.GVignetteProfile";

            vignette = profile.Add<Vignette>(true);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = 0f;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = 0.55f;
            vignette.color.overrideState = true;
            vignette.color.value = Color.black;

            colorAdjust = profile.Add<ColorAdjustments>(true);
            colorAdjust.saturation.overrideState = true;
            colorAdjust.saturation.value = 0f;
            colorAdjust.colorFilter.overrideState = true;
            colorAdjust.colorFilter.value = Color.white;

            volume.profile = profile;
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            Camera camera = cameras != null ? cameras.mainCamera : null;
            flightCamera = camera;
            UniversalAdditionalCameraData cameraData = camera != null ? camera.GetComponent<UniversalAdditionalCameraData>() : null;
            if (cameraData != null)
            {
                int mask = cameraData.volumeLayerMask;
                for (int i = 0; i < 32; i++) if ((mask & (1 << i)) != 0) { host.layer = i; break; }
            }
            RenderPipelineManager.beginCameraRendering += ScopeForCamera;
        }

        private void ScopeForCamera(ScriptableRenderContext context, Camera camera)
        {
            if (volume != null) volume.weight = camera == flightCamera ? currentWeight : 0f;
        }
    }
}
