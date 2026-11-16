using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Hud.Patches;
using BoscaliSummer.Modules.Hud.Runtime;
using NOAvionics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static BoscaliSummer.Core.Contracts.ScopeTone;

namespace BoscaliSummer.Modules.Hud.Presentation
{
    // The CUE stack (HUD holo pass E, 2026-10-13): the game's short messages on grey boxes (the action report: weapon safety, flight assist, radar, gear, damage failures; the reserve notice; the funds line; the score line), drawn by the mod
    // at the LEFT edge in the visor's language, in place of the game's boxes. The messages are captured as the game shows them (Patches/CueFeedPatch.cs: text, the game's time for it, whether its colour marks a failure, into CueFeed.Log, the
    // newest four) and painted here: a stack 12 units in from the left and 281 wide (the CUE zone, HudZones: 299 down, lower when COMMS grew past its zone), newest on top, at most three, 40 apart.
    // The NOTICE line (2026-11-16) shares this tree: the board's notice ticker, one line under the stack (the NOTICE zone, or 16 under the last cue), drawn whether the board is on or not; the Corners option and the gate govern it as they do the cues. One cue is a DIM class word on a short
    // captioned shelf (SYS, WPN, DMG, FUNDS, RSV) and under it the message in regular capitals, BRIGHT at 16 (AMBER for a caution, RED for an alert; 14, then whole words, when it is long), with a soft shade behind it. A cue SHOWS for 0.32 s
    // (two bracket halves snap in from 46 units outside, the message decodes left to right, an underline draws, the whole cue is a tone brighter for 0.12 s), HOLDS for as long as the game keeps its own line (at least 1.2 s) and FADES for
    // the last 0.35 s (the underline retracts, the brackets spread out, the words flicker 1, 0.55, 0.85, 0.3, 0); the others slide down a slot over 0.12 s when a new one comes (Domain/CueTimeline.cs: everything is a function of the cue's age).
    // The game's own boxes are hidden through NativeCues (owned CanvasGroups, alpha 0) while their cues are drawn, one box at a time: a box that was not found, or whose text the capture never saw (CueLog.Healthy), stays the game's. The
    // Corners option governs the whole port (off: the game's boxes exactly as they are); Lean has no shelf or class word, Minimal only the AMBER and RED cues. The ink has its own group whose alpha is the pilot's Opacity;
    // it shares the left column's one faint shadow (StatusPanel.Corners), follows sway and the device pixel grid, and redraws only when something moved a step (CueLog.Signature).
    internal sealed partial class StatusPanel
    {
        private const int CueQuads = 120;

        /// <summary>What the last paint drew for one cue (the offline check reads it): the cue, the top of its slot, where its words start and how wide they are, where its bracket halves stood, its alpha and how many characters had locked.</summary>
        internal struct CueSpot
        {
            public Cue Cue;
            public float Y, TextX, TextW, LeftBar, RightBar, Alpha;
            public int Locked;
        }

        private readonly CueLog cueLog = CueFeed.Log;
        private readonly NativeCues cueNatives = new NativeCues();
        private readonly Cue[] cueShown = new Cue[CueLog.MaxShown];
        private readonly CueSpot[] cueSpots = new CueSpot[CueLog.MaxShown];
        private readonly char[] cueChars = new char[96];
        private RectTransform cueRoot, cueSurface;
        private float cueBottom, cueLit;
        private CanvasGroup cueGroup;
        private AvVector cueVector;
        private ScopeText cueText;
        private ScopeCanvas cueCanvas;
        /// <summary>How many cues the last paint drew, which sources were in charge, and the signature it was drawn for.</summary>
        private int cueCount, cueCharge, cueSig;
        private Vector2 seenCueCanvas;
        /// <summary>The offline check's own clock for the cues (NaN: the game's, <c>Time.timeSinceLevelLoad</c>).</summary>
        private float cueClockOverride = float.NaN;
        /// <summary>The native boxes are looked for in the game until all are held (a stand-in render never looks).</summary>
        private bool cueFinding = true;
        private float cueFindAt;

        // The NOTICE line: the ticker's current notice (copied from the board's frame at every present, so it is current with the board off), whether the last paint drew it and where, and the words' cache.
        private TickerLine noticeLine;
        private bool noticeHas, noticeOn;
        private string tickerWords, tickerCount, tickerSourceText;
        private int tickerSourceCount, tickerSourcePhase;
        private HudTone tickerSourceTone;

        private float CueClock() => float.IsNaN(cueClockOverride) ? Time.timeSinceLevelLoad : cueClockOverride;

        /// <summary>How many cues the stack drew at its last paint, and which boxes' cues are in charge (bit <see cref="CueSource"/>): what the sim hook reports.</summary>
        internal int CueDrawn => cueCount;
        internal int CueInCharge => cueCharge;

        /// <summary>
        /// Every frame (<paramref name="tick"/> false) and at the end of every present (true), with the corners in charge: work out which boxes the cues are standing in for (the box is held and its capture is seeing the game's
        /// messages), draw the stack again when a cue moved a step (or on a tick while one is up), and hide exactly those boxes while the stack is on the screen. Fail open: a paint that throws gives every box back this frame.
        /// </summary>
        private void CueFrame(bool tick)
        {
            float clock = CueClock();
            cueLog.Tick(clock);
            if (cueFinding && !cueNatives.Complete && Time.unscaledTime >= cueFindAt)
            {
                cueFindAt = Time.unscaledTime + 1f;
                try { cueNatives.Find(); }
                catch (Exception e) { cueFinding = false; BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Hud.Cues.Find", e); }      // the game's boxes stay the game's
            }
            int charge = 0;
            for (int s = 0; s < 4; s++)
            {
                var source = (CueSource)s;
                if (cueNatives.Held(source) && cueLog.Healthy(source, clock, cueNatives.Showing(source))) charge |= 1 << s;
            }
            CueFeed.Charge = charge;
            BoardDeclutter level = Options.Declutter;
            int shown = charge == 0 ? 0 : cueLog.Select(clock, level, charge, cueShown);
            int signature = CueLog.Signature(clock, level, charge, cueShown, shown) * 31 + (int)(Mathf.Clamp01(Options.Shade) * 100f);
            Vector2 canvasSize = canvasRect.rect.size;
            signature = signature * 31 + Mathf.RoundToInt(LeftHudStack.CueTop);
            noticeOn = noticeHas && Options.ShowTicker;
            signature = signature * 31 + NoticeSignature();
            signature = signature * 31 + (routeLive ? 1 : 0);      // the NOTICE line yields to a ROUTE face (decided at this present, before this paint)
            if (charge != cueCharge || signature != cueSig || canvasSize != seenCueCanvas || (tick && (shown > 0 || noticeOn)))
            {
                cueCharge = charge;
                cueSig = signature;
                seenCueCanvas = canvasSize;
                try
                {
                    if (charge == 0 && !noticeOn) HideCueTree();
                    else PaintCues(clock, shown);
                }
                catch (Exception e)
                {
                    StandDownCues();
                    BoscaliSummer.Core.Diagnostics.PatchGuard.Report("Hud.Cues", e);
                }
            }
            RouteFrame(tick);      // under the NOTICE line, so after the cues have said where that ends
            // The game's boxes are hidden only while our stack is up in the HUD (a hidden HUD canvas takes it with it and gives them back).
            bool up = cueCharge != 0 && cueRoot != null && cueRoot.gameObject.activeInHierarchy;
            for (int s = 0; s < 4; s++) cueNatives.Hide((CueSource)s, up && (cueCharge >> s & 1) != 0);
        }

        private void HideCueTree()
        {
            if (cueRoot != null) Show(cueRoot, false);
            cueBottom = cueLit = 0f;
            cueCount = 0;
        }

        /// <summary>Nothing of the stack is up and every game box is the game's again.</summary>
        private void StandDownCues()
        {
            CueFeed.Charge = 0;
            HideCueTree();
            for (int s = 0; s < 4; s++) cueNatives.Hide((CueSource)s, false);
            cueCharge = 0;
            cueSig = 0;
        }

        private bool EnsureCueTree()
        {
            if (cueRoot != null) return true;
            if (canvasRect == null) return false;
            cueRoot = CornerRoot("BoscaliCues");
            cueGroup = cueRoot.gameObject.AddComponent<CanvasGroup>();
            cueGroup.ignoreParentGroups = true;      // the pilot's opacity is this group's alone, as the board's is
            cueGroup.interactable = cueGroup.blocksRaycasts = false;
            cueSurface = Pinned(cueRoot, "Cues", false);
            cueVector = AvVector.Create(cueSurface, "Strokes", CueQuads);
            cueText = new ScopeText(Holder(cueSurface, "Words"), font, fontMaterial);
            cueCanvas = new ScopeCanvas(cueVector.Buffer, cueText, "scope.cues");
            PlaceCornerRoots();
            return true;
        }

        /// <summary>Lay out and draw the <paramref name="shown"/> cues of <see cref="cueShown"/>, newest on top, at <paramref name="clock"/>.</summary>
        private void PaintCues(float clock, int shown)
        {
            if (!EnsureCueTree()) return;
            float k = 1f / baseUnit, canvasW = canvasRect.rect.width, canvasH = canvasRect.rect.height;
            float top = LeftHudStack.CueTop;
            bool furniture = Options.Declutter == BoardDeclutter.Full;
            Show(cueRoot, true);
            PlaceOnGrid(cueSurface, 0f, 0f, Whole(canvasW), Whole(canvasH));
            ScopeCanvas g = cueCanvas;
            g.Furniture = furniture;
            g.Begin(Palette(), baseUnit, Whole(canvasH), pixel);
            float inkBottom = 0f, lit = 0f;
            for (int i = 0; i < CueLog.MaxShown; i++)
            {
                cueSpots[i] = default;
                if (i >= shown) continue;
                float y = Mathf.Round(top + CueLayout.Offset(cueShown, i, clock));
                DrawCue(g, i, cueShown[i], y, clock, furniture, out float alpha);
                if (alpha <= 0f) continue;
                inkBottom = Mathf.Max(inkBottom, y + CueLayout.Pitch - 3f);
                lit = Mathf.Max(lit, alpha);
            }
            if (noticeOn)
            {
                ZoneRect zone = HudZones.Ref(HudZone.Notice, canvasW * k);
                float noticeTop = Mathf.Round(HudZones.NoticeTop(inkBottom));
                if (HudZones.NoticeFits(noticeTop, routeLive)) PaintNotice(g, zone.X, noticeTop, zone.W, furniture);      // else the ROUTE slot has the room: it comes back by itself when there is some
            }
            g.End();
            cueVector.Commit();
            cueCount = shown;
            cueBottom = inkBottom;
            cueLit = lit;
        }

        /// <summary>The ticker's words and its repeat count, rebuilt only when the notice, its count, its phase or its tone changed.</summary>
        private string TickerWords(in TickerLine line)
        {
            int phase = NoticeTicker.Phase(line);
            if (tickerWords == null || !ReferenceEquals(tickerSourceText, line.Text) || tickerSourceCount != line.Count ||
                tickerSourcePhase != phase || tickerSourceTone != line.Tone)
            {
                tickerSourceText = line.Text;
                tickerSourceCount = line.Count;
                tickerSourcePhase = phase;
                tickerSourceTone = line.Tone;
                // A warning says so in words, not only in red.
                tickerWords = (line.Tone == HudTone.Warning ? "WARNING · " : "") + NoticeTicker.Words(line).ToUpperInvariant();
                tickerCount = NoticeTicker.CountWord(line);
            }
            return tickerWords;
        }

        /// <summary>A beat of a pulsing notice (a sticky warning, a fresh one): the board's flash phase, on for half of each cycle.</summary>
        private static bool NoticeFlashOn() => ((int)(Time.unscaledTime * FlashHz * 2f) & 1) == 0;

        /// <summary>What the NOTICE line looks like now, as one number (0: not drawn): its words, count and tone, and the beat while it pulses. A change of it is a reason to paint.</summary>
        private int NoticeSignature()
        {
            if (!noticeOn) return 0;
            TickerLine line = noticeLine;
            string words = TickerWords(line);
            bool pulse = line.Tone == HudTone.Warning || NoticeTicker.IsFresh(line);
            return unchecked(((words.GetHashCode() * 31 + (tickerCount?.GetHashCode() ?? 0)) * 31 + (int)line.Tone) * 31 + (pulse ? (NoticeFlashOn() ? 2 : 1) : 0));
        }

        /// <summary>
        /// The notice as one caption-shelf line at (<paramref name="x"/>, <paramref name="y"/>), <paramref name="w"/> wide: the shelf with NOTICE, then a tone tick, the words in bold in the same tone and the repeat count in bold at the
        /// right end (the board's old ticker row, moved). A sticky warning pulses. Lean and Minimal have no shelf: the tick and the words start at the left edge.
        /// </summary>
        private void PaintNotice(ScopeCanvas g, float x, float y, float w, bool furniture)
        {
            TickerLine line = noticeLine;
            ScopeTone tone = SystemsMath.ToneOf(line.Tone);
            bool dip = line.Tone == HudTone.Warning && !NoticeFlashOn();
            float cy = y + 6f, shelfW = furniture ? Mathf.Ceil(CueLayout.TextInset + ScopeCanvas.TextW("NOTICE") + 20f) : 0f;
            if (furniture) g.CaptionShelf(x, y, shelfW, "NOTICE");
            float tx = furniture ? x + shelfW + 12f : x, room = x + w - tx - 10f;
            g.ToneTick(tx, y, tone, dip ? 0.55f : 1f);
            if (tickerCount != null)
            {
                g.Text(tickerCount, x + w, cy, tone, 12f, ScopeAlign.Right, true);
                room -= ScopeCanvas.TextW(tickerCount) + 10f;
            }
            g.FitText(TickerWords(line), tx + 10f, cy, room, tone, 12f, 12f, ScopeAlign.Left, true, dip ? 0.8f : 1f);
        }

        /// <summary>
        /// One cue with its slot's top at <paramref name="y"/>: the shelf with the class word, the bracket halves, the underline and the message as far as it has decoded, each as the cue's age says. The shelf is DIM (INK in
        /// the first 0.12 s), the brackets and underline DIM (or the cue's AMBER / RED), the words in the cue's tone; a tone brighter for the first 0.12 s; everything at the fade's alpha (<paramref name="alpha"/>; a cue at 0 draws
        /// nothing). Returns how far right its ink reaches at rest, design units.
        /// </summary>
        private float DrawCue(ScopeCanvas g, int place, Cue cue, float y, float clock, bool furniture, out float alpha)
        {
            float age = cue.Age(clock), life = cue.Life;
            alpha = CueTimeline.Alpha(age, life);
            if (alpha <= 0f) return 0f;
            bool flash = CueTimeline.Flash(age);
            ScopeTone words = CueTimeline.ToneOf(cue.Severity), line = cue.Severity == CueSeverity.Normal ? Dim : words, shelf = Dim;
            if (flash) { words = CueTimeline.Raise(words); line = CueTimeline.Raise(line); shelf = CueTimeline.Raise(shelf); }
            int length = CueDecode.Fill(cueChars, cue.Text, age, cue.Seed);
            float textW = ScopeCanvas.TextW(cue.Text.AsSpan(0, length), cue.Size), x = CueLayout.Left, tx = x + CueLayout.TextInset, lift = furniture ? 0f : CueLayout.BareLift;
            float row = y + CueLayout.MessageRow - lift, under = y + CueLayout.UnderlineRow - lift, barTop = y + CueLayout.BracketTop - lift, barBottom = y + CueLayout.BracketBottom - lift;
            float hugL = tx - CueLayout.HugGap, hugR = tx + textW + CueLayout.HugGap, offset = CueTimeline.BracketOffset(age, life), lBar = hugL - offset, rBar = hugR + offset;
            string word = CueWords.Of(cue.Class);
            float shelfW = CueLayout.ShelfWidth(word.Length);

            if (furniture) g.FadingShelf(x, y + CueLayout.ShelfRow - 6f, shelfW, word, shelf, alpha);      // CaptionShelf puts its line 6 under the y it is given
            // Two bracket halves: a bar with an arm at each end, pointing at the words.
            g.Line(lBar, barTop, lBar, barBottom, line, alpha);
            g.Line(lBar, barTop, lBar + CueLayout.Arm, barTop, line, alpha);
            g.Line(lBar, barBottom, lBar + CueLayout.Arm, barBottom, line, alpha);
            g.Line(rBar, barTop, rBar, barBottom, line, alpha);
            g.Line(rBar, barTop, rBar - CueLayout.Arm, barTop, line, alpha);
            g.Line(rBar, barBottom, rBar - CueLayout.Arm, barBottom, line, alpha);
            CueTimeline.Underline(age, life, out float ulFrom, out float ulTo);
            if (ulTo - ulFrom > 0.001f) g.Line(hugL + (hugR - hugL) * ulFrom, under, hugL + (hugR - hugL) * ulTo, under, line, line == Dim ? alpha : alpha * 0.55f);
            g.Text(new ReadOnlySpan<char>(cueChars, 0, length), tx, row, words, cue.Size, ScopeAlign.Left, false, alpha);
            cueSpots[place] = new CueSpot { Cue = cue, Y = y, TextX = tx, TextW = textW, LeftBar = lBar, RightBar = rBar, Alpha = alpha, Locked = CueTimeline.Locked(age, cue.Text.Length) };
            return Math.Max(hugR, furniture ? x + shelfW + 8f : 0f);
        }

        private void DestroyCues()
        {
            CueFeed.Charge = 0;
            cueNatives.Dispose();
            cueLog.Clear();      // the scene is going: its messages are not the next one's
            if (cueRoot != null) UnityEngine.Object.Destroy(cueRoot.gameObject);
            cueRoot = cueSurface = null;
            cueBottom = cueLit = 0f;
            cueGroup = null;
            cueVector = null;
            cueText = null;
            cueCanvas = null;
            cueCount = cueCharge = cueSig = 0;
            noticeHas = noticeOn = false;
            seenCueCanvas = default;
            Array.Clear(cueSpots, 0, cueSpots.Length);
        }
    }
}
