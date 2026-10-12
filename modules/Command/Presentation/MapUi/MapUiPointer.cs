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
            if (MissionContractWindow.BlocksMap) return true;
            if (MfdPanelDock.ContainsScreenPoint(screenPoint)) return true;
            if (MfdLogPanel.ContainsScreenPoint(screenPoint)) return true;
            if (OverFloating(screenPoint)) return true;
            if (MfdMapOrbitControls.Contains(screenPoint)) return true;
            if (MfdMapInteractions.ContainsMenu(screenPoint)) return true;
            return MfdRail.TryGetRail(out RectTransform rail) && Contains(rail, screenPoint);
        }

        // Floating map chrome (the STR card, the layer plate, the legend strip) registers its rect once, so wheel, drag and
        // clicks over it stay off the map behind it. At most Capacity rects; a destroyed one is dropped on the way.
        private const int Capacity = 8;
        private static readonly RectTransform[] floating = new RectTransform[Capacity];
        private static int floatingCount;

        internal static void Register(RectTransform rect)
        {
            if (rect == null) return;
            for (int i = 0; i < floatingCount; i++) if (ReferenceEquals(floating[i], rect)) return;
            if (floatingCount < Capacity) floating[floatingCount++] = rect;
        }

        internal static void Unregister(RectTransform rect)
        {
            for (int i = 0; i < floatingCount; i++)
            {
                if (!ReferenceEquals(floating[i], rect)) continue;
                RemoveAt(i);
                return;
            }
        }

        /// <summary>Scene reset: forget every registered rect.</summary>
        internal static void ResetFloating()
        {
            for (int i = 0; i < floatingCount; i++) floating[i] = null;
            floatingCount = 0;
        }

        private static bool OverFloating(Vector2 screenPoint)
        {
            for (int i = floatingCount - 1; i >= 0; i--)
            {
                RectTransform rect = floating[i];
                if (rect == null) { RemoveAt(i); continue; }           // destroyed: Unity's null check
                if (rect.gameObject.activeInHierarchy && Contains(rect, screenPoint)) return true;
            }
            return false;
        }

        private static void RemoveAt(int index)
        {
            floating[index] = floating[--floatingCount];
            floating[floatingCount] = null;
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
