using BoscaliSummer.Core.Ui;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// One map-work drawer between the viewport and native instruments. Its roots are parked on close,
    /// so folding and switching tabs preserve tool state and scroll positions without rebuilding controls.
    /// </summary>
    internal static class MfdMapControls
    {
        internal const string Name = "NOAvionics.MapControls";
        internal const float HeaderHeight = 36f;
        private const float Pad = 8f, Gap = 8f, FooterHeight = 24f;
        private static RectTransform root, body, view, layers, plot;
        private static AvScrollView viewScroll;
        private static AvTicker ticker;
        private static AvFrame frame;
        private static Image header, rule;
        private static TMP_Text title, status;
        private static AvControl collapse, cancel;
        private static readonly AvControl[] tabs = new AvControl[3];
        private static Vector2 laidSize;
        private static bool toolsBuilt, laidFolded, lastBusy, layoutPending;
        private static float nextRead;
        private static float expandedHeight = 232f;

        internal static RectTransform Root => root;
        internal static RectTransform SelectionSlot { get; private set; }
        internal static RectTransform ViewSlot { get; private set; }
        internal static RectTransform TheaterSlot { get; private set; }
        internal static AvFooter Footer { get; private set; }
        internal static bool Folded { get; private set; }
        internal static int SelectedTab { get; private set; }
        internal static bool Shown => root != null && root.gameObject.activeInHierarchy;

        internal static float Height(float canvasHeight) => Folded ? HeaderHeight : canvasHeight < 900f ? 216f : 232f;

        internal static void Ensure(Canvas canvas, MfdLayout.Columns columns)
        {
            if (canvas == null) return;
            if (root == null || root.parent != canvas.transform) Build(canvas);
            expandedHeight = columns.Canvas.y < 900f ? 216f : 232f;
            root.anchorMin = root.anchorMax = new Vector2(.5f, .5f);
            root.pivot = new Vector2(0f, 1f);
            root.anchoredPosition = columns.Controls.position;
            root.sizeDelta = columns.Controls.size;
            root.localScale = Vector3.one;
            if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
            if (!MfdLayout.Near(laidSize, root.sizeDelta) || laidFolded != Folded) Layout();
            ShowPage();
            root.SetAsLastSibling();
            RequestRefresh();
        }

        private static void Build(Canvas canvas)
        {
            Reset();
            root = AvLay.Child((RectTransform)canvas.transform, Name);
            AvLay.Nest(root, true);
            ticker = root.gameObject.AddComponent<AvTicker>();
            frame = AvFrame.Add(root, "Frame", AvChamfer.All(0f));
            AvLay.Fill(frame.rectTransform);
            frame.raycastTarget = true;
            frame.Stroke = 1f;
            frame.Bracket = 0f;
            header = AvLay.Solid(root, "Header", Color.clear);
            AvSurfaceGradient.ApplyHeader(header);
            title = AvText.Make(root, "Title", AvTextRole.Head, "MAP CONTROLS", TextAlignmentOptions.MidlineLeft);
            title.fontSize = 15f;
            title.characterSpacing = 1f;
            title.raycastTarget = false;
            AvText.Fit(title, false);
            status = AvText.Make(root, "Status", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            status.raycastTarget = false;
            AvText.Fit(status, false);
            status.overflowMode = TextOverflowModes.Ellipsis;
            string[] labels = { "VIEW", "LAYERS", "PLOT" };
            AvIcon[] icons = { AvIcon.Focus2, AvIcon.LayersSubtract, AvIcon.Pencil };
            for (int i = 0; i < tabs.Length; i++)
            {
                int page = i;
                tabs[i] = AvControl.Make(root, new AvControl.Spec(labels[i], () => SelectTab(page), AvButtonStyle.Quiet, icons[i]));
                tabs[i].Rect.name = labels[i];
                tabs[i].Bracket = 0f;
                tabs[i].SingleLine();
                tabs[i].Help = i == 0 ? "Selection, map gestures, camera and theater controls."
                    : i == 1 ? "Map layers, overlays, display settings and the symbol key."
                    : "Private marks and shared plot tools. Arm here, then right-click the map.";
            }
            cancel = AvControl.Make(root, new AvControl.Spec("CANCEL", MapTool.RequestCancel, AvButtonStyle.Quiet, AvIcon.X));
            cancel.Rect.name = "Cancel";
            cancel.Bracket = 0f;
            cancel.SingleLine();
            cancel.Help = "Put down the armed map tool, like Escape. Existing marks stay on the map.";
            collapse = AvControl.Make(root, new AvControl.Spec("", () => SetFolded(!Folded), AvButtonStyle.Quiet, AvIcon.ChevronDown));
            collapse.Rect.name = "Collapse";
            collapse.Bracket = 0f;
            collapse.Help = "Collapse map controls to return their space to the map. The active tool stays armed.";
            rule = AvLay.Solid(root, "HeaderRule", Color.clear);
            rule.raycastTarget = false;

            body = AvLay.Child(root, "Body");
            view = AvLay.Child(body, "View");
            viewScroll = new AvScrollView(view, true);
            SelectionSlot = AvLay.Child(viewScroll.Content, "SelectionSlot");
            ViewSlot = AvLay.Child(viewScroll.Content, "ViewSlot");
            TheaterSlot = AvLay.Child(viewScroll.Content, "TheaterSlot");
            layers = AvLay.Child(body, "Layers");
            plot = AvLay.Child(body, "Plot");
            Footer = new AvFooter(root);
            root.gameObject.AddComponent<AvHelpScope>().Footer = Footer;
            ticker.Register(Footer);
            ticker.Register(new AvRestyleHook(Restyle));
            ticker.Add(-1, AvTickRate.Fast, Tick);
            MapUiPointer.Register(root);
            Restyle();
        }

        private static void Layout()
        {
            laidSize = root.sizeDelta;
            laidFolded = Folded;
            float width = laidSize.x;
            AvLay.Place(header.rectTransform, 1f, 1f, width - 2f, HeaderHeight - 1f);
            AvLay.Place(title.rectTransform, 12f, 4f, 122f, 28f);
            for (int i = 0; i < tabs.Length; i++) AvLay.Place(tabs[i].Rect, 142f + i * 82f, 4f, 80f, 28f);
            AvLay.Place(collapse.Rect, width - 38f, 4f, 30f, 28f);
            AvLay.Place(cancel.Rect, width - 122f, 4f, 80f, 28f);
            AvLay.Place(rule.rectTransform, 8f, HeaderHeight - 1f, width - 16f, 1f);
            LayoutHeader();
            float inner = Mathf.Max(1f, width - Pad * 2f);
            float height = Mathf.Max(0f, expandedHeight - HeaderHeight - FooterHeight - Pad * 2f);
            AvLay.Place(body, Pad, HeaderHeight + Pad, inner, height);
            AvLay.Fill(view);
            AvLay.Fill(layers);
            AvLay.Fill(plot);
            viewScroll.Place(inner, height);
            float selectionWidth = inner >= 1060f ? (inner - 16f) * .5f : inner;
            float selectionHeight = MapSelectionDeck.HeightForWidth(selectionWidth);
            float y;
            if (inner >= 1060f)
            {
                AvLay.Place(SelectionSlot, 0f, 0f, selectionWidth, selectionHeight);
                AvLay.Place(ViewSlot, selectionWidth + 16f, 0f, selectionWidth, 86f);
                y = Mathf.Max(selectionHeight, 86f) + Gap;
            }
            else
            {
                AvLay.Place(SelectionSlot, 0f, 0f, inner, selectionHeight);
                AvLay.Place(ViewSlot, 0f, selectionHeight + Gap, inner, 86f);
                y = selectionHeight + Gap + 86f + Gap;
            }
            AvLay.Place(TheaterSlot, 0f, y, inner, 62f);
            // Preserve the current scroll offset when the column resizes; clamp after the new extent.
            float offset = viewScroll.Content.anchoredPosition.y;
            AvLay.Place(viewScroll.Content, 0f, 0f, inner, y + 62f);
            viewScroll.Content.anchoredPosition = new Vector2(0f, Mathf.Clamp(offset, 0f, Mathf.Max(0f, y + 62f - height)));
            if (toolsBuilt) MfdMapTools.Layout(inner, height);
            Footer.Place(new AvSlot(Pad, laidSize.y - FooterHeight - 2f, inner, FooterHeight));
            collapse.SetIcon(Folded ? AvIcon.ChevronUp : AvIcon.ChevronDown);
        }

        private static void LayoutHeader()
        {
            if (root == null) return;
            bool busy = MapPicker.IsBusy;
            float x = Folded ? 148f : 398f;
            float right = root.sizeDelta.x - (busy ? 130f : 46f);
            AvLay.Place(status.rectTransform, x, 4f, Mathf.Max(0f, right - x), 28f);
            cancel.Rect.gameObject.SetActive(busy);
            status.enabled = right - x >= 70f;
            lastBusy = busy;
        }

        private static void ShowPage()
        {
            body.gameObject.SetActive(!Folded);
            Footer.Rect.gameObject.SetActive(!Folded);
            rule.gameObject.SetActive(!Folded);
            view.gameObject.SetActive(SelectedTab == 0);
            layers.gameObject.SetActive(SelectedTab == 1);
            plot.gameObject.SetActive(SelectedTab == 2);
            for (int i = 0; i < tabs.Length; i++)
            {
                tabs[i].Rect.gameObject.SetActive(!Folded);
                tabs[i].Latched = i == SelectedTab;
            }
            if (!Folded && SelectedTab != 0 && !toolsBuilt)
            {
                MfdMapTools.Build(layers, plot, ticker, Footer);
                toolsBuilt = true;
                MfdMapTools.Layout(body.rect.width, body.rect.height);
            }
            if (toolsBuilt) MfdMapTools.Refresh(Folded ? -1 : SelectedTab);
        }

        private static void Tick()
        {
            if (!Shown || Time.unscaledTime < nextRead) return;
            nextRead = Time.unscaledTime + .15f;
            if (layoutPending) { layoutPending = false; Layout(); }
            if (lastBusy != MapPicker.IsBusy) LayoutHeader();
            string prompt = MapPicker.Prompt;
            int count = SceneSingleton<DynamicMap>.i?.selectedIcons?.Count ?? 0;
            AvText.Set(status, !string.IsNullOrEmpty(prompt) ? prompt : count + (count == 1 ? " TRACK SELECTED" : " TRACKS SELECTED"));
            status.color = MapPicker.IsBusy ? AvInk.State(AvState.Caution) : AvInk.Dim;
            string viewStatus = !Folded && SelectedTab == 0 ? MapSelectionDeck.StatusLine : null;
            if (!Folded)
                Footer.Set(!string.IsNullOrEmpty(prompt) ? prompt : !string.IsNullOrEmpty(viewStatus) ? viewStatus : MfdMapInteractions.Feedback ??
                    (SelectedTab == 0 ? "LEFT: SELECT / PAN   ·   WHEEL: ZOOM   ·   RIGHT: TOOL / ORDERS"
                    : SelectedTab == 1 ? "LIT = VISIBLE   ·   LAYERS USE YOUR CURRENT FACTION'S INFORMATION"
                    : "ARM A TOOL, THEN RIGHT-CLICK THE MAP   ·   ESC: CANCEL"), MapPicker.IsBusy ? AvState.Caution : AvState.Inert);
            // Comms keeps its gesture authority alive while the drawer is folded or on another tab.
            if (toolsBuilt) MfdMapTools.Refresh(Folded ? -1 : SelectedTab);
        }

        internal static void RequestRefresh() { nextRead = 0f; layoutPending = true; }

        internal static void SelectTab(int index)
        {
            SelectedTab = Mathf.Clamp(index, 0, 2);
            SetFolded(false);
            if (root != null) { ShowPage(); RequestRefresh(); }
        }

        internal static void SetFolded(bool folded)
        {
            if (Folded == folded) return;
            Folded = folded;
            Footer?.SetHint(null);
            MfdRailPatch.ReLayoutNow();
            if (root != null) { Layout(); ShowPage(); RequestRefresh(); }
            MfdRail.Refresh();
        }

        internal static void Open()
        {
            SetFolded(false);
            if (root == null && MfdRailPatch.IsApplied) MfdRailPatch.ReLayoutNow();
            RequestRefresh();
            MfdRail.Refresh();
        }

        internal static bool TryOpen(string key, string page)
        {
            if (!MfdRailPatch.IsApplied || !string.Equals(key, "MAP", System.StringComparison.OrdinalIgnoreCase)) return false;
            Open();
            if (!string.IsNullOrEmpty(page))
                SelectTab(string.Equals(page, "LAYERS", System.StringComparison.OrdinalIgnoreCase) ? 1
                    : string.Equals(page, "VIEW", System.StringComparison.OrdinalIgnoreCase) ? 0 : 2);
            return true;
        }

        private static void Restyle()
        {
            frame?.Paint(AvInk.Ground.WithAlpha(.95f), AvInk.Frame.WithAlpha(.85f));
            if (header != null) header.color = AvInk.Raised.WithAlpha(.9f);
            if (rule != null) rule.color = AvInk.Frame.WithAlpha(.65f);
            if (title != null) title.color = AvInk.Key;
            if (status != null) status.color = AvInk.Dim;
            // Manually hosted controls have no AvFlow part to carry a theme refresh.
            // This traversal runs only when the stylesheet generation changes.
            if (root != null)
                foreach (AvControl control in root.GetComponentsInChildren<AvControl>(true)) control.Restyle();
            viewScroll?.Restyle();
        }

        internal static void Hide()
        {
            if (root != null) root.gameObject.SetActive(false);
        }

        internal static void Reset()
        {
            MfdMapTools.Reset();
            if (root != null)
            {
                MapUiPointer.Unregister(root);
                root.gameObject.SetActive(false);
                root.name = Name + ".Retired";
                Object.Destroy(root.gameObject);
            }
            root = body = view = layers = plot = null;
            SelectionSlot = ViewSlot = TheaterSlot = null;
            Footer = null;
            viewScroll = null;
            ticker = null;
            frame = null;
            header = rule = null;
            title = status = null;
            collapse = cancel = null;
            toolsBuilt = false;
            laidSize = Vector2.zero;
        }
    }
}
