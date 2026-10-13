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
        private static AvControl dragButton, clickButton, sideButton, clearButton;
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
            RefreshTools(map);
            string cursor;
            if (MfdMapInteractions.Feedback != null)
                cursor = MfdMapInteractions.Feedback;
            // Says the orbit drag is live: a wheel button the game never sees leaves this line unchanged.
            else if (ReliefNavigator.Turning)
                cursor = "TURNING  /  DRAG SIDEWAYS TO TURN, UP OR DOWN TO TILT";
            else if (MfdTerrainRelief.HoveredStackCount >= 3)
                cursor = MfdTerrainRelief.HoveredStackCount + " TRACKS  /  RIGHT CLICK TO SELECT STACK";
            else if (MfdTerrainRelief.TryInspectCursor(out GlobalPosition point, out float elevation))
            {
                // A dropped range mark turns the line into a ruler; the context card still carries the elevation.
                string leg = MfdMapInteractions.FromMark(point);
                cursor = "X " + AvNum.Signed(point.x / 1000f, 1) + "  Z " + AvNum.Signed(point.z / 1000f, 1) +
                    " km  ·  " + (leg != null ? "MARK " + leg : "TERRAIN ~" + AvNum.Fixed(elevation, 0) + " m");
            }
            else cursor = "DRAG MOVES  /  WHEEL ZOOMS  /  RMB DRAG TURNS  /  RMB CLICK MENU";
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
            root.sizeDelta = new Vector2(RootWidth, 107f);
            MfdChromeLay.Panel(root, "Back", new Rect(0f, 0f, RootWidth, 107f),
                AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(.92f),
                AvStyleHost.FuiColor("frame", AvTheme.Frame), AvChamfer.Diagonal(5f));
            cursorLine = new AvFooter(root);
            root.gameObject.AddComponent<AvHelpScope>().Footer = cursorLine;
            cursorLine.Place(new AvSlot(5f, 90f, RootWidth - 10f, 15f));
            readout = AvText.Make(root, "Readout", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineLeft);
            MfdChromeLay.Place(readout.rectTransform, new Rect(8f, -3f, 130f, 27f));
            readout.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            AvText.Fit(readout, false);
            float x = 139f;
            Button("-15°", 57f, -3f, () => ReliefNavigator.Turn(-15f),
                "Turn the view 15 degrees left. Ctrl + wheel does the same; hold the wheel or the right button and drag to orbit freely.", ref x);
            Button("+15°", 57f, -3f, () => ReliefNavigator.Turn(15f),
                "Turn the view 15 degrees right. Ctrl + wheel does the same; hold the wheel or the right button and drag to orbit freely.", ref x);
            Button("NORTH", 65f, -3f, ReliefNavigator.North,
                "North up at the default terrain tilt.", ref x);
            Button("FIT", 60f, -3f, ReliefNavigator.Fit,
                "Fit the whole mission map. Double-click the ground to zoom in there.", ref x);
            x = 6f;
            Button("TILT -10°", 82f, -32f, () => ReliefNavigator.Tilt(-10f),
                "Lower the viewing angle by 10 degrees. Shift + wheel tilts in smaller steps.", ref x);
            Button("TILT +10°", 82f, -32f, () => ReliefNavigator.Tilt(10f),
                "Raise the viewing angle by 10 degrees. Shift + wheel tilts in smaller steps.", ref x);
            followButton = Button("FREE PAN", 103f, -32f, ReliefNavigator.ToggleFollow,
                "Follow your aircraft, or drag the map to move freely.", ref x);
            flyButton = Button("WASD OFF", 103f, -32f, ReliefNavigator.ToggleFly,
                "Toggle keyboard camera flight. W/A/S/D moves relative to view heading while the map is open.", ref x);
            // Selection tools: the modifiers as buttons, so nothing about the map has to be remembered.
            // Each one writes its setting, so the choice is kept between sessions.
            x = 6f;
            dragButton = Button("DRAG PAN", 92f, -61f, FlipDrag,
                "What a left drag does: PAN moves the map and Shift or Ctrl + drag draws a selection box; BOX swaps the two.", ref x);
            clickButton = Button("CLICK ADDS", 96f, -61f, FlipClick,
                "What a click on a contact does: ADDS builds up the selection (a second click drops it); ONE replaces it, and Shift + click still adds. While you fly, the selection is your target list.", ref x);
            sideButton = Button("BOX ALL", 98f, -61f, CycleSide,
                "Which contacts a selection box picks up: ALL, HOSTILE only, or your OWN side only.", ref x);
            clearButton = Button("CLEAR", 82f, -61f, MfdMapInteractions.ClearSelectionTool,
                "Empty the selection. A click on bare map does the same.", ref x);
            lastRevision = -1;
            nextCursorRead = 0f;
        }

        private static void FlipDrag()
        {
            var entry = Plugin.Settings?.Command?.MapLeftDragBox;
            if (entry != null) entry.Value = !entry.Value;
            nextCursorRead = 0f;
        }

        private static void FlipClick()
        {
            var entry = Plugin.Settings?.Command?.MapClickAdds;
            if (entry != null) entry.Value = !entry.Value;
            nextCursorRead = 0f;
        }

        private static void CycleSide()
        {
            var entry = Plugin.Settings?.Command?.MapBoxSide;
            if (entry != null) entry.Value = (Domain.MapBoxSide)(((int)entry.Value + 1) % 3);
            nextCursorRead = 0f;
        }

        private static void Tool(AvControl control, string label, bool latched)
        {
            if (control == null) return;
            if (control.Label != label) control.Label = label;
            if (control.Latched != latched) control.Latched = latched;
        }

        private static void RefreshTools(DynamicMap map)
        {
            var settings = Plugin.Settings?.Command;
            bool box = settings?.MapLeftDragBox?.Value == true, adds = settings?.MapClickAdds?.Value != false;
            Domain.MapBoxSide side = settings?.MapBoxSide?.Value ?? Domain.MapBoxSide.All;
            Tool(dragButton, box ? "DRAG BOX" : "DRAG PAN", box);
            Tool(clickButton, adds ? "CLICK ADDS" : "CLICK ONE", !adds);
            Tool(sideButton, side == Domain.MapBoxSide.Hostile ? "BOX HOSTILE" : side == Domain.MapBoxSide.Friendly ? "BOX OWN" : "BOX ALL",
                side != Domain.MapBoxSide.All);
            int selected = map.selectedIcons != null ? map.selectedIcons.Count : 0;
            Tool(clearButton, selected > 0 ? "CLEAR " + AvNum.Fixed(selected, 0) : "CLEAR", false);
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
            dragButton = clickButton = sideButton = clearButton = null;
            lastRevision = -1;
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }
    }
}
