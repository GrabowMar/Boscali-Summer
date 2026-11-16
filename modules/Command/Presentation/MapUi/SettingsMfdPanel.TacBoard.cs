using NOAvionics;
using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The TAC BOARD's options on the HUD tab, written only through <see cref="ITacBoardSettings"/> (the Hud module owns the board and its config; this console never
    /// imports it). Three groups: the board itself, its major parts and the extras. SCOPE (AUTO, MANUAL, OFF) is also the C-menu's HUD row; the two MANUAL page rows live only here. Client-local: the board takes a change on its next 10 Hz tick, so a pilot sees it
    /// in flight and there is no preview here. Every control but RESET and the kill feed and map restyle (which stay when the board is off) waits on the board's own switch, as the immersion page waits on its master.
    /// </summary>
    internal sealed partial class SettingsMfdPanel
    {
        // Index = the enum's value, so the controls write a choice's index straight to the contract.
        // SCOPE is listed AUTO, MANUAL, OFF (the contract enum is Manual, Off, Auto): ScopeModes maps a segment back to the enum.
        private static readonly string[] BoardScopeNames = { "AUTO", "MANUAL", "OFF" };
        private static readonly BoardScopeMode[] ScopeModes = { BoardScopeMode.Auto, BoardScopeMode.Manual, BoardScopeMode.Off };
        private static readonly string[] BoardScopeBriefs =
        {
            "AUTO: The board picks the page for what is happening.",
            "MANUAL: Shows the two pages you picked.",
            "OFF: No pages; stores and systems only.",
        };
        // The pages that can still live on the board; MISSILE CAM, SYSTEMS, DOCK, CARGO and OBJECTIVE have their own homes now.
        private static readonly BoardPage[] BoardPageChoices = { BoardPage.Threat, BoardPage.Shot, BoardPage.Approach, BoardPage.Wing, BoardPage.Strike };
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
            BoardStrip(flow, page, "SCOPE", BoardScopeNames, BoardScopeBriefs,
                () => Array.IndexOf(ScopeModes, board.ScopeMode),
                i => { board.ScopeMode = ScopeModes[i]; if (ScopeModes[i] == BoardScopeMode.Manual) board.PageCount = 2; }, on, off);
            Func<bool> manual = () => board.Enabled && board.ScopeMode == BoardScopeMode.Manual;
            Func<string> notManual = () => board.Enabled ? "Set SCOPE to MANUAL to choose pages." : "Turn on TAC BOARD first.";
            // The steppers show what the board will show (a stored page that is no longer choosable, or twice, reads as the board reads it).
            Func<BoardPage> first = () => ShownPage(board.Page1, BoardPage.Systems, BoardPage.Threat), second = () => ShownPage(board.Page2, first(), Array.IndexOf(BoardPageChoices, board.Page2) >= 0 || first() == BoardPage.Wing ? BoardPage.Shot : BoardPage.Wing);
            BoardPageRow(flow, page, "PAGE 1", first, p => board.Page1 = p, second, manual, notManual);
            BoardPageRow(flow, page, "PAGE 2", second, p => board.Page2 = p, first, manual, notManual);
            BoardStrip(flow, page, "DECLUTTER", BoardDeclutterNames, BoardDeclutterBriefs,
                () => (int)board.Declutter, i => board.Declutter = (BoardDeclutter)i, on, off);

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

            ToggleCell(flow, page, "LAMPS AND GAUGES", "The lamp line and the gauge row. DECLUTTER still sets their detail.",
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

        /// <summary>
        /// A MANUAL page slot: a stepper over <see cref="BoardPageChoices"/> that skips the other slot's page (the board keeps two distinct pages). A stored page outside the list
        /// (an old config's DOCK, say) shows by name until the first step.
        /// </summary>
        /// <summary>The page a slot shows: <paramref name="stored"/> when it is choosable and not the <paramref name="earlier"/> slot's, else <paramref name="fallback"/>, else the first choice that is free (BoardOptions.Sanitize's rule).</summary>
        private static BoardPage ShownPage(BoardPage stored, BoardPage earlier, BoardPage fallback)
        {
            if (Array.IndexOf(BoardPageChoices, stored) >= 0 && stored != earlier) return stored;
            if (fallback != earlier) return fallback;
            foreach (BoardPage choice in BoardPageChoices) if (choice != earlier) return choice;
            return fallback;
        }

        private static string PageWord(BoardPage page)
        {
            switch (page)
            {
                case BoardPage.Threat: return "THREAT";
                case BoardPage.Shot: return "SHOT";
                case BoardPage.Approach: return "APPROACH";
                case BoardPage.Wing: return "WING";
                case BoardPage.Strike: return "STRIKE";
                default: return "--";
            }
        }

        private AvRow BoardPageRow(AvFlow flow, int page, string title, Func<BoardPage> get, Action<BoardPage> set, Func<BoardPage> other,
            Func<bool> enabled, Func<string> reason)
        {
            void Step(int d)
            {
                int at = Array.IndexOf(BoardPageChoices, get()), n = BoardPageChoices.Length;
                if (at < 0) at = d > 0 ? -1 : n;
                for (int k = 0; k < n; k++)
                {
                    at = (at + d + n) % n;
                    if (BoardPageChoices[at] != other()) break;
                }
                set(BoardPageChoices[at]);
            }
            return Stepper(flow, page, title, () => PageWord(get()),
                Step, () => true, () => true, "The page this slot shows in MANUAL: THREAT, SHOT, APPROACH, WING or STRIKE.", enabled, reason);
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
