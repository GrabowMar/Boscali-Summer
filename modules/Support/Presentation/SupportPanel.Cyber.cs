using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Domain.Layout;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// CYBER — the faction's cyber network. STATUS is the watch floor: INFOCON, resources, the
    /// node mesh, the live breach and the voice loop. ACTIONS holds the map abilities the
    /// network has earned. All hacking lives in the full-screen console. This file is the shell
    /// plus STATUS and the work that keeps running with the page closed: the voice loop and the
    /// alarm. Every figure comes from the network model; every control is a host request.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const int CyberStatusSub = 0;
        private const int CyberActionsSub = 1;
        private const float CyberBannerHeight = 120f;
        private const float CyberGraphHeight = 202f;
        private const int Ladder = 5;

        private static readonly string[] CyberSubTips =
        {
            "Watch floor: INFOCON, resources, the node mesh and the voice loop.",
            "The map abilities the network has earned."
        };
        private static readonly string[] CyberTileKeys =
            { "COMPUTING", "INTEL", "NODES", "STAGES", "INTRUSION", "JAMMING", "FOOTHOLD", "ADVERSARY" };

        private readonly GameObject[] cyberSubPages = new GameObject[2];
        private readonly string[] cyberLoop = new string[LoopLines];
        private readonly string[] cyberLoopShown = new string[LoopLines];
        private readonly Image[] ladderFill = new Image[Ladder];
        private readonly TMP_Text[] ladderText = new TMP_Text[Ladder];
        private readonly Tile[] cyberTiles = new Tile[8];
        private int cyberSub;
        private int cyberLoggedSerial = -1;
        private int selectedSite = -1;

        private Image cyberRail;
        private TMP_Text cyberInfocon, cyberPhase, cyberNote;
        private Image cyberBand;
        private TMP_Text cyberBandText;
        private Views.MiniNetmap cyberMap;
        private TMP_Text[] cyberLoopLabels;
        private AvButton openConsoleButton;
        private CyberAlarm alarm;

        private void ResetCyberPage()
        {
            for (int i = 0; i < cyberSubPages.Length; i++) cyberSubPages[i] = null;
            for (int i = 0; i < cyberLoop.Length; i++) cyberLoop[i] = cyberLoopShown[i] = null;
            for (int i = 0; i < Ladder; i++)
            {
                ladderFill[i] = null;
                ladderText[i] = null;
            }
            for (int i = 0; i < cyberTiles.Length; i++) cyberTiles[i] = null;
            cyberSub = CyberStatusSub;
            cyberLoggedSerial = -1;
            selectedSite = -1;
            cyberRail = cyberBand = null;
            cyberInfocon = cyberPhase = cyberNote = cyberBandText = null;
            cyberMap = null;
            cyberLoopLabels = null;
            openConsoleButton = null;
            alarm?.Dispose();
            alarm = null;
            ResetCyberOpsPage();
        }

        // ---- Build -------------------------------------------------------------------------------

        private void BuildCyberPage()
        {
            var page = (RectTransform)shell.CreatePage(TabCyber, "CyberPage").transform;
            // Each sub-page carries its own title row with the STATUS / ACTIONS toggle (M1).
            Rect subBody = shell.Body;
            for (int i = 0; i < cyberSubPages.Length; i++)
            {
                var go = new GameObject("Cyber" + i, typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.SetParent(page, false);
                AvKit.Stretch(rect);
                cyberSubPages[i] = go;
            }

            BuildStatusPage((RectTransform)cyberSubPages[CyberStatusSub].transform, subBody);
            BuildCyberOpsPage((RectTransform)cyberSubPages[CyberActionsSub].transform, subBody);
            SelectCyberSub(CyberStatusSub);
            alarm = new CyberAlarm(screenRoot != null ? screenRoot.transform : transform);
            CyberLog("WATCH FLOOR ONLINE · " + CyberWords.NetworkName + " STANDING BY");
        }

        private void SelectCyberSub(int sub)
        {
            cyberSub = Mathf.Clamp(sub, 0, cyberSubPages.Length - 1);
            AvButton.ClearTooltip();
            for (int i = 0; i < cyberSubPages.Length; i++)
                if (cyberSubPages[i] != null) cyberSubPages[i].SetActive(i == cyberSub);
            nextRefresh = 0f;
        }

        /// <summary>The board's node click: remember it and open the console on it.</summary>
        private void SelectSite(int slot)
        {
            selectedSite = slot;
            nextRefresh = 0f;
        }

        private void BuildStatusPage(RectTransform root, Rect body)
        {
            Rect content = PageFrame(root, body, OpsDomain.Cyber, CyberWords.NetworkName + " · WATCH FLOOR", CyberStatusSub, SelectCyberSub,
                CyberSubTips, out _);
            Rect[] at = Stack(content, new[]
            {
                new StackPiece(1, Mathf.Min(CyberBannerHeight, content.height), 0f, false),
                new StackPiece(2, CyberGraphHeight, 170f, false),
                new StackPiece(3, TileHeight * 2f + 24f, TileHeight * 2f + 24f, false),
                new StackPiece(4, 0f, 60f, true)
            }, 8f);

            RectTransform banner = Section(root, "Infocon", at[0]);
            float w = at[0].width, h = at[0].height;
            cyberRail = InstrumentPlate(banner, new Rect(0f, 0f, w, h), AvTheme.RailInfo);
            AvKit.Label(banner, "NETWORK CONDITION / C2", new Rect(12f, -4f, w - 176f, 11f), AvTheme.RailInfo,
                AvTokens.FontMicro, FontStyles.Bold).characterSpacing = 0.8f;
            cyberInfocon = AvKit.Label(banner, "", new Rect(12f, -16f, w - 176f, 30f), AvTheme.Dim, 22f, FontStyles.Bold);
            cyberInfocon.enableAutoSizing = true;
            cyberInfocon.fontSizeMin = AvTokens.FontLead;
            cyberInfocon.fontSizeMax = 22f;
            cyberPhase = SingleLine(AvKit.Label(banner, "", new Rect(12f, -46f, w - 176f, 16f), AvTheme.Dim, AvTokens.FontSmall,
                FontStyles.Bold));
            openConsoleButton = AvStyled.Button(banner, new Rect(w - 152f, -10f, 140f, 28f), "OPEN CONSOLE", "btn",
                OpenConsole, AvButtonStyle.Primary)
                .WithTooltip("The network-ops terminal: breach locations, answer incidents, buy network upgrades.");
            cyberBandText = SingleLine(AvKit.Label(banner, "", new Rect(12f, -66f, w - 24f, 16f), AvTheme.Dim,
                AvTokens.FontSmall, FontStyles.Normal));
            cyberBand = AvKit.ProgressBar(banner, new Rect(12f, -84f, w - 24f, 4f), 0f, AvTheme.RailInfo);
            cyberNote = Wrapped(AvStyled.Label(banner, new Rect(12f, -91f, w - 24f, Mathf.Max(14f, h - 98f)), "", "row-sub"));
            cyberNote.gameObject.SetActive(h >= 112f);

            // The INFOCON ladder beside the compact wire netmap (the page's second instrument).
            if (at[1].height > 0f)
            {
                RectTransform board = Section(root, "Netmap", at[1]);
                AvKit.Rule(board, new Rect(72f, 0f, at[1].width - 72f, 1f), AvTheme.RailInfo.WithAlpha(0.6f));
                float rung = (at[1].height - 4f * 4f) / Ladder;
                for (int i = 0; i < Ladder; i++)
                {
                    var r = new Rect(0f, -i * (rung + 4f), 64f, rung);
                    ladderFill[i] = AvKit.Panel(board, r, AvTheme.Surface);
                    ladderText[i] = AvKit.Label(board, "INFOCON\n" + (Ladder - i), r, AvTheme.Dim, AvTokens.FontMicro, FontStyles.Bold,
                        TextAlignmentOptions.Center);
                }
                cyberMap = new Views.MiniNetmap();
                cyberMap.Build(board, new Rect(72f, 0f, at[1].width - 72f, at[1].height), slot =>
                {
                    SelectSite(slot);
                    OpenConsole();
                });
            }

            if (at[2].height > 0f)
            {
                RectTransform health = Section(root, "Health", at[2]);
                AvStyled.Label(health, new Rect(0f, 0f, at[2].width, 16f), "NETWORK HEALTH · RESOURCES · HOLDINGS · THREATS",
                    "section-title");
                float tileWidth = (at[2].width - 12f) / 4f;
                for (int i = 0; i < cyberTiles.Length; i++)
                    cyberTiles[i] = BuildTile(health, new Rect((i % 4) * (tileWidth + 4f), -20f - (i / 4) * (TileHeight + 4f), tileWidth,
                        TileHeight), CyberTileKeys[i]);
            }

            if (at[3].height > 0f)
                cyberLoopLabels = BuildLoopLines(Section(root, "Shell", at[3]), at[3].width, at[3].height, "EVENT LOG · NEWEST FIRST");
        }

        /// <summary>The log in shell style: a prompt and monospace figures, written only when a line changes.</summary>
        private void WriteShellLoop()
        {
            for (int i = 0; i < cyberLoopLabels.Length && i < cyberLoop.Length; i++)
            {
                if (cyberLoopShown[i] == cyberLoop[i]) continue;
                cyberLoopShown[i] = cyberLoop[i];
                cyberLoopLabels[i].text = cyberLoop[i] == null ? "" : Mono("$ " + cyberLoop[i]);
            }
        }

        private void PaintLadder(int infocon)
        {
            if (ladderFill[0] == null) return;
            for (int i = 0; i < Ladder; i++)
            {
                int level = Ladder - i;
                bool lit = level == infocon;
                Color colour = level >= 4 ? AvTheme.RailReady : level == 3 ? AvTheme.RailCaution : AvTheme.RailDanger;
                ladderFill[i].color = lit ? colour.WithAlpha(0.3f) : AvTheme.Surface;
                ladderText[i].color = lit ? colour : AvTheme.Dim;
            }
        }

        // ---- Console and loop --------------------------------------------------------------------

        private void OpenConsole()
        {
            if (FullscreenInput.AnyOpen && !Window.OpsWindow.IsOpen) return;
            OpenRoom(CyberRoom(), selectedSite, openConsoleButton);
            CyberLog("CONSOLE · " + CyberWords.NetworkName + " ON THE BIG BOARD");
        }

        /// <summary>Work that must not wait for the CYBER page: the loop and the alarm.</summary>
        private void TickCyberBackground()
        {
            if (cyberSubPages[CyberStatusSub] == null) return;
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
            if (cyberSubPages[CyberStatusSub] == null) return;
            CyberNetwork network = support.LocalCyber;
            double now = support.OrbitNow;
            if (cyberSub == CyberStatusSub) RefreshStatusPage(network, now);
            else RefreshCyberOpsPage(bypass, network, now);
        }

        private void RefreshStatusPage(CyberNetwork network, double now)
        {
            if (cyberInfocon == null) return;
            bool built = network != null && network.HasCommand;
            CyberStats stats = network != null ? network.Stats() : default;

            if (!support.CyberEnabled)
            {
                cyberRail.color = AvTheme.RailInert;
                cyberInfocon.text = "OFFLINE";
                cyberInfocon.color = AvTheme.Dim;
                cyberPhase.text = "DISABLED IN HOST CONFIG";
                cyberNote.text = "The host has switched cyber operations off.";
            }
            else if (!built)
            {
                cyberRail.color = AvTheme.RailInert;
                cyberInfocon.text = "NO NETWORK";
                cyberInfocon.color = AvTheme.Dim;
                cyberPhase.text = "HOLD AN AIRBASE";
                cyberNote.text = CyberWords.Advice(network, now, out _, out _);
            }
            else
            {
                int infocon = network.Infocon;
                Color colour = infocon >= 5 ? AvTheme.RailReady : infocon >= 3 ? AvTheme.RailCaution : AvTheme.RailDanger;
                cyberRail.color = colour;
                cyberInfocon.text = CyberWords.Infocon(infocon);
                cyberInfocon.color = colour;
                cyberPhase.text = network.CommandCompromised ? "C2 BREACHED"
                    : CyberWords.Phase(network.Phase) + " · HEAT " + Mathf.RoundToInt(network.Heat);
                cyberNote.text = CyberWords.Advice(network, now, out _, out _);
            }

            float computing = network != null ? network.Computing : 0f;
            float intel = network != null ? network.Intel : 0f;
            float computingCap = Mathf.Max(1f, network != null ? network.ComputingCapacity() : 1f);
            float intelCap = Mathf.Max(1f, network != null ? network.IntelCapacity() : 1f);
            string breach = "";
            if (network != null && network.BreachActive)
                breach = " · BREACH " + CyberWords.PhaseOf(network.BreachPhase) + " " +
                         Mathf.RoundToInt(network.BreachTrace * 100f) + "%";
            else if (network != null && network.BreachAwaitingChoice)
                breach = " · CAPSTONE PENDING";
            cyberBandText.text = "COMP " + Mathf.FloorToInt(computing) + "/" + Mathf.RoundToInt(computingCap) +
                                 " +" + (network != null ? network.ComputingIncome() : 0f).ToString("0.#", Invariant) + "/S" +
                                 " · INTEL " + Mathf.FloorToInt(intel) + "/" + Mathf.RoundToInt(intelCap) +
                                 " +" + (network != null ? network.IntelIncome() : 0f).ToString("0.#", Invariant) + "/S" +
                                 breach;
            cyberBand.fillAmount = computing / computingCap;
            cyberBand.color = computing < 20f ? AvTheme.RailCaution : AvTheme.RailInfo;

            RefreshCyberTiles(network, stats, now, built);
            PaintLadder(built ? network.Infocon : 0);
            cyberMap?.Paint(network, now, selectedSite);
            if (cyberLoopLabels != null) WriteShellLoop();
        }

        private void RefreshCyberTiles(CyberNetwork network, in CyberStats stats, double now, bool built)
        {
            if (!built)
            {
                for (int i = 0; i < cyberTiles.Length; i++) PaintTile(cyberTiles[i], "—", Tone.Locked);
                return;
            }
            float computing = network.ComputingCapacity() > 0f ? network.Computing / network.ComputingCapacity() : 0f;
            PaintTile(cyberTiles[0], computing < 0.25f ? "LOW" : "NOMINAL",
                computing < 0.25f ? Tone.Armed : Tone.Ready);
            float intel = network.IntelCapacity() > 0f ? network.Intel / network.IntelCapacity() : 0f;
            PaintTile(cyberTiles[1], Mathf.FloorToInt(network.Intel) + " BANKED",
                intel < 0.2f ? Tone.Pending : Tone.Ready);
            int home = network.Count(NodeKind.Command) + network.Count(NodeKind.Base);
            PaintTile(cyberTiles[2], stats.Hacked + " LOC · " + home + " HOME",
                stats.Hacked > 0 ? Tone.Ready : Tone.Locked);
            PaintTile(cyberTiles[3], stats.StageTotal > 0 ? "STAGE " + stats.StageTotal : "NONE",
                stats.StageTotal >= 8 ? Tone.Ready : stats.StageTotal > 0 ? Tone.Pending : Tone.Locked);
            int intrusions = network.ActiveIncidents(IncidentKind.Intrusion);
            PaintTile(cyberTiles[4], network.CommandCompromised ? "C2 BREACH" : intrusions > 0 ? intrusions + " ACTIVE" : "CLEAR",
                network.CommandCompromised || intrusions > 0 ? Tone.Danger : Tone.Ready);
            int raids = network.ActiveIncidents(IncidentKind.Raid);
            PaintTile(cyberTiles[5], raids > 0 ? "RAID" : "CLEAR", raids > 0 ? Tone.Armed : Tone.Ready);
            PaintTile(cyberTiles[6], network.AnyFoothold(now) ? "ESTABLISHED" : "NONE",
                network.AnyFoothold(now) ? Tone.Ready : Tone.Locked);
            PaintTile(cyberTiles[7], CyberWords.Phase(network.Phase),
                network.Phase == CampaignPhase.Offensive ? Tone.Danger
                : network.Phase == CampaignPhase.Active ? Tone.Armed : Tone.Pending);
        }
    }
}
