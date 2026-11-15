using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Services;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Domain;
using NOAvionics;
using TMPro;
using UnityEngine;

namespace BoscaliSummer.Modules.Command.Presentation
{
    /// <summary>
    /// The STR bezel: the Station Board (design D1). Tabs BOARD and STATUS under four pinned tiles; a function of <see cref="StrViewModel"/>, bound to the
    /// hub's model each time its revision changes. No part reads a service, a clock or a status string, and a press goes to the hub through
    /// <see cref="Controls"/>, the one command path. It only exists beside the maximised relief map.
    /// </summary>
    internal sealed partial class StrMfdPanel : MonoBehaviour, ISceneService
    {
        private const float Width = AvTokens.PanelWidth;
        private const int PageBoard = 0, PageStatus = 1, PageCount = 2;
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
        // NATION: the desk shows the nation layer while the service says it is active; the key moves when a shown value does (whole seconds) and forces one rebind.
        private int nationKey;

        // The parts of each page: only the open page binds; a page that missed a revision is stale until it is shown (OnPageChanged).
        private readonly List<StrPart>[] pageParts = { new List<StrPart>(24), new List<StrPart>(4) };
        private readonly string[] tabIds = new string[PageCount];
        private readonly Func<StrAct>[] tabActs = new Func<StrAct>[PageCount];
        private int staleMask;
        private string footerStatus, footerNote;
        private StrViewModel boundModel;
        private int boundRevision = -1;
        private float nextLog;

        // BOARD
        private AvSection lanesSection;
        private readonly StrFrontLane[] lanes = new StrFrontLane[MaxLanes];
        // STATUS
        private readonly AvRow[] sensors = new AvRow[3];

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
            if (installer.Root != null) installer.Root.SetActive(false);
            installer.Reset();
            ResetContent();
        }

        private void ResetContent()
        {
            console = null;
            nationKey = 0;
            StrNation.View = null;
            for (int i = 0; i < pageParts.Length; i++) pageParts[i].Clear();
            staleMask = 0;
            footerStatus = footerNote = null;
            Controls.Clear();
            boundModel = null;
            boundRevision = -1;
            lanesSection = null;
            for (int i = 0; i < lanes.Length; i++) lanes[i] = null;
            for (int i = 0; i < sensors.Length; i++) sensors[i] = null;
            nextLog = 0f;
        }

        private void OnEnable() => Active = this;

        private void OnDisable()
        {
            ResetForScene();
            if (Active == this) Active = null;
        }

        private void OnDestroy() => OnDisable();

        /// <summary>Automation: the bezel press plus a page switch, the way a player opens the tab (map already maximised).</summary>
        internal void OpenForAutomation(int page)
        {
            if (screen != null && !screen.isActive && installer.Bezel != null) installer.Bezel.onClick.Invoke();
            if (console != null) console.SetPage(Mathf.Clamp(page, 0, PageCount - 1));
            boundRevision = -1;
        }

        private void Update()
        {
            if (installer.Failed || settings == null || !settings.Enabled.Value) return;
            if (!installer.Tick()) return;
            // A closed screen costs nothing.
            if (!screen.isActive) return;
            TickNation();
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
                Bind(false);
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
            console = AvConsole.Build(rootRect, "STR", "THEATER", PageCount, Width, height);
            TabHelp.Apply(console.Tabs(
                (AvIcon.Stack2, "BOARD"),
                (AvIcon.Radar2, "STATUS")),
                "The live operation, the staff's proposals, the platforms, the war plan and the force balance.",
                "Sensors, the alert level and your chain of command.");
            for (int i = 0; i < PageCount; i++)
            {
                int page = i;
                tabIds[i] = StrControlIds.Tab(page);
                tabActs[i] = () => { console.SetPage(page); return new StrAct(StrWhy.None, ""); };
            }
            BoscaliSummer.Core.Ui.CountryStrip.Mount(console);

            BuildBoard(console.Page(PageBoard));
            BuildStatus(console.Page(PageStatus));
            console.Finish();
            console.SetPage(PageBoard);
            console.PageChanged += OnPageChanged;
            return console.Root;
        }

        private T Add<T>(int page, AvFlow flow, T part) where T : StrPart
        {
            flow.Add(part);
            pageParts[page].Add(part);
            return part;
        }

        private void BuildBoard(AvFlow p)
        {
            Add(PageBoard, p, new StrNationTicker(p.Content, Controls, hub));
            Add(PageBoard, p, new StrBannerPart(p.Content, Controls, hub));
            // WAR PLAN: with the nation layer on, the plan row (it keeps its lock) stands in for the posture row.
            Add(PageBoard, p, new StrPolicyRow(p.Content, Controls, hub, StrPolicyRow.Kind.Plan));
            Add(PageBoard, p, new StrHeroLane(p.Content, Controls, hub));
            for (int i = 0; i < StrViewModel.MaxProposals; i++) Add(PageBoard, p, new StrProposalTicket(p.Content, Controls, hub, i));
            Add(PageBoard, p, new StrPostureRow(p.Content, Controls, hub));
            lanesSection = p.Section(AvIcon.Map2, "PLATFORMS", StrPanelText.NotchCaption);
            for (int i = 0; i < MaxLanes; i++) lanes[i] = Add(PageBoard, p, new StrFrontLane(p.Content, Controls, hub, i));
            p.Section(AvIcon.Scale, "FORCE BALANCE", "OWN : HOSTILE, TRACKED ONLY");
            Add(PageBoard, p, new StrBalanceBar(p.Content, Controls, hub, vm => vm.Sectors, true));
            Add(PageBoard, p, new StrBalanceBar(p.Content, Controls, hub, vm => vm.Air, false));
            Add(PageBoard, p, new StrBalanceBar(p.Content, Controls, hub, vm => vm.Ground, false));
            Add(PageBoard, p, new StrBalanceBar(p.Content, Controls, hub, vm => vm.Airbases, true));
            BuildOrders(p);
        }

        private void BuildStatus(AvFlow p)
        {
            p.Section(AvIcon.Antenna, "SENSORS");
            for (int i = 0; i < sensors.Length; i++) sensors[i] = p.Add(new AvRow(p.Content));
            p.Section(AvIcon.AlertTriangle, "ALERT LEVEL");
            Add(PageStatus, p, new StrAlertRow(p.Content, Controls, hub));
            p.Section(AvIcon.UsersGroup, "CHAIN OF COMMAND", "READ ONLY");
            Add(PageStatus, p, new StrChainPart(p.Content, Controls, hub));
        }

        // ---- Binding ---------------------------------------------------------------------

        /// <summary>Binds the hub's current model to every page now (the harness and the automation use it; a press by id needs the ids of both pages).</summary>
        internal void BindNow() => Bind(true);

        // The open page only; the other one is stale until it is shown (OnPageChanged).
        private void Bind(bool all)
        {
            if (console == null || hub == null) return;
            StrViewModel vm = hub.Current;
            Controls.Clear();
            StrNation.Fill();
            for (int i = 0; i < PageCount; i++) Controls.Register(tabIds[i], null, tabActs[i]);
            int open = console.CurrentPage;
            staleMask = 0;
            for (int page = 0; page < PageCount; page++)
            {
                if (!all && page != open) { staleMask |= 1 << page; continue; }
                List<StrPart> list = pageParts[page];
                for (int i = 0; i < list.Count; i++) list[i].Bind(vm);
            }
            if (all || open == PageBoard) { BindLanesSection(vm); BindOrders(); }
            if (all || open == PageStatus) BindSensors(vm);
            // The standing flavour line only repeats the hover help; a live result (a lock, a pick, a prompt) still shows under the status.
            string note = ReferenceEquals(vm.FooterNote, StrWords.Flavour(vm.Mode)) ? "" : vm.FooterNote;
            if (!string.Equals(vm.FooterStatus, footerStatus) || !string.Equals(note, footerNote))
            {
                footerStatus = vm.FooterStatus; footerNote = note;
                console.Footer.Set(note.Length > 0 ? footerStatus + "\n" + note : footerStatus, vm.Stale ? AvState.Caution : AvState.Inert);
            }
            boundModel = vm;
            boundRevision = hub.Revision;
        }

        // A page that was stale catches up the moment it opens.
        private void OnPageChanged(int page)
        {
            if ((staleMask & (1 << page)) == 0) return;
            boundRevision = -1;
            TickBind();
        }

        // The lane heading shows only when some lane does.
        private void BindLanesSection(StrViewModel vm)
        {
            StrPagePlan plan = StrPagePlan.For(vm.Mode);
            bool any = false;
            if (plan.Lanes)
                for (int i = 0; i < vm.FrontCount && !any; i++) any = StrPagePlan.ShowsLane(vm.Mode, vm.Fronts[i]);
            lanesSection.SetShown(any);
            if (any) lanesSection.SetCaption(StrPanelText.LaneCaption(vm));
        }

        // ---- Nation ----------------------------------------------------------------------

        private float nextNation;

        private void TickNation()
        {
            // 4 Hz is plenty for a countdown in whole seconds; the keys below walk the plant and decision lists.
            if (Time.unscaledTime < nextNation) return;
            nextNation = Time.unscaledTime + 0.25f;
            int key = unchecked(StrNation.Refresh() * 31 + OrdersKey(StrNation.View));   // ORDERS IN FORCE rebinds with the nation
            if (key == nationKey) return;
            nationKey = key;
            // The parts read the nation through StrNation; only the BOARD shows it, so on STATUS the board is just marked stale.
            if (console != null && console.CurrentPage == PageBoard) boundRevision = -1;
            else staleMask |= 1 << PageBoard;
        }

        private void BindSensors(StrViewModel vm)
        {
            sensors[0].Set("RADARS", "FRIENDLY RADARS ONLINE", vm.RadarsText, vm.RadarsText == StrWords.Dash ? AvState.Inert : AvState.Info);
            sensors[1].Set("KNOWN AD", "ENEMY AIR DEFENCE YOUR SIDE KNOWS ABOUT. INTEL MAY BE INCOMPLETE", vm.KnownAdText, vm.KnownAdText == "NOT AVAILABLE" ? AvState.Inert : AvState.Info);
            sensors[2].Set("CONTESTED BASES", "AIRBASES UNDER CAPTURE", vm.ContestedBasesText, vm.ContestedBasesText == StrWords.Dash ? AvState.Inert : vm.ContestedBasesText == "0" ? AvState.Info : AvState.Caution);
        }
    }
}
