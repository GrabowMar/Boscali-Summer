using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;
using NuclearOption.UIStyleSystem;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The board's orchestration: the update (data at 10 Hz, motion every frame), the layout (which blocks exist, where the box goes: ScopeLayout's numbers) and the hand-off to the painters of the stores side
    // (StatusPanel.Stores.cs), the wing side (StatusPanel.Wing.cs: lamp line, gauge line, ticker; StatusPanel.Page.cs: the page, its header and frame) and the shade (StatusPanel.Shade.cs). Nothing here
    // draws a block or works a block's place out by hand.
    internal sealed partial class StatusPanel
    {
        private const float FlashHz = 3f;
        private ScopePageBase[] pages;
        /// <summary>The pages in the pilot's slots (the first <see cref="selectedCount"/>), and what each page's last <see cref="ScopePageBase.Poll"/> said: it has something to draw.</summary>
        private readonly ScopePageBase[] selectedPages = new ScopePageBase[ScopeLayout.MaxSlots];
        private readonly bool[] pageLive = new bool[CuePriority.PageCount];
        private int selectedCount;
        private readonly System.Collections.Generic.HashSet<int> failed = new System.Collections.Generic.HashSet<int>();
        private BoardFrame board;
        private int lastPhase = -1;
        /// <summary>What the pilot's options and declutter level allow, as of the last <see cref="Present"/> (see <see cref="BoardOptions.Effective"/>).</summary>
        private BoardEffective allow;
        private bool hasBoard;
        /// <summary>When the last <see cref="Present"/> finished (<c>Time.unscaledTime</c>).</summary>
        private float presentedAt;

        /// <summary>Something on the board blinks, so its flash phase is part of what it draws.</summary>
        private bool beat => shellBeat || scopeBeat || header.Beat;
        private float now;
        /// <summary>Where the visor's lag has put the symbology (<see cref="ApplySway"/>): whole device pixels in dock units, y up; zero while the head is still or the option is off. <see cref="rootBase"/> is the root's place without it.</summary>
        private Vector2 swayShift, rootBase;
        /// <summary>The box's distance from the screen's right edge as the last repaint placed it (dock units).</summary>
        private float boxFromRight;
        /// <summary>The board's box on the screen as the third-person cluster lays out (centre-origin, y up, dock units = canvas units; no size while the board is not drawn): what the cluster and the other modules' banners keep clear of.</summary>
        public RectF BoardBox { get; private set; }
        // One device pixel in the board's own units, and where the dock's origin falls inside a device pixel: what every edge is laid on (see ScopeCanvas.Begin).
        private float pixel = 1f, phaseX, phaseY;
        private AvUnits units;

        private void BuildBoard()
        {
            pages = ScopePages.Create();
            selectedCount = 1;
            selectedPages[0] = pages[(int)ScopePage.Threat];
            Array.Clear(pageLive, 0, pageLive.Length);
            transition.Reset();
            lampChange.Reset();
            lastPhase = -1;
            hasBoard = false;
        }

        private void DestroyBoard()
        {
            pages = null;
            Array.Clear(selectedPages, 0, selectedPages.Length);
            hasBoard = false;
            headerFault = false;
            tickerWords = tickerSourceText = null;
        }

        // ------------------------------------------------------------------------------------------------ update

        /// <summary>The player's HUD theme as the scope's tokens: ink, caution and warning colours, the rest derived.</summary>
        private static ScopePalette Palette()
        {
            ColorTheme theme = ThemeManager.Active.ColorTheme;
            return new ScopePalette(ToRgba(theme.AllClear), ToRgba(theme.Warning), ToRgba(theme.Alert));
        }

        private static Rgba ToRgba(Color c) => new Rgba(c.r, c.g, c.b, 1f);

        /// <summary>
        /// The 10 Hz refresh: lamps, every page's data (<see cref="ScopePageBase.Poll"/>), the pilot's page slots, the gauge row and the ticker, then a
        /// full repaint. <see cref="Animate"/> carries it between ticks.
        /// </summary>
        public void Present(BoardFrame frame)
        {
            if (root == null) return;
            // Switched off: hide the board and, through the same fail-open path as any other reason to stand down, give the native boxes back (TickNatives). Present again restores it. The kill feed and the map
            // keep their own switch (LAYOUT.md 8: "corners stay"), so they are measured, kept fresh and laid out here as for a board that is up.
            if (!Options.Enabled)
            {
                HideBoard();
                MeasurePixel();
                allow = Options.Effective();
                MeasureScreen();
                hasBoard = true;
                presentedAt = Time.unscaledTime;
                CornersFrame(true, true);
                return;
            }
            // The pilot's opacity is the root group's alpha (nothing else in the board animates it); the shade is outside it (StatusPanel.Shade.cs).
            if (rootGroup.alpha != Options.Opacity) rootGroup.alpha = Options.Opacity;
            board = frame;
            now = frame.Now;
            units = frame.Units;

            SystemStates systems = frame.Systems;
            if (nativeSafety != null) systems.WeaponSafe = nativeSafety.enabled;
            HudLamps.Fill(systems, lamps);
            HudThreats.Fill(frame.Threat, threatLamps);

            MeasurePixel();
            // Every feed remains polled for notices, warning lamps and dispenser calls. The pilot's selected pages never follow the live mask.
            allow = Options.Effective();
            var voiced = new ScopeEnv(now, frame.Units, Notice);
            var silent = new ScopeEnv(now, frame.Units, null);
            if (systemsNotice == null) systemsNotice = NoticeFromSystems;
            var systemsEnv = new ScopeEnv(now, frame.Units, systemsNotice);      // SYSTEMS at Minimal speaks only in its critical form
            for (int i = 0; i < pages.Length; i++)
            {
                var id = (ScopePage)i;
                ScopeEnv env = !CuePriority.Has(allow.Voiced, id) ? silent : id == ScopePage.Systems ? systemsEnv : voiced;
                pageLive[i] = PollPage(pages[i], env);
            }
            selectedCount = Math.Max(1, Math.Min(ScopeLayout.MaxSlots, Options.PageCount));
            ushort selected = 0;
            for (int i = 0; i < selectedCount; i++)
            {
                ScopePage id = Options.PageAt(i);
                // Sanitize keeps the slots distinct and off SYSTEMS (its own block); options written past it still never paint a page twice in one frame.
                if (id == ScopePage.Systems || (uint)id >= (uint)pages.Length || CuePriority.Has(selected, id))
                    for (int candidate = 0; candidate < pages.Length; candidate++)
                        if (candidate != (int)ScopePage.Systems && !CuePriority.Has(selected, (ScopePage)candidate)) { id = (ScopePage)candidate; break; }
                selectedPages[i] = pages[(int)id];
                selected |= CuePriority.Bit(id);
            }
            PollStores();
            MeasureScreen();      // after the header's read: where the board goes depends on what the header stands in for

            PresentLamps(frame);
            PresentGauges();
            hasBoard = true;
            Repaint(true);
            presentedAt = Time.unscaledTime;
            CornersFrame(true, true);
        }

        /// <summary>The board's box (<see cref="BoardBox"/>) while it is on the screen, else no size.</summary>
        public RectF KeepClearBox => root != null && root.gameObject.activeInHierarchy ? BoardBox : default;

        /// <summary>The board's box in device pixels from the screen's top-left corner (y down): false while the board is not drawn. <c>IHudBoard.TryGetScreenBox</c>.</summary>
        public bool TryGetScreenBox(out float left, out float top, out float right, out float bottom)
        {
            RectF box = KeepClearBox;
            left = top = right = bottom = 0f;
            if (box.Width <= 0f || !(pixel > 0f)) return false;
            left = (box.Left + screenW * 0.5f) / pixel;
            right = (box.Right + screenW * 0.5f) / pixel;
            top = (screenH * 0.5f - box.Top) / pixel;
            bottom = (screenH * 0.5f - box.Bottom) / pixel;
            return true;
        }

        /// <summary>
        /// Every frame: put the symbology where the visor's lag puts it (<see cref="VisorSway"/>, in board units, y up): the board's root and the corners' roots move together, in whole device pixels, and only when the pixel
        /// offset changes (a position write and nothing else). The sun band stays hung from the top of the screen.
        /// </summary>
        public void ApplySway(float x, float y)
        {
            if (root == null || !(pixel > 0f)) return;
            float perUnit = baseUnit / pixel;
            var shift = new Vector2(VisorSway.Pixels(x, perUnit) * pixel, VisorSway.Pixels(y, perUnit) * pixel);
            if (shift == swayShift) return;
            swayShift = shift;
            root.anchoredPosition = rootBase + shift;
            if (shadeBand != null) shadeBand.rectTransform.anchoredPosition = shadeBandBase - new Vector2(0f, shift.y);
            PlaceCornerRoots();
        }

        private Action<string, HudTone, string, string> systemsNotice;

        /// <summary>SYSTEMS' notices, through the declutter level's rule (<see cref="BoardEffective.Posts"/>).</summary>
        private void NoticeFromSystems(string channel, HudTone tone, string text, string detail)
        {
            if (allow.Posts(ScopePage.Systems, tone)) Notice?.Invoke(channel, tone, text, detail);
        }

        private bool PollPage(ScopePageBase p, in ScopeEnv env)
        {
            try { return p.Poll(env); }
            catch (Exception e) { ReportOnce(p, e); return false; }
        }

        private ScopeHeadline HeadlineOf(ScopePageBase p)
        {
            try { return p.Headline(); }
            catch (Exception e) { ReportOnce(p, e); return default; }
        }

        /// <summary>
        /// How big one DEVICE pixel is in the dock's units, and the fraction of a pixel the dock's origin sits at, from where the dock really lands on the screen (any canvas mode:
        /// overlay, camera, a CanvasScaler or a scaled parent). 1 and 0 when it cannot be told.
        /// </summary>
        private void MeasurePixel()
        {
            pixel = 1f;
            phaseX = phaseY = 0f;
            if (dock == null || rootCanvas == null) return;
            Camera cam = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
            if (cam == null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay) return;
            Vector2 origin = RectTransformUtility.WorldToScreenPoint(cam, dock.TransformPoint(Vector3.zero));
            Vector2 across = RectTransformUtility.WorldToScreenPoint(cam, dock.TransformPoint(new Vector3(100f, 0f, 0f)));
            float perUnit = Mathf.Abs(across.x - origin.x) / 100f;
            if (!(perUnit > 0.05f && perUnit < 20f)) return;
            pixel = 1f / perUnit;
            phaseX = (Mathf.Ceil(origin.x - 0.001f) - origin.x) * pixel;
            phaseY = (Mathf.Ceil(origin.y - 0.001f) - origin.y) * pixel;
        }

        /// <summary>A dock-local coordinate on the device pixel grid: x (or y) of the dock's origin plus a whole number of pixels.</summary>
        private float OnGrid(float v, float phase) => phase + Mathf.Round((v - phase) / pixel) * pixel;

        /// <summary>A length rounded UP to whole device pixels (a surface's size: its mesh must reach the edge the next surface starts at).</summary>
        private float Whole(float v) => Mathf.Ceil(v / pixel - 0.001f) * pixel;

        private void ReportOnce(ScopePageBase p, Exception e)
        {
            if (failed.Add((int)p.Id)) BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Hud.Scope." + p.Title, e);
        }

        /// <summary>Every frame: carry the wipe and the flash beat, and repaint a page that moves.</summary>
        public void Animate(float clock)
        {
            if (root == null || !hasBoard || !root.gameObject.activeSelf) return;
            now = clock;
            int phase = (int)(clock * FlashHz * 2f);
            bool flashChanged = beat && phase != lastPhase;
            bool fast = allow.ShowSystems && pages[(int)ScopePage.Systems].Fast;
            for (int i = 0; i < selectedCount; i++) fast |= selectedPages[i].Fast;
            if (!flashChanged && !transition.Running && !fast) return;
            Repaint(flashChanged || transition.Running);
        }

        // ------------------------------------------------------------------------------------------------ layout and paint

        private void Repaint(bool shellToo)
        {
            bool scopeOn = Options.ScopeMode != BoardScopeMode.Off;
            // A change of the slots (or their count) wipes the pages in; they are all full pages, so the height only moves when the pages come or go.
            int signature = 0;
            if (scopeOn)
            {
                signature = selectedCount;
                for (int i = 0; i < selectedCount; i++) signature = signature * 11 + (int)selectedPages[i].Id + 1;
            }
            float target = scopeOn ? ScopeLayout.Full : 0f;
            transition.Update(signature, target, now);
            // SYSTEMS is the board's own block, not a page: there while there is an aircraft to read and its switch is on, whatever the slots or the declutter level.
            ScopePageBase systems = pages[(int)ScopePage.Systems];
            var blocks = new BoardBlocks
            {
                Layout = Options.Layout, Hero = header.HasWeapon, Pwr = header.HasCap, Exp = header.HasCm, StoresHidden = !allow.ShowStores, NoShelves = !allow.Furniture, Lamps = lampRow, PageH = transition.Height, PageCount = selectedCount,
                StatusH = allow.ShowSystems && systems.Ready ? ScopeLayout.Compact : 0f, GaugeLines = gaugeLines, Ticker = allow.ShowTicker && board.HasTicker,
            };
            BoardGeometry geo = ScopeLayout.Compute(blocks);
            // Where the wipe is heading: the size, the anchor and the shade go by it, so pages coming in never pump the scale or move the box.
            BoardBlocks settled = blocks;
            settled.PageH = target;
            BoardGeometry end = ScopeLayout.Compute(settled);
            if (geo.Height <= 0f)
            {
                ClearHeader();
                Show(root, false);
                BoardBox = default;
                return;
            }
            Show(root, true);

            // The pilot's size unless the whole board would not fit the screen at it (four pages at a large size on a small screen): then as big as fits.
            float availableHeight = screenH - (underNative ? Math.Max(0f, screenTop - dock.rect.yMin) : 0f);
            float requestedUnit = baseUnit * Mathf.Clamp(Options.Scale, BoardOptions.ScaleMin, BoardOptions.ScaleMax);
            unit = ScopeLayout.FitUnit(requestedUnit, pixel, screenW, availableHeight, end.Width, end.Height);

            // The box's top-right corner on the device pixel grid, so every edge drawn inside it can be too.
            PlaceBoard(end, end.Height, out float fromTop);
            rootBase = new Vector2(OnGrid(cornerX - dock.rect.xMin, phaseX), OnGrid(cornerY - dock.rect.yMin, phaseY));
            Vector2 rootPosition = rootBase + swayShift;
            if (root.anchoredPosition != rootPosition) root.anchoredPosition = rootPosition;
            Vector2 rootSize = new Vector2(Whole(geo.Width * unit), Whole(geo.Height * unit));
            if (root.sizeDelta != rootSize) root.sizeDelta = rootSize;
            BoardBox = ScopeLayout.Box(screenW, screenH, boxFromRight, fromTop, rootSize.x, rootSize.y);
            PaintShade(end, fromTop);      // baked once for a page change, not on every frame of the wipe

            ScopePalette pal = Palette();
            lastPhase = (int)(now * FlashHz * 2f);
            bool flashOn = lastPhase % 2 == 0;      // FlashHz full cycles a second: on for half of each (LAYOUT.md 3.4, 10: 3 Hz)
            if (geo.Page.Present) PaintPages(pal, geo, flashOn);
            else
            {
                for (int i = 0; i < pageSurfaces.Length; i++) Show(pageSurfaces[i].Scope, false);
                scopeBeat = false;
            }
            if (!geo.Status.Present) Show(statusRect, false);
            else if (shellToo || systems.Fast) PaintStatus(pal, geo, flashOn);
            if (shellToo)
            {
                PaintWing(pal, geo, flashOn);
                PaintStores(pal, geo, flashOn);
            }
        }
    }
}
