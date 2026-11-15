using BoscaliSummer.Core.Services;
using NOAvionics;
using System;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>The THIS PILOT / SERVER switch and the two consoles it flips between.</summary>
    internal sealed partial class SettingsMfdPanel
    {
        private void BuildConsoles(RectTransform body, float height)
        {
            clientCon = BuildConsole(body, height, false);
            clientCon.PageChanged += _ => CancelMaintenance();
            serverCon = BuildConsole(body, height, true);
            ApplyMode(lastServerMode);
        }

        private AvConsole BuildConsole(RectTransform body, float height, bool server)
        {
            // SERVER is one page, so it has no tab bar.
            AvConsole c = AvConsole.Build(body, "SET", "SETTINGS", server ? 1 : 5, AvTokens.PanelWidth, height);

            // The kit has no slot for a control between the header and the tabs. A hidden metric reserves one
            // metric-height strip there and the mode switch (a module-local part) is drawn over exactly that strip.
            AvMetric[] band = c.Metrics("MODE");
            band[0].SetShown(false);
            var mode = new ModeSwitch(c.Root, SwitchMode);
            mode.Place(new AvSlot(AvGridTokens.Pad, AvGridTokens.Header + 4f,
                AvTokens.PanelWidth - 2f * AvGridTokens.Pad, AvGridTokens.Metric));
            c.Ticker.Register(mode);

            if (server) BuildServerPage(c.Page(SRules), SRules);
            else
            {
                AvTabBar tabs = c.Tabs(
                    (AvIcon.Typography, "PANELS"),
                    (AvIcon.Map2, "MAP"),
                    (AvIcon.Target, "HUD"),
                    (AvIcon.Eye, "VISUALS"),
                    (AvIcon.Gauge, "PERF"));
                TabHelp.Apply(tabs,
                    "How every MFD panel looks: colour scheme, screen finish, panel motion and sound, and which screens the bezel offers.",
                    "Map console layout, terrain, contacts, mouse handling, backdrop and the news ticker.",
                    "Keys, the TAC BOARD, the visor terrain ghost, flight aids, comms notices and the radial target presets.",
                    "What you feel and see: head motion, shakes, glare, pilot body, cockpit sound, sky, rain, sea, world effects and colour grade. Compat mod links close the page.",
                    "One-click quality presets, client work budgets and maintenance. Factory reset requires a game restart.");
                BuildPanelsPage(c.Page(CPanels), CPanels);
                BuildMapPage(c.Page(CMap), CMap);
                BuildHudPage(c.Page(CHud), CHud);
                BuildVisualsPage(c.Page(CVisuals), CVisuals);
                BuildPerformancePage(c.Page(CPerf), CPerf);
            }

            ApplyOrders(c, server);
            c.Ticker.Add(-1, AvTickRate.Slow, () => RefreshChrome(c, mode, server));
            c.Finish();
            return c;
        }

        private void SwitchMode(bool server)
        {
            if (serverMode == server || clientCon == null || serverCon == null) return;
            ApplyMode(server);
        }

        /// <summary>Show one console and hide the other (the hidden one stops ticking).</summary>
        private void ApplyMode(bool server)
        {
            CancelMaintenance();
            serverMode = lastServerMode = server;
            clientCon.Root.gameObject.SetActive(!server);
            serverCon.Root.gameObject.SetActive(server);
            con = server ? serverCon : clientCon;
            con.Ticker.TickNow();
        }

        /// <summary>
        /// The two big segments under the header (what you are editing and who can change it). Not a kit part (the kit
        /// has no console-level mode control), built from kit v2 controls. The footer says what each mode means.
        /// </summary>
        private sealed class ModeSwitch : AvPart
        {
            private const float ButtonH = 44f;   // fills the strip the console reserves for it
            private readonly AvControl pilot, server;
            private readonly TMP_Text serverGlyph;
            private bool lockedIcon;

            public ModeSwitch(RectTransform parent, Action<bool> pick)
            {
                Rect = AvLay.Child(parent, "Mode");
                AvLay.Nest(Rect, true);
                pilot = AvControl.Make(Rect, new AvControl.Spec("THIS PILOT", () => pick(false), AvButtonStyle.Default, AvIcon.User));
                server = AvControl.Make(Rect, new AvControl.Spec("SERVER", () => pick(true), AvButtonStyle.Default, AvIcon.Database));
                pilot.Help = "Settings for your own machine: panels, map, HUD, visuals and performance. " +
                             "Saved automatically and never shared.";
                server.Help = "Rules of this game. Host-authoritative: the host can change them, " +
                              "everyone else sees them locked.";
                serverGlyph = server.transform.Find("Icon " + AvIcon.Database)?.GetComponent<TMP_Text>();
            }

            public void Set(bool serverMode, bool host)
            {
                pilot.Latched = !serverMode;
                server.Latched = serverMode;
                // A remote client cannot change server rules: say so on the button before it is even opened.
                bool locked = !host;
                if (serverGlyph != null && locked != lockedIcon)
                {
                    lockedIcon = locked;
                    AvIcons.Set(serverGlyph, locked ? AvIcon.Lock : AvIcon.Database, AvGridTokens.IconInline);
                }
            }

            public override float Measure(float width) => AvGridTokens.Metric;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float half = (s.W - 4f) * 0.5f;
                AvLay.Place(pilot.Rect, 0f, 0f, half, ButtonH);
                AvLay.Place(server.Rect, half + 4f, 0f, half, ButtonH);
            }

            public override void Restyle()
            {
                pilot.Restyle();
                server.Restyle();
            }
        }
    }
}
