using NOAvionics;
using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The TAC BOARD's options on the HUD tab, written only through <see cref="ITacBoardSettings"/> (the Hud module owns the board and its config; this console never
    /// imports it). Three groups: the board itself, its major parts and the extras. SCOPE (ON, OFF) is also the C-menu's HUD row. Client-local: the board takes a change on its next 10 Hz tick, so a pilot sees it
    /// in flight and there is no preview here. Every control but RESET and the kill feed and map restyle (which stay when the board is off) waits on the board's own switch, as the immersion page waits on its master.
    /// </summary>
    internal sealed partial class SettingsMfdPanel
    {
        // Index = the enum's value, so the controls write a choice's index straight to the contract.
        // SCOPE is listed ON, OFF (the contract enum is Manual, Off, Auto; Manual is retired and reads as Auto): ScopeModes maps a segment back to the enum.
        private static readonly string[] BoardScopeNames = { "ON", "OFF" };
        private static readonly BoardScopeMode[] ScopeModes = { BoardScopeMode.Auto, BoardScopeMode.Off };
        private static readonly string[] BoardScopeBriefs =
        {
            "ON: RWR, FIRES and WING panels appear while they have something to show.",
            "OFF: No panels; stores and systems only.",
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
            BoardStrip(flow, page, "SCOPE", BoardScopeNames, BoardScopeBriefs,
                () => Array.IndexOf(ScopeModes, board.ScopeMode),
                i => board.ScopeMode = ScopeModes[i], on, off);

            var look = flow.Add(new SetRingRow(flow.Content));
            BoardRing(flow, look, page, "SIZE", () => board.Scale, v => board.Scale = v, 0.6f, 1.4f, .05f, v => AvNum.Percent(v),
                "How big the whole board is.", on, off);
            BoardRing(flow, look, page, "BACKGROUNDS", () => board.Shade, v => board.Shade = v, 0f, 1f, .05f, v => AvNum.Percent(v),
                "The one soft backdrop behind every block of the HUD: the board, the left column, the map, the wing strip and the centre alerts. 0% draws none.", () => true, off);
            var edge = flow.Add(new SetRingRow(flow.Content));
            BoardRing(flow, edge, page, "TEXT EDGE", () => board.TextEdge, v => board.TextEdge = v, 0f, 0.25f, .01f, v => v < .005f ? "OFF" : v.ToString("0.00"),
                "A hair-thin dark edge round every letter of the HUD so small words stay sharp on bright sky. OFF draws none.", () => true, off);

            var snd = flow.Add(new SetRingRow(flow.Content));
            BoardRing(flow, snd, page, "VISOR SOUND", () => board.VisorSound, v => board.VisorSound = v, 0f, 1f, .05f, v => v < .025f ? "OFF" : AvNum.Percent(v),
                "The helmet's own sounds: power-up, self-test, glitch on a hit, EMP stutter, warning blip, page wipe, brow ticks, radial menu and radio YES/NO. Quiet under a real alarm or voice. OFF is silent.", () => true, off);

            flow.Section(AvIcon.ListDetails, "TAC BOARD PARTS", null);
            ToggleCell(flow, page, "STORES AND POWER", "The weapon, countermeasure and power readouts.",
                () => board.ShowStores, v => board.ShowStores = v, on, off);

            ToggleCell(flow, page, "LAMPS AND GAUGES", "The lamp line and the gauge row.",
                () => board.ShowLamps || board.ShowGauges,
                v => { board.ShowLamps = v; board.ShowGauges = v; }, on, off);
            ToggleCell(flow, page, "NOTICES", "The NOTICE line in the left column. It needs MAP AND FEED on.",
                () => board.ShowTicker, v => board.ShowTicker = v, on, off);
            ToggleCell(flow, page, "SYSTEMS BLOCK", "The fire, engine and fuel strip under the lamps. Its warnings still post when hidden.",
                () => board.ShowSystems, v => board.ShowSystems = v, on, off);
            ToggleCell(flow, page, "FEED AND MAP", "Draw the kill feed and map frame in the board's look. Stays on with the board off.",
                () => board.Corners, v => board.Corners = v);
            ToggleCell(flow, page, "CENTRE ALERTS", "MISSILE / PULL UP / LOCKED banner at the top centre, missile arrows round the reticle, HIT and DESTROYED calls. Stays on with the board off.",
                () => board.CentreAlerts, v => board.CentreAlerts = v);
            ToggleCell(flow, page, "VISOR FRAME", "The curved brow rail at the top centre with bearing marks, and the visor's rim: corner lines, side ticks and the etched line of identity along the bottom edge. Cockpit view. Stays on with the board off.",
                () => board.Brow, v => board.Brow = v);
            ToggleCell(flow, page, "VISOR POWER-ON", "The visor boots when you take the seat: a self-test of what the aircraft really reports, then the zones come alive one by one. A respawn only re-syncs. A real warning cuts it short. Cockpit view.",
                () => board.Boot, v => board.Boot = v);
            ToggleCell(flow, page, "VISOR REACTIONS", "The visor reacts like a helmet display: symbols shake and dim under heavy G, glitch when you are hit, stutter out under an EMP, flash once for a new warning, breathe faintly when calm. Never with reduced motion. Cockpit view.",
                () => board.Reactive, v => board.Reactive = v);
            ToggleCell(flow, page, "FUEL AND THROTTLE ARCS", "Two segmented arcs hugging the reticle in place of the game's plain fuel and throttle arcs. Cockpit view. Stays on with the board off.",
                () => board.Arcs, v => board.Arcs = v);

            flow.Section(AvIcon.Target, "AR HELMET LAYER", "DRAWN ON THE WORLD · ONLY WHILE IT MATTERS");
            ToggleCell(flow, page, "MISSILE LINES", "Your missiles joined to their targets with the seconds to impact; every missile coming at you boxed with its next two seconds.",
                () => board.ArMissiles, v => board.ArMissiles = v);
            ToggleCell(flow, page, "BEACONS", "A pillar over the nearest friendly field at JOKER, BINGO or with damage, and over a live contract zone.",
                () => board.ArBeacons, v => board.ArBeacons = v);
            ToggleCell(flow, page, "HUD REACTIONS", "Brackets spin in on a new target; the HUD glitches for a moment when a hit damages the aircraft. REDUCED MOTION keeps it still.",
                () => board.HudFx, v => board.HudFx = v);

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

        /// <summary>A labelled choice strip (SCOPE, DECLUTTER): one segment per name, the latched one read from <paramref name="get"/>.</summary>
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
