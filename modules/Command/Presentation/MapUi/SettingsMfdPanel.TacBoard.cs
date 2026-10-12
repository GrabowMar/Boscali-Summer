using NOAvionics;
using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The TAC BOARD's options on the COCKPIT tab, written only through <see cref="ITacBoardSettings"/> (the Hud module owns the board and its config; this console never
    /// imports it). Four groups: the board itself, its pages, its blocks and the extras. Client-local: the board takes a change on its next 10 Hz tick, so a pilot sees it
    /// in flight and there is no preview here. Every control but RESET and the kill feed and map restyle (which stay when the board is off) waits on the board's own switch, as the immersion page waits on its master.
    /// </summary>
    internal sealed partial class SettingsMfdPanel
    {
        // Index = the enum's value (BoardLayout, BoardScopeMode, BoardDeclutter), so a strip writes a segment's index straight to the contract.
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
            "LEAN: no gauge row, dark lamps or captions; the cruise strip only while something is wrong.",
            "MIN: only lock, spike and missile lamps and the pages of a fight; the strip only in a fire, engine out or bingo.",
        };
        private static readonly string[] BoardPagesNames = { "AUTO", "OFF" };
        private static readonly string[] BoardPagesBriefs =
        {
            "AUTO: a page appears in the board's page area when it is relevant.",
            "OFF: the board's page area shows no pages. Lamps, gauges and notices stay.",
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

            flow.Section(AvIcon.Radar2, "TAC BOARD PAGES", "WHAT THE PAGE AREA SHOWS");
            BoardStrip(flow, page, "PAGES", BoardPagesNames, BoardPagesBriefs,
                () => (int)board.ScopeMode, i => board.ScopeMode = (BoardScopeMode)i, on, off);
            AvCellGrid pages = flow.Grid(3);
            ToggleCell(flow, pages, page, "RADAR WARNING", "The page for contacts locking, painting or launching at you.",
                () => board.PageThreat, v => board.PageThreat = v, on, off);
            ToggleCell(flow, pages, page, "SHOT", "The page that follows your own missiles and bombs from launch to result.",
                () => board.PageShot, v => board.PageShot = v, on, off);
            ToggleCell(flow, pages, page, "MISSILE CAM", "The page shown while the camera rides one of your missiles.",
                () => board.PageMissileCam, v => board.PageMissileCam = v, on, off);
            ToggleCell(flow, pages, page, "APPROACH", "The runway funnel for an ILS or autoland approach.",
                () => board.PageApproach, v => board.PageApproach = v, on, off);
            ToggleCell(flow, pages, page, "WING", "Your wingmen's formation, fuel, ammunition and orders.",
                () => board.PageWing, v => board.PageWing = v, on, off);
            ToggleCell(flow, pages, page, "STRIKE", "Your own strikes in the air, recon windows and cyber intrusions.",
                () => board.PageStrike, v => board.PageStrike = v, on, off);
            ToggleCell(flow, pages, page, "DOCK", "The page shown while you hook up to a SKYWELL tanker.",
                () => board.PageDock, v => board.PageDock = v, on, off);
            ToggleCell(flow, pages, page, "OBJECTIVE", "Your selected contract: its zone, deadline and air defences.",
                () => board.PageObjective, v => board.PageObjective = v, on, off);
            ToggleCell(flow, pages, page, "SYSTEMS", "The cruise strip, and the home of fire, engine, fuel and structure warnings.",
                () => board.PageSystems, v => board.PageSystems = v, on, off);

            var times = flow.Add(new SetRingRow(flow.Content));
            BoardRing(flow, times, page, "RWR HOLD", () => board.ThreatHoldSeconds, v => board.ThreatHoldSeconds = v, 2f, 30f, 1f, v => AvNum.Seconds(v, 0),
                "Seconds the radar-warning page keeps a contact after its last warning.", on, off);
            BoardRing(flow, times, page, "PAGE MIN STAY", () => board.PageMinSeconds, v => board.PageMinSeconds = v, 0f, 15f, 1f, v => AvNum.Seconds(v, 0),
                "Seconds a page holds the board's page area once shown. Does not apply to SYSTEMS.", on, off);

            flow.Section(AvIcon.ListDetails, "TAC BOARD BLOCKS", "WHAT THE BOARD CARRIES");
            AvCellGrid blocks = flow.Grid(2);
            ToggleCell(flow, blocks, page, "LAMPS", "The lamp row: switch states and the threat lamps.",
                () => board.ShowLamps, v => board.ShowLamps = v, on, off);
            ToggleCell(flow, blocks, page, "SYSTEMS STRIP", "The compact strip the page area shows in cruise. Needs the SYSTEMS page and PAGES = AUTO.",
                () => board.ShowSystemsStrip, v => board.ShowSystemsStrip = v, on, off);
            ToggleCell(flow, blocks, page, "GAUGES", "The gauge row under the board's page area. LEAN and MIN hide it anyway.",
                () => board.ShowGauges, v => board.ShowGauges = v, on, off);
            ToggleCell(flow, blocks, page, "NOTICES", "The notice line at the foot of the board.",
                () => board.ShowTicker, v => board.ShowTicker = v, on, off);

            flow.Section(AvIcon.Settings, "TAC BOARD EXTRAS", "SWAY · RESTYLE · RESET");
            AvCellGrid extras = flow.Grid(2);
            ToggleCell(flow, extras, page, "VISOR SWAY", "The symbols lag a little behind head movement, as if hung just off the glass.",
                () => board.Sway, v => board.Sway = v, on, off);
            ToggleCell(flow, extras, page, "RESTYLE FEED AND MAP", "Draw the kill feed and the map frame in the board's look. Stays on with the board off.",
                () => board.Corners, v => board.Corners = v);
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
