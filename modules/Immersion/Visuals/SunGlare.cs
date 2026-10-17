using BoscaliSummer.Core.Math;
using UnityEngine;
using UnityEngine.Rendering;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Glare around the sun as an eye sees it: a tight core, a short halo and a few soft rays, no veil, camera-lens streak or ghosts. It takes
    /// the sun's colour and strength through the air (white and fierce overhead, a dim orange glow at sunset) and
    /// goes out behind terrain, buildings and cloud. Drawn by the URP lens flare the game ships for its fireballs.
    /// - Fades smoothly beyond the rendered view and skips the physics raycast once the glare is out of sight.
    /// - One raycast at 10 Hz on the StaticsMask.
    /// Uses a proxy GameObject positioned in beginCameraRendering inside the camera far clip.
    /// </summary>
    internal sealed class SunGlare
    {
        private const float CheckInterval = 0.1f;
        private const float RayLength = 40000f;
        private const float ProxyDistance = 2.5f;
        private const int RaySize = 512;

        private static readonly Color HaloTint = new Color(1f, 0.93f, 0.82f, 1f), RayTint = new Color(1f, 0.97f, 0.92f, 1f);
        private static LensFlareDataSRP data;
        private static Texture2D rays;

        private Light sun;
        private GameObject proxy;
        private LensFlareComponentSRP flare;
        private float visibility, viewFade, frameIntensity;
        private bool blocked;
        private float nextCheck;
        private Camera flightCamera;

        /// <summary>Automation: multiplier on the flare intensity.</summary>
        public float Gain { get; set; } = 1f;

        public float Intensity => frameIntensity * viewFade;
        /// <summary>0-1 sun visibility after the horizon, terrain and cloud transmission; it also fades while the view faces away from the sun.</summary>
        internal float Visibility => visibility * viewFade;

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
                visibility = viewFade = frameIntensity = 0f;
                return;
            }

            Vector3 toSun = -sun.transform.forward;
            Vector3 eye = camera.transform.position;
            viewFade = ViewFade(camera, toSun);
            if (viewFade <= 0f)
            {
                visibility = Mathf.MoveTowards(visibility, 0f, 6f * dt);
                frameIntensity = 0f;
                if (flare != null && flare.enabled) flare.intensity = 0f;
                return;
            }

            if (flare == null)
            {
                flare = Proxy().AddComponent<LensFlareComponentSRP>();
                flare.useOcclusion = false;
                // Let the glare enter/leave the edge continuously; the shared screen fade limits its reach.
                flare.allowOffScreen = true;
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
            elements[0].tint = HaloTint * air;
            elements[1].tint = RayTint * air;
            elements[2].tint = RayTint * air;
            // Glare follows how bright the disc looks, not its radiance: a square-root response keeps a low sun's glow.
            frameIntensity = visibility * Gain * Mathf.Sqrt(0.2126f * r + 0.7152f * g + 0.0722f * b);
            flare.intensity = frameIntensity * viewFade;
        }

        /// <summary>Use the rendered view, including camera-parent scale or an explicit view override.</summary>
        internal static float ViewFade(Camera camera, Vector3 toSun)
        {
            Vector3 direction = camera.worldToCameraMatrix.MultiplyVector(toSun);
            float facing = -direction.z / Mathf.Max(direction.magnitude, 1e-5f);
            if (facing <= 0f) return 0f;
            Vector4 clip = camera.projectionMatrix * new Vector4(direction.x, direction.y, direction.z, 0f);
            if (clip.w <= 1e-5f) return 0f;
            return Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(facing * 4f)) *
                SunDiscMath.ScreenFade(clip.x / clip.w * 0.5f + 0.5f, clip.y / clip.w * 0.5f + 0.5f);
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
            if (proxy == null || flare == null || !flare.enabled || sun == null || camera == null || flightCamera == null) return;
            // The cockpit view is a camera stack: the flight camera draws the world, and post-processing (which draws
            // the flare) runs on an overlay camera parented to it. The flare has to be on for that one.
            bool view = camera == flightCamera || camera.transform.IsChildOf(flightCamera.transform);
            // OnEnable/OnDisable mutate SRP's flare registry and allocate. Keep registration stable across the stack;
            // SRP skips zero-intensity flares before doing any projection, occlusion or element draws.
            flare.intensity = view ? frameIntensity * ViewFade(camera, -sun.transform.forward) : 0f;
            if (!view || flare.intensity <= 0f) return;
            int mask = camera.cullingMask;
            if (mask == 0) { flare.intensity = 0f; return; }
            if ((mask & (1 << proxy.layer)) == 0)
            {
                int layer = 0;
                while (layer < 32 && (mask & (1 << layer)) == 0) layer++;
                if (layer < 32) proxy.layer = layer;
            }
            float near = camera.nearClipPlane + Mathf.Max(0.001f, camera.nearClipPlane * 0.05f);
            float far = camera.farClipPlane - Mathf.Max(0.001f, camera.farClipPlane * 0.01f);
            if (far <= near) { flare.intensity = 0f; return; }
            Vector3 toSun = -sun.transform.forward;
            float forwardDepth = -camera.worldToCameraMatrix.MultiplyVector(toSun).z;
            float distance = Mathf.Clamp(ProxyDistance, near, far) / Mathf.Max(forwardDepth, 1e-5f);
            // Anchor to the rendered eye: a floating-origin/view override can move it independently of the transform.
            proxy.transform.position = camera.cameraToWorldMatrix.MultiplyPoint3x4(Vector3.zero) + toSun * distance;
        }

        public void Release()
        {
            if (proxy != null) RenderPipelineManager.beginCameraRendering -= PlaceForCamera;
            if (flare != null) flare.intensity = 0f;
            if (flare != null) Object.Destroy(flare);
            if (proxy != null) Object.Destroy(proxy);
            flare = null;
            proxy = null;
            sun = null;
            flightCamera = null;
            visibility = viewFade = frameIntensity = nextCheck = 0f;
            blocked = false;
        }

        internal static void ReleaseData()
        {
            if (data != null) Object.Destroy(data);
            if (rays != null) Object.Destroy(rays);
            data = null;
            rays = null;
        }

        /// <summary>A short halo, a few soft rays and a tight core. Built once and shared across scenes.</summary>
        private static LensFlareDataSRP Data()
        {
            if (data != null) return data;
            data = ScriptableObject.CreateInstance<LensFlareDataSRP>();
            data.name = "BoscaliSummer.SunGlare";
            data.hideFlags = HideFlags.DontSave;

            // The halo spans its whole radius (edgeOffset 1) but stays small: a wide one reads as a grey wash.
            var halo = new LensFlareDataElementSRP
            {
                flareType = SRPLensFlareType.Circle,
                localIntensity = 0.06f,
                tint = HaloTint,
                uniformScale = 3.2f,
                edgeOffset = 1f,
                fallOff = 0.5f,
                position = 0f
            };
            var burst = new LensFlareDataElementSRP
            {
                flareType = SRPLensFlareType.Image,
                lensFlareTexture = Rays(),
                preserveAspectRatio = true,
                localIntensity = 0.3f,
                tint = RayTint,
                uniformScale = 6f,
                position = 0f
            };
            var core = new LensFlareDataElementSRP
            {
                flareType = SRPLensFlareType.Circle,
                localIntensity = 0.6f,
                tint = RayTint,
                uniformScale = 1.5f,
                edgeOffset = 1f,
                fallOff = 0.55f,
                position = 0f
            };
            data.elements = new[] { halo, burst, core };
            return data;
        }

        /// <summary>
        /// The eye's own starburst, kept sparse: eight soft rays spread round the disc with some jitter, each of
        /// its own length and strength, fading toward the tip. Fixed seed, so the pattern belongs to the pilot
        /// and never shimmers.
        /// </summary>
        private static Texture2D Rays()
        {
            if (rays != null) return rays;
            const int Bins = 1024, Count = 8;
            var strength = new float[Bins];
            var length = new float[Bins];
            var random = new System.Random(7);
            for (int ray = 0; ray < Count; ray++)
            {
                int centre = ray * (Bins / Count) + random.Next(-Bins / 24, Bins / 24);
                float s = 0.35f + 0.65f * (float)random.NextDouble(), reach = 0.4f + 0.5f * (float)random.NextDouble();
                for (int k = -10; k <= 10; k++)
                {
                    int bin = (centre + k + Bins) % Bins;
                    float edge = s * Mathf.Exp(-k * k * 0.07f);
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
                    float angle = (Mathf.Atan2(dy, dx) / (2f * Mathf.PI) + 0.5f) * Bins;
                    int bin = (int)angle % Bins, next = (bin + 1) % Bins;
                    float blend = angle - Mathf.Floor(angle);
                    float reach = Mathf.Lerp(length[bin], length[next], blend);
                    float along = reach > 0f ? Mathf.Clamp01(1f - radius / reach) : 0f;
                    byte v = (byte)(255f * Mathf.Lerp(strength[bin], strength[next], blend) * along * along * Mathf.Sqrt(along));
                    pixels[y * RaySize + x] = new Color32(v, v, v, v);
                }
            rays = new Texture2D(RaySize, RaySize, TextureFormat.RGBA32, true, true)
            {
                name = "BoscaliSummer.SunGlare.Rays",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Trilinear,
                hideFlags = HideFlags.DontSave
            };
            rays.SetPixels32(pixels);
            rays.Apply(true, true);
            return rays;
        }
    }
}
