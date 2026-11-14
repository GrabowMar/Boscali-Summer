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
        public static bool Grid = true, Orders = true, Clock = true, HoverHelp = true;
        public static int Revision { get; private set; }
        public static void Changed() => Revision++;
    }

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
        private readonly AvScrollView scrollView;
        private readonly RectTransform[] pageRects;
        private readonly Canvas[] pageCanvases;
        private readonly AvFlow[] flows;
        private readonly Image scanCover;
        private readonly AvDecorGraphic decor;
        private readonly string name;
        private string pageTitle;
        private string[] pageNames;
        private AvFx scanFx;
        private AvChip[] chips = new AvChip[0];
        private AvMetric[] metrics = new AvMetric[0];
        private AvTabBar tabs;
        private readonly RawImage groundArt;
        private readonly AvGroundGraphic graticule;
        private readonly string[][] orders;
        private readonly TMP_Text bootText;
        private readonly string id;
        private int bootLine = -1;
        private float nextBootLine;
        private bool booted;
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

            frame = AvFrame.Add(Root, "Frame", AvChamfer.Diagonal(10f));
            AvLay.Fill(frame.rectTransform);
            // Ground: matte terminal glass (one shared texture, tinted per scheme) inside the frame, under everything else.
            Texture2D contours = AvBundle.Ground;
            if (contours != null)
            {
                var groundGo = new GameObject("Ground", typeof(RectTransform), typeof(CanvasRenderer));
                groundGo.transform.SetParent(Root, false);
                AvLay.Fill((RectTransform)groundGo.transform, 1f);
                groundArt = groundGo.AddComponent<RawImage>();
                groundArt.texture = contours;
                groundArt.raycastTarget = false;
                // Crop to the console's aspect, centred, never stretched.
                float art = (float)contours.width / contours.height, box = w / Mathf.Max(1f, h);
                groundArt.uvRect = box > art ? new Rect(0f, .5f - art / box * .5f, 1f, art / box) : new Rect(.5f - box / art * .5f, 0f, box / art, 1f);
            }
            headerBack = AvLay.Solid(Root, "Header", Color.clear);
            idPlate = AvLay.Solid(Root, "IdPlate", Color.clear);
            AvSurfaceGradient.Apply(headerBack, Color.white, new Color(.65f, .71f, .74f, 1f));
            AvSurfaceGradient.Apply(idPlate, Color.white, new Color(.65f, .71f, .74f, 1f));
            idText = AvText.Make(Root, "Id", AvTextRole.Head, id, TextAlignmentOptions.Center);
            titleText = AvText.Make(Root, "Title", AvTextRole.Head, title);
            titleText.fontSize = 15f;
            titleText.characterSpacing = 5f;
            AvText.Fit(titleText, false);
            // Right end: the console's name (the rail key's words) and the page count, "MISSIONS  01/03".
            pageIndex = AvText.Make(Root, "PageIndex", AvTextRole.DataSmall, "", TextAlignmentOptions.MidlineRight);
            AvText.Fit(pageIndex, false);

            live = AvLay.Child(Root, "Live");
            AvLay.Nest(live, false);

            bodyRect = AvLay.Child(Root, "Body");
            var gridGo = new GameObject("Graticule", typeof(RectTransform), typeof(CanvasRenderer));
            gridGo.transform.SetParent(bodyRect, false);   // first child: pages draw over it
            AvLay.Fill((RectTransform)gridGo.transform);
            graticule = gridGo.AddComponent<AvGroundGraphic>();
            graticule.raycastTarget = false;
            scrollView = new AvScrollView(bodyRect, false);
            viewport = scrollView.Viewport;

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
            bootText = AvText.Make(bodyRect, "Boot", AvTextRole.DataSmall, "", TextAlignmentOptions.TopLeft);
            bootText.enabled = false;

            var decorGo = new GameObject("Decor", typeof(RectTransform), typeof(CanvasRenderer));
            decorGo.transform.SetParent(Root, false);
            AvLay.Fill((RectTransform)decorGo.transform);
            decor = decorGo.AddComponent<AvDecorGraphic>();
            decor.raycastTarget = false;

            Footer = new AvFooter(Root);
            // Its own canvas: the clock and hover help change the strip every second, and that must not rebatch the whole console.
            AvLay.Nest(Footer.Rect, false);
            Root.gameObject.AddComponent<AvHelpScope>().Footer = Footer;
            Ticker.Register(Footer);
            Ticker.Frame = Animate;
            Ticker.Add(-1, AvTickRate.Slow, Footer.Tick);
            Ticker.Add(-1, AvTickRate.Slow, SeatGround);
            Ticker.Add(-1, AvTickRate.Fast, Unpark);
            Ticker.Shown = Wake;
            if (Application.isPlaying && PageCount > 1) { all.Add(this); AvFxDriver.EnsureRunning(); }
            Restyle();
            Ticker.Register(new AvRestyleHook(Restyle));
        }

        public static AvConsole Build(RectTransform host, string id, string title, int pages,
            float width = AvTokens.PanelWidth, float height = AvTokens.PanelHeight) =>
            new AvConsole(host, id, title, pages, width, height);

        /// <summary>Overrides the page title (the left of the header); null or empty goes back to the open tab's label.</summary>
        public void SetTitle(string t) { pageTitle = t; ShowTitle(); }

        // Left: the page (the open tab's label unless overridden). Right: the console's name, which is the rail key's own words
        // when the host mod supplies them, and the page count.
        private void ShowTitle()
        {
            string t = !string.IsNullOrEmpty(pageTitle) ? pageTitle
                : pageNames != null && CurrentPage >= 0 ? pageNames[CurrentPage] : name;
            if (titleText.text != t) titleText.text = t;
            string n = (KeyName?.Invoke(id) ?? name) + "  " +
                       (Math.Max(0, CurrentPage) + 1).ToString("00", System.Globalization.CultureInfo.InvariantCulture) + "/" +
                       PageCount.ToString("00", System.Globalization.CultureInfo.InvariantCulture);
            if (pageIndex.text != n) pageIndex.text = n;
        }

        // ---- rail wiring: every console can be opened on a page from any other (link chips) ------------------------------------

        /// <summary>The words on a rail key for a console id ("MIS" -> "MISSIONS"); null keeps the console's own name.</summary>
        public static Func<string, string> KeyName;

        /// <summary>Opens the screen behind a rail key (no-op when it is already open); false when no such key is up.</summary>
        public static Func<string, bool> PressKey;

        private static readonly System.Collections.Generic.Dictionary<string, AvConsole> byId =
            new System.Collections.Generic.Dictionary<string, AvConsole>(16);
        /// <summary>Every "KEY|PAGE" a link chip targets (filled by <see cref="AvFlow.Links"/>), for the dev link audit.</summary>
        public static readonly System.Collections.Generic.HashSet<string> LinkTargets = new System.Collections.Generic.HashSet<string>();

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
            if (changed) { StartScan(); Footer.SetOrders(orders[index]); PageChanged?.Invoke(index); }
            // Flush deferred layout after page callbacks, including a same-page reopen.
            flows[CurrentPage].Relayout();
            scrollView.Scroll.StopMovement();
            scrollView.Scroll.verticalNormalizedPosition = 1f;
            SeatGround();
        }

        // The graticule keeps to the room under the current page's last part (a float compare; the mesh rebuilds only on a change).
        private void SeatGround()
        {
            if (lookRevision != AvConsoleLook.Revision) { lookRevision = AvConsoleLook.Revision; Restyle(); Footer.Relook(); }
            if (CurrentPage >= 0) graticule.Top = flows[CurrentPage].ContentHeight;
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
            AvLay.Place(idPlate.rectTransform, 0f, 0f, 56f, AvGridTokens.Header);
            AvLay.Place(idText.rectTransform, 0f, 0f, 56f, AvGridTokens.Header);
            float right = width >= 360f ? 150f : 60f;
            AvLay.Place(titleText.rectTransform, 66f, 0f, Mathf.Max(0f, width - 66f - right - 6f), AvGridTokens.Header);
            decor.RulerTop = AvGridTokens.Header; decor.SetVerticesDirty();
            AvLay.Place(pageIndex.rectTransform, width - right - 4f, 0f, right, AvGridTokens.Header);
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
            if (tabs != null) { float th = tabs.Measure(inner); tabs.Place(new AvSlot(pad, y, inner, th)); y += th + 4f; }

            float footerH = Footer.Measure(width);
            Footer.Place(new AvSlot(0f, height - footerH, width, footerH));
            float bodyH = Mathf.Max(0f, height - footerH - y);
            AvLay.Place(bodyRect, 0f, y, width, bodyH);
            scrollView.Place(width, bodyH);
            foreach (AvFlow f in flows) f.ViewportHeight = bodyH;
            AvLay.Place(scanCover.rectTransform, 0f, 0f, width, bodyH);
            AvLay.Place(bootText.rectTransform, AvGridTokens.Pad + 4f, AvGridTokens.Pad, width - 2f * AvGridTokens.Pad - 8f, 96f);
            for (int i = 0; i < PageCount; i++) { pageRects[i].anchoredPosition = Vector2.zero; }
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
            if (scanFx == null) scanFx = AvFx.On(scanCover);
            if (AvFxDriver.FxMaterial == null) return;
            scanCover.color = AvStyleHost.FuiColor("ground", AvTheme.Ground);
            scanCover.enabled = true;
            scanFx.Play(AvFxKind.Scan, 0.9f, 1f); // param 1 = cover mode (P0 shader)
            scanUntil = Time.unscaledTime + 0.3f / AvFxPacking.AnimationSpeed;
        }

        private float scanUntil;

        private void EndScan()
        {
            if (scanCover.enabled && Time.unscaledTime >= scanUntil) scanCover.enabled = false;
        }

        private void Animate()
        {
            if (bootLine < 0 && !scanCover.enabled) return;
            if (AvFxPacking.Resolve(AvFxKind.Scan, AvFxDriver.Tier, AvFxDriver.ReducedMotion) == AvFxKind.None)
            {
                bootLine = -1;
                bootText.enabled = scanCover.enabled = false;
                return;
            }
            BootTick();
            EndScan();
        }

        private static readonly string[] BootSteps = { "BIT", "BUS A/B", "SYMBOLOGY" };

        // Back on screen: the first time a console powers up it prints its self-test, after that a scan wipe.
        private void Wake()
        {
            if (AvFxPacking.Resolve(AvFxKind.Scan, AvFxDriver.Tier, AvFxDriver.ReducedMotion) == AvFxKind.None ||
                AvFxDriver.FxMaterial == null) return;
            if (booted) { StartScan(); return; }
            booted = true;
            if (scanFx == null) scanFx = AvFx.On(scanCover);
            scanFx.Clear();
            scanCover.color = AvStyleHost.FuiColor("ground", AvTheme.Ground);
            scanCover.enabled = true;
            scanUntil = float.MaxValue;
            bootText.text = "";
            bootText.enabled = true;
            bootLine = 0;
            nextBootLine = Time.unscaledTime;
        }

        private void BootTick()
        {
            if (bootLine < 0 || Time.unscaledTime < nextBootLine) return;
            nextBootLine = Time.unscaledTime + .025f;
            if (bootLine == 0) bootText.text = id + " DISPLAY PROCESSOR · " + AvPortalMath.Serial(id);
            else if (bootLine <= BootSteps.Length) bootText.text += "\n" + BootSteps[bootLine - 1].PadRight(12, '.') + " OK";
            else
            {
                bootLine = -1;
                bootText.enabled = false;
                scanUntil = 0f;   // if the wipe cannot play (FX switched off meanwhile) the cover still clears next tick
                StartScan();
                return;
            }
            bootLine++;
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
            titleText.color = AvStyleHost.FuiInk("title", AvTheme.TextPrimary);
            pageIndex.color = AvStyleHost.FuiInk("page-index", AvTheme.Dim);
            decor.color = AvStyleHost.FuiFill("decor", AvTheme.Hairline);
            bootText.color = AvStyleHost.FuiInk("section-title", AvTheme.RailInfo);
            if (groundArt != null)
            {
                Color g = AvStyleHost.FuiInk("console-ground", AvTheme.TextPrimary.WithAlpha(0.08f));
                groundArt.color = g.WithAlpha(AvBundle.OverlayAlpha(g.a));
            }
            graticule.enabled = AvConsoleLook.Grid;
            Color grid = AvStyleHost.FuiFill("console-ground", AvTheme.Hairline.WithAlpha(0.3f));
            graticule.color = grid.WithAlpha(AvBundle.OverlayAlpha(grid.a));
            scrollView.Restyle();
        }
    }
}
