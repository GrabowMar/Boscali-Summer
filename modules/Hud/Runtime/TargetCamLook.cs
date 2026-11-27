using BoscaliSummer.Core.Fx;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// The targeting-pod look (remade 2026-11-27): keeps vanilla's lightweight <c>ColorAdjustments</c> (auto-exposure and IR desaturation)
    /// and disables any heavy multi-pass post effects (<c>FilmGrain</c>, <c>Vignette</c>, <c>Bloom</c>, <c>ChromaticAberration</c>) on
    /// <c>TargetCam.screenVolume</c>; all lens/sensor effects run in one UI shader pass (<c>NOA/UI/CamFeed</c>) on the HUD card.
    /// Client-local, idempotent per target camera.
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
            if (profile == null) return;
            if (profile.TryGet(out FilmGrain grain)) grain.active = false;
            if (profile.TryGet(out Vignette vig)) vig.active = false;
            if (profile.TryGet(out Bloom bloom)) bloom.active = false;
            if (profile.TryGet(out ChromaticAberration ca)) ca.active = false;
        }

        public static bool IsIr(TargetCam cam) => cam != null && cam.UsingIR();

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
    }
}
