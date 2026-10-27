using NOAvionics;
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BepInEx.Logging;
using BoscaliSummer.Core.Config;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// MISC, the last key on the rail: the drawer for everything that is not a cockpit system. A module may add tabs first
    /// (<see cref="IMiscTabs"/>: ARES's SPAWN, UNITS, POWERS and CHEATS); DEBUG drives the [Debug] config entries. Both stay locked
    /// behind the DEV ACCESS page until the player accepts the cheating warning (<see cref="DevAccess"/>, this session only).
    /// Spooktober adds one tab (<see cref="ISetExtras"/>), always shown.
    /// </summary>
    internal sealed class MiscMfdPanel : MonoBehaviour, ISceneService
    {
        private const int Standby = 0, Gate = 1;

        private readonly MfdPanelInstaller installer =
            new MfdPanelInstaller(MfdSlots.Misc, "MISC", "BoscaliMisc.Screen", preferLeft: false) { Host = true };
        private ModConfiguration settings;
#if !PUBLIC_RELEASE
        private AvConsole console; // the dev automation hook reads it
#endif
        private AvTabBar bar;
        private List<(AvIcon icon, string label)> tabs;

        /// <summary>The live panel, for the dev automation hook.</summary>
        internal static MiscMfdPanel Active { get; private set; }

        private void OnEnable() => Active = this;

        public void Configure(ModConfiguration config, ManualLogSource log)
        {
            settings = config;
            installer.Log = log;
            installer.Builder = BuildScreen;
        }

        public void ResetForScene()
        {
            installer.Reset();
#if !PUBLIC_RELEASE
            console = null;
#endif
            bar = null;
            tabs = null;
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (settings != null) installer.Tick();
        }

        private RectTransform BuildScreen(RectTransform host, float height)
        {
            ModuleServices.TryGet(out ISetExtras extras);
            ModuleServices.TryGet(out IMiscTabs added);
            var titles = new List<string> { "MISC · STANDBY", "MISC · DEV ACCESS" };
            tabs = new List<(AvIcon icon, string label)> { (AvIcon.Stack2, "STANDBY"), (AvIcon.AlertTriangle, "DEV ACCESS") };
            int firstAdded = titles.Count;
            for (int i = 0; added != null && i < added.Count; i++)
            {
                titles.Add(added.Title(i));
                tabs.Add((added.Icon(i), added.Label(i)));
            }
            int debug = titles.Count;
            titles.Add("MISC · DEBUG");
            tabs.Add((AvIcon.Terminal, "DEBUG"));
            int extra = -1;
            if (extras != null)
            {
                extra = titles.Count;
                titles.Add(extras.ExtraTabTitle);
                tabs.Add((AvIcon.Skull, extras.ExtraTabLabel));
            }

            AvConsole c = AvConsole.Build(host, MfdSlots.Misc, titles[0], titles.Count, AvTokens.PanelWidth, height);
            c.PageChanged += index => c.SetTitle(titles[index]);
#if !PUBLIC_RELEASE
            console = c;
#endif
            bar = c.Tabs(tabs.ToArray());
            bar.SetShown(Standby, false);

            BuildStandby(c, c.Page(Standby));
            BuildGate(c, c.Page(Gate));
            if (added != null)
            {
                var hints = new string[tabs.Count];
                for (int i = 0; i < added.Count; i++)
                {
                    added.Build(i, firstAdded + i, c.Page(firstAdded + i), c);
                    hints[firstAdded + i] = added.Help(i);
                }
                AvControl[] tabControls = bar.Rect.GetComponentsInChildren<AvControl>(true);
                for (int i = 0; i < tabControls.Length && i < hints.Length; i++)
                    if (hints[i] != null) tabControls[i].Help = hints[i];
            }
            BuildDebug(c, c.Page(debug), debug);
            if (extras != null) extras.BuildExtraPage(extra, c.Page(extra), c);

            // STANDBY is only the fallback: the console sits on the first tab it shows, and leaves a tab that hides while open.
            // DEV ACCESS shows until the warning is accepted; ARES and DEBUG show only after it.
            Action sync = () =>
            {
                bool granted = DevAccess.Granted;
                bool Shown(int tab) => tab == Gate ? !granted : tab < firstAdded || tab > debug || granted;
                bar.SetShown(Gate, !granted);
                for (int i = firstAdded; i <= debug; i++) bar.SetShown(i, granted);
                int first = Standby;
                for (int i = 1; i < titles.Count && first == Standby; i++)
                    if (Shown(i)) first = i;
                int page = c.CurrentPage;
                if (page < 0 || page == Standby || !Shown(page)) { if (page != first) c.SetPage(first); }
                if (c.CurrentPage == Gate) c.Footer.Set("Developer tools are locked until you accept.", AvState.Caution);
                else c.Footer.Set(c.CurrentPage == Standby ? "Nothing assigned." : "Applies now. Saved with the game's config.", AvState.Inert);
            };
            c.Ticker.Add(-1, AvTickRate.Slow, sync);
            c.Finish();
            sync();
            return null;
        }

#if !PUBLIC_RELEASE
        internal AvConsole ConsoleForAutomation => console;

        /// <summary>The tab labels the bar shows now, in order.</summary>
        internal List<string> ShownTabsForAutomation()
        {
            var shown = new List<string>();
            if (bar == null) return shown;
            AvControl[] controls = bar.Rect.GetComponentsInChildren<AvControl>(true);
            for (int i = 0; i < controls.Length && i < tabs.Count; i++)
                if (controls[i].gameObject.activeSelf) shown.Add(tabs[i].label);
            return shown;
        }

        internal int TabIndexForAutomation(string label) => tabs == null ? -1 : tabs.FindIndex(t => t.label == label);
#endif

        private static void BuildStandby(AvConsole c, AvFlow flow)
        {
            flow.Section(AvIcon.Stack2, "MISC", "STANDBY");
            flow.Add(new AvNote(flow.Content, "Nothing is assigned to this console."));
            c.Orders(Standby,
                "Nothing is assigned to this console.",
                "Some pages here are earned, not issued.");
        }

        /// <summary>
        /// The cheating warning. ARES (spawn, units, powers, cheats) and DEBUG stay hidden until ACCEPT; DENY keeps them locked.
        /// Accepting lasts until the game closes and is never written to the config.
        /// </summary>
        private void BuildGate(AvConsole c, AvFlow flow)
        {
            flow.Section(AvIcon.AlertTriangle, "DEVELOPER TOOLS", "TESTING ONLY");
            AvNote note = flow.Add(new AvNote(flow.Content));
            AvNote detail = flow.Add(new AvNote(flow.Content, AvTextRole.ProseSmall));
            const string warning = "WARNING: THIS IS CHEATING. ARES and DEBUG change the world, the rules and your config. " +
                "Using them outside testing breaks the game for everyone who shares your session.";
            note.Set(warning, AvState.Danger);
            detail.Set("ARES: spawn units, take control, strikes, weather, god mode, infinite fuel and ammo. DEBUG: logging and developer tools. " +
                "Host only. Accepting lasts until the game closes; nothing is saved.", AvState.Info);
            AvButtons answer = flow.Buttons(
                new AvControl.Spec("ACCEPT", () =>
                {
                    DevAccess.Accept();
                    installer.Log?.LogWarning("MISC: developer tools unlocked for this session (the player accepted the cheating warning).");
                }, AvButtonStyle.Danger),
                new AvControl.Spec("DENY", () =>
                {
                    note.Set("ACCESS DENIED. ARES and DEBUG stay locked. Press ACCEPT only if you are testing.", AvState.Caution);
                }, AvButtonStyle.Quiet));
            answer.Controls[0].Help = "I understand this is cheating outside testing. Unlock ARES and DEBUG until the game closes.";
            answer.Controls[1].Help = "Keep the developer tools locked.";
            c.Orders(Gate, warning, "Pressing ACCEPT unlocks them until the game closes.");
        }

        private void BuildDebug(AvConsole c, AvFlow flow, int page)
        {
            flow.Section(AvIcon.Terminal, "LOGGING", "THIS MACHINE");
            AvCellGrid logs = flow.Grid(2);
            Toggle(flow, logs, page, "VERBOSE LOG", settings.Diagnostics.VerboseLogging);
            Toggle(flow, logs, page, "WING LOG", settings.Wing.VerboseLogging);

            flow.Section(AvIcon.Activity, "TOOLS", "DEVELOPER");
            AvCellGrid tools = flow.Grid(2);
            Toggle(flow, tools, page, "FOOTPRINT", settings.Diagnostics.FootprintProfiler);
            Toggle(flow, tools, page, "DEV TOOLS", settings.Wing.DevTools);
            ConfigEntry<bool> devTools = settings.Wing.DevTools;
            Toggle(flow, tools, page, "WING OVERLAY", settings.Wing.Overlay, () => devTools.Value);

            c.Orders(page,
                "Logs go to the BepInEx log on this machine only.",
                "FOOTPRINT costs a little while on; read it with nomod bridge footprint.");
        }

        /// <summary>One [Debug] switch as a kit cell; the entry's own description is the hover help.</summary>
        private static void Toggle(AvFlow flow, AvCellGrid grid, int page, string title, ConfigEntry<bool> entry, Func<bool> enabled = null)
        {
            AvCell cell = grid.Toggle(title, "", () => entry.Value, v => { if (enabled == null || enabled()) entry.Value = v; });
            string help = entry.Description.Description;
            void Refresh()
            {
                bool avail = enabled == null || enabled();
                cell.Interactable = avail;
                cell.Help = avail ? help : "Turn on DEV TOOLS first.";
                cell.Refresh();
            }
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
        }
    }
}
