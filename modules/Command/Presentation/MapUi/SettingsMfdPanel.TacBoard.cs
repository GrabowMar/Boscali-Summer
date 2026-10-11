using NOAvionics;
using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The TAC BOARD's options on the COCKPIT tab, written only through <see cref="ITacBoardSettings"/> (the Hud module owns the board and its config; this console never
    /// imports it). Three groups (the board itself, its pages, its blocks) and a reset. Client-local: the board takes a change on its next 10 Hz tick, so a pilot sees it
    /// in flight and there is no preview here. Every control but RESET waits on the board's own switch, as the immersion page waits on its master. Only an option the board
    /// draws has a row: Layout, Scale, OffsetX, OffsetY, Shade, Sway and Corners are in the contract and get one when the board honours them.
    /// </summary>
    internal sealed partial class SettingsMfdPanel
    {
        // Index = the enum's value (BoardScopeMode, BoardDeclutter), so a strip writes a segment's index straight to the contract.
        private static readonly string[] BoardDeclutterNames = { "FULL", "LEAN", "MIN" };
        private static readonly string[] BoardDeclutterBriefs =
        {
            "FULL: everything the switches below allow.",
            "LEAN: no gauge row, no dark lamps.",
            "MIN: only the lock, spike and missile lamps, and the pages that matter in a fight.",
        };
        private static readonly string[] BoardPagesNames = { "AUTO", "OFF" };
        private static readonly string[] BoardPagesBriefs =
        {
            "AUTO: a page appears on the scope when it is relevant.",
            "OFF: the scope shows no pages. Lamps, gauges and notices stay.",
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

            flow.Section(AvIcon.Target, "TAC BOARD", "BOARD · DECLUTTER · LOOK");
            ToggleCell(flow, flow.Grid(1), page, "TAC BOARD",
                "Off hides the board and gives back the game's own weapon, countermeasure and power panels.",
                () => board.Enabled, v => board.Enabled = v);
            BoardStrip(flow, page, "DECLUTTER", BoardDeclutterNames, BoardDeclutterBriefs,
                () => (int)board.Declutter, i => board.Declutter = (BoardDeclutter)i, on, off);

            var look = flow.Add(new SetRingRow(flow.Content));
            BoardRing(flow, look, page, "OPACITY", () => board.Opacity, v => board.Opacity = v, 0.25f, 1f, .05f, v => AvNum.Percent(v),
                "How solid the board's ink reads against the sky.", on, off);

            flow.Section(AvIcon.Radar2, "TAC BOARD PAGES", "WHAT THE SCOPE SHOWS");
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
                "Seconds a page stays on the scope once it has appeared.", on, off);

            flow.Section(AvIcon.ListDetails, "TAC BOARD BLOCKS", "WHAT THE BOARD CARRIES");
            AvCellGrid blocks = flow.Grid(2);
            ToggleCell(flow, blocks, page, "LAMPS", "The lamp row: switch states and the threat lamps.",
                () => board.ShowLamps, v => board.ShowLamps = v, on, off);
            ToggleCell(flow, blocks, page, "SYSTEMS STRIP", "The compact systems strip the scope falls back to in cruise.",
                () => board.ShowSystemsStrip, v => board.ShowSystemsStrip = v, on, off);
            ToggleCell(flow, blocks, page, "GAUGES", "The gauge row under the scope. LEAN and MIN hide it anyway.",
                () => board.ShowGauges, v => board.ShowGauges = v, on, off);
            ToggleCell(flow, blocks, page, "NOTICES", "The notice line at the foot of the board.",
                () => board.ShowTicker, v => board.ShowTicker = v, on, off);

            flow.Section(AvIcon.Settings, "TAC BOARD RESET", "BACK TO DEFAULTS");
            AvControl reset = flow.Buttons(new AvControl.Spec("RESET TO DEFAULTS", () =>
            {
                board.ResetToDefaults();
                Echo("TAC BOARD options reset to defaults.");
                Changed();
            }, AvButtonStyle.Default, AvIcon.Refresh)).Controls[0];
            reset.Help = "Put every TAC BOARD option back to its default. Nothing else changes.";
        }

        /// <summary>A labelled choice strip (DECLUTTER, PAGES): one segment per name, the latched one read from <paramref name="get"/>.</summary>
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
