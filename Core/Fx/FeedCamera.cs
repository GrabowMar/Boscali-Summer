using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Core.Fx
{
    /// <summary>How a <see cref="FeedCamera"/> picture is graded.</summary>
    internal enum FeedLook : byte
    {
        /// <summary>A seeker video link: black and white, hard contrast, white-hot bloom, heavy coarse grain, a dark lens rim.</summary>
        Seeker,
        /// <summary>A daylight sensor ball: muted colour, light grain, a soft rim.</summary>
        Eo,
        /// <summary>White-hot: black and white, lifted highlights, bloom and grain.</summary>
        WhiteHot,
    }

    /// <summary>
    /// A client-local video feed (lifted out of the HUD missile feed 2026-10-19, now shared with the VANGUARD drone link): our own
    /// camera copied from the main camera's settings without the cockpit, HUD and UI layers, post-processing of the world or
    /// shadows, drawn into a small texture <see cref="Fps"/> times a second by being enabled for one frame (the satellite imager's
    /// way) and graded by a volume stack of its own. Place it every frame after the floating-origin shift.
    /// </summary>
    internal sealed class FeedCamera
    {
        private readonly string name;
        private readonly int width, height;
        private readonly bool point;
        private FeedLook look;
        private Camera cam;
        private VolumeStack stack;
        private RenderTexture picture;
        private float nextFrame;
        private int enabledFrame;
        private bool refused;

        public float Fps = 15f, FieldOfView = 40f;

        // The game's own auto-exposure (ExposureController keeps the post volume's ColorAdjustments): the EO and IR feeds follow it,
        // because the world is lit in HDR and a fixed LDR feed clips every cloud deck and sky to white.
        private static readonly System.Reflection.FieldInfo GameExposure = AccessTools.Field(typeof(ExposureController), "colorAdjustments");
        private ColorAdjustments ours;
        private Bloom glow;

        private static float Threshold(FeedLook look) => look == FeedLook.WhiteHot ? 1.1f : 0.95f;

        // Every look is HDR + ACES now: the LDR seeker clipped a bright cloud deck to a white sheet (in game 2026-10-20).
        private bool Hdr => true;

        private static readonly System.Collections.Generic.HashSet<Camera> Lenses = new System.Collections.Generic.HashSet<Camera>();

        /// <summary>The camera is a feed lens: the weather's CloudProbePass marches the mod's clouds into it (they draw on the main camera only).</summary>
        internal static bool IsLens(Camera camera) => Lenses.Contains(camera);

        public FeedCamera(string name, int width, int height, FeedLook look, bool pointFilter)
        {
            this.name = name;
            this.width = width;
            this.height = height;
            this.look = look;
            point = pointFilter;
        }

        /// <summary>The picture; null until the first <see cref="Shoot"/> made one, or when the render-texture budget refused it.</summary>
        public Texture Texture => picture;

        public FeedLook Look
        {
            get => look;
            set
            {
                if (look == value) return;
                look = value;
                if (stack != null) Grade(stack, look);
                if (cam != null) cam.allowHDR = Hdr;
            }
        }

        /// <summary>Puts the lens at <paramref name="position"/> along <paramref name="rotation"/> and draws a frame when one is due. False when no camera could be made.</summary>
        public bool Shoot(Vector3 position, Quaternion rotation, float now)
        {
            Settle();
            if (!Ensure()) return false;
            cam.transform.SetPositionAndRotation(position, rotation);
            cam.fieldOfView = FieldOfView;
            if (now < nextFrame) return true;
            nextFrame = now + 1f / Mathf.Max(1f, Fps);
            if (Hdr && ours != null)
            {
                float exposure = Exposure() + (look == FeedLook.WhiteHot ? -1.0f : -0.2f);
                ours.postExposure.value = exposure;
                // URP tests the bloom threshold on the raw HDR frame, before exposure: in daylight (about -4 EV) the whole picture passed it and the feed went milky.
                if (glow != null) glow.threshold.value = Threshold(look) * Mathf.Pow(2f, -exposure);
            }
            cam.enabled = true;
            enabledFrame = Time.frameCount;
            return true;
        }

        /// <summary>Where a world point (local space) falls in the picture, 0..1 from the bottom left; false when behind the lens or no camera yet.</summary>
        public bool Project(Vector3 world, out Vector2 viewport)
        {
            viewport = default;
            if (cam == null) return false;
            Vector3 p = cam.WorldToViewportPoint(world);
            viewport = new Vector2(p.x, p.y);
            return p.z > 0f;
        }

        /// <summary>No picture wanted this frame: the camera stays off and the last picture is kept.</summary>
        public void Idle()
        {
            Settle();
            if (cam != null) cam.enabled = false;
        }

        // A camera enabled for one frame renders once; switch it off again afterwards.
        private void Settle()
        {
            if (cam != null && cam.enabled && Time.frameCount > enabledFrame) cam.enabled = false;
        }

        private bool Ensure()
        {
            if (refused) return false;
            if (picture == null)
            {
                picture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = name, filterMode = point ? FilterMode.Point : FilterMode.Bilinear };
                picture.Create();
                if (!FxRtPool.Own(picture))
                {
                    picture.Release();
                    Object.Destroy(picture);
                    picture = null;
                    refused = true;      // over the mod's render-texture budget: no feed this scene
                    return false;
                }
            }
            if (cam != null) return true;
            CameraStateManager state = SceneSingleton<CameraStateManager>.i;
            Camera main = state != null ? state.mainCamera : null;
            if (main == null) return false;
            cam = new GameObject(name + "Camera", typeof(Camera)).GetComponent<Camera>();
            cam.CopyFrom(main);
            cam.enabled = false;
            Lenses.Add(cam);
            cam.targetTexture = picture;
            cam.ResetAspect();
            cam.fieldOfView = FieldOfView;
            cam.nearClipPlane = 0.5f;
            cam.allowHDR = Hdr;
            cam.allowMSAA = false;
            cam.depth = -10f;
            cam.cullingMask = main.cullingMask & ~((1 << PhysicsLayers.UI) | (1 << PhysicsLayers.HUD) | (1 << PhysicsLayers.Cockpit));
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = false;
                // The weather's cloud pass marches up to this lens's own depth (ZTest Always); without it the march ran through the ground and an overcast filled the feed white.
                data.requiresDepthTexture = true;
                // Our own volume stack, set by hand and never updated from the scene's volumes: the world's grade stays off this feed and this one off the world.
                stack = VolumeManager.instance.CreateStack();
                Grade(stack, look);
                ours = stack.GetComponent<ColorAdjustments>();
                glow = stack.GetComponent<Bloom>();
                cam.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);      // before the stack: switching the mode builds the camera a stack of its own
                data.volumeStack = stack;
                data.renderPostProcessing = true;
            }
            return true;
        }

        private static void Grade(VolumeStack s, FeedLook look)
        {
            bool mono = look != FeedLook.Eo;
            ColorAdjustments color = s.GetComponent<ColorAdjustments>();
            color.saturation.value = mono ? -100f : -35f;
            color.contrast.value = look == FeedLook.Seeker ? 15f : look == FeedLook.WhiteHot ? 18f : 12f;
            color.postExposure.value = look == FeedLook.Seeker ? -0.2f : look == FeedLook.WhiteHot ? 0.15f : 0f;
            s.GetComponent<Tonemapping>().mode.value = TonemappingMode.ACES;
            Bloom bloom = s.GetComponent<Bloom>();
            bloom.threshold.value = Threshold(look);
            bloom.intensity.value = look == FeedLook.WhiteHot ? 0.25f : 0.1f;
            bloom.scatter.value = 0.6f;
            FilmGrain grain = s.GetComponent<FilmGrain>();
            grain.type.value = FilmGrainLookup.Custom;      // built-in lookups are stripped from the game (NRE in SetupGrain)
            grain.texture.value = Noise();
            grain.intensity.value = look == FeedLook.Eo ? 0.3f : look == FeedLook.WhiteHot ? 0.45f : 0.3f;
            grain.response.value = 0.4f;
            Vignette rim = s.GetComponent<Vignette>();
            rim.intensity.value = look == FeedLook.Eo ? 0.28f : look == FeedLook.WhiteHot ? 0.38f : 0.25f;
            rim.smoothness.value = 0.4f;
            s.GetComponent<ChromaticAberration>().intensity.value = look == FeedLook.WhiteHot ? 0.25f : 0.1f;
        }

        /// <summary>Drops the camera and its picture (scene reset, feed off). Idempotent.</summary>
        public void Release()
        {
            Lenses.Remove(cam);      // also once a scene change has already destroyed it
            if (cam != null) Object.Destroy(cam.gameObject);
            cam = null;
            // URP leaves the last rendered camera's stack as the current one and reads it before the next base camera resets it
            // (CheckPostProcessForDepth for overlay stacks, e.g. the target camera). Hand the current stack back before dropping our reference.
            if (stack != null && ReferenceEquals(VolumeManager.instance.stack, stack)) VolumeManager.instance.ResetMainStack();
            // URP owns this stack once assigned to the camera: OnDestroy caches it for the next camera, even if the scene already
            // destroyed our lens. Never dispose it here: the next TargetCam would reuse a dead stack after restart (issue #22).
            // VolumeManager.Update resets the cached stack's grade before reuse. Object.Destroy above is deferred.
            stack = null;
            ours = null;
            glow = null;
            if (picture != null)
            {
                FxRtPool.Disown(picture);
                picture.Release();
                Object.Destroy(picture);
            }
            picture = null;
            nextFrame = 0f;
            refused = false;
        }

        private static float Exposure()
        {
            try
            {
                var game = GameExposure?.GetValue(null) as ColorAdjustments;
                return game != null ? game.postExposure.value : 0f;
            }
            catch (System.Exception) { return 0f; }
        }

        private static Texture2D noise;

        /// <summary>A 128 px grey noise tile, built once: the Custom grain lookup of every feed and of the targeting-pod look.</summary>
        internal static Texture2D Noise()
        {
            if (noise != null) return noise;
            const int size = 128;
            noise = new Texture2D(size, size, TextureFormat.R8, false, true) { name = "BoscaliSummer.Grain", wrapMode = TextureWrapMode.Repeat };
            var bytes = new byte[size * size];
            var rng = new System.Random(1);
            rng.NextBytes(bytes);
            noise.LoadRawTextureData(bytes);
            noise.Apply(false, true);
            return noise;
        }
    }
}
