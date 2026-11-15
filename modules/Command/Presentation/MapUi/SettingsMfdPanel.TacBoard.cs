using NOAvionics;
using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The TAC BOARD's options on the HUD tab, written only through <see cref="ITacBoardSettings"/> (the Hud module owns the board and its config; this console never
    /// imports it). Three groups: the board itself, its major parts and the extras. Its manual page slots are the C-menu's (PAGES rows in MenuCatalog), not this console's. Client-local: the board takes a change on its next 10 Hz tick, so a pilot sees it
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

            flow.Section(AvIcon.Target, "TAC BOARD", null);
            ToggleCell(flow, page, "TAC BOARD",
                "Off hides the board and gives back the game's own weapon, countermeasure and power panels.",
                () => board.Enabled, v => board.Enabled = v);
            BoardStrip(flow, page, "LAYOUT", BoardLayoutNames, BoardLayoutBriefs,
                () => (int)board.Layout, i => board.Layout = (BoardLayout)i, on, off);
            BoardStrip(flow, page, "DECLUTTER", BoardDeclutterNames, BoardDeclutterBriefs,
                () => (int)board.Declutter, i => board.Declutter = (BoardDeclutter)i, on, off);

            var look = flow.Add(new SetRingRow(flow.Content));
            BoardRing(flow, look, page, "SIZE", () => board.Scale, v => board.Scale = v, 0.6f, 1.4f, .05f, v => AvNum.Percent(v),
                "How big the whole board is.", on, off);
            BoardRing(flow, look, page, "BACKGROUNDS", () => board.Shade, v => board.Shade = v, 0f, 1f, .05f, v => AvNum.Percent(v),
                "The one soft backdrop behind every block of the HUD: the board, the left column, the map, the wing strip and the centre alerts. 0% draws none.", () => true, off);

            flow.Section(AvIcon.ListDetails, "TAC BOARD PARTS", null);
            ToggleCell(flow, page, "STORES AND POWER", "The weapon, countermeasure and power readouts.",
                () => board.ShowStores, v => board.ShowStores = v, on, off);
            ToggleCell(flow, page, "TACTICAL PAGES", "Your manually selected full pages. Events never change the selection.",
                () => board.ScopeMode != BoardScopeMode.Off, v => board.ScopeMode = v ? BoardScopeMode.Manual : BoardScopeMode.Off, on, off);
            ToggleCell(flow, page, "LAMPS AND GAUGES", "The lamp line and the gauge row. DECLUTTER still sets their detail.",
                () => board.ShowLamps || board.ShowGauges,
                v => { board.ShowLamps = v; board.ShowGauges = v; }, on, off);
            ToggleCell(flow, page, "NOTICES", "The notice line at the foot of the board.",
                () => board.ShowTicker, v => board.ShowTicker = v, on, off);
            ToggleCell(flow, page, "SYSTEMS BLOCK", "The fire, engine and fuel strip under the lamps. Its warnings still post when hidden.",
                () => board.ShowSystems, v => board.ShowSystems = v, on, off);
            ToggleCell(flow, page, "FEED AND MAP", "Draw the kill feed and map frame in the board's look. Stays on with the board off.",
                () => board.Corners, v => board.Corners = v);
            ToggleCell(flow, page, "CENTRE ALERTS", "MISSILE / PULL UP / LOCKED banner at the top centre, missile arrows round the reticle, HIT and DESTROYED calls. Stays on with the board off.",
                () => board.CentreAlerts, v => board.CentreAlerts = v);

            flow.Section(AvIcon.Settings, "TAC BOARD EXTRAS", null);
            ToggleCell(flow, page, "VISOR SWAY", "The symbols lag a little behind head movement, as if hung just off the glass.",
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

        /// <summary>A labelled choice strip (LAYOUT, DECLUTTER): one segment per name, the latched one read from <paramref name="get"/>.</summary>
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
