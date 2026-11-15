using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The relief's view row, inside the map console (<see cref="MapSelectionDeck.ViewSlot"/>, 2026-11-15; it was its own bar of
    /// eight worded buttons under the deck). One line of quiet icon buttons: a compass whose needle points to north on screen
    /// (press: north up), turn left/right, tilt up/down, fit; the yaw/pitch/zoom readout; FOLLOW and WASD toggles at the end, latched
    /// while on. Hover help shows on the console's line. Mouse navigation lives in <see cref="ReliefNavigator"/>.
    /// </summary>
    internal static class MfdMapOrbitControls
    {
        private const float Pad = 8f, Icon = 24f, H = 20f, Top = -3f, Gap = 2f, WasdW = 52f;
        private static RectTransform root;
        private static TMP_Text readout, needle;
        private static AvControl followButton, flyButton;
        private static int lastRevision = -1;
        private static bool lastFollowing, lastFlying;

        internal static bool Contains(Vector2 screenPoint) => MapUiPointer.Contains(root, screenPoint);

        internal static void Tick(DynamicMap map)
        {
            if (map?.mapBackground == null || !MfdTerrainRelief.IsDrawing) return;
            RectTransform slot = MapSelectionDeck.ViewSlot;
            if (slot == null) return;   // the console ticks after the relief: its slot exists from the next frame
            if (root == null || root.parent != slot) Build(slot);

            bool following = ReliefNavigator.Following, flying = ReliefNavigator.Flying;
            if (lastRevision == MfdTerrainRelief.ViewRevision && following == lastFollowing && flying == lastFlying) return;
            lastRevision = MfdTerrainRelief.ViewRevision;
            lastFollowing = following;
            lastFlying = flying;
            int yaw = Mathf.RoundToInt(MfdTerrainRelief.Yaw);
            readout.text = Heading(yaw) + "°  " + MfdChromeLay.TwoDigits(Mathf.RoundToInt(MfdTerrainRelief.Pitch)) + "°  ×" +
                AvNum.Fixed(MfdTerrainRelief.Zoom, 1);
            // The needle turns opposite to the view, so it keeps pointing at north on the screen.
            if (needle != null) needle.rectTransform.localEulerAngles = new Vector3(0f, 0f, yaw);
            followButton.Latched = following;
            flyButton.Latched = flying;
        }

        /// <summary>Yaw as a signed three-digit field (+045 / -045 / 000), digits written by hand like AvNum.</summary>
        private static string Heading(int degrees)
        {
            string digits = AvNum.Fixed(Mathf.Abs(degrees), 0).PadLeft(3, '0');
            return degrees > 0 ? "+" + digits : degrees < 0 ? "-" + digits : digits;
        }

        private static void Build(RectTransform slot)
        {
            Restore();
            var go = new GameObject("NOAvionics.MapOrbit", typeof(RectTransform));
            root = go.GetComponent<RectTransform>();
            root.SetParent(slot, false);
            AvLay.Fill(root);
            float x = Pad, width = MapSelectionDeck.Width;
            AvControl compass = Button("", AvIcon.ArrowUp, ReliefNavigator.North,
                "Compass: the needle points to north. Press for north up at the default tilt.", ref x);
            needle = compass.Glyph;
            Button("", AvIcon.ChevronLeft, () => ReliefNavigator.Turn(-15f),
                "Turn the view 15 degrees left. Ctrl + wheel does the same; hold the wheel or the right button and drag to orbit freely.", ref x);
            Button("", AvIcon.ChevronRight, () => ReliefNavigator.Turn(15f),
                "Turn the view 15 degrees right. Ctrl + wheel does the same; hold the wheel or the right button and drag to orbit freely.", ref x);
            Button("", AvIcon.ChevronUp, () => ReliefNavigator.Tilt(10f),
                "Look more straight down (tilt +10 degrees). Shift + wheel tilts in smaller steps.", ref x);
            Button("", AvIcon.ChevronDown, () => ReliefNavigator.Tilt(-10f),
                "Look more toward the horizon (tilt -10 degrees). Shift + wheel tilts in smaller steps.", ref x);
            Button("", AvIcon.Maximize, ReliefNavigator.Fit,
                "Fit the whole mission map. Double-click the ground to zoom in there.", ref x);

            float right = width - Pad - WasdW;
            float end = right;
            flyButton = Button("WASD", AvIcon.None, ReliefNavigator.ToggleFly,
                "Keyboard camera flight: W/A/S/D move relative to the view heading while the map is open. Lit while on.", ref end, WasdW);
            end = right - Gap - Icon;
            followButton = Button("", AvIcon.CurrentLocation, ReliefNavigator.ToggleFollow,
                "Follow your aircraft (lit while on); a drag of the map moves freely again.", ref end);

            readout = AvText.Make(root, "Readout", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
            MfdChromeLay.Place(readout.rectTransform, new Rect(x + 4f, Top, Mathf.Max(40f, right - Gap - Icon - 6f - x - 4f), H));
            readout.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            readout.raycastTarget = false;
            AvText.Fit(readout, false);
            lastRevision = -1;
        }

        private static AvControl Button(string label, AvIcon icon, System.Action onClick, string help, ref float x, float width = Icon)
        {
            AvControl control = AvControl.Make(root, new AvControl.Spec(label, onClick, AvButtonStyle.Quiet, icon));
            MfdChromeLay.Place(control.Rect, new Rect(x, Top, width, H));
            control.SingleLine();
            control.Bracket = 0f;
            control.Help = help;
            x += width + Gap;
            return control;
        }

        internal static void Restore()
        {
            readout = needle = null;
            followButton = flyButton = null;
            lastRevision = -1;
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }
    }
}
