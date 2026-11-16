using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The former top-of-map selection, camera and theater controls, gathered below the viewport.
    /// MAP's Tools and Layers remain in the left console. Folding this strip preserves its scroll position.
    /// </summary>
    internal static class MfdMapControls
    {
        internal const string Name = "NOAvionics.MapControls";
        internal const float HeaderHeight = 28f;
        internal const float RowHeight = 28f, RowPitch = 32f;
        private const float Pad = 6f, Gap = 12f, ContentHeight = RowPitch * 2f + RowHeight, ScrollbarHeight = 8f;
        private const float ExpandedHeight = HeaderHeight + Pad * 2f + ContentHeight + ScrollbarHeight;
        private static RectTransform root, body, view;
        private static AvScrollView viewScroll;
        private static AvTicker ticker;
        private static AvFrame frame;
        private static Image header, rule;
        private static TMP_Text title, status;
        private static AvControl collapse;
        private static Vector2 laidSize;
        private static bool laidFolded, layoutPending;
        private static float nextRead;

        internal static RectTransform Root => root;
        internal static RectTransform SelectionSlot { get; private set; }
        internal static RectTransform ViewSlot { get; private set; }
        internal static RectTransform TheaterSlot { get; private set; }
        internal static AvFooter Footer { get; private set; }
        internal static bool Folded { get; private set; }
        internal static bool Shown => root != null && root.gameObject.activeInHierarchy;

        internal static float Height(float canvasHeight) => Folded ? HeaderHeight : ExpandedHeight;

        internal static void Ensure(Canvas canvas, MfdLayout.Columns columns)
        {
            if (canvas == null) return;
            if (root == null || root.parent != canvas.transform) Build(canvas);
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
            AvText.Size(title, 15f);
            title.characterSpacing = 1f;
            title.raycastTarget = false;
            AvText.Fit(title, false);
            status = AvText.Make(root, "Status", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            status.raycastTarget = false;
            AvText.Fit(status, false);
            status.overflowMode = TextOverflowModes.Ellipsis;
            collapse = AvControl.Make(root, new AvControl.Spec("", () => SetFolded(!Folded), AvButtonStyle.Quiet, AvIcon.ChevronDown));
            collapse.Rect.name = "Collapse";
            collapse.Bracket = 0f;
            collapse.Help = "Collapse map controls to return their space to the map. The active tool stays armed.";
            rule = AvLay.Solid(root, "HeaderRule", Color.clear);
            rule.raycastTarget = false;

            body = AvLay.Child(root, "Body");
            view = AvLay.Child(body, "View");
            viewScroll = new AvScrollView(view, true);
            // Keep three readable rows even when the panel column leaves a narrow map.
            viewScroll.Scroll.horizontal = true;
            viewScroll.Scroll.vertical = false;
            viewScroll.Scroll.verticalScrollbar = null;
            viewScroll.Scroll.horizontalScrollbar = viewScroll.Bar;
            viewScroll.Scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            viewScroll.Bar.direction = Scrollbar.Direction.LeftToRight;
            AvLay.Fill((RectTransform)viewScroll.Bar.transform.Find("Track"));
            AvLay.Fill((RectTransform)viewScroll.Bar.transform.Find("Area/Handle/Thumb"));
            SelectionSlot = AvLay.Child(viewScroll.Content, "SelectionSlot");
            ViewSlot = AvLay.Child(viewScroll.Content, "ViewSlot");
            TheaterSlot = AvLay.Child(viewScroll.Content, "TheaterSlot");
            Footer = new AvFooter(viewScroll.Content);
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
            AvLay.Place(title.rectTransform, 10f, 0f, 122f, HeaderHeight);
            AvLay.Place(collapse.Rect, width - 34f, 2f, 28f, HeaderHeight - 4f);
            AvLay.Place(rule.rectTransform, 8f, HeaderHeight - 1f, width - 16f, 1f);
            LayoutHeader();
            float inner = Mathf.Max(1f, width - Pad * 2f);
            float height = ContentHeight + ScrollbarHeight;
            AvLay.Place(body, Pad, HeaderHeight + Pad, inner, height);
            AvLay.Fill(view);
            viewScroll.Place(inner, height);
            AvLay.Place((RectTransform)viewScroll.Bar.transform, 0f, ContentHeight, inner, ScrollbarHeight);
            AvLay.Place(viewScroll.Viewport, 0f, 0f, inner, ContentHeight);
            float contentWidth = Mathf.Max(1040f, inner);
            float selectionWidth = Mathf.Clamp((contentWidth - Gap) * .52f, 520f, 650f);
            float viewX = selectionWidth + Gap;
            AvLay.Place(SelectionSlot, 0f, 0f, selectionWidth, RowPitch + RowHeight);
            AvLay.Place(ViewSlot, viewX, 0f, contentWidth - viewX, RowPitch + RowHeight);
            AvLay.Place(TheaterSlot, 0f, RowPitch * 2f, selectionWidth, RowHeight);
            Footer.Place(new AvSlot(viewX, RowPitch * 2f + 2f, contentWidth - viewX, RowHeight - 4f));
            float offset = viewScroll.Content.anchoredPosition.x;
            AvLay.Place(viewScroll.Content, 0f, 0f, contentWidth, ContentHeight);
            viewScroll.Content.anchoredPosition = new Vector2(Mathf.Clamp(offset, Mathf.Min(0f, inner - contentWidth), 0f), 0f);
            collapse.SetIcon(Folded ? AvIcon.ChevronUp : AvIcon.ChevronDown);
        }

        private static void LayoutHeader()
        {
            if (root == null) return;
            float x = 148f;
            float right = root.sizeDelta.x - 46f;
            AvLay.Place(status.rectTransform, x, 0f, Mathf.Max(0f, right - x), HeaderHeight);
            status.enabled = right - x >= 70f;
        }

        private static void ShowPage()
        {
            body.gameObject.SetActive(!Folded);
            Footer.Rect.gameObject.SetActive(!Folded);
            rule.gameObject.SetActive(!Folded);
        }

        private static void Tick()
        {
            if (!Shown || Time.unscaledTime < nextRead) return;
            nextRead = Time.unscaledTime + .15f;
            if (layoutPending) { layoutPending = false; Layout(); }
            string prompt = MapPicker.Prompt;
            int count = SceneSingleton<DynamicMap>.i?.selectedIcons?.Count ?? 0;
            AvText.Set(status, !string.IsNullOrEmpty(prompt) ? prompt : count + (count == 1 ? " TRACK SELECTED" : " TRACKS SELECTED"));
            status.color = MapPicker.IsBusy ? AvInk.State(AvState.Caution) : AvInk.Dim;
            string viewStatus = !Folded ? MapSelectionDeck.StatusLine : null;
            if (!Folded)
                Footer.Set(!string.IsNullOrEmpty(prompt) ? prompt : !string.IsNullOrEmpty(viewStatus) ? viewStatus : MfdMapInteractions.Feedback ??
                    "LEFT: SELECT / PAN   ·   WHEEL: ZOOM   ·   RIGHT: TOOL / ORDERS", MapPicker.IsBusy ? AvState.Caution : AvState.Inert);
        }

        internal static void RequestRefresh() { nextRead = 0f; layoutPending = true; }

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
            if (root != null)
            {
                MapUiPointer.Unregister(root);
                root.gameObject.SetActive(false);
                root.name = Name + ".Retired";
                Object.Destroy(root.gameObject);
            }
            root = body = view = null;
            SelectionSlot = ViewSlot = TheaterSlot = null;
            Footer = null;
            viewScroll = null;
            ticker = null;
            frame = null;
            header = rule = null;
            title = status = null;
            collapse = null;
            laidSize = Vector2.zero;
        }
    }
}
