using BoscaliSummer.Features.Immersion.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Features.Immersion.Runtime
{
    /// <summary>
    /// A procedural URP lens flare for the sun. The game ships the data-driven flare shader with
    /// every variant (it uses it for the nuclear fireball) but gives the sun none. Occlusion is done
    /// here rather than by URP's depth test, because post-processing runs on an overlay camera: the
    /// flare fades below the horizon, behind terrain and buildings (a 5 Hz raycast on the statics
    /// layer) and under the cloud deck (<c>LevelInfo.GetCloudOcclusion</c>).
    /// <para>URP skips a flare whose GameObject layer is not in the drawing camera's culling mask,
    /// and the flare pass runs on the game's <c>postProcessingRenderer</c> overlay camera, which
    /// renders almost nothing (the sun light's own layer included). The flare therefore hangs on a
    /// proxy that, just before each camera renders, takes a layer that camera draws and moves
    /// <see cref="ProxyDistance"/> towards the sun from it (inside that camera's 5 m far clip).</para>
    /// </summary>
    internal sealed class SunGlare
    {
        private const float CheckInterval = 0.2f;
        private const float RayLength = 40000f;
        private const float ProxyDistance = 2.5f;

        private static LensFlareDataSRP data;

        private Light sun;
        private GameObject proxy;
        private LensFlareComponentSRP flare;
        private float visibility;
        private float target;
        private float nextCheck;

        /// <summary>Automation: multiply the flare intensity.</summary>
        public float Gain { get; set; } = 1f;

        public float Intensity => flare != null && flare.isActiveAndEnabled ? flare.intensity : 0f;

        public void Tick(LevelInfo level, Camera camera, bool on, float dt)
        {
            Light current = level != null ? level.sun : null;
            if (current != sun) Release();
            sun = current;
            bool sunUp = sun != null && sun.gameObject.activeInHierarchy;
            if (!on || !sunUp || camera == null)
            {
                if (flare != null) flare.enabled = false;
                return;
            }

            if (flare == null)
            {
                flare = Proxy().AddComponent<LensFlareComponentSRP>();
                flare.useOcclusion = false;
                flare.allowOffScreen = false;
                flare.attenuationByLightShape = false;
                flare.maxAttenuationDistance = 1000f;
                flare.maxAttenuationScale = 1000f;
                flare.distanceAttenuationCurve = AnimationCurve.Constant(0f, 1f, 1f);
                flare.scaleByDistanceCurve = AnimationCurve.Constant(0f, 1f, 1f);
                flare.scale = 1f;
                flare.intensity = 0f;
                flare.lensFlareData = Data();
            }
            if (!flare.enabled) flare.enabled = true;

            Vector3 toSun = -sun.transform.forward;
            Vector3 eye = camera.transform.position;

            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + CheckInterval;
                float elevation = Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg;
                bool blocked = Physics.Raycast(eye, toSun, RayLength, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore);
                target = ImmersionMath.SunVisibility(elevation, level.GetCloudOcclusion(eye), blocked);
            }
            visibility = Mathf.MoveTowards(visibility, target, 4f * dt);
            flare.intensity = visibility * Gain;
        }

        private GameObject Proxy()
        {
            if (proxy == null)
            {
                proxy = new GameObject("BoscaliSummer.SunGlare");
                Object.DontDestroyOnLoad(proxy);
                RenderPipelineManager.beginCameraRendering += PlaceForCamera;
            }
            return proxy;
        }

        /// <summary>Exact for every camera, including the fast-moving cockpit one, because it runs
        /// after all Update/LateUpdate movement and right before that camera draws.</summary>
        private void PlaceForCamera(ScriptableRenderContext context, Camera camera)
        {
            if (proxy == null || sun == null || camera == null) return;
            int mask = camera.cullingMask;
            if (mask == 0) return;
            if ((mask & (1 << proxy.layer)) == 0)
            {
                int layer = 0;
                while ((mask & (1 << layer)) == 0) layer++;
                proxy.layer = layer;
            }
            float distance = Mathf.Min(ProxyDistance, camera.farClipPlane * 0.5f);
            proxy.transform.position = camera.transform.position - sun.transform.forward * distance;
        }

        public void Release()
        {
            if (proxy != null) RenderPipelineManager.beginCameraRendering -= PlaceForCamera;
            if (flare != null) Object.Destroy(flare);
            if (proxy != null) Object.Destroy(proxy);
            flare = null;
            proxy = null;
            sun = null;
            visibility = target = 0f;
        }

        /// <summary>A thin anamorphic streak, a faint halo ring and two sets of polygonal ghosts
        /// along the sun-to-centre axis. No core glow: the game's own sun bloom already covers it.
        /// Built once and shared across scenes.</summary>
        private static LensFlareDataSRP Data()
        {
            if (data != null) return data;
            data = ScriptableObject.CreateInstance<LensFlareDataSRP>();
            data.name = "BoscaliSummer.SunGlare";
            data.hideFlags = HideFlags.DontSave;

            var streak = new LensFlareDataElementSRP
            {
                flareType = SRPLensFlareType.Circle,
                localIntensity = 0.5f,
                tint = new Color(0.8f, 0.88f, 1f, 0.5f),
                sizeXY = new Vector2(14f, 0.07f),
                uniformScale = 1f,
                fallOff = 0.25f,
                position = 0f
            };
            var halo = new LensFlareDataElementSRP
            {
                flareType = SRPLensFlareType.Circle,
                inverseSDF = true,
                localIntensity = 0.16f,
                tint = new Color(1f, 0.82f, 0.62f, 0.5f),
                uniformScale = 2.4f,
                fallOff = 0.5f,
                edgeOffset = 0.2f,
                position = 0f
            };
            var ghosts = new LensFlareDataElementSRP
            {
                flareType = SRPLensFlareType.Polygon,
                sideCount = 6,
                sdfRoundness = 0.35f,
                localIntensity = 0.32f,
                tint = new Color(0.6f, 0.85f, 1f, 0.5f),
                uniformScale = 0.4f,
                fallOff = 0.7f,
                edgeOffset = 0.06f,
                allowMultipleElement = true,
                count = 4,
                distribution = SRPLensFlareDistribution.Uniform,
                lengthSpread = 1.5f,
                position = 0.7f
            };
            var amber = new LensFlareDataElementSRP
            {
                flareType = SRPLensFlareType.Circle,
                localIntensity = 0.26f,
                tint = new Color(1f, 0.72f, 0.4f, 0.5f),
                uniformScale = 0.6f,
                fallOff = 0.9f,
                allowMultipleElement = true,
                count = 2,
                distribution = SRPLensFlareDistribution.Uniform,
                lengthSpread = 0.5f,
                position = 1.55f
            };
            data.elements = new[] { streak, halo, ghosts, amber };
            return data;
        }
    }
}
