using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The visor's power-on (VisorBootHud) brings the board and the left column up one after another: only the mod's own groups are touched, by alpha (the game's HUD is never dimmed, moved or switched off).
    internal sealed partial class StatusPanel
    {
        private CanvasGroup routeGroup, cornerGroup;
        private bool bootDimmed;

        /// <summary>
        /// The board's and the left column's visibility during the power-on, 0..1 each (the board: its root; the column: DL EVT LOG, the cue stack, the route slot and the corner furniture). Called every frame while a boot
        /// runs and once with (1, 1) when it ends or is cut, which snaps everything back; with no boot it is never called, so a steady frame does nothing here.
        /// </summary>
        public void ApplyBoot(float board, float left)
        {
            if (rootGroup != null && rootGroup.alpha != board) rootGroup.alpha = board;
            if (logGroup != null && logGroup.alpha != left) logGroup.alpha = left;
            if (cueGroup != null && cueGroup.alpha != left) cueGroup.alpha = left;
            if (routeGroup != null && routeGroup.alpha != left) routeGroup.alpha = left;
            if (cornerGroup != null && cornerGroup.alpha != left) cornerGroup.alpha = left;
            bootDimmed = board < 1f || left < 1f;
        }

        /// <summary>A power-on left something dimmed (the groups are created while it runs too): the owner asks, to give it back once.</summary>
        public bool BootDimmed => bootDimmed;
    }
}
