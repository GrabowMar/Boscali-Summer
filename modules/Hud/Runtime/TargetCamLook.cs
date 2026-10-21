using BoscaliSummer.Core.Fx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// The targeting-pod look (2026-10-14): grain, a lens vignette, a white-hot bloom and a hint of colour fringe added once to vanilla's own target-camera
    /// post volume (<c>TargetCam.screenVolume</c>, a per-instance profile only that camera sees), so the cockpit tac screen and the third-person TGT card both
    /// read as a sensor feed. Vanilla keeps its own colour adjustments (IR desaturation, exposure). Client-local, idempotent per target camera.
    /// </summary>
    internal static class TargetCamLook
    {
        private static readonly AccessTools.FieldRef<TargetCam, Volume> VolumeRef = Ref();
        private static TargetCam styled;

        private static AccessTools.FieldRef<TargetCam, Volume> Ref()
        {
            try { return AccessTools.FieldRefAccess<TargetCam, Volume>("screenVolume"); }
            catch (System.Exception) { return null; }
        }

        public static void Apply(TargetCam cam)
        {
            if (cam == null || ReferenceEquals(cam, styled) || VolumeRef == null) return;
            Volume volume = VolumeRef(cam);
            if (volume == null) return;      // not initialised yet (no authority, or before OnStartClient)
            styled = cam;
            VolumeProfile profile = volume.profile;
            Set(profile.TryGet(out FilmGrain grain) ? grain : profile.Add<FilmGrain>(true), g =>
            {
                // The game ships without URP's built-in grain textures: any preset lookup NREs in PostProcessPass.SetupGrain every frame and smears the screen.
                g.type.value = FilmGrainLookup.Custom;
                g.texture.value = FeedCamera.Noise();
                g.intensity.value = 0.55f;
                g.response.value = 0.55f;
            });
            Set(profile.TryGet(out Vignette vig) ? vig : profile.Add<Vignette>(true), v =>
            {
                v.intensity.value = 0.42f;
                v.smoothness.value = 0.35f;
            });
            Set(profile.TryGet(out Bloom bloom) ? bloom : profile.Add<Bloom>(true), b =>
            {
                b.threshold.value = 0.85f;
                b.intensity.value = 0.9f;
                b.scatter.value = 0.55f;
            });
            Set(profile.TryGet(out ChromaticAberration ca) ? ca : profile.Add<ChromaticAberration>(true), c => c.intensity.value = 0.3f);
        }

        private static readonly AccessTools.FieldRef<TargetCam, TargetScreenUI> ScreenUiRef = Field<TargetCam, TargetScreenUI>("targetScreenUI");
        private static readonly AccessTools.FieldRef<TargetScreenUI, Canvas> CanvasRef = Field<TargetScreenUI, Canvas>("displayCanvas");

        private static AccessTools.FieldRef<TOwner, T> Field<TOwner, T>(string name) where TOwner : class
        {
            try { return AccessTools.FieldRefAccess<TOwner, T>(name); }
            catch (System.Exception) { return null; }
        }

        /// <summary>
        /// Vanilla's target-screen text (name, RNG/ALT/SPD, GRID, MODE, Mag) is drawn by a UI camera into the same picture the third-person TGT card
        /// shows, under the card's own header: off while the card is up (outside the cockpit nobody sees the tac screen), back on otherwise.
        /// </summary>
        public static void SetOverlay(TargetCam cam, bool shown)
        {
            if (cam == null || ScreenUiRef == null || CanvasRef == null) return;
            TargetScreenUI ui = ScreenUiRef(cam);
            Canvas canvas = ui != null ? CanvasRef(ui) : null;
            if (canvas != null && canvas.enabled != shown) canvas.enabled = shown;
        }

        private static void Set<T>(T component, System.Action<T> write) where T : VolumeComponent
        {
            component.active = true;
            component.SetAllOverridesTo(true);
            write(component);
        }
    }
}
