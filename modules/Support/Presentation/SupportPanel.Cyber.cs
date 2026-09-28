using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// CYBER — the faction's cyber network. STATUS is the watch floor: INFOCON, resources, the
    /// node mesh, the live breach and the event log. ACTIONS holds the map abilities the network
    /// has earned. All hacking lives in the full-screen console. This file is the shell plus
    /// STATUS and the work that keeps running with the page closed: the event log and the alarm.
    /// Every figure comes from the network model; every control is a host request.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const string CyberStatusHelp = "Watch floor: INFOCON, resources, the node mesh and the voice loop.";
        private const string CyberActionsHelp = "The map abilities the network has earned.";

        private static readonly string[] CyberTileKeys =
            { "COMPUTING", "INTEL", "NODES", "ACCESS", "INTRUSION", "JAMMING", "TRACE LEAD", "ADVERSARY" };

        /// <summary>Hosts the other agent's <see cref="Views.MiniNetmap"/> board inside a kit v2 part
        /// (spec §9.2: genuinely-data visuals stay as they are, hosted in a kit v2 wrapper).</summary>
        private sealed class NetmapPart : AvPart
        {
            private const float H = 160f;
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
        private AvSection cyberSection;
        private HintLine cyberHint;
        private AvControl openConsoleButton;
        private NoteText cyberAdvice;
        private AvGauge cyberLadder;
        private AvRow cyberComputingRow, cyberIntelRow;
        private NetmapPart cyberMap;
        private AvChip[] cyberTiles;
        private LogLines cyberLog;
        private readonly string[] cyberLoop = new string[LoopLines];
        private readonly string[] cyberLoopMono = new string[LoopLines];
        private int cyberLoggedSerial = -1;
        private int selectedSite = -1;
        private CyberAlarm alarm;

        private void ResetCyberPage()
        {
            cyberPage = null;
            cyberSection = null;
            cyberHint = null;
            openConsoleButton = null;
            cyberAdvice = null;
            cyberLadder = null;
            cyberComputingRow = cyberIntelRow = null;
            cyberMap = null;
            cyberTiles = null;
            cyberLog = null;
            for (int i = 0; i < cyberLoop.Length; i++) cyberLoop[i] = cyberLoopMono[i] = null;
            cyberLoggedSerial = -1;
            selectedSite = -1;
            alarm?.Dispose();
            alarm = null;
            ResetCyberOpsPage();
        }

        // ---- Build -------------------------------------------------------------------------------

        private void BuildCyberPage(AvFlow page)
        {
            cyberPage = page.Add(new OpsSubPage(page.Content, page.Ticker, page.Inner, AvIcon.ShieldLock, "CYBER", sub =>
            {
                nextRefresh = 0f;
                shell.Page(TabCyber).RequestRelayout();
            }, CyberStatusHelp, CyberActionsHelp));
            BuildCyberStatusPage(cyberPage.Status);
            BuildCyberOpsPage(cyberPage.Actions);
            alarm = new CyberAlarm(screenRoot != null ? screenRoot.transform : transform);
            CyberLog("WATCH FLOOR ONLINE · " + CyberWords.NetworkName + " STANDING BY");
        }

        /// <summary>The board's node click: remember it and open the console on it.</summary>
        private void SelectSite(int slot)
        {
            selectedSite = slot;
            nextRefresh = 0f;
        }

        private void BuildCyberStatusPage(AvFlow status)
        {
            cyberSection = status.Section(AvIcon.ShieldLock, CyberWords.NetworkName + " · WATCH FLOOR", "");
            cyberHint = status.Add(new HintLine(status.Content));
            AvButtons buttons = status.Buttons(new AvControl.Spec("OPEN CONSOLE", OpenConsole, AvButtonStyle.Primary, AvIcon.Typography));
            openConsoleButton = buttons.Controls[0];
            openConsoleButton.Help = "The network-ops terminal: breach locations, answer incidents, buy network upgrades.";
            cyberAdvice = status.Add(new NoteText(status.Content));

            status.Section(AvIcon.Gauge, "INFOCON · RESOURCES");
            cyberLadder = status.Add(new AvGauge(status.Content, "INFOCON", AvGaugeShape.Segments, 64f));
            cyberComputingRow = status.Add(new AvRow(status.Content));
            cyberIntelRow = status.Add(new AvRow(status.Content));

            status.Section(AvIcon.Map2, "NODE MESH");
            cyberMap = status.Add(new NetmapPart(status.Content, slot =>
            {
                SelectSite(slot);
                OpenConsole();
            }));

            status.Section(AvIcon.Activity, "NETWORK HEALTH · RESOURCES · HOLDINGS · THREATS");
            cyberTiles = BuildChipRow(status, CyberTileKeys);

            status.Section(AvIcon.ListDetails, "EVENT LOG · NEWEST FIRST");
            cyberLog = status.Add(new LogLines(status.Content, LoopLines));
        }

        // ---- Console and loop --------------------------------------------------------------------

        private void OpenConsole()
        {
            if (FullscreenInput.AnyOpen && !Window.OpsWindow.IsOpen) return;
            OpenRoom(CyberRoom(), selectedSite, openConsoleButton != null ? openConsoleButton.Rect : null);
            CyberLog("CONSOLE · " + CyberWords.NetworkName + " ON THE BIG BOARD");
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

        private void RefreshCyberStatusPage(CyberNetwork network, double now)
        {
            if (cyberHint == null) return;
            bool built = network != null && network.HasCommand;
            CyberStats stats = network != null ? network.Stats() : default;

            string word, phase, advice;
            AvState tone;
            if (!support.CyberEnabled)
            {
                word = "OFFLINE"; phase = "DISABLED IN HOST CONFIG";
                advice = "The host has switched cyber operations off.";
                tone = AvState.Inert;
            }
            else if (!built)
            {
                word = "NO NETWORK"; phase = "HOLD AN AIRBASE";
                advice = CyberWords.Advice(network, now, out _, out _);
                tone = AvState.Inert;
            }
            else
            {
                int infocon = network.Infocon;
                word = CyberWords.Infocon(infocon);
                phase = network.CommandCompromised ? "C2 BREACHED" : CyberWords.Phase(network.Phase) + " · HEAT " + Mathf.RoundToInt(network.Heat);
                advice = CyberWords.Advice(network, now, out _, out _);
                tone = infocon >= 5 ? AvState.Ready : infocon >= 3 ? AvState.Caution : AvState.Danger;
            }
            cyberHint.Set(word + " · " + phase, tone);
            cyberSection.SetCaption(AvStates.Glyph(tone) + word);
            cyberAdvice.Set(advice, tone);

            int infoconValue = built ? network.Infocon : 0;
            cyberLadder.Set(infoconValue / 5f, infoconValue.ToString(System.Globalization.CultureInfo.InvariantCulture), tone);

            float computing = network != null ? network.Computing : 0f;
            float computingCap = Mathf.Max(1f, network != null ? network.ComputingCapacity() : 1f);
            float intel = network != null ? network.Intel : 0f;
            float intelCap = Mathf.Max(1f, network != null ? network.IntelCapacity() : 1f);
            string breach = "";
            if (network != null && network.BreachActive)
                breach = " · BREACH " + CyberWords.PhaseOf(network.BreachPhase) + " " + AvNum.Percent(network.BreachTrace);
            else if (network != null && network.AccessRemaining(now) > 0f)
                breach = " · ACCESS " + CyberWords.Seconds(network.AccessRemaining(now)) + " · ONE EFFECT";
            else if (network != null && network.BreachAwaitingChoice)
                breach = " · PAYLOAD SELECT";
            cyberComputingRow.Set("COMPUTING", "+" + AvNum.Fixed(network != null ? network.ComputingIncome() : 0f, 1) + "/S" + breach,
                Mathf.FloorToInt(computing) + "/" + Mathf.RoundToInt(computingCap),
                computing < computingCap * 0.25f ? AvState.Caution : AvState.Info);
            cyberIntelRow.Set("INTEL", "+" + AvNum.Fixed(network != null ? network.IntelIncome() : 0f, 1) + "/S",
                Mathf.FloorToInt(intel) + "/" + Mathf.RoundToInt(intelCap), AvState.Info);

            RefreshCyberTiles(network, stats, now, built);
            cyberMap.Paint(network, now, selectedSite);

            for (int i = 0; i < cyberLoopMono.Length; i++)
                cyberLoopMono[i] = cyberLoop[i] != null ? Mono("$ " + cyberLoop[i]) : null;
            cyberLog.Write(cyberLoopMono);
        }

        private void RefreshCyberTiles(CyberNetwork network, in CyberStats stats, double now, bool built)
        {
            if (!built)
            {
                for (int i = 0; i < cyberTiles.Length; i++) SetChip(cyberTiles[i], CyberTileKeys[i], "—", AvState.Inert);
                return;
            }
            float computing = network.ComputingCapacity() > 0f ? network.Computing / network.ComputingCapacity() : 0f;
            SetChip(cyberTiles[0], CyberTileKeys[0], computing < 0.25f ? "LOW" : "NOMINAL", computing < 0.25f ? AvState.Caution : AvState.Ready);
            float intel = network.IntelCapacity() > 0f ? network.Intel / network.IntelCapacity() : 0f;
            SetChip(cyberTiles[1], CyberTileKeys[1], Mathf.FloorToInt(network.Intel) + " BANKED", intel < 0.2f ? AvState.Info : AvState.Ready);
            int home = network.Count(NodeKind.Command) + network.Count(NodeKind.Base);
            SetChip(cyberTiles[2], CyberTileKeys[2], stats.Hacked + " LIVE / " + home + " HOME", stats.Hacked > 0 ? AvState.Ready : AvState.Inert);
            float accessLeft = network.AccessRemaining(now);
            SetChip(cyberTiles[3], CyberTileKeys[3], accessLeft > 0f ? CyberWords.Seconds(accessLeft) : "NO LEASE",
                accessLeft > 0f ? AvState.Info : AvState.Inert);
            int intrusions = network.ActiveIncidents(IncidentKind.Intrusion);
            SetChip(cyberTiles[4], CyberTileKeys[4], network.CommandCompromised ? "C2 BREACH" : intrusions > 0 ? intrusions + " ACTIVE" : "CLEAR",
                network.CommandCompromised || intrusions > 0 ? AvState.Danger : AvState.Ready);
            int raids = network.ActiveIncidents(IncidentKind.Raid);
            SetChip(cyberTiles[5], CyberTileKeys[5], raids > 0 ? "RAID" : "CLEAR", raids > 0 ? AvState.Caution : AvState.Ready);
            SetChip(cyberTiles[6], CyberTileKeys[6], network.AnyFoothold(now) ? "TRACEABLE" : "NONE",
                network.AnyFoothold(now) ? AvState.Ready : AvState.Inert);
            SetChip(cyberTiles[7], CyberTileKeys[7], CyberWords.Phase(network.Phase),
                network.Phase == CampaignPhase.Offensive ? AvState.Danger
                : network.Phase == CampaignPhase.Active ? AvState.Caution : AvState.Info);
        }
    }
}
