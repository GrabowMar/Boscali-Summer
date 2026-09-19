using System.Globalization;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static BoscaliSummer.Features.Support.Presentation.SupportOverlayUi;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// The CYBER console: a full-screen network-defence board.
    ///
    /// <para>The live mesh fills the centre at real geography — sites, links, traffic pulses,
    /// intrusions walking toward Cyber Command, raid sectors. The left rail is the watch
    /// officer's status and the selected site; the right rail holds the five verbs (keys 1–5)
    /// with their costs, recharges and refusal words, and the incident stack (Tab cycles it).
    /// Click a site to select it, Q/E step through sites, Esc or a right-click closes. The
    /// border burns red at INFOCON 2 and below.</para>
    ///
    /// <para>Like the station uplink it owns input while it is up (<see cref="FullscreenInput"/>
    /// plus the map guard patch) and closes itself when the map closes or the game pauses.
    /// Everything shown is the local network model; every verb is a host-validated command.</para>
    /// </summary>
    internal sealed class CyberConsole : MonoBehaviour
    {
        private const float FrameWidth = 1920f;
        private const float FrameHeight = 1080f;
        private const float BoardX = 320f;
        private const float BoardTop = -112f;
        private const float BoardWidth = 1280f;
        private const float LeftX = 24f;
        private const float RightX = 1616f;
        private const float RailWidth = 280f;
        // One grid for every readout: key column, then the value column. 30 px rows, and the
        // list's pitch stretches so each rail is flush top and bottom.
        private const float KeyWidth = RailWidth * 0.34f;
        private const float AdviceHeight = 34f;
        private const float LoopPitch = 17f;
        private const float LegendHeight = 16f;
        private const float TextInterval = 0.1f;
        private const int SortingOrder = 30000;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        private static readonly string[] Keys =
            { "INFOCON", "ADVERSARY", "HEAT", "BANDWIDTH", "NET FLOW", "SITES", "LINKS", "FOOTHOLD", "EMITTERS", "ECM KILLS",
              "DEFENDED", "BREACHED" };
        private static readonly KeyCode[] VerbKeys =
            { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5 };

        private static bool visible;
        private static int closedFrame = -10;

        /// <summary>True while the console is on screen and for one frame after it closes.</summary>
        public static bool IsOpen => visible || Time.frameCount <= closedFrame + 1;

        private SupportManager support;
        private string[] loop;
        private Canvas canvas;
        private GameObject content;
        private RectTransform frame;
        private AvStyled.DataBar bar;
        private CyberGraph graph;
        private TMP_Text boardTitle, boardPhase, boardAdvice;
        private Image adviceRail;
        private AvButton adviceButton;
        private CyberVerb adviceVerb;
        private int adviceTarget = -1;
        private string emptyMessage;
        private Image[] alertFrame;
        private readonly TMP_Text[] values = new TMP_Text[Keys.Length];
        private TMP_Text siteName, siteState, siteDetail, siteMore;
        private readonly AvButton[] verbButtons = new AvButton[CyberNetwork.VerbCount];
        private readonly TMP_Text[] verbStatus = new TMP_Text[CyberNetwork.VerbCount];
        private readonly Image[] verbRecharge = new Image[CyberNetwork.VerbCount];
        private readonly AvButton[] incidentButtons = new AvButton[CyberNetwork.IncidentSlots];
        private readonly TMP_Text[] loopLabels = new TMP_Text[6];
        private TMP_Text status;
        private Image statusRail;
        private string localStatus;
        private float localStatusUntil;

        private readonly FullscreenInput input = new FullscreenInput();
        private int selectedSite = -1;
        private int selectedIncident = -1;
        private float nextText;

        public static CyberConsole Create(SupportManager manager, string[] voiceLoop)
        {
            var go = new GameObject("BoscaliCyberConsole", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = SortingOrder;
            canvas.enabled = false;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(FrameWidth, FrameHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;

            CyberConsole console = go.AddComponent<CyberConsole>();
            console.support = manager;
            console.loop = voiceLoop;
            console.canvas = canvas;
            console.Build();
            console.content.SetActive(false);
            return console;
        }

        public void Show(int site)
        {
            if (site >= 0) selectedSite = site;
            visible = true;
            canvas.enabled = true;
            content.SetActive(true);
            nextText = 0f;
            AvButton.ClearTooltip();
            input.Hold();
        }

        public void Close()
        {
            if (!visible) return;
            visible = false;
            closedFrame = Time.frameCount;
            if (content != null) content.SetActive(false);
            if (canvas != null) canvas.enabled = false;
            AvButton.ClearTooltip();
        }

        private void OnDestroy()
        {
            Close();
            input.Release();
            closedFrame = -10;
        }

        // ---- Build -------------------------------------------------------------------------------

        private void Build()
        {
            var root = (RectTransform)transform;
            var contentObject = new GameObject("Content", typeof(RectTransform));
            content = contentObject;
            var contentRect = (RectTransform)contentObject.transform;
            contentRect.SetParent(root, false);
            AvKit.Stretch(contentRect);

            Image blocker = AvKit.Panel(contentRect, new Rect(0f, 0f, 10f, 10f), AvTheme.Surface.WithAlpha(0.98f));
            AvKit.Stretch(blocker.rectTransform);
            blocker.raycastTarget = true;

            var frameObject = new GameObject("Frame", typeof(RectTransform));
            frame = (RectTransform)frameObject.transform;
            frame.SetParent(contentRect, false);
            frame.anchorMin = frame.anchorMax = new Vector2(0.5f, 0.5f);
            frame.pivot = new Vector2(0.5f, 0.5f);
            frame.sizeDelta = new Vector2(FrameWidth, FrameHeight);

            alertFrame = AvKit.Outline(frame, new Rect(6f, -6f, FrameWidth - 12f, FrameHeight - 12f), Color.clear);
            bar = AvStyled.TopBar(frame, new Rect(LeftX, -16f, FrameWidth - LeftX * 2f, AvTokens.ScreenHeaderHeight), "CYBER", 4);

            // The board sits between the title band and the bottom readouts; the rails flank
            // it and their primary lists absorb whatever height is left over, so no band of
            // the console is left empty and nothing overlaps.
            float frameHeight = frame.rect.height;
            float statusTop = -frameHeight + AvTokens.Space4 + AvTokens.StatusStripHeight;
            float legendTop = statusTop + AvTokens.Space1 + LegendHeight;
            float loopTop = legendTop + AvTokens.Space1 + loopLabels.Length * LoopPitch;
            float adviceTop = loopTop + AvTokens.Space3 + AdviceHeight;
            float boardBottom = adviceTop + AvTokens.Space2;
            float boardHeight = BoardTop - boardBottom;

            var board = new Rect(BoardX, BoardTop, BoardWidth, boardHeight);
            graph = new CyberGraph(frame, board, true, Select);
            boardTitle = AvKit.Label(frame, "", new Rect(BoardX, BoardTop + 26f, 700f, 18f), AvTheme.RailReady,
                AvTokens.FontTitle, FontStyles.Bold);
            boardPhase = AvKit.Label(frame, "", new Rect(BoardX + BoardWidth - 626f, BoardTop + 26f, 600f, 16f),
                AvTheme.Dim, AvTokens.FontSmall, FontStyles.Bold, TextAlignmentOptions.Right);

            // The watch officer's next move, always on screen, always one key away. The alert
            // and the empty state are written here too — text and rail, never colour alone —
            // so the pulsing border only ever repeats what the words already say.
            var adviceArea = new Rect(BoardX, adviceTop, BoardWidth, AdviceHeight);
            AvStyled.Box(frame, adviceArea, "card");
            adviceRail = AvKit.Rule(frame, new Rect(adviceArea.x, adviceArea.y, 3f, adviceArea.height), AvTheme.RailInfo);
            boardAdvice = AvKit.Label(frame, "", new Rect(adviceArea.x + 14f, adviceArea.y - 8f, adviceArea.width - 190f, 20f),
                AvTheme.TextPrimary, AvTokens.FontLead, FontStyles.Bold);
            adviceButton = AvStyled.Button(frame, new Rect(adviceArea.x + adviceArea.width - 170f, adviceArea.y - 2f, 160f, AvTokens.RowHeight),
                "[SPACE] RESPOND", "btn", DoAdvice, AvButtonStyle.Primary)
                .WithTooltip("Run the suggested countermeasure on the site or incident it names.");

            BuildLeftRail(BoardTop, boardBottom);
            BuildRightRail(BoardTop, boardBottom);

            for (int i = 0; i < loopLabels.Length; i++)
            {
                loopLabels[i] = AvKit.Label(frame, "", new Rect(BoardX, loopTop - i * LoopPitch, BoardWidth, 16f),
                    i == 0 ? AvTheme.TextPrimary : AvTheme.Dim, AvTokens.FontSmall, FontStyles.Bold);
            }
            AvKit.Label(frame,
                "CLICK SITE  SELECT   ·   Q / E  PREV / NEXT SITE   ·   TAB  NEXT INCIDENT   ·   1-5  VERB   ·   " +
                "SPACE  ADVISED MOVE   ·   ESC / RIGHT-CLICK  CLOSE",
                new Rect(BoardX, legendTop, BoardWidth, LegendHeight), AvTheme.Dim, AvTokens.FontSmall,
                FontStyles.Normal, TextAlignmentOptions.Center);
            status = AvStyled.StatusStrip(
                frame, new Rect(LeftX, statusTop, FrameWidth - LeftX * 2f, AvTokens.StatusStripHeight), out statusRail);
        }

        private void BuildLeftRail(float railTop, float railBottom)
        {
            float y = railTop;
            Section(frame, LeftX, ref y, RailWidth, "WATCH OFFICER");
            float rowsTop = y;
            // The readout list is the rail's primary list: it takes the slack between the
            // section head and the selected-site block.
            float block = AvTokens.Space4 + 30f + 176f;
            float pitch = FillPitch(rowsTop - railBottom - block, Keys.Length, AvTokens.RowHeight);
            for (int i = 0; i < Keys.Length; i++)
            {
                float rowY = rowsTop - i * pitch;
                AvKit.Label(frame, Keys[i], new Rect(LeftX, rowY, KeyWidth, AvTokens.RowHeight), AvTheme.Dim, 12f);
                values[i] = Value(frame, new Rect(LeftX + KeyWidth + AvTokens.Gap, rowY,
                    RailWidth - KeyWidth - AvTokens.Gap, AvTokens.RowHeight));
            }
            y = rowsTop - (Keys.Length - 1) * pitch - AvTokens.RowHeight - AvTokens.Space4;
            Section(frame, LeftX, ref y, RailWidth, "SELECTED SITE");
            siteName = AvKit.Label(frame, "", new Rect(LeftX, y, RailWidth, 22f), AvTheme.TextPrimary, AvTokens.FontTitle,
                FontStyles.Bold);
            siteState = AvKit.Label(frame, "", new Rect(LeftX, y - 26f, RailWidth, 18f), AvTheme.Dim, AvTokens.FontLead,
                FontStyles.Bold);
            siteDetail = AvKit.Label(frame, "", new Rect(LeftX, y - 50f, RailWidth, 60f), AvTheme.Dim, AvTokens.FontSmall,
                FontStyles.Normal, TextAlignmentOptions.TopLeft, wrap: true);
            siteMore = AvKit.Label(frame, "", new Rect(LeftX, y - 116f, RailWidth, 60f), AvTheme.Dim, AvTokens.FontSmall,
                FontStyles.Normal, TextAlignmentOptions.TopLeft, wrap: true);
        }

        private void BuildRightRail(float railTop, float railBottom)
        {
            float y = railTop;
            Section(frame, RightX, ref y, RailWidth, "COUNTERMEASURES");
            for (int i = 0; i < verbButtons.Length; i++)
            {
                int index = i;
                var verb = (CyberVerb)i;
                verbButtons[i] = AvStyled.Button(frame, new Rect(RightX, y, RailWidth, AvTokens.RowHeight),
                    "[" + (i + 1) + "] " + CyberWords.Verb(verb), "btn", () => Verb(index),
                    verb == CyberVerb.Isolate ? AvButtonStyle.Danger : AvButtonStyle.Default)
                    .WithTooltip(CyberWords.Verb(verb) + " — " + CyberWords.VerbHelp(verb));
                verbStatus[i] = Detail(frame, new Rect(RightX, y - 34f, RailWidth, 30f));
                verbRecharge[i] = AvKit.ProgressBar(frame, new Rect(RightX, y - 67f, RailWidth, 3f), 0f, AvTheme.RailCaution);
                y -= 78f;
            }
            y -= 10f;
            Section(frame, RightX, ref y, RailWidth, "INCIDENT STACK · TAB");
            // Close is anchored to the rail's bottom; the incident stack is the primary list
            // and absorbs the rest, so the rail never ends in a dead band.
            float closeTop = railBottom + 34f;
            float listBottom = closeTop + AvTokens.Space3;
            float pitch = FillPitch(y - listBottom, incidentButtons.Length, AvTokens.RowHeight);
            for (int i = 0; i < incidentButtons.Length; i++)
            {
                int index = i;
                incidentButtons[i] = AvStyled.Button(frame, new Rect(RightX, y - i * pitch, RailWidth, AvTokens.RowHeight),
                    "", "btn", () => SelectIncident(index), AvButtonStyle.Quiet);
            }
            AvStyled.Button(frame, new Rect(RightX, closeTop, RailWidth, 34f), "CLOSE CONSOLE  [ESC]", "btn", Close,
                AvButtonStyle.Quiet).WithTooltip("Close the console. The network keeps running; the loop keeps talking.");
        }

        // ---- Frame -------------------------------------------------------------------------------

        private void Update()
        {
            if (!visible)
            {
                if (input.Held && Time.frameCount > closedFrame + 1) input.Release();
                return;
            }
            if (support == null || !DynamicMap.mapMaximized || GameplayUI.GameIsPaused)
            {
                Close();
                return;
            }

            CyberNetwork network = support.LocalCyber;
            double now = support.OrbitNow;
            HandleKeys(network);
            if (!visible) return;
            KeepSelection(network);

            float time = Time.unscaledTime;
            bool built = network != null && network.HasCommand;
            graph.Paint(network, now, selectedSite, time, built ? null : "NO AIRBASE HELD");
            emptyMessage = !support.CyberEnabled ? "SPECTRUM DEFENCE DISABLED BY THE HOST"
                : !built ? "NO CYBER COMMAND · IT COMES UP BY ITSELF ON YOUR CENTRAL AIRBASE" : null;

            int infocon = built ? network.Infocon : 5;
            Color alert = infocon <= 2 ? AvTheme.RailDanger.WithAlpha(0.65f)
                : infocon <= 3 ? AvTheme.RailCaution.WithAlpha(0.35f) : AvTheme.Hairline.WithAlpha(0.4f);
            for (int i = 0; i < alertFrame.Length; i++) alertFrame[i].color = alert;

            if (time < nextText) return;
            nextText = time + TextInterval;
            WriteText(network, now, built, infocon);
        }

        private void HandleKeys(CyberNetwork network)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetMouseButtonDown(1))
            {
                Close();
                return;
            }
            for (int i = 0; i < VerbKeys.Length; i++)
                if (Input.GetKeyDown(VerbKeys[i])) Verb(i);
            if (Input.GetKeyDown(KeyCode.Space)) DoAdvice();
            if (Input.GetKeyDown(KeyCode.Tab)) CycleIncident(network);
            if (Input.GetKeyDown(KeyCode.Q)) CycleSite(network, -1);
            if (Input.GetKeyDown(KeyCode.E)) CycleSite(network, 1);
        }

        private void KeepSelection(CyberNetwork network)
        {
            if (network == null) return;
            if (!network.Exists(selectedSite)) selectedSite = -1;
            if (!network.IncidentActive(selectedIncident))
            {
                selectedIncident = -1;
                for (int i = 0; i < CyberNetwork.IncidentSlots; i++)
                {
                    if (!network.IncidentActive(i)) continue;
                    selectedIncident = i;
                    break;
                }
            }
        }

        /// <summary>Fire the advised verb on the advised target, selecting it first so the rails agree.</summary>
        private void DoAdvice()
        {
            if (adviceTarget < 0)
            {
                Say("NOTHING TO DO · THE NETWORK IS HOLDING");
                return;
            }
            if (CyberNetwork.TargetsIncident(adviceVerb)) SelectIncident(adviceTarget);
            else Select(adviceTarget);
            Verb((int)adviceVerb);
        }

        private void Select(int slot)
        {
            selectedSite = slot;
            nextText = 0f;
        }

        private void SelectIncident(int index)
        {
            CyberNetwork network = support.LocalCyber;
            if (network == null || !network.IncidentActive(index)) return;
            selectedIncident = index;
            int site = network.Incident(index).Site;
            if (network.Exists(site)) selectedSite = site;
            nextText = 0f;
        }

        private void CycleIncident(CyberNetwork network)
        {
            if (network == null) return;
            for (int step = 1; step <= CyberNetwork.IncidentSlots; step++)
            {
                int index = (Mathf.Max(selectedIncident, -1) + step + CyberNetwork.IncidentSlots) % CyberNetwork.IncidentSlots;
                if (!network.IncidentActive(index)) continue;
                SelectIncident(index);
                return;
            }
        }

        private void CycleSite(CyberNetwork network, int direction)
        {
            if (network == null) return;
            int start = selectedSite < 0 ? (direction > 0 ? -1 : 0) : selectedSite;
            for (int step = 1; step <= CyberNetwork.SlotCount; step++)
            {
                int slot = ((start + direction * step) % CyberNetwork.SlotCount + CyberNetwork.SlotCount) % CyberNetwork.SlotCount;
                if (!network.Exists(slot)) continue;
                Select(slot);
                return;
            }
        }

        private void Verb(int index)
        {
            CyberNetwork network = support.LocalCyber;
            if (network == null || index < 0 || index >= CyberNetwork.VerbCount) return;
            var verb = (CyberVerb)index;
            int target = CyberNetwork.TargetsIncident(verb) ? selectedIncident : selectedSite;
            CyberDenial denial = support.CommandPending ? CyberDenial.None : network.Check(verb, target, support.OrbitNow);
            if (support.CommandPending)
            {
                Say("COMMAND PENDING · WAIT FOR THE HOST");
                return;
            }
            if (denial != CyberDenial.None)
            {
                Say(CyberWords.Verb(verb) + " REFUSED · " + CyberWords.Denial(denial));
                return;
            }
            support.RequestCyberVerb(verb, target);
            Say(CyberWords.Verb(verb) + " SENT · " + Target(network, verb, target));
            nextText = 0f;
        }

        private void Say(string line)
        {
            localStatus = line;
            localStatusUntil = Time.unscaledTime + 3f;
            nextText = 0f;
        }

        private string Target(CyberNetwork network, CyberVerb verb, int target) =>
            CyberNetwork.TargetsIncident(verb)
                ? CyberWords.Incident(network.Incident(target).Kind) + " · " + support.CyberOriginName(network.Incident(target).Origin)
                : CyberWords.Callsign(network, target);

        // ---- Text --------------------------------------------------------------------------------

        private void WriteText(CyberNetwork network, double now, bool built, int infocon)
        {
            CyberStats stats = network != null ? network.Stats() : default;
            Color infoconColour = infocon >= 5 ? AvTheme.RailReady : infocon >= 3 ? AvTheme.RailCaution : AvTheme.RailDanger;
            int active = built ? network.ActiveIncidents(IncidentKind.None) : 0;

            bar.State.text = CyberWords.NetworkName + " · NETWORK DEFENCE · " + (built ? CyberWords.Infocon(infocon) : "OFFLINE");
            bar.SetChip(0, built ? CyberWords.Phase(network.Phase) : "NO NET",
                !built ? "inert" : network.Phase == CampaignPhase.Offensive ? "danger" : network.Phase == CampaignPhase.Active ? "warn" : "info");
            bar.SetChip(1, active > 0 ? active + " INCIDENT" + (active == 1 ? "" : "S") : "BOARD CLEAR", active > 0 ? "danger" : "live");
            bar.SetChip(2, stats.Congested ? "CONGESTED" : "BAND OK", stats.Congested ? "warn" : "live");
            bar.SetChip(3, support.OpsStateFresh ? "LINKED" : "SYNCING", support.OpsStateFresh ? "live" : "warn");

            boardTitle.text = built ? CyberWords.Infocon(infocon) + (network.CommandCompromised ? " · C2 BREACHED" : "") : "";
            boardTitle.color = infoconColour;
            boardPhase.text = built
                ? stats.OnNet + "/" + stats.Sites + " ON NET · " + stats.Links + " LINKS · " +
                  (network.NextIncident > now ? "NEXT MOVE ~" + TheaterGrid.Clock(network.NextIncident - now) : "ADVERSARY IDLE")
                : "";

            values[0].text = built ? infocon.ToString(Invariant) : "—";
            values[0].color = infoconColour;
            values[1].text = built ? CyberWords.Phase(network.Phase) : "—";
            values[2].text = built ? Mathf.RoundToInt(network.Heat) + " / 100" : "—";
            values[3].text = network != null ? Mathf.FloorToInt(network.Bandwidth) + " / " + Mathf.RoundToInt(stats.Capacity) + " MB" : "—";
            values[4].text = (stats.Net >= 0f ? "+" : "") + stats.Net.ToString("0.#", Invariant) + " MB/S";
            values[4].color = stats.Congested ? AvTheme.RailCaution : AvTheme.TextPrimary;
            int field = network != null ? network.FieldCount : 0;
            values[5].text = (stats.Sites - field) + " BASE · " + field + "/" + support.CyberSiteLimit;
            values[6].text = stats.Links.ToString(Invariant);
            values[7].text = built && network.AnyFoothold(now) ? "ESTABLISHED" : "NONE";
            bool exposed = built && network.ExposedUntil > now;
            values[8].text = exposed ? "EXPOSED " + CyberWords.Seconds(network.ExposedUntil - now) : "HIDDEN";
            values[8].color = exposed ? AvTheme.RailDanger : AvTheme.TextPrimary;
            values[9].text = network != null ? network.SeekersDefeated.ToString(Invariant) : "0";
            values[10].text = network != null ? network.Defended.ToString(Invariant) : "0";
            values[10].color = network != null && network.Defended > 0 ? AvTheme.RailReady : AvTheme.TextPrimary;
            values[11].text = network != null ? network.Breached.ToString(Invariant) : "0";
            values[11].color = network != null && network.Breached > 0 ? AvTheme.RailDanger : AvTheme.TextPrimary;

            string advice = CyberWords.Advice(network, now, out adviceVerb, out adviceTarget);
            bool actionable = emptyMessage == null && adviceTarget >= 0 && network != null &&
                              network.Check(adviceVerb, adviceTarget, now) == CyberDenial.None && !support.CommandPending;
            string alert = built && infocon <= 2
                ? "ALERT · " + CyberWords.Infocon(infocon) + (network.CommandCompromised ? " · C2 BREACHED" : "")
                : built && infocon == 3 ? "WATCH · " + CyberWords.Infocon(infocon) : null;
            boardAdvice.text = emptyMessage ?? (alert != null ? alert + " — " + advice : advice);
            boardAdvice.color = emptyMessage != null ? AvTheme.RailCaution
                : actionable ? AvTheme.TextPrimary : AvTheme.Dim;
            adviceRail.color = emptyMessage != null ? AvTheme.RailInert
                : infocon <= 2 ? AvTheme.RailDanger : infocon == 3 ? AvTheme.RailCaution : AvTheme.RailInfo;
            adviceButton.SetEnabled(actionable);
            adviceButton.SetText(actionable ? "[SPACE] " + CyberWords.Verb(adviceVerb) : "NO ACTION NEEDED");
            adviceButton.WithTooltip(emptyMessage != null
                ? emptyMessage + " · nothing to run yet."
                : actionable
                    ? "Run the advised " + CyberWords.Verb(adviceVerb) + " on " + Target(network, adviceVerb, adviceTarget) + "."
                    : "No move to advise right now — " + CyberWords.Denial(
                        network != null ? network.Check(adviceVerb, adviceTarget, now) : CyberDenial.NoCommand) + ".");

            WriteSite(network, now);
            WriteVerbs(network, now);
            WriteIncidents(network, now);

            for (int i = 0; i < loopLabels.Length; i++)
                loopLabels[i].text = loop != null && i < loop.Length ? loop[i] ?? "" : "";
            string manager = support.Status;
            bool action = Time.unscaledTime < localStatusUntil;
            status.text = (action ? "ACTION" : "STATUS") + "  ·  " + (action ? localStatus : manager ?? "");
            status.color = action ? AvTheme.TextPrimary : AvTheme.Dim;
            if (statusRail != null) statusRail.color = action ? AvTheme.Warning : AvTheme.RailInert;
        }

        private void WriteSite(CyberNetwork network, double now)
        {
            if (network == null || !network.Exists(selectedSite))
            {
                siteName.text = "NONE";
                siteState.text = "CLICK A SITE ON THE BOARD";
                siteState.color = AvTheme.Dim;
                siteDetail.text = "";
                siteMore.text = "";
                return;
            }
            CyberSite site = network.Site(selectedSite);
            CyberSiteInfo info = CyberSites.Info(site.Kind);
            siteName.text = CyberWords.Callsign(network, selectedSite);
            string state = CyberWords.SiteState(network, selectedSite, now);
            siteState.text = state + (site.Kind == CyberSiteKind.Jammer ? " · " + CyberWords.Mode(site.Mode) : "");
            siteState.color = site.Compromised || site.Lost || site.Down ? AvTheme.RailDanger
                : site.Isolated ? AvTheme.Disabled
                : network.OnNet(selectedSite) ? AvTheme.RailReady : AvTheme.RailCaution;
            int hops = network.Hops(selectedSite);
            siteDetail.text = info.Name + (site.Static ? " · AIRBASE" : "") + "\n" + TheaterGrid.Kilometres(site.X, site.Z) + "\n" +
                              (hops >= 0 ? hops + " HOP(S) TO CYBER COMMAND" : "NO PATH TO CYBER COMMAND");
            siteMore.text = "EMISSIONS " + CyberSites.Emissions(network.EmissionOf(selectedSite)) + "\nSTRENGTH " +
                            Mathf.RoundToInt(network.EffectScale(selectedSite, now) * 100f) + "%" +
                            (info.EffectRadius > 0f
                                ? "\nCOVER " + (network.EffectRadius(selectedSite, now) / 1000f).ToString("0.#", Invariant) + " KM"
                                : "");
        }

        private void WriteVerbs(CyberNetwork network, double now)
        {
            for (int i = 0; i < verbButtons.Length; i++)
            {
                var verb = (CyberVerb)i;
                int target = CyberNetwork.TargetsIncident(verb) ? selectedIncident : selectedSite;
                CyberDenial denial = network != null ? network.Check(verb, target, now) : CyberDenial.NoCommand;
                bool free = verb == CyberVerb.Isolate && network != null && network.Exists(target) && network.Site(target).Isolated;
                verbButtons[i].SetEnabled(denial == CyberDenial.None && !support.CommandPending);
                verbButtons[i].SetText("[" + (i + 1) + "] " + (free ? "REJOIN" : CyberWords.Verb(verb)));
                verbButtons[i].WithTooltip(CyberWords.Verb(verb) + " — " + CyberWords.VerbHelp(verb) + " " +
                    (denial == CyberDenial.None
                        ? support.CommandPending ? "COMMAND PENDING · WAIT FOR THE HOST."
                        : "READY ON " + Target(network, verb, target) + "."
                        : CyberWords.Denial(denial) + "."));
                string cost = free ? "FREE" : Mathf.RoundToInt(CyberNetwork.VerbCost(verb)) + " MB";
                string on = denial == CyberDenial.None && network != null ? " · " + Target(network, verb, target) : "";
                verbStatus[i].text = cost + " · " + CyberWords.Denial(denial) + on;
                verbStatus[i].color = denial == CyberDenial.None ? AvTheme.RailReady
                    : denial == CyberDenial.Recharging || denial == CyberDenial.LowBandwidth ? AvTheme.RailCaution
                    : AvTheme.Dim;
                float recharge = network != null ? network.RechargeRemaining(verb, now) : 0f;
                verbRecharge[i].fillAmount = recharge > 0f ? 1f - recharge / CyberNetwork.VerbRecharge(verb) : 1f;
            }
        }

        private void WriteIncidents(CyberNetwork network, double now)
        {
            for (int i = 0; i < incidentButtons.Length; i++)
            {
                AvButton button = incidentButtons[i];
                CyberIncident incident = network != null ? network.Incident(i) : default;
                if (incident.Kind == IncidentKind.None)
                {
                    button.SetText("—");
                    button.SetEnabled(false);
                    button.SetLatched(false);
                    button.WithTooltip("SLOT " + (i + 1) + " EMPTY · NO INCIDENT ON THE BOARD");
                    continue;
                }
                bool active = incident.Outcome == IncidentOutcome.Active;
                string where = network.Exists(incident.Site) ? CyberWords.Callsign(network, incident.Site) : "SECTOR";
                string line = CyberWords.IncidentCode(incident.Kind) + " " + where + " · " +
                              (!active ? CyberWords.Outcome(incident.Outcome)
                               : incident.Tracing ? "TRACE " + Mathf.RoundToInt(incident.Trace * 100f) + "%"
                               : incident.Held ? "HELD"
                               : CyberWords.Seconds(incident.Ends - now));
                button.SetText(line);
                button.SetEnabled(active);
                button.SetLatched(active && i == selectedIncident);
                button.WithTooltip(CyberWords.Incident(incident.Kind) + " · " + where + " · " + line +
                    (active ? " — click to select it; TAB cycles the stack." : " · RESOLVED · nothing to select."));
            }
        }
    }
}
