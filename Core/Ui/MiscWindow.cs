using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using NOAvionics;
using NuclearOption.MissionEditorScripts;
using UnityEngine;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// Standalone floating window for MISC / EXTRAS (ARES SPAWN, POWERS, CHEATS and Spooktober),
    /// toggled by a configurable keybind so players can still reach MISC options when the tactical
    /// map extension (Command / AVIONICS) is turned off.
    /// </summary>
    internal sealed class MiscWindow : MonoBehaviour, ISceneService
    {
        private const float TitleHeight = 30f;
        private const float Pad = 8f;
        private const float WindowWidth = AvTokens.PanelWidth + Pad * 2f;
        private const float WindowHeight = 820f;
        private const int SortingOrder = 260;

        private readonly WindowInput input = new WindowInput();
        private ConfigEntry<KeyCode> toggleKey;
        private AvWindow window;
        private RectTransform consoleHost;
        private AvConsole console;
        private IMiscTabs builtTabs;
        private ISetExtras builtExtras;

        internal static MiscWindow Active { get; private set; }

        internal void Configure(ConfigEntry<KeyCode> key) => toggleKey = key;

        private void OnEnable() => Active = this;

        public void ResetForScene()
        {
            Close();
            DestroyWindow();
        }

        private void OnDisable()
        {
            input.Force();
        }

        private void OnDestroy()
        {
            input.Force();
            DestroyWindow();
            if (Active == this) Active = null;
        }

        private void Update()
        {
            input.Tick();
            if (toggleKey == null || Application.isBatchMode) return;
            if (MissionPrompt.Active != null)
            {
                if (window != null && window.Visible) Close();
                return;
            }

            if (window != null && window.Visible)
            {
                ModuleServices.TryGet(out IMiscTabs liveTabs);
                ModuleServices.TryGet(out ISetExtras liveExtras);
                if (liveTabs != builtTabs || liveExtras != builtExtras) RebuildConsole();

                if (Input.GetKeyDown(KeyCode.Escape) ||
                    (!InputFieldChecker.InsideInputField && KeyPressed()))
                {
                    Close();
                    return;
                }

                input.Keep();
                input.Pointer(window.Root, Input.mousePosition, Input.GetMouseButtonDown(0));
                return;
            }

            if (!InputFieldChecker.InsideInputField && KeyPressed())
                Open();
        }

        private bool KeyPressed()
        {
            KeyCode key = toggleKey.Value;
            return key != KeyCode.None && Input.GetKeyDown(key);
        }

        internal void Open()
        {
            if (window != null && window.Visible) return;
            EnsureWindow();
            if (window == null) return;
            ModuleServices.TryGet(out IMiscTabs liveTabs);
            ModuleServices.TryGet(out ISetExtras liveExtras);
            if (console == null || liveTabs != builtTabs || liveExtras != builtExtras)
                RebuildConsole();
            UpdateFooter();
            window.Show();
            input.Take();
        }

        internal void Close()
        {
            if (window == null || !window.Visible) return;
            window.Hide();
            input.GiveBack();
        }

        private void EnsureWindow()
        {
            if (window != null) return;
            window = AvWindow.Build(transform, "misc-extras", "MISC  /  EXTRAS", WindowWidth, WindowHeight, SortingOrder);
            window.Closed += OnWindowClosed;
            window.CloseControl.Help = "Close (Esc)";
            consoleHost = AvLay.Child(window.Root, "MiscConsoleHost");
            float consoleHeight = WindowHeight - TitleHeight - AvGridTokens.Footer - Pad * 2f;
            AvLay.Place(consoleHost, Pad, TitleHeight + Pad, AvTokens.PanelWidth, consoleHeight);
            UpdateFooter();
        }

        private void RebuildConsole()
        {
            if (consoleHost == null) return;
            for (int i = consoleHost.childCount - 1; i >= 0; i--)
                Destroy(consoleHost.GetChild(i).gameObject);
            console = null;

            ModuleServices.TryGet(out ISetExtras extras);
            ModuleServices.TryGet(out IMiscTabs added);
            builtTabs = added;
            builtExtras = extras;

            var tabs = new List<(AvIcon icon, string label)>();
            for (int i = 0; added != null && i < added.Count; i++) tabs.Add((added.Icon(i), added.Label(i)));
            int extra = -1;
            if (extras != null)
            {
                extra = tabs.Count;
                tabs.Add((AvIcon.Skull, extras.ExtraTabLabel));
            }

            float consoleHeight = WindowHeight - TitleHeight - AvGridTokens.Footer - Pad * 2f;
            // Empty id: never collides with MiscMfdPanel's MFD console registration when Command is also on.
            AvConsole c = AvConsole.Build(consoleHost, "", "EXTRAS", System.Math.Max(1, tabs.Count), AvTokens.PanelWidth, consoleHeight);
            c.Chips(1)[0].Set("CHEATING · THESE ACTIONS ALTER THE MISSION", AvState.Caution);
            AvTabBar bar = tabs.Count > 1 ? c.Tabs(tabs.ToArray()) : null;

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
                flow.Section(AvIcon.Stack2, "EXTRAS", "NO PAGES AVAILABLE");
                flow.Add(new AvNote(flow.Content, "No extra tools or incident files are enabled for this session."));
            }

            c.Finish();
            console = c;
        }

        private void UpdateFooter()
        {
            if (window == null) return;
            KeyCode key = toggleKey != null ? toggleKey.Value : KeyCode.None;
            string closeHint = key != KeyCode.None ? "Press Esc or " + key + " to close." : "Press Esc to close.";
            window.Footer.Set("MISC / EXTRAS  ·  " + closeHint);
        }

        private void OnWindowClosed() => input.GiveBack();

        private void DestroyWindow()
        {
            if (window != null)
            {
                window.Closed -= OnWindowClosed;
                window.Hide();
                if (window.Root != null) Destroy(window.Root.gameObject);
                window = null;
            }
            consoleHost = null;
            console = null;
            builtTabs = null;
            builtExtras = null;
        }
    }
}
