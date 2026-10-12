using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// The STR bezel: the Station Board (design D1). Tabs BOARD, SITREP and INFO under four pinned tiles; a function of <see cref="StrViewModel"/>, bound to the
    /// hub's model each time its revision changes. No part reads a service, a clock or a status string, and a press goes to the hub through
    /// <see cref="Controls"/>, the one command path. It only exists beside the maximised relief map.
    /// </summary>
    internal sealed partial class StrMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const int PageBoard = 0, PageSitrep = 1, PageInfo = 2;
        private const int MaxLanes = StrViewModel.MaxFronts;

        // ---- Dependencies ----------------------------------------------------------------

        private CommandSettings settings;
        private IStrHub hub;
        private ManualLogSource log;

        // ---- Screen ----------------------------------------------------------------------

        private readonly MfdPanelInstaller installer =
            new MfdPanelInstaller(MfdSlots.Str, "STR", "BoscaliStrategic.Screen", preferLeft: true);
        private MFDScreen screen => installer.Screen;
        private AvConsole console;
        private AvMetric[] metrics;

        private readonly List<IStrBound> parts = new List<IStrBound>(32);
        private StrViewModel boundModel;
        private int boundRevision = -1;
        private float nextLog;

        // BOARD
        private AvSection otherSection, allSection;
        private readonly StrFrontLane[] lanes = new StrFrontLane[MaxLanes];
        // SITREP
        private AvSection sortieSection;
        private readonly AvRow[] sensors = new AvRow[4];
        // INFO
        private StrNote logNote;
        private readonly AvRow[] misc = new AvRow[StrViewModel.MiscCount];

        /// <summary>The scene's panel, for the automation hooks.</summary>
        internal static StrMfdPanel Active { get; private set; }

        /// <summary>The kit console; the harness reads its pages and ticker.</summary>
        internal AvConsole Console => console;

        /// <summary>Control ids for the sim and the harness (annex 05 P11): an id maps to a gate read at press time and a press action.</summary>
        internal StrControls Controls { get; } = new StrControls();

        internal bool ScreenActiveForAutomation => screen != null && screen.isActive;
        internal int PageForAutomation => console != null ? console.CurrentPage : -1;

        // ==================================================================================

        internal void Configure(CommandSettings config, IStrHub strHub, ManualLogSource logger)
        {
            Active = this;
            settings = config;
            hub = strHub;
            log = logger;
            installer.Log = logger;
            installer.Builder = BuildScreen;
        }

        public void ResetForScene()
        {
            installer.Reset();
            ResetContent();
        }

        private void ResetContent()
        {
            console = null;
            metrics = null;
            parts.Clear();
            Controls.Clear();
            boundModel = null;
            boundRevision = -1;
            otherSection = allSection = sortieSection = null;
            logNote = null;
            for (int i = 0; i < lanes.Length; i++) lanes[i] = null;
            for (int i = 0; i < sensors.Length; i++) sensors[i] = null;
            for (int i = 0; i < misc.Length; i++) misc[i] = null;
            nextLog = 0f;
        }

        private void OnDestroy() => ResetForScene();

        /// <summary>Automation: the bezel press plus a page switch, the way a player opens the tab (map already maximised).</summary>
        internal void OpenForAutomation(int page)
        {
            if (screen != null && !screen.isActive && installer.Bezel != null) installer.Bezel.onClick.Invoke();
            if (console != null) console.SetPage(Mathf.Clamp(page, 0, 2));
            boundRevision = -1;
        }

        /// <summary>Harness seam: builds the console into <paramref name="root"/> at <paramref name="height"/>, bound to <paramref name="strHub"/>; no installer, no reflection.</summary>
        internal static StrMfdPanel BuildForTest(RectTransform root, float height, IStrHub strHub)
        {
            var go = new GameObject("StrMfdPanel");
            go.transform.SetParent(root, false);
            StrMfdPanel panel = go.AddComponent<StrMfdPanel>();
            panel.hub = strHub;
            panel.BuildScreen(root, height);
            return panel;
        }

        private void Update()
        {
            if (installer.Failed || settings == null || !settings.Enabled.Value) return;
            if (!installer.Tick()) return;
            // A closed screen costs nothing.
            if (!screen.isActive) return;
            TickBind();
        }

        /// <summary>Binds when the hub built something new; the revision only advances after a bind that succeeded, and a failure logs at most every five seconds.</summary>
        private void TickBind()
        {
            if (console == null || hub == null) return;
            StrViewModel vm = hub.Current;
            if (vm == null || !vm.Built) return;
            if (ReferenceEquals(vm, boundModel) && hub.Revision == boundRevision) return;
            try
            {
                BindNow();
            }
            catch (Exception e)
            {
                if (Time.unscaledTime >= nextLog)
                {
                    nextLog = Time.unscaledTime + 5f;
                    log?.LogError("STR panel bind failed: " + e);
                }
            }
        }

        // ---- Installation ----------------------------------------------------------------

        private RectTransform BuildScreen(RectTransform rootRect, float height)
        {
            ResetContent();
            console = AvConsole.Build(rootRect, "STR", "THEATER", 3, Width, height);
            AvTabBar tabBar = console.Tabs(
                (AvIcon.Stack2, "BOARD"),
                (AvIcon.Radar2, "SITREP"),
                (AvIcon.InfoCircle, "INFO"));
            TabHelp.Apply(tabBar,
                "The live operation, the staff's proposals, the platforms and the posture.",
                "Force balance, sensors, the alert level and the AI sortie board.",
                "Your chain of command and the enemy posts you have confirmed, the staff's log, and report ages and host switches.");
            metrics = console.Metrics("SECTORS", "AIR", "GROUND", "POOL");

            BuildBoard(console.Page(PageBoard));
            BuildSitrep(console.Page(PageSitrep));
            BuildInfo(console.Page(PageInfo));
            console.Finish();
            console.SetPage(PageBoard);
            return console.Root;
        }

        private T Add<T>(AvFlow flow, T part) where T : StrPart
        {
            flow.Add(part);
            parts.Add(part);
            return part;
        }

        private void BuildBoard(AvFlow p)
        {
            Add(p, new StrBannerPart(p.Content, Controls, hub));
            Add(p, new StrPostureRow(p.Content, Controls, hub, false));
            Add(p, new StrHeroLane(p.Content, Controls, hub));
            for (int i = 0; i < StrViewModel.MaxProposals; i++) Add(p, new StrProposalTicket(p.Content, Controls, hub, i));
            otherSection = p.Section(AvIcon.Map2, "OTHER PLATFORMS", StrPanelText.NotchCaption);
            allSection = p.Section(AvIcon.Map2, "PLATFORMS", StrPanelText.NotchCaption);
            for (int i = 0; i < MaxLanes; i++) lanes[i] = Add(p, new StrFrontLane(p.Content, Controls, hub, i));
            Add(p, new StrPostureRow(p.Content, Controls, hub, true));
        }

        private void BuildSitrep(AvFlow p)
        {
            p.Section(AvIcon.Scale, "FORCE BALANCE", "OWN : HOSTILE, TRK = TRACKED ONLY");
            Add(p, new StrBalanceBar(p.Content, Controls, hub, vm => vm.Sectors));
            Add(p, new StrBalanceBar(p.Content, Controls, hub, vm => vm.Air));
            Add(p, new StrBalanceBar(p.Content, Controls, hub, vm => vm.Ground));
            Add(p, new StrBalanceBar(p.Content, Controls, hub, vm => vm.Airbases));
            p.Section(AvIcon.Antenna, "SENSORS AND FRONT", "WHAT WE KNOW, NOT WHAT EXISTS");
            for (int i = 0; i < sensors.Length; i++) sensors[i] = p.Add(new AvRow(p.Content));
            p.Section(AvIcon.AlertTriangle, "ALERT LEVEL", "FROM THE HOST'S OWN COUNTS");
            Add(p, new StrAlertRow(p.Content, Controls, hub));
            sortieSection = p.Section(AvIcon.Plane, "AI SORTIE BOARD", "");
            Add(p, new StrSortieBoard(p.Content, Controls, hub));
        }

        private void BuildInfo(AvFlow p)
        {
            p.Section(AvIcon.UsersGroup, "CHAIN OF COMMAND", "READ ONLY · THE STAFF RUNS ITSELF");
            Add(p, new StrChainPart(p.Content, Controls, hub));
            p.Section(AvIcon.ListDetails, "STAFF LOG", "NEWEST FIRST · ROW NUMBERS, NOT CLOCK TIMES");
            Add(p, new StrTeletype(p.Content, Controls, hub));
            logNote = p.Add(new StrNote(p.Content, AvIcon.ListDetails));
            p.Section(AvIcon.Activity, "MISC", "REPORT AGES · THE HOST · HOST SWITCHES");
            for (int i = 0; i < misc.Length; i++) misc[i] = p.Add(new AvRow(p.Content));
        }

        // ---- Binding ---------------------------------------------------------------------

        /// <summary>Binds the hub's current model now (the harness and the automation use it).</summary>
        internal void BindNow()
        {
            if (console == null || hub == null) return;
            StrViewModel vm = hub.Current;
            Controls.Clear();
            RegisterTabs();
            for (int i = 0; i < parts.Count; i++) parts[i].Bind(vm);
            BindSections(vm);
            BindTiles(vm);
            BindSensors(vm);
            BindLog(vm);
            BindMisc(vm);
            console.Footer.Set(vm.FooterStatus + "\n" + vm.FooterNote, vm.Stale ? AvState.Caution : AvState.Inert);
            boundModel = vm;
            boundRevision = hub.Revision;
        }

        private void RegisterTabs()
        {
            for (int i = 0; i < 3; i++)
            {
                int page = i;
                Controls.Register(StrControlIds.Tab(page), null, () => { console.SetPage(page); return new StrAct(StrWhy.None, ""); });
            }
        }

        // One of the two lane headings shows, and only when some lane does.
        private void BindSections(StrViewModel vm)
        {
            StrPagePlan plan = StrPagePlan.For(vm.Mode);
            int shown = 0;
            if (plan.Placeholders) shown = 2;
            else if (plan.Lanes)
                for (int i = 0; i < vm.FrontCount; i++) if (StrPagePlan.ShowsLane(vm.Mode, vm.Fronts[i])) shown++;
            bool other = shown > 0 && plan.LaneTitle == "OTHER PLATFORMS";
            otherSection.SetShown(other);
            allSection.SetShown(shown > 0 && !other);
            string laneCaption = StrPanelText.LaneCaption(vm);
            otherSection.SetCaption(laneCaption);
            allSection.SetCaption(laneCaption);
            sortieSection.SetCaption(vm.SortieBoardCaption);
        }

        private void BindTiles(StrViewModel vm)
        {
            if (metrics == null || metrics.Length < 4) return;
            for (int i = 0; i < 4; i++)
            {
                StrTile t = vm.Tiles[i];
                if (t.Key == null) continue;
                metrics[i].Set(t.Value, t.Caption, float.IsNaN(t.Bar) ? 0f : t.Bar, t.State);
                StrTips.Metric(metrics[i], t.Help);
            }
        }

        private void BindSensors(StrViewModel vm)
        {
            string front = vm.FrontText ?? StrWords.Dash;
            string frontValue = StrPanelText.FrontLength(front), frontRest = front.Length > frontValue.Length ? front.Substring(frontValue.Length + StrWords.Dot.Length) : "";
            sensors[0].Set("RADARS", "FRIENDLY RADARS ONLINE", vm.RadarsText, vm.RadarsText == StrWords.Dash ? AvState.Inert : AvState.Info);
            sensors[1].Set("KNOWN AD", "ENEMY AIR DEFENCE YOUR SIDE KNOWS ABOUT. INTEL MAY BE INCOMPLETE", vm.KnownAdText, vm.KnownAdText == "NOT AVAILABLE" ? AvState.Inert : AvState.Info);
            sensors[2].Set("FRONT", frontRest.Length > 0 ? "THE FRONT LINE" + StrWords.Dot + frontRest : "THE FRONT LINE", frontValue, frontValue == StrWords.Dash ? AvState.Inert : AvState.Info);
            sensors[3].Set("CONTESTED BASES", "AIRBASES UNDER CAPTURE", vm.ContestedBasesText, vm.ContestedBasesText == StrWords.Dash ? AvState.Inert : vm.ContestedBasesText == "0" ? AvState.Info : AvState.Caution);
        }

        private void BindMisc(StrViewModel vm)
        {
            for (int i = 0; i < misc.Length; i++)
            {
                StrMiscLine line = vm.Misc[i];
                if (line.Key != null) misc[i].Set(line.Key, line.Text, line.Value, line.State);
            }
        }

        private void BindLog(StrViewModel vm)
        {
            bool empty = vm.LogCount == 0;
            logNote.SetShown(empty);
            if (!empty) return;
            StrPanelText.LogNote(vm, out string title, out string detail);
            logNote.Set(title, detail);
        }
    }
}
