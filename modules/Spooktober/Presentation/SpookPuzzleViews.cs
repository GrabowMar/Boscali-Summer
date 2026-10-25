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
    /// What the puzzles and menu share: progress and their own message rows. Never writes SET's functional footer.
    /// </summary>
    internal sealed class SpookConsole
    {
        /// <summary>Real time. The offline check drives it by hand (the override is not in the public build).</summary>
#if !PUBLIC_RELEASE
        internal static Func<float> Clock = () => Time.unscaledTime;
#else
        internal static float Clock() => Time.unscaledTime;
#endif

        private readonly AvConsole console;
        private readonly List<Action> seals = new List<Action>(5);
        private readonly Dictionary<int, AvNote> messages = new Dictionary<int, AvNote>();
        private string line;
        private float lineUntil;
        private int messagePage;
        private int menuPage = -1;

        public SpookConsole(AvConsole console)
        {
            this.console = console;
            console.Ticker.Add(-1, AvTickRate.Slow, Tick);
        }

        /// <param name="menu">The boss menu's row: it shows the standing spawn hint between messages.</param>
        public void RegisterMessage(int page, AvFlow flow, bool menu = false)
        {
            messages[page] = flow.Add(new AvNote(flow.Content) { CollapseEmpty = true });
            if (menu) menuPage = page;
            Tick();
        }

        public void RegisterSeal(Action redraw) => seals.Add(redraw);

        /// <summary>Records a solve (once), saves it, updates the puzzle's message and redraws every seal line.</summary>
        public bool Solve(SpookPuzzle puzzle)
        {
            SpookProgress progress = SpookStore.Current;
            if (!progress.Solve(puzzle)) return false;
            SpookStore.Save();
            // ASCII noise: the console font has no block glyphs (they print as empty boxes in game).
            Say("#%: SEAL " + progress.SolvedCount + "/4" + (progress.Unlocked ? " \u00B7 SOMETHING IS LISTENING" : ""), 2f);
            foreach (Action redraw in seals) redraw();
            return true;
        }

        /// <summary>Shows <paramref name="text"/> in this page's Spooktober message row for a few seconds.</summary>
        public void Say(string text, float seconds)
        {
            line = text;
            lineUntil = Clock() + seconds;
            messagePage = console.CurrentPage;
            Tick();
        }

        // Every fourth 7-second slot the menu's hint gives way to one of these.
        private static readonly string[] Whispers =
        {
            "...is someone else on this net?",
            "Your wingman did not answer the last call.",
            "The kaiju was here first.",
            "Static on 131.0. Do not tune it.",
            "It knows which seat you fly from."
        };

        private void Tick()
        {
            float now = Clock();
            int slot = (int)(now / 7f);
            string hint = slot % 4 == 3
                ? Whispers[(slot / 4) % Whispers.Length]
                : SpookMenu.SpawnBlocked(GameAccess.IsServer())
                    ? "Only the host can spawn. Your chain still counts."
                    : "SPAWN drops a boss ahead of you. One at a time.";
            foreach (var message in messages)
                message.Value.Set(message.Key == messagePage && now < lineUntil ? line : message.Key == menuPage ? hint : "", AvState.Info);
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
            console.RegisterMessage(page, flow);
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
                sb.Append(p.Has((SpookPuzzle)i) ? '#' : '-');
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

        // ---- ECHO: an isolated lamp keys a word in Morse; no telemetry or shared chrome is touched ----

        private sealed class EchoLamp : AvPart
        {
            private readonly AvFrame bulb;
            private readonly AvRow row;
            private bool lit, solved;

            public EchoLamp(RectTransform parent)
            {
                Rect = AvLay.Child(parent, "Echo RX Indicator");
                row = new AvRow(Rect) { Parent = this };
                row.Help = "Receiver self-test. Short pulse = dot, long pulse = dash; a longer pause separates letters.";
                bulb = AvFrame.Add(row.Rect, "Bulb", AvChamfer.All(4f));
                Refresh();
            }

            public void Set(bool on)
            {
                on &= !solved;
                if (lit == on) return;
                lit = on;
                Refresh();
            }

            public void SetSolved(bool value)
            {
                if (solved == value) return;
                solved = value;
                if (solved) lit = false;
                Refresh();
            }

            private void Refresh()
            {
                row.Set("RX INDICATOR", solved ? "CALL SIGN CONFIRMED" :
                    "Short pulse = dot. Long pulse = dash. Enter the call sign.",
                    solved ? SpookPuzzles.EchoWord : lit ? "RX" : "IDLE", solved ? AvState.Ready : AvState.Info);
                Restyle();
            }

            public override float Measure(float width) => row.Measure(width);

            public override void Place(AvSlot s)
            {
                base.Place(s);
                row.Place(new AvSlot(0f, 0f, s.W, s.H));
                AvLay.Place(bulb.rectTransform, s.W - 94f, 8f, 12f, 12f);
            }

            public override void Restyle()
            {
                Color on = AvStyleHost.FuiColor("ready", AvTheme.RailReady);
                Color dim = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
                bulb.Paint(lit ? on : new Color(0.025f, 0.035f, 0.025f, 1f), lit ? on : dim);
                row.Restyle();
            }
        }

        private sealed class Echo
        {
            private readonly SpookConsole console;
            private readonly AvField field;
            private readonly EchoLamp lamp;
            private float started = -1f, lastTick = -1f;
            private string seen = "";

            public Echo(AvFlow flow, SpookConsole console, int page)
            {
                this.console = console;
                flow.Section(AvIcon.Activity, "LINK ECHO", "DIAGNOSTIC");
                lamp = flow.Add(new EchoLamp(flow.Content));
                field = flow.Add(new AvField(flow.Content, "CALL SIGN", 12, v => Submit(v, true)));
                Refresh();
                flow.Ticker.Add(page, AvTickRate.Slow, Tick);
                flow.Ticker.Add(page, AvTickRate.Fast, Blink);
            }

            private void Blink()
            {
                float now = SpookConsole.Clock();
                if (started < 0f || now - lastTick > 0.5f || now < lastTick) started = now + 1.2f;
                lastTick = now;
                lamp.Set(!SpookStore.Current.Has(SpookPuzzle.Echo) && SpookPuzzles.EchoLampLit(now - started));
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
                lamp.SetSolved(solved);
                field.SetShown(!solved);
            }
        }
    }
}
