using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>
    /// Client look options for every console (SET > PANELS > CONSOLE). The host mod writes them and calls <see cref="Changed"/>;
    /// each console picks the change up on its own slow tick (an int compare), so a setting costs nothing per frame.
    /// </summary>
    public static class AvConsoleLook
    {
        public static bool Grid = true, Orders = true, Clock = true, HoverHelp = true, Art = true;
        public static float Opacity = 1f;
        public static int Revision { get; private set; }
        public static void Changed() => Revision++;
    }

    /// <summary>
    /// The chrome every kit v2 console wears (spec §6.2): square frame, header (id / title / page index),
    /// chips, metrics, icon tabs, paged scrolling body with an edge scrollbar, footer. Owns its canvas split:
    /// the console root is a nested canvas, chips + metrics sit on a live child canvas, each page is its own
    /// canvas that is disabled (not deactivated) while hidden.
    /// </summary>
    public sealed class AvConsole
    {
        private readonly float width;
        private float height;
        private readonly AvFrame frame;
        private readonly Image headerBack, idPlate, idRail;
        private readonly TMP_Text idText, titleText;
        private readonly RectTransform live, bodyRect, viewport;
        private readonly CanvasGroup opacity;
        private readonly AvScrollView scrollView;
        private readonly RectTransform[] pageRects;
        private readonly Canvas[] pageCanvases;
        private readonly AvFlow[] flows;
        private readonly Image scanCover;
        private readonly string name;
        private string pageTitle;
        private string[] pageNames;
        private AvFx scanFx;
        private AvChip[] chips = new AvChip[0];
        private bool chipsShown = true;
        private AvMetric[] metrics = new AvMetric[0];
        private AvTabBar tabs;
        private AvPart headerBand;
        private readonly AvGroundGraphic graticule;
        private readonly RawImage consoleArt;
        private float bodyHeight;
        private readonly string[][] orders;
        private readonly string id;
        // Hidden consoles keep only their current page active, so coming back on screen regenerates one page of
        // text and graphics instead of all of them; the rest wake one per fast tick behind their disabled canvases.
        private static readonly System.Collections.Generic.List<AvConsole> all = new System.Collections.Generic.List<AvConsole>(16);
        private bool parked;
        private int lookRevision = -1;

        public RectTransform Root { get; }
        public AvTicker Ticker { get; }
        public AvFooter Footer { get; }
        public int PageCount { get; }
        public int CurrentPage { get; private set; } = -1;
        /// <summary>The open tab's label, null without tabs.</summary>
        public string CurrentPageName => pageNames != null && CurrentPage >= 0 ? pageNames[CurrentPage] : null;
        public event Action<int> PageChanged;

        private AvConsole(RectTransform host, string id, string title, int pages, float w, float h)
        {
            width = w; height = h; PageCount = Math.Max(1, pages);
            this.id = id ?? "";
            name = title ?? "";
            if (this.id.Length > 0) byId[this.id] = this;
            orders = new string[PageCount][];
            Root = AvLay.Child(host, "AvConsole " + id);
            AvLay.Place(Root, 0f, 0f, w, h);
            AvLay.Nest(Root, true);
            Ticker = Root.gameObject.AddComponent<AvTicker>();
            Ticker.Console = this;

            // Opacity belongs to the glass. Keep text, hover feedback and controls at full contrast.
            var surface = AvLay.Child(Root, "Surface");
            AvLay.Fill(surface);
            opacity = surface.gameObject.AddComponent<CanvasGroup>();
            opacity.blocksRaycasts = false;
            frame = AvFrame.Add(surface, "Frame", default);
            AvLay.Fill(frame.rectTransform);
            // Frosted glass over the tint: the blurred world bleeds through a little, lit at the top (nothing without blur).
            AvFrost.Add(surface, "Frost", .14f, 1f);
            headerBack = AvLay.Solid(Root, "Header", Color.clear);
            idPlate = AvLay.Solid(Root, "IdPlate", Color.clear);
            idRail = AvLay.Solid(Root, "IdRail", Color.clear);
            idText = AvText.Make(Root, "Id", AvTextRole.DataStrong, id, TextAlignmentOptions.Center);
            AvText.Fit(idText, false);
            titleText = AvText.Make(Root, "Title", AvTextRole.Title, title);
            AvText.Fit(titleText, false);

            live = AvLay.Child(Root, "Live");
            AvLay.Nest(live, true);

            bodyRect = AvLay.Child(Root, "Body");
            scrollView = new AvScrollView(bodyRect, false);
            viewport = scrollView.Viewport;
            var gridGo = new GameObject("Graticule", typeof(RectTransform), typeof(CanvasRenderer));
            gridGo.transform.SetParent(viewport, false);
            graticule = gridGo.AddComponent<AvGroundGraphic>();
            graticule.raycastTarget = false;
            // Both drawings live in a measured region inside the viewport, clear of the last row.
            Texture2D picture = AvBundle.ConsoleArt(this.id);
            if (picture != null)
            {
                var artGo = new GameObject("Art", typeof(RectTransform), typeof(CanvasRenderer));
                artGo.transform.SetParent(graticule.transform, false);
                consoleArt = artGo.AddComponent<RawImage>();
                consoleArt.texture = picture;
                consoleArt.raycastTarget = false;
                consoleArt.enabled = false;
            }

            pageRects = new RectTransform[PageCount];
            pageCanvases = new Canvas[PageCount];
            flows = new AvFlow[PageCount];
            for (int i = 0; i < PageCount; i++)
            {
                pageRects[i] = AvLay.Child(viewport, "Page " + (i + 1));
                pageCanvases[i] = AvLay.Nest(pageRects[i], true);
                flows[i] = new AvFlow(pageRects[i], Ticker, w);
                flows[i].HeightChanged += SeatGround;
            }

            scanCover = AvLay.Solid(Root, "HeaderSweep", Color.clear);
            scanCover.enabled = false;

            Footer = new AvFooter(Root);
            // Its own canvas: the clock and hover help change the strip every second, and that must not rebatch the whole console.
            AvLay.Nest(Footer.Rect, false);
            Root.gameObject.AddComponent<AvHelpScope>().Footer = Footer;
            Ticker.Register(Footer);
            Ticker.Frame = Animate;
            Ticker.Add(-1, AvTickRate.Slow, Footer.Tick);
            Ticker.Add(-1, AvTickRate.Slow, RefreshLook);
            Ticker.Add(-1, AvTickRate.Fast, Unpark);
            Ticker.Shown = Wake;
            if (Application.isPlaying && PageCount > 1) { all.Add(this); AvFxDriver.EnsureRunning(); }
            Restyle();
            Ticker.Register(new AvRestyleHook(Restyle));
        }

        public static AvConsole Build(RectTransform host, string id, string title, int pages,
            float width = AvTokens.PanelWidth, float height = AvTokens.PanelHeight) =>
            new AvConsole(host, id, title, pages, width, height);

        /// <summary>Overrides the header title; null or empty goes back to the console's name.</summary>
        public void SetTitle(string t) { pageTitle = t; ShowTitle(); }

        // The header says which console this is, once: the id plate and the rail key's own words (the host mod supplies them). The lit
        // tab names the page, so the header neither repeats it nor counts pages. A console whose name is its id shows the plate alone.
        private void ShowTitle()
        {
            string t = !string.IsNullOrEmpty(pageTitle) ? pageTitle : KeyName?.Invoke(id) ?? name;
            AvText.Set(titleText, t);
            bool showId = id.Length > 0 && !string.Equals(t, id, StringComparison.OrdinalIgnoreCase);
            idPlate.enabled = idText.enabled = showId;
            float left = showId ? 76f : 14f;
            AvLay.Place(titleText.rectTransform, left, 0f, Mathf.Max(0f, width - left - AvGridTokens.Pad), AvGridTokens.Header);
        }

        // ---- rail wiring: a console can be opened on a page by key (menus, automation) ------------------------------------

        /// <summary>The words on a rail key for a console id ("MIS" -> "MISSIONS"); null keeps the console's own name.</summary>
        public static Func<string, string> KeyName;

        /// <summary>Opens the screen behind a rail key (no-op when it is already open); false when no such key is up.</summary>
        public static Func<string, bool> PressKey;

        private static readonly System.Collections.Generic.Dictionary<string, AvConsole> byId =
            new System.Collections.Generic.Dictionary<string, AvConsole>(16);

        private static readonly System.Collections.Generic.Dictionary<string, string> pendingPage =
            new System.Collections.Generic.Dictionary<string, string>(4);

        // ponytail: one console per id; SET's CLIENT and SERVER consoles share "SET", so the newer one answers. Key by mode if that bites.
        public static AvConsole Find(string id) =>
            id != null && byId.TryGetValue(id, out AvConsole c) && c.Root != null ? c : null;

        /// <summary>Opens a rail key and, when <paramref name="page"/> names one of its tabs, that page. A console not built yet takes the page when it finishes.</summary>
        public static void Open(string key, string page = null)
        {
            if (string.IsNullOrEmpty(key)) return;
            PressKey?.Invoke(key);
            if (string.IsNullOrEmpty(page)) return;
            AvConsole c = Find(key);
            if (c == null) pendingPage[key] = page;
            else c.ShowPage(page);
        }

        /// <summary>Selects the tab labelled <paramref name="page"/> (any case), as a press on it would; false when there is none.</summary>
        public bool ShowPage(string page)
        {
            if (pageNames == null || string.IsNullOrEmpty(page)) return false;
            for (int i = 0; i < pageNames.Length; i++)
            {
                if (!string.Equals(pageNames[i], page, StringComparison.OrdinalIgnoreCase)) continue;
                if (tabs != null) tabs.Select(i); else SetPage(i);
                return true;
            }
            return false;
        }

        public AvChip[] Chips(int count)
        {
            chips = new AvChip[Mathf.Clamp(count, 1, 3)];
            for (int i = 0; i < chips.Length; i++) { chips[i] = new AvChip(live); Ticker.Register(chips[i]); }
            Layout();
            return chips;
        }

        public void SetChipsShown(bool shown)
        {
            if (chipsShown == shown) return;
            chipsShown = shown;
            Layout();
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
            tabs.HeightChanged += Layout;
            if (items.Length == PageCount)
            {
                pageNames = new string[items.Length];
                for (int i = 0; i < items.Length; i++) pageNames[i] = items[i].label ?? "";
            }
            Ticker.Register(tabs);
            Layout();
            return tabs;
        }

        public AvFlow Page(int index) => flows[Mathf.Clamp(index, 0, PageCount - 1)];

        public float SurfaceOpacity => opacity.alpha;

        /// <summary>A persistent control strip below the console title, measured at the same width as its tabs.</summary>
        public void HeaderBand(AvPart band)
        {
            headerBand = band;
            Ticker.Register(band);
            Layout();
        }

        /// <summary>Fits the console to its host's available height without changing its width, selected page or data.</summary>
        public void SetHeight(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return;
            value = Mathf.Max(0f, value);
            if (Mathf.Abs(value - height) < .5f) return;
            Vector2 offset = CurrentPage >= 0 ? pageRects[CurrentPage].anchoredPosition : Vector2.zero;
            height = value;
            Root.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            Layout();
            if (CurrentPage >= 0 && Root.gameObject.activeInHierarchy) flows[CurrentPage].Relayout();
            scrollView.Scroll.StopMovement();
            if (CurrentPage >= 0)
            {
                offset.y = Mathf.Clamp(offset.y, 0f, Mathf.Max(0f, flows[CurrentPage].ContentHeight - bodyHeight));
                pageRects[CurrentPage].anchoredPosition = offset;
            }
            SeatGround();
        }

        /// <summary>
        /// Standing orders for a page: two to four short usage notes in the unit's own voice, most important first. The footer
        /// shows them one at a time under the status line while that page is up (see <see cref="AvFooter.SetOrders"/>).
        /// </summary>
        public void Orders(int page, params string[] lines)
        {
            if (page < 0 || page >= PageCount) return;
            orders[page] = lines;
            if (page == CurrentPage) Footer.SetOrders(lines);
        }

        public void SetPage(int index)
        {
            index = Mathf.Clamp(index, 0, PageCount - 1);
            if (!pageRects[index].gameObject.activeSelf) pageRects[index].gameObject.SetActive(true);
            for (int i = 0; i < PageCount; i++)
            {
                bool on = i == index;
                pageCanvases[i].enabled = on;
                var ray = pageRects[i].GetComponent<GraphicRaycaster>();
                if (ray != null) ray.enabled = on;
            }
            scrollView.Scroll.content = pageRects[index];
            Ticker.ActivePage = index;
            if (tabs != null && tabs.Selected != index) tabs.Select(index, false);
            bool changed = CurrentPage != index;
            CurrentPage = index;
            ShowTitle();
            if (changed) { Footer.SetOrders(orders[index]); PageChanged?.Invoke(index); }
            // Flush deferred layout after page callbacks, including a same-page reopen.
            flows[CurrentPage].Relayout();
            scrollView.Scroll.StopMovement();
            scrollView.Scroll.verticalNormalizedPosition = 1f;
            SeatGround();
        }

        private void RefreshLook()
        {
            if (lookRevision == AvConsoleLook.Revision) return;
            lookRevision = AvConsoleLook.Revision;
            Restyle();
            Footer.Relook();
        }

        // A short page ends in a small engraved band. Long pages give every pixel to scrolling content.
        private void SeatGround()
        {
            if (CurrentPage < 0) return;
            float room = bodyHeight - flows[CurrentPage].ContentHeight - 20f;
            bool visible = room >= 44f && (AvConsoleLook.Grid || (consoleArt != null && AvConsoleLook.Art));
            RectTransform band = graticule.rectTransform;
            band.gameObject.SetActive(visible);
            if (!visible) return;
            float h = Mathf.Min(room, 104f);
            float w = Mathf.Max(0f, width - 2f * AvGridTokens.Pad);
            AvLay.Place(band, AvGridTokens.Pad, bodyHeight - h - 8f, w, h);
            graticule.enabled = AvConsoleLook.Grid;
            if (consoleArt == null) return;
            consoleArt.enabled = AvConsoleLook.Art;
            float aspect = (float)consoleArt.texture.width / consoleArt.texture.height;
            float aw = Mathf.Min(w * .60f, h * aspect);
            AvLay.Place(consoleArt.rectTransform, w - aw, h - aw / aspect, aw, aw / aspect);
        }

        public void Finish()
        {
            Layout();
            foreach (AvFlow f in flows) f.Relayout();
            SetPage(CurrentPage < 0 ? 0 : CurrentPage);
            if (pendingPage.TryGetValue(id, out string wanted)) { pendingPage.Remove(id); ShowPage(wanted); }
        }

        private void Layout()
        {
            float pad = AvGridTokens.Pad, y = 0f, inner = width - 2f * pad;
            AvLay.Place(headerBack.rectTransform, 0f, 0f, width, AvGridTokens.Header);
            AvLay.Place(idPlate.rectTransform, 12f, 6f, 54f, AvGridTokens.Header - 12f);
            AvLay.Place(idText.rectTransform, 12f, 6f, 54f, AvGridTokens.Header - 12f);
            AvLay.Place(idRail.rectTransform, 0f, 5f, 3f, AvGridTokens.Header - 10f);
            ShowTitle();
            y += AvGridTokens.Header + 6f;

            AvLay.Place(live, 0f, 0f, width, height);
            if (headerBand != null)
            {
                float bandH = headerBand.Measure(inner);
                headerBand.Place(new AvSlot(pad, y, inner, bandH));
                y += bandH + 4f;
            }
            if (tabs != null)
            {
                bool redundant = PageCount == 1 && pageNames != null &&
                    (string.Equals(pageNames[0], name, StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(pageNames[0], id, StringComparison.OrdinalIgnoreCase));
                tabs.Rect.gameObject.SetActive(!redundant);
                if (!redundant)
                {
                    float th = tabs.Measure(inner);
                    tabs.Place(new AvSlot(pad, y, inner, th));
                    y += th + 5f;
                }
            }
            foreach (AvChip chip in chips) chip.Rect.gameObject.SetActive(chipsShown);
            if (chipsShown && chips.Length > 0)
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

            float footerH = Footer.Measure(width);
            Footer.Place(new AvSlot(0f, height - footerH, width, footerH));
            float bodyH = Mathf.Max(0f, height - footerH - y);
            AvLay.Place(bodyRect, 0f, y, width, bodyH);
            bodyHeight = bodyH;
            scrollView.Place(width, bodyH);
            foreach (AvFlow f in flows) f.ViewportHeight = bodyH;
            AvLay.Place(scanCover.rectTransform, 0f, 0f, width, AvGridTokens.Header);
            SeatGround();
        }

        /// <summary>Parks the off-screen pages of every console that is currently hidden (the FX driver calls this each frame).</summary>
        internal static void ParkHidden()
        {
            for (int i = all.Count - 1; i >= 0; i--)
            {
                AvConsole c = all[i];
                if (c.Root == null) { all.RemoveAt(i); continue; }
                if (c.parked || c.CurrentPage < 0 || c.Root.gameObject.activeInHierarchy) continue;
                c.parked = true;
                for (int p = 0; p < c.PageCount; p++)
                    if (p != c.CurrentPage) c.pageRects[p].gameObject.SetActive(false);
            }
        }

        private void Unpark()
        {
            if (!parked) return;
            for (int i = 0; i < PageCount; i++)
            {
                if (pageRects[i].gameObject.activeSelf) continue;
                pageRects[i].gameObject.SetActive(true);
                return;
            }
            parked = false;
        }

        private void StartScan()
        {
            if (AvFxPacking.Resolve(AvFxKind.Scan, AvFxDriver.Tier, AvFxDriver.ReducedMotion) == AvFxKind.None) return;
            if (AvFxDriver.FxMaterial == null) return;
            if (scanFx == null) scanFx = AvFx.On(scanCover);
            scanCover.color = AvStyleHost.FuiBorder("id-plate", AvInk.Select).WithAlpha(.22f);
            scanCover.enabled = true;
            scanFx.Play(AvFxKind.Scan, .3f); // non-cover mode: the body and header text remain readable
            scanUntil = Time.unscaledTime + AvFxPacking.DurationOf(AvFxKind.Scan);
        }

        private float scanUntil;

        private void Animate()
        {
            if (!scanCover.enabled) return;
            if (Time.unscaledTime >= scanUntil ||
                AvFxPacking.Resolve(AvFxKind.Scan, AvFxDriver.Tier, AvFxDriver.ReducedMotion) == AvFxKind.None)
            {
                scanCover.enabled = false;
                scanFx?.Clear();
            }
        }

        // Opening feedback is confined to the header; no faux boot sequence delays the actual page.
        private void Wake() => StartScan();

        private void Restyle()
        {
            opacity.alpha = Mathf.Clamp(AvConsoleLook.Opacity, .1f, 1f);
            AvStyle c = AvStyleHost.FuiStyle("console");
            frame.Paint(AvStyleHost.Resolve(c.Background, AvTheme.Ground), AvStyleHost.Resolve(c.Border, AvTheme.Frame));
            AvStyle h = AvStyleHost.FuiStyle("header");
            headerBack.color = AvStyleHost.Resolve(h.Background, AvTheme.Surface);
            AvStyle p = AvStyleHost.FuiStyle("id-plate");
            idPlate.color = AvStyleHost.Resolve(p.Background, AvTheme.SurfaceRaised);
            idText.color = AvStyleHost.Resolve(p.Color, AvTheme.TextPrimary);
            titleText.color = AvStyleHost.FuiInk("title", AvTheme.TextPrimary);
            idRail.color = AvStyleHost.FuiBorder("id-plate", AvInk.Select);
            Color grid = AvStyleHost.FuiFill("console-ground", AvTheme.Hairline.WithAlpha(0.3f));
            graticule.color = grid.WithAlpha(AvBundle.OverlayAlpha(grid.a));
            if (consoleArt != null)
            {
                Color ink = AvStyleHost.FuiInk("console-ground", AvTheme.RailInfo.WithAlpha(.32f));
                consoleArt.color = ink.WithAlpha(AvBundle.OverlayAlpha(ink.a));
            }
            SeatGround();
            scrollView.Restyle();
        }
    }
}
