using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Client-only relief view buttons and readout, anchored to the fixed map viewport.
    /// Mouse navigation lives in <see cref="ReliefNavigator"/>.
    /// </summary>
    internal static class MfdMapOrbitControls
    {
        private const float RootWidth = 396f;
        private static RectTransform root;
        private static TMP_Text readout;
        private static AvFooter cursorLine;
        private static AvControl followButton;
        private static AvControl flyButton;
        private static int lastRevision = -1;
        private static bool lastFollowing;
        private static bool lastFlying;
        private static float nextCursorRead;

        internal static bool Contains(Vector2 screenPoint) => MapUiPointer.Contains(root, screenPoint);

        internal static void Tick(DynamicMap map)
        {
            if (map?.mapBackground == null || !MfdTerrainRelief.IsDrawing) return;
            RectTransform viewport = map.mapBackground.rectTransform;
            if (root == null || root.parent != viewport) Build(viewport);
            if (root == null) return;
            if (root.GetSiblingIndex() != root.parent.childCount - 1) root.SetAsLastSibling();

            bool following = ReliefNavigator.Following;
            bool flying = ReliefNavigator.Flying;
            if (lastRevision != MfdTerrainRelief.ViewRevision || following != lastFollowing ||
                flying != lastFlying)
            {
                lastRevision = MfdTerrainRelief.ViewRevision;
                lastFollowing = following;
                lastFlying = flying;
                if (readout != null)
                    readout.text = "3D " + Heading(Mathf.RoundToInt(MfdTerrainRelief.Yaw)) + "°/" +
                        MfdChromeLay.TwoDigits(Mathf.RoundToInt(MfdTerrainRelief.Pitch)) + "°  ×" +
                        AvNum.Fixed(MfdTerrainRelief.Zoom, 1);
                if (followButton != null)
                {
                    followButton.Label = following ? "FOLLOW" : "FREE PAN";
                    followButton.Latched = following;
                }
                if (flyButton != null)
                {
                    flyButton.Label = flying ? "WASD ON" : "WASD OFF";
                    flyButton.Latched = flying;
                }
            }
            if (cursorLine == null || Time.unscaledTime < nextCursorRead) return;
            nextCursorRead = Time.unscaledTime + .1f;
            string cursor;
            if (MfdMapInteractions.Feedback != null)
                cursor = MfdMapInteractions.Feedback;
            else if (MfdTerrainRelief.HoveredStackCount >= 3)
                cursor = MfdTerrainRelief.HoveredStackCount + " TRACKS  /  RIGHT CLICK TO SELECT STACK";
            else if (MfdTerrainRelief.TryInspectCursor(out GlobalPosition point, out float elevation))
                cursor = "X " + AvNum.Signed(point.x / 1000f, 1) + "  Z " + AvNum.Signed(point.z / 1000f, 1) +
                    " km  ·  TERRAIN ~" + AvNum.Fixed(elevation, 0) + " m";
            else cursor = "SHIFT CLICK TO ADD  /  CTRL DRAG TO BOX  /  RMB ACTIONS";
            cursorLine.Set(cursor);
        }

        /// <summary>Yaw as a signed three-digit field (+045 / -045 / 000), digits written by hand like AvNum.</summary>
        private static string Heading(int degrees)
        {
            string digits = AvNum.Fixed(Mathf.Abs(degrees), 0).PadLeft(3, '0');
            return degrees > 0 ? "+" + digits : degrees < 0 ? "-" + digits : digits;
        }

        private static void Build(RectTransform viewport)
        {
            Restore();
            var go = new GameObject("NOAvionics.MapOrbit", typeof(RectTransform));
            root = go.GetComponent<RectTransform>();
            root.SetParent(viewport, false);
            root.anchorMin = root.anchorMax = new Vector2(1f, 1f);
            root.pivot = new Vector2(1f, 1f);
            root.anchoredPosition = new Vector2(-12f, -8f);
            root.sizeDelta = new Vector2(RootWidth, 78f);
            MfdChromeLay.Panel(root, "Back", new Rect(0f, 0f, RootWidth, 78f),
                AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(.92f),
                AvStyleHost.FuiColor("frame", AvTheme.Frame), AvChamfer.Diagonal(5f));
            cursorLine = new AvFooter(root);
            root.gameObject.AddComponent<AvHelpScope>().Footer = cursorLine;
            cursorLine.Place(new AvSlot(5f, 61f, RootWidth - 10f, 15f));
            readout = AvText.Make(root, "Readout", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineLeft);
            MfdChromeLay.Place(readout.rectTransform, new Rect(8f, -3f, 130f, 27f));
            readout.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            AvText.Fit(readout, false);
            float x = 139f;
            Button("-15°", 57f, -3f, () => ReliefNavigator.Turn(-15f),
                "Turn the view 15 degrees left. Right or middle drag orbits freely.", ref x);
            Button("+15°", 57f, -3f, () => ReliefNavigator.Turn(15f),
                "Turn the view 15 degrees right. Right or middle drag orbits freely.", ref x);
            Button("NORTH", 65f, -3f, ReliefNavigator.North,
                "North up at the default terrain tilt.", ref x);
            Button("FIT", 60f, -3f, ReliefNavigator.Fit,
                "Fit the whole mission map. Double-click the ground to zoom in there.", ref x);
            x = 6f;
            Button("TILT -10°", 82f, -32f, () => ReliefNavigator.Tilt(-10f),
                "Lower the viewing angle by 10 degrees.", ref x);
            Button("TILT +10°", 82f, -32f, () => ReliefNavigator.Tilt(10f),
                "Raise the viewing angle by 10 degrees.", ref x);
            followButton = Button("FREE PAN", 103f, -32f, ReliefNavigator.ToggleFollow,
                "Follow your aircraft, or drag the map to move freely.", ref x);
            flyButton = Button("WASD OFF", 103f, -32f, ReliefNavigator.ToggleFly,
                "Toggle keyboard camera flight. W/A/S/D moves relative to view heading while the map is open.", ref x);
            lastRevision = -1;
            nextCursorRead = 0f;
        }

        private static AvControl Button(string label, float width, float y,
            System.Action onClick, string help, ref float x)
        {
            AvControl control = AvControl.Make(root, new AvControl.Spec(label, onClick));
            MfdChromeLay.Place(control.Rect, new Rect(x, y, width, 27f));
            control.Help = help;
            x += width + 4f;
            return control;
        }

        internal static void Restore()
        {
            readout = null;
            cursorLine = null;
            followButton = null;
            flyButton = null;
            lastRevision = -1;
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }
    }
}
