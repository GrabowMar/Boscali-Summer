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
        private static RectTransform root;
        private static TMP_Text readout;
        private static TMP_Text cursorText;
        private static AvButton followButton;
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
                    readout.text = $"3D {Mathf.RoundToInt(MfdTerrainRelief.Yaw):+000;-000;000}°/" +
                        $"{Mathf.RoundToInt(MfdTerrainRelief.Pitch):00}°  ×{MfdTerrainRelief.Zoom:0.0}";
                if (followButton != null)
                {
                    followButton.SetText(following ? "FOLLOW" : "FREE");
                    followButton.SetLatched(following);
                }
            }
            if (cursorText == null || Time.unscaledTime < nextCursorRead) return;
            nextCursorRead = Time.unscaledTime + .1f;
            string cursor;
            if (MfdMapInteractions.Feedback != null)
                cursor = MfdMapInteractions.Feedback;
            else if (MfdTerrainRelief.HoveredStackCount >= 5)
                cursor = MfdTerrainRelief.HoveredStackCount + " TRACKS  /  ZOOM TO RESOLVE";
            else if (MfdTerrainRelief.TryInspectCursor(out GlobalPosition point, out float elevation))
                cursor = $"X {point.x / 1000f:+0.0;-0.0;0.0}  Z {point.z / 1000f:+0.0;-0.0;0.0} km  ·  TERRAIN ~{elevation:0} m";
            else cursor = "";
            if (cursorText.text != cursor) cursorText.text = cursor;
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
            root.sizeDelta = new Vector2(420f, 46f);
            readout = AvKit.Label(root, "", new Rect(0f, 2f, 128f, 24f),
                AvTheme.RailInfo, AvTokens.FontMicro);
            AvKit.Button(root, "-15°", new Rect(130f, 0f, 33f, 26f),
                () => ReliefNavigator.Turn(-15f), AvTokens.FontMicro)
                .WithTooltip("Turn the view 15 degrees left. Right or middle drag orbits freely.");
            AvKit.Button(root, "+15°", new Rect(165f, 0f, 33f, 26f),
                () => ReliefNavigator.Turn(15f), AvTokens.FontMicro)
                .WithTooltip("Turn the view 15 degrees right. Right or middle drag orbits freely.");
            AvKit.Button(root, "N", new Rect(200f, 0f, 27f, 26f),
                ReliefNavigator.North, AvTokens.FontMicro)
                .WithTooltip("North up at the default terrain tilt.");
            AvKit.Button(root, "LOW", new Rect(229f, 0f, 40f, 26f),
                () => ReliefNavigator.Tilt(-10f), AvTokens.FontMicro)
                .WithTooltip("Lower the viewing angle by 10 degrees.");
            AvKit.Button(root, "HIGH", new Rect(271f, 0f, 44f, 26f),
                () => ReliefNavigator.Tilt(10f), AvTokens.FontMicro)
                .WithTooltip("Raise the viewing angle by 10 degrees.");
            AvKit.Button(root, "FIT", new Rect(317f, 0f, 33f, 26f),
                ReliefNavigator.Fit, AvTokens.FontMicro)
                .WithTooltip("Fit the whole mission map. Double-click the ground to zoom in there.");
            followButton = AvKit.Button(root, "FREE", new Rect(352f, 0f, 68f, 26f),
                ReliefNavigator.ToggleFollow, AvTokens.FontMicro)
                .WithTooltip("FOLLOW keeps your aircraft centred; dragging the map sets it FREE.");
            cursorText = AvKit.Label(root, "", new Rect(0f, -29f, 420f, 15f),
                AvTheme.Dim, AvTokens.FontMicro);
            lastRevision = -1;
            nextCursorRead = 0f;
        }

        internal static void Restore()
        {
            readout = null;
            cursorText = null;
            followButton = null;
            lastRevision = -1;
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }
    }
}
