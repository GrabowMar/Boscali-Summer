using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static class MfdPresentation
    {
        private sealed class SurfaceState
        {
            public RectTransform Surface;
            public Vector2 Min, Max, Pivot, Position, Size, RenderSize;
            public Vector3 Scale;
            public void Restore()
            {
                if (Surface == null) return;
                Surface.anchorMin = Min; Surface.anchorMax = Max; Surface.pivot = Pivot;
                Surface.sizeDelta = Size; Surface.anchoredPosition = Position; Surface.localScale = Scale;
            }
        }
        private static readonly Dictionary<MFDScreen, SurfaceState> surfaces = new Dictionary<MFDScreen, SurfaceState>();
        public static bool Expanded => MapUiAccess.MfdAvailable &&
            Plugin.Settings != null && Plugin.Settings.Command.ExpandedMapUi.Value;
        public static void Capture(VirtualMFD mfd)
        {
            if (mfd == null) return;
            Capture(MapUiAccess.GetLeftScreens(mfd));
            Capture(MapUiAccess.GetRightScreens(mfd));
        }
        private static void Capture(List<MFDScreen> screens)
        {
            if (screens == null) return;
            foreach (var screen in screens)
            {
                if (screen == null || screen.displayPanel == null || surfaces.ContainsKey(screen)) continue;
                var rt = screen.displayPanel.transform as RectTransform;
                if (rt == null || rt.name != "Content") continue;
                surfaces.Add(screen, new SurfaceState { Surface = rt, Min = rt.anchorMin, Max = rt.anchorMax,
                    Pivot = rt.pivot, Position = rt.anchoredPosition, Size = rt.sizeDelta,
                    RenderSize = rt.rect.size, Scale = rt.localScale });
            }
        }
        public static void Apply(MFDScreen screen)
        {
            if (screen == null || !MfdPanelDock.IsDocked(screen) || !surfaces.TryGetValue(screen, out var state)) return;
            var surface = state.Surface;
            if (surface == null) return;
            surface.anchorMin = surface.anchorMax = surface.pivot = new Vector2(0f, 1f);
            surface.sizeDelta = state.RenderSize;
            surface.anchoredPosition = Vector2.zero;
            surface.localScale = Vector3.one;
        }
        public static void Restore()
        {
            foreach (var state in surfaces.Values) state.Restore();
            surfaces.Clear();
        }
    }
}
