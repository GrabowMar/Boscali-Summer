using System;
using BoscaliSummer.Modules.Command.Domain;
using HarmonyLib;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Centres the open map on a ground point, on the relief or on the flat sheet. Command-internal: no other module needs it.
    /// The game's own SetMapTarget is no fix for the flat sheet: it lerps the image position only, and the next MapControls frame
    /// rebuilds that position from the private offsets, so the sheet snaps back. This writes the offsets instead (the pattern of
    /// Wing's map fit), with no Harmony patch.
    /// </summary>
    internal static class MapFocus
    {
        private static AccessTools.FieldRef<DynamicMap, Vector2> positionOffset, stationaryOffset;
        private static bool resolved, usable, warned;

        /// <summary>Relief: ReliefNavigator.Focus (it eases over the next frames). Flat: raise the zoom, then write positionOffset. False when the map is closed or the point is not finite.</summary>
        internal static bool To(float worldX, float worldZ, bool zoomIn)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || !DynamicMap.mapMaximized || !float.IsFinite(worldX) || !float.IsFinite(worldZ)) return false;
            if (MfdTerrainRelief.IsDrawing)
            {
                ReliefNavigator.Focus(new GlobalPosition(worldX, 0f, worldZ), zoomIn);
                return true;
            }
            return Flat(map, worldX, worldZ, zoomIn);
        }

        /// <summary>Where the point is on screen as a fraction of the viewport height from its centre; -1 when unknown. For the sim read-back and the harness.</summary>
        internal static float CentreOffset(float worldX, float worldZ)
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || map.mapImage == null || map.mapBackground == null || !float.IsFinite(worldX) || !float.IsFinite(worldZ)) return -1f;
            Vector2 local;
            if (MfdTerrainRelief.IsDrawing)
            {
                RectTransform image = MfdTerrainRelief.MapImageRect(map.mapImage);
                if (image == null || !MfdTerrainRelief.TryProject(worldX, worldZ, image.rect, out local)) return -1f;
            }
            else local = new Vector2(worldX, worldZ) * map.mapDisplayFactor;
            RectTransform viewport = map.mapBackground.rectTransform;
            Rect bounds = viewport.rect;
            if (!(bounds.height > 0f)) return -1f;
            Vector3 at = viewport.InverseTransformPoint(map.mapImage.transform.TransformPoint(local));
            return Vector2.Distance(new Vector2(at.x, at.y), bounds.center) / bounds.height;
        }

        private static bool Flat(DynamicMap map, float worldX, float worldZ, bool zoomIn)
        {
            if (!Resolve()) return false;
            try
            {
                float current = map.GetZoomLevel();
                float zoom = FlatFocus.Zoom(current, zoomIn);
                if (zoom != current) map.SetZoomLevel(zoom);
                Vector2 stationary = stationaryOffset(map);
                FlatFocus.Offset(worldX, worldZ, map.mapDisplayFactor, stationary.x, stationary.y, out float x, out float z);
                positionOffset(map) = new Vector2(x, z);
                return true;
            }
            catch (Exception error)
            {
                Warn("could not move the flat map (" + error.Message + ")");
                return false;
            }
        }

        private static bool Resolve()
        {
            if (resolved) return usable;
            resolved = true;
            try
            {
                positionOffset = AccessTools.FieldRefAccess<DynamicMap, Vector2>("positionOffset");
                stationaryOffset = AccessTools.FieldRefAccess<DynamicMap, Vector2>("stationaryOffset");
                usable = true;
            }
            catch (Exception error)
            {
                usable = false;
                Warn("flat map offsets not found (" + error.Message + "): locate does nothing on the flat sheet");
            }
            return usable;
        }

        private static void Warn(string message)
        {
            if (warned) return;
            warned = true;
            Plugin.Logger?.LogWarning("[STR] " + message + ".");
        }
    }
}
