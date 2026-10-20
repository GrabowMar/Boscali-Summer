using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Spooktober.Domain;
using BoscaliSummer.Modules.Spooktober.Runtime;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Presentation
{
    /// <summary>
    /// What the four puzzles and the menu share on one SET console: the solve, the footer lines they borrow (the solve flash, the ECHO blink,
    /// the menu's idle line) and the seal lines to redraw when a puzzle falls.
    /// </summary>
    internal sealed class SpookConsole
    {
        public const int EchoPage = 4, MenuPage = 5;

        /// <summary>Real time. The offline check drives it by hand (the override is not in the public build).</summary>
#if !PUBLIC_RELEASE
        internal static Func<float> Clock = () => Time.unscaledTime;
#else
        internal static float Clock() => Time.unscaledTime;
#endif

        private readonly AvConsole console;
        private readonly List<Action> seals = new List<Action>(5);
        private string line;
        private float lineUntil, nextBlink = -1f, blinkUntil;
        private bool armed;

        public SpookConsole(AvConsole console)
        {
            this.console = console;
            // Command refreshes the footer on its own Slow tick, and it registers that tick after the extras are built, so anything set
            // earlier in the same pass is overwritten before it is drawn. The first tick runs after the build: it adds the real driver behind Command's.
            console.Ticker.Add(-1, AvTickRate.Slow, Arm);
        }

        private void Arm()
        {
            if (armed) return;
            armed = true;
            console.Ticker.Add(-1, AvTickRate.Slow, Tick);
        }

        public void RegisterSeal(Action redraw) => seals.Add(redraw);

        /// <summary>Records a solve (once), saves it, flashes the footer and redraws every seal line.</summary>
        public bool Solve(SpookPuzzle puzzle)
        {
            SpookProgress progress = SpookStore.Current;
            if (!progress.Solve(puzzle)) return false;
            SpookStore.Save();
            Say("\u2593\u2592\u2591 SEAL " + progress.SolvedCount + "/4" + (progress.Unlocked ? " \u00B7 SOMETHING IS LISTENING" : ""), 2f);
            foreach (Action redraw in seals) redraw();
            return true;
        }

        /// <summary>Shows <paramref name="text"/> in the footer for a few seconds.</summary>
        public void Say(string text, float seconds)
        {
            line = text;
            lineUntil = Clock() + seconds;
            console.Footer.Set(text, AvState.Info);
        }

        private void Tick()
        {
            float now = Clock();
            if (now < lineUntil) { console.Footer.Set(line, AvState.Info); return; }
            int page = console.CurrentPage;
            if (page == EchoPage && !SpookStore.Current.Has(SpookPuzzle.Echo))
            {
                if (nextBlink < 0f) nextBlink = now + 2.5f;   // a moment after the page opens, then every 20 s
                if (now >= nextBlink) { blinkUntil = now + 4f; nextBlink = now + 20f; }
                if (now < blinkUntil) console.Footer.Set(SpookPuzzles.Morse(SpookPuzzles.EchoWord));
            }
            else nextBlink = -1f;
            if (page == MenuPage)
                console.Footer.Set(SpookMenu.SpawnBlocked(GameAccess.IsServer())
                    ? "Only the host can spawn. Your chain still counts."
                    : "SPAWN drops a boss ahead of you. One at a time.");
        }
    }

    /// <summary>
    /// The four puzzles, built from the same rows, cells and fields as the settings around them. Each one is appended to the end of its page
    /// (also when the page shows a stub), reads the answer from <see cref="SpookPuzzles"/>, and locks as SOLVED.
    /// </summary>
    internal static class SpookPuzzleViews
    {
        public const int MapPage = 1, CockpitPage = 2, ImmersionPage = 3, PerfPage = 4;

        public static void Build(int page, AvFlow flow, SpookConsole console)
        {
            switch (page)
            {
                case MapPage: new Cipher(flow, console, page); break;
                case CockpitPage: new Breaker(flow, console, page); break;
                case ImmersionPage: new Signal(flow, console, page); break;
                case PerfPage: new Echo(flow, console, page); break;
            }
            Seal(flow, console, page);
        }

        // ---- SEAL: one block per puzzle (full = solved, light = open) on every PILOT page; nothing shows until the first one falls ----
        // The spec's diamonds are not in the kit's fonts (they draw as boxes); the block elements the footer flash uses are.

        private static void Seal(AvFlow flow, SpookConsole console, int page)
        {
            var note = flow.Add(new AvNote(flow.Content, AvTextRole.DataSmall));
            void Redraw()
            {
                SpookProgress p = SpookStore.Current;
                note.SetShown(p.SolvedCount > 0);
                note.Set(SealText(p));
            }
            Redraw();
            console.RegisterSeal(Redraw);
            flow.Ticker.Add(page, AvTickRate.Slow, Redraw);
        }

        internal static string SealText(SpookProgress p)
        {
            if (p.SolvedCount == 0) return "";
            var sb = new StringBuilder("SEAL  ");
            for (int i = 0; i < 4; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(p.Has((SpookPuzzle)i) ? '\u2588' : '\u2591');
            }
            return sb.ToString();
        }

        // ---- SIGNAL: SCAN FREQ on IMMERSION ----

        private sealed class Signal
        {
            private const int Start = 1215, Step = 5;   // 121.5 MHz (guard), half a MHz a click
            // The ends sit on the click grid (SignalMax = 1369 is not on it): a clamp to the raw bound would knock every later click off the
            // grid and leave 131.0 unreachable until the console is rebuilt.
            private const int Lo = (SpookPuzzles.SignalMin + Step - 1) / Step * Step, Hi = SpookPuzzles.SignalMax / Step * Step;
            private readonly SpookConsole console;
            private readonly AvRow freq, intercept;
            private readonly AvControl minus, plus;
            private int tenths = Start;
            private float held = -1f, last = -1f;

            public Signal(AvFlow flow, SpookConsole console, int page)
            {
                this.console = console;
                flow.Section(AvIcon.Radio, "RADIO TUNER", "SCAN \u00B7 SQUELCH");
                freq = new AvRow(flow.Content);
                minus = freq.AddTrailing(new AvControl.Spec("", () => Nudge(-1), AvButtonStyle.Quiet, AvIcon.Minus));
                plus = freq.AddTrailing(new AvControl.Spec("", () => Nudge(1), AvButtonStyle.Quiet, AvIcon.Plus));
                freq.Help = "Receiver scan frequency in MHz.";
                flow.Add(freq);
                intercept = flow.Add(new AvRow(flow.Content));
                Refresh();
                flow.Ticker.Add(page, AvTickRate.Slow, Tick);
            }

            private static string Format(int t) => (t / 10).ToString(CultureInfo.InvariantCulture) + "." + (t % 10).ToString(CultureInfo.InvariantCulture);

            private bool Solved => SpookStore.Current.Has(SpookPuzzle.Signal);

            private void Nudge(int d)
            {
                if (Solved) return;
                tenths = Mathf.Clamp(tenths + d * Step, Lo, Hi);
                float now = SpookConsole.Clock();
                held = tenths == SpookPuzzles.SignalTenths ? now : -1f;
                last = now;
                Refresh();
            }

            /// <summary>Held time only counts while this page is open: a gap between ticks means it was closed, so the hold starts over.</summary>
            private void Tick()
            {
                float now = SpookConsole.Clock();
                if (tenths != SpookPuzzles.SignalTenths) held = -1f;
                else if (held < 0f || now - last > 1.5f) held = now;
                last = now;
                if (!Solved && held >= 0f && SpookPuzzles.SignalSolved(tenths, now - held)) console.Solve(SpookPuzzle.Signal);
                Refresh();
            }

            private void Refresh()
            {
                SpookProgress p = SpookStore.Current;
                bool solved = p.Has(SpookPuzzle.Signal);
                if (solved) tenths = SpookPuzzles.SignalTenths;
                bool onTarget = tenths == SpookPuzzles.SignalTenths;
                minus.Interactable = !solved && tenths > Lo;
                plus.Interactable = !solved && tenths < Hi;
                string text = Format(tenths);
                minus.Help = "Receiver scan frequency in MHz. Tune down. " + text;
                plus.Help = "Receiver scan frequency in MHz. Tune up. " + text;
                freq.Set("SCAN FREQ", solved ? "SOLVED" : onTarget ? "CARRIER DETECTED" : "", text,
                    solved ? AvState.Ready : onTarget ? AvState.Caution : AvState.Info);
                intercept.SetShown(p.HeardIntercept);
                if (p.HeardIntercept) intercept.Set("LAST INTERCEPT", InterceptText(), "", AvState.Caution);
            }
        }

        private static string interceptCache;

        /// <summary>The transmission with the noise of a bad line on it; the carrier tone that holds the clue is left readable.</summary>
        internal static string InterceptText()
        {
            if (interceptCache != null) return interceptCache;
            string t = SpookText.Transmission;
            int clue = t.IndexOf("Carrier tone", StringComparison.Ordinal);
            return interceptCache = clue < 0 ? SpookText.Glitch(t, 31, 0.06f) : SpookText.Glitch(t.Substring(0, clue), 31, 0.1f) + t.Substring(clue);
        }

        // ---- CIPHER: a corrupted label and a field on MAP ----

        private sealed class Cipher
        {
            private readonly SpookConsole console;
            private readonly AvRow row;
            private readonly AvField field;
            private string seen = "";

            public Cipher(AvFlow flow, SpookConsole console, int page)
            {
                this.console = console;
                flow.Section(AvIcon.Typography, "LABEL CACHE", "RECOVERY");
                row = flow.Add(new AvRow(flow.Content));
                row.Help = "ROT " + SpookPuzzles.CipherShift;
                field = flow.Add(new AvField(flow.Content, "ROT " + SpookPuzzles.CipherShift, 12, v => Submit(v, true)));
                Refresh();
                flow.Ticker.Add(page, AvTickRate.Slow, Tick);
            }

            private void Submit(string text, bool enter)
            {
                if (SpookStore.Current.Has(SpookPuzzle.Cipher)) return;
                if (SpookPuzzles.CipherSolved(text)) { console.Solve(SpookPuzzle.Cipher); Refresh(); }
                else if (enter) { seen = field.Text; field.Invalid = true; }
            }

            /// <summary>Typing the word is enough; Enter only matters for the red edge of a wrong one.</summary>
            private void Tick()
            {
                string text = field.Text;
                if (text != seen) { seen = text; field.Invalid = false; Submit(text, false); }
                Refresh();
            }

            private void Refresh()
            {
                bool solved = SpookStore.Current.Has(SpookPuzzle.Cipher);
                row.Set(solved ? SpookPuzzles.CipherWord : SpookPuzzles.CipherLabel,
                    solved ? "SOLVED" : "Label corrupted. Re-enter it to restore.", "", solved ? AvState.Ready : AvState.Info);
                field.SetShown(!solved);
            }
        }

        // ---- BREAKER: six SENSOR BANK toggles on COCKPIT ----

        private sealed class Breaker
        {
            private readonly SpookConsole console;
            private readonly AvSection section;
            private readonly AvCell[] cells = new AvCell[SpookPuzzles.BreakerCount];
            private int state = SpookPuzzles.BreakerStart;

            public Breaker(AvFlow flow, SpookConsole console, int page)
            {
                this.console = console;
                section = flow.Section(AvIcon.Gauge, "SENSOR BANKS", "SELF-TEST");
                AvCellGrid grid = flow.Grid(3);
                for (int i = 0; i < cells.Length; i++)
                {
                    int bank = i;
                    cells[i] = grid.Toggle("SENSOR BANK " + (i + 1), "", () => On(bank), _ => Press(bank));
                    cells[i].Help = "Sensor bank " + (i + 1) + " self-test relay.";
                }
                Refresh();
                flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
            }

            private bool Solved => SpookStore.Current.Has(SpookPuzzle.Breaker);

            private bool On(int bank) => Solved || ((state >> bank) & 1) != 0;

            private void Press(int bank)
            {
                if (Solved) return;
                state = SpookPuzzles.BreakerPress(state, bank);
                if (SpookPuzzles.BreakerSolved(state)) console.Solve(SpookPuzzle.Breaker);
                Refresh();
            }

            private void Refresh()
            {
                foreach (AvCell cell in cells) cell.Refresh();
                section.SetCaption(Solved ? "SOLVED" : "SELF-TEST");
            }
        }

        // ---- ECHO: the footer keys a word in Morse; a field on PERFORMANCE takes it ----

        private sealed class Echo
        {
            private readonly SpookConsole console;
            private readonly AvRow row;
            private readonly AvField field;
            private string seen = "";

            public Echo(AvFlow flow, SpookConsole console, int page)
            {
                this.console = console;
                flow.Section(AvIcon.Activity, "LINK ECHO", "DIAGNOSTIC");
                row = flow.Add(new AvRow(flow.Content));
                row.Help = "Keyed on the status line while this page is open.";
                field = flow.Add(new AvField(flow.Content, "CALL SIGN", 12, v => Submit(v, true)));
                Refresh();
                flow.Ticker.Add(page, AvTickRate.Slow, Tick);
            }

            private void Submit(string text, bool enter)
            {
                if (SpookStore.Current.Has(SpookPuzzle.Echo)) return;
                if (SpookPuzzles.EchoSolved(text)) { console.Solve(SpookPuzzle.Echo); Refresh(); }
                else if (enter) { seen = field.Text; field.Invalid = true; }
            }

            private void Tick()
            {
                string text = field.Text;
                if (text != seen) { seen = text; field.Invalid = false; Submit(text, false); }
                Refresh();
            }

            private void Refresh()
            {
                bool solved = SpookStore.Current.Has(SpookPuzzle.Echo);
                row.Set("ECHO TEST", solved ? "SOLVED" : "The status line keys a call sign. Enter it.", solved ? SpookPuzzles.EchoWord : "",
                    solved ? AvState.Ready : AvState.Info);
                field.SetShown(!solved);
            }
        }
    }
}
