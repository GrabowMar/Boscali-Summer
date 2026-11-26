using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>Uses native contacts where the map draws them, and plots roster-visible drones without an icon.</summary>
    internal static class DroneMapTracks
    {
        private static readonly List<(Unit Track, Vector2 Screen, float Offset)> contacts = new List<(Unit, Vector2, float)>();
        private static readonly List<(TMP_Text Text, AvIcon Glyph)> markers = new List<(TMP_Text, AvIcon)>();
        private static RectTransform layer;

        internal static void Draw(DynamicMap map)
        {
            contacts.Clear();
            int shown = 0;
            if (ModuleServices.TryGet(out IDroneCommand drones) && map.mapImage != null && drones.Linkable > 0)
            {
                RectTransform viewport = map.mapBackground.rectTransform;
                RectTransform sheet = MfdTerrainRelief.MapImageRect(map.mapImage);
                if (sheet != null)
                {
                    Camera camera = MapUiPointer.UiCamera(map.maximizedMapCanvas);
                    for (int i = 0; i < drones.Linkable; i++)
                    {
                        Unit track = drones.Track(i);
                        if (track == null || track.disabled) continue;
                        bool selected = i == drones.LinkedIndex;
                        Vector3 world;
                        bool native = false;
                        if (map.TryGetIcon(track, out UnitMapIcon icon) && icon != null && icon.iconImage != null &&
                            icon.iconImage.sprite != null && icon.iconImage.isActiveAndEnabled)
                        {
                            world = icon.iconImage.transform.position;
                            native = true;
                        }
                        else
                        {
                            if (!selected && icon != null && MfdTerrainRelief.ClusterHidden(icon.iconImage)) continue;
                            GlobalPosition point = track.GlobalPosition();
                            Vector2 sheetPoint = new Vector2(point.x, point.z) * map.mapDisplayFactor;
                            if (MfdTerrainRelief.IsDrawing && !MfdTerrainRelief.TryProjectAirTrack(point, out sheetPoint))
                                continue;
                            world = sheet.TransformPoint(sheetPoint);
                        }

                        Rect bounds = viewport.rect;
                        Vector2 at = viewport.InverseTransformPoint(world);
                        if (!bounds.Contains(at) || bounds.height <= 0f) continue;
                        contacts.Add((track, RectTransformUtility.WorldToScreenPoint(camera, world),
                            Vector2.Distance(at, bounds.center) / bounds.height));
                        if (!native || selected) Mark(viewport, shown++, world, native ? AvIcon.Focus2 : AvIcon.Drone, selected);
                    }
                }
            }
            for (int i = shown; i < markers.Count; i++)
                if (markers[i].Text != null) markers[i].Text.gameObject.SetActive(false);
        }

        private static void Mark(RectTransform viewport, int index, Vector3 world, AvIcon glyph, bool selected)
        {
            if (layer == null || layer.parent != viewport)
            {
                if (layer != null) Object.Destroy(layer.gameObject);
                markers.Clear();
                layer = AvLay.Child(viewport, "NOAvionics.DroneTracks");
                AvLay.Fill(layer);
            }
            float size = glyph == AvIcon.Focus2 ? 32f : 20f;
            TMP_Text marker;
            if (index == markers.Count)
            {
                marker = AvIcons.Make(layer, glyph, size, AvTheme.Friendly);
                marker.rectTransform.sizeDelta = new Vector2(34f, 34f);
                markers.Add((marker, glyph));
            }
            else
            {
                marker = markers[index].Text;
                if (markers[index].Glyph != glyph)
                {
                    AvIcons.Set(marker, glyph, size);
                    markers[index] = (marker, glyph);
                }
            }
            marker.color = selected ? AvStyleHost.FuiColor("select", AvTheme.Accent)
                : AvStyleHost.FuiColor("friendly", AvTheme.Friendly);
            marker.rectTransform.position = world;
            marker.gameObject.SetActive(true);
        }

        internal static Unit At(Vector2 point, float radius, out float distance)
        {
            distance = radius * radius;
            Unit found = null;
            foreach (var contact in contacts)
            {
                float next = (contact.Screen - point).sqrMagnitude;
                if (next >= distance) continue;
                distance = next;
                found = contact.Track;
            }
            return found;
        }

#if !PUBLIC_RELEASE
        internal static float CentreOffset(Unit track)
        {
            foreach (var contact in contacts)
                if (contact.Track == track) return contact.Offset;
            return -1f;
        }
#endif

        internal static void Hide()
        {
            contacts.Clear();
            foreach (var marker in markers)
                if (marker.Text != null) marker.Text.gameObject.SetActive(false);
        }

        internal static void Reset()
        {
            if (layer != null) Object.Destroy(layer.gameObject);
            layer = null;
            markers.Clear();
            contacts.Clear();
        }
    }
}
