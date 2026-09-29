using System;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
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

            AvChip[] chips = c.Chips(2);

            // The kit has no slot for a control between the chips and the tabs. A hidden metric reserves one
            // metric-height strip there and the mode switch (a module-local part) is drawn over exactly that strip.
            AvMetric[] band = c.Metrics("MODE");
            band[0].SetShown(false);
            var mode = new ModeSwitch(c.Root, SwitchMode);
            mode.Place(new AvSlot(AvGridTokens.Pad, AvGridTokens.Header + 4f + AvGridTokens.ChipStrip + 6f,
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
                        (AvIcon.Gauge, "PERF")),
                    "Glass, CRT, tint, theme, motion and interface audio.",
                    "Map layout, terrain, backdrop imagery and the news ticker.",
                    "Target camera, radial presets and the common HUD element.",
                    "Client-local work budgets. Each switch applies during this mission. " +
                    "Installing a disabled Weather module requires a game restart.");
                BuildDisplayPage(c.Page(CDisplay), CDisplay);
                BuildMapPage(c.Page(CMap), CMap);
                BuildCockpitPage(c.Page(CCockpit), CCockpit);
                BuildPerformancePage(c.Page(CPerf), CPerf);
            }

            AvChip saved = chips[0], role = chips[1];
            c.Ticker.Add(-1, AvTickRate.Slow, () => RefreshChrome(c, saved, role, mode, server));
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
        /// The two big segments under the header: what you are editing and who can change it. Not a kit part
        /// (the kit has no console-level mode control), built from kit v2 controls.
        /// </summary>
        private sealed class ModeSwitch : AvPart
        {
            private const float ButtonH = 36f;
            private readonly AvControl pilot, server;
            private readonly TMP_Text note;
            private readonly TMP_Text serverGlyph;
            private bool lockedIcon;
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
                note = AvText.Make(Rect, "Note", AvTextRole.ProseSmall, "", TextAlignmentOptions.TopLeft, true);
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
                    ? "Your machine only. Nobody else sees these. Saved automatically."
                    : host
                        ? "You are the host. Changes apply to everyone on this server."
                        : "Locked. The host decides these; you can read the values.";
                if (text == shown) return;
                shown = text;
                note.text = text;
            }

            public override float Measure(float width) => AvGridTokens.Metric;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                float half = (s.W - 4f) * 0.5f;
                AvLay.Place(pilot.Rect, 0f, 0f, half, ButtonH);
                AvLay.Place(server.Rect, half + 4f, 0f, half, ButtonH);
                AvLay.Place(note.rectTransform, 2f, ButtonH + 5f, s.W - 4f, s.H - ButtonH - 5f);
            }

            public override void Restyle()
            {
                pilot.Restyle();
                server.Restyle();
                note.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
            }
        }
    }
}
