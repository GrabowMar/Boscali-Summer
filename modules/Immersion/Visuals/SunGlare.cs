using BoscaliSummer.Core.Math;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Glare around the sun as an eye sees it: a soft veil and fine rays, no camera-lens streak or ghosts. It takes
    /// the sun's colour and strength through the air (white and fierce overhead, a dim orange glow at sunset) and
    /// goes out behind terrain, buildings and cloud. Drawn by the URP lens flare the game ships for its fireballs.
    /// - Skips the physics raycast while looking away (more than about 78 degrees off the sun).
    /// - One raycast at 10 Hz on the StaticsMask.
    /// Uses a proxy GameObject positioned in beginCameraRendering inside the camera far clip.
    /// </summary>
    internal sealed class SunGlare
    {
        private const float CheckInterval = 0.1f;
        private const float RayLength = 40000f;
        private const float ProxyDistance = 2.5f;
        private const int RaySize = 256;

        private static readonly Color VeilTint = new Color(1f, 0.93f, 0.82f, 1f), RayTint = new Color(1f, 0.97f, 0.92f, 1f);
        private static LensFlareDataSRP data;
        private static Texture2D rays;

        private Light sun;
        private GameObject proxy;
        private LensFlareComponentSRP flare;
        private float visibility;
        private bool blocked;
        private float nextCheck;
        private Camera flightCamera;

        /// <summary>Automation: multiplier on the flare intensity.</summary>
        public float Gain { get; set; } = 1f;

        public float Intensity => flare != null ? flare.intensity : 0f;
        /// <summary>0-1 sun visibility after the horizon, terrain and cloud transmission; it also fades while the view faces away from the sun.</summary>
        internal float Visibility => visibility;

        public void Tick(LevelInfo level, Camera camera, bool on, float dt, float transmission = 1f)
        {
            Light current = level != null ? level.sun : null;
            if (current != sun) Release();
            sun = current;
            flightCamera = camera;
            bool sunUp = sun != null && sun.isActiveAndEnabled && sun.intensity > 0f;
            if (!on || !sunUp || camera == null)
            {
                if (flare != null && flare.enabled) flare.enabled = false;
                visibility = 0f;
                return;
            }

            Vector3 toSun = -sun.transform.forward;
            Vector3 eye = camera.transform.position;
            Vector3 camForward = camera.transform.forward;

            // Performance optimization: skip raycasts if the sun is behind or far off-screen
            float viewDot = Vector3.Dot(camForward, toSun);
            if (viewDot < 0.2f)
            {
                visibility = Mathf.MoveTowards(visibility, 0f, 6f * dt);
                if (flare != null && flare.enabled) flare.intensity = 0f;
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

            float elevation = Mathf.Asin(Mathf.Clamp(toSun.y, -1f, 1f)) * Mathf.Rad2Deg;
            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + CheckInterval;
                blocked = Physics.Raycast(eye, toSun, RayLength, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore);
            }

            // Rays need a clean line to the disc: thin cloud leaves a glow, not a starburst, so cloud counts cubed.
            float through = Mathf.Clamp01(transmission);
            float target = blocked ? 0f : SunDiscMath.AboveHorizon(elevation) * through * through * through;
            // The eye loses the sun at once behind a ridge and takes a moment to be dazzled again.
            visibility = Mathf.MoveTowards(visibility, target, (target < visibility ? 10f : 4f) * dt);

            SunDiscMath.Transmission(elevation, camera.transform.GlobalPosition().y, out float r, out float g, out float b);
            float peak = Mathf.Max(Mathf.Max(r, g), Mathf.Max(b, 1e-4f));
            var air = new Color(r / peak, g / peak, b / peak, 1f);
            LensFlareDataElementSRP[] elements = flare.lensFlareData.elements;
            elements[0].tint = VeilTint * air;
            elements[1].tint = RayTint * air;
            // Glare follows how bright the disc looks, not its radiance: a square-root response keeps a low sun's glow.
            flare.intensity = visibility * Gain * Mathf.Sqrt(0.2126f * r + 0.7152f * g + 0.0722f * b);
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

        private void PlaceForCamera(ScriptableRenderContext context, Camera camera)
        {
            if (proxy == null || sun == null || camera == null || flightCamera == null) return;
            // The cockpit view is a camera stack: the flight camera draws the world, and post-processing (which draws
            // the flare) runs on an overlay camera parented to it. The flare has to be on for that one.
            bool view = camera == flightCamera || camera.transform.IsChildOf(flightCamera.transform);
            if (flare != null) flare.enabled = view && visibility > 0.001f;
            if (!view) return;
            int mask = camera.cullingMask;
            if (mask == 0) return;
            if ((mask & (1 << proxy.layer)) == 0)
            {
                int layer = 0;
                while (layer < 32 && (mask & (1 << layer)) == 0) layer++;
                if (layer < 32) proxy.layer = layer;
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
            flightCamera = null;
            visibility = 0f;
            blocked = false;
        }

        internal static void ReleaseData()
        {
            if (data != null) Object.Destroy(data);
            if (rays != null) Object.Destroy(rays);
            data = null;
            rays = null;
        }

        /// <summary>A wide soft veil and a burst of fine uneven rays. Built once and shared across scenes.</summary>
        private static LensFlareDataSRP Data()
        {
            if (data != null) return data;
            data = ScriptableObject.CreateInstance<LensFlareDataSRP>();
            data.name = "BoscaliSummer.SunGlare";
            data.hideFlags = HideFlags.DontSave;

            var veil = new LensFlareDataElementSRP
            {
                flareType = SRPLensFlareType.Circle,
                // Tuned in game: the gradient has to span the whole radius (edgeOffset 1) or the veil is a flat plate.
                localIntensity = 0.3f,
                tint = VeilTint,
                uniformScale = 8f,
                edgeOffset = 1f,
                fallOff = 0.25f,
                position = 0f
            };
            var burst = new LensFlareDataElementSRP
            {
                flareType = SRPLensFlareType.Image,
                lensFlareTexture = Rays(),
                preserveAspectRatio = true,
                localIntensity = 0.5f,
                tint = RayTint,
                uniformScale = 6.5f,
                position = 0f
            };
            data.elements = new[] { veil, burst };
            return data;
        }

        /// <summary>
        /// The eye's own starburst (light scattered by the lens fibres): many thin rays of uneven length and strength,
        /// brightest at the centre. Fixed seed, so the pattern belongs to the pilot and never shimmers.
        /// </summary>
        private static Texture2D Rays()
        {
            if (rays != null) return rays;
            const int Bins = 1024;
            var strength = new float[Bins];
            var length = new float[Bins];
            var random = new System.Random(7);
            for (int ray = 0; ray < 150; ray++)
            {
                int centre = random.Next(Bins);
                float s = (float)random.NextDouble(), reach = 0.3f + 0.7f * (float)random.NextDouble();
                s *= s;
                for (int k = -2; k <= 2; k++)
                {
                    int bin = (centre + k + Bins) % Bins;
                    float edge = s * (1f - Mathf.Abs(k) / 3f);
                    if (edge <= strength[bin]) continue;
                    strength[bin] = edge; length[bin] = reach;
                }
            }
            var pixels = new Color32[RaySize * RaySize];
            float half = (RaySize - 1) * 0.5f;
            for (int y = 0; y < RaySize; y++)
                for (int x = 0; x < RaySize; x++)
                {
                    float dx = (x - half) / half, dy = (y - half) / half;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    int bin = (int)((Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f) * Bins) % Bins;
                    float along = length[bin] > 0f ? Mathf.Clamp01(1f - radius / length[bin]) : 0f;
                    byte v = (byte)(255f * strength[bin] * along * along);
                    pixels[y * RaySize + x] = new Color32(v, v, v, 255);
                }
            rays = new Texture2D(RaySize, RaySize, TextureFormat.RGBA32, true, true)
            {
                name = "BoscaliSummer.SunGlare.Rays",
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            rays.SetPixels32(pixels);
            rays.Apply(true, true);
            return rays;
        }
    }
}
