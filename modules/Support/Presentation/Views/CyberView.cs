using System;
using System.Collections.Generic;
using System.Globalization;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LayoutMotion = BoscaliSummer.Features.Support.Domain.Layout.Motion;

namespace BoscaliSummer.Features.Support.Presentation.Views
{
    /// <summary>
    /// CYBER › CONSOLE — a terrain-first spectrum defence workspace. The netmap carries selected
    /// target, reach and route context; the adjacent work pane shows breach progress or defender
    /// reaction; the lower command rail contains incidents, orders and host replies.
    ///
    /// <para>Orders stay on <c>SupportManager.RequestCyber*</c>; the room pre-checks only what the host
    /// re-checks (<c>CyberNetwork.Check</c>, <c>CheckBreach</c>, upgrade level and ops reserve).</para>
    /// </summary>
    internal sealed class CyberView : IOpsView
    {
        private const int Slots = CyberNetwork.SlotCount;
        private const int Incidents = CyberNetwork.IncidentSlots;
        private const int Verbs = CyberNetwork.VerbCount;
        private const int ShellLines = 3;
        private const float StatusHeight = 68f;
        private float ColumnWidth = 240f;
        private bool compact;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly KeyCode[] VerbKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5 };
        private static readonly SupportActionId[] QuickLeaseActions =
            { SupportActionId.HackScan, SupportActionId.HackTrack, SupportActionId.HackBlackout };
        private static readonly string[] Rungs = { "5  NORMAL", "4  VIGILANT", "3  ENHANCED", "2  GREATER", "1  MAXIMUM" };
        private static readonly BreachPhase[] PhaseOrder = { BreachPhase.Probe, BreachPhase.Exploit, BreachPhase.Extract };

        private readonly SupportManager support;
        private readonly string[] log;

        private sealed class Pane
        {
            public RectTransform Root, Body;
            public CanvasGroup Content;
            public TMP_Text Title, Status, Boot;
            public string BootText;
        }

        private sealed class Row
        {
            public RoomControl Control;
            public Image Fill;
            public TMP_Text Text;
            public TMP_Text Right;
        }

        private readonly CyberNetmap map = new CyberNetmap();
        private readonly Pane[] panes = new Pane[4];
        private readonly Rect[] sections = new Rect[5];
        private readonly Row[] upgrades = new Row[4];
        private readonly Row[] verbs = new Row[Verbs];
        private readonly Row[] incidents = new Row[Incidents];
        private readonly Row[] capstones = new Row[3];
        private readonly Row[] quickLeaseRows = new Row[3];
        private readonly SupportActionDefinition[] quickLeaseDefinitions = new SupportActionDefinition[3];
        private readonly Action[] quickLeaseCallbacks = new Action[3];
        private readonly TMP_Text[] rungs = new TMP_Text[5];
        private readonly Image[] rungFills = new Image[5];
        private readonly TMP_Text[] phases = new TMP_Text[3];
        private readonly Image[] phaseFills = new Image[3];
        private readonly Image[] phaseProgress = new Image[3];
        private Image traceFill;
        private float phaseWidth, traceWidth;
        private readonly TMP_Text[] stages = new TMP_Text[CyberLocations.StageCount];
        private readonly TMP_Text[] shell = new TMP_Text[ShellLines];
        private readonly string[] shellText = new string[ShellLines];
        private readonly Color[] shellInk = new Color[ShellLines];
        private readonly Sparkline heat = new Sparkline();

        private Rect hero;
        private CanvasGroup mapFade;
        private Image block;
        private TMP_Text blockDigit, blockLabel, status1, status2, link;
        private TMP_Text compLine, intelLine, upgradeTitle, legend, incidentHeader, incidentEmpty, keys;
        private RectTransform dormantMap;
        private CanvasGroup dormantFade;
        private TMP_Text dormantTitle, dormantAction;
        private Image incidentQuietMarker;
        private TMP_Text targetName, targetKind, targetState, traceLine, stageTitle, capTitle, breachHint;
        private Row advice, quiet, loud, spoof, disconnect;
        private RectTransform locationGroup, homeGroup, capstoneGroup, incidentGroup;
        private Row patchRow, isolateRow, baitRow;
        private readonly Row[] answers = new Row[3];
        private readonly CyberVerb[] answerVerb = new CyberVerb[3];
        private readonly int[] answerTarget = new int[3];
        private TMP_Text incidentDetail;
        private bool incidentPinned;
        private TMP_Text homeDetail, primer;

        private int selectedSite = -1;
        private int selectedIncident = -1;
        private CyberVerb adviceVerb;
        private int adviceTarget = -1;
        private bool awaiting;
        private string lastLog;
        private float nextHeat;
        private float entrance = 1f;
        private bool layoutDue = true;
        private CyberNetwork lastNetwork;

        public CyberView(SupportManager support, string[] log)
        {
            this.support = support;
            this.log = log;
            quickLeaseCallbacks[0] = SendQuickScan;
            quickLeaseCallbacks[1] = SendQuickTrack;
            quickLeaseCallbacks[2] = SendQuickBlackout;
            if (support != null)
                for (int i = 0; i < QuickLeaseActions.Length; i++)
                    foreach (SupportActionDefinition action in support.Actions)
                        if (action.Id == QuickLeaseActions[i]) { quickLeaseDefinitions[i] = action; break; }
            for (int i = 0; i < ShellLines; i++) shellText[i] = "";
        }

        public OpsDomain Domain => OpsDomain.Cyber;
        public float EntranceSeconds => CyberStyle.EntranceSeconds;
        public Rect Hero => hero;
        public IReadOnlyList<Rect> Sections => sections;

        // ---- Build -------------------------------------------------------------------------------

        public void Build(RectTransform room, Rect area)
        {
            CyberStyle.Resolve();
            OpsSprites.Ensure();
            float w = area.width, h = area.height;
            compact = h < 800f || w < 1500f;
            ColumnWidth = compact ? 196f : 240f;
            Image surface = Chrome.Panel(room, new Rect(0f, 0f, w, h), CyberStyle.Surface.WithAlpha(1f));
            surface.raycastTarget = true;

            float top = StatusHeight + CyberStyle.Gutter;
            float bottomH = compact ? 210f : Mathf.Round(h * 0.275f);
            float midH = h - top - bottomH - CyberStyle.Gutter;
            float mapW = Mathf.Round(w * 0.68f);
            float incW = Mathf.Round(w * 0.55f);
            var netRect = new Rect(0f, -top, mapW, midH);
            var breachRect = new Rect(mapW + CyberStyle.Gutter, -top, w - mapW - CyberStyle.Gutter, midH);
            var incRect = new Rect(0f, -(top + midH + CyberStyle.Gutter), incW, bottomH);
            var shellRect = new Rect(incW + CyberStyle.Gutter, -(top + midH + CyberStyle.Gutter), w - incW - CyberStyle.Gutter, bottomH);

            BuildStatus(room, new Rect(0f, 0f, w, StatusHeight));
            panes[0] = BuildPane(room, "THEATER NETWORK", netRect, "NETWORK PICTURE\nHost mirrored · terrain reference\nSelect a node to inspect reach and route.");
            panes[1] = BuildPane(room, "TARGET / RESPONSE", breachRect, "TARGET WORKSPACE\nBreach phase and defender response\nOrders await host confirmation.");
            panes[2] = BuildPane(room, "ACTIVE INCIDENTS", incRect, "THREAT QUEUE\nHost reported events\nSelect an incident for a response.");
            panes[3] = BuildPane(room, "COMMAND RAIL", shellRect, "COMMAND LINK\nOne order at a time\nHost acknowledgement appears here.");

            BuildNetmap(netRect, w, h);
            BuildBreach(panes[1].Body, breachRect.width, breachRect.height - CyberStyle.TitleBar);
            BuildIncidents(panes[2].Body, incRect.width);
            BuildShell(panes[3].Body, shellRect.width, shellRect.height - CyberStyle.TitleBar);

            hero = new Rect(netRect.x, -netRect.y, netRect.width, netRect.height);
            sections[0] = new Rect(0f, 0f, w, StatusHeight);
            sections[1] = hero;
            sections[2] = new Rect(breachRect.x, -breachRect.y, breachRect.width, breachRect.height);
            sections[3] = new Rect(incRect.x, -incRect.y, incRect.width, incRect.height);
            sections[4] = new Rect(shellRect.x, -shellRect.y, shellRect.width, shellRect.height);
            layoutDue = true;
        }

        private void BuildStatus(RectTransform room, Rect at)
        {
            Chrome.Panel(room, at, CyberStyle.Bar);
            block = Chrome.Panel(room, new Rect(0f, 0f, 104f, at.height), AvTheme.RailReady);
            blockLabel = CyberStyle.Line(block.rectTransform, new Rect(0f, -3f, 104f, 14f), CyberStyle.Micro, CyberStyle.Surface,
                TextAlignmentOptions.Center);
            CyberStyle.Type(blockLabel, "INFOCON");
            blockDigit = Chrome.Label(block.rectTransform, "5", new Rect(0f, -12f, 104f, 52f), CyberStyle.Surface, 44f,
                FontStyles.Bold, TextAlignmentOptions.Center);
            status1 = CyberStyle.Line(room, new Rect(122f, -10f, at.width - 122f - 250f, 20f), 14f, CyberStyle.Ink);
            status2 = CyberStyle.Line(room, new Rect(122f, -36f, at.width - 122f - 250f, 18f), CyberStyle.Small, CyberStyle.Dim);
            link = CyberStyle.Line(room, new Rect(at.width - 250f, -10f, 234f, 20f), CyberStyle.Body, CyberStyle.Title,
                TextAlignmentOptions.MidlineRight);
            keys = CyberStyle.Line(room, new Rect(at.width - 250f, -38f, 234f, 18f), CyberStyle.Micro, CyberStyle.Dim,
                TextAlignmentOptions.MidlineRight);
            CyberStyle.Type(keys, "OPS RESERVE · HOST CONFIRMED");
        }

        private Pane BuildPane(RectTransform room, string title, Rect at, string boot)
        {
            var pane = new Pane { BootText = boot };
            var go = new GameObject(title.Trim('[', ']', ' '), typeof(RectTransform));
            pane.Root = (RectTransform)go.transform;
            pane.Root.SetParent(room, false);
            Chrome.Place(pane.Root, at);
            Chrome.Panel(pane.Root, new Rect(0f, 0f, at.width, at.height), CyberStyle.Pane);
            Chrome.Outline(pane.Root, new Rect(0f, 0f, at.width, at.height), CyberStyle.PaneEdge);
            Chrome.Panel(pane.Root, new Rect(1f, -1f, at.width - 2f, CyberStyle.TitleBar), CyberStyle.Bar);
            Chrome.Panel(pane.Root, new Rect(1f, -1f, 3f, CyberStyle.TitleBar), CyberStyle.Accent);
            pane.Title = CyberStyle.Line(pane.Root, new Rect(14f, -1f, Mathf.Max(0f, Mathf.Min(280f, at.width * 0.52f) - 14f), CyberStyle.TitleBar), CyberStyle.Small, CyberStyle.Title);
            pane.Title.fontStyle = FontStyles.Bold;
            CyberStyle.Type(pane.Title, title);
            pane.Status = CyberStyle.Line(pane.Root, new Rect(Mathf.Min(300f, at.width * 0.58f), -1f, Mathf.Max(0f, at.width - Mathf.Min(312f, at.width * 0.60f)), CyberStyle.TitleBar), CyberStyle.Micro,
                CyberStyle.Dim, TextAlignmentOptions.MidlineRight);
            var body = new GameObject("Body", typeof(RectTransform), typeof(CanvasGroup));
            pane.Body = (RectTransform)body.transform;
            pane.Body.SetParent(pane.Root, false);
            Chrome.Place(pane.Body, new Rect(0f, -CyberStyle.TitleBar - 1f, at.width, at.height - CyberStyle.TitleBar - 1f));
            pane.Content = body.GetComponent<CanvasGroup>();
            pane.Boot = CyberStyle.Line(pane.Root, new Rect(12f, -CyberStyle.TitleBar - 8f, at.width - 24f, 54f), CyberStyle.Small,
                CyberStyle.Title, TextAlignmentOptions.TopLeft, true);
            CyberStyle.Type(pane.Boot, boot);
            pane.Boot.gameObject.SetActive(false);
            return pane;
        }

        private void BuildNetmap(Rect netRect, float roomW, float roomH)
        {
            RectTransform body = panes[0].Body;
            float bodyH = netRect.height - CyberStyle.TitleBar - 1f;
            Chrome.Rule(body, new Rect(ColumnWidth, 0f, 1f, bodyH), CyberStyle.PaneEdge);
            CyberStyle.Type(Label(body, new Rect(14f, -8f, ColumnWidth - 28f, 16f), CyberStyle.Micro, CyberStyle.Title), "infocon ladder");
            for (int i = 0; i < 5; i++)
            {
                rungFills[i] = Chrome.Panel(body, new Rect(12f, -28f - i * (compact ? 16f : 20f), ColumnWidth - 24f, compact ? 16f : 18f), CyberStyle.Pane);
                rungs[i] = Label(body, new Rect(20f, -28f - i * (compact ? 16f : 20f), ColumnWidth - 40f, compact ? 16f : 18f), CyberStyle.Body, CyberStyle.Dim);
                CyberStyle.Type(rungs[i], Rungs[i]);
            }
            heat.Build(body, new Rect(14f, compact ? -126f : -152f, ColumnWidth - 28f, compact ? 36f : 64f), CyberStyle.Terminal());
            CyberStyle.Type(Label(body, new Rect(14f, compact ? -112f : -138f, 120f, 14f), CyberStyle.Micro, CyberStyle.Title), "heat · 60 s");
            compLine = Label(body, new Rect(14f, compact ? -170f : -228f, ColumnWidth - 28f, 34f), CyberStyle.Small, CyberStyle.Ink, true);
            intelLine = Label(body, new Rect(14f, compact ? -208f : -266f, ColumnWidth - 28f, 34f), CyberStyle.Small, CyberStyle.Ink, true);
            legend = Label(body, new Rect(ColumnWidth + 12f, -bodyH + 20f, netRect.width - ColumnWidth - 24f, 16f), CyberStyle.Micro,
                CyberStyle.Dim);
            CyberStyle.Type(legend, "SELECT SITE · REACH · ROUTE · LIVE ACCESS · HOSTILE ACTIVITY");
            Row fit = BuildRow(body, new Rect(12f, compact ? -378f : -512f, ColumnWidth - 24f, compact ? 24f : 30f), FitMap);
            CyberStyle.Type(fit.Text, "[H] FIT ALL");
            fit.Control.WithTooltip("Frame every network node. Wheel zooms; drag pans; Q / E selects nodes.");
            var view = new Rect(netRect.x + ColumnWidth + 1f, netRect.y - CyberStyle.TitleBar - 1f, netRect.width - ColumnWidth - 2f,
                bodyH - 26f);
            // The map lives in room coordinates so the board projects straight onto it; its host fades
            // with the netmap pane during the boot entrance.
            var room = (RectTransform)panes[0].Root.parent;
            var hostObject = new GameObject("NetmapHost", typeof(RectTransform), typeof(CanvasGroup));
            var host = (RectTransform)hostObject.transform;
            host.SetParent(room, false);
            Chrome.Place(host, new Rect(0f, 0f, roomW, roomH));
            mapFade = hostObject.GetComponent<CanvasGroup>();
            map.Build(host, view, Select);
            map.Board.Clicked = (local, button) =>
            {
                if (button != UnityEngine.EventSystems.PointerEventData.InputButton.Left) return;
                int hit = map.Board.Hit(local);
                if (hit >= 0) Select(hit);
            };

            // The empty topology is a real network state, not an unlabelled dead map.
            float plateW = Mathf.Min(520f, view.width - 48f);
            const float plateH = 154f;
            var plate = Chrome.Panel(room, new Rect(view.x + (view.width - plateW) * 0.5f,
                view.y - (view.height - plateH) * 0.5f, plateW, plateH), CyberStyle.Pane.WithAlpha(0.96f));
            dormantMap = plate.rectTransform;
            dormantFade = plate.gameObject.AddComponent<CanvasGroup>();
            plate.raycastTarget = false;
            Chrome.Outline(dormantMap, new Rect(0f, 0f, plateW, plateH), CyberStyle.Title.WithAlpha(0.7f));
            Chrome.Rule(dormantMap, new Rect(16f, -28f, plateW - 32f, 1f), CyberStyle.Title.WithAlpha(0.45f));
            CyberStyle.Type(Label(dormantMap, new Rect(18f, -7f, plateW - 36f, 18f), CyberStyle.Micro, CyberStyle.Title),
                "NETWORK TOPOLOGY / CONTROL LINK");
            dormantTitle = Label(dormantMap, new Rect(18f, -42f, plateW - 36f, 38f), 25f, CyberStyle.Ink);
            dormantTitle.fontStyle = FontStyles.Bold;
            dormantAction = Label(dormantMap, new Rect(18f, -91f, plateW - 36f, 50f), CyberStyle.Small, CyberStyle.Dim, true);
        }

        private static TMP_Text Label(RectTransform parent, Rect at, float size, Color color, bool wrap = false) =>
            CyberStyle.Line(parent, at, size, color, wrap ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft, wrap);

        /// <summary>A terminal row: text left, figure right; hover inverts to the title ink.</summary>
        private Row BuildRow(RectTransform parent, Rect at, Action click, bool twoLines = false)
        {
            var row = new Row();
            row.Control = RoomControl.Create(parent, at, click, "Row");
            RectTransform host = row.Control.Rect;
            row.Fill = Chrome.Panel(host, new Rect(0f, 0f, at.width, at.height), CyberStyle.Bar);
            Chrome.Outline(host, new Rect(0f, 0f, at.width, at.height), CyberStyle.PaneEdge);
            row.Text = CyberStyle.Line(host, new Rect(8f, 0f, at.width - 16f, at.height), CyberStyle.Small, CyberStyle.Ink,
                twoLines ? TextAlignmentOptions.MidlineLeft : TextAlignmentOptions.MidlineLeft, twoLines);
            row.Right = CyberStyle.Line(host, new Rect(8f, 0f, at.width - 16f, at.height), CyberStyle.Small, CyberStyle.Ink,
                TextAlignmentOptions.MidlineRight);
            row.Control.Changed = _ => PaintRow(row);
            PaintRow(row);
            return row;
        }

        private static void PaintRow(Row row)
        {
            RoomControl c = row.Control;
            bool hot = c.Enabled && (c.Hovered || c.Latched);
            row.Fill.color = !c.Enabled ? CyberStyle.Pane : c.Pressed ? CyberStyle.Accent.WithAlpha(0.5f)
                : hot ? CyberStyle.Title.WithAlpha(c.Latched ? 0.9f : 0.25f) : CyberStyle.Bar;
            Color ink = !c.Enabled ? CyberStyle.Dim : c.Latched ? CyberStyle.Surface : CyberStyle.Ink;
            row.Text.color = ink;
            row.Right.color = ink;
        }

        private void BuildBreach(RectTransform body, float w, float h)
        {
            advice = BuildRow(body, new Rect(12f, -8f, w - 24f, compact ? 36f : 40f), RunAdvice, true);
            targetName = CyberStyle.Line(body, new Rect(14f, compact ? -52f : -62f, w - 28f, 28f), 22f, CyberStyle.Ink);
            targetName.fontStyle = FontStyles.Bold;
            targetKind = Label(body, new Rect(14f, compact ? -80f : -92f, w - 28f, 16f), CyberStyle.Small, CyberStyle.Dim);
            targetState = Label(body, new Rect(14f, compact ? -98f : -110f, w - 28f, 16f), CyberStyle.Small, CyberStyle.Ink);
            primer = Label(body, new Rect(14f, -140f, w - 28f, 150f), CyberStyle.Small, CyberStyle.Dim, true);

            var locationObject = new GameObject("Location", typeof(RectTransform));
            locationGroup = (RectTransform)locationObject.transform;
            locationGroup.SetParent(body, false);
            Chrome.Place(locationGroup, new Rect(0f, compact ? -112f : -132f, w, h - (compact ? 112f : 132f)));
            CyberStyle.Type(Label(locationGroup, new Rect(14f, 0f, w - 28f, 16f), CyberStyle.Micro, CyberStyle.Title), "process · probe > exploit > extract");
            phaseWidth = (w - 40f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float x = 12f + i * (phaseWidth + 8f);
                phaseFills[i] = Chrome.Panel(locationGroup, new Rect(x, -20f, phaseWidth, compact ? 62f : 80f), CyberStyle.Bar);
                phases[i] = Label(locationGroup, new Rect(x + 8f, -24f, phaseWidth - 16f, 58f), CyberStyle.Small, CyberStyle.Ink, true);
                Chrome.Panel(locationGroup, new Rect(x, compact ? -78f : -96f, phaseWidth, 4f), CyberStyle.Lattice);
                phaseProgress[i] = Chrome.Panel(locationGroup, new Rect(x, compact ? -78f : -96f, 0f, 4f), CyberStyle.Accent);
            }
            traceLine = CyberStyle.Line(locationGroup, new Rect(14f, compact ? -88f : -112f, w - 28f, 28f), 18f, CyberStyle.Ink);
            traceWidth = w - 28f;
            Chrome.Panel(locationGroup, new Rect(14f, compact ? -118f : -146f, traceWidth, 8f), CyberStyle.Lattice);
            traceFill = Chrome.Panel(locationGroup, new Rect(14f, compact ? -118f : -146f, 0f, 8f), CyberStyle.Accent);
            Chrome.Panel(locationGroup, new Rect(14f + traceWidth * 0.7f, compact ? -114f : -142f, 2f, 16f), AvTheme.RailCaution);
            float bw = (w - 32f) / 2f;
            quiet = BuildRow(locationGroup, new Rect(12f, compact ? -136f : -172f, bw, compact ? 44f : 54f), () => Breach(BreachTool.RetuneQuiet), true);
            loud = BuildRow(locationGroup, new Rect(20f + bw, compact ? -136f : -172f, bw, compact ? 44f : 54f), () => Breach(BreachTool.RetuneForce), true);
            spoof = BuildRow(locationGroup, new Rect(12f, compact ? -188f : -234f, bw, 30f), () => Breach(BreachTool.Spoof));
            disconnect = BuildRow(locationGroup, new Rect(20f + bw, compact ? -188f : -234f, bw, 30f), () => Breach(BreachTool.Disconnect));
            quickLeaseRows[0] = quiet;
            quickLeaseRows[1] = loud;
            quickLeaseRows[2] = spoof;
            breachHint = Label(locationGroup, new Rect(14f, compact ? -226f : -280f, w - 28f, 40f), CyberStyle.Small, CyberStyle.Ink, true);
            stageTitle = Label(locationGroup, new Rect(14f, -330f, w - 28f, 18f), CyberStyle.Micro, CyberStyle.Title);
            stageTitle.gameObject.SetActive(!compact);
            CyberStyle.Type(stageTitle, "LIVE ACCESS  ·  ONE EFFECT USE");
            for (int i = 0; i < stages.Length; i++)
                stages[i] = Label(locationGroup, new Rect(14f, compact ? -264f : -356f - i * 22f, w - 28f, compact ? 26f : 20f), CyberStyle.Small, CyberStyle.Dim);

            var capObject = new GameObject("Capstone", typeof(RectTransform));
            capstoneGroup = (RectTransform)capObject.transform;
            capstoneGroup.SetParent(body, false);
            Chrome.Place(capstoneGroup, new Rect(0f, compact ? -112f : -132f, w, h - (compact ? 112f : 132f)));
            capTitle = Label(capstoneGroup, new Rect(14f, 0f, w - 28f, 34f), CyberStyle.Small, CyberStyle.Title, true);
            for (int i = 0; i < capstones.Length; i++)
            {
                Capstone capstone = Capstones.All[i];
                capstones[i] = BuildRow(capstoneGroup, new Rect(12f, -40f - i * 60f, w - 24f, 54f), () => Choose(capstone), true);
            }

            var homeObject = new GameObject("Home", typeof(RectTransform));
            homeGroup = (RectTransform)homeObject.transform;
            homeGroup.SetParent(body, false);
            Chrome.Place(homeGroup, new Rect(0f, compact ? -112f : -132f, w, h - (compact ? 112f : 132f)));
            homeDetail = Label(homeGroup, new Rect(14f, 0f, w - 28f, 48f), CyberStyle.Small, CyberStyle.Ink, true);
            isolateRow = BuildRow(homeGroup, new Rect(12f, -56f, w - 24f, 34f), () => Verb((int)CyberVerb.Isolate));
            patchRow = BuildRow(homeGroup, new Rect(12f, -96f, w - 24f, 34f), () => Verb((int)CyberVerb.Patch));
            baitRow = BuildRow(homeGroup, new Rect(12f, -136f, w - 24f, 34f), () => Verb((int)CyberVerb.Honeypot));

            var incidentObject = new GameObject("Incident", typeof(RectTransform));
            incidentGroup = (RectTransform)incidentObject.transform;
            incidentGroup.SetParent(body, false);
            Chrome.Place(incidentGroup, new Rect(0f, compact ? -112f : -132f, w, h - (compact ? 112f : 132f)));
            incidentDetail = Label(incidentGroup, new Rect(14f, 0f, w - 28f, 48f), CyberStyle.Small, CyberStyle.Ink, true);
            for (int i = 0; i < answers.Length; i++)
            {
                int index = i;
                answers[i] = BuildRow(incidentGroup, new Rect(12f, -56f - i * 44f, w - 24f, i == 0 ? 40f : 36f), () => Answer(index), true);
            }
        }

        /// <summary>Fire one of the incident pane's answers on its own target.</summary>
        private void Answer(int index)
        {
            int target = answerTarget[index];
            if (target < 0) return;
            if (CyberNetwork.TargetsIncident(answerVerb[index])) selectedIncident = target;
            else selectedSite = target;
            Verb((int)answerVerb[index]);
        }

        private void BuildIncidents(RectTransform body, float w)
        {
            incidentHeader = Label(body, new Rect(12f, -6f, w - 24f, 18f), CyberStyle.Small, CyberStyle.Title);
            CyberStyle.Type(incidentHeader, "THREAT / TARGET                                 IMPACT / TRACE");
            for (int i = 0; i < Incidents; i++)
            {
                int index = i;
                incidents[i] = BuildRow(body, new Rect(8f, -28f - i * 34f, w - 16f, 30f), () => SelectIncident(index));
                incidents[i].Control.gameObject.SetActive(false);
            }
            incidentEmpty = Label(body, new Rect(12f, -34f, w - 24f, 60f), CyberStyle.Body, CyberStyle.Dim, true);
            incidentQuietMarker = Chrome.Panel(body, new Rect(12f, -36f, 3f, 52f), CyberStyle.Accent);
            incidentEmpty.rectTransform.anchoredPosition = new Vector2(26f, -34f);
            incidentEmpty.rectTransform.sizeDelta = new Vector2(w - 38f, 60f);
        }

        private void BuildShell(RectTransform body, float w, float h)
        {
            float columnGap = 12f;
            float columnWidth = (w - 24f - columnGap) * 0.5f;
            for (int i = 0; i < ShellLines; i++)
            {
                shell[i] = Label(body, new Rect(12f, -6f - (compact ? 0 : i) * CyberStyle.LinePitch, w - 24f, CyberStyle.LinePitch), CyberStyle.Small, CyberStyle.Ink);
                shell[i].gameObject.SetActive(!compact || i == ShellLines - 1);
            }
            float top = compact ? 27f : 6f + ShellLines * CyberStyle.LinePitch + 4f;
            Chrome.Rule(body, new Rect(8f, -top, w - 16f, 1f), CyberStyle.PaneEdge);
            upgradeTitle = Label(body, new Rect(12f, -(top + 4f), columnWidth, 16f), CyberStyle.Micro, CyberStyle.Title);
            CyberStyle.Type(upgradeTitle, "NETWORK UPGRADES · OPS RESERVE");
            TMP_Text verbTitle = Label(body, new Rect(12f + columnWidth + columnGap, -(top + 4f), columnWidth, 16f), CyberStyle.Micro, CyberStyle.Title);
            CyberStyle.Type(verbTitle, "DEFENDER ORDERS · 1–5");
            float rowHeight = compact ? 20f : 28f;
            float rowStep = compact ? 22f : 31f;
            for (int i = 0; i < upgrades.Length; i++)
            {
                var upgrade = (CyberUpgrade)i;
                upgrades[i] = BuildRow(body, new Rect(8f, -(top + 22f) - i * rowStep, columnWidth + 8f, rowHeight), () => Buy(upgrade), true);
            }
            for (int i = 0; i < Verbs; i++)
            {
                int index = i;
                verbs[i] = BuildRow(body, new Rect(16f + columnWidth + columnGap, -(top + 22f) - i * rowStep, columnWidth, rowHeight), () => Verb(index));
                Chrome.SetSize(verbs[i].Text, CyberStyle.Body);
            }
        }

        // ---- Lifecycle ---------------------------------------------------------------------------

        public void Show(object context)
        {
            if (context is int slot && slot >= 0) selectedSite = slot;
            layoutDue = true;
        }

        public void Hide() => Chrome.ClearTooltip();

        public void Entrance(float progress)
        {
            entrance = Mathf.Clamp01(progress);
            for (int i = 0; i < panes.Length; i++)
            {
                Pane pane = panes[i];
                if (pane == null) continue;
                float p = Mathf.Clamp01((entrance - i * 0.1f) / 0.7f);
                bool booting = p < 1f;
                if (pane.Boot.gameObject.activeSelf != booting) pane.Boot.gameObject.SetActive(booting);
                if (booting) pane.Boot.maxVisibleCharacters = Mathf.RoundToInt(pane.BootText.Length * Mathf.Clamp01(p * 1.6f));
                pane.Content.alpha = Mathf.Clamp01((p - 0.55f) / 0.45f);
                if (i == 0)
                {
                    if (mapFade != null) mapFade.alpha = pane.Content.alpha;
                    if (dormantFade != null) dormantFade.alpha = pane.Content.alpha;
                }
            }
        }

        public void Refresh(double now, float time, bool textTick)
        {
            CyberNetwork network = support?.LocalCyber;
            if (support != null && map.Board.RefreshFrontline(LocalFaction(), time)) map.FrontChanged();
            Paint(network, support != null ? support.OrbitNow : now, time, textTick);
        }

        /// <summary>Paint from a network. The game calls it every frame; the harness with a fixture.</summary>
        internal void Paint(CyberNetwork network, double now, float time, bool textTick)
        {
            lastNetwork = network;
            KeepSelection(network);
            if (awaiting && support != null && !support.CommandPending)
            {
                awaiting = false;
                string reply = support.Status ?? "";
                bool denied = reply.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                              reply.IndexOf("no response", StringComparison.OrdinalIgnoreCase) >= 0;
                Echo("host: " + reply.ToLowerInvariant(), denied ? AvTheme.RailDanger : CyberStyle.Title);
                textTick = true;
            }
            if (time >= nextHeat && network != null)
            {
                nextHeat = time + 1f;
                heat.Push(network.Heat, 0f, CyberNetwork.HeatMaximum, "heat " + Mathf.RoundToInt(network.Heat) + "/100 · " +
                                                                    CyberWords.Phase(network.Phase).ToLowerInvariant());
            }
            if (textTick || layoutDue || map.FrameChanged)
            {
                layoutDue = false;
                map.Layout(network, now, selectedSite, network != null && network.BreachActive ? network.BreachTarget : -1);
                WriteText(network, now);
            }
            map.Animate(network, now, time);
        }

        public bool HandleKeys()
        {
            CyberNetwork network = support?.LocalCyber;
            for (int i = 0; i < VerbKeys.Length; i++)
                if (Input.GetKeyDown(VerbKeys[i])) { Verb(i); return true; }
            if (Input.GetKeyDown(KeyCode.Space)) { RunAdvice(); return true; }
            if (Input.GetKeyDown(KeyCode.Tab)) { CycleIncident(network); return true; }
            if (Input.GetKeyDown(KeyCode.Q)) { CycleNode(network, -1); return true; }
            if (Input.GetKeyDown(KeyCode.E)) { CycleNode(network, 1); return true; }
            if (Input.GetKeyDown(KeyCode.G)) { Breach(BreachTool.RetuneQuiet); return true; }
            if (Input.GetKeyDown(KeyCode.F)) { Breach(BreachTool.RetuneForce); return true; }
            if (Input.GetKeyDown(KeyCode.X)) { Breach(BreachTool.Spoof); return true; }
            if (Input.GetKeyDown(KeyCode.C)) { Breach(BreachTool.Disconnect); return true; }
            if (Input.GetKeyDown(KeyCode.H)) { FitMap(); return true; }
            return false;
        }

        /// <summary>Right-click inside is "back": drop the selection, then the zoom.</summary>
        public void RightClickInside()
        {
            if (selectedSite >= 0 || selectedIncident >= 0)
            {
                selectedSite = -1;
                selectedIncident = -1;
                layoutDue = true;
                return;
            }
            FitMap();
        }

        private void FitMap()
        {
            map.Board.ResetFraming();
            layoutDue = true;
        }

        // ---- Selection ---------------------------------------------------------------------------

        private void Select(int slot)
        {
            selectedSite = slot;
            incidentPinned = false;
            layoutDue = true;
        }

        private void SelectIncident(int index)
        {
            CyberNetwork network = lastNetwork ?? support?.LocalCyber;
            if (network == null || !network.IncidentActive(index)) return;
            selectedIncident = index;
            incidentPinned = true;
            int site = network.Incident(index).Site;
            if (network.Exists(site)) selectedSite = site;
            layoutDue = true;
        }

        private void KeepSelection(CyberNetwork network)
        {
            if (network == null) return;
            if (!network.Exists(selectedSite)) selectedSite = -1;
            if (network.IncidentActive(selectedIncident)) return;
            selectedIncident = -1;
            incidentPinned = false;
            for (int i = 0; i < Incidents; i++)
            {
                if (!network.IncidentActive(i)) continue;
                selectedIncident = i;
                break;
            }
        }

        private void CycleIncident(CyberNetwork network)
        {
            if (network == null) return;
            for (int step = 1; step <= Incidents; step++)
            {
                int index = (Mathf.Max(selectedIncident, -1) + step + Incidents) % Incidents;
                if (!network.IncidentActive(index)) continue;
                SelectIncident(index);
                return;
            }
        }

        private void CycleNode(CyberNetwork network, int direction)
        {
            if (network == null) return;
            int start = selectedSite < 0 ? (direction > 0 ? -1 : 0) : selectedSite;
            for (int step = 1; step <= Slots; step++)
            {
                int slot = ((start + direction * step) % Slots + Slots) % Slots;
                if (!network.Exists(slot)) continue;
                Select(slot);
                return;
            }
        }

        // ---- Orders ------------------------------------------------------------------------------

        private void RunAdvice()
        {
            CyberNetwork network = support?.LocalCyber;
            if (network != null && network.BreachActive)
            {
                Select(network.BreachTarget);
                if (network.BreachTrace >= 0.7f)
                {
                    Breach(network.SpoofRechargeRemaining(support.OrbitNow) <= 0f && network.Computing >= CyberLocations.SpoofCost
                        ? BreachTool.Spoof : BreachTool.RetuneQuiet);
                    return;
                }
                Breach(network.BreachQuiet ? BreachTool.RetuneForce : BreachTool.RetuneQuiet);
                return;
            }
            if (adviceTarget < 0)
            {
                Error("nothing to run · the network is holding");
                return;
            }
            if (CyberNetwork.TargetsIncident(adviceVerb)) SelectIncident(adviceTarget);
            else Select(adviceTarget);
            Verb((int)adviceVerb);
        }

        private void Verb(int index)
        {
            CyberNetwork network = support?.LocalCyber;
            if (network == null || index < 0 || index >= Verbs) return;
            var verb = (CyberVerb)index;
            int target = CyberNetwork.TargetsIncident(verb) ? network.Incident(selectedIncident).Id : selectedSite;
            if (support.CommandPending)
            {
                Error("busy · one order at a time, wait for the host");
                return;
            }
            CyberDenial denial = network.Check(verb, target, support.OrbitNow);
            if (denial != CyberDenial.None)
            {
                Error(CyberWords.Verb(verb).ToLowerInvariant() + ": refused · " + CyberWords.Denial(denial).ToLowerInvariant());
                return;
            }
            support.RequestCyberVerb(verb, target);
            Sent("> " + VerbCommand(verb) + " " + Target(network, verb, target).ToLowerInvariant());
        }

        private void Breach(BreachTool tool)
        {
            CyberNetwork network = support?.LocalCyber;
            if (network == null) return;
            if (support.CommandPending)
            {
                Error("busy · one order at a time, wait for the host");
                return;
            }
            if (network.BreachActive) Select(network.BreachTarget);
            if (tool == BreachTool.RetuneQuiet || tool == BreachTool.RetuneForce)
            {
                bool quietMode = tool == BreachTool.RetuneQuiet;
                if (!network.BreachActive)
                {
                    StartBreach(network, quietMode);
                    return;
                }
                support.RequestCyberBreach(network.BreachTarget, tool);
                Sent("> retune --" + (quietMode ? "quiet" : "loud"));
                return;
            }
            if (!network.BreachActive)
            {
                Error((tool == BreachTool.Spoof ? "spoof" : "disconnect") + ": no breach running");
                return;
            }
            if (tool == BreachTool.Spoof && network.SpoofRechargeRemaining(support.OrbitNow) > 0f)
            {
                Error("spoof: recharging " + CyberWords.Seconds(network.SpoofRechargeRemaining(support.OrbitNow)).ToLowerInvariant());
                return;
            }
            support.RequestCyberBreach(network.BreachTarget, tool);
            Sent(tool == BreachTool.Spoof ? "> spoof --trace" : "> disconnect");
        }

        private void StartBreach(CyberNetwork network, bool quietMode)
        {
            int slot = selectedSite;
            if (slot < CyberNetwork.TargetBase)
            {
                Error("breach: select a location on the netmap first");
                return;
            }
            BreachDenial denial = network.CheckBreach(slot, support.OrbitNow);
            if (denial == BreachDenial.None && network.Computing + 0.001f < CyberNetwork.PhaseCost(BreachPhase.Probe, 1, quietMode))
                denial = BreachDenial.LowComputing;
            if (denial != BreachDenial.None)
            {
                Error("breach: refused · " + CyberWords.Refusal(denial).ToLowerInvariant());
                return;
            }
            support.RequestCyberBreach(slot, quietMode ? BreachTool.Quiet : BreachTool.Force);
            Sent("> breach " + CyberWords.Callsign(network, slot).ToLowerInvariant() + (quietMode ? " --quiet" : " --loud"));
        }

        private void Choose(Capstone capstone)
        {
            CyberNetwork network = support?.LocalCyber;
            if (network == null || !network.BreachAwaitingChoice) return;
            if (support.CommandPending)
            {
                Error("busy · one order at a time, wait for the host");
                return;
            }
            support.RequestCyberChoice(capstone);
            Sent("> capstone " + Capstones.Code(capstone).ToLowerInvariant());
        }

        private void Buy(CyberUpgrade upgrade)
        {
            CyberNetwork network = support?.LocalCyber;
            if (network == null) return;
            if (support.CommandPending)
            {
                Error("busy · one order at a time, wait for the host");
                return;
            }
            if (!network.CanUpgrade(upgrade))
            {
                Error("upgrade: " + CyberLocations.UpgradeName(upgrade).ToLowerInvariant() + " is at maximum");
                return;
            }
            float cost = support.CyberUpgradeCost(upgrade);
            if (!support.BypassRequirements && support.LocalOpsReserve + 0.001f < cost)
            {
                Error("upgrade: needs " + Figure(cost) + " ops reserve");
                return;
            }
            support.RequestCyberUpgrade(upgrade);
            Sent("> upgrade " + CyberLocations.UpgradeName(upgrade).ToLowerInvariant().Replace(' ', '-'));
        }

        private static string VerbCommand(CyberVerb verb)
        {
            switch (verb)
            {
                case CyberVerb.Isolate: return "isolate";
                case CyberVerb.Patch: return "patch";
                case CyberVerb.Honeypot: return "honeypot";
                case CyberVerb.Trace: return "trace";
                default: return "burn";
            }
        }

        private void Sent(string command)
        {
            Echo(command, CyberStyle.Ink);
            awaiting = true;
            layoutDue = true;
        }

        private void Error(string line)
        {
            Echo("error: " + line, AvTheme.RailDanger);
            awaiting = false;
            layoutDue = true;
        }

        /// <summary>Append a line to the shell's ring (newest at the bottom, like a terminal).</summary>
        private void Echo(string line, Color ink)
        {
            for (int i = 0; i < ShellLines - 1; i++)
            {
                shellText[i] = shellText[i + 1];
                shellInk[i] = shellInk[i + 1];
            }
            shellText[ShellLines - 1] = line ?? "";
            shellInk[ShellLines - 1] = ink;
        }

        private string Target(CyberNetwork network, CyberVerb verb, int target) =>
            CyberNetwork.TargetsIncident(verb)
                ? CyberWords.Incident(network.Incident(network.IncidentSlot((byte)target)).Kind) + " · " + Origin(network.Incident(network.IncidentSlot((byte)target)).Origin)
                : CyberWords.Callsign(network, target);

        private string Origin(byte origin) => support != null ? support.CyberOriginName(origin) : "ADVERSARY";

        // ---- Text --------------------------------------------------------------------------------

        private void WriteText(CyberNetwork network, double now)
        {
            bool built = network != null && network.HasCommand;
            int infocon = built ? network.Infocon : 5;
            Color tone = infocon >= 5 ? AvTheme.RailReady : infocon >= 3 ? AvTheme.RailCaution : AvTheme.RailDanger;
            block.color = built ? tone : CyberStyle.Dim;
            string digit = built ? infocon.ToString(Invariant) : "-";
            if (blockDigit.text != digit) blockDigit.text = digit;

            string enabled = support != null && !support.CyberEnabled ? "CYBER OPERATIONS DISABLED BY THE HOST" : null;
            if (network == null)
            {
                CyberStyle.Type(status1, "AEGIS-NET · NO LINK · AWAITING THEATER DATA");
                CyberStyle.Type(status2, enabled ?? "the host has not sent the network yet");
            }
            else
            {
                CyberStyle.Type(status1, "AEGIS-NET · " + (built ? CyberWords.Infocon(infocon) : "NO C2") + " · HEAT " +
                                         Mathf.RoundToInt(network.Heat) + "/100 · COMP " + Mathf.FloorToInt(network.Computing) + "/" +
                                         Mathf.RoundToInt(network.ComputingCapacity()) + " +" +
                                         network.ComputingIncome().ToString("0.#", Invariant) + "/s · INTEL " +
                                         Mathf.FloorToInt(network.Intel) + "/" + Mathf.RoundToInt(network.IntelCapacity()));
                CyberStats stats = network.Stats();
                int home = network.Count(NodeKind.Command) + network.Count(NodeKind.Base);
                int active = network.ActiveIncidents(IncidentKind.None);
                string reserve = support != null ? "OPS RESERVE " + Figure(support.LocalOpsReserve) : "OPS RESERVE —";
                float lease = network.AccessRemaining(now);
                string leaseText = lease > 0f ? " · ACCESS " + CyberWords.Callsign(network, network.AccessSlot) + " " + CyberWords.Seconds(lease) + " · ONE EFFECT"
                    : " · NO LIVE ACCESS";
                CyberStyle.Type(status2, enabled ?? (reserve + " · " + CyberWords.Phase(network.Phase).ToLowerInvariant() + " · " + home + " home" + leaseText + " · " + active +
                                                     (active == 1 ? " incident" : " incidents") + " · " +
                                                     (network.NextIncident > now ? "next move ~" + TheaterGrid.Clock(network.NextIncident - now) : "adversary idle") +
                                                     (network.ExposedUntil > now ? " · EXPOSED " + CyberWords.Seconds(network.ExposedUntil - now) : "")));
            }
            bool pending = support != null && support.CommandPending;
            bool fresh = support != null && support.OpsStateFresh;
            CyberStyle.Type(link, pending ? "[ ORDER PENDING · HOST ]" : fresh ? "[ HOST LINK CONFIRMED ]" : "[ SYNCING · ORDERS WAIT ]");
            link.color = pending ? AvTheme.RailCaution : fresh ? CyberStyle.Title : AvTheme.RailDanger;

            WriteNetmap(network, built, infocon, tone);
            WriteBreach(network, now, built);
            WriteIncidents(network, now);
            WriteShell(network, now);
        }

        private void WriteNetmap(CyberNetwork network, bool built, int infocon, Color tone)
        {
            if (dormantMap.gameObject.activeSelf != !built) dormantMap.gameObject.SetActive(!built);
            if (!built)
            {
                bool disabled = support != null && !support.CyberEnabled;
                CyberStyle.Type(dormantTitle, disabled ? "OPERATIONS DISABLED" : network == null ? "LINK PENDING" : "NO COMMAND NODE");
                CyberStyle.Type(dormantAction, disabled ? "The host has disabled cyber operations for this theater."
                    : network == null ? "Waiting for the host to send network state."
                    : "Hold an airbase to establish cyber command.\nThe network comes online when the host confirms control.");
            }
            float mpp = map.Board.MetresPerPixel;
            CyberStyle.Type(panes[0].Status, Mathf.RoundToInt(map.Board.View.width * mpp / 1000f) + " km across · north up · wheel zoom · drag pan · [h] fit" +
                (map.TerrainAvailable ? "" : " · terrain unavailable"));
            for (int i = 0; i < 5; i++)
            {
                bool current = built && 5 - i == infocon;
                rungFills[i].color = current ? tone : CyberStyle.Pane;
                rungs[i].color = current ? CyberStyle.Surface : CyberStyle.Dim;
            }
            if (network == null)
            {
                CyberStyle.Type(compLine, "comp   —");
                CyberStyle.Type(intelLine, "intel  —");
            }
            else
            {
                float comp = network.Computing, compCap = network.ComputingCapacity();
                float intel = network.Intel, intelCap = network.IntelCapacity();
                CyberStyle.Type(compLine, "comp  " + Mathf.FloorToInt(comp) + "/" + Mathf.RoundToInt(compCap) + "  +" +
                                          network.ComputingIncome().ToString("0.#", Invariant) + "/s\n" + CyberStyle.Bar10(comp / Mathf.Max(1f, compCap)));
                CyberStyle.Type(intelLine, "intel " + Mathf.FloorToInt(intel) + "/" + Mathf.RoundToInt(intelCap) + "  +" +
                                           network.IntelIncome().ToString("0.#", Invariant) + "/s\n" + CyberStyle.Bar10(intel / Mathf.Max(1f, intelCap)));
            }
            float reserve = support != null ? support.LocalOpsReserve : 0f;
            CyberStyle.Type(upgradeTitle, "upgrades · ops reserve " + Figure(reserve));
            bool pending = support != null && support.CommandPending;
            for (int i = 0; i < upgrades.Length; i++)
            {
                var upgrade = (CyberUpgrade)i;
                Row row = upgrades[i];
                int level = network != null ? network.UpgradeLevel(upgrade) : 0;
                bool can = network != null && network.CanUpgrade(upgrade);
                float cost = support != null ? support.CyberUpgradeCost(upgrade) : 0f;
                bool afford = support == null || support.BypassRequirements || reserve + 0.001f >= cost;
                row.Control.SetEnabled(can && afford && !pending);
                CyberStyle.Type(row.Text, CyberLocations.UpgradeName(upgrade).ToLowerInvariant() + "  " + level + "/" + CyberLocations.UpgradeLevels);
                CyberStyle.Type(row.Right, can ? "[ " + Figure(cost) + " ]" : "[ max ]");
                row.Text.rectTransform.sizeDelta = new Vector2(row.Control.Rect.sizeDelta.x * 0.68f - 16f, row.Control.Rect.sizeDelta.y);
                row.Right.color = can && !afford ? AvTheme.RailDanger : row.Text.color;
                row.Control.WithTooltip(CyberLocations.UpgradeName(upgrade) + " — " + CyberLocations.UpgradeEffect(upgrade) +
                                        " per level. " + (can ? afford ? "Buy the next level for " + Figure(cost) + " ops reserve."
                                            : "Needs " + Figure(cost) + " ops reserve; you have " + Figure(reserve) + "."
                                            : "At maximum."));
            }
        }

        private void WriteBreach(CyberNetwork network, double now, bool built)
        {
            string adviceWords = Here(CyberWords.Advice(network, now, out adviceVerb, out adviceTarget));
            bool breaching = built && network.BreachActive;
            bool pending = support != null && support.CommandPending;
            bool actionable = !breaching && adviceTarget >= 0 && network != null &&
                              network.Check(adviceVerb, adviceTarget, now) == CyberDenial.None && !pending;
            advice.Control.SetEnabled(breaching ? !pending : actionable);
            CyberStyle.Type(advice.Text, breaching
                ? network.BreachTrace >= 0.7f ? "[SPACE] " + (network.SpoofRechargeRemaining(now) <= 0f && network.Computing >= CyberLocations.SpoofCost
                    ? "SPOOF TRACE  ·  warning 70% / lockout 100%" : "GO QUIET  ·  warning 70% / lockout 100%")
                    : "[SPACE] SWITCH " + (network.BreachQuiet ? "TO LOUD  ·  faster, higher trace" : "TO QUIET  ·  slower, lower trace")
                : (actionable ? "[SPACE] " : "NEXT MOVE  ·  ") + adviceWords.ToLowerInvariant());
            CyberStyle.Type(advice.Right, "");
            advice.Control.WithTooltip(breaching ? "Retune the running breach." : actionable
                ? "Run the advised " + CyberWords.Verb(adviceVerb) + " on " + Target(network, adviceVerb, adviceTarget) + "."
                : "Nothing to run right now.");

            bool choice = built && network.BreachAwaitingChoice;
            bool incidentMode = !choice && incidentPinned && network != null && network.IncidentActive(selectedIncident);
            int slot = choice ? network.ChoiceTarget : incidentMode ? network.Incident(selectedIncident).Site : network != null && network.Exists(selectedSite)
                ? selectedSite : breaching ? network.BreachTarget : -1;
            bool exists = network != null && network.Exists(slot);
            CyberNode node = exists ? network.Node(slot) : default;
            bool location = exists && !node.Static && !incidentMode;
            bool home = exists && node.Static && !incidentMode;
            if (locationGroup.gameObject.activeSelf != (location && !choice)) locationGroup.gameObject.SetActive(location && !choice);
            if (capstoneGroup.gameObject.activeSelf != choice) capstoneGroup.gameObject.SetActive(choice);
            if (homeGroup.gameObject.activeSelf != (home && !choice)) homeGroup.gameObject.SetActive(home && !choice);
            if (incidentGroup.gameObject.activeSelf != incidentMode) incidentGroup.gameObject.SetActive(incidentMode);

            int threatenedHome = support != null ? support.LocalCyberThreatSlot : -1;
            CyberStyle.Type(panes[1].Status, threatenedHome >= 0
                ? "HOME NODE UNDER BREACH · SELECT " + CyberWords.Callsign(network, threatenedHome) + " · ISOLATE TO DELAY"
                : breaching ? "session open · " + CyberWords.PhaseOf(network.BreachPhase).ToLowerInvariant()
                : choice ? "capstone pending" : exists ? "selected · q e step" : "no target · click a node");
            CyberStyle.Type(primer, exists || choice ? "" :
                "how this terminal works\n\n" +
                "1  choose a real city or airfield in reach. QUIET takes ~21 s; LOUD takes ~12 s and raises trace faster.\n" +
                "2  a completed breach opens one 75 s lease. Choose one supported map effect; host acceptance consumes the lease.\n" +
                "3  defend from your home ear: isolate to delay, patch compromised nodes, or bait intrusions. Host confirms every order.");
            if (!exists)
            {
                CyberStyle.Type(targetName, "no target");
                CyberStyle.Type(targetKind, network == null ? "awaiting the network" : "click a node on the netmap, or q / e to step");
                CyberStyle.Type(targetState, "");
            }
            else
            {
                CyberStyle.Type(targetName, CyberWords.Callsign(network, slot).ToLowerInvariant());
                string kind = node.Static || node.Hacked ? CyberLocations.NodeName(node.Kind)
                    : CyberWords.Kind(CyberLocations.LocationOf(node.Kind)) + " · not yours";
                CyberStyle.Type(targetKind, kind.ToLowerInvariant() +
                                            (node.Hacked ? " · ACCESS " + CyberWords.Seconds(network.AccessRemaining(now)) + " · ONE EFFECT" : "") +
                                            " · " + TheaterGrid.Kilometres(node.X, node.Z).ToLowerInvariant());
                string state = CyberWords.NodeState(network, slot, now);
                bool session = network.BreachActive && network.BreachTarget == slot;
                CyberStyle.Type(targetState, "state: " + state.ToLowerInvariant() +
                                             (session ? " · breach open, " + CyberWords.PhaseOf(network.BreachPhase).ToLowerInvariant() : ""));
                targetState.color = node.Compromised || (node.Static && node.Down) ? AvTheme.RailDanger
                    : node.Isolated ? CyberStyle.Dim : network.Working(slot) ? AvTheme.RailReady : CyberStyle.Ink;
            }

            if (incidentMode)
            {
                CyberStyle.Type(panes[1].Status, "incident · recommended answer first");
                WriteIncident(network, selectedIncident, now, pending);
            }
            if (choice) WriteCapstones(network, now, pending);
            else if (incidentMode) { }
            else if (location) WriteLocation(network, slot, node, now, breaching, pending);
            else if (home) WriteHome(network, slot, node, now, pending);
        }

        private void WriteLocation(CyberNetwork network, int slot, CyberNode node, double now, bool breaching, bool pending)
        {
            bool mine = breaching && network.BreachTarget == slot;
            int stage = network.Stage(slot);
            bool quietMode = !mine || network.BreachQuiet;
            for (int i = 0; i < 3; i++)
            {
                BreachPhase phase = PhaseOrder[i];
                float seconds = CyberNetwork.PhaseSeconds(phase, quietMode);
                float cost = CyberNetwork.PhaseCost(phase, stage + 1, quietMode);
                string state;
                float fill;
                if (!mine) { state = "queued"; fill = 0f; }
                else if (network.BreachPhase > phase) { state = "done"; fill = 1f; }
                else if (network.BreachPhase == phase)
                {
                    float remaining = network.BreachPhaseRemaining(now);
                    state = "run " + CyberWords.Seconds(remaining).ToLowerInvariant();
                    fill = seconds > 0f ? 1f - remaining / seconds : 1f;
                }
                else { state = "queued"; fill = 0f; }
                bool running = mine && network.BreachPhase == phase;
                CyberStyle.Type(phases[i], (i + 1) + "  " + CyberWords.PhaseOf(phase).ToUpperInvariant() + "\n" +
                                           state.ToUpperInvariant() + "\n" + Mathf.RoundToInt(cost) + " comp · " + Mathf.RoundToInt(seconds) + "s");
                phases[i].color = running ? CyberStyle.Title : CyberStyle.Ink;
                phaseFills[i].color = running ? CyberStyle.Title.WithAlpha(0.12f) : CyberStyle.Bar;
                phaseProgress[i].rectTransform.sizeDelta = new Vector2(phaseWidth * Mathf.Clamp01(fill), 4f);
            }
            bool liveLeaseHere = network.AccessRemaining(now) > 0f && network.AccessSlot == slot;
            if (liveLeaseHere && !mine)
            {
                string lease = CyberWords.Seconds(network.AccessRemaining(now));
                CyberStyle.Type(phases[0], "ACCESS\nLIVE\n" + lease);
                CyberStyle.Type(phases[1], "ONE\nEFFECT\nLEFT");
                CyberStyle.Type(phases[2], "CENTER\n" + CyberWords.Callsign(network, slot) + "\nHOST CONFIRMS");
                for (int i = 0; i < phases.Length; i++) phases[i].color = CyberStyle.Title;
            }
            float trace = mine ? network.BreachTrace : 0f;
            CyberStyle.Type(traceLine, "TRACE  " + Mathf.RoundToInt(trace * 100f) + "%" +
                                       (trace >= 0.7f ? "  /  HIGH TRACE" : mine ? "  /  " + (network.BreachQuiet ? "QUIET" : "LOUD") : "  /  NOT CONNECTED"));
            traceLine.color = trace > 0.7f ? AvTheme.RailDanger : trace > 0.4f ? AvTheme.RailCaution : CyberStyle.Ink;
            traceFill.color = traceLine.color;
            traceFill.rectTransform.sizeDelta = new Vector2(traceWidth * Mathf.Clamp01(trace), 8f);

            BreachDenial denial = network.CheckBreach(slot, now);
            bool canStart = !breaching && denial == BreachDenial.None && !pending;
            SetRow(quiet, "[G] QUIET / LOW TRACE" + ModePreview(stage + 1, true), mine ? !pending : canStart, mine && network.BreachQuiet,
                mine ? "Run the next phase quiet: slower, but the trace barely moves." : "Open a breach here, quiet: slow and low trace.");
            SetRow(loud, "[F] LOUD / HIGH TRACE" + ModePreview(stage + 1, false), mine ? !pending : canStart &&
                network.Computing + 0.001f >= CyberNetwork.PhaseCost(BreachPhase.Probe, 1, false), mine && !network.BreachQuiet,
                mine ? "Run the next phase loud: fast, but the trace climbs hard." : "Open a breach here, loud: fast and loud.");
            float spoofLeft = network.SpoofRechargeRemaining(now);
            SetRow(spoof, spoofLeft > 0f ? "[X] SPOOF · " + CyberWords.Seconds(spoofLeft) : "[X] SPOOF · " + (int)CyberLocations.SpoofCost + " comp", mine && spoofLeft <= 0f && network.Computing >= CyberLocations.SpoofCost && !pending,
                false, "Burn " + (int)CyberLocations.SpoofCost + " computing to knock the trace back.");
            SetRow(disconnect, "[c] disconnect", mine && !pending, false, "Leave the session safely. Nothing is taken, nothing is traced.");
            CyberStyle.Type(breachHint, mine
                ? "Mode changes trace now; next phase uses its time / cost. 100% trace locks you out. Spoof lowers trace; disconnect ends this attempt."
                : breaching ? "another breach is running · one session at a time."
                : denial == BreachDenial.None ? "in reach · complete one breach to open a 75 s, one-effect access lease."
                : "cannot breach · " + CyberWords.Refusal(denial).ToLowerInvariant());
            breachHint.color = mine || denial == BreachDenial.None ? CyberStyle.Dim : AvTheme.RailCaution;
            bool showQuick = liveLeaseHere && !mine;
            stageTitle.gameObject.SetActive(!compact || showQuick);
            CyberStyle.Type(stageTitle, showQuick ? "QUICK SEND · CENTERED ON LEASE SITE" : "ACCESS MODEL");
            for (int i = 0; i < stages.Length; i++)
            {
                bool show = i == 0;
                if (stages[i].gameObject.activeSelf != show) stages[i].gameObject.SetActive(show);
                CyberStyle.Type(stages[i], showQuick
                    ? "75S LEASE · FIRST HOST-ACCEPTED EFFECT CONSUMES IT"
                    : "NO PERSISTENT CAPTURE · CHOOSE QUIET OR LOUD FOR ONE SORTIE WINDOW");
                stages[i].color = showQuick ? CyberStyle.Accent : CyberStyle.Dim;
            }
            if (showQuick) WriteQuickLeaseRows(network, slot, now);
            else RestoreBreachRows();
        }

        private void WriteQuickLeaseRows(CyberNetwork network, int slot, double now)
        {
            float width = locationGroup.rect.width - 24f;
            float top = compact ? -136f : -172f;
            float step = compact ? 34f : 38f;
            for (int i = 0; i < quickLeaseRows.Length; i++)
            {
                Row row = quickLeaseRows[i];
                Chrome.Place(row.Control.Rect, new Rect(12f, top - i * step, width, compact ? 30f : 34f));
                SupportActionDefinition action = quickLeaseDefinitions[i];
                if (action == null)
                {
                    SetRow(row, "[" + (i + 1) + "] UNAVAILABLE", false, false, "This server does not provide this effect.");
                    row.Right.text = "—";
                    continue;
                }
                AbilityFacts facts = AbilityStatus.For(support, action, support.BypassRequirements);
                row.Control.SetAction(quickLeaseCallbacks[i]);
                row.Control.SetEnabled(facts.Enabled && support.OpsStateFresh && !support.CommandPending);
                row.Control.SetLatched(false);
                CyberStyle.Type(row.Text, "[" + (i + 1) + "] " + action.Name);
                row.Text.rectTransform.sizeDelta = new Vector2(width * 0.68f, row.Control.Rect.sizeDelta.y);
                CyberStyle.Type(row.Right, facts.Enabled ? facts.CostText + " · 1 USE" : "WAIT · " + facts.CostText);
                row.Control.WithTooltip(action.Name + " at " + CyberWords.Callsign(network, slot) +
                    " — " + action.Description + " Cost " + facts.CostText + ". " + facts.Readiness +
                    ". First accepted effect closes the " +
                    CyberWords.Seconds(network.AccessRemaining(now)) + " access window.");
                PaintRow(row);
            }
            if (disconnect.Control.gameObject.activeSelf) disconnect.Control.gameObject.SetActive(false);
        }

        private void RestoreBreachRows()
        {
            float bw = (locationGroup.rect.width - 32f) / 2f;
            Chrome.Place(quiet.Control.Rect, new Rect(12f, compact ? -136f : -172f, bw, compact ? 44f : 54f));
            Chrome.Place(loud.Control.Rect, new Rect(20f + bw, compact ? -136f : -172f, bw, compact ? 44f : 54f));
            Chrome.Place(spoof.Control.Rect, new Rect(12f, compact ? -188f : -234f, bw, 30f));
            Chrome.Place(disconnect.Control.Rect, new Rect(20f + bw, compact ? -188f : -234f, bw, 30f));
            quiet.Control.SetAction(() => Breach(BreachTool.RetuneQuiet));
            loud.Control.SetAction(() => Breach(BreachTool.RetuneForce));
            spoof.Control.SetAction(() => Breach(BreachTool.Spoof));
            disconnect.Control.SetAction(() => Breach(BreachTool.Disconnect));
            quiet.Text.rectTransform.sizeDelta = new Vector2(bw - 16f, quiet.Control.Rect.sizeDelta.y);
            loud.Text.rectTransform.sizeDelta = new Vector2(bw - 16f, loud.Control.Rect.sizeDelta.y);
            spoof.Text.rectTransform.sizeDelta = new Vector2(bw - 16f, spoof.Control.Rect.sizeDelta.y);
            quiet.Right.text = loud.Right.text = spoof.Right.text = disconnect.Right.text = "";
            if (!disconnect.Control.gameObject.activeSelf) disconnect.Control.gameObject.SetActive(true);
        }

        private void SendQuickLease(int index)
        {
            if (support == null || index < 0 || index >= quickLeaseDefinitions.Length) return;
            SupportActionDefinition action = quickLeaseDefinitions[index];
            CyberNetwork network = support.LocalCyber;
            int slot = network != null ? network.AccessSlot : -1;
            if (action == null || network == null || slot < CyberNetwork.TargetBase) return;
            CyberNode node = network.Node(slot);
            if (support.ArmedAction.HasValue) support.Arm(support.ArmedAction.Value);
            support.RequestAt(action.Id, new GlobalPosition(node.X, 0f, node.Z));
            Sent("> " + action.Name + " @ " + CyberWords.Callsign(network, slot));
            layoutDue = true;
        }

        private void SendQuickScan() => SendQuickLease(0);
        private void SendQuickTrack() => SendQuickLease(1);
        private void SendQuickBlackout() => SendQuickLease(2);

        private static string ModePreview(int stage, bool quietMode)
        {
            float seconds = 0f, cost = 0f;
            foreach (BreachPhase phase in PhaseOrder)
            {
                seconds += CyberNetwork.PhaseSeconds(phase, quietMode);
                cost += CyberNetwork.PhaseCost(phase, stage, quietMode);
            }
            return "\nCycle: " + Mathf.RoundToInt(seconds) + "s · " + Mathf.RoundToInt(cost) + " comp";
        }

        private void WriteHome(CyberNetwork network, int slot, CyberNode node, double now, bool pending)
        {
            CyberStyle.Type(homeDetail, support != null && support.LocalCyberThreatSlot == slot
                ? "HOSTILE BREACH IN PROGRESS · ISOLATE TO DELAY THEIR PHASE · YOUR NODE GOES OFFLINE"
                : node.Down ? "anchor building destroyed · the node returns when it is repaired"
                : "home node · online · network reach " + Mathf.RoundToInt(network.Reach / 1000f) + " km from here" +
                  (slot == network.CommandSlot ? " · cyber command: abilities stop if it is breached" : ""));
            WriteVerbRow(isolateRow, network, CyberVerb.Isolate, slot, now, pending);
            WriteVerbRow(patchRow, network, CyberVerb.Patch, slot, now, pending);
            WriteVerbRow(baitRow, network, CyberVerb.Honeypot, slot, now, pending);
        }

        private void WriteVerbRow(Row row, CyberNetwork network, CyberVerb verb, int target, double now, bool pending)
        {
            CyberDenial denial = network.Check(verb, target, now);
            bool rejoin = verb == CyberVerb.Isolate && network.Exists(target) && network.Node(target).Isolated;
            float teamLeft = verb == CyberVerb.Isolate && !rejoin && support != null
                ? support.TeamCooldownRemaining(TeamGate.Isolate) : 0f;
            SetRow(row, "[" + ((int)verb + 1) + "] " + (rejoin ? "rejoin" : VerbCommand(verb)),
                denial == CyberDenial.None && !pending && teamLeft <= 0.5f, false,
                CyberWords.Verb(verb) + " — " + CyberWords.VerbHelp(verb));
            CyberStyle.Type(row.Right, (rejoin ? "free" : Mathf.RoundToInt(CyberNetwork.VerbCost(verb)) + " comp") + " · " +
                                       (teamLeft > 0.5f ? "team t-" + Mathf.CeilToInt(teamLeft) + "s"
                                           : CyberWords.Denial(denial).ToLowerInvariant()));
        }

        /// <summary>The selected incident: what it is, then its countermeasures, the recommended one first.</summary>
        private void WriteIncident(CyberNetwork network, int index, double now, bool pending)
        {
            CyberIncident incident = network.Incident(index);
            string where = network.Exists(incident.Site) ? CyberWords.Callsign(network, incident.Site) : "SECTOR";
            CyberStyle.Type(targetName, CyberWords.Incident(incident.Kind).ToLowerInvariant());
            CyberStyle.Type(targetKind, "on " + where.ToLowerInvariant() + " · from " + Origin(incident.Origin).ToLowerInvariant());
            CyberStyle.Type(targetState, incident.Held ? "held in bait · tracing faster"
                : "impact in " + CyberWords.Seconds(incident.Ends - now).ToLowerInvariant() +
                  (CyberNetwork.Traceable(incident.Kind) ? " · trace " + Mathf.RoundToInt(incident.Trace * 100f) + "%" : ""));
            targetState.color = AvTheme.RailDanger;
            int site = network.Exists(incident.Site) ? incident.Site : -1;
            int count = 0;
            switch (incident.Kind)
            {
                case IncidentKind.Intrusion:
                    count = Offer(count, CyberVerb.Isolate, site);
                    count = Offer(count, CyberVerb.Trace, index);
                    count = Offer(count, CyberVerb.Honeypot, site);
                    CyberStyle.Type(incidentDetail, "an intrusion hops the network toward cyber command. isolate its current node to stall and contain it; trace the source.");
                    break;
                case IncidentKind.HostileOperation:
                    count = Offer(count, CyberVerb.Trace, index);
                    CyberStyle.Type(incidentDetail, "an enemy operation was heard. trace it to identify and track its source for a short window.");
                    break;
                case IncidentKind.Raid:
                    count = Offer(count, CyberVerb.BurnThrough, index);
                    CyberStyle.Type(incidentDetail, "a jamming raid halves ability radius inside its sector. burn through it with a hacked location inside or beside it.");
                    break;
                default:
                    count = Offer(count, CyberVerb.Honeypot, site);
                    CyberStyle.Type(incidentDetail, "a recon probe maps your network. bait the node it touches, or let it pass: probes raise heat, they do no damage.");
                    break;
            }
            for (int i = 0; i < answers.Length; i++)
            {
                bool show = i < count;
                if (answers[i].Control.gameObject.activeSelf != show) answers[i].Control.gameObject.SetActive(show);
                if (!show) continue;
                CyberVerb verb = answerVerb[i];
                int target = answerTarget[i];
                CyberDenial denial = target >= 0 ? network.Check(verb, CyberNetwork.TargetsIncident(verb) ? network.Incident(target).Id : target, now) : CyberDenial.NoTarget;
                string on = target < 0 ? "<none>" : CyberNetwork.TargetsIncident(verb) ? "this incident" : CyberWords.Callsign(network, target).ToLowerInvariant();
                SetRow(answers[i], (i == 0 ? "recommended  " : "or           ") + "[" + ((int)verb + 1) + "] " + VerbCommand(verb) + " " + on,
                    denial == CyberDenial.None && !pending, i == 0 && denial == CyberDenial.None, CyberWords.Verb(verb) + " — " + CyberWords.VerbHelp(verb));
                CyberStyle.Type(answers[i].Right, Mathf.RoundToInt(CyberNetwork.VerbCost(verb)) + " comp · " + CyberWords.Denial(denial).ToLowerInvariant());
            }
        }

        private int Offer(int count, CyberVerb verb, int target)
        {
            if (count >= answers.Length) return count;
            answerVerb[count] = verb;
            answerTarget[count] = target;
            return count + 1;
        }

        private void WriteCapstones(CyberNetwork network, double now, bool pending)
        {
            CyberStyle.Type(capTitle, "temporary access · choose one optional payload (auto reveal in " +
                                      CyberWords.Seconds(network.ChoiceRemaining(now)).ToLowerInvariant() + ")");
            for (int i = 0; i < capstones.Length; i++)
            {
                Capstone capstone = Capstones.All[i];
                SetRow(capstones[i], "[" + Capstones.Code(capstone).ToLowerInvariant() + "] " + Capstones.Name(capstone).ToLowerInvariant() +
                                     "\n" + Capstones.Summary(capstone).ToLowerInvariant(), !pending, false,
                    Capstones.Name(capstone) + " — " + Capstones.Summary(capstone));
                CyberStyle.Type(capstones[i].Right, "");
            }
        }

        private static void SetRow(Row row, string text, bool enabled, bool latched, string tip)
        {
            CyberStyle.Type(row.Text, text);
            row.Control.SetEnabled(enabled);
            row.Control.SetLatched(latched);
            row.Control.WithTooltip(tip);
            PaintRow(row);
        }

        private void WriteIncidents(CyberNetwork network, double now)
        {
            int active = 0;
            float rowHeight = compact ? 18f : 30f;
            float rowStep = Mathf.Min(compact ? 18f : 34f,
                (panes[2].Body.sizeDelta.y - 28f - rowHeight) / Mathf.Max(1, Incidents - 1));
            for (int i = 0; i < Incidents; i++)
            {
                Row row = incidents[i];
                bool live = network != null && network.IncidentActive(i);
                if (row.Control.gameObject.activeSelf != live) row.Control.gameObject.SetActive(live);
                if (!live) continue;
                Vector2 at = row.Control.Rect.anchoredPosition;
                row.Control.Rect.anchoredPosition = new Vector2(at.x, -28f - active * rowStep);
                row.Control.Rect.sizeDelta = new Vector2(row.Control.Rect.sizeDelta.x, rowHeight);
                row.Fill.rectTransform.sizeDelta = row.Control.Rect.sizeDelta;
                row.Text.rectTransform.sizeDelta = new Vector2(row.Control.Rect.sizeDelta.x * 0.58f - 16f, rowHeight);
                row.Right.rectTransform.sizeDelta = new Vector2(row.Control.Rect.sizeDelta.x - 16f, rowHeight);
                active++;
                CyberIncident incident = network.Incident(i);
                string where = network.Exists(incident.Site) ? CyberWords.Callsign(network, incident.Site) : "SECTOR";
                double left = incident.Ends - now;
                double span = Math.Max(1.0, incident.Ends - incident.Started);
                string impact = incident.Held ? "HELD" : CyberWords.Seconds(left);
                string trace = CyberNetwork.Traceable(incident.Kind) ? Mathf.RoundToInt(incident.Trace * 100f) + "%" : "—";
                CyberStyle.Type(row.Text, CyberWords.IncidentCode(incident.Kind) + "  " + where.ToLowerInvariant());
                CyberStyle.Type(row.Right, impact + " · " + (incident.Tracing ? "TRACING " : "TRACE ") + trace);
                row.Control.SetLatched(i == selectedIncident);
                row.Control.WithTooltip(CyberWords.Incident(incident.Kind) + " on " + where + " from " + Origin(incident.Origin) +
                                        ". Click to select it; Tab cycles; the breach pane shows the recommended countermeasure.");
                PaintRow(row);
            }
            CyberStyle.Type(panes[2].Status, active + " active · tab cycles");
            bool built = network != null && network.HasCommand;
            if (incidentQuietMarker.enabled != (active == 0)) incidentQuietMarker.enabled = active == 0;
            incidentQuietMarker.color = built ? AvTheme.RailReady : CyberStyle.Dim;
            CyberStyle.Type(incidentEmpty, active > 0 ? "" : built
                ? "WATCH FLOOR QUIET  /  0 ACTIVE\nBreach a real site for one short access window; defenders can counter it."
                : network == null ? "WATCH FLOOR WAITING  /  HOST LINK PENDING" : "WATCH FLOOR OFFLINE  /  NO CYBER COMMAND");
        }

        private void WriteShell(CyberNetwork network, double now)
        {
            string newest = log != null && log.Length > 0 ? log[0] : null;
            if (!string.IsNullOrEmpty(newest) && newest != lastLog)
            {
                if (lastLog == null)
                {
                    // First sight: the last few lines of the watch-floor log, oldest first.
                    for (int i = Math.Min(3, log.Length) - 1; i >= 0; i--)
                        if (!string.IsNullOrEmpty(log[i])) Echo("log: " + log[i].ToLowerInvariant(), CyberStyle.Dim);
                }
                else Echo("log: " + newest.ToLowerInvariant(), CyberStyle.Dim);
                lastLog = newest;
            }
            bool pending = support != null && support.CommandPending;
            CyberStyle.Type(panes[3].Status, pending ? "awaiting host" : "host link ok");
            for (int i = 0; i < ShellLines; i++)
            {
                CyberStyle.Type(shell[i], shellText[i]);
                shell[i].color = shellText[i].Length > 0 ? shellInk[i] : CyberStyle.Dim;
            }
            if (Blank()) CyberStyle.Type(shell[ShellLines - 1], "> _   orders echo here · the host answers below them");
            for (int i = 0; i < Verbs; i++)
            {
                var verb = (CyberVerb)i;
                Row row = verbs[i];
                int target = network != null && CyberNetwork.TargetsIncident(verb) ? network.Incident(selectedIncident).Id : selectedSite;
                CyberDenial denial = network != null ? network.Check(verb, target, now) : CyberDenial.NoCommand;
                bool rejoin = verb == CyberVerb.Isolate && network != null && network.Exists(target) && network.Node(target).Isolated;
                float teamLeft = verb == CyberVerb.Isolate && !rejoin && support != null
                    ? support.TeamCooldownRemaining(TeamGate.Isolate) : 0f;
                // The active target is already named in the response pane; keep the action rail legible
                // at 1280px by avoiding a second, potentially long callsign in each narrow row.
                CyberStyle.Type(row.Text, "[" + (i + 1) + "] " + VerbCommand(verb).ToUpperInvariant());
                CyberStyle.Type(row.Right, Mathf.RoundToInt(CyberNetwork.VerbCost(verb)) + " comp · " +
                    (teamLeft > 0.5f ? "team t-" + Mathf.CeilToInt(teamLeft) + "s" : CyberWords.Denial(denial).ToLowerInvariant()));
                row.Text.rectTransform.sizeDelta = new Vector2(row.Control.Rect.sizeDelta.x * 0.51f - 16f, row.Control.Rect.sizeDelta.y);
                row.Control.SetEnabled(denial == CyberDenial.None && !pending && teamLeft <= 0.5f);
                row.Control.WithTooltip(CyberWords.Verb(verb) + " — " + CyberWords.VerbHelp(verb));
            }
        }

        private bool Blank()
        {
            for (int i = 0; i < ShellLines; i++)
                if (shellText[i].Length > 0) return false;
            return true;
        }

        /// <summary>The shared advice names the console; inside the console that becomes "here" (C6).</summary>
        private static string Here(string advice)
        {
            if (string.IsNullOrEmpty(advice)) return "";
            return advice.Replace("OPEN THE CONSOLE AND ", "").Replace(" IN THE CONSOLE", " HERE").Replace(" FROM THE CONSOLE", " HERE");
        }

        private static int LocalFaction()
        {
            GameManager.GetLocalPlayer<Player>(out Player player);
            return player != null && player.HQ != null ? player.HQ.GetInstanceID() : 0;
        }

        internal CyberNetmap Map => map;

        /// <summary>Offline fixtures: select a node or an incident.</summary>
        internal void Select(int slot, int incident)
        {
            selectedSite = slot;
            selectedIncident = incident;
            incidentPinned = incident >= 0;
            layoutDue = true;
        }

        private static string Figure(float value) => Mathf.Round(value).ToString("N0", Invariant);
    }
}

