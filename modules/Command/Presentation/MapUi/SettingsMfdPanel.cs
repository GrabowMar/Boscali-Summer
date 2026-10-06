using NOAvionics;
using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BepInEx.Configuration;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Core.Game;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation.MapUi
{
    /// <summary>
    /// The SET console (kit v2). A THIS PILOT / SERVER switch under the header picks one of two
    /// consoles, each with at most four icon tabs: CLIENT pages hold only settings stored on this
    /// machine; SERVER pages are host-authoritative and read-only (locked, with the reason on every
    /// row) for a remote client. Both consoles are built once and only one is shown, so scroll
    /// position and page survive a switch and the hidden one does not tick.
    /// </summary>
    internal sealed partial class SettingsMfdPanel : MonoBehaviour, ISceneService
    {
        // Page indices are per console (each console has its own ticker and page set).
        private const int CDisplay = 0, CMap = 1, CCockpit = 2, CImmersion = 3, CPerf = 4;
        private const int SWorld = 0, SForces = 1, SEffects = 2, STasking = 3;

        private static readonly string[] ClientPageNames =
        {
            "PILOT · DISPLAY STYLE", "PILOT · MAP & BACKDROP", "PILOT · COCKPIT", "PILOT · IMMERSION", "PILOT · PERFORMANCE"
        };

        private static readonly string[] ServerPageNames =
        {
            "SERVER · WORLD RULES", "SERVER · FORCES & ECONOMY", "SERVER · WORLD EFFECTS", "SERVER · FACTION TASKING"
        };

        /// <summary>The last mode the pilot picked; kept for the session, across map opens and scene reloads.</summary>
        private static bool lastServerMode;

        private CommandSettings settings;
        private ComMapOverlay overlay;
        private ManualLogSource logger;
        private HostSettingsBoard hostSettings;
        private ClientSettingsBoard clientSettings;
        private GameObject root;
        private GameObject surface;
        private MFDScreen screen;
        private AvConsole con;
        private AvConsole clientCon;
        private AvConsole serverCon;
        private bool serverMode;
        private List<MFDScreen> boundScreens;
        private int boundSlot = -1;
        private bool claimed;
        private bool failed;
        private float nextTick;
        private string actionEcho;
        private float actionEchoUntil;
        private bool appearancePending;
        private bool overlayPending;
        private bool layoutPending;
        private bool tickerPending;

        // The last few changes made on this screen this session, newest last. The SERVER tabs list them under
        // their controls so a change is confirmed after the one-line footer echo has faded.
        private const int ChangeLogSize = 8;
        private readonly string[] changeText = new string[ChangeLogSize];
        private readonly float[] changeTime = new float[ChangeLogSize];
        private int changeCount;

        public void Configure(CommandSettings config, ManualLogSource log, ComMapOverlay mapOverlay = null,
            HostSettingsBoard hostSettingsBoard = null, ClientSettingsBoard clientSettingsBoard = null)
        {
            settings = config;
            overlay = mapOverlay;
            logger = log;
            hostSettings = hostSettingsBoard;
            clientSettings = clientSettingsBoard;
            MfdMapDeck.Configure(config);
            ApplyDisplayEffects();
            if (configFile != null) configFile.SettingChanged -= OnSettingChanged;
            configFile = config.ExpandedMapUi.ConfigFile;
            configFile.SettingChanged += OnSettingChanged;
        }

        private ConfigFile configFile;

        private void OnSettingChanged(object sender, SettingChangedEventArgs args)
        {
            // Live world-state side effects (display glass, map deck, news ticker, overlay
            // sync) must still react when F1 or another session changes a key, whether or not
            // this panel is even installed. The console's own row values re-read the config
            // on their own next Slow tick, so no "dirty" flag is needed for those.
            string section = args.ChangedSetting.Definition.Section;
            if (section != "Command" && section != "Hud" && section != "Avionics" &&
                section != "Weather" && section != "Garrisons" && section != "Performance" &&
                section != "Immersion") return;
            if (section != "Command") return;
            switch (args.ChangedSetting.Definition.Key)
            {
                case "ExpandedMapUi": layoutPending = true; break;
                case "GridRefreshInterval": overlayPending = true; break;
                case "NewsTicker": tickerPending = true; break;
                case "NewsTickerSpeed": break;
                default: appearancePending = true; break;
            }
        }

        private void Update()
        {
            if (settings == null || failed || Application.isBatchMode || Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + (screen == null ? 1f : 0.25f);
            ApplyPending();
            if (screen == null)
            {
                // A torn-down dock slot can destroy the screen root without a scene reset;
                // release the stale reservation so the panel can install again.
                if (claimed) ReleaseClaim();
                VirtualMFD mfd = MapMfdLookup.Resolve(SceneSingleton<DynamicMap>.i?.maximizedMapCanvas);
                if (mfd == null) return;
                try { Install(mfd); }
                catch (Exception error)
                {
                    ResetForScene();
                    failed = true;
                    logger?.LogWarning("SET panel installation failed: " + error);
                }
            }
            bool mapOpen = DynamicMap.mapMaximized &&
                SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            bool visible = screen != null && screen.isActive && mapOpen;

            // Vanilla's close path is authoritative for showing. This keeps the surface in
            // step with the screen's own state, so a close path that skips CloseScreen can no
            // longer leave the settings panel floating over the cockpit.
            if (surface != null && surface.activeSelf != visible) surface.SetActive(visible);
        }

        private static bool HostAuthority() => GameAccess.IsServer();

        private string AmbientStatus(bool server, int page)
        {
            if (server)
            {
                if (page == STasking)
                    return HostAuthority()
                        ? "Host tasking board. Refreshes while visible."
                        : "Locked. The host issues faction tasking.";
                return HostAuthority()
                    ? "Host controls are saved automatically."
                    : "Locked. These are the host's values for this server.";
            }
            if (page == CPerf || page == CImmersion) return "Changes apply now. No mission or game restart.";
            return "Saved automatically. Hover a control for help.";
        }

        private void Install(VirtualMFD mfd)
        {
            if (claimed) return;
            if (!MfdBezel.TryClaim(MfdSlots.Set, preferLeft: false, mfd,
                    out var buttons, out var screens, out int slot, out bool left)) return;
            claimed = true;

            var template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
            if (template == null)
            {
                MfdBezel.Release(MfdSlots.Set);
                claimed = false;
                return;
            }

            var bezel = buttons[slot];
            var label = bezel.GetComponentInChildren<TextMeshProUGUI>(true);
            var highlight = MfdPanelInstaller.FindHighlight(bezel);
            if (label == null || highlight == null)
                throw new InvalidOperationException("SET bezel label or highlight is missing.");

            RectTransform rect = MfdPanelInstaller.MakeRoot(template, "BoscaliSummer.SET", true, false, out root, out float height);

            // The dock naming convention ("Content" marks a mod-built panel, vs. a stock
            // screen's "DisplayPanel") predates kit v2 and is enforced elsewhere
            // (MfdPanelDock.IsStockScreen); it is unrelated to AvConsole's own chrome, which
            // is built as a nested child of this wrapper.
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(rect, false);
            var body = (RectTransform)content.transform;
            AvLay.Fill(body);
            surface = content;

            BuildConsoles(body, height);

            screen = root.AddComponent<MFDScreen>();
            screen.shortName = MfdSlots.Set;
            screen.displayPanel = content;
            screen.aircraftOnly = false;
            screen.label = label;
            screen.highlight = highlight;
            boundScreens = screens;
            boundSlot = slot;
            if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                throw new InvalidOperationException("SET bezel changed before binding.");

            if (DynamicMap.mapMaximized)
            {
                var dynMap = SceneSingleton<DynamicMap>.i;
                if (dynMap != null) MfdRailPatch.OnStructureChanged(dynMap);
            }

            MfdMapDeck.ApplyAppearance(settings);
            logger?.LogInfo("SET MFD installed on " + (left ? "left" : "right") + " bezel slot " + (slot + 1) + ".");
        }

        private static void ApplyTabHelp(AvTabBar bar, params string[] hints)
        {
            AvControl[] tabs = bar.Rect.GetComponentsInChildren<AvControl>(true);
            for (int i = 0; i < tabs.Length && i < hints.Length; i++)
                if (hints[i] != null) tabs[i].Help = hints[i];
        }

        private void RefreshChrome(AvConsole c, ModeSwitch mode, bool server)
        {
            AvUiSound.Volume = settings.UiSoundVolume.Value;
            mode.Set(serverMode, HostAuthority());
            string echo = Time.unscaledTime < actionEchoUntil ? actionEcho : null;
            c.Footer.Set(echo ?? AmbientStatus(server, c.CurrentPage), echo != null ? AvState.Info : AvState.Inert);
        }

        /// <summary>Confirm the action on the footer for a moment. Text, not colour alone.</summary>
        private void Echo(string text)
        {
            actionEcho = text;
            actionEchoUntil = Time.unscaledTime + 1.6f;
            if (changeCount == ChangeLogSize)
            {
                Array.Copy(changeText, 1, changeText, 0, ChangeLogSize - 1);
                Array.Copy(changeTime, 1, changeTime, 0, ChangeLogSize - 1);
                changeCount--;
            }
            changeText[changeCount] = text;
            changeTime[changeCount] = Time.unscaledTime;
            changeCount++;
        }

        private bool EffectsEnabled() => settings.DisplayEffects.Value;
        private static string EffectsDisabled() => "Turn on DISPLAY EFFECTS first.";

        private void ApplyDisplayEffects()
        {
            AvDisplayGlass.Configure(settings.DisplayEffects.Value, settings.DisplayGlass.Value,
                settings.DisplayAutoLight.Value, settings.DisplayScanlines.Value,
                settings.DisplayVignette.Value, settings.DisplayTint.Value, settings.DisplayTintStrength.Value);
        }

        private void ApplyPending()
        {
            if (appearancePending || layoutPending) ApplyDisplayEffects();
            if (layoutPending) MfdRailPatch.Reconcile();
            if (appearancePending || layoutPending) MfdMapDeck.ApplyAppearance(settings);
            if (overlayPending) overlay?.SyncSettings();
            if (tickerPending)
            {
                var map = SceneSingleton<DynamicMap>.i;
                if (settings.ExpandedMapUi.Value && DynamicMap.mapMaximized && map != null &&
                    MfdLayout.TryResolve(map.maximizedMapCanvas, out var columns))
                    MfdNewsTicker.Ensure(map.maximizedMapCanvas, columns, settings);
            }
            appearancePending = overlayPending = layoutPending = tickerPending = false;
        }

        private void Changed() => ApplyPending();

        // ------------------------------------------------------------------ row helpers
        //
        // Every SET control is one of a few compact shapes, all built from kit v2 primitives and refreshed at
        // the console's 2 Hz "Slow" tier while their page is open:
        //   * ToggleCell  - an AvCell (LED + name + ON/OFF), two or three to a line; the sentence lives on hover.
        //   * Ring        - a SetRingCell dial with - / + for a level (percent, seconds, a named step), packed by
        //                   a SetRingRow so hidden dials never leave a hole.
        //   * Stepper     - the classic "name - value +" AvRow, kept for values that are words or file names.

        private AvCell ToggleCell(AvFlow flow, AvCellGrid grid, int page, string title, string help, Func<bool> get,
            Action<bool> set, Func<bool> enabled = null, Func<string> reason = null)
        {
            AvCell cell = null;
            Action<bool> apply = v =>
            {
                if (enabled != null && !enabled()) return;
                set(v);
                Echo(title + " — " + (get() ? "ON" : "OFF"));
                Changed();
            };
            cell = grid != null
                ? grid.Toggle(title, "", get, apply)
                : AvCell.Toggle(flow.Content, title, "", get, apply);
            void RefreshCell()
            {
                bool avail = enabled == null || enabled();
                cell.Interactable = avail;
                cell.Help = avail ? help : (reason != null ? reason() : help);
                cell.Refresh();
            }
            RefreshCell();
            flow.Ticker.Add(page, AvTickRate.Slow, RefreshCell);
            return cell;
        }

        private SetRingCell Ring(AvFlow flow, SetRingRow row, int page, string title, Func<string> get, Func<float> level01,
            Action<int> change, Func<bool> decrease, Func<bool> increase, string help, Func<bool> enabled = null,
            Func<string> reason = null, Func<string> ringText = null)
        {
            SetRingCell cell = null;
            void Refresh()
            {
                bool avail = enabled == null || enabled();
                string text = get();
                string why = avail ? help : (reason != null ? reason() : help);
                cell.Bind(ringText != null ? ringText() : text, level01(), avail, decrease(), increase(),
                    avail ? help + " Now " + text + "." : why,
                    avail ? help + " Decrease. Now " + text + "." : why,
                    avail ? help + " Increase. Now " + text + "." : why);
            }
            void Nudge(int d)
            {
                if ((enabled != null && !enabled()) || !(d < 0 ? decrease() : increase())) return;
                change(d);
                Echo(title + " — " + get());
                Changed();
                Refresh();
            }
            cell = new SetRingCell(flow.Content, title, () => Nudge(-1), () => Nudge(1));
            row.Add(cell);
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
            return cell;
        }

        /// <summary>A 0-to-max level stored as a float config entry, as a dial that steps by <paramref name="step"/>.</summary>
        private SetRingCell PercentRing(AvFlow flow, SetRingRow row, int page, string title, ConfigEntry<float> entry,
            float min, float max, float step, string help, Func<bool> enabled = null, Func<string> reason = null) =>
            Ring(flow, row, page, title, () => AvNum.Percent(entry.Value), () => Mathf.Clamp01(entry.Value / max),
                d => entry.Value = Mathf.Clamp(Mathf.Round((entry.Value + d * step) * 100f) / 100f, min, max),
                () => entry.Value > min + .001f, () => entry.Value < max - .001f, help, enabled, reason);

        private AvRow Stepper(AvFlow flow, int page, string title, Func<string> get, Action<int> change,
            Func<bool> decrease, Func<bool> increase, string help, Func<bool> enabled = null,
            Func<string> reason = null, bool readOnlyValue = false)
        {
            var row = new AvRow(flow.Content);
            AvControl minus = row.AddTrailing(new AvControl.Spec("", () =>
            {
                if ((enabled != null && !enabled()) || !decrease()) return;
                change(-1);
                Echo(title + " — " + get());
                Changed();
            }, AvButtonStyle.Quiet, AvIcon.Minus));
            AvControl plus = row.AddTrailing(new AvControl.Spec("", () =>
            {
                if ((enabled != null && !enabled()) || !increase()) return;
                change(1);
                Echo(title + " — " + get());
                Changed();
            }, AvButtonStyle.Quiet, AvIcon.Plus));
            void Refresh()
            {
                bool avail = enabled == null || enabled();
                minus.Interactable = avail && decrease();
                plus.Interactable = avail && increase();
                string text = avail || readOnlyValue ? get() : "--";
                string why = avail ? help : (reason != null ? reason() : help);
                minus.Help = avail ? help + " Previous / decrease. " + text : why;
                plus.Help = avail ? help + " Next / increase. " + text : why;
                row.Set(title, avail ? "" : why, text, AvState.Info);
            }
            flow.Add(row);
            Refresh();
            flow.Ticker.Add(page, AvTickRate.Slow, Refresh);
            return row;
        }

        public void ResetForScene()
        {
            ReleaseClaim();
            if (root != null) Destroy(root);
            root = null;
            screen = null;
            failed = false;
            nextTick = 0f;
            actionEcho = null;
            actionEchoUntil = 0f;
            changeCount = 0;
            tasking = null;
            taskCards = null;
            nextTaskingRefresh = 0f;
            clientCon = null;
            serverCon = null;
            AvUiSound.Reset();
        }

        /// <summary>
        /// Drop the bezel reservation and every reference into the screen tree without
        /// touching the service itself. A torn-down dock slot can destroy the root before a
        /// scene reset; releasing here lets the next Update install a fresh panel.
        /// </summary>
        private void ReleaseClaim()
        {
            MfdBezel.Release(MfdSlots.Set);
            if (boundScreens != null && boundSlot >= 0 && boundSlot < boundScreens.Count &&
                ReferenceEquals(boundScreens[boundSlot], screen)) boundScreens[boundSlot] = null;
            boundScreens = null;
            boundSlot = -1;
            claimed = false;
            surface = null;
            con = null;
            clientCon = null;
            serverCon = null;
            taskRequest = null;
            taskNote = null;
            taskList = null;
            tasking = null;
            taskCards = null;
        }

        private void OnDestroy()
        {
            if (configFile != null) configFile.SettingChanged -= OnSettingChanged;
            ResetForScene();
        }
    }
}
