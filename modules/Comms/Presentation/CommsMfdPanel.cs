using NOAvionics;
using System;
using BepInEx.Logging;
using BoscaliSummer.Modules.Comms.Configuration;
using BoscaliSummer.Modules.Comms.Domain;
using BoscaliSummer.Modules.Comms.Runtime;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
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

        // The six vanilla slots belong to WMC and the claimed screens; EVN hosts an extra slot on the
        // right, so COM hosts one on the left to keep the columns even.
        private readonly MfdPanelInstaller installer =
            new MfdPanelInstaller(MfdSlots.Comms, "COM", "BoscaliComms.Screen", preferLeft: true)
            { Host = true, Wrap = true, Clamp = false };
        private MFDScreen screen => installer.Screen;
        private AvConsole console;
        private AvChip[] chips;

        private float nextRefresh;
        private bool viewOpen;

        public void Configure(CommsSettings config, CommsManager manager, ManualLogSource log)
        {
            settings = config;
            comms = manager;
            installer.Log = log;
            installer.Builder = BuildScreen;
        }

        public void ResetForScene()
        {
            installer.Reset();
            console = null;
            chips = null;
            if (viewOpen) AvInput.Deselect();
            viewOpen = false;
            nextRefresh = 0f;
            ResetMap();
            ResetTalk();
            ResetGames();
            ResetLog();
        }

        private void OnDestroy() => ResetForScene();

        private void Update()
        {
            if (installer.Failed || settings == null || comms == null) return;
            if (!settings.Enabled.Value)
            {
                if (screen != null) ResetForScene();
                return;
            }
            if (!installer.Tick()) return;

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

        private RectTransform BuildScreen(RectTransform content, float height)
        {
            console = AvConsole.Build(content, "COM", "MULTIPLAYER COMMS", TabSpecs.Length, Width, height);
            chips = console.Chips(3);
            BindAudienceChip();
            console.Tabs(TabSpecs);
            console.PageChanged += _ => nextRefresh = 0f;

            BuildMapPage(console.Page(TabMap));
            BuildCallPage(console.Page(TabCall));
            BuildPollPage(console.Page(TabPoll));
            BuildGamePage(console.Page(TabGame));
            BuildLogPage(console.Page(TabLog));
            console.Finish();
            console.SetPage(TabMap);
            return null;
        }

        // ---- Refresh -----------------------------------------------------------------------

        private void Refresh()
        {
            if (console == null) return;
            CommsClientState state = comms.State;
            float now = Time.unscaledTime;

            bool online = comms.Online;
            string connText = comms.HostSilent ? "HOST NOT ANSWERING"
                : !online ? "NOT CONNECTED"
                : comms.IsHost ? "HOSTING COMMS" : "ONLINE";
            chips[0].Set(connText, !online || comms.HostSilent ? AvState.Caution : AvState.Info);
            chips[1].Set(comms.Channel == CommsChannel.Team ? "TO TEAM / CHANGE" : "TO ALL / CHANGE",
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

        private void BindAudienceChip()
        {
            Image hit = AvLay.Solid(chips[1].Rect, "Change audience", Color.clear);
            AvLay.Fill(hit.rectTransform);
            AvHit.On(hit).Click = _ => { comms.ToggleChannel(); nextRefresh = 0f; };
            AvHelpTip.Attach(hit.gameObject,
                "Change audience on any COM page. TEAM reaches your side; ALL reaches both sides for one post, then returns to TEAM. The host can disable ALL.");
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
