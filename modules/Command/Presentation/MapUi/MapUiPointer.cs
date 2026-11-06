using BoscaliSummer.Core.Ui;
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
            // A full-screen window (OPS front, Met Office, UAV terminal, cinematic console) or the radial menu takes the game's own
            // mouse while it is up: the map is under it everywhere, so no map click, pick or card goes through it.
            if (WindowHoldsMouse()) return true;
            if (MfdPanelDock.ContainsScreenPoint(screenPoint)) return true;
            if (MfdLogPanel.ContainsScreenPoint(screenPoint)) return true;
            if (MapControlRects.Contains(screenPoint)) return true;
            if (MapSelectionDeck.Contains(screenPoint)) return true;
            if (MfdMapOrbitControls.Contains(screenPoint)) return true;
            if (MfdMapInteractions.ContainsMenu(screenPoint)) return true;
            // STR tags are many and pooled, so they answer for themselves instead of registering.
            if (StrMapOverlay.Active != null && StrMapOverlay.Active.ContainsTag(screenPoint)) return true;
            return MfdRail.TryGetRail(out RectTransform rail) && Contains(rail, screenPoint);
        }

        // Floating map chrome (the STR card, the layer plate, the legend strip) registers its rect once, in Core's
        // MapControlRects so other modules' chrome (the event dispatch alert) can too.
        internal static void Register(RectTransform rect) => MapControlRects.Register(rect);

        internal static void Unregister(RectTransform rect) => MapControlRects.Unregister(rect);

        /// <summary>Scene reset: forget every registered rect.</summary>
        internal static void ResetFloating() => MapControlRects.Reset();

        /// <summary>The Rewired mouse is off: every big window and the radial menu turn it off while open, and the map cannot be
        /// worked without it anyway.</summary>
        internal static bool WindowHoldsMouse()
        {
            if (!Rewired.ReInput.isReady || Rewired.ReInput.controllers == null) return false;
            Rewired.Mouse mouse = Rewired.ReInput.controllers.Mouse;
            return mouse != null && !mouse.enabled;
        }

        /// <summary>The camera that projects screen points on this canvas (none for an overlay canvas).</summary>
        public static Camera UiCamera(Canvas canvas) =>
            canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;

        public static bool Contains(RectTransform rect, Vector2 screenPoint)
        {
            if (rect == null) return false;

            Canvas canvas = CanvasFor(rect);
            Camera camera = UiCamera(canvas);
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
