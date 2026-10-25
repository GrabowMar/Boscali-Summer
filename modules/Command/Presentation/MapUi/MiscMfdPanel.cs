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
    /// MISC, the last key on the rail: the drawer for everything that is not a cockpit system. CHEATS and DEBUG drive the [Debug]
    /// config entries and are compiled out of the public build; a module may add one tab of its own (<see cref="ISetExtras"/>,
    /// Spooktober), which shows only while it says so. With nothing to show, the console sits on an empty STANDBY page.
    /// </summary>
    internal sealed class MiscMfdPanel : MonoBehaviour, ISceneService
    {
        private const int Standby = 0;

        private readonly MfdPanelInstaller installer =
            new MfdPanelInstaller(MfdSlots.Misc, "MISC", "BoscaliMisc.Screen", preferLeft: false) { Host = true };
        private ModConfiguration settings;
        private AvConsole console;
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
            console = null;
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
            var titles = new List<string> { "MISC · STANDBY" };
            tabs = new List<(AvIcon icon, string label)> { (AvIcon.Stack2, "STANDBY") };
#if !PUBLIC_RELEASE
            int cheats = titles.Count;
            titles.Add("MISC · CHEATS");
            tabs.Add((AvIcon.Bolt, "CHEATS"));
            int debug = titles.Count;
            titles.Add("MISC · DEBUG");
            tabs.Add((AvIcon.Terminal, "DEBUG"));
#endif
            int extra = -1;
            if (extras != null)
            {
                extra = titles.Count;
                titles.Add(extras.ExtraTabTitle);
                tabs.Add((AvIcon.Skull, extras.ExtraTabLabel));
            }

            AvConsole c = AvConsole.Build(host, MfdSlots.Misc, titles[0], titles.Count, AvTokens.PanelWidth, height);
            c.PageChanged += index => c.SetTitle(titles[index]);
            console = c;
            bar = c.Tabs(tabs.ToArray());
            bar.SetShown(Standby, false);

            BuildStandby(c, c.Page(Standby));
#if !PUBLIC_RELEASE
            BuildCheats(c, c.Page(cheats), cheats);
            BuildDebug(c, c.Page(debug), debug);
#endif
            if (extras != null) extras.BuildExtraPage(extra, c.Page(extra), c);

            // STANDBY is only the fallback: the console sits on the first tab it shows, and leaves a tab that hides while open.
            Action sync = () =>
            {
                bool spook = extra >= 0 && extras.ExtraTabShown;
                if (extra >= 0) bar.SetShown(extra, spook);
                int first = Standby;
                for (int i = 1; i < titles.Count && first == Standby; i++)
                    if (i != extra || spook) first = i;
                int page = c.CurrentPage;
                if (page < 0 || page == Standby || (page == extra && !spook)) { if (page != first) c.SetPage(first); }
                c.Footer.Set(c.CurrentPage == Standby ? "Nothing assigned." : "Applies now. Saved with the game's config.", AvState.Inert);
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

#if !PUBLIC_RELEASE
        private void BuildCheats(AvConsole c, AvFlow flow, int page)
        {
            flow.Section(AvIcon.Bolt, "CHEATS", "TESTING AIDS");
            AvCellGrid grid = flow.Grid(2);
            Toggle(flow, grid, page, "BYPASS REQS", settings.Diagnostics.BypassRequirements);
            Toggle(flow, grid, page, "NO OPS COOLDOWN", settings.Diagnostics.DisableOpsCooldowns);

            var squad = settings.Squad;
            flow.Section(AvIcon.Skull, "HUNTING WING", "HOST ONLY");
            ConfigEntry<int> tier = squad.DebugWingTier;
            var tierRow = new AvRow(flow.Content);
            AvControl minus = tierRow.AddTrailing(new AvControl.Spec("", () => tier.Value = Mathf.Max(1, tier.Value - 1), AvButtonStyle.Quiet, AvIcon.Minus));
            AvControl plus = tierRow.AddTrailing(new AvControl.Spec("", () => tier.Value = Mathf.Min(5, tier.Value + 1), AvButtonStyle.Quiet, AvIcon.Plus));
            tierRow.Help = tier.Description.Description;
            flow.Add(tierRow);
            var result = new AvNote(flow.Content, "Host only. Fly an aircraft in a running mission.");
            AvButtons actions = flow.Buttons(
                new AvControl.Spec("SPAWN", () => result.Set(squad.SpawnDebugWing?.Invoke(tier.Value) ?? "", AvState.Info), AvButtonStyle.Primary, AvIcon.Skull),
                new AvControl.Spec("CLEAR MINE", () => result.Set(squad.ClearDebugWings?.Invoke() ?? "", AvState.Info), AvButtonStyle.Quiet, AvIcon.Trash));
            flow.Add(result);
            void Refresh()
            {
                tierRow.Set("TIER", tier.Value <= 2 ? "Two aircraft" : tier.Value <= 4 ? "Three aircraft" : "Four aircraft",
                    tier.Value + "/5", AvState.Info);
                minus.Interactable = tier.Value > 1;
                plus.Interactable = tier.Value < 5;
                bool installed = squad.SpawnDebugWing != null;
                actions.Controls[0].Interactable = installed;
                actions.Controls[1].Interactable = squad.ClearDebugWings != null;
                if (!installed) result.Set("Enable Progression and restart to install Squad.", AvState.Caution);
            }
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);

            c.Orders(page,
                "Testing aids, never in a public build.",
                "On a server only the host's BYPASS and COOLDOWN count.",
                "A new hunting wing replaces your previous debug wings.");
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
#endif
    }
}
