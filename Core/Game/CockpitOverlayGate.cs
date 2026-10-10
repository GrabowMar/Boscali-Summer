using UnityEngine;

namespace BoscaliSummer.Core.Game
{
    /// <summary>
    /// The one gate for the mod's own screen-space cockpit overlays (contract markers, ping markers): they draw on canvases of
    /// their own, so unlike the status board they do not follow the native HUD's visibility by themselves. They show only
    /// where the native flight HUD is up, which is exactly where the player expects HUD marks: not in the maximized map,
    /// not paused, not in cinematic mode, not in a batch run, and not while the HUD is hidden or the camera is somewhere
    /// the HUD is not shown (a free camera, another unit, an orbit view the Hud module has not forced open).
    /// </summary>
    internal static class CockpitOverlayGate
    {
        public static bool Open() =>
            !Application.isBatchMode && !DynamicMap.mapMaximized && !GameplayUI.GameIsPaused && !PlayerSettings.cinematicMode && HudUp();

        /// <summary>The native HUD's icon layer is live: the same test the board uses to find its dock.</summary>
        private static bool HudUp()
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            return hud != null && hud.iconLayer != null && hud.iconLayer.gameObject.activeInHierarchy;
        }
    }
}
