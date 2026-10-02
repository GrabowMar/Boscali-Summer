using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
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
            if (StrPlanningWindow.BlocksMap || MissionContractWindow.BlocksMap) return true;
            if (MfdPanelDock.ContainsScreenPoint(screenPoint)) return true;
            if (MfdLogPanel.ContainsScreenPoint(screenPoint)) return true;
            if (MfdMapOrbitControls.Contains(screenPoint)) return true;
            if (MfdMapInteractions.ContainsMenu(screenPoint)) return true;
            return MfdRail.TryGetRail(out RectTransform rail) && Contains(rail, screenPoint);
        }

        public static bool Contains(RectTransform rect, Vector2 screenPoint)
        {
            if (rect == null) return false;

            Canvas canvas = CanvasFor(rect);
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, camera);
        }

        // Hit tests cluster on one rect during a gesture, so one cached canvas answers
        // almost every call. A destroyed canvas fails the null check and re-resolves;
        // a destroyed rect never reaches the lookup.
        private static RectTransform cachedRect;
        private static Canvas cachedCanvas;

        private static Canvas CanvasFor(RectTransform rect)
        {
            if (cachedCanvas != null && ReferenceEquals(cachedRect, rect)) return cachedCanvas;
            cachedRect = rect;
            cachedCanvas = rect.GetComponentInParent<Canvas>();
            return cachedCanvas;
        }
    }
}
