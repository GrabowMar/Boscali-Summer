using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// Client-only relief view buttons and readout, under the selection deck on the fixed map viewport (the selection tools and the
    /// cursor line moved into <see cref="MapSelectionDeck"/> in the 2026-10-29 rework; this bar's hover help shows on the deck's line).
    /// Its chevron folds it to the readout (2026-11-06), and it follows the deck's folded height.
    /// Mouse navigation lives in <see cref="ReliefNavigator"/>.
    /// </summary>
    internal static class MfdMapOrbitControls
    {
        private const float RootWidth = MapSelectionDeck.Width, RootHeight = 64f, FoldedHeight = 34f, RowH = 26f;
        private static RectTransform root, body;
        private static AvFrame back;
        private static TMP_Text readout;
        private static AvControl followButton;
        private static AvControl flyButton;
        private static AvControl fold;
        private static AvHelpScope help;
        private static int lastRevision = -1;
        private static bool lastFollowing;
        private static bool lastFlying;
        private static bool folded;

        internal static bool Contains(Vector2 screenPoint) => MapUiPointer.Contains(root, screenPoint);

        internal static void Tick(DynamicMap map)
        {
            if (map?.mapBackground == null || !MfdTerrainRelief.IsDrawing) return;
            RectTransform viewport = map.mapBackground.rectTransform;
            if (root == null || root.parent != viewport) Build(viewport);
            if (root == null) return;
            // The deck ticks after the relief, so its line may not exist yet when this bar is built.
            if (help == null && MapSelectionDeck.Footer != null)
            {
                help = root.gameObject.AddComponent<AvHelpScope>();
                help.Footer = MapSelectionDeck.Footer;
            }
            // Right under the deck, which may be folded.
            float top = -(8f + MapSelectionDeck.ShownHeight + 4f);
            if (root.anchoredPosition.y != top) root.anchoredPosition = new Vector2(-12f, top);

            bool following = ReliefNavigator.Following;
            bool flying = ReliefNavigator.Flying;
            if (lastRevision == MfdTerrainRelief.ViewRevision && following == lastFollowing && flying == lastFlying) return;
            lastRevision = MfdTerrainRelief.ViewRevision;
            lastFollowing = following;
            lastFlying = flying;
            // Above the map layers when the view changes, never every frame (a reorder rebuilds the canvas).
            if (root.GetSiblingIndex() < viewport.childCount - 3) root.SetAsLastSibling();
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
            root.anchoredPosition = new Vector2(-12f, -(8f + MapSelectionDeck.ShownHeight + 4f));
            back = MfdChromeLay.Panel(root, "Back", new Rect(0f, 0f, RootWidth, RootHeight),
                AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(.92f),
                AvStyleHost.FuiColor("frame", AvTheme.Frame), AvChamfer.Diagonal(5f));
            readout = AvText.Make(root, "Readout", AvTextRole.DataStrong, "", TextAlignmentOptions.MidlineLeft);
            MfdChromeLay.Place(readout.rectTransform, new Rect(10f, -4f, 126f, RowH));
            readout.color = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);
            AvText.Fit(readout, false);
            fold = AvControl.Make(root, new AvControl.Spec("", Fold, AvButtonStyle.Quiet, AvIcon.ChevronUp));
            MfdChromeLay.Place(fold.Rect, new Rect(RootWidth - 34f, -5f, 28f, 24f));
            fold.Help = "Fold the view bar to its readout, or open it again.";

            var bodyGo = new GameObject("Body", typeof(RectTransform));
            body = bodyGo.GetComponent<RectTransform>();
            body.SetParent(root, false);
            MfdChromeLay.Place(body, new Rect(0f, 0f, RootWidth, RootHeight));
            float x = 138f;
            Button("-15°", 48f, -4f, () => ReliefNavigator.Turn(-15f),
                "Turn the view 15 degrees left. Ctrl + wheel does the same; hold the wheel or the right button and drag to orbit freely.", ref x);
            Button("+15°", 48f, -4f, () => ReliefNavigator.Turn(15f),
                "Turn the view 15 degrees right. Ctrl + wheel does the same; hold the wheel or the right button and drag to orbit freely.", ref x);
            Button("NORTH", 58f, -4f, ReliefNavigator.North,
                "North up at the default terrain tilt.", ref x);
            Button("FIT", 50f, -4f, ReliefNavigator.Fit,
                "Fit the whole mission map. Double-click the ground to zoom in there.", ref x);
            x = 6f;
            float quarter = (RootWidth - 12f - 3f * 4f) / 4f;
            Button("TILT -10°", quarter, -34f, () => ReliefNavigator.Tilt(-10f),
                "Lower the viewing angle by 10 degrees. Shift + wheel tilts in smaller steps.", ref x);
            Button("TILT +10°", quarter, -34f, () => ReliefNavigator.Tilt(10f),
                "Raise the viewing angle by 10 degrees. Shift + wheel tilts in smaller steps.", ref x);
            followButton = Button("FREE PAN", quarter, -34f, ReliefNavigator.ToggleFollow,
                "Follow your aircraft, or drag the map to move freely.", ref x);
            flyButton = Button("WASD OFF", quarter, -34f, ReliefNavigator.ToggleFly,
                "Toggle keyboard camera flight. W/A/S/D moves relative to view heading while the map is open.", ref x);
            lastRevision = -1;
            Apply();
        }

        private static void Fold()
        {
            folded = !folded;
            Apply();
        }

        private static void Apply()
        {
            float h = folded ? FoldedHeight : RootHeight;
            root.sizeDelta = new Vector2(RootWidth, h);
            back.rectTransform.sizeDelta = new Vector2(RootWidth, h);
            body.gameObject.SetActive(!folded);
            fold.SetIcon(folded ? AvIcon.ChevronDown : AvIcon.ChevronUp);
        }

        private static AvControl Button(string label, float width, float y,
            System.Action onClick, string help, ref float x)
        {
            AvControl control = AvControl.Make(body, new AvControl.Spec(label, onClick));
            MfdChromeLay.Place(control.Rect, new Rect(x, y, width, RowH));
            control.SingleLine();
            control.Help = help;
            x += width + 4f;
            return control;
        }

        internal static void Restore()
        {
            readout = null;
            followButton = null;
            flyButton = null;
            fold = null;
            back = null;
            body = null;
            help = null;
            lastRevision = -1;
            if (root != null) Object.Destroy(root.gameObject);
            root = null;
        }
    }
}
