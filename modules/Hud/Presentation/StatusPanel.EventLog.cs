using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Patches;
using NOAvionics;
using UnityEngine;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // DL EVT LOG (HUD holo pass D, 2026-10-13): the top-left corner's own event log in the visor's language, in place of the game's feed text. The lines are captured as the game adds them (Patches/EventFeedPatch.cs: every
    // line, with its rich text and the game's removal time, into EventFeed.Log, the newest six); this paints them. A captioned shelf on top, up to six rows under it, oldest first: a DIM mission-clock stamp, a DIM class word and the
    // line in the HUD's mono face and capitals (names INK, or BRIGHT when they are the pilot's own, each with a small square in the colour the game gave it; the verb between them DIM; chat and radio words INK), with a thin
    // DIM spine and a tick per row at the left. A row is BRIGHT for its first 1.5 s, wipes in over 180 ms, fades over its last second and goes when the game takes the line off its own feed. The block hangs from the native feed
    // box's top-left corner, follows Opacity (a group of its own), the sway (PlaceCornerRoots), the device pixel grid and the declutter level (Lean and Minimal: no shelf, spine or stamps; Minimal: only the pilot's
    // KILL rows and COMM); the soft shade behind it is StatusPanel.Corners.cs's (the band and the cloud, over this block's rectangle). The game's own feed text is hidden through NativeScreenCorners while this is drawn
    // and left whole while the chat input is open (the base game's chat works as ever), and whenever the capture cannot be trusted (EventLog.Healthy). Text is written only when it changes (ScopeText); the surface is drawn again only when
    // EventLog.Signature moves (a row coming or going, a wipe or fade step, BRIGHT ending), not every frame.
    internal sealed partial class StatusPanel
    {
        private const int LogQuads = 96;
        /// <summary>The square in front of a name in the game's colour (design units): the lamps' size.</summary>
        private const float LogSwatch = 4f;

        private readonly EventLog eventLog = EventFeed.Log;
        private readonly EventRow[] logRows = new EventRow[EventLog.Capacity];
        private RectTransform logRoot, logSurface;
        private CanvasGroup logGroup;
        private AvVector logVector;
        private ScopeText logText;
        private ScopeCanvas logCanvas;
        /// <summary>The block as last laid out (what the offline check reads), the rows it drew, and how lit the brightest of them was (the shade follows it).</summary>
        private EventLogPlan logPlan;
        private int logCount;
        private float logLit;
        /// <summary>What the last paint was drawn for: in charge of the feed corner, and the log's signature.</summary>
        private bool logCharge;
        private int logSig;
        /// <summary>The offline check's own clock for the log (NaN: the game's, <c>Time.timeSinceLevelLoad</c>).</summary>
        private float logClockOverride = float.NaN;

        private float LogClock() => float.IsNaN(logClockOverride) ? Time.timeSinceLevelLoad : logClockOverride;

        /// <summary>
        /// DL EVT LOG is in charge of the top-left corner: the game's feed box is up, the pilot is not typing in the chat (then the game's feed is left whole) and the capture is seeing the game's lines
        /// (<see cref="EventLog.Healthy"/>). Run every frame; the clock check inside empties the log when a scene started again.
        /// </summary>
        private bool LogInCharge(bool feedUp)
        {
            float clock = LogClock();
            eventLog.Tick(clock);
            bool healthy = eventLog.Healthy(clock, screenCorners.FeedHasText);
            return feedUp && !screenCorners.Typing && healthy;
        }

        private bool EnsureLogTree()
        {
            if (logRoot != null) return true;
            if (canvasRect == null) return false;
            logRoot = CornerRoot("BoscaliEventLog");
            logGroup = logRoot.gameObject.AddComponent<CanvasGroup>();
            logGroup.ignoreParentGroups = true;      // the pilot's opacity is this group's alone, as the board's is
            logGroup.interactable = logGroup.blocksRaycasts = false;
            logSurface = Pinned(logRoot, "Log", false);
            logVector = AvVector.Create(logSurface, "Strokes", LogQuads);
            logText = new ScopeText(Holder(logSurface, "Words"), font, fontMaterial);
            logCanvas = new ScopeCanvas(logVector.Buffer, logText, "scope.eventlog");
            PlaceCornerRoots();
            return true;
        }

        /// <summary>
        /// Lay out and draw the log for the feed box whose top-left corner is (<paramref name="boxLeft"/>, <paramref name="boxTop"/>) in design units: what <see cref="EventLog.Select"/> says the level shows, the block, the rows.
        /// False when there is no tree to draw on (the game's feed is then left alone).
        /// </summary>
        private bool PaintEventLog(float boxLeft, float boxTop)
        {
            if (!EnsureLogTree()) return false;
            float clock = LogClock();
            BoardDeclutter level = Options.Declutter;
            logCount = eventLog.Select(clock, level, logRows);
            int widest = 0;
            logLit = 0f;
            for (int i = 0; i < logCount; i++)
            {
                widest = Math.Max(widest, logRows[i].Line.Chars + (logRows[i].Line.Truncated ? 2 : 0));
                logLit = Math.Max(logLit, logRows[i].Alpha(clock));
            }
            logPlan = EventLogLayout.Plan(boxLeft, boxTop, logCount, widest, level);
            LeftHudStack.LogBottom = logPlan.Present ? logPlan.Top + logPlan.Height : 0f;
            Show(logRoot, true);
            if (logGroup.alpha != Options.Opacity) logGroup.alpha = Options.Opacity;
            PlaceOnGrid(logSurface, 0f, 0f, Whole(canvasRect.rect.width), Whole(canvasRect.rect.height));
            ScopeCanvas g = logCanvas;
            g.Furniture = logPlan.Furniture;
            g.Begin(Palette(), baseUnit, Whole(canvasRect.rect.height), pixel);
            if (logPlan.Present)
            {
                g.CaptionShelf(logPlan.Left, logPlan.Top, logPlan.Width, "DL EVT LOG");
                if (logPlan.Furniture) g.Line(logPlan.Left + 1f, logPlan.SpineTop, logPlan.Left + 1f, logPlan.SpineBottom, Dim);
                for (int i = 0; i < logCount; i++) DrawEventRow(g, logPlan, logRows[i], logPlan.RowY(i), clock);
            }
            g.End();
            logVector.Commit();
            return true;
        }

        /// <summary>
        /// One row at <paramref name="cy"/>: the spine's tick, the stamp, the class word and the line, each as much of it as the wipe has reached (cells counted over the whole row, left to right) and all of it at the row's fade.
        /// </summary>
        private static void DrawEventRow(ScopeCanvas g, in EventLogPlan p, EventRow row, float cy, float clock)
        {
            EventLine line = row.Line;
            string word = EventWords.Of(line.Class);
            float alpha = row.Alpha(clock), wipe = row.Wipe(clock);
            bool bright = row.Bright(clock);
            int total = (p.Furniture ? row.Stamp.Length : 0) + word.Length + line.Chars + (line.Truncated ? 2 : 0);
            int budget = wipe >= 1f ? int.MaxValue : (int)Math.Ceiling(wipe * total);
            if (p.Furniture)
            {
                g.Line(p.Left + 1f, cy, p.Left + 1f + EventLogLayout.SpineTick, cy, Dim, alpha);
                Reveal(g, row.Stamp, p.StampX, cy, Dim, alpha, ref budget);
            }
            Reveal(g, word, p.ClassX, cy, Dim, alpha, ref budget);
            float x = p.LineX;
            foreach (EventSpan span in line.Spans)
            {
                if (budget <= 0) return;
                if (span.Swatch)
                {
                    g.Swatch(x, cy, LogSwatch, new Rgba(((span.Rgb >> 16) & 255) / 255f, ((span.Rgb >> 8) & 255) / 255f, (span.Rgb & 255) / 255f), alpha);
                    x += EventLogLayout.Advance;
                    budget--;
                }
                ScopeTone tone = span.Role == EventRole.Verb && !bright && !span.Own ? Ink : Hi;
                float emphasis = bright || span.Own ? 1f : 0.9f;
                x = Reveal(g, span.Text, x, cy, tone, alpha * emphasis, ref budget);
            }
            if (line.Truncated && budget > 0) Reveal(g, "+", x + EventLogLayout.Advance, cy, Dim, alpha, ref budget);
        }

        /// <summary>
        /// The first <paramref name="budget"/> characters of <paramref name="s"/> at (<paramref name="x"/>, <paramref name="cy"/>); returns where the next word goes. A DIM word fades as INK at DIM's own alpha: <c>ScopeCanvas.Text</c>
        /// keeps a DIM word's alpha at its readable floor, which a fade must go under.
        /// </summary>
        private static float Reveal(ScopeCanvas g, string s, float x, float cy, ScopeTone tone, float alpha, ref int budget)
        {
            int take = Math.Min(s.Length, budget);
            if (take <= 0 || alpha <= 0f) return x;
            budget -= take;
            ReadOnlySpan<char> shown = s.AsSpan(0, take);
            if (tone == Dim && alpha < 1f) g.Text(shown, x, cy, Ink, 12f, ScopeAlign.Left, false, alpha * ScopePalette.DimAlpha);
            else g.Text(shown, x, cy, tone, 12f, ScopeAlign.Left, false, alpha);
            return x + ScopeCanvas.TextW(shown);
        }

        /// <summary>Nothing of the log is up (the corner is the game's again, or there is no corner).</summary>
        private void StandDownLog()
        {
            LeftHudStack.LogBottom = 0f;
            if (logRoot != null) Show(logRoot, false);
            logCharge = false;
            logSig = 0;
        }

        private void DestroyLog()
        {
            LeftHudStack.LogBottom = 0f;
            if (LeftHudStack.Font == font) { LeftHudStack.Font = null; LeftHudStack.FontMaterial = null; }
            eventLog.Clear();      // the scene is going: its lines are not the next one's
            if (logRoot != null) UnityEngine.Object.Destroy(logRoot.gameObject);
            logRoot = logSurface = null;
            logGroup = null;
            logVector = null;
            logText = null;
            logCanvas = null;
            logPlan = default;
            logCount = 0;
            logLit = 0f;
            logCharge = false;
            logSig = 0;
        }
    }
}
