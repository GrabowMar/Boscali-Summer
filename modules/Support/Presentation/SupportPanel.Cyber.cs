using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// CYBER — the faction's spectrum-defence network. NETWORK is the watch floor (INFOCON,
    /// annunciators, the live mesh, the console door, the voice loop); ARCHITECT builds it
    /// (sites on the map, inspector, doctrine); THREATS is the adversary campaign and the
    /// incident board; OPERATIONS strikes back with the offensive operations. This file is the
    /// shell plus NETWORK and the work that keeps running with the page closed: the voice loop
    /// and the alarm. Every figure comes from the network model; every control is a host request.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const int CyberNetworkSub = 0;
        private const int CyberArchitectSub = 1;
        private const int CyberThreatSub = 2;
        private const int CyberOpsSub = 3;
        private const float CyberBannerHeight = 136f;
        private const float CyberGraphHeight = 170f;

        private static readonly string[] CyberSubLabels = { "NETWORK", "ARCHITECT", "THREATS", "OPERATIONS" };
        private static readonly string[] CyberSubTips =
        {
            "Watch floor: INFOCON, annunciators, the mesh and the voice loop.",
            "Build and move field sites; the facility doctrine.",
            "The adversary campaign and the incident board.",
            "Offensive operations and the flare barrage."
        };
        private static readonly string[] CyberTileKeys =
            { "BANDWIDTH", "LINKS", "INTRUSION", "JAMMING", "EMCON", "FOOTHOLD", "SITES", "ADVERSARY" };

        private readonly GameObject[] cyberSubPages = new GameObject[4];
        private readonly AvButton[] cyberSubTabs = new AvButton[4];
        private readonly string[] cyberLoop = new string[LoopLines];
        private readonly Tile[] cyberTiles = new Tile[8];
        private int cyberSub;
        private int cyberLoggedSerial = -1;
        private int selectedSite = -1;

        private Image cyberRail;
        private TMP_Text cyberName, cyberInfocon, cyberPhase, cyberNote;
        private Image cyberBand;
        private TMP_Text cyberBandText;
        private CyberGraph cyberGraph;
        private TMP_Text cyberGraphNote;
        private TMP_Text[] cyberLoopLabels;
        private CyberConsole console;
        private CyberAlarm alarm;

        private void ResetCyberPage()
        {
            for (int i = 0; i < cyberSubPages.Length; i++)
            {
                cyberSubPages[i] = null;
                cyberSubTabs[i] = null;
            }
            for (int i = 0; i < cyberLoop.Length; i++) cyberLoop[i] = null;
            for (int i = 0; i < cyberTiles.Length; i++) cyberTiles[i] = null;
            cyberSub = CyberNetworkSub;
            cyberLoggedSerial = -1;
            selectedSite = -1;
            cyberRail = cyberBand = null;
            cyberName = cyberInfocon = cyberPhase = cyberNote = cyberBandText = cyberGraphNote = null;
            cyberGraph = null;
            cyberLoopLabels = null;
            if (console != null) Destroy(console.gameObject);
            console = null;
            alarm?.Dispose();
            alarm = null;
            ResetArchitectPage();
            ResetThreatPage();
            ResetCyberOpsPage();
        }

        // ---- Build -------------------------------------------------------------------------------

        private void BuildCyberPage()
        {
            var page = (RectTransform)shell.CreatePage(TabCyber, "CyberPage").transform;
            Rect body = shell.Body;
            float segment = (body.width - 12f) / CyberSubLabels.Length;
            for (int i = 0; i < CyberSubLabels.Length; i++)
            {
                int sub = i;
                cyberSubTabs[i] = AvStyled.Button(page,
                    new Rect(body.x + i * (segment + 4f), body.y, segment, SubNavHeight),
                    CyberSubLabels[i], "tab", () => SelectCyberSub(sub), AvButtonStyle.Tab)
                    .WithTooltip(CyberSubTips[i]);
            }

            var subBody = new Rect(body.x, body.y - SubNavHeight - 8f, body.width, body.height - SubNavHeight - 8f);
            for (int i = 0; i < cyberSubPages.Length; i++)
            {
                var go = new GameObject("Cyber" + i, typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.SetParent(page, false);
                AvKit.Stretch(rect);
                cyberSubPages[i] = go;
            }

            BuildNetworkPage((RectTransform)cyberSubPages[CyberNetworkSub].transform, subBody);
            BuildArchitectPage((RectTransform)cyberSubPages[CyberArchitectSub].transform, subBody);
            BuildThreatPage((RectTransform)cyberSubPages[CyberThreatSub].transform, subBody);
            BuildCyberOpsPage((RectTransform)cyberSubPages[CyberOpsSub].transform, subBody);
            SelectCyberSub(CyberNetworkSub);
            alarm = new CyberAlarm(screenRoot != null ? screenRoot.transform : transform);
            CyberLog("WATCH FLOOR ONLINE · " + CyberWords.NetworkName + " STANDING BY");
        }

        private void SelectCyberSub(int sub)
        {
            cyberSub = Mathf.Clamp(sub, 0, cyberSubPages.Length - 1);
            AvButton.ClearTooltip();
            for (int i = 0; i < cyberSubPages.Length; i++)
            {
                if (cyberSubPages[i] != null) cyberSubPages[i].SetActive(i == cyberSub);
                if (cyberSubTabs[i] != null) cyberSubTabs[i].SetLatched(i == cyberSub);
            }
            nextRefresh = 0f;
        }

        private void BuildNetworkPage(RectTransform root, Rect body)
        {
            float height = CyberBannerHeight + SectionGap +
                           HeaderHeight + TileHeight * 2f + 4f + SectionGap +
                           HeaderHeight + CyberGraphHeight + SectionGap +
                           HeaderHeight + LoopLines * LoopPitch + SectionGap;
            // The loop is the last block; on a tall panel it spaces its lines out rather than
            // leaving the page short above the status strip.
            float loopPitch = LoopPitch;
            RectTransform parent = BeginSub(root, body, height, out float x, out float y, out float width);

            var banner = new Rect(x, y, width, CyberBannerHeight);
            cyberRail = AvKit.TacticalCard(parent, banner, AvTheme.RailInert).Rail;
            cyberName = AvKit.Label(parent, CyberWords.NetworkName, new Rect(x + 12f, y - 8f, 200f, 20f),
                AvTheme.TextPrimary, AvTokens.FontTitle, FontStyles.Bold);
            cyberName.characterSpacing = 2f;
            cyberPhase = AvKit.Label(parent, "", new Rect(x + width - 232f, y - 10f, 220f, 16f), AvTheme.Dim,
                AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Right);
            cyberInfocon = AvKit.Label(parent, "", new Rect(x + 12f, y - 32f, width * 0.55f, 30f), AvTheme.Dim, 22f,
                FontStyles.Bold);
            cyberInfocon.enableAutoSizing = true;
            cyberInfocon.fontSizeMin = AvTokens.FontLead;
            cyberInfocon.fontSizeMax = 22f;
            AvStyled.Button(parent, new Rect(x + width - 150f, y - 34f, 138f, 26f), "OPEN CONSOLE", "btn",
                OpenConsole, AvButtonStyle.Primary)
                .WithTooltip("Full-screen network defence console: isolate, patch, bait and trace in real time.");
            cyberBandText = SingleLine(AvKit.Label(parent, "", new Rect(x + 12f, y - 68f, width - 24f, 16f), AvTheme.Dim,
                AvTokens.FontSmall, FontStyles.Normal));
            cyberBand = AvKit.ProgressBar(parent, new Rect(x + 12f, y - 88f, width - 24f, 4f), 0f, AvTheme.RailInfo);
            cyberNote = Wrapped(AvStyled.Label(parent, new Rect(x + 12f, y - 100f, width - 24f, 30f), "", "row-sub"));
            y -= CyberBannerHeight + SectionGap;

            Header(parent, x, ref y, width, "NETWORK HEALTH", "CONNECTION · DEFENCE · READINESS");
            float tileWidth = (width - 12f) / 4f;
            for (int i = 0; i < cyberTiles.Length; i++)
            {
                float tx = x + (i % 4) * (tileWidth + 4f);
                float ty = y - (i / 4) * (TileHeight + 4f);
                cyberTiles[i] = BuildTile(parent, new Rect(tx, ty, tileWidth, TileHeight), CyberTileKeys[i]);
            }
            y -= TileHeight * 2f + 4f + SectionGap;

            cyberGraphNote = Header(parent, x, ref y, width, "NETWORK MAP", "");
            cyberGraph = new CyberGraph(parent, new Rect(x, y, width, CyberGraphHeight), false, slot =>
            {
                SelectSite(slot);
                SelectCyberSub(CyberArchitectSub);
            });
            y -= CyberGraphHeight + SectionGap;

            cyberLoopLabels = BuildCyberLoop(parent, x, ref y, width, loopPitch);
        }

        private TMP_Text[] BuildCyberLoop(RectTransform parent, float x, ref float y, float width, float pitch)
        {
            Header(parent, x, ref y, width, "EVENT LOG", "NETWORK DEFENCE · NEWEST FIRST");
            var labels = new TMP_Text[LoopLines];
            for (int i = 0; i < LoopLines; i++)
            {
                labels[i] = Wrapped(AvStyled.Label(parent, new Rect(x, y - i * pitch, width, 28f), "", "row-sub"));
                labels[i].color = i == 0 ? AvTheme.TextPrimary : AvTheme.Dim;
            }
            y -= LoopLines * pitch;
            return labels;
        }

        // ---- Console and loop --------------------------------------------------------------------

        private void OpenConsole()
        {
            if (console == null) console = CyberConsole.Create(support, cyberLoop);
            console.Show(selectedSite);
            CyberLog("CONSOLE · " + CyberWords.NetworkName + " ON THE BIG BOARD");
        }

        /// <summary>Work that must not wait for the CYBER page: the loop and the alarm.</summary>
        private void TickCyberBackground()
        {
            if (cyberSubPages[CyberNetworkSub] == null) return;
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
                    support.CyberOriginName(network.NoticeOrigin(age)));
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
            if (cyberSubPages[CyberNetworkSub] == null) return;
            CyberNetwork network = support.LocalCyber;
            double now = support.OrbitNow;
            switch (cyberSub)
            {
                case CyberNetworkSub: RefreshNetworkPage(network, now); break;
                case CyberArchitectSub: RefreshArchitectPage(bypass, network, now); break;
                case CyberThreatSub: RefreshThreatPage(network, now); break;
                default: RefreshCyberOpsPage(bypass, network, now); break;
            }
        }

        private void RefreshNetworkPage(CyberNetwork network, double now)
        {
            if (cyberInfocon == null) return;
            bool built = network != null && network.HasCommand;
            CyberStats stats = network != null ? network.Stats() : default;
            float time = Time.unscaledTime;

            if (!support.CyberEnabled)
            {
                cyberRail.color = AvTheme.RailInert;
                cyberInfocon.text = "OFFLINE";
                cyberInfocon.color = AvTheme.Dim;
                cyberPhase.text = "DISABLED IN HOST CONFIG";
                cyberNote.text = "The host has switched spectrum defence off.";
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

            cyberBandText.text = "BANDWIDTH " + Mathf.FloorToInt(network != null ? network.Bandwidth : 0f) + "/" +
                                 Mathf.RoundToInt(stats.Capacity) + " MB · NET " +
                                 (stats.Net >= 0f ? "+" : "") + stats.Net.ToString("0.#", Invariant) + "/S" +
                                 (stats.Congested ? " · CONGESTED" : "");
            cyberBand.fillAmount = stats.Capacity > 0f && network != null ? network.Bandwidth / stats.Capacity : 0f;
            cyberBand.color = stats.Congested ? AvTheme.RailCaution : AvTheme.RailInfo;

            RefreshCyberTiles(network, stats, now, built);
            cyberGraphNote.text = built ? stats.Links + " LINK" + (stats.Links == 1 ? "" : "S") + " · CLICK A SITE" : "";
            cyberGraph.Paint(network, now, selectedSite, time, built ? null : "NO AIRBASE HELD");
            WriteLoop(cyberLoopLabels, cyberLoop);
        }

        private void RefreshCyberTiles(CyberNetwork network, in CyberStats stats, double now, bool built)
        {
            if (!built)
            {
                for (int i = 0; i < cyberTiles.Length; i++) PaintTile(cyberTiles[i], "—", Tone.Locked);
                return;
            }
            float band = stats.Capacity > 0f ? network.Bandwidth / stats.Capacity : 0f;
            PaintTile(cyberTiles[0], stats.Congested ? "CONGESTED" : band < 0.25f ? "LOW" : "NOMINAL",
                stats.Congested ? Tone.Danger : band < 0.25f ? Tone.Armed : Tone.Ready);
            int offNet = stats.Sites - stats.OnNet;
            PaintTile(cyberTiles[1], offNet > 0 ? offNet + " OFF-NET" : stats.Links + " UP",
                offNet > 0 ? Tone.Armed : Tone.Ready);
            int intrusions = network.ActiveIncidents(IncidentKind.Intrusion);
            PaintTile(cyberTiles[2], network.CommandCompromised ? "C2 BREACH" : intrusions > 0 ? intrusions + " ACTIVE" : "CLEAR",
                network.CommandCompromised || intrusions > 0 ? Tone.Danger : Tone.Ready);
            int raids = network.ActiveIncidents(IncidentKind.Raid);
            PaintTile(cyberTiles[3], raids > 0 ? "RAID" : "CLEAR", raids > 0 ? Tone.Armed : Tone.Ready);
            bool exposed = network.ExposedUntil > now;
            PaintTile(cyberTiles[4], exposed ? "EXPOSED " + CyberWords.Seconds(network.ExposedUntil - now) : "HOLDING",
                exposed ? Tone.Danger : Tone.Ready);
            PaintTile(cyberTiles[5], network.AnyFoothold(now) ? "ESTABLISHED" : "NONE",
                network.AnyFoothold(now) ? Tone.Ready : Tone.Locked);
            int field = network.FieldCount;
            PaintTile(cyberTiles[6], (stats.Sites - field) + " BASE · " + field + "/" + support.CyberSiteLimit,
                stats.Sites > 0 ? Tone.Ready : Tone.Locked);
            PaintTile(cyberTiles[7], CyberWords.Phase(network.Phase),
                network.Phase == CampaignPhase.Offensive ? Tone.Danger
                : network.Phase == CampaignPhase.Active ? Tone.Armed : Tone.Pending);
        }
    }
}
