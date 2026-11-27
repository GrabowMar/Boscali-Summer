using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Relief camera controls in the drawer's VIEW page: labeled turn, tilt, fit, follow and WASD actions with the current pose.
    /// The stock floating console retains its compact icon row. The north needle follows the view; mouse navigation remains
    /// in <see cref="ReliefNavigator"/>. Hidden controls neither block map input nor refresh their readouts.
    /// </summary>
    internal static class MfdMapOrbitControls
    {
        private const float Pad = 8f, Icon = 24f, H = 20f, Top = -3f, Gap = 2f, WasdW = 52f;
        private static RectTransform root;
        private static TMP_Text readout, needle;
        private static AvControl followButton, flyButton, perspButton;
        private static readonly AvControl[] navigation = new AvControl[6];
        private static float layoutWidth = -1f;
        private static int lastRevision = -1;
        private static bool lastFollowing, lastFlying, lastDrawing, lastTopDown, embedded;

        internal static bool Contains(Vector2 screenPoint) => root != null && root.gameObject.activeInHierarchy &&
            MapUiPointer.Contains(embedded ? MfdMapControls.Root : root, screenPoint);

        internal static void Tick(DynamicMap map)
        {
            if (map?.mapBackground == null) return;
            bool drawing = MfdTerrainRelief.IsDrawing;
            if (!drawing && !MfdMapControls.Shown) return;
            RectTransform slot = MapSelectionDeck.ViewSlot;
            if (slot == null) return;   // the console ticks after the relief: its slot exists from the next frame
            if (root == null || root.parent != slot || embedded != MfdMapControls.Shown) Build(slot);
            if (!root.gameObject.activeInHierarchy) return;
            if (embedded && Mathf.Abs(layoutWidth - slot.rect.width) >= .5f) LayoutEmbedded(slot.rect.width);

            bool following = ReliefNavigator.Following, flying = ReliefNavigator.Flying, topDown = ReliefNavigator.IsTopDown;
            if (lastRevision == MfdTerrainRelief.ViewRevision && following == lastFollowing && flying == lastFlying &&
                drawing == lastDrawing && topDown == lastTopDown) return;
            lastRevision = MfdTerrainRelief.ViewRevision;
            lastFollowing = following;
            lastFlying = flying;
            lastDrawing = drawing;
            lastTopDown = topDown;
            for (int i = 0; i < navigation.Length; i++) navigation[i].Interactable = drawing;
            followButton.Interactable = flyButton.Interactable = perspButton.Interactable = drawing;
            int yaw = Mathf.RoundToInt(MfdTerrainRelief.Yaw);
            readout.text = !drawing ? "3D RELIEF OFF"
                : Heading(yaw) + "°  " + MfdChromeLay.TwoDigits(Mathf.RoundToInt(MfdTerrainRelief.Pitch)) + "°  ×" +
                  AvNum.Fixed(MfdTerrainRelief.Zoom, 1);
            // The needle turns opposite to the view, so it keeps pointing at north on the screen.
            if (needle != null) needle.rectTransform.localEulerAngles = new Vector3(0f, 0f, yaw);
            perspButton.Label = topDown ? "TOP" : "3D";
            perspButton.Latched = drawing && !topDown;
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
            embedded = MfdMapControls.Shown;
            var go = new GameObject("NOAvionics.MapOrbit", typeof(RectTransform));
            root = go.GetComponent<RectTransform>();
            root.SetParent(slot, false);
            AvLay.Fill(root);
            float x = Pad, width = MapSelectionDeck.Width;
            AvControl compass = navigation[0] = Button("", AvIcon.ArrowUp, ReliefNavigator.North,
                "Compass: needle points north. Click for North-Up; click again while North-Up for top-down.", ref x);
            needle = compass.Glyph;
            navigation[1] = Button("", AvIcon.ChevronLeft, () => ReliefNavigator.Turn(-15f),
                "Turn the view 15 degrees left. Ctrl + wheel does the same; hold wheel, Alt+left or right button and drag to orbit.", ref x);
            navigation[2] = Button("", AvIcon.ChevronRight, () => ReliefNavigator.Turn(15f),
                "Turn the view 15 degrees right. Ctrl + wheel does the same; hold wheel, Alt+left or right button and drag to orbit.", ref x);
            navigation[3] = Button(embedded ? "TILT UP" : "", AvIcon.ChevronUp, () => ReliefNavigator.Tilt(10f),
                "Look more straight down (tilt +10 degrees). Shift + wheel tilts in smaller steps.", ref x);
            navigation[4] = Button(embedded ? "TILT DOWN" : "", AvIcon.ChevronDown, () => ReliefNavigator.Tilt(-10f),
                "Look more toward the horizon (tilt -10 degrees). Shift + wheel tilts in smaller steps.", ref x);
            perspButton = Button("TOP", AvIcon.None, ReliefNavigator.TogglePerspective,
                "Toggle between straight top-down chart view and tilted 3D relief.", ref x, 34f);
            navigation[5] = Button("", AvIcon.Maximize, ReliefNavigator.Fit,
                "Fit the whole mission map (Home key). Double-click empty ground to zoom in there.", ref x);
            navigation[0].Rect.name = "Control NORTH";
            navigation[1].Rect.name = "Control TURN L";
            navigation[2].Rect.name = "Control TURN R";
            perspButton.Rect.name = "Control VIEW";
            navigation[5].Rect.name = "Control FIT";

            float right = width - Pad - WasdW;
            float end = right;
            flyButton = Button("WASD", AvIcon.None, ReliefNavigator.ToggleFly,
                "Keyboard camera flight: W/A/S/D pan, Q/E turn, R/F tilt while the map is open. Lit while on.", ref end, WasdW);
            end = right - Gap - Icon;
            followButton = Button(embedded ? "FOLLOW" : "", AvIcon.CurrentLocation, ReliefNavigator.ToggleFollow,
                "Follow your aircraft (lit while on); a drag of the map moves freely again.", ref end);

            readout = AvText.Make(root, "Readout", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);
            MfdChromeLay.Place(readout.rectTransform, new Rect(x + 4f, Top, Mathf.Max(40f, right - Gap - Icon - 6f - x - 4f), H));
            readout.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            readout.raycastTarget = false;
            AvText.Fit(readout, false);
            if (embedded) LayoutEmbedded(slot.rect.width);
            lastRevision = -1;
        }

        private static void LayoutEmbedded(float width)
        {
            layoutWidth = width;
            float readoutWidth = Mathf.Min(164f, width * .33f);
            float cell = Mathf.Max(1f, (width - readoutWidth - 16f) / 5f);
            MfdChromeLay.Place(readout.rectTransform, new Rect(0f, 0f, readoutWidth - 4f, MfdMapControls.RowHeight));
            readout.alignment = TextAlignmentOptions.MidlineLeft;
            // Compass/turn/perspective/fit share row 1; hover help carries full descriptions.
            MfdChromeLay.Place(navigation[0].Rect, new Rect(readoutWidth, 0f, cell, MfdMapControls.RowHeight));
            MfdChromeLay.Place(navigation[1].Rect, new Rect(readoutWidth + cell + 4f, 0f, cell, MfdMapControls.RowHeight));
            MfdChromeLay.Place(navigation[2].Rect, new Rect(readoutWidth + (cell + 4f) * 2f, 0f, cell, MfdMapControls.RowHeight));
            MfdChromeLay.Place(perspButton.Rect, new Rect(readoutWidth + (cell + 4f) * 3f, 0f, cell, MfdMapControls.RowHeight));
            MfdChromeLay.Place(navigation[5].Rect, new Rect(readoutWidth + (cell + 4f) * 4f, 0f, cell, MfdMapControls.RowHeight));
            cell = Mathf.Max(1f, (width - 12f) / 4f);
            float y = -MfdMapControls.RowPitch;
            MfdChromeLay.Place(navigation[3].Rect, new Rect(0f, y, cell, MfdMapControls.RowHeight));
            MfdChromeLay.Place(navigation[4].Rect, new Rect(cell + 4f, y, cell, MfdMapControls.RowHeight));
            MfdChromeLay.Place(followButton.Rect, new Rect((cell + 4f) * 2f, y, cell, MfdMapControls.RowHeight));
            MfdChromeLay.Place(flyButton.Rect, new Rect((cell + 4f) * 3f, y, cell, MfdMapControls.RowHeight));
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
            followButton = flyButton = perspButton = null;
            for (int i = 0; i < navigation.Length; i++) navigation[i] = null;
            layoutWidth = -1f;
            lastRevision = -1;
            if (root != null) { root.gameObject.SetActive(false); Object.Destroy(root.gameObject); }
            root = null;
        }
    }
}
