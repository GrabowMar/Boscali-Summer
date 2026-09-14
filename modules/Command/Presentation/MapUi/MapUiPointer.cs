using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Screen-space hit test for the mod's map controls: the instrument column the panels
    /// dock into and the control rail.
    ///
    /// <para>DynamicMap reads the mouse through Rewired and raw <c>Input</c> calls rather
    /// than through uGUI, so a panel cannot consume wheel or drag input. The map patches
    /// consult this to keep a mouse gesture that belongs to a panel from also zooming,
    /// panning or placing a map order behind it.</para>
    /// </summary>
    internal static class MapUiPointer
    {
        public static bool OverControls() => OverControls(Input.mousePosition);

        public static bool OverControls(Vector2 screenPoint)
        {
            if (MfdPanelDock.ContainsScreenPoint(screenPoint)) return true;
            return MfdRail.TryGetRail(out RectTransform rail) && Contains(rail, screenPoint);
        }

        public static bool Contains(RectTransform rect, Vector2 screenPoint)
        {
            if (rect == null) return false;

            var canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, camera);
        }
    }
}
