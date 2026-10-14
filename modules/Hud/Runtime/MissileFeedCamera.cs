using System.Collections.Generic;
using BoscaliSummer.Core.Fx;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// The missile feed (2026-10-12): our own camera on the nose of the last own weapon in the air (<see cref="MissileTracker.Missiles"/> is launch order), drawn into a small
    /// texture the third-person cluster shows as a card. A copy of the main camera's settings without the cockpit, HUD and UI layers, post-processing or shadows; it draws
    /// <see cref="Fps"/> times a second by being enabled for one frame (the satellite imager's way). When the weapon ends the last picture is held <see cref="HoldSeconds"/>
    /// (<see cref="Lost"/>), then the feed goes, or falls back to an older weapon still flying. Ticked every frame by <c>HudBoard</c>, after the floating-origin shift. Client-local.
    /// </summary>
    internal sealed class MissileFeedCamera
    {
        // A seeker video link, not a camera: a coarse point-filtered picture at a choppy rate, graded by its own post stack (Grade).
        private const int Width = 160, Height = 90;
        private const float Fps = 15f, FieldOfView = 40f, HoldSeconds = 1.5f;

        private Camera cam;
        private VolumeStack stack;
        private RenderTexture picture;
        private Missile riding;
        private float nextFrame, lostAt = -1f;
        private int enabledFrame;
        private bool refused;

        /// <summary>The picture while there is a feed to show (live or held), else null.</summary>
        public Texture Texture { get; private set; }
        /// <summary>The ridden weapon's short name, kept through the hold.</summary>
        public string Name { get; private set; }
        /// <summary>The weapon ended; the last picture is being held.</summary>
        public bool Lost => lostAt >= 0f;

        /// <summary>Metres from the ridden weapon to what it guides on; NaN with no target or no weapon.</summary>
        public float RangeM
        {
            get
            {
                Unit target = riding != null && !riding.disabled ? ShotBook.TargetOf(riding) : null;
                return target != null ? Vector3.Distance(riding.transform.position, target.transform.position) : float.NaN;
            }
        }

        public void Tick(IReadOnlyList<Missile> live, bool allowed, float now)
        {
            if (!allowed)
            {
                if (cam != null) cam.enabled = false;
                riding = null;
                Texture = null;
                lostAt = -1f;
                return;
            }
            // A camera enabled for one frame renders once; switch it off again afterwards.
            if (cam != null && cam.enabled && Time.frameCount > enabledFrame) cam.enabled = false;

            Missile last = null;
            for (int i = live.Count - 1; last == null && i >= 0; i--)
                if (live[i] != null && !live[i].disabled) last = live[i];

            if (last == null)
            {
                riding = null;
                if (Texture == null) { lostAt = -1f; }
                else if (lostAt < 0f) lostAt = now;
                else if (now - lostAt > HoldSeconds) { Texture = null; lostAt = -1f; }
                return;
            }
            if (!Ensure()) { Texture = null; return; }
            if (!ReferenceEquals(last, riding))
            {
                riding = last;
                WeaponInfo info = null;
                try { info = last.GetWeaponInfo(); } catch { info = null; }
                Name = ShotBook.Short(info);
            }
            lostAt = -1f;
            Texture = picture;
            // Ahead of the nose, so the weapon's own body and plume stay behind the lens.
            Transform t = last.transform;
            cam.transform.SetPositionAndRotation(t.position + t.forward * (last.maxRadius + 0.5f), t.rotation);
            if (now < nextFrame) return;
            nextFrame = now + 1f / Fps;
            cam.enabled = true;
            enabledFrame = Time.frameCount;
        }

        private bool Ensure()
        {
            if (refused) return false;
            if (picture == null)
            {
                picture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { name = "BoscaliMissileFeed", filterMode = FilterMode.Point };
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
            cam = new GameObject("BoscaliMissileFeedCamera", typeof(Camera)).GetComponent<Camera>();
            cam.CopyFrom(main);
            cam.enabled = false;
            cam.targetTexture = picture;
            cam.ResetAspect();
            cam.fieldOfView = FieldOfView;
            cam.nearClipPlane = 0.5f;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            cam.depth = -10f;
            cam.cullingMask = main.cullingMask & ~((1 << PhysicsLayers.UI) | (1 << PhysicsLayers.HUD) | (1 << PhysicsLayers.Cockpit));
            UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
            if (data != null)
            {
                data.antialiasing = AntialiasingMode.None;
                data.renderShadows = false;
                // Our own volume stack, set by hand and never updated from the scene's volumes: the world's grade stays off this feed and this one off the world.
                stack = VolumeManager.instance.CreateStack();
                Grade(stack);
                cam.SetVolumeFrameworkUpdateMode(VolumeFrameworkUpdateMode.ViaScripting);      // before the stack: switching the mode builds the camera a stack of its own
                data.volumeStack = stack;
                data.renderPostProcessing = true;
            }
            return true;
        }

        /// <summary>The seeker look: black and white, hard contrast, white-hot bloom, heavy coarse grain and a dark lens rim.</summary>
        private static void Grade(VolumeStack s)
        {
            ColorAdjustments color = s.GetComponent<ColorAdjustments>();
            color.saturation.value = -100f;
            color.contrast.value = 25f;
            color.postExposure.value = -0.2f;
            Bloom bloom = s.GetComponent<Bloom>();
            bloom.threshold.value = 0.95f;
            bloom.intensity.value = 0.35f;
            bloom.scatter.value = 0.6f;
            FilmGrain grain = s.GetComponent<FilmGrain>();
            grain.type.value = FilmGrainLookup.Custom;      // built-in lookups are stripped from the game (NRE in SetupGrain)
            grain.texture.value = TargetCamLook.Noise();
            grain.intensity.value = 0.5f;
            grain.response.value = 0.4f;
            Vignette rim = s.GetComponent<Vignette>();
            rim.intensity.value = 0.38f;
            rim.smoothness.value = 0.4f;
            s.GetComponent<ChromaticAberration>().intensity.value = 0.25f;
        }

        /// <summary>Drop the camera and its picture (scene reset, board off). Idempotent.</summary>
        public void Release()
        {
            if (cam != null) Object.Destroy(cam.gameObject);
            cam = null;
            stack?.Dispose();
            stack = null;
            if (picture != null)
            {
                FxRtPool.Disown(picture);
                picture.Release();
                Object.Destroy(picture);
            }
            picture = null;
            riding = null;
            Texture = null;
            Name = null;
            lostAt = -1f;
            nextFrame = 0f;
            refused = false;
        }
    }
}
