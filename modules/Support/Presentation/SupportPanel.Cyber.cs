using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using NOAvionics;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// CYBER — the faction's cyber network. STATUS is the watch floor: the INFOCON rail and advice,
    /// the console door, the node mesh, the resource meters with the live breach or lease,
    /// six status chips and the event tape. ACTIONS holds the map abilities the network has
    /// earned. Breach and incident answers live in the full-screen console. This file is the
    /// shell plus STATUS and the work that keeps running with the page closed: the event log
    /// and the alarm. Every figure comes from the network model; every control is a host
    /// request. With no network the page is its one card and the log.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const string CyberStatusHelp = "STATUS: the watch floor. INFOCON and the adversary phase, the node mesh (click a node to open the console on it), resources, incidents and the event log.";
        private const string CyberActionsHelp = "ACTIONS: the map effects your network has earned. Analyze, inject and commit three services. Quality opens deeper INTRUSION / EW tiers; arm one earned effect and right-click the map.";

        private static readonly string[] CyberTileKeys = { "INTRUSION", "JAMMING", "TRACE" };

        /// <summary>Hosts the <see cref="Views.MiniNetmap"/> board inside a kit v2 part.</summary>
        private sealed class NetmapPart : AvPart
        {
            private const float H = 156f;
            private readonly Views.MiniNetmap map = new Views.MiniNetmap();
            private readonly System.Action<int> onClick;
            private bool built;

            public NetmapPart(RectTransform parent, System.Action<int> onClick)
            {
                Rect = AvLay.Child(parent, "Netmap");
                this.onClick = onClick;
            }

            public override float Measure(float width) => H;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                if (built) return;
                built = true;
                map.Build(Rect, new UnityEngine.Rect(0f, 0f, s.W, s.H), onClick);
            }

            public void Paint(CyberNetwork network, double now, int selected)
            {
                if (built) map.Paint(network, now, selected);
            }
        }

        private OpsSubPage cyberPage;
        private InfoconHero cyberHero;
        private AvSection cyberMeshSection;
        private AvButtons cyberButtons;
        private AvControl openConsoleButton;
        private NetmapPart cyberMap;
        private AvGauge cyberComputing, cyberIntel, cyberNodes;
        private AvHazardBar cyberLease;
        private AvLineChart cyberChart;
        private AvChip[] cyberTiles;
        private LogTape cyberLog;
        private readonly string[] cyberLoop = new string[LoopLines];
        private int cyberLoggedSerial = -1;
        private int selectedSite = -1;
        private CyberAlarm alarm;

        private void ResetCyberPage()
        {
            cyberPage = null;
            cyberHero = null;
            cyberMeshSection = null;
            cyberButtons = null;
            openConsoleButton = null;
            cyberMap = null;
            cyberComputing = null;
            cyberIntel = null;
            cyberNodes = null;
            cyberChart = null;
            cyberLease = null;
            cyberTiles = null;
            cyberLog = null;
            for (int i = 0; i < cyberLoop.Length; i++) cyberLoop[i] = null;
            cyberLoggedSerial = -1;
            selectedSite = -1;
            alarm?.Dispose();
            alarm = null;
            ResetCyberOpsPage();
        }

        // ---- Build -------------------------------------------------------------------------------

        private void BuildCyberPage(AvFlow page)
        {
            cyberPage = page.Add(new OpsSubPage(page.Content, page.Ticker, page.Inner, AvIcon.ShieldLock, "CYBER",
                sub => nextRefresh = 0f, CyberStatusHelp, CyberActionsHelp));
            BuildCyberStatusPage(cyberPage.Status);
            BuildCyberOpsPage(cyberPage.Actions);
            alarm = new CyberAlarm(screenRoot != null ? screenRoot.transform : transform);
            CyberLog("WATCH FLOOR ONLINE · " + CyberWords.NetworkName + " STANDING BY");
        }

        /// <summary>The board's node click: remember the selected site (S3 restores the console).</summary>
        private void SelectSite(int slot)
        {
            selectedSite = slot;
            nextRefresh = 0f;
        }

        private void BuildCyberStatusPage(AvFlow status)
        {
            cyberHero = status.Add(new InfoconHero(status.Content));
            cyberButtons = status.Buttons(new AvControl.Spec("OPEN CONSOLE", OpenConsole, AvButtonStyle.Primary, AvIcon.Maximize));
            openConsoleButton = cyberButtons.Controls[0];
            openConsoleButton.Help = "The network-ops terminal: breach locations, answer incidents, buy network upgrades.";

            cyberMeshSection = status.Section(AvIcon.Map2, "NODE MESH", "");
            cyberMap = status.Add(new NetmapPart(status.Content, slot =>
            {
                SelectSite(slot);
                OpenConsole();
            }));

            cyberComputing = new AvGauge(status.Content, "COMPUTING", AvGaugeShape.Segments, 64f);
            cyberIntel = new AvGauge(status.Content, "INTEL", AvGaugeShape.Segments, 64f);
            cyberNodes = new AvGauge(status.Content, "NODES", AvGaugeShape.Segments, 64f);
            status.Row(cyberComputing, cyberIntel, cyberNodes);
            cyberLease = status.Add(new AvHazardBar(status.Content, "OP"));
            cyberLease.Help = "The live operation: an open breach (trace risk), the one-use access lease that follows it, or a payload waiting for your choice.";
            cyberTiles = BuildChipRow(status, CyberTileKeys, 3);

            cyberLog = status.Add(new LogTape(status.Content, 5, true));
            cyberChart = AddTrend(status);
        }

        private void SetCyberStatusParts(bool live)
        {
            cyberMeshSection.SetShown(live);
            cyberButtons.SetShown(live);
            cyberMap.SetShown(live);
            cyberComputing.SetShown(live);
            cyberIntel.SetShown(live);
            cyberLease.SetShown(live);
            cyberNodes.SetShown(live);
            if (!live) cyberLease.SetShown(false);
        }

        // ---- Console and loop --------------------------------------------------------------------

        private void OpenConsole()
        {
            if (FullscreenInput.AnyOpen && !Window.OpsWindow.IsOpen) return;
            OpenRoom(CyberRoom(), selectedSite, openConsoleButton != null ? openConsoleButton.Rect : null);
            CyberLog("CONSOLE - " + CyberWords.NetworkName + " ON THE BIG BOARD");
        }

        /// <summary>Work that must not wait for the CYBER page: the loop and the alarm.</summary>
        private void TickCyberBackground()
        {
            if (cyberPage == null) return;
            CyberNetwork network = support.LocalCyber;
            if (network == null) return;
            if (cyberLoggedSerial < 0)
            {
                cyberLoggedSerial = network.NoticeSerial;
                return;
            }
            int fresh = Mathf.Min(network.NoticeSerial - cyberLoggedSerial, network.NoticeCount);
            if (network.NoticeSerial < cyberLoggedSerial) fresh = 0;
            cyberLoggedSerial = network.NoticeSerial;
            bool alarmed = false;
            for (int age = fresh - 1; age >= 0; age--)
            {
                CyberNotice notice = network.NoticeKind(age);
                string line = CyberWords.Notice(notice, CyberWords.Callsign(network, network.NoticeSite(age)),
                    support.CyberOriginName(network.NoticeOrigin(age)), network.NoticeOrigin(age));
                if (line == null) continue;
                if (CyberWords.Klaxon(notice)) alarmed = true;
                CyberLog(CyberWords.Alarm(notice) ? "!! " + line : line);
            }
            if (alarmed) alarm?.Sound();
        }

        private void CyberLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            for (int i = cyberLoop.Length - 1; i > 0; i--) cyberLoop[i] = cyberLoop[i - 1];
            cyberLoop[0] = TheaterGrid.Elapsed(support != null ? support.OrbitNow : 0.0) + "  " + line;
        }

        // ---- Refresh ---------------------------------------------------------------------------------

        private void RefreshCyber(bool bypass)
        {
            if (cyberPage == null) return;
            CyberNetwork network = support.LocalCyber;
            double now = support.OrbitNow;
            if (cyberPage.Sub == 0) RefreshCyberStatusPage(network, now);
            else RefreshCyberOpsPage(bypass, network, now);
        }

        /// <summary>The words a level answers to on the rail (presentation only; the level is the model's).</summary>
        private static string InfoconWord(int level)
        {
            switch (level)
            {
                case 5: return "NORMAL";
                case 4: return "VIGILANT";
                case 3: return "ENHANCED";
                case 2: return "HIGH ALERT";
                default: return "MAXIMUM";
            }
        }

        private void RefreshCyberStatusPage(CyberNetwork network, double now)
        {
            if (cyberHero == null) return;
            bool built = network != null && network.HasCommand;
            bool enabled = support.CyberEnabled;
            bool live = built && enabled;
            SetCyberStatusParts(live);
            cyberLog.Write(cyberLoop);

            PaintCyberHeadline(network, enabled, built, now);
            if (!enabled)
            {
                cyberHero.Set(0, "CYBER OFFLINE", "HOST OFF", "DISABLED IN HOST CONFIG", "The host has switched cyber operations off.", AvState.Inert);
                return;
            }
            if (!built)
            {
                cyberHero.Set(0, "NO NETWORK", "", "HOLD AN AIRBASE", CyberWords.Advice(network, now, out _, out _), AvState.Inert);
                return;
            }

            CyberStats stats = network.Stats();
            int infocon = Mathf.Clamp(network.Infocon, 1, 5);
            AvState tone = infocon >= 5 ? AvState.Ready : infocon >= 3 ? AvState.Caution : AvState.Danger;
            string phase = (network.CommandCompromised ? "C2 BREACHED" : CyberWords.Phase(network.Phase)) + " · HEAT " + Mathf.RoundToInt(network.Heat);
            cyberHero.Set(infocon, CyberWords.Infocon(infocon), InfoconWord(infocon), phase,
                CyberWords.Advice(network, now, out _, out _), tone);

            int nodes = 0, reach = 0;
            for (int i = 0; i < CyberNetwork.SlotCount; i++)
            {
                if (!network.Exists(i)) continue;
                nodes++;
                CyberNode node = network.Node(i);
                if (!(node.Static || node.Hacked) && network.CheckBreach(i, now) == BreachDenial.None) reach++;
            }
            cyberMap.Paint(network, now, selectedSite);
            cyberNodes.Set(nodes > 0 ? stats.Hacked / (float)nodes : 0f, stats.Hacked + "/" + nodes,
                stats.Hacked > 0 ? AvState.Ready : AvState.Inert);
            string nodeHelp = "NODES: " + stats.Hacked + " of " + nodes + " nodes are yours; " + reach +
                " more are inside breach reach. Click one on the mesh to open the console on it.";
            if (cyberNodes.Help != nodeHelp) cyberNodes.Help = nodeHelp;

            float computing = network.Computing, computingCap = Mathf.Max(1f, network.ComputingCapacity());
            float intel = network.Intel, intelCap = Mathf.Max(1f, network.IntelCapacity());
            AvState computeState = computing < computingCap * 0.25f ? AvState.Caution : AvState.Info;
            cyberComputing.Set(computing / computingCap, AvStates.Glyph(computeState) + Mathf.FloorToInt(computing) + "/" + Mathf.RoundToInt(computingCap), computeState);
            cyberIntel.Set(intel / intelCap, Mathf.FloorToInt(intel) + "/" + Mathf.RoundToInt(intelCap), AvState.Info);
            string computeHelp = "COMPUTING: " + Mathf.FloorToInt(computing) + " of " + Mathf.RoundToInt(computingCap) +
                " spendable on active intrusion work and defence. Online home infrastructure refills it.";
            if (cyberComputing.Help != computeHelp) cyberComputing.Help = computeHelp;
            string intelHelp = "INTEL: " + Mathf.FloorToInt(intel) + " of " + Mathf.RoundToInt(intelCap) +
                " earned from completed intrusion packages. Map effects are paid in intel.";
            if (cyberIntel.Help != intelHelp) cyberIntel.Help = intelHelp;
            PaintTrend(cyberChart, computingTrend, "");

            RefreshCyberLease(network, now);
            RefreshCyberTiles(network, stats, now);
        }

        /// <summary>The one live-operation meter: an open breach, the access lease, or a payload waiting for a choice.</summary>
        private void RefreshCyberLease(CyberNetwork network, double now)
        {
            if (network.BreachActive)
            {
                float trace = network.BreachTrace;
                cyberLease.Set(trace, "WORK " + network.WorkProgress + "/3 · Q" + network.WorkQuality + " · " + AvNum.Percent(trace) + " TRACE",
                    trace >= 0.8f ? AvState.Danger : trace >= 0.5f ? AvState.Caution : AvState.Info);
                cyberLease.SetShown(true);
            }
            else if (network.AccessRemaining(now) > 0f)
            {
                float left = network.AccessRemaining(now);
                cyberLease.Set(left / CyberLocations.AccessSeconds, "ACCESS " + CyberWords.Seconds(left) + " · ONE USE", AvState.Ready);
                cyberLease.SetShown(true);
            }
            else if (network.BreachAwaitingChoice)
            {
                cyberLease.Set(1f, "PAYLOAD · SELECT", AvState.Caution);
                cyberLease.SetShown(true);
            }
            else cyberLease.SetShown(false);
        }

        private void RefreshCyberTiles(CyberNetwork network, in CyberStats stats, double now)
        {
            int intrusions = network.ActiveIncidents(IncidentKind.Intrusion);
            SetChip(cyberTiles[0], CyberTileKeys[0], network.CommandCompromised ? "C2 BREACH" : intrusions > 0 ? intrusions + " ACTIVE" : "CLEAR",
                network.CommandCompromised || intrusions > 0 ? AvState.Danger : AvState.Ready);
            int raids = network.ActiveIncidents(IncidentKind.Raid);
            SetChip(cyberTiles[1], CyberTileKeys[1], raids > 0 ? "RAID" : "CLEAR", raids > 0 ? AvState.Caution : AvState.Ready);
            SetChip(cyberTiles[2], CyberTileKeys[2], network.AnyFoothold(now) ? "TRACEABLE" : "NONE",
                network.AnyFoothold(now) ? AvState.Caution : AvState.Inert);
        }

        /// <summary>The mode word in the title slot: only what the hero card below does not already say.</summary>
        private void PaintCyberHeadline(CyberNetwork network, bool enabled, bool built, double now)
        {
            if (!enabled) { cyberPage.SetHeadline("OFFLINE", AvState.Inert); return; }
            if (!built) { cyberPage.SetHeadline("NO NETWORK", AvState.Inert); return; }
            float access = network.AccessRemaining(now);
            if (network.CommandCompromised) cyberPage.SetHeadline("C2 BREACHED", AvState.Danger);
            else if (network.BreachActive) cyberPage.SetHeadline("BREACH RUNNING", AvState.Caution);
            else if (access > 0f) cyberPage.SetHeadline("ACCESS " + CyberWords.Seconds(access), AvState.Ready);
            else cyberPage.SetHeadline("NETWORK UP", AvState.Ready);
        }
    }
}
