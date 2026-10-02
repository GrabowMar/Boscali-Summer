using NOAvionics;
using System;
using System.Collections.Generic;
using System.Globalization;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Layout;
using BoscaliSummer.Modules.Support.Domain.SpecOps;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using BoscaliSummer.Modules.Support.Presentation.Window;
using BoscaliSummer.Modules.Support.Runtime;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LayoutMotion = BoscaliSummer.Modules.Support.Domain.Layout.Motion;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    /// <summary>
    /// SPEC OPS mission control: a portrait roster, a clipped terrain map, the objective
    /// dossier, field reports and a five-order control rail. Controls request host orders.
    ///
    /// <para>Three decisions — objective, team, mission — and watching the teams work. Every
    /// control is a host request (<c>RequestSpecOps*</c>); the desk pre-checks only what the host
    /// re-checks and shows the host's own words when it answers.</para>
    /// </summary>
    internal sealed class DeskView : IOpsView
    {
        private const int TeamCount = SpecOpsDetachment.TeamCount;
        private const int MissionCount = FieldCatalog.MissionCount;
        private const int LogLines = 4;
        private const float TimelineWindow = 600f;
        private const int LaneSegments = 6;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly KeyCode[] MissionKeys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4 };

        private readonly SupportManager support;
        private readonly string[] log;

        // ---- Widgets (room-owned skins) --------------------------------------------------------

        private sealed class Stamp
        {
            public RoomControl Control;
            public Image Fill, Frame;
            public CommandStepGraphic Step;
            public TMP_Text Text;
            public bool Danger, Primary;
        }

        private sealed class DogTag
        {
            public RectTransform Root;
            public CanvasGroup Group;
            public RoomControl Select;
            public Image Body;
            public Image[] Outline;
            public TMP_Text Letter, Callsign, Rank, State, Clock, Detail;
            public Image StateFrame, Bar, BarTrack, LostFrame, SelectedRail;
            public Image[] Chevrons;
            public Stamp Action, Secondary;
        }

        private sealed class Sheet
        {
            public RectTransform Root;
            public RoomControl Hover;
            public Image Paper, MissionRail;
            public Image[] Odds;
            public Image[] Time;
            public TMP_Text Title, Cost, Effect, OddsText, TimeText, Post, Refusal, Dispatched;
            public Image DispatchedFrame;
            public Stamp Launch;
        }

        private sealed class Chip
        {
            public RoomControl Control;
            public Image Fill;
            public Image[] Outline;
            public TMP_Text Text;
        }

        private sealed class Lane
        {
            public TMP_Text Name, Empty;
            public Image[] Segments;
            public TMP_Text[] Words;
        }

        private readonly DeskMap map = new DeskMap();
        private readonly DogTag[] tags = new DogTag[TeamCount];
        private readonly Sheet[] sheets = new Sheet[MissionCount];
        private readonly Chip[] chips = new Chip[TeamCount];
        private readonly Lane[] lanes = new Lane[TeamCount];
        private readonly TMP_Text[] logLines = new TMP_Text[LogLines];
        private readonly LaneSegment[] segments = new LaneSegment[8];
        private readonly Rect[] sections = new Rect[7];
        private readonly float[] homeXs = new float[DeskMap.Homes];
        private readonly float[] homeZs = new float[DeskMap.Homes];
        private readonly string[] homeNames = new string[DeskMap.Homes];
        private readonly Airbase[] homeBases = new Airbase[DeskMap.Homes];

        private Rect focus;
        private Rect folderRect;
        private RectTransform headerGroup, rosterGroup, folderGroup, timelineGroup, noteGroup;
        private CanvasGroup headerFade, folderFade, timelineFade, noteFade;
        private TMP_Text summary, dtg, link, alloc;
        private TMP_Text objectiveName, objectiveLine, threatFigure, threatWords, radarFigure, radarWords, scoutFigure, scoutWords,
            teamOn, folderIndex, folderEmpty;
        private Image objectiveGlyph;
        private OpsSiteImage siteImage;
        private TMP_Text marketStatus;
        private RectTransform folderBody;
        private TMP_Text note, planObjective, planTeam, planMission;
        private RectTransform emptyNote;
        private TMP_Text emptyTitle, emptyBody;
        private RectTransform operationGroup;
        private TMP_Text operationTitle, operationReport, operationResult, operationClock;
        private readonly TMP_Text[] operationMetrics = new TMP_Text[3];
        private readonly Image[] operationBars = new Image[3];
        private readonly Stamp[] operationOrders = new Stamp[5];
        private readonly Stamp[] marketOrders = new Stamp[2];
        private static readonly SpecOpsDirective[] FieldOrders = { SpecOpsDirective.Observe, SpecOpsDirective.Advance,
            SpecOpsDirective.Conceal, SpecOpsDirective.Execute, SpecOpsDirective.Extract };
        private float operationBarWidth;
        private float laneX0, laneWidth;

        // ---- State -----------------------------------------------------------------------------

        private int selectedTeam;
        private int selectedAnchor;
        private bool anchorChosen;
        private int hoverMission = -1;
        private string noteText = "DESK · SELECT AN OBJECTIVE, ASSIGN A TEAM, THEN PREVIEW AN ORDER";
        private bool noteBad;
        private bool awaiting;
        private bool confirmArmed;
        private int confirmTeam = -1;
        private int confirmMission = -1;
        private int confirmAnchor;
        private bool confirmRecall;
        private double confirmUntil;
        private const float ConfirmSeconds = 8f;
        private float entrance = 1f;
        private int homeCount;
        private float nextHomes;
        private bool layoutDue = true;
        private SpecOpsDetachment lastDetachment;

        public DeskView(SupportManager support, string[] log)
        {
            this.support = support;
            this.log = log;
        }

        public OpsDomain Domain => OpsDomain.SpecialOperations;
        public float EntranceSeconds => DeskStyle.EntranceSeconds;
        public Rect Hero => ToTopDown(focus);
        public IReadOnlyList<Rect> Sections => sections;

        // ---- Build -------------------------------------------------------------------------------

        public void Build(RectTransform room, Rect area)
        {
            homePositions.Clear();
            DeskStyle.Resolve();
            DeskStyle.ForgetTyped();
            OpsSprites.Ensure();
            float w = area.width, h = area.height, g = DeskStyle.Gutter;
            float top = 120f;
            float commandTop = h - 76f;
            float rosterW = Mathf.Clamp(w * 0.22f, 340f, 420f);
            float rosterHeight = 418f;
            float timelineTop = top + rosterHeight + 12f;
            float folderW = Mathf.Clamp(w * 0.30f, 548f, 630f);
            float folderX = w - g - folderW;
            float noteTop = commandTop - 54f;
            float mapX = g + rosterW + 12f;
            focus = new Rect(mapX, -16f, folderX - mapX - 12f, noteTop - 28f);
            folderRect = new Rect(folderX, -16f, folderW, commandTop - 28f);

            Chrome.Panel(room, new Rect(0f, 0f, w, h), DeskStyle.Map);
            map.Build(room, focus, focus, SelectSlot);
            map.Board.Clicked = (local, button) =>
            {
                // A click that missed a token still selects the objective under it.
                int hit = map.Board.Hit(local);
                if (hit >= 0) SelectSlot(hit);
            };

            BuildHeader(room, rosterW + g * 2f);
            BuildRoster(room, new Rect(g, -top, rosterW, rosterHeight));
            commandGroup = Group(room, "FieldCommandRail", new Rect(g, -commandTop, w - g * 2f, 58f), out _);
            BuildFolder(room);
            BuildTimeline(room, new Rect(g, -timelineTop, rosterW, commandTop - timelineTop - 12f));
            BuildNote(room, new Rect(focus.x, -noteTop, focus.width, 42f));
            BuildEmpty(room);
            map.SetObstacles(new Rect(focus.x, focus.y - focus.height + 42f, focus.width, 42f));

            sections[0] = new Rect(0f, 0f, rosterW + g * 2f, top - 10f);
            sections[1] = ToTopDown(new Rect(g, -top, rosterW, rosterHeight));
            sections[2] = ToTopDown(focus);
            sections[3] = ToTopDown(new Rect(focus.x, -noteTop, focus.width, 42f));
            sections[4] = ToTopDown(folderRect);
            sections[5] = ToTopDown(new Rect(g, -timelineTop, rosterW, commandTop - timelineTop - 12f));
            sections[6] = new Rect(g, commandTop, w - g * 2f, 58f);
            layoutDue = true;
        }

        private static Rect ToTopDown(Rect avKit) => new Rect(avKit.x, -avKit.y, avKit.width, avKit.height);

        private static RectTransform Group(RectTransform parent, string name, Rect at, out CanvasGroup fade)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Chrome.Place(rect, at);
            fade = go.GetComponent<CanvasGroup>();
            return rect;
        }

        private void BuildHeader(RectTransform room, float w)
        {
            headerGroup = Group(room, "DetachmentIdentity", new Rect(0f, 0f, w, 112f), out headerFade);
            Chrome.Lead(DeskStyle.Title(headerGroup, "DETACHMENT", new Rect(18f, -6f, w - 36f, 48f), 42f, DeskStyle.Ink), AvIcon.UsersGroup, 28f);
            DeskStyle.Title(headerGroup, "FORWARD OPERATIONS COMMAND", new Rect(20f, -52f, w - 40f, 20f), 14f, DeskStyle.Ink);
            Chrome.Outline(headerGroup, new Rect(18f, -78f, w - 36f, 25f), DeskStyle.Khaki.WithAlpha(0.5f));
            DeskStyle.Title(headerGroup, "RECON / DIRECT ACTION / ESPIONAGE", new Rect(26f, -79f, w - 52f, 23f), 12f, RoomPaint.Instrument);
        }

        private void BuildRoster(RectTransform room, Rect at)
        {
            rosterGroup = Group(room, "Roster", at, out _);
            Chrome.Lead(DeskStyle.Title(rosterGroup, "// TEAMS", new Rect(2f, 0f, at.width - 4f, 17f),
                12f, DeskStyle.Ink), AvIcon.UsersGroup);
            float cardHeight = (at.height - 30f - DeskStyle.TagGap * 3f) / TeamCount;
            for (int i = 0; i < TeamCount; i++)
                tags[i] = BuildTag(i, new Rect(0f, -26f - i * (cardHeight + DeskStyle.TagGap), at.width, cardHeight));
        }

        private DogTag BuildTag(int team, Rect at)
        {
            var tag = new DogTag();
            tag.Root = Group(rosterGroup, "Tag" + team, at, out tag.Group);
            tag.Root.pivot = new Vector2(0f, 1f);
            float w = at.width, h = at.height;
            tag.Select = RoomControl.Create(tag.Root, new Rect(0f, 0f, w, h), () => SelectTeam(team), "SelectTeam");
            tag.Select.WithTooltip(FieldWords.Callsign(team) + " — select this team for the next mission (Tab cycles).");
            RoomPaint.Inset(tag.Root, new Rect(0f, 0f, w, h), DeskStyle.Paper, DeskStyle.Khaki);
            tag.Body = Chrome.Panel(tag.Root, new Rect(1f, -3f, w - 2f, h - 4f), DeskStyle.Paper);
            tag.Outline = OutlineThick(tag.Root, new Rect(-3f, 3f, w + 6f, h + 6f), DeskStyle.Ink, 2f);
            tag.SelectedRail = Chrome.Rule(tag.Root, new Rect(0f, 0f, w, 3f), RoomPaint.Instrument);
            OpsArtwork.Draw(tag.Root, new Rect(8f, -5f, 56f, 56f), 4 + team);
            tag.Letter = DeskStyle.Title(tag.Root, FieldWords.Callsign(team).Substring(0, 1), new Rect(72f, -5f, 26f, 27f), 25f,
                DeskStyle.Ink, TextAlignmentOptions.Center);
            tag.Letter.characterSpacing = 0f;
            tag.Callsign = DeskStyle.Title(tag.Root, FieldWords.Callsign(team), new Rect(104f, -5f, w - 116f, 24f), 15f,
                DeskStyle.Ink);
            tag.Chevrons = new Image[FieldCatalog.MaxRank];
            for (int i = 0; i < tag.Chevrons.Length; i++)
            {
                tag.Chevrons[i] = Chrome.Panel(tag.Root, new Rect(8f + i * 11f, -29f, 9f, 9f), DeskStyle.Ink);
                tag.Chevrons[i].sprite = OpsSprites.Glyph(OpsSprites.G.Team);
            }
            tag.Rank = DeskStyle.Body(tag.Root, new Rect(76f, -28f, w - 90f, 17f), 11f, DeskStyle.Khaki);
            tag.StateFrame = Chrome.Panel(tag.Root, new Rect(76f, -46f, 104f, 18f), DeskStyle.Ink, OpsSprites.Stamp);
            tag.State = Chrome.Label(tag.Root, "", new Rect(76f, -46f, 104f, 18f), DeskStyle.Ink, 11f, FontStyles.Bold,
                TextAlignmentOptions.Center);
            tag.State.characterSpacing = 1f;
            tag.Clock = DeskStyle.Body(tag.Root, new Rect(186f, -46f, w - 198f, 18f), 12f, DeskStyle.Ink, TextAlignmentOptions.MidlineRight);
            tag.Detail = DeskStyle.Body(tag.Root, new Rect(0f, 0f, 1f, 1f), 12f, DeskStyle.Khaki);
            tag.Detail.gameObject.SetActive(false);
            tag.BarTrack = Chrome.Panel(tag.Root, new Rect(8f, -h + 31f, w - 16f, 3f), DeskStyle.Khaki.WithAlpha(0.3f));
            tag.Bar = Chrome.Panel(tag.Root, new Rect(8f, -h + 31f, 0f, 3f), DeskStyle.Ink);
            tag.Action = BuildStamp(tag.Root, new Rect(8f, -h + 26f, w - 16f, 23f), () => PrimaryTeamAction(team), 10f);
            tag.Secondary = BuildStamp(tag.Root, new Rect(8f + (w - 24f) * 0.5f + 8f, -h + 26f,
                (w - 24f) * 0.5f, 23f), () => Directive(team, SpecOpsDirective.Extract), 10f);
            tag.Secondary.Control.Rect.gameObject.SetActive(false);
            tag.LostFrame = Chrome.Panel(tag.Root, new Rect(w * 0.5f - 56f, -h * 0.5f + 16f, 112f, 30f), AvTheme.RailDanger, OpsSprites.Stamp);
            DeskStyle.Title(tag.LostFrame.rectTransform, "LOST", new Rect(0f, 0f, 112f, 30f), 18f, AvTheme.RailDanger,
                TextAlignmentOptions.Center);
            tag.LostFrame.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            tag.LostFrame.rectTransform.anchoredPosition = new Vector2(w * 0.5f, -h * 0.5f + 4f);
            tag.LostFrame.rectTransform.localEulerAngles = Vector3.zero;
            tag.LostFrame.gameObject.SetActive(false);
            return tag;
        }

        private static Image[] OutlineThick(RectTransform parent, Rect at, Color color, float thickness)
        {
            var edges = new[]
            {
                Chrome.Rule(parent, new Rect(at.x, at.y, at.width, thickness), color),
                Chrome.Rule(parent, new Rect(at.x, at.y - at.height + thickness, at.width, thickness), color),
                Chrome.Rule(parent, new Rect(at.x, at.y, thickness, at.height), color),
                Chrome.Rule(parent, new Rect(at.x + at.width - thickness, at.y, thickness, at.height), color)
            };
            return edges;
        }

        private Stamp BuildStamp(RectTransform parent, Rect at, Action click, float size)
        {
            var stamp = new Stamp();
            stamp.Control = RoomControl.Create(parent, at, click, "Stamp");
            RectTransform host = stamp.Control.Rect;
            stamp.Fill = Chrome.Panel(host, new Rect(0f, 0f, at.width, at.height), DeskStyle.Tape.WithAlpha(0f));
            stamp.Frame = Chrome.Panel(host, new Rect(0f, 0f, at.width, at.height), DeskStyle.Ink, OpsSprites.Stamp);
            stamp.Text = Chrome.Label(host, "", new Rect(4f, 0f, at.width - 8f, at.height), DeskStyle.Ink, size,
                FontStyles.Bold, TextAlignmentOptions.Center);
            stamp.Text.characterSpacing = 1f;
            stamp.Control.Changed = _ => PaintStamp(stamp);
            PaintStamp(stamp);
            return stamp;
        }

        private static void PaintStamp(Stamp stamp)
        {
            RoomControl c = stamp.Control;
            Color ink = !c.Enabled ? DeskStyle.Khaki.WithAlpha(0.75f)
                : stamp.Danger ? DeskStyle.Stamp : stamp.Primary ? DeskStyle.Stamp : DeskStyle.Ink;
            stamp.Frame.color = ink;
            stamp.Text.color = stamp.Primary && c.Enabled ? DeskStyle.Map : ink;
            stamp.Fill.color = !c.Enabled ? DeskStyle.Khaki.WithAlpha(0.08f)
                : stamp.Primary ? DeskStyle.Stamp.WithAlpha(c.Pressed ? 0.75f : c.Hovered ? 1f : 0.9f)
                : c.Pressed ? DeskStyle.Tape : c.Hovered ? DeskStyle.Tape.WithAlpha(0.7f) : DeskStyle.Tape.WithAlpha(0.18f);
            if (stamp.Step != null) stamp.Step.FillColor = stamp.Fill.color;
        }

        private static void SetStamp(Stamp stamp, string text, bool enabled, bool danger, string tip)
        {
            if (stamp.Text.text != text) stamp.Text.text = text;
            bool repaint = stamp.Danger != danger;
            stamp.Danger = danger;
            stamp.Control.SetEnabled(enabled);
            stamp.Control.WithTooltip(tip);
            if (repaint) PaintStamp(stamp);
        }

        private static void PlaceStamp(Stamp stamp, Rect at)
        {
            Chrome.Place(stamp.Control.Rect, at);
            Chrome.Place(stamp.Fill.rectTransform, new Rect(0f, 0f, at.width, at.height));
            Chrome.Place(stamp.Frame.rectTransform, new Rect(0f, 0f, at.width, at.height));
            Chrome.Place(stamp.Text.rectTransform, new Rect(4f, 0f, at.width - 8f, at.height));
        }

        private void BuildFolder(RectTransform room)
        {
            folderGroup = Group(room, "Folder", folderRect, out folderFade);
            float w = folderRect.width, h = folderRect.height;
            Chrome.Panel(folderGroup, new Rect(0f, 0f, w, h), DeskStyle.Khaki.WithAlpha(0.3f));
            Chrome.Panel(folderGroup, new Rect(0f, 0f, w, h), DeskStyle.Paper);
            Chrome.Rule(folderGroup, new Rect(0f, 0f, w, 2f), RoomPaint.Instrument);
            Chrome.Rule(folderGroup, new Rect(0f, 0f, 3f, h), RoomPaint.Instrument.WithAlpha(0.7f));
            RoomPaint.Brackets(folderGroup, new Rect(0f, 0f, w, h), 10f, RoomPaint.Instrument);
            Chrome.Lead(DeskStyle.Title(folderGroup, "// 01 OBJECTIVE", new Rect(18f, -5f, 160f, 20f), 12f, DeskStyle.Ink), AvIcon.MapPin);
            Stamp previous = BuildStamp(folderGroup, new Rect(180f, -4f, 40f, 24f), () => StepObjective(lastDetachment, -1), 11f);
            SetStamp(previous, "< Q", true, false, "Previous objective");
            Stamp next = BuildStamp(folderGroup, new Rect(226f, -4f, 40f, 24f), () => StepObjective(lastDetachment, 1), 11f);
            SetStamp(next, "E >", true, false, "Next objective");
            folderIndex = DeskStyle.Body(folderGroup, new Rect(w - 200f, -8f, 184f, 16f), DeskStyle.TypewriterSmall, DeskStyle.Khaki,
                TextAlignmentOptions.MidlineRight);

            // The objective and all four mission decisions share one compact board.
            // Taller windows reveal timing and post effects inside each row without a second page.
            float summaryH = 254f;
            float rowH = Mathf.Max(58f, (h - summaryH - 48f) / MissionCount);
            float briefHeight = summaryH + (rowH + 4f) * MissionCount;
            RectTransform content = Chrome.Scroll(folderGroup, new Rect(0f, -32f, w, h - 32f), briefHeight, out Rect contentArea);
            float bodyW = contentArea.width;
            folderBody = (RectTransform)new GameObject("Brief", typeof(RectTransform)).transform;
            folderBody.SetParent(content, false);
            Chrome.Place(folderBody, new Rect(0f, content == folderGroup ? -32f : 0f, bodyW, briefHeight));
            objectiveGlyph = Chrome.Panel(folderBody, new Rect(bodyW - 38f, -116f, 22f, 22f), DeskStyle.Stamp);
            objectiveName = DeskStyle.Title(folderBody, "", new Rect(178f, -8f, bodyW - 190f, 54f), 18f, DeskStyle.Ink);
            objectiveName.characterSpacing = 1f;
            objectiveName.enableWordWrapping = true;
            siteImage = new OpsSiteImage(folderBody, new Rect(12f, -8f, 150f, 106f), true);
            DeskStyle.Title(folderBody, "AREA IMAGE / 12 KM", new Rect(178f, -66f, bodyW - 190f, 18f), 11f, DeskStyle.Khaki);
            objectiveLine = DeskStyle.Body(folderBody, new Rect(178f, -92f, bodyW - 190f, 23f), 11f, DeskStyle.Khaki);
            float factW = (bodyW - 40f) / 3f;
            BuildFact(folderBody, "THREAT / 2 KM", new Rect(12f, -134f, factW, 56f), out threatFigure, out threatWords);
            BuildFact(folderBody, "RADARS", new Rect(20f + factW, -134f, factW, 56f), out radarFigure, out radarWords);
            BuildFact(folderBody, "SCOUTING", new Rect(28f + factW * 2f, -134f, factW, 56f), out scoutFigure, out scoutWords);
            teamOn = DeskStyle.Body(folderBody, new Rect(12f, -204f, bodyW - 24f, 18f), 11f, DeskStyle.Ink);
            float chipW = (bodyW - 42f) / TeamCount;
            for (int i = 0; i < TeamCount; i++)
                chips[i] = BuildChip(i, new Rect(12f + i * (chipW + 6f), -227f, chipW, 22f));
            for (int i = 0; i < MissionCount; i++)
                sheets[i] = BuildSheet((FieldMission)i, new Rect(8f, -(summaryH + i * (rowH + 4f)), bodyW - 16f, rowH));
            BuildOperation(folderBody, new Rect(8f, -summaryH, bodyW - 16f, briefHeight - summaryH));

            folderEmpty = DeskStyle.Body(folderGroup, new Rect(24f, -40f, w - 48f, 120f), DeskStyle.Typewriter, DeskStyle.Ink,
                TextAlignmentOptions.TopLeft, true);
        }

        private void BuildOperation(RectTransform parent, Rect at)
        {
            operationGroup = Group(parent, "FieldControl", at, out _);
            float w = at.width, h = at.height;
            bool compact = h < 340f;
            Chrome.Panel(operationGroup, new Rect(0f, 0f, w, h), DeskStyle.Map);
            RoomPaint.Brackets(operationGroup, new Rect(0f, 0f, w, h), 10f, RoomPaint.Instrument);
            operationTitle = DeskStyle.Title(operationGroup, "", new Rect(12f, -6f, w - 132f, 22f), 16f, DeskStyle.Ink);
            operationClock = DeskStyle.Body(operationGroup, new Rect(w - 124f, -7f, 112f, 20f), 11f, DeskStyle.Khaki,
                TextAlignmentOptions.MidlineRight);
            float step = compact ? 27f : 36f;
            operationBarWidth = w - 24f;
            for (int i = 0; i < 3; i++)
            {
                float y = 33f + i * step;
                operationMetrics[i] = DeskStyle.Body(operationGroup, new Rect(12f, -y, w - 24f, 17f), 11f, DeskStyle.Ink);
                Chrome.Panel(operationGroup, new Rect(12f, -y - 19f, operationBarWidth, 4f), DeskStyle.Khaki.WithAlpha(0.25f));
                operationBars[i] = Chrome.Panel(operationGroup, new Rect(12f, -y - 19f, 0f, 4f), RoomPaint.Instrument);
            }
            float reportY = 33f + step * 3f;
            operationReport = DeskStyle.Body(operationGroup, new Rect(12f, -reportY, w - 24f, compact ? 35f : 68f),
                11f, DeskStyle.Khaki, TextAlignmentOptions.TopLeft, true);
            float resultY = reportY + (compact ? 36f : 72f);
            operationResult = DeskStyle.Body(operationGroup, new Rect(12f, -resultY, w - 24f,
                Mathf.Max(30f, h - resultY - 142f)), 11f, DeskStyle.Ink, TextAlignmentOptions.TopLeft, true);
            float commandW = (commandGroup.sizeDelta.x - 32f) / operationOrders.Length;
            for (int i = 0; i < operationOrders.Length; i++)
            {
                SpecOpsDirective directive = FieldOrders[i];
                operationOrders[i] = BuildStamp(commandGroup, new Rect(i * (commandW + 8f), 0f, commandW, 56f),
                    () => Directive(selectedTeam, directive), 16f);
                operationOrders[i].Primary = directive == SpecOpsDirective.Execute;
                operationOrders[i].Frame.enabled = false;
                var frame = Chrome.Graphic<CommandStepGraphic>(operationOrders[i].Control.Rect, new Rect(0f, 0f, commandW, 56f), "CommandStep");
                frame.transform.SetAsFirstSibling();
                frame.color = DeskStyle.Khaki; frame.raycastTarget = false;
                operationOrders[i].Step = frame;
                operationOrders[i].Fill.enabled = false;
                SetStamp(operationOrders[i], (i + 1) + "   " + directive.ToString().ToUpperInvariant(), false, false,
                    "Deploy a team and select its mission to issue field orders.");
            }
            Chrome.Rule(operationGroup, new Rect(12f, -h + 128f, w - 24f, 1f), DeskStyle.Stamp.WithAlpha(0.6f));
            OpsArtwork.Draw(operationGroup, new Rect(12f, -h + 118f, 82f, 74f), 3);
            DeskStyle.Title(operationGroup, "BLACK MARKET", new Rect(106f, -h + 117f, w - 118f, 22f), 18f, DeskStyle.Stamp);
            marketStatus = DeskStyle.Body(operationGroup, new Rect(106f, -h + 89f, w - 118f, 38f), 12f, DeskStyle.Khaki, TextAlignmentOptions.TopLeft, true);
            for (int i = 0; i < marketOrders.Length; i++)
            {
                byte offer = (byte)i;
                marketOrders[i] = BuildStamp(operationGroup, new Rect(12f + i * ((w - 30f) * 0.5f + 6f),
                    -(h - 38f), (w - 30f) * 0.5f, 30f), () => support?.RequestBlackMarket(offer), 10f);
            }
        }

        private void ClearFieldOrders()
        {
            for (int i = 0; i < operationOrders.Length; i++)
                SetStamp(operationOrders[i], (i + 1) + "   " + FieldOrders[i].ToString().ToUpperInvariant(), false, false,
                    "Select an objective and deploy a team to issue field orders.");
        }

        private static string Branch(FieldMission mission) => mission == FieldMission.Recon
            ? "FIELDCRAFT / OBSERVER → SPOT Q0 → SKYWATCH Q1"
            : mission == FieldMission.Sabotage ? "FIELDCRAFT / CELL → SUPPRESS Q0 → HUNT Q2"
            : mission == FieldMission.Steal ? "ESPIONAGE / EAVESDROP Q0 → BLACK MARKET Q2"
            : "FIELDCRAFT / SAFEHOUSE → FORTIFY";

        private void WriteOperation(SpecOpsDetachment detachment, double now)
        {
            FieldTeam team = detachment.Team(selectedTeam);
            bool active = team.Deployed;
            operationGroup.gameObject.SetActive(active);
            for (int i = 0; i < sheets.Length; i++) sheets[i].Root.gameObject.SetActive(!active);
            if (!active)
            {
                for (int i = 0; i < operationOrders.Length; i++)
                {
                    operationOrders[i].Control.gameObject.SetActive(true);
                    SetStamp(operationOrders[i], (i + 1) + "   " + FieldOrders[i].ToString().ToUpperInvariant(), false, false,
                        "Select an objective and deploy this team to work the operation.");
                }
                return;
            }
            bool working = team.State == TeamState.Deciding;
            bool holding = team.State == TeamState.Holding;
            int quality = holding || team.State == TeamState.OnTask ? team.Quality : team.Friendly ? 0 :
                FieldCatalog.Quality(team.Preparation, team.Intel, team.Exposure);
            DeskStyle.Type(operationTitle, FieldWords.Callsign(selectedTeam) + " / " + FieldWords.State(team.State));
            DeskStyle.Type(operationClock, (holding ? "POST " : "WINDOW ") + FieldWords.Clock(detachment.Remaining(selectedTeam, now)));
            int[] values = { team.Preparation, team.Intel, team.Exposure };
            string[] labels = { "PREPARATION " + team.Preparation + "/100 · EXECUTE AT 60",
                "INTEL " + team.Intel + "/100 · OBSERVE MAKES ADVANCE MORE EFFICIENT",
                "EXPOSURE " + team.Exposure + "/100 · EXECUTE ≤75 / WITHDRAW AT 100" };
            for (int i = 0; i < values.Length; i++)
            {
                DeskStyle.Type(operationMetrics[i], labels[i]);
                operationBars[i].rectTransform.sizeDelta = new Vector2(operationBarWidth * values[i] / 100f, 4f);
                operationBars[i].color = i == 2 ? (values[i] > 75 ? AvTheme.RailDanger : AvTheme.RailCaution) : RoomPaint.Instrument;
            }
            string report = team.State == TeamState.EnRoute ? "INSERTION / " + FieldWords.Origin(team) + " → " + team.Target
                : !detachment.IntelKnown(team.Anchor, now) || team.CurrentThreat == byte.MaxValue
                    ? "FIELD REPORT / UNCONFIRMED · OBSERVE TO LOCATE DEFENDERS"
                : "FIELD REPORT / " + team.CurrentThreat + " GROUND · " + team.CurrentRadars + " RADARS · PRESSURE " +
                  FieldCatalog.Pressure(team.CurrentThreat, team.CurrentRadars) + "/30";
            report += "\n" + Branch(team.Mission);
            DeskStyle.Type(operationReport, report);
            int supplier = detachment.FindMarket(now);
            string marketStatus = supplier >= 0 ? "BLACK MARKET READY / " + FieldWords.Callsign(supplier) + " / " +
                detachment.Team(supplier).Charges + " SHARED CHARGES" : "BLACK MARKET LOCKED / HOSTILE ESPIONAGE POST AT Q2 REQUIRED";
            DeskStyle.Type(this.marketStatus, marketStatus);
            string payoff = holding
                ? "Q" + quality + " / " + team.Charges + " CHARGES · " + FieldWords.PostGrant(team.Mission) +
                  (team.Mission == FieldMission.Steal ? "\n" + marketStatus : "")
                : "PACKAGE Q" + quality + " / " + (team.Friendly ? 1 : FieldCatalog.PostCharges(quality)) + " CHARGES · EXECUTE ≥60 PREP / ≤75 EXP\n" +
                  (team.Friendly ? "HOME STAGING / Q0 · NO COMBAT RANK OR STOLEN INTEL"
                  : "Q1:70/40/≤60 · Q2:80/60/≤45 · Q3:95/80/≤30 (PREP/INTEL/EXP)");
            if (operationResult.rectTransform.sizeDelta.y >= 80f)
                payoff += "\n\nNEXT / " + FieldWords.OperatorAdvice(team, now) +
                    (holding ? "\nSUPPLY / " + (team.Mission == FieldMission.Steal
                        ? "SIGNAL KIT: +" + BlackMarketCatalog.Computing + " COMPUTING / +" + BlackMarketCatalog.Intel + " INTEL · CARGO: ORBITAL RESUPPLY"
                        : FieldWords.PostGrant(team.Mission)) : "\nPAYOFF / " + FieldWords.BriefEffect(team.Mission, quality));
            DeskStyle.Type(operationResult, payoff);
            bool ready = support != null && support.OpsStateFresh && !support.CommandPending;
            for (int i = 0; i < operationOrders.Length; i++)
            {
                SpecOpsDirective directive = FieldOrders[i];
                Stamp order = operationOrders[i];
                order.Control.Rect.gameObject.SetActive(true);
                SpecOpsDenial denial = detachment.CheckDirective(selectedTeam, directive, now);
                string text = directive == SpecOpsDirective.Observe ? "OBSERVE +INTEL" : directive == SpecOpsDirective.Advance
                    ? "ADVANCE +PREP" : directive == SpecOpsDirective.Conceal ? "CONCEAL −EXPOSURE" : directive.ToString().ToUpperInvariant();
                if (working && now < team.OrderReadyAt && directive != SpecOpsDirective.Extract)
                    text = "ACK " + FieldWords.Clock(team.OrderReadyAt - now);
                SetStamp(order, (i + 1) + "   " + text, ready && denial == SpecOpsDenial.None, directive == SpecOpsDirective.Extract,
                    denial != SpecOpsDenial.None ? FieldWords.Denial(denial) : directive == SpecOpsDirective.Observe
                    ? "Gain 22 intel and 5 preparation; exposure rises with enemy pressure."
                    : directive == SpecOpsDirective.Advance ? "Gain 18 preparation plus intel advantage; creates a large signature."
                    : directive == SpecOpsDirective.Conceal ? "Lose 28 exposure and 5 preparation. Enemy pressure still accumulates."
                    : directive == SpecOpsDirective.Execute ? "Q" + quality + ": " + FieldWords.BriefEffect(team.Mission, quality) +
                        " Better preparation unlocks stronger branches and more post charges."
                    : "Extract safely. Unused charges and unfinished work are abandoned.");
            }
            for (int i = 0; i < marketOrders.Length; i++)
            {
                bool market = holding && team.Mission == FieldMission.Steal;
                marketOrders[i].Control.Rect.gameObject.SetActive(market);
                if (!market) continue;
                float price = support != null ? support.BlackMarketPrice((byte)i) : 0f;
                SetStamp(marketOrders[i], (i == 0 ? "INTEL KIT " : "ORBITAL CARGO ") + Figure(price),
                    ready && supplier >= 0 && support.LocalOpsReserve >= price, false,
                    "BLACK MARKET / Q2 hostile listening post. One shared post charge plus OPS reserve; host verifies supply capacity.");
            }
        }

        private static void BuildFact(RectTransform parent, string title, Rect at, out TMP_Text figure, out TMP_Text words)
        {
            Chrome.Outline(parent, at, DeskStyle.Khaki);
            TMP_Text heading = Chrome.Label(parent, title, new Rect(at.x + 8f, at.y - 3f, at.width - 16f, 13f), DeskStyle.Khaki, 10f,
                FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            heading.characterSpacing = 3f;
            figure = DeskStyle.Title(parent, "", new Rect(at.x + 8f, at.y - 17f, 54f, 25f), 22f, DeskStyle.Ink);
            figure.characterSpacing = 1f;
            words = DeskStyle.Body(parent, new Rect(at.x + 68f, at.y - 17f, at.width - 76f, at.height - 20f), 10f, DeskStyle.Khaki, TextAlignmentOptions.MidlineLeft, true);
        }

        private Chip BuildChip(int team, Rect at)
        {
            var chip = new Chip();
            chip.Control = RoomControl.Create(folderBody, at, () => SelectTeam(team), "TeamChip");
            RectTransform host = chip.Control.Rect;
            chip.Fill = Chrome.Panel(host, new Rect(0f, 0f, at.width, at.height), DeskStyle.Ink.WithAlpha(0f));
            chip.Outline = Chrome.Outline(host, new Rect(0f, 0f, at.width, at.height), DeskStyle.Khaki);
            chip.Text = Chrome.Label(host, "", new Rect(4f, 0f, at.width - 8f, at.height), DeskStyle.Ink, 11f, FontStyles.Bold,
                TextAlignmentOptions.Center);
            chip.Control.Changed = _ => nextPaint = true;
            return chip;
        }

        private RectTransform commandGroup;
        private bool nextPaint;

        private Sheet BuildSheet(FieldMission mission, Rect at)
        {
            var sheet = new Sheet();
            int index = (int)mission;
            var go = new GameObject("Sheet" + index, typeof(RectTransform));
            sheet.Root = (RectTransform)go.transform;
            sheet.Root.SetParent(folderBody, false);
            Chrome.Place(sheet.Root, at);
            float w = at.width, h = at.height;
            sheet.Hover = RoomControl.Create(sheet.Root, new Rect(0f, 0f, w, h), null, "SheetHover");
            sheet.Hover.Changed = c =>
            {
                if (c.Hovered) hoverMission = index;
                else if (hoverMission == index) hoverMission = -1;
                nextPaint = true;
            };
            sheet.Paper = Chrome.Panel(sheet.Root, new Rect(0f, 0f, w, h), DeskStyle.Map);
            Chrome.Rule(sheet.Root, new Rect(3f, 0f, w - 3f, 1f), DeskStyle.Khaki.WithAlpha(0.6f));
            Chrome.Rule(sheet.Root, new Rect(10f, -h + 1f, w - 20f, 1f), DeskStyle.Khaki.WithAlpha(0.45f));
            sheet.MissionRail = Chrome.Rule(sheet.Root, new Rect(0f, 0f, 3f, h), RoomPaint.Instrument);
            int symbol = mission == FieldMission.Recon ? OpsSprites.G.Spot
                : mission == FieldMission.Sabotage ? OpsSprites.G.Suppress
                : mission == FieldMission.Steal ? OpsSprites.G.SpecOps : OpsSprites.G.Fortify;
            Image missionIcon = Chrome.Panel(sheet.Root, new Rect(12f, -7f, 22f, 22f), RoomPaint.Instrument,
                OpsSprites.Glyph(symbol));
            missionIcon.raycastTarget = false;
            sheet.Title = DeskStyle.Title(sheet.Root, "[" + (index + 1) + "] " + FieldWords.Mission(mission),
                new Rect(42f, -8f, w - 146f, 20f), 16f, DeskStyle.Ink);
            sheet.Title.characterSpacing = 1f;
            sheet.Cost = DeskStyle.Body(sheet.Root, new Rect(w - 96f, -8f, 84f, 20f), DeskStyle.Typewriter, DeskStyle.Ink,
                TextAlignmentOptions.MidlineRight);
            sheet.Effect = DeskStyle.Body(sheet.Root, new Rect(12f, -30f, w - 156f, 26f), DeskStyle.TypewriterSmall, DeskStyle.Ink,
                TextAlignmentOptions.TopLeft, true);
            float barW = w - 24f - 132f;
            sheet.Odds = new Image[3];
            Color[] odds = { RoomPaint.Ready, AvTheme.RailCaution, AvTheme.RailDanger };
            for (int i = 0; i < 3; i++)
            {
                sheet.Odds[i] = Chrome.Panel(sheet.Root, new Rect(12f, -60f, 10f, 10f), odds[i]);
                if (i == 1) { sheet.Odds[i].sprite = OpsSprites.Guard; sheet.Odds[i].type = Image.Type.Tiled; }
                if (i == 2) { sheet.Odds[i].sprite = OpsSprites.Dash; sheet.Odds[i].type = Image.Type.Tiled; }
            }
            sheet.OddsText = DeskStyle.Body(sheet.Root, new Rect(12f, -72f, barW, 14f), DeskStyle.TypewriterSmall, DeskStyle.Ink);
            sheet.Time = new Image[4];
            Color[] time = { RoomPaint.Instrument, AvTheme.RailCaution, AvTheme.RailDanger, RoomPaint.Ready };
            for (int i = 0; i < sheet.Time.Length; i++)
            {
                sheet.Time[i] = Chrome.Panel(sheet.Root, new Rect(12f, -90f, 10f, 6f), time[i]);
                if (i == 2) { sheet.Time[i].sprite = OpsSprites.Guard; sheet.Time[i].type = Image.Type.Tiled; }
            }
            sheet.TimeText = DeskStyle.Body(sheet.Root, new Rect(12f, -98f, barW, 14f), DeskStyle.TypewriterSmall, DeskStyle.Ink);
            sheet.Post = DeskStyle.Body(sheet.Root, new Rect(12f, -h + 17f, w - 24f, 14f), DeskStyle.TypewriterSmall, DeskStyle.Khaki);
            sheet.Refusal = Chrome.Label(sheet.Root, "", new Rect(12f, -66f, barW, 48f), DeskStyle.Stamp, 12f, FontStyles.Bold,
                TextAlignmentOptions.MidlineLeft, true);
            sheet.Refusal.characterSpacing = 2f;
            sheet.Launch = BuildStamp(sheet.Root, new Rect(w - 12f - 120f, -62f, 120f, 50f), () => Launch(mission), 14f);
            sheet.Launch.Primary = true;
            PaintStamp(sheet.Launch);
            sheet.Launch.Text.enableAutoSizing = true;
            sheet.Launch.Text.fontSizeMin = 10f;
            sheet.Launch.Text.fontSizeMax = 14f;
            sheet.Launch.Text.enableWordWrapping = true;
            sheet.DispatchedFrame = Chrome.Panel(sheet.Root, new Rect(0f, 0f, 220f, 44f), DeskStyle.Stamp, OpsSprites.Stamp);
            sheet.DispatchedFrame.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            sheet.DispatchedFrame.rectTransform.anchoredPosition = new Vector2(w * 0.5f, -h * 0.5f);
            sheet.DispatchedFrame.rectTransform.localEulerAngles = Vector3.zero;
            sheet.Dispatched = DeskStyle.Title(sheet.DispatchedFrame.rectTransform, "DISPATCHED", new Rect(0f, 0f, 220f, 44f), 20f,
                DeskStyle.Stamp, TextAlignmentOptions.Center);
            sheet.DispatchedFrame.gameObject.SetActive(false);
            if (h < 135f)
            {
                Chrome.Place(missionIcon.rectTransform, new Rect(10f, -5f, 18f, 18f));
                Chrome.Place(sheet.Title.rectTransform, new Rect(34f, -3f, w - 250f, 19f));
                Chrome.SetSize(sheet.Title, 13f);
                Chrome.Place(sheet.Cost.rectTransform, new Rect(w - 213f, -3f, 95f, 19f));
                Chrome.Place(sheet.Effect.rectTransform, new Rect(12f, -23f, w - 132f, 17f));
                sheet.Effect.enableWordWrapping = false;
                Chrome.Place(sheet.OddsText.rectTransform, new Rect(12f, -41f, w - 132f, 15f));
                for (int i = 0; i < sheet.Odds.Length; i++)
                    Chrome.Place(sheet.Odds[i].rectTransform, new Rect(12f, -h + 3f, 0f, 3f));
                Chrome.Place(sheet.Refusal.rectTransform, new Rect(12f, -39f, w - 132f, 18f));
                sheet.Refusal.characterSpacing = 0f;
                Chrome.SetSize(sheet.Refusal, 11f);
                for (int i = 0; i < sheet.Time.Length; i++) sheet.Time[i].gameObject.SetActive(false);
                sheet.TimeText.gameObject.SetActive(false);
                sheet.Post.gameObject.SetActive(false);
                if (h >= 104f)
                {
                    for (int i = 0; i < sheet.Time.Length; i++)
                        Chrome.Place(sheet.Time[i].rectTransform, new Rect(12f, -60f, 0f, 3f));
                    Chrome.Place(sheet.TimeText.rectTransform, new Rect(12f, -67f, w - 132f, 15f));
                    Chrome.Place(sheet.Post.rectTransform, new Rect(12f, -84f, w - 24f, 16f));
                }
                PlaceStamp(sheet.Launch, new Rect(w - 112f, -8f, 104f, h - 16f));
                sheet.DispatchedFrame.rectTransform.sizeDelta = new Vector2(w - 20f, 28f);
                sheet.Dispatched.rectTransform.sizeDelta = new Vector2(w - 20f, 28f);
                Chrome.SetSize(sheet.Dispatched, 14f);
            }
            return sheet;
        }

        private void BuildTimeline(RectTransform room, Rect at)
        {
            timelineGroup = Group(room, "FieldReport", at, out timelineFade);
            float w = at.width, h = at.height;
            RoomPaint.Inset(timelineGroup, new Rect(0f, 0f, w, h), DeskStyle.Paper, DeskStyle.Khaki);
            DeskStyle.Title(timelineGroup, "FIELD REPORT / DETACHMENT", new Rect(12f, -8f, w - 24f, 22f), 14f, DeskStyle.Ink);
            for (int i = 0; i < LogLines; i++)
                logLines[i] = DeskStyle.Body(timelineGroup, new Rect(14f, -40f - i * 32f, w - 28f, 29f), 12f,
                    i == 0 ? DeskStyle.Ink : DeskStyle.Khaki, TextAlignmentOptions.TopLeft, true);
            float lanesTop = 180f;
            laneX0 = 72f; laneWidth = w - laneX0 - 14f;
            for (int t = 0; t < TeamCount; t++)
            {
                float y = -lanesTop - t * 18f;
                var lane = new Lane
                {
                    Name = DeskStyle.Body(timelineGroup, new Rect(12f, y, 58f, 15f), 11f, DeskStyle.Ink),
                    Empty = DeskStyle.Body(timelineGroup, new Rect(laneX0, y, laneWidth, 15f), 10f, DeskStyle.Khaki),
                    Segments = new Image[LaneSegments], Words = new TMP_Text[LaneSegments]
                };
                DeskStyle.Type(lane.Name, FieldWords.Callsign(t));
                for (int j = 0; j < LaneSegments; j++)
                {
                    lane.Segments[j] = Chrome.Panel(timelineGroup, new Rect(laneX0, y, 10f, 13f), RoomPaint.Instrument);
                    lane.Words[j] = Chrome.Label(timelineGroup, "", new Rect(laneX0, y, 10f, 13f), DeskStyle.Ink, 10f);
                    lane.Segments[j].enabled = false;
                }
                lanes[t] = lane;
            }
            summary = DeskStyle.Body(timelineGroup, new Rect(12f, -h + 66f, w - 24f, 18f), 11f, DeskStyle.Khaki);
            dtg = DeskStyle.Title(timelineGroup, "", new Rect(12f, -h + 44f, w - 100f, 18f), 11f, DeskStyle.Khaki);
            link = DeskStyle.Body(timelineGroup, new Rect(w - 82f, -h + 44f, 70f, 18f), 11f, RoomPaint.Ready, TextAlignmentOptions.MidlineRight);
            alloc = DeskStyle.Body(timelineGroup, new Rect(12f, -h + 23f, w - 24f, 18f), 12f, DeskStyle.Ink);
        }

        private void BuildNote(RectTransform room, Rect at)
        {
            noteGroup = Group(room, "Note", at, out noteFade);
            Chrome.Panel(noteGroup, new Rect(0f, 0f, at.width, at.height), DeskStyle.Paper);
            float step = at.width / 3f;
            Chrome.Rule(noteGroup, new Rect(0f, 0f, at.width, 1f), RoomPaint.Instrument);
            Chrome.Rule(noteGroup, new Rect(step, -3f, 1f, 19f), DeskStyle.Khaki.WithAlpha(0.45f));
            Chrome.Rule(noteGroup, new Rect(step * 2f, -3f, 1f, 19f), DeskStyle.Khaki.WithAlpha(0.45f));
            planObjective = PlanLabel(noteGroup, 4f, step - 8f);
            planTeam = PlanLabel(noteGroup, step + 5f, step - 10f);
            planMission = PlanLabel(noteGroup, step * 2f + 5f, step - 10f);
            Chrome.Rule(noteGroup, new Rect(0f, -22f, at.width, 1f), DeskStyle.Khaki.WithAlpha(0.5f));
            Chrome.Rule(noteGroup, new Rect(0f, -23f, 3f, at.height - 23f), RoomPaint.Instrument);
            note = DeskStyle.Body(noteGroup, new Rect(12f, -25f, at.width - 24f, 15f), DeskStyle.TypewriterSmall,
                DeskStyle.Ink, TextAlignmentOptions.MidlineLeft);
        }

        private static TMP_Text PlanLabel(RectTransform parent, float x, float width)
        {
            TMP_Text label = DeskStyle.Body(parent, new Rect(x, -3f, width, 18f), DeskStyle.Typewriter,
                DeskStyle.Ink, TextAlignmentOptions.MidlineLeft);
            label.enableAutoSizing = true;
            label.fontSizeMin = 10f;
            label.fontSizeMax = DeskStyle.Typewriter;
            return label;
        }

        private void BuildEmpty(RectTransform room)
        {
            float width = Mathf.Min(560f, focus.width - 32f);
            var at = new Rect(focus.x + (focus.width - width) * 0.5f, focus.y - focus.height * 0.5f + 70f, width, 140f);
            emptyNote = Group(room, "Empty", at, out _);
            Chrome.Panel(emptyNote, new Rect(4f, -5f, at.width, at.height), DeskStyle.Ink.WithAlpha(0.3f));
            Chrome.Panel(emptyNote, new Rect(0f, 0f, at.width, at.height), DeskStyle.Paper);
            emptyTitle = DeskStyle.Title(emptyNote, "", new Rect(20f, -16f, at.width - 40f, 26f), DeskStyle.Stencil, DeskStyle.Ink);
            emptyBody = DeskStyle.Body(emptyNote, new Rect(20f, -50f, at.width - 40f, 80f), DeskStyle.Typewriter, DeskStyle.Ink,
                TextAlignmentOptions.TopLeft, true);
            emptyNote.gameObject.SetActive(false);
        }

        // ---- Lifecycle ---------------------------------------------------------------------------

        public void Show(object context)
        {
            if (context is int slot) SelectSlot(slot);
            PickDefaultTeam(support != null ? support.LocalDetachment : null);
            layoutDue = true;
            nextHomes = 0f;
        }

        public void Hide()
        {
            hoverMission = -1;
        }

        public void Entrance(float progress)
        {
            entrance = Mathf.Clamp01(progress);
            map.Settle(Window(entrance, 0f, 0.6f));
            Slide(headerGroup, headerFade, Window(entrance, 0f, 0.45f), 0f, 26f);
            for (int i = 0; i < TeamCount; i++)
            {
                DogTag tag = tags[i];
                float p = Window(entrance, 0.06f * i, 0.45f + 0.06f * i);
                Slide(tag.Root, tag.Group, p, -90f, 0f);
                tag.Root.localEulerAngles = new Vector3(0f, 0f, (1f - LayoutMotion.EaseOutCubic(p)) * (i % 2 == 0 ? -5f : 4f));
            }
            Slide(folderGroup, folderFade, Window(entrance, 0.22f, 0.72f), 110f, 0f);
            Slide(timelineGroup, timelineFade, Window(entrance, 0.34f, 0.84f), 0f, -70f);
            Slide(noteGroup, noteFade, Window(entrance, 0.5f, 1f), 0f, 30f);
        }

        private static float Window(float p, float start, float end) => Mathf.Clamp01((p - start) / Mathf.Max(0.0001f, end - start));

        private readonly Dictionary<RectTransform, Vector2> homePositions = new Dictionary<RectTransform, Vector2>(8);

        private void Slide(RectTransform rect, CanvasGroup fade, float p, float dx, float dy)
        {
            if (rect == null) return;
            if (!homePositions.TryGetValue(rect, out Vector2 home))
            {
                home = rect.anchoredPosition;
                homePositions[rect] = home;
            }
            float eased = LayoutMotion.EaseOutCubic(p);
            rect.anchoredPosition = home + new Vector2(dx, dy) * (1f - eased);
            if (fade != null) fade.alpha = eased;
        }

        public void Refresh(double now, float time, bool textTick)
        {
            SpecOpsDetachment detachment = support != null ? support.LocalDetachment : null;
            if (time >= nextHomes)
            {
                nextHomes = time + 2f;
                LocalHomes();
                layoutDue = true;
            }
            if (support != null && map.Board.RefreshFrontline(LocalFaction(), time)) map.FrontChanged();
            Paint(detachment, support != null ? support.OrbitNow : now, time, textTick);
        }

        /// <summary>Paint from a detachment. The game calls it every frame; the harness with a fixture.</summary>
        internal void Paint(SpecOpsDetachment detachment, double now, float time, bool textTick)
        {
            lastDetachment = detachment;
            int slot = SelectedSlot(detachment);
            if (awaiting && support != null && !support.CommandPending)
            {
                awaiting = false;
                string reply = support.Status ?? "NO RESPONSE";
                noteBad = reply.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          reply.IndexOf("no response", StringComparison.OrdinalIgnoreCase) >= 0;
                noteText = "HOST · " + reply;
                textTick = true;
            }
            if (textTick || nextPaint || layoutDue || map.FrameChanged)
            {
                nextPaint = false;
                layoutDue = false;
                map.Layout(detachment, now, slot, new Vector2(folderRect.x, folderRect.y - 44f), Live(detachment) && slot >= 0);
                WriteText(detachment, slot, now);
                PreviewHover(detachment, slot);
            }
            map.Animate(detachment, now, time);
        }

        public bool HandleKeys()
        {
            SpecOpsDetachment detachment = support != null ? support.LocalDetachment : null;
            bool deployed = detachment != null && detachment.Team(selectedTeam).Deployed;
            for (int i = 0; i < MissionKeys.Length; i++)
                if (Input.GetKeyDown(MissionKeys[i]))
                {
                    if (deployed) Directive(selectedTeam, FieldOrders[i]);
                    else Launch((FieldMission)i);
                    return true;
                }
            if (deployed && Input.GetKeyDown(KeyCode.Alpha5))
            { Directive(selectedTeam, SpecOpsDirective.Extract); return true; }
            if (Input.GetKeyDown(KeyCode.Tab)) { SelectTeam((selectedTeam + 1) % TeamCount); return true; }
            if (Input.GetKeyDown(KeyCode.Q)) { StepObjective(detachment, -1); return true; }
            if (Input.GetKeyDown(KeyCode.E)) { StepObjective(detachment, 1); return true; }
            if (Input.GetKeyDown(KeyCode.R)) { RaiseOrRecall(selectedTeam); return true; }
            if (detachment != null && detachment.Team(selectedTeam).State == TeamState.Deciding)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
                { Directive(selectedTeam, SpecOpsDirective.Execute); return true; }
                if (Input.GetKeyDown(KeyCode.X)) { Directive(selectedTeam, SpecOpsDirective.Extract); return true; }
            }
            if (Input.GetKeyDown(KeyCode.F)) { FitMap(); return true; }
            return false;
        }

        /// <summary>Right-click inside the table is "back": drop the zoom and frame the theatre again.</summary>
        public void RightClickInside() => FitMap();

        private void FitMap()
        {
            map.Board.ResetFraming();
            layoutDue = true;
        }

        // ---- Selection ---------------------------------------------------------------------------

        private void SelectTeam(int team)
        {
            selectedTeam = Mathf.Clamp(team, 0, TeamCount - 1);
            SpecOpsDetachment detachment = lastDetachment ?? support?.LocalDetachment;
            if (detachment != null && detachment.Team(selectedTeam).Deployed)
            {
                selectedAnchor = detachment.Team(selectedTeam).Anchor;
                anchorChosen = true;
            }
            DisarmConfirm();
            nextPaint = true;
        }

        private void SelectSlot(int slot)
        {
            SpecOpsDetachment detachment = lastDetachment ?? support?.LocalDetachment;
            if (detachment == null || slot < 0 || slot >= detachment.ObjectiveCount) return;
            selectedAnchor = detachment.Objective(slot).Anchor;
            anchorChosen = true;
            DisarmConfirm();
            nextPaint = true;
        }

        private void StepObjective(SpecOpsDetachment detachment, int step)
        {
            if (detachment == null || detachment.ObjectiveCount == 0) return;
            int slot = SelectedSlot(detachment);
            int count = detachment.ObjectiveCount;
            SelectSlot(((slot < 0 ? 0 : slot + step) % count + count) % count);
        }

        private int SelectedSlot(SpecOpsDetachment detachment)
        {
            if (detachment == null || !detachment.Enabled || detachment.ObjectiveCount == 0) return -1;
            if (detachment.Team(selectedTeam).Deployed)
            {
                int activeSlot = detachment.SlotOf(detachment.Team(selectedTeam).Anchor);
                if (activeSlot >= 0) return activeSlot;
            }
            int slot = anchorChosen ? detachment.SlotOf(selectedAnchor) : -1;
            if (slot >= 0) return slot;
            selectedAnchor = detachment.Objective(0).Anchor;
            anchorChosen = false;
            return 0;
        }

        private void PickDefaultTeam(SpecOpsDetachment detachment)
        {
            if (detachment == null) return;
            int best = -1;
            for (int i = 0; i < TeamCount; i++)
            {
                FieldTeam team = detachment.Team(i);
                if (team.State != TeamState.Ready) continue;
                if (best < 0 || team.Rank > detachment.Team(best).Rank) best = i;
            }
            if (best >= 0) selectedTeam = best;
        }

        // ---- Orders ------------------------------------------------------------------------------

        private void Launch(FieldMission mission)
        {
            SpecOpsDetachment detachment = support?.LocalDetachment;
            int slot = SelectedSlot(detachment);
            if (detachment == null || slot < 0)
            {
                DisarmConfirm();
                Say("NO OBJECTIVE SELECTED", true);
                return;
            }
            string refusal = LaunchRefusal(detachment, mission, slot);
            if (refusal != null)
            {
                DisarmConfirm();
                Say(MissionLabel(mission, detachment.Objective(slot)) + " · " + refusal, true);
                return;
            }
            int anchor = detachment.Objective(slot).Anchor;

            if (!ConfirmArmed(selectedTeam, (int)mission, anchor, false))
            {
                ArmConfirm(selectedTeam, (int)mission, anchor, false);
                float cost = support.SpecOpsMissionCost(mission);
                Say("PREVIEW · " + FieldWords.Callsign(selectedTeam) + " " + MissionLabel(mission, detachment.Objective(slot)) +
                    " → " + PlaceNames.Shorten(detachment.Objective(slot).Name, 16) + " · OPS " + Figure(cost) +
                    " · ACTIVE FIELD WORK · CLICK AGAIN TO COMMIT", false);
                nextPaint = true;
                return;
            }
            DisarmConfirm();
            support.RequestSpecOpsLaunch(selectedTeam, mission, anchor);
            Sent(FieldWords.Callsign(selectedTeam) + " · " + MissionLabel(mission, detachment.Objective(slot)) + " → " + detachment.Objective(slot).Name);
        }

        /// <summary>Preview and commit are bound to one team, mission and objective for a few
        /// seconds. The host still validates the final request.</summary>
        private bool ConfirmArmed(int team, int mission, int anchor, bool recall) =>
            confirmArmed && confirmRecall == recall && confirmTeam == team &&
            (recall || (confirmMission == mission && confirmAnchor == anchor)) &&
            (support == null || support.OrbitNow <= confirmUntil);

        private void ArmConfirm(int team, int mission, int anchor, bool recall)
        {
            confirmArmed = true;
            confirmTeam = team;
            confirmMission = mission;
            confirmAnchor = anchor;
            confirmRecall = recall;
            confirmUntil = (support != null ? support.OrbitNow : 0.0) + ConfirmSeconds;
        }

        private void DisarmConfirm()
        {
            confirmArmed = false;
            confirmTeam = -1;
        }

        private void PrimaryTeamAction(int team)
        {
            SpecOpsDetachment detachment = support?.LocalDetachment;
            if (detachment != null && detachment.Team(team).State == TeamState.Deciding)
            {
                if (selectedTeam != team) SelectTeam(team);
                Directive(team, SpecOpsDirective.Execute);
            }
            else RaiseOrRecall(team);
        }

        private void RaiseOrRecall(int team)
        {
            if (selectedTeam != team) SelectTeam(team);
            SpecOpsDetachment detachment = support?.LocalDetachment;
            if (detachment == null) return;
            FieldTeam value = detachment.Team(team);
            if (support.CommandPending)
            {
                DisarmConfirm();
                Say("AWAITING HOST · ONE ORDER AT A TIME", true);
                return;
            }
            if (!value.Formed)
            {
                SpecOpsDenial denial = detachment.CheckRaise(team);
                float cost = support.SpecOpsRaiseCost();
                if (denial != SpecOpsDenial.None) Say(FieldWords.Denial(denial), true);
                else if (!support.BypassRequirements && support.LocalOpsReserve + 0.001f < cost)
                    Say("RAISE " + FieldWords.Callsign(team) + " · NEEDS " + Figure(cost) + " OPS RESERVE", true);
                else
                {
                    support.RequestSpecOpsRaise(team);
                    Sent("RAISE " + FieldWords.Callsign(team));
                }
                return;
            }
            if (value.Deployed)
            {
                Directive(team, SpecOpsDirective.Extract);
            }
            else Say(FieldWords.Callsign(team) + " IS " + FieldWords.State(value.State) + " · PICK A MISSION", false);
        }

        private void Directive(int team, SpecOpsDirective directive)
        {
            SpecOpsDetachment detachment = support?.LocalDetachment;
            if (detachment == null || support == null) return;
            if (support.CommandPending)
            {
                Say("AWAITING HOST · ONE ORDER AT A TIME", true);
                return;
            }
            if (!support.OpsStateFresh)
            {
                Say("LINK STALE · WAITING FOR THE HOST", true);
                return;
            }
            SpecOpsDenial denial = detachment.CheckDirective(team, directive, support.OrbitNow);
            if (directive == SpecOpsDirective.Execute && !support.SpecOpsEnabled) denial = SpecOpsDenial.Disabled;
            if (denial != SpecOpsDenial.None)
            {
                Say(FieldWords.Denial(denial), true);
                return;
            }
            support.RequestSpecOpsDirective(team, directive);
            Sent(FieldWords.Callsign(team) + " · " + directive.ToString().ToUpperInvariant());
        }

        /// <summary>What the host would refuse, in words; null when the launch can go.</summary>
        private string LaunchRefusal(SpecOpsDetachment detachment, FieldMission mission, int slot)
        {
            if (support == null) return "NO LINK";
            if (!support.SpecOpsEnabled) return FieldWords.Denial(SpecOpsDenial.Disabled);
            if (support.CommandPending) return "AWAITING HOST";
            if (!support.OpsStateFresh) return "LINK STALE · WAITING FOR THE HOST";
            SpecOpsDenial denial = detachment.CheckLaunch(selectedTeam, mission, detachment.Objective(slot).Anchor);
            if (denial != SpecOpsDenial.None) return FieldWords.Denial(denial);
            float cost = support.SpecOpsMissionCost(mission);
            if (!support.BypassRequirements && support.LocalOpsReserve + 0.001f < cost)
                return "NEEDS " + Figure(cost) + " OPS RESERVE";
            return null;
        }

        private void Say(string line, bool bad)
        {
            noteText = "LOCAL · " + line;
            noteBad = bad;
            awaiting = false;
            nextPaint = true;
        }

        private void Sent(string order)
        {
            noteText = "PENDING · " + order + " · AWAITING HOST";
            noteBad = false;
            awaiting = true;
            nextPaint = true;
        }

        // ---- Text --------------------------------------------------------------------------------

        private bool Live(SpecOpsDetachment detachment) =>
            detachment != null && detachment.Enabled && (support == null || support.SpecOpsEnabled);

        private void WriteText(SpecOpsDetachment detachment, int slot, double now)
        {
            bool live = Live(detachment);
            DeskStyle.Type(summary, !live ? (detachment == null ? "AWAITING THEATER DATA" : "SPEC OPS IS OFF ON THIS SERVER")
                : detachment.Posts() + " POSTS HELD / READINESS " + detachment.GroundReadiness);
            DateTime utc = DateTime.UtcNow;
            string month = utc.ToString("MMM", Invariant).ToUpperInvariant();
            string clock = "DTG " + utc.ToString("ddHHmm", Invariant) + "Z " + month + " " + utc.ToString("yy", Invariant);
            if (dtg.text != clock) dtg.text = clock;
            float reserve = support != null ? support.LocalOpsReserve : 0f;
            DeskStyle.Type(alloc, "OPS RESERVE " + Figure(reserve));
            bool pending = support != null && support.CommandPending;
            bool fresh = support != null && support.OpsStateFresh;
            string linkWord = pending ? "AWAITING" : fresh ? "LINKED" : "SYNCING";
            if (link.text != linkWord) link.text = linkWord;
            link.color = pending ? AvTheme.RailCaution : fresh ? DeskStyle.Ink : DeskStyle.Stamp;

            for (int i = 0; i < TeamCount; i++) WriteTag(detachment, i, now, live);
            WriteFolder(detachment, slot, now, live);
            WriteTimeline(detachment, now, live);
            for (int i = 0; i < LogLines; i++) DeskStyle.Type(logLines[i], log != null && i < log.Length ? log[i] : "");

            WritePlan(detachment, slot, live);
            DeskStyle.Type(note, noteText);
            note.color = noteBad ? AvTheme.RailDanger : DeskStyle.Ink;

            bool empty = !live || slot < 0;
            if (emptyNote.gameObject.activeSelf != empty) emptyNote.gameObject.SetActive(empty);
            if (empty)
            {
                string title = detachment == null ? "AWAITING THEATER DATA" : !live ? "SPEC OPS IS OFF" : "NO OBJECTIVES ON THE TABLE";
                if (emptyTitle.text != title) emptyTitle.text = title;
                DeskStyle.Type(emptyBody, detachment == null
                    ? "The host has not sent the detachment yet. The table fills in as soon as it does."
                    : !live ? "The host has SPEC OPS switched off. Teams, posts and their abilities are unavailable."
                    : "The host scans the map every few seconds for enemy airfields, towns, outposts and air-defence sites. Raise a team while you wait.");
            }
        }

        private void WritePlan(SpecOpsDetachment detachment, int slot, bool live)
        {
            bool target = live && slot >= 0;
            FieldTeam team = live ? detachment.Team(selectedTeam) : default;
            int preview = target && ConfirmArmed(selectedTeam, confirmMission, detachment.Objective(slot).Anchor, false)
                ? confirmMission : hoverMission;
            DeskStyle.Type(planObjective, "01 / " + (target ? detachment.Objective(slot).Name : "AWAITING FIX"));
            DeskStyle.Type(planTeam, "02 / " + (live
                ? FieldWords.Callsign(selectedTeam) + " " + (team.Formed ? Short(team.State) : "NOT FORMED") : "NO LINK"));
            DeskStyle.Type(planMission, "03 / " + (preview >= 0
                ? MissionLabel((FieldMission)preview, target ? detachment.Objective(slot) : default) : "PICK A MISSION"));
            planObjective.color = target ? DeskStyle.Ink : DeskStyle.Khaki;
            planTeam.color = live && team.Formed && team.State == TeamState.Ready ? RoomPaint.Ready
                : live && team.Formed ? DeskStyle.Ink : DeskStyle.Khaki;
            planMission.color = preview >= 0 ? RoomPaint.Instrument : DeskStyle.Khaki;
        }

        private void WriteTag(SpecOpsDetachment detachment, int index, double now, bool live)
        {
            DogTag tag = tags[index];
            FieldTeam team = detachment != null ? detachment.Team(index) : default;
            bool selected = index == selectedTeam;
            bool formed = live && team.Formed;
            bool lost = !team.Formed && team.Last == MissionOutcome.Lost;
            Color tone = formed ? FieldTones.State(team.State) : lost ? AvTheme.RailDanger : DeskStyle.Khaki;
            // Paper cannot carry the bright semantic hues as text; the frame carries the hue, the words stay ink.
            for (int i = 0; i < tag.Outline.Length; i++) tag.Outline[i].enabled = selected;
            tag.Body.color = selected ? Color.Lerp(DeskStyle.Paper, DeskStyle.Tape, 0.45f) : formed ? DeskStyle.Paper
                : Color.Lerp(DeskStyle.Paper, DeskStyle.Khaki, 0.25f);
            tag.SelectedRail.color = selected ? RoomPaint.Ready : formed ? RoomPaint.Instrument : DeskStyle.Khaki.WithAlpha(0.45f);
            tag.Letter.color = tag.Callsign.color = formed ? DeskStyle.Ink : DeskStyle.Khaki;
            for (int i = 0; i < tag.Chevrons.Length; i++)
                tag.Chevrons[i].color = formed && i < team.Rank ? DeskStyle.Ink : DeskStyle.Khaki.WithAlpha(0.35f);
            DeskStyle.Type(tag.Rank, !formed ? (lost ? "SLOT OPEN" : "NOT FORMED")
                : FieldWords.Rank(team.Rank) + " · " + team.Wins + (team.Wins == 1 ? " WIN" : " WINS"));
            double remaining = detachment != null ? detachment.Remaining(index, now) : 0.0;
            string state = detachment == null ? "NO DATA" : !team.Formed ? (lost ? "LOST" : "EMPTY SLOT") : FieldWords.State(team.State);
            if (tag.State.text != state) tag.State.text = state;
            tag.State.color = formed ? DeskStyle.Ink : DeskStyle.Khaki;
            tag.StateFrame.color = formed ? tone : DeskStyle.Khaki;
            DeskStyle.Type(tag.Clock, formed && remaining > 0.0 ? FieldWords.Clock(remaining) : "");
            DeskStyle.Type(tag.Detail, Detail(team, formed, lost, remaining));
            float progress = detachment != null ? detachment.Progress(index, now) : 0f;
            float fill = team.State == TeamState.Holding ? 1f - progress : progress;
            tag.Bar.rectTransform.sizeDelta = new Vector2(tag.BarTrack.rectTransform.sizeDelta.x * (formed ? fill : 0f), 5f);
            tag.Bar.color = tone;
            if (tag.LostFrame.gameObject.activeSelf != lost) tag.LostFrame.gameObject.SetActive(lost);

            bool pending = support != null && support.CommandPending;
            bool deciding = team.Formed && team.State == TeamState.Deciding;
            bool canExtract = detachment != null && support != null && !pending && support.OpsStateFresh &&
                detachment.CheckDirective(index, SpecOpsDirective.Extract) == SpecOpsDenial.None;
            tag.Secondary.Control.Rect.gameObject.SetActive(deciding);
            if (deciding)
            {
                float half = (tag.Root.sizeDelta.x - 24f) * 0.5f;
                PlaceStamp(tag.Action, new Rect(8f, -tag.Root.sizeDelta.y + 26f, half, 23f));
                PlaceStamp(tag.Secondary, new Rect(16f + half, -tag.Root.sizeDelta.y + 26f, half, 23f));
                SetStamp(tag.Action, "EXECUTE · ENTER", live && !pending && detachment.CheckDirective(index, SpecOpsDirective.Execute, now) == SpecOpsDenial.None, false,
                    "Commit the prepared package. Work preparation, intel and exposure in the field dossier first.");
                SetStamp(tag.Secondary, "EXTRACT · X", canExtract, true,
                    "Abort safely before the task begins. The team returns safely.");
            }
            else if (!team.Formed)
            {
                PlaceStamp(tag.Action, new Rect(8f, -tag.Root.sizeDelta.y + 26f, tag.Root.sizeDelta.x - 16f, 23f));
                float cost = support != null ? support.SpecOpsRaiseCost() : FieldCatalog.RaiseCost;
                bool afford = support == null || support.BypassRequirements || support.LocalOpsReserve + 0.001f >= cost;
                SetStamp(tag.Action, "RAISE · " + Figure(cost), live && !pending && afford, false, afford
                    ? "Form a new RECRUIT team in this slot for " + Figure(cost) + " from OPS reserve."
                    : "Raising a team costs " + Figure(cost) + " OPS reserve; you have " + Figure(support.LocalOpsReserve) + ".");
            }
            else if (team.Deployed)
            {
                PlaceStamp(tag.Action, new Rect(8f, -tag.Root.sizeDelta.y + 26f, tag.Root.sizeDelta.x - 16f, 23f));
                SetStamp(tag.Action, team.State == TeamState.Holding ? "EXTRACT POST" : "EXTRACT", canExtract, true,
                    team.State == TeamState.Holding
                        ? "End the temporary " + FieldWords.Post(team.Mission).ToLowerInvariant() + " now. Earned rank stays; its field ability ends."
                        : "Abort safely and return the team. Unfinished work is abandoned.");
            }
            else
            {
                PlaceStamp(tag.Action, new Rect(8f, -tag.Root.sizeDelta.y + 26f, tag.Root.sizeDelta.x - 16f, 23f));
                SetStamp(tag.Action, team.State == TeamState.Ready ? (selected ? "SELECTED" : "SELECT TEAM") : "RESTING",
                    false, false, team.State == TeamState.Ready
                        ? "Ready. Pick an objective on the map, then choose a mission on the right."
                        : "Recovering; ready again when the clock runs out.");
            }
        }

        private static string Detail(in FieldTeam team, bool formed, bool lost, double remaining)
        {
            if (!formed) return lost
                ? "Lost on " + (string.IsNullOrEmpty(team.Target) ? "a mission" : team.Target) + ". Raise a new team to fill the slot."
                : "Empty slot. A new team starts as a RECRUIT.";
            switch (team.State)
            {
                case TeamState.EnRoute:
                    return FieldWords.Mission(team.Mission) + " · " + FieldWords.Origin(team) + " → " + team.Target;
                case TeamState.Deciding:
                    return "PREP " + team.Preparation + " / INTEL " + team.Intel + " / EXP " + team.Exposure + " · WORK DOSSIER";
                case TeamState.OnTask:
                    return "DELIVERING Q" + team.Quality + " PACKAGE · " + FieldWords.Clock(remaining);
                case TeamState.Holding:
                    return FieldWords.Post(team.Mission) + " · " + team.Charges + " CHARGES / Q" + team.Quality + " · " + FieldWords.Clock(remaining);
                case TeamState.Recovering:
                    return team.Last == MissionOutcome.Failed ? "Last mission failed; resting longer."
                        : team.Last == MissionOutcome.NoBuildings ? team.Mission == FieldMission.Seize
                            ? "Found no building to hold; back to rest." : "Effect unavailable; back to rest."
                        : "Back at base, resting.";
                default:
                    return team.Last == MissionOutcome.Success ? "Last mission: success. Standing by." : "Standing by for orders.";
            }
        }

        private void WriteFolder(SpecOpsDetachment detachment, int slot, double now, bool live)
        {
            bool open = live && slot >= 0;
            if (folderBody.gameObject.activeSelf != live) folderBody.gameObject.SetActive(live);
            DeskStyle.Type(folderEmpty, live ? "" : "No briefing while SPEC OPS is offline. When the host sends the detachment, the selected objective and its four operation branches open here.");
            DeskStyle.Type(folderIndex, open ? "OBJECTIVE " + (slot + 1) + "/" + detachment.ObjectiveCount + " · Q E" : "BRIEFING");
            if (!live) { ClearFieldOrders(); return; }
            WriteChips(detachment);
            if (!open)
            {
                operationGroup.gameObject.SetActive(false);
                for (int i = 0; i < sheets.Length; i++) sheets[i].Root.gameObject.SetActive(true);
                ClearFieldOrders();
                WriteCatalogue();
                return;
            }

            FieldObjective o = detachment.Objective(slot);
            siteImage.Set(map.TerrainSource, map.TerrainMetres, o.X, o.Z);
            objectiveGlyph.sprite = OpsSprites.Glyph(DeskMap.Glyph(o.Kind));
            objectiveGlyph.color = o.Hostile ? AvTheme.RailDanger : o.Friendly ? RoomPaint.Ready : DeskStyle.Khaki;
            if (objectiveName.text != o.Name) objectiveName.text = o.Name;
            DeskStyle.Type(objectiveLine, (o.Hostile ? "HOSTILE" : o.Friendly ? "FRIENDLY" : "NEUTRAL") + " · " + FieldWords.Kind(o.Kind) + " · " + Distance(o));
            bool known = detachment.IntelKnown(o.Anchor, now) && o.Threat != byte.MaxValue;
            SetFact(threatFigure, threatWords, known ? o.Threat.ToString(Invariant) : "?",
                known ? FieldWords.Threat(o.Threat) + (o.Threat == 1 ? " · UNIT" : " · UNITS") : "OBSERVE / SCOUT",
                o.Threat > 8 ? DeskStyle.Stamp : DeskStyle.Ink);
            SetFact(radarFigure, radarWords, known ? o.Radars.ToString(Invariant) : "?",
                known ? o.Radars == 0 ? "NONE SEEN" : "SEEN NEAR IT" : "UNCONFIRMED", DeskStyle.Ink);
            double scout = detachment.ScoutRemaining(o.Anchor, now);
            SetFact(scoutFigure, scoutWords, scout > 0.0 ? FieldWords.Clock(scout) : "—",
                scout > 0.0 ? "SCOUTED · +30 INTEL" : "SCOUT FOR +30 INTEL", DeskStyle.Ink);
            int on = detachment.TeamOn(o.Anchor);
            DeskStyle.Type(teamOn, on >= 0
                ? FieldWords.Callsign(on) + " IS " + FieldWords.State(detachment.Team(on).State) + " HERE"
                : o.Friendly ? "FRIENDLY STAGING SITE · READY FOR A TEAM"
                : "STRIKE / JAM DEFENDERS TO REDUCE FIELD PRESSURE");

            for (int i = 0; i < MissionCount; i++) WriteSheet(detachment, (FieldMission)i, slot, now);
            WriteOperation(detachment, now);
        }

        /// <summary>No objective yet: the folder still says what each mission earns and costs.</summary>
        private void WriteCatalogue()
        {
            objectiveGlyph.sprite = OpsSprites.Glyph(OpsSprites.G.SpecOps);
            objectiveGlyph.color = DeskStyle.Khaki;
            const string name = "NO OBJECTIVE YET";
            if (objectiveName.text != name) objectiveName.text = name;
            DeskStyle.Type(objectiveLine, "THE HOST LISTS TARGETS FROM THE MAP EVERY FEW SECONDS");
            SetFact(threatFigure, threatWords, "—", "SELECT TARGET", DeskStyle.Khaki);
            SetFact(radarFigure, radarWords, "—", "SELECT TARGET", DeskStyle.Khaki);
            SetFact(scoutFigure, scoutWords, "—", "RECON SCOUTS IT", DeskStyle.Khaki);
            DeskStyle.Type(teamOn, "RAISE TEAMS NOW · LAUNCH WHEN AN OBJECTIVE IS LISTED");
            for (int i = 0; i < MissionCount; i++)
            {
                var mission = (FieldMission)i;
                Sheet sheet = sheets[i];
                sheet.MissionRail.color = DeskStyle.Khaki.WithAlpha(0.55f);
                int rank = 0;
                float cost = support != null ? support.SpecOpsMissionCost(mission) : FieldCatalog.MissionCost(mission);
                DeskStyle.Type(sheet.Cost, "COST " + Figure(cost));
                DeskStyle.Type(sheet.Effect, sheet.Root.sizeDelta.y < 135f
                    ? FieldWords.BriefEffect(mission, rank) : FieldWords.Effect(mission, rank));
                for (int k = 0; k < 3; k++)
                {
                    sheet.Odds[k].gameObject.SetActive(false);
                }
                for (int k = 0; k < sheet.Time.Length; k++) sheet.Time[k].gameObject.SetActive(false);
                sheet.OddsText.gameObject.SetActive(false);
                sheet.TimeText.gameObject.SetActive(false);
                sheet.Refusal.gameObject.SetActive(true);
                const string why = "SELECT AN OBJECTIVE TO PREPARE AN OPERATION";
                if (sheet.Refusal.text != why) sheet.Refusal.text = why;
                sheet.Refusal.color = DeskStyle.Khaki;
                DeskStyle.Type(sheet.Post, "HOLDS A " + FieldWords.Post(mission) + " · ARMS " + FieldWords.PostGrant(mission));
                sheet.Post.color = DeskStyle.Khaki;
                SetStamp(sheet.Launch, "NO OBJECTIVE", false, false, FieldWords.MissionTitle(mission) + " — no objective is listed yet.");
                if (sheet.DispatchedFrame.gameObject.activeSelf) sheet.DispatchedFrame.gameObject.SetActive(false);
            }
        }

        private void WriteChips(SpecOpsDetachment detachment)
        {
            for (int i = 0; i < TeamCount; i++)
            {
                FieldTeam team = detachment.Team(i);
                Chip chip = chips[i];
                bool selected = i == selectedTeam;
                string text = FieldWords.Callsign(i) + " · " + (team.Formed ? Short(team.State) : "—");
                if (chip.Text.text != text) chip.Text.text = text;
                chip.Fill.color = selected ? DeskStyle.Ink : chip.Control.Hovered ? DeskStyle.Tape.WithAlpha(0.6f) : DeskStyle.Ink.WithAlpha(0f);
                chip.Text.color = selected ? DeskStyle.Paper : team.Formed ? DeskStyle.Ink : DeskStyle.Khaki;
                for (int e = 0; e < chip.Outline.Length; e++) chip.Outline[e].color = selected ? DeskStyle.Ink : DeskStyle.Khaki;
                chip.Control.WithTooltip(FieldWords.Callsign(i) + (team.Formed
                    ? " — " + FieldWords.Rank(team.Rank) + ", " + FieldWords.State(team.State).ToLowerInvariant() + ". Operate this team from its field dossier."
                    : " — not formed. Raise it in the team roster."));
            }
        }

        private static string Short(TeamState state)
        {
            switch (state)
            {
                case TeamState.Ready: return "READY";
                case TeamState.EnRoute: return "OUT";
                case TeamState.OnTask: return "TASK";
                case TeamState.Deciding: return "FIELD";
                case TeamState.Holding: return "HOLD";
                case TeamState.Recovering: return "REST";
                default: return "—";
            }
        }

        private static void SetFact(TMP_Text figure, TMP_Text words, string value, string caption, Color ink)
        {
            if (figure.text != value) figure.text = value;
            figure.color = ink;
            DeskStyle.Type(words, caption);
        }

        /// <summary>Sheet effect line, plus the CYBER intel outlook on a STEAL sheet.</summary>
        private string EffectText(FieldMission mission, int rank, bool brief)
        {
            string effect = brief ? FieldWords.BriefEffect(mission, rank) : FieldWords.Effect(mission, rank);
            if (mission == FieldMission.Steal && support != null && support.LocalCyber != null)
            {
                string outlook = FieldWords.StealOutlook(FieldCatalog.StealIntel(rank),
                    support.LocalCyber.Intel, support.LocalCyber.IntelCapacity());
                if (!string.IsNullOrEmpty(outlook)) effect += " " + outlook;
            }
            return effect;
        }

        private void WriteSheet(SpecOpsDetachment detachment, FieldMission mission, int slot, double now)
        {
            Sheet sheet = sheets[(int)mission];
            FieldObjective objective = detachment.Objective(slot);
            float cost = support != null ? support.SpecOpsMissionCost(mission) : FieldCatalog.MissionCost(mission);
            string refusal = LaunchRefusal(detachment, mission, slot);
            string title = "[" + ((int)mission + 1) + "] " + MissionLabel(mission, objective);
            DeskStyle.Type(sheet.Title, title);
            DeskStyle.Type(sheet.Cost, "OPS " + Figure(cost));
            DeskStyle.Type(sheet.Effect, Branch(mission));
            for (int i = 0; i < sheet.Odds.Length; i++) sheet.Odds[i].gameObject.SetActive(false);
            for (int i = 0; i < sheet.Time.Length; i++) sheet.Time[i].gameObject.SetActive(false);
            sheet.OddsText.gameObject.SetActive(true);
            DeskStyle.Type(sheet.OddsText, "OBSERVE / ADVANCE / CONCEAL → EXECUTE AT 60 PREP");
            sheet.TimeText.gameObject.SetActive(sheet.Root.sizeDelta.y >= 104f);
            DeskStyle.Type(sheet.TimeText, FieldWords.Clock(FieldCatalog.TravelSeconds(TravelMetres(objective))) +
                " INSERTION · ACTIVE FIELD WORK · 3 MIN POST / 1–3 CHARGES");
            sheet.Post.gameObject.SetActive(sheet.Root.sizeDelta.y >= 104f);
            DeskStyle.Type(sheet.Post, refusal ?? FieldWords.BriefEffect(mission, 0));
            sheet.Post.color = refusal == null ? DeskStyle.Khaki : DeskStyle.Stamp;
            sheet.Refusal.gameObject.SetActive(false);
            sheet.DispatchedFrame.gameObject.SetActive(false);
            bool armed = refusal == null && ConfirmArmed(selectedTeam, (int)mission, objective.Anchor, false);
            SetStamp(sheet.Launch, armed ? "COMMIT" : refusal == null ? "PREVIEW" : "UNAVAILABLE",
                refusal == null, armed, refusal ?? (FieldWords.BriefEffect(mission, 0) +
                " Operator preparation unlocks better quality, more charges and advanced branches. " + LaunchTipTail(mission)));
            sheet.MissionRail.color = refusal == null ? RoomPaint.Ready : DeskStyle.Khaki;
            sheet.Paper.color = hoverMission == (int)mission ? DeskStyle.Tape : DeskStyle.Map;
        }
        /// <summary>Launch tooltip tail: forecast changes with the host's real threat scan.</summary>
        private static string LaunchTipTail(FieldMission mission) => "Work the operation after insertion. Observe builds intel; advance builds preparation; conceal trades progress for safety." +
            (mission == FieldMission.Seize ? " Safehouse FORTIFY needs the SQD fortify perk." : "");

        private static bool MissionAllowedAt(FieldMission mission, in FieldObjective objective) =>
            FieldCatalog.Allowed(mission, objective.Kind) &&
            (!objective.Friendly || mission == FieldMission.Recon);

        private static string MissionLabel(FieldMission mission, in FieldObjective objective) =>
            objective.Friendly && mission == FieldMission.Recon ? "OBSERVE"
            : objective.Friendly && mission == FieldMission.Steal ? "LISTEN"
            : FieldWords.Mission(mission);

        /// <summary>A short button word for a refusal that the sheet already explains in full.</summary>
        private static string Brief(string refusal)
        {
            if (refusal.StartsWith("NEEDS", StringComparison.Ordinal)) return "SHORT OPS RESERVE";
            if (refusal.StartsWith("TEAM", StringComparison.Ordinal)) return "TEAM NOT READY";
            if (refusal.StartsWith("AWAITING", StringComparison.Ordinal)) return "AWAITING HOST";
            if (refusal.StartsWith("LINK", StringComparison.Ordinal)) return "LINK STALE";
            return "UNAVAILABLE";
        }

        private void PreviewHover(SpecOpsDetachment detachment, int slot)
        {
            bool committedPreview = Live(detachment) && slot >= 0 && confirmMission >= 0 &&
                ConfirmArmed(selectedTeam, confirmMission, detachment.Objective(slot).Anchor, false);
            int preview = committedPreview ? confirmMission : hoverMission;
            bool show = preview >= 0 && Live(detachment) && slot >= 0;
            if (!show)
            {
                map.Preview(false, 0f, 0f, null);
                return;
            }
            FieldObjective o = detachment.Objective(slot);
            var mission = (FieldMission)preview;
            float travel = FieldCatalog.TravelSeconds(TravelMetres(o));
            float cost = support != null ? support.SpecOpsMissionCost(mission) : FieldCatalog.MissionCost(mission);
            map.Preview(true, o.X, o.Z, "PREVIEW · " + MissionLabel(mission, o) + " · OPS " + Figure(cost) + " · " +
                FieldWords.Clock(travel) + " INSERTION + OPERATOR FIELD WORK + " + FieldWords.Clock(FieldCatalog.TaskSeconds(mission)) +
                " DELIVERY · POST UP TO 180 s · NOT SENT");
        }

        private void WriteTimeline(SpecOpsDetachment detachment, double now, bool live)
        {
            for (int t = 0; t < TeamCount; t++)
            {
                Lane lane = lanes[t];
                FieldTeam team = live ? detachment.Team(t) : default;
                float remaining = live ? (float)detachment.Remaining(t, now) : 0f;
                int count = live ? TimelineMath.Team(team.State, remaining, team.Mission, team.Rank, TimelineWindow, segments) : 0;
                bool any = false;
                for (int s = 0; s < LaneSegments; s++)
                {
                    bool show = s < count && segments[s].Kind != LaneKind.Empty;
                    Image bar = lane.Segments[s];
                    TMP_Text words = lane.Words[s];
                    bar.enabled = show;
                    if (!show)
                    {
                        if (words.text.Length > 0) words.text = "";
                        continue;
                    }
                    any = true;
                    LaneSegment segment = segments[s];
                    float x = laneX0 + segment.Start * laneWidth;
                    float width = Mathf.Max(2f, (segment.End - segment.Start) * laneWidth - 2f);
                    RectTransform r = bar.rectTransform;
                    r.anchoredPosition = new Vector2(x, r.anchoredPosition.y);
                    r.sizeDelta = new Vector2(width, 14f);
                    Color hue = LaneColour(segment.Kind, team.Mission);
                    bar.color = segment.Projected ? hue.WithAlpha(0.5f) : hue;
                    bar.sprite = segment.Projected ? OpsSprites.Guard : null;
                    bar.type = segment.Projected ? Image.Type.Tiled : Image.Type.Simple;
                    // The longest wording that fits: projected phases say they depend on a success.
                    string full = LaneWord(segment.Kind) + (segment.Projected
                        ? segment.Kind == LaneKind.OnTask ? " IF EXECUTED"
                        : segment.Kind == LaneKind.Holding ? " IF SUCCESSFUL" : "" : "");
                    string text = width > full.Length * 7.2f + 8f ? full
                        : width > LaneWord(segment.Kind).Length * 7.2f + 8f ? LaneWord(segment.Kind)
                        : width > LaneCode(segment.Kind).Length * 7.2f + 8f ? LaneCode(segment.Kind) : "";
                    if (words.text != text) words.text = text;
                    words.rectTransform.anchoredPosition = new Vector2(x + 4f, words.rectTransform.anchoredPosition.y);
                    words.rectTransform.sizeDelta = new Vector2(width - 6f, 14f);
                    words.color = DeskStyle.Ink;
                }
                DeskStyle.Type(lane.Empty, any ? "" : !live ? "" : team.State == TeamState.Deciding
                    ? "FIELD CONTROL · WORK OPERATION IN THE DOSSIER" : team.Formed ? "READY · NO MISSION"
                    : team.Last == MissionOutcome.Lost ? "LOST · SLOT OPEN" : "NOT FORMED");
                lane.Name.color = team.Formed ? DeskStyle.Ink : DeskStyle.Khaki;
            }
        }

        private static Color LaneColour(LaneKind kind, FieldMission mission)
        {
            switch (kind)
            {
                case LaneKind.EnRoute: return RoomPaint.Instrument;
                case LaneKind.Deciding: return AvTheme.RailCaution;
                case LaneKind.OnTask: return AvTheme.RailCaution;
                case LaneKind.Holding: return FieldTones.Post(mission);
                default: return DeskStyle.Khaki;
            }
        }

        private static string LaneWord(LaneKind kind)
        {
            switch (kind)
            {
                case LaneKind.EnRoute: return "EN ROUTE";
                case LaneKind.Deciding: return "FIELD WORK";
                case LaneKind.OnTask: return "ON TASK";
                case LaneKind.Holding: return "HOLDING";
                case LaneKind.Recovering: return "RESTING";
                default: return "";
            }
        }

        private static string LaneCode(LaneKind kind)
        {
            switch (kind)
            {
                case LaneKind.EnRoute: return "OUT";
                case LaneKind.Deciding: return "DEC";
                case LaneKind.OnTask: return "TASK";
                case LaneKind.Holding: return "HOLD";
                case LaneKind.Recovering: return "REST";
                default: return "";
            }
        }

        // ---- Game reads (presentation only) ------------------------------------------------------

        private static float TravelMetres(in FieldObjective objective)
        {
            GameManager.GetLocalPlayer<Player>(out Player player);
            return SpecOpsTheater.TravelMetres(player != null ? player.HQ : null, objective.X, objective.Z);
        }

        private static string Distance(in FieldObjective objective)
        {
            float metres = TravelMetres(objective);
            return metres < 0f ? "NO HOME BASE" : FieldWords.Km(metres).ToUpperInvariant() + " FROM BASE";
        }

        private static int LocalFaction()
        {
            GameManager.GetLocalPlayer<Player>(out Player player);
            return player != null && player.HQ != null ? player.HQ.GetInstanceID() : 0;
        }

        /// <summary>The faction's own airbases, for orientation and route starts; named once per base.</summary>
        private void LocalHomes()
        {
            int count = 0;
            if (GameManager.GetLocalPlayer<Player>(out Player player) && player != null && player.HQ != null)
            {
                foreach (Airbase airbase in player.HQ.GetAirbases())
                {
                    if (count >= homeXs.Length) break;
                    if (airbase == null || airbase.AttachedAirbase || airbase.CurrentHQ != player.HQ) continue;
                    GlobalPosition at = (airbase.center != null ? airbase.center.position : airbase.transform.position).ToGlobalPosition();
                    homeXs[count] = at.x;
                    homeZs[count] = at.z;
                    if (homeBases[count] != airbase)
                    {
                        homeBases[count] = airbase;
                        homeNames[count] = HomeName(airbase);
                    }
                    count++;
                }
            }
            for (int i = count; i < homeBases.Length; i++) homeBases[i] = null;
            homeCount = count;
            map.SetHomes(homeXs, homeZs, homeNames, homeCount);
        }

        private static string HomeName(Airbase airbase)
        {
            string name = null;
            try
            {
                if (airbase.SavedAirbase != null) name = airbase.SavedAirbase.DisplayName;
            }
            catch (Exception)
            {
                name = null;
            }
            if (string.IsNullOrWhiteSpace(name)) name = airbase.name;
            name = PlaceNames.Clean(name);
            return name.Length < 3 ? "HOME BASE" : name;
        }

        /// <summary>Offline fixtures: homes without a game.</summary>
        internal void SetHomes(float[] xs, float[] zs, string[] names, int count)
        {
            homeCount = Mathf.Min(count, homeXs.Length);
            for (int i = 0; i < homeCount; i++)
            {
                homeXs[i] = xs[i];
                homeZs[i] = zs[i];
                homeNames[i] = names[i];
            }
            map.SetHomes(homeXs, homeZs, homeNames, homeCount);
            nextHomes = float.MaxValue;
            layoutDue = true;
        }

        /// <summary>Offline fixtures: select a team.</summary>
        internal void Select(int team, int slot)
        {
            SelectTeam(team);
            if (slot >= 0) SelectSlot(slot);
        }

        internal DeskMap Map => map;

        private static string Figure(float value) => Mathf.Round(value).ToString("N0", Invariant);
    }
}
