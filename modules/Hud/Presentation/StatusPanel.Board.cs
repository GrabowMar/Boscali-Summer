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
        private readonly ScopeSelector selector = new ScopeSelector();
        private ScopePageBase page;
        private readonly System.Collections.Generic.HashSet<int> failed = new System.Collections.Generic.HashSet<int>();
        private BoardFrame board;
        private int lastPhase = -1;
        /// <summary>What the pilot's options and declutter level allow, as of the last <see cref="Present"/> (see <see cref="BoardOptions.Effective"/>).</summary>
        private BoardEffective allow;
        /// <summary>The compact SYSTEMS strip may show.</summary>
        private bool systemsStrip;
        private bool hasBoard;
        /// <summary>When the last <see cref="Present"/> finished (<c>Time.unscaledTime</c>).</summary>
        private float presentedAt;

        /// <summary>Something on the board blinks, so its flash phase is part of what it draws.</summary>
        private bool beat => shellBeat || scopeBeat || header.Beat;
        private float now;
        // One device pixel in the board's own units, and where the dock's origin falls inside a device pixel: what every edge is laid on (see ScopeCanvas.Begin).
        private float pixel = 1f, phaseX, phaseY;
        private AvUnits units;

        private void BuildBoard()
        {
            pages = ScopePages.Create();
            page = pages[(int)ScopePage.Systems];
            selector.Reset();
            transition.Reset();
            lampChange.Reset();
            lastPhase = -1;
            hasBoard = false;
        }

        private void DestroyBoard()
        {
            pages = null;
            page = null;
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
        /// The 10 Hz refresh: lamps, every page's data (<see cref="ScopePageBase.Poll"/>), the page selection, the gauge row and the ticker, then a
        /// full repaint. <see cref="Animate"/> carries it between ticks.
        /// </summary>
        public void Present(BoardFrame frame)
        {
            if (root == null) return;
            // Switched off: hide the board and, through the same fail-open path as any other reason to stand down, give the native boxes back (TickNatives). Present again restores it.
            if (!Options.Enabled)
            {
                Hide();
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
            // The page switches. A page that is switched off (or that the declutter level drops) is still polled, other code reads its state, but it is never live, so it never takes the
            // scope, and it is silent: no ticker notices (its environment has no Notice) and no dispenser call in the stores header. The scope mode Off hides the scope, not the warnings.
            allow = Options.Effective();
            var voiced = new ScopeEnv(now, frame.Units, Notice);
            var silent = new ScopeEnv(now, frame.Units, null);
            ushort live = 0;
            for (int i = 0; i < pages.Length; i++)
                if (PollPage(pages[i], CuePriority.Has(allow.Voiced, (ScopePage)i) ? voiced : silent)) live |= CuePriority.Bit((ScopePage)i);
            bool notNominal = CuePriority.Has(live, ScopePage.Systems);      // before the mask: the strip's rule is about the aircraft's state, not about a page switch
            selector.Select(live, allow, Options.PageMinSeconds, now, pages[(int)ScopePage.Systems].Critical);
            page = pages[(int)selector.Page];
            systemsStrip = allow.SystemsStripShows(notNominal, pages[(int)ScopePage.Systems].Critical);
            PollStores();
            MeasureScreen();      // after the header's read: where the board goes depends on what the header stands in for

            PresentLamps(frame);
            PresentGauges();
            hasBoard = true;
            Repaint(true);
            presentedAt = Time.unscaledTime;
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
            if (!flashChanged && !transition.Running && !page.Fast) return;
            Repaint(flashChanged || transition.Running);
        }

        // ------------------------------------------------------------------------------------------------ layout and paint

        private void Repaint(bool shellToo)
        {
            bool compact = selector.Compact;
            bool strip = !compact && page.HasStrip;
            // The scope mode Off shows no page at all, nor does a compact SYSTEMS strip the pilot's switch or the declutter level keeps away: the block has no height and the layout drops it;
            // the lamps, gauges and ticker stay. A block that is switched off (or has nothing live) is no block either: it is not drawn and takes no room.
            bool scopeOn = Options.ScopeMode != BoardScopeMode.Off && page.Ready && (!compact || systemsStrip);
            float target = scopeOn ? (compact ? ScopeLayout.Compact : ScopeLayout.Full) : 0f;
            transition.Update((int)selector.Page * 2 + (compact ? 1 : 0), target, now);
            var blocks = new BoardBlocks
            {
                Layout = Options.Layout, Hero = header.HasWeapon, Pwr = header.HasCap, Exp = header.HasCm, Lamps = lampRow, PageH = transition.Height,
                GaugeLines = gaugeLines, Ticker = allow.ShowTicker && board.HasTicker,
            };
            BoardGeometry geo = ScopeLayout.Compute(blocks);
            // The stores header is drawn as one piece for now (StatusPanel.Stores.cs): the box reaches as low as it does.
            if (geo.Height > 0f) geo.Height = Math.Max(geo.Height, ScopeLayout.BlockTop + header.Height);
            if (geo.Height <= 0f)
            {
                header.Clear();
                Show(root, false);
                return;
            }
            Show(root, true);

            // The box's top-right corner on the device pixel grid (position and size in whole pixels), so every edge drawn inside it can be too.
            PlaceBoard(geo, out float fromTop);
            Vector2 rootPosition = new Vector2(OnGrid(cornerX - dock.rect.xMin, phaseX), OnGrid(cornerY - dock.rect.yMin, phaseY));
            if (root.anchoredPosition != rootPosition) root.anchoredPosition = rootPosition;
            Vector2 rootSize = new Vector2(Whole(geo.Width * unit), Whole(geo.Height * unit));
            if (root.sizeDelta != rootSize) root.sizeDelta = rootSize;
            PaintShade(geo, fromTop);

            ScopePalette pal = Palette();
            lastPhase = (int)(now * FlashHz * 2f);
            bool flashOn = (int)(now * FlashHz) % 2 == 0;
            if (geo.Page.Present) PaintPage(pal, geo, compact, strip, flashOn);
            else { Show(scopeRect, false); scopeBeat = false; }
            if (shellToo)
            {
                PaintWing(pal, geo, flashOn);
                PaintStores(pal, geo, flashOn);
            }
        }
    }
}
