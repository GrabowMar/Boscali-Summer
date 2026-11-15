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
    /// (<see cref="IMiscTabs"/>: ARES's SPAWN, UNITS, POWERS and CHEATS); DEBUG drives the [Debug] config entries. All shown, no unlock.
    /// Spooktober adds one tab (<see cref="ISetExtras"/>).
    /// </summary>
    internal sealed class MiscMfdPanel : MonoBehaviour, ISceneService
    {
        private const int Standby = 0;

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
            var titles = new List<string> { "MISC · STANDBY" };
            tabs = new List<(AvIcon icon, string label)> { (AvIcon.Stack2, "STANDBY") };
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

            // STANDBY is only the hidden fallback: the console opens on the first real tab (DEBUG is always there).
            Action sync = () =>
            {
                if (c.CurrentPage <= Standby) c.SetPage(Standby + 1);
                c.Footer.Set("Applies now. Saved with the game's config.", AvState.Inert);
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
