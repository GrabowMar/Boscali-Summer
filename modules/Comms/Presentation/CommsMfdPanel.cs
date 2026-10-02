using NOAvionics;
using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Modules.Comms.Configuration;
using BoscaliSummer.Modules.Comms.Domain;
using BoscaliSummer.Modules.Comms.Runtime;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Core.Game;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Comms.Presentation
{
    /// <summary>
    /// "COM" — the multiplayer comms screen. Five pages, one job each: MAP arms the pen, the
    /// shapes, pings, stickers and labels; CALL sends brevity calls; POLL asks and answers
    /// questions; CREW holds the dice, rock-paper-scissors and the map hunt with its
    /// leaderboard; LOG is the record of all of it plus the mute list.
    ///
    /// <para>The chip rail always says the connection state, which audience a post will reach
    /// (TEAM or ALL) and what the left mouse button will do on the map right now; the footer
    /// gives the armed tool's instructions or the host's latest refusal. Nothing here decides
    /// anything: every verb goes through <see cref="CommsManager"/>, and the host has the last
    /// word.</para>
    /// </summary>
    internal sealed partial class CommsMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const float RefreshInterval = 0.2f;
        private const float NoticeSeconds = 6f;

        private const int TabMap = 0;
        private const int TabCall = 1;
        private const int TabPoll = 2;
        private const int TabGame = 3;
        private const int TabLog = 4;

        private static readonly (AvIcon Icon, string Label)[] TabSpecs =
        {
            (AvIcon.Map2, "MAP"),
            (AvIcon.Message2, "CALL"),
            (AvIcon.QuestionMark, "POLL"),
            (AvIcon.UsersGroup, "CREW"),
            (AvIcon.ListDetails, "LOG"),
        };

        private CommsSettings settings;
        private CommsManager comms;
        private ManualLogSource logger;

        private MFDScreen screen;
        private GameObject screenRoot;
        private AvConsole console;
        private AvChip[] chips;

        private float nextAttempt;
        private float nextRefresh;
        private bool failed;
        private bool viewOpen;

        public void Configure(CommsSettings config, CommsManager manager, ManualLogSource log)
        {
            settings = config;
            comms = manager;
            logger = log;
        }

        public void ResetForScene()
        {
            MfdScreenHost.Release(MfdSlots.Comms);
            if (screenRoot != null) Destroy(screenRoot);
            screenRoot = null;
            screen = null;
            console = null;
            chips = null;
            if (viewOpen) AvInput.Deselect();
            viewOpen = false;
            nextAttempt = 0f;
            nextRefresh = 0f;
            failed = false;
            ResetMap();
            ResetTalk();
            ResetGames();
            ResetLog();
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (failed || settings == null || comms == null) return;
            if (!settings.Enabled.Value)
            {
                if (screen != null) ResetForScene();
                return;
            }
            if (Application.isBatchMode || !GameAccess.MfdAvailable)
            {
                failed = true;
                return;
            }

            if (screen == null)
            {
                if (Time.unscaledTime < nextAttempt) return;
                nextAttempt = Time.unscaledTime + 1f;
                TryInstall();
                return;
            }

            bool visible = screen.isActive &&
                SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.isActiveAndEnabled == true;
            SetViewOpen(visible);
            if (!visible || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        private void SetViewOpen(bool open)
        {
            if (viewOpen == open) return;
            viewOpen = open;
            // A text field that loses its screen must hand the keyboard back to the flight
            // controls: deselecting it fires its own onDeselect, which releases the guard.
            if (!open) AvInput.Deselect();
        }

        // ---- Installation ----------------------------------------------------------------

        private void TryInstall()
        {
            try
            {
                VirtualMFD mfd = SceneSingleton<DynamicMap>.i?.maximizedMapCanvas?.GetComponentInChildren<VirtualMFD>(true)
                    ?? FindObjectOfType<VirtualMFD>();
                if (mfd == null) return;

                // The six vanilla slots belong to WMC and the claimed screens; EVN hosts an
                // extra slot on the right, so COM hosts one on the left to keep the columns even.
                if (!MfdScreenHost.TryHost(MfdSlots.Comms, preferLeft: true, mfd,
                    out List<Button> buttons, out List<MFDScreen> screens, out int slot, out bool left))
                {
                    failed = true;
                    logger?.LogWarning("COM MFD unavailable: could not add a host button.");
                    return;
                }

                MFDScreen template = MfdBezel.FindTemplate(screens) ?? MfdBezel.FindTemplate(mfd);
                if (template == null)
                {
                    MfdScreenHost.Release(MfdSlots.Comms);
                    return;
                }

                screen = Build(template, buttons[slot]);
                if (screen == null)
                {
                    MfdScreenHost.Release(MfdSlots.Comms);
                    failed = true;
                    return;
                }

                if (!MfdBezel.Bind(mfd, buttons, screens, slot, left, screen))
                {
                    ResetForScene();
                    failed = true;
                    logger?.LogWarning("COM MFD unavailable: bezel changed during installation.");
                    return;
                }
                logger?.LogInfo("COM MFD installed on " + (left ? "left" : "right") + " bezel slot " + (slot + 1) + ".");
            }
            catch (Exception e)
            {
                ResetForScene();
                failed = true;
                logger?.LogError("COM MFD install failed: " + e);
            }
        }

        private MFDScreen Build(MFDScreen template, Button bezel)
        {
            var root = new GameObject("BoscaliComms.Screen", typeof(RectTransform));
            screenRoot = root;
            var rootRect = root.GetComponent<RectTransform>();
            rootRect.SetParent(template.transform.parent, false);

            var templateRect = (RectTransform)template.transform;
            rootRect.anchorMin = templateRect.anchorMin;
            rootRect.anchorMax = templateRect.anchorMax;
            rootRect.pivot = templateRect.pivot;
            rootRect.localScale = templateRect.localScale;

            float height = AvLay.ResolveHeight(templateRect.parent as RectTransform, AvTokens.PanelHeight, AvTokens.PanelHeightMax);
            rootRect.sizeDelta = new Vector2(Width, height);

            var contentObject = new GameObject("Content", typeof(RectTransform));
            var content = contentObject.GetComponent<RectTransform>();
            content.SetParent(rootRect, false);
            content.anchorMin = Vector2.zero;
            content.anchorMax = Vector2.one;
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;

            console = AvConsole.Build(content, "COM", "MULTIPLAYER COMMS", TabSpecs.Length, Width, height);
            chips = console.Chips(3);
            console.Tabs(TabSpecs);
            console.PageChanged += _ => nextRefresh = 0f;

            BuildMapPage(console.Page(TabMap));
            BuildCallPage(console.Page(TabCall));
            BuildPollPage(console.Page(TabPoll));
            BuildGamePage(console.Page(TabGame));
            BuildLogPage(console.Page(TabLog));
            console.Finish();

            MFDScreen result = root.AddComponent<MFDScreen>();
            result.shortName = MfdSlots.Comms;
            result.displayPanel = contentObject;
            result.aircraftOnly = false;
            result.label = bezel != null ? bezel.GetComponentInChildren<TextMeshProUGUI>(true) : null;
            result.highlight = FindHighlight(bezel);
            if (result.label == null || result.highlight == null)
            {
                Destroy(root);
                return null;
            }

            screenRoot = root;
            console.SetPage(TabMap);
            return result;
        }

        private static Image FindHighlight(Button button)
        {
            if (button == null) return null;
            Image[] images = button.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
                if (images[i].gameObject != button.gameObject) return images[i];
            return button.GetComponent<Image>();
        }

        // ---- Refresh -----------------------------------------------------------------------

        private void Refresh()
        {
            if (console == null) return;
            CommsClientState state = comms.State;
            float now = Time.unscaledTime;

            bool online = comms.Online;
            string connText = !online ? "NOT CONNECTED"
                : comms.HostSilent ? "HOST NOT ANSWERING"
                : comms.IsHost ? "HOSTING COMMS" : "ONLINE";
            chips[0].Set(connText, !online || comms.HostSilent ? AvState.Caution : AvState.Info);
            chips[1].Set(comms.Channel == CommsChannel.Team ? "TO TEAM" : "TO ALL",
                comms.Channel == CommsChannel.Team ? AvState.Ready : AvState.Caution);
            chips[2].Set(ToolName(comms.Tool), comms.Tool == CommsTool.None ? AvState.Inert : AvState.Info);

            switch (console.CurrentPage)
            {
                case TabMap: RefreshMap(); break;
                case TabCall: RefreshCalls(now); break;
                case TabPoll: RefreshPolls(now); break;
                case TabGame: RefreshGames(now); break;
                case TabLog: RefreshLog(now); break;
            }

            string alert = comms.HostSilent
                ? "HOST NOT ANSWERING \u00b7 NEEDS BOSCALI SUMMER"
                : state.Notice != null && state.NoticeIsError && now - state.NoticeAt < NoticeSeconds ? state.Notice : null;
            string prompt = comms.Tool != CommsTool.None ? comms.Prompt(comms.Tool) : null;
            string ambient = state.Notice != null && !state.NoticeIsError && now - state.NoticeAt < NoticeSeconds
                ? state.Notice
                : Ambient(state);
            if (alert != null) console.Footer.Set(alert, AvState.Caution);
            else if (prompt != null) console.Footer.Set(prompt, AvState.Info);
            else console.Footer.Set(ambient, AvState.Inert);
        }

        private string Ambient(CommsClientState state)
        {
            int open = 0;
            for (int i = 0; i < state.Polls.Count; i++) if (!state.Polls[i].Closed) open++;
            string line = AvNum.Fixed(state.Board.CountOf(CommsItemKind.Ping), 0) + " PINGS · " +
                          AvNum.Fixed(open, 0) + " POLLS OPEN · " + AvNum.Fixed(state.Duels.Count, 0) + " CHALLENGES";
            KeyCode hold = settings.DrawHoldKey.Value;
            return hold != KeyCode.None ? line + " · HOLD " + KeyName(hold) + " + DRAG TO DRAW" : line;
        }

        private static string ToolName(CommsTool tool)
        {
            switch (tool)
            {
                case CommsTool.None: return "MAP FREE";
                case CommsTool.HuntHide: return "HIDING";
                case CommsTool.HuntGuess: return "GUESSING";
                case CommsTool.Label: return "TEXT";
                default: return tool.ToString().ToUpperInvariant();
            }
        }

        private static string KeyName(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftShift: return "L-SHIFT";
                case KeyCode.RightShift: return "R-SHIFT";
                case KeyCode.LeftControl: return "L-CTRL";
                case KeyCode.LeftAlt: return "L-ALT";
                case KeyCode.Mouse2: return "MIDDLE MOUSE";
                case KeyCode.Mouse3: return "MOUSE 4";
                case KeyCode.Mouse4: return "MOUSE 5";
                default: return key.ToString().ToUpperInvariant();
            }
        }

        // ---- Shared helpers ------------------------------------------------------------------

        /// <summary>The kit's fixed state vocabulary a comms tone maps onto (R1: colour always carries a word too).</summary>
        private static AvState ToneState(CommsTone tone)
        {
            switch (tone)
            {
                case CommsTone.Friendly: return AvState.Ready;
                case CommsTone.Caution: return AvState.Caution;
                case CommsTone.Danger: return AvState.Danger;
                default: return AvState.Info;
            }
        }

        /// <summary>
        /// Lays <paramref name="specs"/> out as full rows of <paramref name="columns"/> equal
        /// icon buttons (a short last row stays left-aligned rather than stretching). Returns
        /// the built controls in the same order as <paramref name="specs"/>, for latching.
        /// <paramref name="helps"/> (optional, same order) becomes each button's hover help.
        /// </summary>
        private static AvControl[] ButtonGrid(AvFlow page, AvControl.Spec[] specs, int columns, string[] helps = null)
        {
            var built = new AvControl[specs.Length];
            for (int row = 0; row * columns < specs.Length; row++)
            {
                int count = Mathf.Min(columns, specs.Length - row * columns);
                var rowSpecs = new AvControl.Spec[count];
                Array.Copy(specs, row * columns, rowSpecs, 0, count);
                AvButtons line = page.Buttons(rowSpecs);
                for (int c = 0; c < count; c++)
                {
                    int index = row * columns + c;
                    built[index] = line.Controls[c];
                    if (helps != null && index < helps.Length) built[index].Help = helps[index];
                }
            }
            return built;
        }

        /// <summary>A wrapped, resizing line of secondary prose (replaces the v1 "hint"/"row-sub" labels).</summary>
        private sealed class AvNote : AvPart
        {
            private readonly TMP_Text text;

            public AvNote(RectTransform parent, string initial = "")
            {
                Rect = AvLay.Child(parent, "Note");
                text = AvText.Make(Rect, "Text", AvTextRole.ProseSmall, initial ?? "", TextAlignmentOptions.TopLeft, true);
                Restyle();
            }

            public string Text
            {
                get => text.text;
                set { if (text.text != (value ?? "")) text.text = value ?? ""; }
            }

            public override float Measure(float width) => Mathf.Max(AvGridTokens.RowDense, AvText.Height(text, width));
            public override void Place(AvSlot s) { base.Place(s); AvLay.Fill(text.rectTransform); }
            public override void Restyle() => text.color = AvStyleHost.Resolve(AvStyleHost.FuiStyle("row-sub").Color, AvTheme.Dim);
        }

        /// <summary>Hover help on a stepper (its buttons and, by bubbling, its whole line).</summary>
        private static void Tip(AvStepper stepper, string help)
        {
            AvHelpTip.Attach(stepper.Rect.gameObject, help);
            stepper.Minus.Help = help;
            stepper.Plus.Help = help;
        }

        /// <summary>Hover help on a text field (the field frame raycasts; the tip bubbles up from it).</summary>
        private static void Tip(AvField field, string help) => AvHelpTip.Attach(field.Rect.gameObject, help);

        private static void Show(AvPart part, bool visible)
        {
            // SetShown also tells the owning flow, so the page re-lays on the next tick instead of on the slow sweep.
            part?.SetShown(visible);
        }

        private static void Show(Component component, bool visible)
        {
            if (component != null && component.gameObject.activeSelf != visible) component.gameObject.SetActive(visible);
        }

        private static string Ago(float seconds)
        {
            int total = Mathf.Max(0, Mathf.FloorToInt(seconds));
            if (total < 60) return total + "s";
            if (total < 3600) return total / 60 + "m";
            return total / 3600 + "h";
        }

        private string Who(ulong author, string name) => author == comms.LocalId ? "YOU" : name;

        /// <summary>
        /// How many rows of a paged/pooled list fit the page: everything else on it is
        /// <paramref name="fixedHeight"/>, a row is 33 px with its gap. A short console gets fewer rows
        /// instead of a scrollbar; a tall one gets more, so the list is what soaks up the height.
        /// </summary>
        private static int FitRows(AvFlow page, float fixedHeight, int max, int min)
        {
            float viewport = page.ViewportHeight;
            if (viewport <= 0f) return max;
            return Mathf.Clamp(Mathf.FloorToInt((viewport - fixedHeight) / 33f), min, max);
        }
    }
}
