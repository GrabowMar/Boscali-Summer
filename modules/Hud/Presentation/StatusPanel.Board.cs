using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Core.Ui;
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
        /// <summary>
        /// The three panels, in the spine's order RWR, FIRES, WING (<see cref="ScopeLayout.SlotRwr"/>...): the page each hosts (kept while its slot wipes shut), whether it is drawn (<see cref="slotOpen"/>) and the height it
        /// settles at (<see cref="slotHeight"/>: 0 when it is not drawn; RWR has its header line alone while <see cref="rwrOpen"/> is false). <see cref="pageLive"/>: what each page's last <see cref="ScopePageBase.Poll"/> said.
        /// </summary>
        private readonly ScopePageBase[] slotPages = new ScopePageBase[ScopeLayout.MaxSlots];
        private readonly bool[] slotOpen = new bool[ScopeLayout.MaxSlots];
        private readonly float[] slotHeight = new float[ScopeLayout.MaxSlots], slotFull = new float[ScopeLayout.MaxSlots];
        private bool rwrOpen;
        private readonly bool[] pageLive = new bool[CuePriority.PageCount];
        private readonly System.Collections.Generic.HashSet<int> failed = new System.Collections.Generic.HashSet<int>();
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
        private Vector2 swayShift, rootBase, boardShift, boardTearShift;
        /// <summary>The box's distance from the screen's right edge as the last repaint placed it (dock units).</summary>
        private float boxFromRight;
        /// <summary>The board's box on the screen as the third-person cluster lays out (centre-origin, y up, dock units = canvas units; no size while the board is not drawn): what the cluster and the other modules' banners keep clear of.</summary>
        public RectF BoardBox { get; private set; }
        // One device pixel in the board's own units, and where the dock's origin falls inside a device pixel: what every edge is laid on (see ScopeCanvas.Begin).
        private float pixel = 1f, phaseX, phaseY;
        private AvUnits units;
        private MissionDashboardStatus missionStatus;
        private bool hasMission;
        private const float MissionRowsHeight = 36f;

        private void BuildBoard()
        {
            pages = ScopePages.Create();
            Array.Clear(slotPages, 0, slotPages.Length);
            Array.Clear(slotOpen, 0, slotOpen.Length);
            Array.Clear(slotHeight, 0, slotHeight.Length);
            rwrOpen = false;
            Array.Clear(pageLive, 0, pageLive.Length);
            for (int i = 0; i < transitions.Length; i++) transitions[i].Reset();
            wipeSoundKey = int.MinValue;
            lampChange.Reset();
            lastPhase = -1;
            hasBoard = false;
        }

        private void DestroyBoard()
        {
            pages = null;
            Array.Clear(slotPages, 0, slotPages.Length);
            Array.Clear(slotOpen, 0, slotOpen.Length);
            wingPainted = false;
            hasBoard = false;
            headerFault = false;
            tickerWords = tickerSourceText = null;
        }

        // ------------------------------------------------------------------------------------------------ update

        /// <summary>The player's HUD theme as the scope's tokens: ink, caution and warning colours, the rest derived.</summary>
        private static ScopePalette Palette()
        {
            ColorTheme theme = ThemeManager.Active.ColorTheme;
            return new ScopePalette(HudInk.Ink(ToRgba(theme.AllClear)), ToRgba(theme.Warning), ToRgba(theme.Alert));
        }

        private static Rgba ToRgba(Color c) => new Rgba(c.r, c.g, c.b, 1f);

        /// <summary>
        /// The 10 Hz refresh: lamps, every page's data (<see cref="ScopePageBase.Poll"/>), which panels are up (<see cref="PickPanels"/>), the gauge row and the ticker, then a
        /// full repaint. <see cref="Animate"/> carries it between ticks.
        /// </summary>
        public void Present(BoardFrame frame)
        {
            if (root == null) return;
            noticeHas = frame.HasTicker;      // the NOTICE line is the left column's and shows with the board off too
            noticeLine = frame.Ticker;
            routeUnits = frame.Units;
            hasMission = ModuleServices.TryGet<IMissionDashboardView>(out var mission) && mission.TryRead(out missionStatus);
            // Switched off: hide the board and, through the same fail-open path as any other reason to stand down, give the native boxes back (TickNatives). Present again restores it. The kill feed and the map
            // keep their own switch (LAYOUT.md 8: "corners stay"), so they are measured, kept fresh and laid out here as for a board that is up.
            if (!Options.Enabled)
            {
                PollRoutePages(frame.Now);      // the ROUTE slot is the left column's and lives with the board off: its four pages stay polled
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
            now = frame.Now;
            units = frame.Units;

            SystemStates systems = frame.Systems;
            if (nativeSafety != null) systems.WeaponSafe = nativeSafety.enabled;
            HudLamps.Fill(systems, lamps);
            HudThreats.Fill(frame.Threat, threatLamps);

            MeasurePixel();
            // Every feed remains polled for notices, warning lamps and dispenser calls, whether or not a panel shows it.
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
            UpdateRoute();
            PickPanels(frame.Threat.Missiles > 0);      // the count that raises AlertKind.Missile (CombatAlerts) and lights the MSL lamp (HudThreats.Fill)
            PollStores();
            MeasureScreen();      // after the header's read: where the board goes depends on what the header stands in for

            PresentLamps(frame);
            PresentGauges();
            hasBoard = true;
            Repaint(true);
            presentedAt = Time.unscaledTime;
            CornersFrame(true, true);
        }

        /// <summary>
        /// Which panels are up and how tall (ScopePick, ScopeLayout): RWR always (its full scope while anything paints us or a missile is inbound, else its header line), FIRES while SHOT or STRIKE is live, WING while there
        /// are wingmen and the spine has room for it. A panel that closes keeps its page until its slot has wiped shut. With the scope Off there are none.
        /// </summary>
        private void PickPanels(bool missile)
        {
            ushort liveMask = 0;
            for (int i = 0; i < pageLive.Length; i++) if (pageLive[i]) liveMask |= CuePriority.Bit((ScopePage)i);
            bool on = Options.ScopeMode != BoardScopeMode.Off;
            rwrOpen = on && ScopePick.RwrOpen(liveMask, missile);
            bool fires = ScopePick.Fires(liveMask, out ScopePage host) && on;
            float rwrH = on ? ScopeLayout.RwrHeight(rwrOpen) : 0f, firesH = fires ? ScopeLayout.FiresHeight(host) : 0f;
            bool wing = on && pageLive[(int)ScopePage.Wing] && ScopeLayout.WingFits(rwrH, firesH, ScopeLayout.Compact + (hasMission ? MissionRowsHeight : 0f));
            SetSlot(ScopeLayout.SlotRwr, on ? pages[(int)ScopePage.Threat] : null, rwrH);
            SetSlot(ScopeLayout.SlotFires, fires ? pages[(int)host] : null, firesH);
            SetSlot(ScopeLayout.SlotWing, wing ? pages[(int)ScopePage.Wing] : null, ScopeLayout.WingH);
        }

        private void SetSlot(int slot, ScopePageBase page, float height)
        {
            slotOpen[slot] = page != null;
            slotHeight[slot] = page != null ? height : 0f;
            if (page == null) return;
            slotPages[slot] = page;
            slotFull[slot] = slot == ScopeLayout.SlotRwr ? ScopeLayout.Full : height;      // the body's own height: RWR's scope is laid out full also while it wipes down to its header line
        }

        /// <summary>The page in <paramref name="slot"/> draws its body (it is open, and RWR is not down to its header line).</summary>
        private bool SlotBody(int slot) => slotOpen[slot] && (slot != ScopeLayout.SlotRwr || rwrOpen);

        /// <summary>The board's box (<see cref="BoardBox"/>) while it is on the screen, else no size.</summary>
        public RectF KeepClearBox => root != null && root.gameObject.activeInHierarchy ? BoardBox : default;

        /// <summary>The board is drawn (its root is up: switched off, stale and torn down all take it down) and the WING panel is up. <c>IHudBoard.ShowsWing</c>.</summary>
        public bool ShowsWing => wingPainted && root != null && root.gameObject.activeInHierarchy;

        /// <summary>The last <see cref="Repaint"/> painted the WING panel (cleared by every repaint and by hiding or destroying the board; an Animate that skips the repaint leaves the last paint, and this, standing).</summary>
        private bool wingPainted;

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
        public void ApplySway(float x, float y, float tearBoardX = 0f, float tearLeftX = 0f)
        {
            if (root == null || !(pixel > 0f)) return;
            float perUnit = baseUnit / pixel;
            var shift = new Vector2(VisorSway.Pixels(x, perUnit) * pixel, VisorSway.Pixels(y, perUnit) * pixel);
            // The hit glitch's torn band (the reactive visor): the board draws aside and the left column the other way for a few frames.
            var boardTear = new Vector2(VisorSway.Pixels(tearBoardX, perUnit) * pixel, 0f);
            var leftShift = shift + new Vector2(VisorSway.Pixels(tearLeftX, perUnit) * pixel, 0f);
            if (leftShift == swayShift && boardTear == boardTearShift) return;
            swayShift = leftShift;
            boardTearShift = boardTear;
            boardShift = shift + boardTear;
            root.anchoredPosition = rootBase + boardShift;
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
            bool wiping = false;
            for (int i = 0; i < slotPages.Length; i++)
            {
                fast |= SlotBody(i) && slotPages[i].Fast;
                wiping |= transitions[i].Running;
            }
            if (!flashChanged && !wiping && !fast) return;
            Repaint(flashChanged || wiping);
        }

        // ------------------------------------------------------------------------------------------------ layout and paint

        /// <summary>The layout the board is drawn in: STACK on a screen narrower than 16:9 whatever the stored option, else the option.</summary>
        private bool boardStack;

        private void Repaint(bool shellToo)
        {
            wingPainted = false;
            // Each slot wipes on its own: a panel that comes, goes or changes its page (FIRES: SHOT or STRIKE) wipes, RWR opening from or closing to its header line only changes height.
            int signature = 0;
            for (int i = 0; i < slotPages.Length; i++)
            {
                int key = slotOpen[i] ? (int)slotPages[i].Id + 1 : 0;
                transitions[i].Update(key, slotHeight[i], now);
                signature = signature * 22 + key * 2 + (i == ScopeLayout.SlotRwr && rwrOpen ? 1 : 0);
            }
            // The page wipe's chirp (HMD-9) when the panels change, never for the first paint.
            if (signature != wipeSoundKey)
            {
                if (wipeSoundKey != int.MinValue) BoscaliSummer.Core.Fx.VisorSound.Play(BoscaliSummer.Core.Fx.VisorClip.Wipe);
                wipeSoundKey = signature;
            }
            // A screen narrower than 16:9 always gets the STACK layout (the stored option is kept): WIDE would reach into the left column.
            boardStack = HudZones.Narrow(canvasRect.rect.width / baseUnit) || Options.Layout == BoardLayout.Stack;
            // SYSTEMS is the board's own block, not a page: there while there is an aircraft to read and its switch is on, whatever the slots or the declutter level.
            ScopePageBase systems = pages[(int)ScopePage.Systems];
            var blocks = new BoardBlocks
            {
                Layout = boardStack ? BoardLayout.Stack : BoardLayout.Wide, Hero = header.HasWeapon, Pwr = header.HasCap, Exp = header.HasCm, StoresHidden = !allow.ShowStores, NoShelves = !allow.Furniture, Lamps = lampRow, RwrH = transitions[0].Height, FiresH = transitions[1].Height, WingH = transitions[2].Height,
                StatusH = allow.ShowSystems && systems.Ready ? ScopeLayout.Compact + (hasMission ? MissionRowsHeight : 0f) : 0f, GaugeLines = gaugeLines,
            };
            BoardGeometry geo = ScopeLayout.Compute(blocks);
            // Where the wipes are heading: the size, the anchor and the shade go by it, so panels coming in never pump the scale or move the box.
            BoardBlocks settled = blocks;
            settled.RwrH = slotHeight[0]; settled.FiresH = slotHeight[1]; settled.WingH = slotHeight[2];
            BoardGeometry end = ScopeLayout.Compute(settled);
            if (geo.Height <= 0f)
            {
                ClearHeader();
                Show(root, false);
                BoardBox = default;
                return;
            }
            Show(root, true);

            // The pilot's size unless the whole board would not fit the screen at it (every panel up at a large size on a small screen): then as big as fits.
            float availableHeight = screenH - (underNative ? Math.Max(0f, screenTop - dock.rect.yMin) : 0f);
            float requestedUnit = baseUnit * Mathf.Clamp(Options.Scale, BoardOptions.ScaleMin, BoardOptions.ScaleMax);
            unit = ScopeLayout.FitUnit(requestedUnit, pixel, screenW, availableHeight, end.Width, end.Height);

            // The box's top-right corner on the device pixel grid, so every edge drawn inside it can be too.
            PlaceBoard(end, end.Height, out float fromTop);
            rootBase = new Vector2(OnGrid(cornerX - dock.rect.xMin, phaseX), OnGrid(cornerY - dock.rect.yMin, phaseY));
            Vector2 rootPosition = rootBase + boardShift;
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
