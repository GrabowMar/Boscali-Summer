using NOAvionics;
using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The TAC BOARD's options on the COCKPIT tab, written only through <see cref="ITacBoardSettings"/> (the Hud module owns the board and its config; this console never
    /// imports it). Four groups: the board itself, its manual pages, its major parts and the extras. Client-local: the board takes a change on its next 10 Hz tick, so a pilot sees it
    /// in flight and there is no preview here. Every control but RESET and the kill feed and map restyle (which stay when the board is off) waits on the board's own switch, as the immersion page waits on its master.
    /// </summary>
    internal sealed partial class SettingsMfdPanel
    {
        // Index = the enum's value, so the controls write a choice's index straight to the contract.
        private static readonly string[] BoardLayoutNames = { "WIDE", "STACK" };
        private static readonly string[] BoardLayoutBriefs =
        {
            "WIDE: spreads the board along the top edge of the screen.",
            "STACK: the classic narrow column.",
        };
        private static readonly string[] BoardDeclutterNames = { "FULL", "LEAN", "MIN" };
        private static readonly string[] BoardDeclutterBriefs =
        {
            "FULL: everything the switches below allow.",
            "LEAN: no gauge row, dark lamps or captions.",
            "MIN: only lock, spike and missile lamps; your selected pages stay visible.",
        };
        private static readonly string[] BoardPageCountNames = { "1", "2", "3", "4" };
        private static readonly string[] BoardPageCountBriefs =
        {
            "Show one full page.",
            "Show two pages: side by side in WIDE, one above the other in STACK.",
            "Show three pages: two above, one under them.",
            "Show four pages, two by two.",
        };
        /// <summary>In <see cref="BoardPage"/> order; SYSTEMS keeps its place but is never offered (<see cref="StepPage"/>): the board always shows it.</summary>
        private static readonly string[] BoardPageNames =
        {
            "RADAR WARNING", "SHOT", "MISSILE CAM", "APPROACH", "WING", "STRIKE", "DOCK", "OBJECTIVE", "SYSTEMS", "CARGO",
        };

        private void BuildTacBoardRows(AvFlow flow, int page)
        {
            ITacBoardSettings board = ModuleServices.TryGet(out ITacBoardSettings svc) ? svc : null;
            if (board == null)
            {
                flow.Section(AvIcon.Target, "TAC BOARD", "UNAVAILABLE");
                flow.Add(new AvNote(flow.Content) { MinHeight = 18f }).Set("TAC BOARD NOT INSTALLED");
                return;
            }

            Func<bool> on = () => board.Enabled;
            Func<string> off = () => "Turn on TAC BOARD first.";

            flow.Section(AvIcon.Target, "TAC BOARD", "BOARD · LAYOUT · LOOK");
            ToggleCell(flow, flow.Grid(1), page, "TAC BOARD",
                "Off hides the board and gives back the game's own weapon, countermeasure and power panels.",
                () => board.Enabled, v => board.Enabled = v);
            BoardStrip(flow, page, "LAYOUT", BoardLayoutNames, BoardLayoutBriefs,
                () => (int)board.Layout, i => board.Layout = (BoardLayout)i, on, off);
            BoardStrip(flow, page, "DECLUTTER", BoardDeclutterNames, BoardDeclutterBriefs,
                () => (int)board.Declutter, i => board.Declutter = (BoardDeclutter)i, on, off);

            var look = flow.Add(new SetRingRow(flow.Content));
            BoardRing(flow, look, page, "SIZE", () => board.Scale, v => board.Scale = v, 0.6f, 1.4f, .05f, v => AvNum.Percent(v),
                "How big the whole board is.", on, off);
            BoardRing(flow, look, page, "OPACITY", () => board.Opacity, v => board.Opacity = v, 0.25f, 1f, .05f, v => AvNum.Percent(v),
                "How solid the board's ink reads against the sky.", on, off);
            BoardRing(flow, look, page, "MOVE LEFT", () => board.OffsetX, v => board.OffsetX = v, 0f, 1200f, 20f, Units,
                "Slides the board left from the right screen edge, 20 units a step (a unit is a 1080p pixel).", on, off);
            BoardRing(flow, look, page, "MOVE DOWN", () => board.OffsetY, v => board.OffsetY = v, 0f, 600f, 20f, Units,
                "Slides the board down from the top screen edge, 20 units a step (a unit is a 1080p pixel).", on, off);
            BoardRing(flow, look, page, "SHADE", () => board.Shade, v => board.Shade = v, 0f, 1f, .05f, v => AvNum.Percent(v),
                "The dark band behind the symbols. 0% draws none (a restyled kill feed has no dark box then either).", on, off);
            BoardRing(flow, look, page, "RWR HOLD", () => board.ThreatHoldSeconds, v => board.ThreatHoldSeconds = v, 2f, 30f, 1f, v => AvNum.Seconds(v, 0),
                "Seconds radar warning keeps a contact after its last warning.", on, off);

            flow.Section(AvIcon.Radar2, "TAC BOARD PAGES", "MANUAL · UP TO 2 x 2");
            Func<bool> pagesOn = () => board.Enabled && board.ScopeMode != BoardScopeMode.Off;
            Func<string> pagesOff = () => board.Enabled ? "Turn on TACTICAL PAGES first." : off();
            BoardStrip(flow, page, "FULL PAGES", BoardPageCountNames, BoardPageCountBriefs,
                () => board.PageCount - 1, i => board.PageCount = i + 1, pagesOn, pagesOff);
            BoardPageChoice(flow, page, "PAGE 1", () => board.Page1, v => SelectBoardPage(board, 0, v), pagesOn, pagesOff);
            BoardPageChoice(flow, page, "PAGE 2", () => board.Page2, v => SelectBoardPage(board, 1, v),
                () => pagesOn() && board.PageCount >= 2, () => pagesOn() ? "Choose 2 or more full pages first." : pagesOff());
            BoardPageChoice(flow, page, "PAGE 3", () => board.Page3, v => SelectBoardPage(board, 2, v),
                () => pagesOn() && board.PageCount >= 3, () => pagesOn() ? "Choose 3 or 4 full pages first." : pagesOff());
            BoardPageChoice(flow, page, "PAGE 4", () => board.Page4, v => SelectBoardPage(board, 3, v),
                () => pagesOn() && board.PageCount >= 4, () => pagesOn() ? "Choose 4 full pages first." : pagesOff());

            flow.Section(AvIcon.ListDetails, "TAC BOARD PARTS", "MAJOR DISPLAY ELEMENTS");
            AvCellGrid blocks = flow.Grid(2);
            ToggleCell(flow, blocks, page, "STORES AND POWER", "The weapon, countermeasure and power readouts.",
                () => board.ShowStores, v => board.ShowStores = v, on, off);
            ToggleCell(flow, blocks, page, "TACTICAL PAGES", "Your manually selected full pages. Events never change the selection.",
                () => board.ScopeMode != BoardScopeMode.Off, v => board.ScopeMode = v ? BoardScopeMode.Manual : BoardScopeMode.Off, on, off);
            ToggleCell(flow, blocks, page, "LAMPS AND GAUGES", "The lamp line and the gauge row. SYSTEMS always shows. DECLUTTER still sets their detail.",
                () => board.ShowLamps || board.ShowGauges,
                v => { board.ShowLamps = v; board.ShowGauges = v; }, on, off);
            ToggleCell(flow, blocks, page, "NOTICES", "The notice line at the foot of the board.",
                () => board.ShowTicker, v => board.ShowTicker = v, on, off);
            ToggleCell(flow, blocks, page, "NAV DATA", "The cockpit view's bottom-right readout: home, target, fuel, clock. LEAN drops its caption, MIN hides it.",
                () => board.ShowNavData, v => board.ShowNavData = v, on, off);
            ToggleCell(flow, blocks, page, "FEED AND MAP", "Draw the kill feed and map frame in the board's look. Stays on with the board off.",
                () => board.Corners, v => board.Corners = v);

            flow.Section(AvIcon.Settings, "TAC BOARD EXTRAS", "SWAY · RESET");
            AvCellGrid extras = flow.Grid(2);
            ToggleCell(flow, extras, page, "VISOR SWAY", "The symbols lag a little behind head movement, as if hung just off the glass.",
                () => board.Sway, v => board.Sway = v, on, off);
            AvControl reset = flow.Buttons(new AvControl.Spec("RESET TO DEFAULTS", () =>
            {
                board.ResetToDefaults();
                Echo("TAC BOARD options reset to defaults.");
                Changed();
                flow.Ticker.TickNow(); // the controls read the new values now, not at the next 2 Hz tick
            }, AvButtonStyle.Default, AvIcon.Refresh)).Controls[0];
            reset.Help = "Put every TAC BOARD option back to its default. Nothing else changes.";
        }

        /// <summary>An offset as the option holds it: design units (a 1080p pixel; the screen's height over 1080 device pixels, never under 0.85 of one), not pixels.</summary>
        private static string Units(float v) => AvNum.Fixed(v, 0) + " units";

        private void BoardPageChoice(AvFlow flow, int page, string title, Func<BoardPage> get, Action<BoardPage> set,
            Func<bool> enabled, Func<string> reason) =>
            Stepper(flow, page, title, () => BoardPageNames[Mathf.Clamp((int)get(), 0, BoardPageNames.Length - 1)],
                d => set(StepPage(get(), d)),
                () => true, () => true, "Choose this page manually; the selection stays until you change it.", enabled, reason, true);

        /// <summary>The page <paramref name="d"/> steps on from <paramref name="from"/>, wrapping, past SYSTEMS (not a slot choice).</summary>
        private static BoardPage StepPage(BoardPage from, int d)
        {
            int n = BoardPageNames.Length, i = Mathf.Clamp((int)from, 0, n - 1);
            do i = (i + d + n) % n; while (i == (int)BoardPage.Systems);
            return (BoardPage)i;
        }

        private static void SelectBoardPage(ITacBoardSettings board, int slot, BoardPage choice)
        {
            var pages = new[] { board.Page1, board.Page2, board.Page3, board.Page4 };
            for (int i = 0; i < pages.Length; i++)
                if (i != slot && pages[i] == choice) pages[i] = pages[slot];
            pages[slot] = choice;
            board.Page1 = pages[0]; board.Page2 = pages[1]; board.Page3 = pages[2]; board.Page4 = pages[3];
        }

        /// <summary>A labelled choice strip (LAYOUT, DECLUTTER, PAGES): one segment per name, the latched one read from <paramref name="get"/>.</summary>
        private AvSegmented BoardStrip(AvFlow flow, int page, string label, string[] names, string[] briefs, Func<int> get, Action<int> set,
            Func<bool> enabled, Func<string> reason)
        {
            Func<int> current = () => Mathf.Clamp(get(), 0, names.Length - 1);
            var strip = flow.Add(new AvSegmented(flow.Content, label, names, current, i =>
            {
                if (!enabled()) return;
                set(i);
                Echo(label + " — " + names[i]);
                Changed();
            }));
            void Refresh()
            {
                bool avail = enabled();
                strip.Refresh();
                for (int i = 0; i < strip.Options.Length; i++)
                {
                    strip.Options[i].Interactable = avail;
                    strip.Options[i].Help = avail ? briefs[i] : reason();
                }
            }
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
            return strip;
        }

        /// <summary>A float option as a dial that steps by <paramref name="step"/> inside min..max (a typed value, not a config entry: the contract is the only way in).</summary>
        private SetRingCell BoardRing(AvFlow flow, SetRingRow row, int page, string title, Func<float> get, Action<float> set, float min, float max,
            float step, Func<float, string> text, string help, Func<bool> enabled, Func<string> reason) =>
            Ring(flow, row, page, title, () => text(get()), () => Mathf.Clamp01((get() - min) / (max - min)),
                d => set(Mathf.Clamp(Mathf.Round((get() + d * step) * 100f) / 100f, min, max)),
                () => get() > min + .001f, () => get() < max - .001f, help, enabled, reason);
    }
}
