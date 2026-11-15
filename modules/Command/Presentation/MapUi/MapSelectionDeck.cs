using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The map console at the top right of the big map (2026-11-15 redesign of the 2026-10-29 deck): ONE box instead of a deck
    /// with a view bar under it. Top to bottom: the selection header (count, hostile/own split, fold), the selection actions
    /// (FRAME / SAME TYPE / CLEAR) as quiet tool buttons, the CLICK / DRAG / BOX choices as compact segmented switches (the
    /// latched one in force; each writes its setting, so it is kept between sessions), the relief's view row while the 3D relief
    /// draws (<see cref="MfdMapOrbitControls"/> builds into <see cref="ViewSlot"/>), and the line for feedback, hover help and the
    /// cursor's fix. The chevron folds it to the header. On the flat map and the relief alike.
    /// </summary>
    internal static class MapSelectionDeck
    {
        internal const float Width = 396f, HeadH = 30f, ViewRowH = 24f;
        private const float Pad = 8f, RowH = 22f, SegH = 20f, LineH = 14f, Gap = 4f, SegGap = 2f, GroupGap = 12f;

        private static RectTransform root, body, viewSlot;
        private static AvFrame back;
        private static TMP_Text count, split;
        private static AvFooter line;
        private static AvControl frameButton, typeButton, clearButton, fold;
        private static readonly AvControl[] click = new AvControl[2], drag = new AvControl[2], side = new AvControl[3];
        private static float nextRead, modesBottom;
        private static int lastTotal = -1, lastHostile = -1, lastOwn = -1;
        private static bool folded, viewShown;

        internal static bool Contains(Vector2 screen) =>
            root != null && root.gameObject.activeInHierarchy && MapUiPointer.Contains(root, screen);

        /// <summary>The console's feedback line, where the view row shows its hover help too.</summary>
        internal static AvFooter Footer => line;

        /// <summary>Where the relief's view row goes; inactive while the relief does not draw or the console is folded.</summary>
        internal static RectTransform ViewSlot => viewSlot;

        /// <summary>Folded to its header (client-local, kept until the game closes).</summary>
        internal static bool Folded => folded;

        /// <summary>Read-back for the automation hook.</summary>
        internal static bool Shown => root != null && root.gameObject.activeInHierarchy;
        internal static string CountText => count != null ? count.text : null;

        internal static void Tick(DynamicMap map)
        {
            RectTransform viewport = map.mapBackground.rectTransform;
            if (root == null || root.parent != viewport) Build(viewport);
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            bool view = MfdTerrainRelief.IsDrawing;
            if (view != viewShown) { viewShown = view; Apply(); }
            if (Time.unscaledTime < nextRead) return;
            nextRead = Time.unscaledTime + .1f;
            // Above the map layers, beside the banner (no per-frame reorder: that rebuilds the canvas).
            if (root.GetSiblingIndex() < viewport.childCount - 2) root.SetAsLastSibling();
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
            count.color = total > 0 ? AvStyleHost.FuiColor("ink", AvTheme.TextPrimary) : AvStyleHost.FuiColor("muted", AvTheme.Dim);
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
            back = MfdChromeLay.Panel(root, "Back", new Rect(0f, 0f, Width, HeadH),
                AvStyleHost.FuiColor("ground", AvTheme.Ground).WithAlpha(.96f),
                AvStyleHost.FuiColor("frame", AvTheme.Frame).WithAlpha(.9f), AvChamfer.Diagonal(5f));
            Color muted = AvStyleHost.FuiColor("muted", AvTheme.Dim);

            // Header: always shown, folded or not. The count reads first; the split says whose.
            count = Text(root, "Count", AvTextRole.DataStrong, "", new Rect(Pad + 2f, -5f, 100f, 20f), muted);
            split = Text(root, "Split", AvTextRole.DataSmall, "", new Rect(Pad + 106f, -6f, Width - Pad - 106f - 34f, 18f), muted);
            split.richText = true;
            fold = AvControl.Make(root, new AvControl.Spec("", Fold, AvButtonStyle.Quiet, AvIcon.ChevronUp));
            MfdChromeLay.Place(fold.Rect, new Rect(Width - 30f, -5f, 24f, 20f));
            fold.Bracket = 0f;
            fold.Help = "Fold the map console to its header, or open it again.";

            var bodyGo = new GameObject("Body", typeof(RectTransform));
            body = bodyGo.GetComponent<RectTransform>();
            body.SetParent(root, false);
            MfdChromeLay.Place(body, new Rect(0f, 0f, Width, HeadH));

            float y = -HeadH, third = (Width - 2f * Pad - 2f * Gap) / 3f;
            frameButton = Tool("FRAME", AvIcon.Focus2, MfdMapInteractions.FrameSelection, Pad, y, third,
                "Centre the view on what is selected (zoomed in on a single track).");
            typeButton = Tool("SAME TYPE", AvIcon.Filter, MfdMapInteractions.SelectSameType, Pad + third + Gap, y, third,
                "Select every visible track of the first selected one's type and side, and only those.");
            clearButton = Tool("CLEAR", AvIcon.Trash, MfdMapInteractions.ClearSelectionTool, Pad + 2f * (third + Gap), y, third,
                "Empty the selection (while you fly, your target list too). A click on bare map leaves it.");

            // The choices flow as segmented switches and wrap when the next group does not fit.
            float x = Pad;
            y -= RowH + Gap + 2f;
            Group(click, "CLICK", new[] { "ADDS", "ONLY" }, SetClick, ref x, ref y,
                "What a click on a track does: ADDS builds the selection up (a second click drops it); ONLY makes it the only one. Shift or Ctrl + click always adds.");
            Group(drag, "DRAG", new[] { "PANS", "BOXES" }, SetDrag, ref x, ref y,
                "What a plain left drag does: PANS moves the map; BOXES draws a box that adds. Shift-drag does the other one, Ctrl-drag always drops.");
            Group(side, "BOX", new[] { "ALL", "HOSTILE", "OWN" }, SetSide, ref x, ref y,
                "Which tracks a selection box picks up, adding or dropping.");
            modesBottom = -y + SegH + Gap + 2f;

            var slotGo = new GameObject("ViewSlot", typeof(RectTransform));
            viewSlot = slotGo.GetComponent<RectTransform>();
            viewSlot.SetParent(body, false);
            MfdChromeLay.Place(viewSlot, new Rect(0f, -modesBottom, Width, ViewRowH));
            MfdChromeLay.Rule(viewSlot, "Split", new Rect(Pad, 0f, Width - 2f * Pad, 1f),
                AvStyleHost.FuiColor("frame", AvTheme.Frame).WithAlpha(.45f)).raycastTarget = false;

            line = new AvFooter(body);
            root.gameObject.AddComponent<AvHelpScope>().Footer = line;
            nextRead = 0f;
            viewShown = MfdTerrainRelief.IsDrawing;
            Apply();
        }

        private static void Fold()
        {
            folded = !folded;
            Apply();
            nextRead = 0f;
        }

        /// <summary>Height and the line's place follow the fold and whether the view row shows.</summary>
        private static void Apply()
        {
            if (root == null) return;
            float viewH = viewShown ? ViewRowH + Gap : 0f;
            float h = folded ? HeadH : modesBottom + viewH + LineH + Gap;
            root.sizeDelta = new Vector2(Width, h);
            back.rectTransform.sizeDelta = new Vector2(Width, h);
            body.gameObject.SetActive(!folded);
            if (viewSlot.gameObject.activeSelf != viewShown) viewSlot.gameObject.SetActive(viewShown);
            line.Place(new AvSlot(Pad, modesBottom + viewH, Width - 2f * Pad, LineH));
            fold.SetIcon(folded ? AvIcon.ChevronDown : AvIcon.ChevronUp);
        }

        private static TMP_Text Text(RectTransform parent, string name, AvTextRole role, string value, Rect area, Color color)
        {
            TMP_Text t = AvText.Make(parent, name, role, value, TextAlignmentOptions.MidlineLeft);
            MfdChromeLay.Place(t.rectTransform, area);
            t.color = color;
            t.raycastTarget = false;
            AvText.Fit(t, false);
            return t;
        }

        private static AvControl Tool(string label, AvIcon icon, System.Action onClick, float x, float y, float width, string help)
        {
            AvControl control = AvControl.Make(body, new AvControl.Spec(label, onClick, AvButtonStyle.Quiet, icon));
            MfdChromeLay.Place(control.Rect, new Rect(x, y, width, RowH));
            control.SingleLine();
            control.Bracket = 0f;
            control.Help = help;
            return control;
        }

        /// <summary>One segmented switch: its key, then its options packed 2 px apart; wraps to a new row when it does not fit.</summary>
        private static void Group(AvControl[] slots, string key, string[] names, System.Action<int> set, ref float x, ref float y, string help)
        {
            TMP_Text label = Text(body, key, AvTextRole.Micro, key, new Rect(0f, 0f, 40f, SegH), AvStyleHost.FuiColor("key", AvTheme.RailInfo));
            float keyW = Mathf.Ceil(AvText.Width(label)) + 5f, total = keyW;
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                slots[i] = AvControl.Make(body, new AvControl.Spec(names[i], () => { set(index); nextRead = 0f; }, AvButtonStyle.Quiet));
                slots[i].SingleLine();
                slots[i].Bracket = 0f;
                slots[i].Help = help;
                total += Mathf.Ceil(slots[i].PreferredWidth) + (i > 0 ? SegGap : 0f);
            }
            if (x > Pad && x + total > Width - Pad) { x = Pad; y -= SegH + Gap; }
            MfdChromeLay.Place(label.rectTransform, new Rect(x, y, keyW, SegH));
            x += keyW;
            for (int i = 0; i < names.Length; i++)
            {
                float w = Mathf.Ceil(slots[i].PreferredWidth);
                MfdChromeLay.Place(slots[i].Rect, new Rect(x, y, w, SegH));
                x += w + SegGap;
            }
            x += GroupGap - SegGap;
        }

        private static void SetClick(int i) { var e = Plugin.Settings?.Command?.MapClickAdds; if (e != null) e.Value = i == 0; }

        private static void SetDrag(int i) { var e = Plugin.Settings?.Command?.MapLeftDragBox; if (e != null) e.Value = i == 1; }

        private static void SetSide(int i) { var e = Plugin.Settings?.Command?.MapBoxSide; if (e != null) e.Value = (MapBoxSide)i; }

        /// <summary>Map closed: the viewport goes back into the cockpit's small map, so the console must not ride along (kept for the next open).</summary>
        internal static void Hide()
        {
            if (root != null && root.gameObject.activeSelf) root.gameObject.SetActive(false);
        }

        /// <summary>Scene reset, or a viewport change: the console is rebuilt on the next tick (the fold is kept).</summary>
        internal static void Reset()
        {
            if (root != null) Object.Destroy(root.gameObject);
            root = body = viewSlot = null;
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
