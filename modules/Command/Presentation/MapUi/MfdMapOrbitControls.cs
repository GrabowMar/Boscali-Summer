using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    /// <summary>
    /// Client-only relief view buttons and readout, anchored to the fixed map viewport.
    /// Mouse navigation lives in <see cref="ReliefNavigator"/>.
    /// </summary>
    internal static class MfdMapOrbitControls
    {
        private const float RootWidth = 520f;
        private static RectTransform root;
        private static TMP_Text readout;
        private static AvFooter cursorLine;
        private static AvControl followButton;
        private static int lastRevision = -1;
        private static bool lastFollowing;
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
            if (lastRevision != MfdTerrainRelief.ViewRevision || following != lastFollowing)
            {
                lastRevision = MfdTerrainRelief.ViewRevision;
                lastFollowing = following;
                if (readout != null)
                    readout.text = "3D " + Heading(Mathf.RoundToInt(MfdTerrainRelief.Yaw)) + "°/" +
                        MfdChromeLay.TwoDigits(Mathf.RoundToInt(MfdTerrainRelief.Pitch)) + "°  ×" +
                        AvNum.Fixed(MfdTerrainRelief.Zoom, 1);
                if (followButton != null)
                {
                    followButton.Label = following ? "FOLLOW" : "FREE";
                    followButton.Latched = following;
                }
            }
            if (cursorLine == null || Time.unscaledTime < nextCursorRead) return;
            nextCursorRead = Time.unscaledTime + .1f;
            string cursor;
            if (MfdMapInteractions.Feedback != null)
                cursor = MfdMapInteractions.Feedback;
            else if (MfdTerrainRelief.HoveredStackCount >= 5)
                cursor = MfdTerrainRelief.HoveredStackCount + " TRACKS  /  ZOOM TO RESOLVE";
            else if (MfdTerrainRelief.TryInspectCursor(out GlobalPosition point, out float elevation))
                cursor = "X " + AvNum.Signed(point.x / 1000f, 1) + "  Z " + AvNum.Signed(point.z / 1000f, 1) +
                    " km  ·  TERRAIN ~" + AvNum.Fixed(elevation, 0) + " m";
            else cursor = "";
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
            root.sizeDelta = new Vector2(RootWidth, 52f);
            cursorLine = new AvFooter(root);
            root.gameObject.AddComponent<AvHelpScope>().Footer = cursorLine;
            cursorLine.Place(new AvSlot(0f, 30f, RootWidth, 22f));
            readout = AvText.Make(root, "Readout", AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
            MfdChromeLay.Place(readout.rectTransform, new Rect(0f, 0f, 150f, 26f));
            readout.color = AvStyleHost.FuiColor("key", AvTheme.RailInfo);
            AvText.Fit(readout, false);
            float x = 152f;
            Button("-15°", 44f, () => ReliefNavigator.Turn(-15f),
                "Turn the view 15 degrees left. Right or middle drag orbits freely.", ref x);
            Button("+15°", 44f, () => ReliefNavigator.Turn(15f),
                "Turn the view 15 degrees right. Right or middle drag orbits freely.", ref x);
            Button("N", 30f, ReliefNavigator.North, "North up at the default terrain tilt.", ref x);
            Button("LOW", 48f, () => ReliefNavigator.Tilt(-10f), "Lower the viewing angle by 10 degrees.", ref x);
            Button("HIGH", 52f, () => ReliefNavigator.Tilt(10f), "Raise the viewing angle by 10 degrees.", ref x);
            Button("FIT", 40f, ReliefNavigator.Fit,
                "Fit the whole mission map. Double-click the ground to zoom in there.", ref x);
            followButton = Button("FREE", 78f, ReliefNavigator.ToggleFollow,
                "FOLLOW keeps your aircraft centred; dragging the map sets it FREE.", ref x);
            lastRevision = -1;
            nextCursorRead = 0f;
        }

        private static AvControl Button(string label, float width, System.Action onClick, string help, ref float x)
        {
            AvControl control = AvControl.Make(root, new AvControl.Spec(label, onClick));
            MfdChromeLay.Place(control.Rect, new Rect(x, 0f, width, 26f));
            control.Help = help;
            x += width + 2f;
            return control;
        }

        internal static void Restore()
        {
            readout = null;
            cursorLine = null;
            followButton = null;
            lastRevision = -1;
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }
    }
}
