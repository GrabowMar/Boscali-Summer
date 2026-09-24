using System;
using System.Collections.Generic;
using System.Globalization;
using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using BoscaliSummer.Features.Support.Presentation.Viz;
using BoscaliSummer.Features.Support.Presentation.Window;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using LayoutMotion = BoscaliSummer.Features.Support.Domain.Layout.Motion;

namespace BoscaliSummer.Features.Support.Presentation.Views
{
    /// <summary>
    /// SPEC OPS mission control: a horizontal team roster, a clipped terrain map, the objective
    /// and mission decision pane, and a common operations timeline. Controls request host orders.
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
            public TMP_Text Letter, Callsign, Rank, State, Clock, Detail, Lost;
            public Image StateFrame, Bar, BarTrack, LostFrame, SelectedRail;
            public Image[] Chevrons;
            public Stamp Action;
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
        private readonly Rect[] sections = new Rect[6];
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
        private RectTransform folderBody;
        private TMP_Text note, planObjective, planTeam, planMission;
        private RectTransform emptyNote;
        private TMP_Text emptyTitle, emptyBody;
        private float laneX0, laneWidth;

        // ---- State -----------------------------------------------------------------------------

        private int selectedTeam;
        private int selectedAnchor;
        private bool anchorChosen;
        private int hoverMission = -1;
        private string noteText = "NO ORDERS SENT YET · 01 PICK OBJECTIVE  /  02 ASSIGN TEAM  /  03 LAUNCH MISSION";
        private bool noteBad;
        private bool awaiting;
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
        public string NotchLabel => "SPEC OPS";
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
            float top = DeskStyle.BannerHeight + DeskStyle.HeaderHeight + 14f;
            float timelineTop = h - g - DeskStyle.TimelineHeight;
            float folderW = Mathf.Clamp(w * 0.30f, DeskStyle.FolderWidth, 560f);
            float folderX = w - g - folderW;
            float focusX = g;
            float noteTop = top + 154f;
            focus = new Rect(focusX, -(noteTop + 42f), folderX - g - focusX, timelineTop - 12f - (noteTop + 42f));
            folderRect = new Rect(folderX, -top, folderW, timelineTop - 12f - top);

            AvKit.Panel(room, new Rect(0f, 0f, w, h), DeskStyle.Map);
            map.Build(room, focus, focus, SelectSlot);
            map.Board.Clicked = (local, button) =>
            {
                // A click that missed a token still selects the objective under it.
                int hit = map.Board.Hit(local);
                if (hit >= 0) SelectSlot(hit);
            };

            BuildHeader(room, w);
            BuildRoster(room, new Rect(g, -top, focus.width, 142f));
            BuildFolder(room);
            BuildTimeline(room, new Rect(g, -timelineTop, w - g * 2f, DeskStyle.TimelineHeight));
            BuildNote(room, new Rect(focus.x, -noteTop, focus.width, 42f));
            BuildEmpty(room);
            map.SetObstacles(new Rect(focus.x, focus.y - focus.height + 42f, focus.width, 42f));

            sections[0] = new Rect(0f, 0f, w, DeskStyle.BannerHeight + DeskStyle.HeaderHeight);
            sections[1] = ToTopDown(new Rect(g, -top, focus.width, 142f));
            sections[2] = ToTopDown(focus);
            sections[3] = ToTopDown(new Rect(focus.x, -noteTop, focus.width, 42f));
            sections[4] = ToTopDown(folderRect);
            sections[5] = ToTopDown(new Rect(g, -timelineTop, w - g * 2f, DeskStyle.TimelineHeight));
            layoutDue = true;
        }

        private static Rect ToTopDown(Rect avKit) => new Rect(avKit.x, -avKit.y, avKit.width, avKit.height);

        private static RectTransform Group(RectTransform parent, string name, Rect at, out CanvasGroup fade)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            AvKit.Place(rect, at);
            fade = go.GetComponent<CanvasGroup>();
            return rect;
        }

        private void BuildHeader(RectTransform room, float w)
        {
            headerGroup = Group(room, "Header", new Rect(0f, 0f, w, DeskStyle.BannerHeight + DeskStyle.HeaderHeight), out headerFade);
            AvKit.Panel(headerGroup, new Rect(0f, 0f, w, DeskStyle.BannerHeight), DeskStyle.Banner);
            TMP_Text banner = AvKit.Label(headerGroup, "SPECIAL OPERATIONS  /  MISSION CONTROL",
                new Rect(0f, 0f, w, DeskStyle.BannerHeight), DeskStyle.BannerInk, 11f, FontStyles.Bold, TextAlignmentOptions.Center);
            banner.characterSpacing = 10f;

            float g = DeskStyle.Gutter;
            AvKit.Panel(headerGroup, new Rect(g, -DeskStyle.BannerHeight, 700f, DeskStyle.HeaderHeight), DeskStyle.Paper);
            Image glyph = AvKit.Panel(headerGroup, new Rect(g + 12f, -DeskStyle.BannerHeight - 8f, 32f, 32f), DeskStyle.Stamp);
            glyph.sprite = OpsSprites.Glyph(OpsSprites.G.SpecOps);
            DeskStyle.Title(headerGroup, FieldWords.Title, new Rect(g + 52f, -DeskStyle.BannerHeight - 3f, 330f, 24f),
                DeskStyle.Stencil, DeskStyle.Ink);
            summary = DeskStyle.Body(headerGroup, new Rect(g + 52f, -DeskStyle.BannerHeight - 26f, 610f, 18f),
                DeskStyle.TypewriterSmall, DeskStyle.Khaki);

            float tagW = 420f;
            Image clock = AvKit.Panel(headerGroup, new Rect(w - g - tagW, -DeskStyle.BannerHeight, tagW, DeskStyle.HeaderHeight),
                DeskStyle.Paper);
            clock.rectTransform.localEulerAngles = Vector3.zero;
            dtg = DeskStyle.Title(clock.rectTransform, "", new Rect(14f, -4f, 250f, 22f), DeskStyle.StencilSmall, DeskStyle.Ink);
            dtg.characterSpacing = 4f;
            alloc = DeskStyle.Body(clock.rectTransform, new Rect(14f, -26f, 250f, 18f), DeskStyle.TypewriterSmall, DeskStyle.Khaki);
            link = AvKit.Label(clock.rectTransform, "", new Rect(tagW - 150f, -12f, 136f, 24f), DeskStyle.Stamp, 11f,
                FontStyles.Bold, TextAlignmentOptions.Center);
            link.characterSpacing = 6f;
            Image linkFrame = AvKit.Panel(clock.rectTransform, new Rect(tagW - 150f, -12f, 136f, 24f), DeskStyle.Stamp, OpsSprites.Stamp);
            linkFrame.rectTransform.SetSiblingIndex(link.rectTransform.GetSiblingIndex());
        }

        private void BuildRoster(RectTransform room, Rect at)
        {
            rosterGroup = Group(room, "Roster", at, out _);
            float pitch = (at.width - DeskStyle.TagGap * (TeamCount - 1)) / TeamCount;
            for (int i = 0; i < TeamCount; i++) tags[i] = BuildTag(i, new Rect(i * (pitch + DeskStyle.TagGap), 0f, pitch, at.height));
        }

        private DogTag BuildTag(int team, Rect at)
        {
            var tag = new DogTag();
            tag.Root = Group(rosterGroup, "Tag" + team, at, out tag.Group);
            tag.Root.pivot = new Vector2(0f, 1f);
            float w = at.width, h = at.height;
            tag.Select = RoomControl.Create(tag.Root, new Rect(0f, 0f, w, h), () => SelectTeam(team), "SelectTeam");
            tag.Select.WithTooltip(FieldWords.Callsign(team) + " — select this team for the next mission (Tab cycles).");
            tag.Body = AvKit.Panel(tag.Root, new Rect(0f, 0f, w, h), DeskStyle.Paper);
            tag.Outline = OutlineThick(tag.Root, new Rect(-3f, 3f, w + 6f, h + 6f), DeskStyle.Ink, 2f);
            tag.SelectedRail = AvKit.Rule(tag.Root, new Rect(0f, 0f, w, 3f), AvTheme.RailInfo);
            tag.Letter = DeskStyle.Title(tag.Root, FieldWords.Callsign(team).Substring(0, 1), new Rect(12f, -8f, 28f, 30f), 26f,
                DeskStyle.Ink, TextAlignmentOptions.Center);
            tag.Letter.characterSpacing = 0f;
            tag.Callsign = DeskStyle.Title(tag.Root, FieldWords.Callsign(team), new Rect(46f, -8f, w - 58f, 24f), DeskStyle.Stencil,
                DeskStyle.Ink);
            tag.Chevrons = new Image[FieldCatalog.MaxRank];
            for (int i = 0; i < tag.Chevrons.Length; i++)
            {
                tag.Chevrons[i] = AvKit.Panel(tag.Root, new Rect(12f + i * 12f, -39f, 10f, 10f), DeskStyle.Ink);
                tag.Chevrons[i].sprite = OpsSprites.Glyph(OpsSprites.G.Team);
            }
            tag.Rank = DeskStyle.Body(tag.Root, new Rect(54f, -34f, w - 66f, 18f), DeskStyle.TypewriterSmall, DeskStyle.Khaki);
            tag.StateFrame = AvKit.Panel(tag.Root, new Rect(12f, -56f, 126f, 22f), DeskStyle.Ink, OpsSprites.Stamp);
            tag.State = AvKit.Label(tag.Root, "", new Rect(12f, -56f, 126f, 22f), DeskStyle.Ink, 12f, FontStyles.Bold,
                TextAlignmentOptions.Center);
            tag.State.characterSpacing = 1f;
            tag.Clock = DeskStyle.Body(tag.Root, new Rect(144f, -56f, w - 156f, 22f), 16f, DeskStyle.Ink, TextAlignmentOptions.MidlineRight);
            tag.Detail = DeskStyle.Body(tag.Root, new Rect(12f, -80f, w - 24f, 28f), DeskStyle.TypewriterSmall, DeskStyle.Ink,
                TextAlignmentOptions.TopLeft, true);
            tag.BarTrack = AvKit.Panel(tag.Root, new Rect(12f, -h + 33f, w - 24f, 3f), DeskStyle.Khaki.WithAlpha(0.3f));
            tag.Bar = AvKit.Panel(tag.Root, new Rect(12f, -h + 33f, 0f, 3f), DeskStyle.Ink);
            tag.Action = BuildStamp(tag.Root, new Rect(12f, -h + 27f, w - 24f, 23f), () => RaiseOrRecall(team), 11f);
            tag.LostFrame = AvKit.Panel(tag.Root, new Rect(w * 0.5f - 70f, -h * 0.5f + 26f, 140f, 44f), AvTheme.RailDanger, OpsSprites.Stamp);
            tag.Lost = DeskStyle.Title(tag.LostFrame.rectTransform, "LOST", new Rect(0f, 0f, 140f, 44f), 24f, AvTheme.RailDanger,
                TextAlignmentOptions.Center);
            tag.LostFrame.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            tag.LostFrame.rectTransform.anchoredPosition = new Vector2(w * 0.5f, -h * 0.5f + 4f);
            tag.LostFrame.rectTransform.localEulerAngles = new Vector3(0f, 0f, 12f);
            tag.LostFrame.gameObject.SetActive(false);
            return tag;
        }

        private static Image[] OutlineThick(RectTransform parent, Rect at, Color color, float thickness)
        {
            var edges = new[]
            {
                AvKit.Rule(parent, new Rect(at.x, at.y, at.width, thickness), color),
                AvKit.Rule(parent, new Rect(at.x, at.y - at.height + thickness, at.width, thickness), color),
                AvKit.Rule(parent, new Rect(at.x, at.y, thickness, at.height), color),
                AvKit.Rule(parent, new Rect(at.x + at.width - thickness, at.y, thickness, at.height), color)
            };
            return edges;
        }

        private Stamp BuildStamp(RectTransform parent, Rect at, Action click, float size)
        {
            var stamp = new Stamp();
            stamp.Control = RoomControl.Create(parent, at, click, "Stamp");
            RectTransform host = stamp.Control.Rect;
            stamp.Fill = AvKit.Panel(host, new Rect(0f, 0f, at.width, at.height), DeskStyle.Tape.WithAlpha(0f));
            stamp.Frame = AvKit.Panel(host, new Rect(0f, 0f, at.width, at.height), DeskStyle.Ink, OpsSprites.Stamp);
            stamp.Text = AvKit.Label(host, "", new Rect(4f, 0f, at.width - 8f, at.height), DeskStyle.Ink, size,
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
                : stamp.Danger ? DeskStyle.Stamp : stamp.Primary ? AvTheme.RailReady : DeskStyle.Ink;
            stamp.Frame.color = ink;
            stamp.Text.color = stamp.Primary && c.Enabled ? AvTheme.TextPrimary : ink;
            stamp.Fill.color = !c.Enabled ? DeskStyle.Khaki.WithAlpha(0.08f)
                : stamp.Primary ? AvTheme.RailReady.WithAlpha(c.Pressed ? 0.4f : c.Hovered ? 0.28f : 0.14f)
                : c.Pressed ? DeskStyle.Tape : c.Hovered ? DeskStyle.Tape.WithAlpha(0.7f) : DeskStyle.Tape.WithAlpha(0.18f);
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

        private void BuildFolder(RectTransform room)
        {
            folderGroup = Group(room, "Folder", folderRect, out folderFade);
            float w = folderRect.width, h = folderRect.height;
            AvKit.Panel(folderGroup, new Rect(0f, 0f, w, h), DeskStyle.Khaki.WithAlpha(0.3f));
            AvKit.Panel(folderGroup, new Rect(0f, 0f, w, h), DeskStyle.Paper);
            AvKit.Rule(folderGroup, new Rect(0f, 0f, w, 2f), AvTheme.RailInfo);
            AvKit.Rule(folderGroup, new Rect(0f, 0f, 3f, h), AvTheme.RailInfo.WithAlpha(0.7f));
            DeskStyle.Title(folderGroup, "01 / OBJECTIVE", new Rect(18f, -5f, 160f, 20f), 12f, DeskStyle.Ink);
            Stamp previous = BuildStamp(folderGroup, new Rect(180f, -4f, 40f, 24f), () => StepObjective(lastDetachment, -1), 11f);
            SetStamp(previous, "< Q", true, false, "Previous objective");
            Stamp next = BuildStamp(folderGroup, new Rect(226f, -4f, 40f, 24f), () => StepObjective(lastDetachment, 1), 11f);
            SetStamp(next, "E >", true, false, "Next objective");
            folderIndex = DeskStyle.Body(folderGroup, new Rect(w - 200f, -8f, 184f, 16f), DeskStyle.TypewriterSmall, DeskStyle.Khaki,
                TextAlignmentOptions.MidlineRight);

            var bodyObject = new GameObject("Brief", typeof(RectTransform));
            folderBody = (RectTransform)bodyObject.transform;
            folderBody.SetParent(folderGroup, false);
            AvKit.Place(folderBody, new Rect(0f, 0f, w, h));

            objectiveGlyph = AvKit.Panel(folderBody, new Rect(18f, -32f, 36f, 36f), DeskStyle.Stamp);
            objectiveName = DeskStyle.Title(folderBody, "", new Rect(66f, -32f, w - 84f, 38f), 20f, DeskStyle.Ink,
                TextAlignmentOptions.TopLeft);
            objectiveName.characterSpacing = 4f;
            objectiveName.enableWordWrapping = true;
            objectiveName.overflowMode = TextOverflowModes.Truncate;
            objectiveName.enableAutoSizing = true;
            objectiveName.fontSizeMin = 13f;
            objectiveName.fontSizeMax = 20f;
            objectiveLine = DeskStyle.Body(folderBody, new Rect(66f, -74f, w - 84f, 16f), DeskStyle.TypewriterSmall, DeskStyle.Khaki);

            float factW = (w - 36f - 16f) / 3f;
            BuildFact(folderBody, "THREAT · 2 KM", new Rect(18f, -96f, factW, 62f), out threatFigure, out threatWords);
            BuildFact(folderBody, "RADARS", new Rect(18f + factW + 8f, -96f, factW, 62f), out radarFigure, out radarWords);
            BuildFact(folderBody, "SCOUTING", new Rect(18f + (factW + 8f) * 2f, -96f, factW, 62f), out scoutFigure, out scoutWords);
            teamOn = DeskStyle.Body(folderBody, new Rect(18f, -162f, w - 36f, 16f), DeskStyle.TypewriterSmall, DeskStyle.Ink);

            DeskStyle.Title(folderBody, "02 / ASSIGN TEAM · TAB", new Rect(18f, -184f, 210f, 18f), 12f, DeskStyle.Ink);
            float chipW = (w - 36f - 3f * 6f) / TeamCount;
            for (int i = 0; i < TeamCount; i++) chips[i] = BuildChip(i, new Rect(18f + i * (chipW + 6f), -204f, chipW, 30f));

            bool fullBrief = h > 550f;
            float sheetTop = fullBrief ? 266f : 246f;
            if (fullBrief)
            {
                DeskStyle.Title(folderBody, "03 / CHOOSE MISSION · LAUNCH", new Rect(18f, -242f, w - 36f, 18f),
                    12f, DeskStyle.Ink);
                AvKit.Rule(folderBody, new Rect(18f, -262f, w - 36f, 1f), DeskStyle.Khaki.WithAlpha(0.45f));
            }
            float sheetH = (h - sheetTop - 14f - (MissionCount - 1) * 8f) / MissionCount;
            for (int i = 0; i < MissionCount; i++)
                sheets[i] = BuildSheet((FieldMission)i, new Rect(14f, -(sheetTop + i * (sheetH + 8f)), w - 28f, sheetH));

            folderEmpty = DeskStyle.Body(folderGroup, new Rect(24f, -40f, w - 48f, 120f), DeskStyle.Typewriter, DeskStyle.Ink,
                TextAlignmentOptions.TopLeft, true);
        }

        private static void BuildFact(RectTransform parent, string title, Rect at, out TMP_Text figure, out TMP_Text words)
        {
            AvKit.Outline(parent, at, DeskStyle.Khaki);
            TMP_Text heading = AvKit.Label(parent, title, new Rect(at.x + 8f, at.y - 3f, at.width - 16f, 13f), DeskStyle.Khaki, 10f,
                FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
            heading.characterSpacing = 3f;
            figure = DeskStyle.Title(parent, "", new Rect(at.x + 8f, at.y - 15f, at.width - 16f, 30f), 26f, DeskStyle.Ink);
            figure.characterSpacing = 1f;
            words = DeskStyle.Body(parent, new Rect(at.x + 8f, at.y - 45f, at.width - 12f, 14f), 10f, DeskStyle.Khaki);
        }

        private Chip BuildChip(int team, Rect at)
        {
            var chip = new Chip();
            chip.Control = RoomControl.Create(folderBody, at, () => SelectTeam(team), "TeamChip");
            RectTransform host = chip.Control.Rect;
            chip.Fill = AvKit.Panel(host, new Rect(0f, 0f, at.width, at.height), DeskStyle.Ink.WithAlpha(0f));
            chip.Outline = AvKit.Outline(host, new Rect(0f, 0f, at.width, at.height), DeskStyle.Khaki);
            chip.Text = AvKit.Label(host, "", new Rect(4f, 0f, at.width - 8f, at.height), DeskStyle.Ink, 11f, FontStyles.Bold,
                TextAlignmentOptions.Center);
            chip.Control.Changed = _ => nextPaint = true;
            return chip;
        }

        private bool nextPaint;

        private Sheet BuildSheet(FieldMission mission, Rect at)
        {
            var sheet = new Sheet();
            int index = (int)mission;
            var go = new GameObject("Sheet" + index, typeof(RectTransform));
            sheet.Root = (RectTransform)go.transform;
            sheet.Root.SetParent(folderBody, false);
            AvKit.Place(sheet.Root, at);
            float w = at.width, h = at.height;
            sheet.Hover = RoomControl.Create(sheet.Root, new Rect(0f, 0f, w, h), null, "SheetHover");
            sheet.Hover.Changed = c =>
            {
                if (c.Hovered) hoverMission = index;
                else if (hoverMission == index) hoverMission = -1;
                nextPaint = true;
            };
            sheet.Paper = AvKit.Panel(sheet.Root, new Rect(0f, 0f, w, h), DeskStyle.Map);
            AvKit.Rule(sheet.Root, new Rect(3f, 0f, w - 3f, 1f), DeskStyle.Khaki.WithAlpha(0.6f));
            AvKit.Rule(sheet.Root, new Rect(10f, -h + 1f, w - 20f, 1f), DeskStyle.Khaki.WithAlpha(0.45f));
            sheet.MissionRail = AvKit.Rule(sheet.Root, new Rect(0f, 0f, 3f, h), AvTheme.RailInfo);
            int symbol = mission == FieldMission.Recon ? OpsSprites.G.Spot
                : mission == FieldMission.Sabotage ? OpsSprites.G.Suppress
                : mission == FieldMission.Steal ? OpsSprites.G.SpecOps : OpsSprites.G.Fortify;
            Image missionIcon = AvKit.Panel(sheet.Root, new Rect(12f, -7f, 22f, 22f), AvTheme.RailInfo,
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
            Color[] odds = { AvTheme.RailReady, AvTheme.RailCaution, AvTheme.RailDanger };
            for (int i = 0; i < 3; i++)
            {
                sheet.Odds[i] = AvKit.Panel(sheet.Root, new Rect(12f, -60f, 10f, 10f), odds[i]);
                if (i == 1) { sheet.Odds[i].sprite = OpsSprites.Guard; sheet.Odds[i].type = Image.Type.Tiled; }
                if (i == 2) { sheet.Odds[i].sprite = OpsSprites.Dash; sheet.Odds[i].type = Image.Type.Tiled; }
            }
            sheet.OddsText = DeskStyle.Body(sheet.Root, new Rect(12f, -72f, barW, 14f), DeskStyle.TypewriterSmall, DeskStyle.Ink);
            sheet.Time = new Image[3];
            Color[] time = { AvTheme.RailInfo, AvTheme.RailCaution, AvTheme.RailReady };
            for (int i = 0; i < 3; i++)
            {
                sheet.Time[i] = AvKit.Panel(sheet.Root, new Rect(12f, -90f, 10f, 6f), time[i]);
                if (i == 2) { sheet.Time[i].sprite = OpsSprites.Guard; sheet.Time[i].type = Image.Type.Tiled; }
            }
            sheet.TimeText = DeskStyle.Body(sheet.Root, new Rect(12f, -98f, barW, 14f), DeskStyle.TypewriterSmall, DeskStyle.Ink);
            sheet.Post = DeskStyle.Body(sheet.Root, new Rect(12f, -h + 17f, w - 24f, 14f), DeskStyle.TypewriterSmall, DeskStyle.Khaki);
            sheet.Refusal = AvKit.Label(sheet.Root, "", new Rect(12f, -66f, barW, 48f), DeskStyle.Stamp, 12f, FontStyles.Bold,
                TextAlignmentOptions.MidlineLeft, true);
            sheet.Refusal.characterSpacing = 2f;
            sheet.Launch = BuildStamp(sheet.Root, new Rect(w - 12f - 120f, -62f, 120f, 50f), () => Launch(mission), 14f);
            sheet.Launch.Primary = true;
            PaintStamp(sheet.Launch);
            sheet.Launch.Text.enableAutoSizing = true;
            sheet.Launch.Text.fontSizeMin = 10f;
            sheet.Launch.Text.fontSizeMax = 14f;
            sheet.Launch.Text.enableWordWrapping = true;
            sheet.DispatchedFrame = AvKit.Panel(sheet.Root, new Rect(0f, 0f, 220f, 44f), DeskStyle.Stamp, OpsSprites.Stamp);
            sheet.DispatchedFrame.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            sheet.DispatchedFrame.rectTransform.anchoredPosition = new Vector2(w * 0.5f, -h * 0.5f);
            sheet.DispatchedFrame.rectTransform.localEulerAngles = Vector3.zero;
            sheet.Dispatched = DeskStyle.Title(sheet.DispatchedFrame.rectTransform, "DISPATCHED", new Rect(0f, 0f, 220f, 44f), 20f,
                DeskStyle.Stamp, TextAlignmentOptions.Center);
            sheet.DispatchedFrame.gameObject.SetActive(false);
            if (h < 135f)
            {
                for (int i = 0; i < 3; i++) sheet.Time[i].gameObject.SetActive(false);
                sheet.TimeText.gameObject.SetActive(false);
                sheet.Post.gameObject.SetActive(false);
                sheet.Launch.Control.Rect.anchoredPosition = new Vector2(w - 132f, -34f);
                sheet.Launch.Control.Rect.sizeDelta = new Vector2(120f, 38f);
                sheet.Launch.Fill.rectTransform.sizeDelta = new Vector2(120f, 38f);
                sheet.Launch.Frame.rectTransform.sizeDelta = new Vector2(120f, 38f);
                sheet.Launch.Text.rectTransform.sizeDelta = new Vector2(112f, 38f);
            }
            return sheet;
        }

        private void BuildTimeline(RectTransform room, Rect at)
        {
            timelineGroup = Group(room, "Clipboard", at, out timelineFade);
            float w = at.width, h = at.height;
            AvKit.Panel(timelineGroup, new Rect(0f, 0f, w, h), DeskStyle.Paper);
            AvKit.Rule(timelineGroup, new Rect(0f, 0f, w, 2f), AvTheme.RailInfo);
            AvKit.Rule(timelineGroup, new Rect(0f, 0f, 3f, h), AvTheme.RailInfo.WithAlpha(0.7f));

            float logX = w * 0.62f;
            DeskStyle.Title(timelineGroup, "OPERATIONS · NEXT 10 MIN", new Rect(22f, -12f, 360f, 18f), DeskStyle.StencilSmall,
                DeskStyle.Ink);
            laneX0 = 130f;
            laneWidth = logX - 30f - laneX0;
            for (int t = 0; t < TeamCount; t++)
            {
                float y = -36f - t * 20f;
                var lane = new Lane
                {
                    Name = DeskStyle.Body(timelineGroup, new Rect(22f, y, 100f, 18f), DeskStyle.TypewriterSmall, DeskStyle.Ink),
                    Empty = DeskStyle.Body(timelineGroup, new Rect(laneX0 + 8f, y, laneWidth - 16f, 18f), 10f, DeskStyle.Khaki),
                    Segments = new Image[LaneSegments],
                    Words = new TMP_Text[LaneSegments]
                };
                DeskStyle.Type(lane.Name, FieldWords.Callsign(t));
                AvKit.Rule(timelineGroup, new Rect(laneX0, y - 17f, laneWidth, 1f), DeskStyle.Khaki.WithAlpha(0.4f));
                for (int s = 0; s < LaneSegments; s++)
                {
                    lane.Segments[s] = AvKit.Panel(timelineGroup, new Rect(laneX0, y - 2f, 10f, 14f), AvTheme.RailInfo);
                    lane.Words[s] = AvKit.Label(timelineGroup, "", new Rect(laneX0, y - 2f, 10f, 14f), DeskStyle.Ink, 10f,
                        FontStyles.Bold, TextAlignmentOptions.MidlineLeft);
                    lane.Segments[s].enabled = false;
                }
                lanes[t] = lane;
            }
            for (int m = 0; m <= 10; m += 2)
            {
                float x = laneX0 + laneWidth * (m * 60f / TimelineWindow);
                AvKit.Rule(timelineGroup, new Rect(x, -34f, 1f, 82f), DeskStyle.Khaki.WithAlpha(0.35f));
                TMP_Text label = DeskStyle.Body(timelineGroup, new Rect(x - 30f, -116f, 60f, 14f), 10f, DeskStyle.Khaki,
                    TextAlignmentOptions.Center);
                DeskStyle.Type(label, m == 0 ? "NOW" : "+" + m + " MIN");
            }
            AvKit.Rule(timelineGroup, new Rect(laneX0, -30f, 2f, 86f), DeskStyle.Stamp);

            AvKit.Rule(timelineGroup, new Rect(logX - 12f, -14f, 1f, h - 30f), DeskStyle.Khaki.WithAlpha(0.5f));
            DeskStyle.Title(timelineGroup, "EVENT LOG", new Rect(logX, -12f, 200f, 18f), DeskStyle.StencilSmall, DeskStyle.Ink);
            for (int i = 0; i < LogLines; i++)
                logLines[i] = DeskStyle.Body(timelineGroup, new Rect(logX, -34f - i * 18f, w - logX - 22f, 16f), DeskStyle.TypewriterSmall,
                    i == 0 ? DeskStyle.Ink : DeskStyle.Khaki);
            TMP_Text keys = DeskStyle.Body(timelineGroup, new Rect(logX, -h + 30f, w - logX - 22f, 16f), 10f, DeskStyle.Khaki);
            DeskStyle.Type(keys, "Q E OBJECTIVE · TAB TEAM · 1 2 3 4 LAUNCH · R RAISE/RECALL · F FIT · WHEEL ZOOM · DRAG PAN");
        }

        private void BuildNote(RectTransform room, Rect at)
        {
            noteGroup = Group(room, "Note", at, out noteFade);
            AvKit.Panel(noteGroup, new Rect(0f, 0f, at.width, at.height), DeskStyle.Paper);
            float step = at.width / 3f;
            AvKit.Rule(noteGroup, new Rect(0f, 0f, at.width, 1f), AvTheme.RailInfo);
            AvKit.Rule(noteGroup, new Rect(step, -3f, 1f, 19f), DeskStyle.Khaki.WithAlpha(0.45f));
            AvKit.Rule(noteGroup, new Rect(step * 2f, -3f, 1f, 19f), DeskStyle.Khaki.WithAlpha(0.45f));
            planObjective = PlanLabel(noteGroup, 4f, step - 8f);
            planTeam = PlanLabel(noteGroup, step + 5f, step - 10f);
            planMission = PlanLabel(noteGroup, step * 2f + 5f, step - 10f);
            AvKit.Rule(noteGroup, new Rect(0f, -22f, at.width, 1f), DeskStyle.Khaki.WithAlpha(0.5f));
            AvKit.Rule(noteGroup, new Rect(0f, -23f, 3f, at.height - 23f), AvTheme.RailInfo);
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
            var at = new Rect(focus.x + focus.width * 0.5f - 280f, focus.y - focus.height * 0.5f + 70f, 560f, 140f);
            emptyNote = Group(room, "Empty", at, out _);
            AvKit.Panel(emptyNote, new Rect(4f, -5f, at.width, at.height), DeskStyle.Ink.WithAlpha(0.3f));
            AvKit.Panel(emptyNote, new Rect(0f, 0f, at.width, at.height), DeskStyle.Paper);
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
            AvButton.ClearTooltip();
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
                noteText = support.Status ?? "";
                noteBad = noteText.IndexOf("denied", StringComparison.OrdinalIgnoreCase) >= 0 ||
                          noteText.IndexOf("no response", StringComparison.OrdinalIgnoreCase) >= 0;
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
            for (int i = 0; i < MissionKeys.Length; i++)
                if (Input.GetKeyDown(MissionKeys[i])) { Launch((FieldMission)i); return true; }
            if (Input.GetKeyDown(KeyCode.Tab)) { SelectTeam((selectedTeam + 1) % TeamCount); return true; }
            if (Input.GetKeyDown(KeyCode.Q)) { StepObjective(detachment, -1); return true; }
            if (Input.GetKeyDown(KeyCode.E)) { StepObjective(detachment, 1); return true; }
            if (Input.GetKeyDown(KeyCode.R)) { RaiseOrRecall(selectedTeam); return true; }
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
            nextPaint = true;
        }

        private void SelectSlot(int slot)
        {
            SpecOpsDetachment detachment = lastDetachment ?? support?.LocalDetachment;
            if (detachment == null || slot < 0 || slot >= detachment.ObjectiveCount) return;
            selectedAnchor = detachment.Objective(slot).Anchor;
            anchorChosen = true;
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
                Say("NO OBJECTIVE SELECTED", true);
                return;
            }
            string refusal = LaunchRefusal(detachment, mission, slot);
            if (refusal != null)
            {
                Say(FieldWords.Mission(mission) + " · " + refusal, true);
                return;
            }
            support.RequestSpecOpsLaunch(selectedTeam, mission, detachment.Objective(slot).Anchor);
            Sent(FieldWords.Callsign(selectedTeam) + " · " + FieldWords.Mission(mission) + " → " + detachment.Objective(slot).Name);
        }

        private void RaiseOrRecall(int team)
        {
            SelectTeam(team);
            SpecOpsDetachment detachment = support?.LocalDetachment;
            if (detachment == null) return;
            FieldTeam value = detachment.Team(team);
            if (support.CommandPending)
            {
                Say("AWAITING HOST · ONE ORDER AT A TIME", true);
                return;
            }
            if (!value.Formed)
            {
                SpecOpsDenial denial = detachment.CheckRaise(team);
                float cost = support.SpecOpsRaiseCost();
                if (denial != SpecOpsDenial.None) Say(FieldWords.Denial(denial), true);
                else if (!support.BypassRequirements && support.LocalAllocation + 0.001f < cost)
                    Say("RAISE " + FieldWords.Callsign(team) + " · NEEDS " + Figure(cost) + " ALLOCATION", true);
                else
                {
                    support.RequestSpecOpsRaise(team);
                    Sent("RAISE " + FieldWords.Callsign(team));
                }
                return;
            }
            if (value.Deployed)
            {
                support.RequestSpecOpsRecall(team);
                Sent("RECALL " + FieldWords.Callsign(team));
            }
            else Say(FieldWords.Callsign(team) + " IS " + FieldWords.State(value.State) + " · PICK A MISSION", false);
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
            if (!support.BypassRequirements && support.LocalAllocation + 0.001f < cost)
                return "NEEDS " + Figure(cost) + " ALLOCATION";
            return null;
        }

        private void Say(string line, bool bad)
        {
            noteText = line;
            noteBad = bad;
            awaiting = false;
            nextPaint = true;
        }

        private void Sent(string order)
        {
            noteText = order + " · SENT · AWAITING HOST";
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
                : FieldWords.Summary(detachment) + " · " + detachment.Posts() + (detachment.Posts() == 1 ? " POST HELD" : " POSTS HELD") +
                  " · GROUND READINESS " + detachment.GroundReadiness);
            DateTime utc = DateTime.UtcNow;
            string month = utc.ToString("MMM", Invariant).ToUpperInvariant();
            string clock = "DTG " + utc.ToString("ddHHmm", Invariant) + "Z " + month + " " + utc.ToString("yy", Invariant);
            if (dtg.text != clock) dtg.text = clock;
            float allocation = support != null ? support.LocalAllocation : 0f;
            DeskStyle.Type(alloc, "ALLOCATION " + Figure(allocation));
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
            DeskStyle.Type(note, (awaiting ? "" : "HOST · ") + noteText);
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
            DeskStyle.Type(planObjective, "01 TARGET  /  " + (target ? detachment.Objective(slot).Name : "AWAITING FIX"));
            DeskStyle.Type(planTeam, "02 TEAM  /  " + (live
                ? FieldWords.Callsign(selectedTeam) + " " + (team.Formed ? Short(team.State) : "NOT FORMED") : "NO LINK"));
            DeskStyle.Type(planMission, "03 EFFECT  /  " + (hoverMission >= 0
                ? FieldWords.Mission((FieldMission)hoverMission) : "PICK A MISSION"));
            planObjective.color = target ? DeskStyle.Ink : DeskStyle.Khaki;
            planTeam.color = live && team.Formed && team.State == TeamState.Ready ? AvTheme.RailReady
                : live && team.Formed ? DeskStyle.Ink : DeskStyle.Khaki;
            planMission.color = hoverMission >= 0 ? AvTheme.RailInfo : DeskStyle.Khaki;
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
            tag.SelectedRail.color = selected ? AvTheme.RailReady : formed ? AvTheme.RailInfo : DeskStyle.Khaki.WithAlpha(0.45f);
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
            DeskStyle.Type(tag.Detail, Detail(team, formed, lost));
            float progress = detachment != null ? detachment.Progress(index, now) : 0f;
            float fill = team.State == TeamState.Holding ? 1f - progress : progress;
            tag.Bar.rectTransform.sizeDelta = new Vector2(tag.BarTrack.rectTransform.sizeDelta.x * (formed ? fill : 0f), 5f);
            tag.Bar.color = tone;
            if (tag.LostFrame.gameObject.activeSelf != lost) tag.LostFrame.gameObject.SetActive(lost);

            bool pending = support != null && support.CommandPending;
            if (!team.Formed)
            {
                float cost = support != null ? support.SpecOpsRaiseCost() : FieldCatalog.RaiseCost;
                bool afford = support == null || support.BypassRequirements || support.LocalAllocation + 0.001f >= cost;
                SetStamp(tag.Action, "[R] RAISE · " + Figure(cost), live && !pending && afford, false, afford
                    ? "Form a new RECRUIT team in this slot for " + Figure(cost) + " allocation."
                    : "Raising a team costs " + Figure(cost) + " allocation; you have " + Figure(support.LocalAllocation) + ".");
            }
            else if (team.Deployed)
            {
                SetStamp(tag.Action, "[R] RECALL", live && !pending, true, team.State == TeamState.Holding
                    ? "Leave the " + FieldWords.Post(team.Mission).ToLowerInvariant() + " now; its ability ends with it."
                    : "Abort the mission and bring the team home. Nothing is refunded; no roll is made.");
            }
            else
            {
                SetStamp(tag.Action, team.State == TeamState.Ready ? (selected ? "SELECTED · PICK A MISSION" : "READY") : "RESTING",
                    false, false, team.State == TeamState.Ready
                        ? "Ready. Pick an objective on the map, then choose a mission on the right."
                        : "Recovering; ready again when the clock runs out.");
            }
        }

        private static string Detail(in FieldTeam team, bool formed, bool lost)
        {
            if (!formed) return lost
                ? "Lost on " + (string.IsNullOrEmpty(team.Target) ? "a mission" : team.Target) + ". Raise a new team to fill the slot."
                : "Empty slot. A new team starts as a RECRUIT.";
            switch (team.State)
            {
                case TeamState.EnRoute:
                case TeamState.OnTask:
                    return FieldWords.Mission(team.Mission) + " · " + team.Target + " · " + team.Chance + "% SUCCESS · " + team.Loss + "% LOSS";
                case TeamState.Holding:
                    return FieldWords.Post(team.Mission) + " · " + FieldWords.PostGrant(team.Mission);
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
            DeskStyle.Type(folderEmpty, live ? "" : "No briefing while SPEC OPS is offline. When the host sends the detachment, the selected objective's threat, the odds for each team and the three missions open here.");
            DeskStyle.Type(folderIndex, open ? "OBJECTIVE " + (slot + 1) + "/" + detachment.ObjectiveCount + " · Q E" : "BRIEFING");
            if (!live) return;
            WriteChips(detachment);
            if (!open)
            {
                WriteCatalogue();
                return;
            }

            FieldObjective o = detachment.Objective(slot);
            objectiveGlyph.sprite = OpsSprites.Glyph(DeskMap.Glyph(o.Kind));
            objectiveGlyph.color = o.Hostile ? AvTheme.RailDanger : DeskStyle.Khaki;
            if (objectiveName.text != o.Name) objectiveName.text = o.Name;
            DeskStyle.Type(objectiveLine, (o.Hostile ? "HOSTILE" : "NEUTRAL") + " · " + FieldWords.Kind(o.Kind) + " · " + Distance(o));
            SetFact(threatFigure, threatWords, o.Threat.ToString(Invariant),
                FieldWords.Threat(o.Threat) + (o.Threat == 1 ? " · UNIT" : " · UNITS"),
                o.Threat > 8 ? DeskStyle.Stamp : DeskStyle.Ink);
            SetFact(radarFigure, radarWords, o.Radars.ToString(Invariant), o.Radars == 0 ? "NONE SEEN" : "SEEN NEAR IT", DeskStyle.Ink);
            double scout = detachment.ScoutRemaining(o.Anchor, now);
            SetFact(scoutFigure, scoutWords, scout > 0.0 ? FieldWords.Clock(scout) : "—",
                scout > 0.0 ? "SCOUTED · +10%" : "RECON FIRST: +10%", DeskStyle.Ink);
            int on = detachment.TeamOn(o.Anchor);
            DeskStyle.Type(teamOn, on >= 0
                ? FieldWords.Callsign(on) + " IS " + FieldWords.State(detachment.Team(on).State) + " HERE"
                : "STRIKE IT FIRST: FEWER DEFENDERS, BETTER ODDS");

            for (int i = 0; i < MissionCount; i++) WriteSheet(detachment, (FieldMission)i, slot, now);
        }

        /// <summary>No objective yet: the folder still says what each mission earns and costs.</summary>
        private void WriteCatalogue()
        {
            objectiveGlyph.sprite = OpsSprites.Glyph(OpsSprites.G.SpecOps);
            objectiveGlyph.color = DeskStyle.Khaki;
            const string name = "NO OBJECTIVE YET";
            if (objectiveName.text != name) objectiveName.text = name;
            DeskStyle.Type(objectiveLine, "THE HOST LISTS TARGETS FROM THE MAP EVERY FEW SECONDS");
            SetFact(threatFigure, threatWords, "—", "PICK ONE ON THE TABLE", DeskStyle.Khaki);
            SetFact(radarFigure, radarWords, "—", "PICK ONE ON THE TABLE", DeskStyle.Khaki);
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
                    sheet.Time[k].gameObject.SetActive(false);
                }
                sheet.OddsText.gameObject.SetActive(false);
                sheet.TimeText.gameObject.SetActive(false);
                sheet.Refusal.gameObject.SetActive(true);
                const string why = "ODDS AND TIMING APPEAR WHEN AN OBJECTIVE IS SELECTED";
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
                    ? " — " + FieldWords.Rank(team.Rank) + ", " + FieldWords.State(team.State).ToLowerInvariant() + ". The odds below are for this team."
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

        private void WriteSheet(SpecOpsDetachment detachment, FieldMission mission, int slot, double now)
        {
            Sheet sheet = sheets[(int)mission];
            FieldObjective o = detachment.Objective(slot);
            FieldTeam team = detachment.Team(selectedTeam);
            int rank = team.Rank;
            float cost = support != null ? support.SpecOpsMissionCost(mission) : FieldCatalog.MissionCost(mission);
            DeskStyle.Type(sheet.Cost, "COST " + Figure(cost));
            DeskStyle.Type(sheet.Effect, sheet.Root.sizeDelta.y < 135f
                ? FieldWords.BriefEffect(mission, rank) : FieldWords.Effect(mission, rank));
            string refusal = LaunchRefusal(detachment, mission, slot);
            bool possible = FieldCatalog.Allowed(mission, o.Kind) &&
                            !(mission == FieldMission.Sabotage && o.Radars == 0) &&
                            !(mission == FieldMission.Seize && !detachment.SeizeAvailable);
            int chance = detachment.ChanceFor(selectedTeam, mission, slot, now);
            int loss = detachment.LossFor(selectedTeam, mission, slot, now);
            int fail = Mathf.Max(0, 100 - chance - loss);
            bool showOdds = possible;
            for (int i = 0; i < 3; i++) sheet.Odds[i].gameObject.SetActive(showOdds);
            bool fullSheet = sheet.Root.sizeDelta.y >= 135f;
            for (int i = 0; i < 3; i++) sheet.Time[i].gameObject.SetActive(showOdds && fullSheet);
            sheet.OddsText.gameObject.SetActive(showOdds);
            sheet.TimeText.gameObject.SetActive(showOdds && fullSheet);
            float barW = sheet.OddsText.rectTransform.sizeDelta.x;
            if (showOdds)
            {
                float x = 12f;
                for (int i = 0; i < 3; i++)
                {
                    float width = barW * (i == 0 ? chance : i == 1 ? fail : loss) / 100f;
                    RectTransform r = sheet.Odds[i].rectTransform;
                    r.anchoredPosition = new Vector2(x, r.anchoredPosition.y);
                    r.sizeDelta = new Vector2(Mathf.Max(0f, width - 2f), 10f);
                    x += width;
                }
                DeskStyle.Type(sheet.OddsText, "SUCCESS " + chance + "% · FAIL " + fail + "% · LOSS " + loss + "%");
                float travel = FieldCatalog.TravelSeconds(TravelMetres(o));
                float task = FieldCatalog.TaskSeconds(mission);
                float hold = FieldCatalog.HoldSeconds(rank);
                float total = travel + task + hold;
                x = 12f;
                for (int i = 0; i < 3; i++)
                {
                    float width = barW * (i == 0 ? travel : i == 1 ? task : hold) / total;
                    RectTransform r = sheet.Time[i].rectTransform;
                    r.anchoredPosition = new Vector2(x, r.anchoredPosition.y);
                    r.sizeDelta = new Vector2(Mathf.Max(0f, width - 2f), 6f);
                    x += width;
                }
                DeskStyle.Type(sheet.TimeText, FieldWords.Clock(travel) + " TRAVEL · " + FieldWords.Clock(task) + " TASK · " +
                                               FieldWords.Clock(hold) + " HOLD");
            }
            // One refusal line: why the mission cannot happen here, else why it cannot go now.
            bool refused = refusal != null && possible;
            DeskStyle.Type(sheet.Post, refused ? refusal : "HOLDS A " + FieldWords.Post(mission) + " · ARMS " + FieldWords.PostGrant(mission));
            sheet.Post.color = refused ? DeskStyle.Stamp : DeskStyle.Khaki;
            sheet.Post.gameObject.SetActive(fullSheet);
            sheet.Refusal.gameObject.SetActive(!possible);
            if (!possible)
            {
                string why = !FieldCatalog.Allowed(mission, o.Kind) ? FieldWords.Denial(SpecOpsDenial.WrongObjective)
                    : mission == FieldMission.Sabotage && o.Radars == 0 ? FieldWords.Denial(SpecOpsDenial.NoRadars)
                    : FieldWords.Denial(SpecOpsDenial.SeizeUnavailable);
                if (sheet.Refusal.text != why) sheet.Refusal.text = why;
                sheet.Refusal.color = DeskStyle.Stamp;
            }
            string launchText = refusal == null ? "[" + ((int)mission + 1) + "] LAUNCH" : possible ? "[" + ((int)mission + 1) + "] " + Brief(refusal) : "NOT HERE";
            SetStamp(sheet.Launch, launchText, refusal == null, false, refusal == null
                ? FieldWords.Callsign(selectedTeam) + " goes to " + o.Name + ": " + chance + "% success, " + loss +
                  "% the team is lost, " + Figure(cost) + " allocation."
                : FieldWords.MissionTitle(mission) + " — " + refusal + ".");
            int on = detachment.TeamOn(o.Anchor);
            bool dispatched = on >= 0 && detachment.Team(on).Mission == mission &&
                              (detachment.Team(on).State == TeamState.EnRoute || detachment.Team(on).State == TeamState.OnTask);
            if (sheet.DispatchedFrame.gameObject.activeSelf != dispatched) sheet.DispatchedFrame.gameObject.SetActive(dispatched);
            if (dispatched)
            {
                string word = "DISPATCHED · " + FieldWords.Callsign(on);
                if (sheet.Dispatched.text != word) sheet.Dispatched.text = word;
                sheet.DispatchedFrame.rectTransform.sizeDelta = new Vector2(320f, 44f);
                sheet.Dispatched.rectTransform.sizeDelta = new Vector2(320f, 44f);
            }
            sheet.Paper.color = hoverMission == (int)mission ? DeskStyle.Tape : DeskStyle.Map;
            sheet.MissionRail.color = refusal == null ? AvTheme.RailReady
                : hoverMission == (int)mission ? AvTheme.RailInfo : DeskStyle.Khaki.WithAlpha(0.55f);
        }

        /// <summary>A short button word for a refusal that the sheet already explains in full.</summary>
        private static string Brief(string refusal)
        {
            if (refusal.StartsWith("NEEDS", StringComparison.Ordinal)) return "SHORT ON ALLOCATION";
            if (refusal.StartsWith("TEAM", StringComparison.Ordinal)) return "TEAM NOT READY";
            if (refusal.StartsWith("AWAITING", StringComparison.Ordinal)) return "AWAITING HOST";
            if (refusal.StartsWith("LINK", StringComparison.Ordinal)) return "LINK STALE";
            return "UNAVAILABLE";
        }

        private void PreviewHover(SpecOpsDetachment detachment, int slot)
        {
            bool show = hoverMission >= 0 && Live(detachment) && slot >= 0;
            if (!show)
            {
                map.Preview(false, 0f, 0f, null);
                return;
            }
            FieldObjective o = detachment.Objective(slot);
            var mission = (FieldMission)hoverMission;
            float travel = FieldCatalog.TravelSeconds(TravelMetres(o));
            map.Preview(true, o.X, o.Z, FieldWords.Mission(mission) + " · " + FieldWords.Clock(travel) + " TRAVEL + " +
                                        FieldWords.Clock(FieldCatalog.TaskSeconds(mission)) + " TASK");
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
                    string full = LaneWord(segment.Kind) + (segment.Projected ? " IF IT WORKS" : "");
                    string text = width > full.Length * 7.2f + 8f ? full
                        : width > LaneWord(segment.Kind).Length * 7.2f + 8f ? LaneWord(segment.Kind)
                        : width > LaneCode(segment.Kind).Length * 7.2f + 8f ? LaneCode(segment.Kind) : "";
                    if (words.text != text) words.text = text;
                    words.rectTransform.anchoredPosition = new Vector2(x + 4f, words.rectTransform.anchoredPosition.y);
                    words.rectTransform.sizeDelta = new Vector2(width - 6f, 14f);
                    words.color = DeskStyle.Ink;
                }
                DeskStyle.Type(lane.Empty, any ? "" : !live ? "" : team.Formed ? "READY · NO MISSION" : team.Last == MissionOutcome.Lost ? "LOST · SLOT OPEN" : "NOT FORMED");
                lane.Name.color = team.Formed ? DeskStyle.Ink : DeskStyle.Khaki;
            }
        }

        private static Color LaneColour(LaneKind kind, FieldMission mission)
        {
            switch (kind)
            {
                case LaneKind.EnRoute: return AvTheme.RailInfo;
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
