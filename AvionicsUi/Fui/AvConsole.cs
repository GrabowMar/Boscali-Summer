using System;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
{
    /// <summary>
    /// The chrome every kit v2 console wears (spec §6.2): chamfered frame, header (id / title / page index),
    /// chips, metrics, icon tabs, paged scrolling body with a reserved gutter, footer. Owns its canvas split:
    /// the console root is a nested canvas, chips + metrics sit on a live child canvas, each page is its own
    /// canvas that is disabled (not deactivated) while hidden.
    /// </summary>
    public sealed class AvConsole
    {
        private readonly float width, height;
        private readonly AvFrame frame;
        private readonly Image headerBack, idPlate;
        private readonly TMP_Text idText, titleText, pageIndex;
        private readonly RectTransform live, bodyRect, viewport;
        private readonly ScrollRect scroll;
        private readonly Scrollbar scrollbar;
        private readonly RectTransform[] pageRects;
        private readonly Canvas[] pageCanvases;
        private readonly AvFlow[] flows;
        private readonly Image scanCover;
        private AvFx scanFx;
        private AvChip[] chips = new AvChip[0];
        private AvMetric[] metrics = new AvMetric[0];
        private AvTabBar tabs;

        public RectTransform Root { get; }
        public AvTicker Ticker { get; }
        public AvFooter Footer { get; }
        public int PageCount { get; }
        public int CurrentPage { get; private set; } = -1;
        public event Action<int> PageChanged;

        private AvConsole(RectTransform host, string id, string title, int pages, float w, float h)
        {
            width = w; height = h; PageCount = Math.Max(1, pages);
            Root = AvLay.Child(host, "AvConsole " + id);
            AvLay.Place(Root, 0f, 0f, w, h);
            AvLay.Nest(Root, true);
            Ticker = Root.gameObject.AddComponent<AvTicker>();

            frame = AvFrame.Add(Root, "Frame", AvChamfer.Diagonal(10f));
            AvLay.Fill(frame.rectTransform);
            headerBack = AvLay.Solid(Root, "Header", Color.clear);
            idPlate = AvLay.Solid(Root, "IdPlate", Color.clear);
            idText = AvText.Make(Root, "Id", AvTextRole.Head, id, TextAlignmentOptions.Center);
            titleText = AvText.Make(Root, "Title", AvTextRole.Head, title);
            pageIndex = AvText.Make(Root, "PageIndex", AvTextRole.DataSmall, "", TextAlignmentOptions.Center);

            live = AvLay.Child(Root, "Live");
            AvLay.Nest(live, false);

            bodyRect = AvLay.Child(Root, "Body");
            scroll = bodyRect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; scroll.scrollSensitivity = 24f;
            viewport = AvLay.Child(bodyRect, "Viewport");
            viewport.gameObject.AddComponent<RectMask2D>();
            scroll.viewport = viewport;
            scrollbar = MakeScrollbar(bodyRect);
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            AvInput.StripNavigation(scrollbar);

            pageRects = new RectTransform[PageCount];
            pageCanvases = new Canvas[PageCount];
            flows = new AvFlow[PageCount];
            for (int i = 0; i < PageCount; i++)
            {
                pageRects[i] = AvLay.Child(viewport, "Page " + (i + 1));
                pageCanvases[i] = AvLay.Nest(pageRects[i], true);
                flows[i] = new AvFlow(pageRects[i], Ticker, w);
            }

            scanCover = AvLay.Solid(bodyRect, "ScanCover", Color.clear);
            scanCover.enabled = false;

            Footer = new AvFooter(Root);
            Ticker.Register(Footer);
            Ticker.Add(-1, AvTickRate.Fast, EndScan);
            Restyle();
            Ticker.Register(new RestyleHook(this));
        }

        public static AvConsole Build(RectTransform host, string id, string title, int pages,
            float width = AvTokens.PanelWidth, float height = AvTokens.PanelHeight) =>
            new AvConsole(host, id, title, pages, width, height);

        public void SetTitle(string t) { if (titleText.text != t) titleText.text = t ?? ""; }

        public AvChip[] Chips(int count)
        {
            chips = new AvChip[Mathf.Clamp(count, 1, 3)];
            for (int i = 0; i < chips.Length; i++) { chips[i] = new AvChip(live); Ticker.Register(chips[i]); }
            Layout();
            return chips;
        }

        public AvMetric[] Metrics(params string[] keys)
        {
            int n = Mathf.Clamp(keys.Length, 1, 4);
            metrics = new AvMetric[n];
            for (int i = 0; i < n; i++) { metrics[i] = new AvMetric(live, keys[i]); Ticker.Register(metrics[i]); }
            Layout();
            return metrics;
        }

        public AvTabBar Tabs(params (AvIcon icon, string label)[] items)
        {
            tabs = new AvTabBar(Root, items, SetPage);
            Ticker.Register(tabs);
            Layout();
            return tabs;
        }

        public AvFlow Page(int index) => flows[Mathf.Clamp(index, 0, PageCount - 1)];

        public void SetPage(int index)
        {
            index = Mathf.Clamp(index, 0, PageCount - 1);
            for (int i = 0; i < PageCount; i++)
            {
                bool on = i == index;
                pageCanvases[i].enabled = on;
                var ray = pageRects[i].GetComponent<GraphicRaycaster>();
                if (ray != null) ray.enabled = on;
            }
            scroll.content = pageRects[index];
            scroll.verticalNormalizedPosition = 1f;
            Ticker.ActivePage = index;
            if (tabs != null && tabs.Selected != index) tabs.Select(index, false);
            pageIndex.text = (index + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "/" +
                             PageCount.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            bool changed = CurrentPage != index;
            CurrentPage = index;
            if (changed) { StartScan(); PageChanged?.Invoke(index); }
        }

        public void Finish()
        {
            Layout();
            foreach (AvFlow f in flows) f.Relayout();
            SetPage(CurrentPage < 0 ? 0 : CurrentPage);
        }

        private void Layout()
        {
            float pad = AvGridTokens.Pad, y = 0f, inner = width - 2f * pad;
            AvLay.Place(headerBack.rectTransform, 0f, 0f, width, AvGridTokens.Header);
            AvLay.Place(idPlate.rectTransform, 0f, 0f, 56f, AvGridTokens.Header);
            AvLay.Place(idText.rectTransform, 0f, 0f, 56f, AvGridTokens.Header);
            AvLay.Place(titleText.rectTransform, 66f, 0f, width - 66f - 64f, AvGridTokens.Header);
            AvLay.Place(pageIndex.rectTransform, width - 60f, 0f, 56f, AvGridTokens.Header);
            y += AvGridTokens.Header + 4f;

            AvLay.Place(live, 0f, 0f, width, height);
            if (chips.Length > 0)
            {
                float cw = AvFlowMath.ColumnWidth(inner, chips.Length, 4f);
                for (int i = 0; i < chips.Length; i++) chips[i].Place(new AvSlot(pad + i * (cw + 4f), y, cw, AvGridTokens.ChipStrip));
                y += AvGridTokens.ChipStrip + 6f;
            }
            if (metrics.Length > 0)
            {
                float mw = AvFlowMath.ColumnWidth(inner, metrics.Length, 4f);
                for (int i = 0; i < metrics.Length; i++) metrics[i].Place(new AvSlot(pad + i * (mw + 4f), y, mw, AvGridTokens.Metric));
                y += AvGridTokens.Metric + 6f;
            }
            if (tabs != null) { tabs.Place(new AvSlot(pad, y, inner, AvGridTokens.Tab)); y += AvGridTokens.Tab + 4f; }

            float footerH = Footer.Measure(width);
            Footer.Place(new AvSlot(0f, height - footerH, width, footerH));
            float bodyH = Mathf.Max(0f, height - footerH - y);
            AvLay.Place(bodyRect, 0f, y, width, bodyH);
            AvLay.Place(viewport, 0f, 0f, width, bodyH);
            AvLay.Place((RectTransform)scrollbar.transform, width - pad - AvGridTokens.Gutter + 2f, 2f, 4f, bodyH - 4f);
            AvLay.Place(scanCover.rectTransform, 0f, 0f, width, bodyH);
            for (int i = 0; i < PageCount; i++) { pageRects[i].anchoredPosition = Vector2.zero; }
        }

        private void StartScan()
        {
            if (AvFxPacking.Resolve(AvFxKind.Scan, AvFxDriver.Tier, AvFxDriver.ReducedMotion) == AvFxKind.None) return;
            if (scanFx == null) scanFx = AvFx.On(scanCover);
            if (AvFxDriver.FxMaterial == null) return;
            scanCover.color = AvStyleHost.FuiColor("ground", AvTheme.Ground);
            scanCover.enabled = true;
            scanFx.Play(AvFxKind.Scan, 0.9f, 1f); // param 1 = cover mode (P0 shader)
            scanUntil = Time.unscaledTime + 0.3f;
        }

        private float scanUntil;

        private void EndScan()
        {
            if (scanCover.enabled && Time.unscaledTime >= scanUntil) scanCover.enabled = false;
        }

        private void Restyle()
        {
            AvStyle c = AvStyleHost.FuiStyle("console");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Ground), AvStyleHost.Resolve(c.Border, AvTheme.Frame));
            AvStyle h = AvStyleHost.FuiStyle("header");
            headerBack.color = AvStyleHost.Resolve(h.Background, AvTheme.Surface);
            AvStyle p = AvStyleHost.FuiStyle("id-plate");
            idPlate.color = AvStyleHost.Resolve(p.Background, AvTheme.SurfaceRaised);
            idText.color = AvStyleHost.Resolve(p.Color, AvTheme.TextPrimary);
            titleText.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("title").Color, AvTheme.TextPrimary);
            pageIndex.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("page-index").Color, AvTheme.Dim);
            scrollbar.GetComponent<Image>().color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("scrollbar").Background, AvTheme.Hairline);
            scrollbar.handleRect.GetComponent<Image>().color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("scrollbar-thumb").Background, AvTheme.Frame);
        }

        private static Scrollbar MakeScrollbar(RectTransform parent)
        {
            RectTransform bar = AvLay.Child(parent, "Scrollbar");
            var track = bar.gameObject.AddComponent<Image>();
            track.raycastTarget = true;
            RectTransform area = AvLay.Child(bar, "Area"); AvLay.Fill(area);
            RectTransform handle = AvLay.Child(area, "Handle"); AvLay.Fill(handle);
            handle.gameObject.AddComponent<Image>();
            var sb = bar.gameObject.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.handleRect = handle;
            sb.targetGraphic = handle.GetComponent<Image>();
            return sb;
        }

        private sealed class RestyleHook : AvPart
        {
            private readonly AvConsole c;
            public RestyleHook(AvConsole console) { c = console; }
            public override void Restyle() => c.Restyle();
            public override void Place(AvSlot slot) { }
        }
    }
}
