using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The selection deck at the top right of the big map (2026-10-29, replaces the old cycling tool row): what is selected, split
    /// by side, with FRAME / SAME TYPE / CLEAR; every choice of CLICK, DRAG and BOX laid out at once, the latched one in force; and
    /// a line for feedback, hover help and (on the relief) the cursor's fix. The header's chevron folds it to the header alone
    /// (2026-11-06; the key-hint block went then). On the flat map and the relief alike; the relief's view bar sits under it.
    /// Each choice writes its setting, so it is kept between sessions.
    /// </summary>
    internal static class MapSelectionDeck
    {
        internal const float Width = 396f, Height = 190f, HeadH = 40f;
        private const float RowH = 26f, KeyW = 58f, Gap = 4f;

        private static RectTransform root, body;
        private static AvFrame back;
        private static TMP_Text count, split;
        private static AvFooter line;
        private static AvControl frameButton, typeButton, clearButton, fold;
        private static readonly AvControl[] click = new AvControl[2], drag = new AvControl[2], side = new AvControl[3];
        private static float nextRead;
        private static int lastTotal = -1, lastHostile = -1, lastOwn = -1;
        private static bool folded;

        internal static bool Contains(Vector2 screen) =>
            root != null && root.gameObject.activeInHierarchy && MapUiPointer.Contains(root, screen);

        /// <summary>The deck's feedback line, where the view bar under it shows its hover help too.</summary>
        internal static AvFooter Footer => line;

        /// <summary>Folded to its header (client-local, kept until the game closes).</summary>
        internal static bool Folded => folded;

        /// <summary>The height it takes now, so the view bar can sit right under it.</summary>
        internal static float ShownHeight => folded ? HeadH : Height;

        /// <summary>Read-back for the automation hook.</summary>
        internal static bool Shown => root != null && root.gameObject.activeInHierarchy;
        internal static string CountText => count != null ? count.text : null;

        internal static void Tick(DynamicMap map)
        {
            RectTransform viewport = map.mapBackground.rectTransform;
            if (root == null || root.parent != viewport) Build(viewport);
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            if (Time.unscaledTime < nextRead) return;
            nextRead = Time.unscaledTime + .1f;
            // Above the map layers, beside the banner and the view bar (no per-frame reorder: that rebuilds the canvas).
            if (root.GetSiblingIndex() < viewport.childCount - 3) root.SetAsLastSibling();
            Counts(map);
            if (folded) return;
            Choices();
            line.Set(Line());
        }

        private static void Counts(DynamicMap map)
        {
            int total = map.selectedIcons != null ? map.selectedIcons.Count : 0, hostile = 0, own = 0;
            for (int i = 0; i < total; i++)
            {
                if (!(map.selectedIcons[i] is UnitMapIcon icon) || icon.unit == null || icon.unit.NetworkHQ == null) continue;
                if (icon.unit.NetworkHQ == map.HQ) own++;
                else hostile++;
            }
            if (total == lastTotal && hostile == lastHostile && own == lastOwn) return;
            lastTotal = total;
            lastHostile = hostile;
            lastOwn = own;
            count.text = AvNum.Fixed(total, 0) + (total == 1 ? " TRACK" : " TRACKS");
            int other = total - hostile - own;
            split.text = total == 0 ? "NOTHING SELECTED"
                : Tinted(hostile + " HOSTILE", AvStyleHost.FuiColor("danger", AvTheme.Alert)) + "  ·  " +
                  Tinted(own + " OWN", AvStyleHost.FuiColor("friendly", AvTheme.Friendly)) +
                  (other > 0 ? "  ·  " + other + " OTHER" : "");
            frameButton.Interactable = typeButton.Interactable = clearButton.Interactable = total > 0;
            clearButton.Label = total > 0 ? "CLEAR " + AvNum.Fixed(total, 0) : "CLEAR";
        }

        private static string Tinted(string text, Color color) => "<color=#" + ColorUtility.ToHtmlStringRGB(color) + ">" + text + "</color>";

        private static void Choices()
        {
            var settings = Plugin.Settings?.Command;
            bool adds = settings?.MapClickAdds?.Value != false, boxes = settings?.MapLeftDragBox?.Value == true;
            int sideNow = (int)(settings?.MapBoxSide?.Value ?? MapBoxSide.All);
            Latch(click, adds ? 0 : 1);
            Latch(drag, boxes ? 1 : 0);
            Latch(side, sideNow);
        }

        private static void Latch(AvControl[] options, int on)
        {
            for (int i = 0; i < options.Length; i++)
                if (options[i] != null && options[i].Latched != (i == on)) options[i].Latched = i == on;
        }

        private static string Line()
        {
            if (MfdMapInteractions.Feedback != null) return MfdMapInteractions.Feedback;
            if (ReliefNavigator.Turning) return "TURNING  ·  DRAG SIDEWAYS TO TURN, UP OR DOWN TO TILT";
            if (MfdTerrainRelief.IsDrawing && MfdTerrainRelief.HoveredStackCount >= 3)
                return MfdTerrainRelief.HoveredStackCount + " TRACKS STACKED  ·  RIGHT CLICK FOR THE STACK";
            if (MfdTerrainRelief.IsDrawing && MfdTerrainRelief.TryInspectCursor(out GlobalPosition point, out float elevation))
            {
                // A dropped range mark turns the line into a ruler; the context card still carries the elevation.
                string leg = MfdMapInteractions.FromMark(point);
                return "X " + AvNum.Signed(point.x / 1000f, 1) + "  Z " + AvNum.Signed(point.z / 1000f, 1) +
                    " KM  ·  " + (leg != null ? "MARK " + leg : "TERRAIN ~" + AvNum.Fixed(elevation, 0) + " M");
            }
            return "";
        }

        private static void Build(RectTransform viewport)
        {
            Reset();
            var go = new GameObject("NOAvionics.MapSelectionDeck", typeof(RectTransform));
            root = go.GetComponent<RectTransform>();
            root.SetParent(viewport, false);
            root.anchorMin = root.anchorMax = root.pivot = new Vector2(1f, 1f);
            root.anchoredPosition = new Vector2(-12f, -8f);
            back = MfdChromeLay.Panel(root, "Back", new Rect(0f, 0f, Width, Height),
                AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(.92f),
                AvStyleHost.FuiColor("frame", AvTheme.Frame), AvChamfer.Diagonal(5f));
            Color muted = AvStyleHost.FuiColor("muted", AvTheme.Dim), ink = AvStyleHost.FuiColor("ink", AvTheme.TextPrimary);

            // Header: always shown, folded or not.
            Text(root, "Head", AvTextRole.Micro, "SELECTION", new Rect(10f, -4f, 120f, 12f), muted).raycastTarget = false;
            count = Text(root, "Count", AvTextRole.DataStrong, "", new Rect(10f, -16f, 120f, 22f), ink);
            split = Text(root, "Split", AvTextRole.DataSmall, "", new Rect(134f, -18f, Width - 134f - 40f, 18f), muted);
            split.richText = true;
            fold = AvControl.Make(root, new AvControl.Spec("", Fold, AvButtonStyle.Quiet, AvIcon.ChevronUp));
            MfdChromeLay.Place(fold.Rect, new Rect(Width - 34f, -8f, 28f, 24f));
            fold.Help = "Fold the deck to its header, or open it again.";

            var bodyGo = new GameObject("Body", typeof(RectTransform));
            body = bodyGo.GetComponent<RectTransform>();
            body.SetParent(root, false);
            MfdChromeLay.Place(body, new Rect(0f, 0f, Width, Height));
            MfdChromeLay.Rule(body, "Split", new Rect(8f, -HeadH + 1f, Width - 16f, 1f),
                AvStyleHost.FuiColor("frame", AvTheme.Frame).WithAlpha(.55f)).raycastTarget = false;

            float y = -HeadH - Gap, third = (Width - 12f - 2f * Gap) / 3f;
            frameButton = Button("FRAME", AvIcon.Focus2, MfdMapInteractions.FrameSelection, 6f, y, third,
                "Centre the view on what is selected (zoomed in on a single track).");
            typeButton = Button("SAME TYPE", AvIcon.Filter, MfdMapInteractions.SelectSameType, 6f + third + Gap, y, third,
                "Select every visible track of the first selected one's type and side, and only those.");
            clearButton = Button("CLEAR", AvIcon.Trash, MfdMapInteractions.ClearSelectionTool, 6f + 2f * (third + Gap), y, third,
                "Empty the selection (while you fly, your target list too). A click on bare map leaves it.");

            y -= RowH + Gap + 4f;
            Choice(click, "CLICK", y, new[] { "ADDS", "REPLACES" }, SetClick,
                "What a click on a track does: ADDS builds the selection up (a second click drops it); REPLACES makes it the only one. Shift or Ctrl + click always adds.");
            y -= RowH + Gap;
            Choice(drag, "DRAG", y, new[] { "PANS", "BOXES" }, SetDrag,
                "What a plain left drag does: PANS moves the map; BOXES draws a box that adds. Shift-drag does the other one, Ctrl-drag always drops.");
            y -= RowH + Gap;
            Choice(side, "BOX", y, new[] { "ALL", "HOSTILE", "OWN" }, SetSide,
                "Which tracks a selection box picks up, adding or dropping.");

            line = new AvFooter(body);
            root.gameObject.AddComponent<AvHelpScope>().Footer = line;
            line.Place(new AvSlot(6f, -y + RowH + 5f, Width - 12f, 16f));
            nextRead = 0f;
            Apply();
        }

        private static void Fold()
        {
            folded = !folded;
            Apply();
            nextRead = 0f;
        }

        private static void Apply()
        {
            float h = ShownHeight;
            root.sizeDelta = new Vector2(Width, h);
            back.rectTransform.sizeDelta = new Vector2(Width, h);
            body.gameObject.SetActive(!folded);
            fold.SetIcon(folded ? AvIcon.ChevronDown : AvIcon.ChevronUp);
        }

        private static TMP_Text Text(RectTransform parent, string name, AvTextRole role, string value, Rect area, Color color)
        {
            TMP_Text t = AvText.Make(parent, name, role, value, TextAlignmentOptions.MidlineLeft);
            MfdChromeLay.Place(t.rectTransform, area);
            t.color = color;
            AvText.Fit(t, false);
            return t;
        }

        private static AvControl Button(string label, AvIcon icon, System.Action onClick, float x, float y, float width, string help)
        {
            AvControl control = AvControl.Make(body, new AvControl.Spec(label, onClick, AvButtonStyle.Default, icon));
            MfdChromeLay.Place(control.Rect, new Rect(x, y, width, RowH));
            control.SingleLine();
            control.Help = help;
            return control;
        }

        private static void Choice(AvControl[] slots, string label, float y, string[] names, System.Action<int> set, string help)
        {
            TMP_Text key = Text(body, label, AvTextRole.Label, label, new Rect(10f, y, KeyW - 6f, RowH), AvStyleHost.FuiColor("key", AvTheme.RailInfo));
            key.raycastTarget = false;
            float left = 10f + KeyW, width = (Width - left - 6f - (names.Length - 1) * 3f) / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                slots[i] = AvControl.Make(body, new AvControl.Spec(names[i], () => { set(index); nextRead = 0f; }));
                MfdChromeLay.Place(slots[i].Rect, new Rect(left + i * (width + 3f), y, width, RowH));
                slots[i].SingleLine();
                slots[i].Help = help;
            }
        }

        private static void SetClick(int i) { var e = Plugin.Settings?.Command?.MapClickAdds; if (e != null) e.Value = i == 0; }

        private static void SetDrag(int i) { var e = Plugin.Settings?.Command?.MapLeftDragBox; if (e != null) e.Value = i == 1; }

        private static void SetSide(int i) { var e = Plugin.Settings?.Command?.MapBoxSide; if (e != null) e.Value = (MapBoxSide)i; }

        /// <summary>Map closed: the viewport goes back into the cockpit's small map, so the deck must not ride along (kept for the next open).</summary>
        internal static void Hide()
        {
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }

        /// <summary>Scene reset, or a viewport change: the deck is rebuilt on the next tick (the fold is kept).</summary>
        internal static void Reset()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = body = null;
            back = null;
            count = split = null;
            line = null;
            frameButton = typeButton = clearButton = fold = null;
            for (int i = 0; i < click.Length; i++) click[i] = null;
            for (int i = 0; i < drag.Length; i++) drag[i] = null;
            for (int i = 0; i < side.Length; i++) side[i] = null;
            lastTotal = lastHostile = lastOwn = -1;
        }
    }
}
