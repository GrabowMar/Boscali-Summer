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
            serverCon = BuildConsole(body, height, true);
            ApplyMode(lastServerMode);
        }

        private AvConsole BuildConsole(RectTransform body, float height, bool server)
        {
            string[] names = server ? ServerPageNames : ClientPageNames;
            AvConsole c = AvConsole.Build(body, "SET", names[0], names.Length, AvTokens.PanelWidth, height);
            c.PageChanged += index => c.SetTitle(names[index]);

            // The kit has no slot for a control between the header and the tabs. A hidden metric reserves one
            // metric-height strip there and the mode switch (a module-local part) is drawn over exactly that strip.
            // The old chip strip (SAVED / HOST) is gone: the switch's status line says the same thing.
            AvMetric[] band = c.Metrics("MODE");
            band[0].SetShown(false);
            var mode = new ModeSwitch(c.Root, SwitchMode);
            mode.Place(new AvSlot(AvGridTokens.Pad, AvGridTokens.Header + 4f,
                AvTokens.PanelWidth - 2f * AvGridTokens.Pad, AvGridTokens.Metric));
            c.Ticker.Register(mode);

            if (server)
            {
                ApplyTabHelp(c.Tabs(
                        (AvIcon.Flag, "WORLD"),
                        (AvIcon.Shield, "FORCES"),
                        (AvIcon.CloudRain, "EFFECTS"),
                        (AvIcon.ListDetails, "TASKING")),
                    "Contracts, world events, trenches and urban combat.",
                    "High command, support call-ins, squad and aces, progression and comms.",
                    "Fire, destruction and weather the host runs for everyone.",
                    "Faction objective board. The host issues tasking.");
                BuildServerGroupPage(c.Page(SWorld), SWorld, SWorld);
                BuildServerGroupPage(c.Page(SForces), SForces, SForces);
                BuildServerGroupPage(c.Page(SEffects), SEffects, SEffects);
                BuildTaskingPage(c.Page(STasking), STasking);
            }
            else
            {
                ApplyTabHelp(c.Tabs(
                        (AvIcon.Typography, "DISPLAY"),
                        (AvIcon.Map2, "MAP"),
                        (AvIcon.Eye, "COCKPIT"),
                        (AvIcon.WaveSine, "IMMERSION"),
                        (AvIcon.Gauge, "PERF")),
                    "Glass, surface texture, tint, motion and interface audio, with a live preview of the finish.",
                    "Map layout, terrain, backdrop imagery and the news ticker, with a layout schematic.",
                    "Target camera, radial presets and the common HUD element, with a sample of it.",
                    "Cockpit head inertia, camera vibrations, Mach buffeting, sun glare, G-vignette, and aerodynamic audio.",
                    "Client-local work budgets and panel effects, with a live frame-time trace. Each switch applies " +
                    "during this mission. Installing a disabled Weather module requires a game restart.");
                BuildDisplayPage(c.Page(CDisplay), CDisplay);
                BuildMapPage(c.Page(CMap), CMap);
                BuildCockpitPage(c.Page(CCockpit), CCockpit);
                BuildImmersionPage(c.Page(CImmersion), CImmersion);
                BuildPerformancePage(c.Page(CPerf), CPerf);
            }

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
            serverMode = lastServerMode = server;
            clientCon.Root.gameObject.SetActive(!server);
            serverCon.Root.gameObject.SetActive(server);
            con = server ? serverCon : clientCon;
            con.Ticker.TickNow();
        }

        /// <summary>
        /// The two big segments under the header (what you are editing and who can change it) with a status line
        /// under them: the scope on the left, the role on the right. Not a kit part (the kit has no console-level
        /// mode control), built from kit v2 controls.
        /// </summary>
        private sealed class ModeSwitch : AvPart
        {
            private const float ButtonH = 30f, LineH = 15f;
            private readonly AvControl pilot, server;
            private readonly TMP_Text scope, role;
            private readonly TMP_Text serverGlyph;
            private bool lockedIcon, hostShown = true;
            private string shown = "";

            public ModeSwitch(RectTransform parent, Action<bool> pick)
            {
                Rect = AvLay.Child(parent, "Mode");
                AvLay.Nest(Rect, true);
                pilot = AvControl.Make(Rect, new AvControl.Spec("THIS PILOT", () => pick(false), AvButtonStyle.Default, AvIcon.User));
                server = AvControl.Make(Rect, new AvControl.Spec("SERVER", () => pick(true), AvButtonStyle.Default, AvIcon.Database));
                pilot.Help = "Settings for your own machine: display, map, cockpit and performance. " +
                             "Saved automatically and never shared.";
                server.Help = "Rules of this game. Host-authoritative: the host can change them, " +
                              "everyone else sees them locked.";
                serverGlyph = server.transform.Find("Icon " + AvIcon.Database)?.GetComponent<TMP_Text>();
                scope = AvText.Make(Rect, "Scope", AvTextRole.Micro, "", TextAlignmentOptions.MidlineLeft);
                AvText.Fit(scope, false);
                role = AvText.Make(Rect, "Role", AvTextRole.Micro, "", TextAlignmentOptions.MidlineRight);
                AvText.Fit(role, false);
                Restyle();
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
                string text = !serverMode
                    ? "YOUR MACHINE ONLY · SAVED AUTOMATICALLY"
                    : host
                        ? "YOU ARE THE HOST · CHANGES APPLY TO EVERYONE"
                        : AvStates.Glyph(AvState.Caution) + "LOCKED · THE HOST DECIDES, YOU CAN READ THEM";
                string who = host ? "HOST" : "CLIENT";
                if (text == shown && host == hostShown) return;
                shown = text; hostShown = host;
                scope.text = text;
                role.text = who;
                Restyle();
            }

            public override float Measure(float width) => AvGridTokens.Metric;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float half = (s.W - 4f) * 0.5f;
                AvLay.Place(pilot.Rect, 0f, 0f, half, ButtonH);
                AvLay.Place(server.Rect, half + 4f, 0f, half, ButtonH);
                float y = ButtonH + 2f;
                AvLay.Place(scope.rectTransform, 2f, y, s.W * 0.78f, LineH);
                AvLay.Place(role.rectTransform, s.W * 0.78f + 2f, y, s.W * 0.22f - 4f, LineH);
            }

            public override void Restyle()
            {
                pilot.Restyle();
                server.Restyle();
                scope.color = AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
                role.color = hostShown
                    ? AvStyleHost.FuiColor("ready", AvTheme.RailReady)
                    : AvStyleHost.FuiInk("row-sub", AvTheme.Dim);
            }
        }
    }
}
