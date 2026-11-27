using NOAvionics;
using System;
using System.Collections.Generic;
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
    /// MISC, the last key on the rail: the drawer for everything that is not a cockpit system. Its tabs come from modules:
    /// ARES's SPAWN, POWERS and CHEATS (<see cref="IMiscTabs"/>, CHEATS ends in the DEV switches) and Spooktober's one
    /// (<see cref="ISetExtras"/>). All shown, no unlock; a single tab gets no tab bar.
    /// </summary>
    internal sealed class MiscMfdPanel : MonoBehaviour, ISceneService
    {
        private readonly MfdPanelInstaller installer =
            new MfdPanelInstaller(MfdSlots.Misc, "MISC", "BoscaliMisc.Screen", preferLeft: false) { Host = true };
        private ModConfiguration settings;
#if !PUBLIC_RELEASE
        private AvConsole console; // the dev automation hook reads it
#endif
        private AvTabBar bar;
        private List<(AvIcon icon, string label)> tabs;
        private IMiscTabs builtTabs;
        private ISetExtras builtExtras;

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
            builtTabs = null;
            builtExtras = null;
        }

        private void OnDestroy()
        {
            ResetForScene();
            if (Active == this) Active = null;
        }

        private void Update()
        {
            if (settings == null) return;
            ModuleServices.TryGet(out IMiscTabs added);
            ModuleServices.TryGet(out ISetExtras extras);
            if (installer.Screen != null && (added != builtTabs || extras != builtExtras)) ResetForScene();
            if (!installer.Tick() || installer.Bezel == null) return;
            // Navigation stays available; each owning service still checks authority for its actions.
            installer.Bezel.enabled = installer.Bezel.interactable = true;
        }

        private RectTransform BuildScreen(RectTransform host, float height)
        {
            ModuleServices.TryGet(out ISetExtras extras);
            ModuleServices.TryGet(out IMiscTabs added);
            builtTabs = added;
            builtExtras = extras;
            tabs = new List<(AvIcon icon, string label)>();
            for (int i = 0; added != null && i < added.Count; i++) tabs.Add((added.Icon(i), added.Label(i)));
            int extra = -1;
            if (extras != null)
            {
                extra = tabs.Count;
                tabs.Add((AvIcon.Skull, extras.ExtraTabLabel));
            }

            AvConsole c = AvConsole.Build(host, MfdSlots.Misc, "EXTRAS", Math.Max(1, tabs.Count), AvTokens.PanelWidth, height);
            if (tabs.Count > 0) c.Chips(1)[0].Set("CHEATING · THESE ACTIONS ALTER THE MISSION", AvState.Caution);
#if !PUBLIC_RELEASE
            console = c;
#endif
            if (tabs.Count > 1) bar = c.Tabs(tabs.ToArray());

            if (added != null)
            {
                for (int i = 0; i < added.Count; i++)
                    added.Build(i, i, c.Page(i), c);
            }
            if (extras != null) extras.BuildExtraPage(extra, c.Page(extra), c);

            if (bar != null)
            {
                AvControl[] tabControls = bar.Rect.GetComponentsInChildren<AvControl>(true);
                for (int i = 0; i < tabControls.Length && i < tabs.Count; i++)
                {
                    tabControls[i].Bracket = 0f;
                    tabControls[i].Help = i == extra
                        ? "Three boss encounters and your personal defeat records."
                        : added?.Help(i);
                }
            }
            if (tabs.Count == 0)
            {
                AvFlow flow = c.Page(0);
                flow.Add(new AvNote(flow.Content, "No extra tools or incident files are enabled for this session."));
            }

            c.Finish();
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
    }
}
