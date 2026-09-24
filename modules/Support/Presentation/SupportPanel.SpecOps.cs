using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using BoscaliSummer.Features.Support.Presentation.Window;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPEC OPS — the detachment, shaped like SPACE and CYBER. STATUS is the glance: the four
    /// teams in words, four annunciators, the theatre board and the event log. ACTIONS is the
    /// flying half: SPOT and SUPPRESS (earned by held posts) and FORTIFY. Every decision that
    /// grows those abilities — raising teams, choosing objectives, launching and recalling — lives
    /// at the briefing table (<see cref="Views.DeskView"/>). The MFD never spends allocation
    /// except by arming an ability. The log keeps running with the page closed.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const float SpecBannerHeight = 76f;
        private const float TeamLaneMax = 44f;
        private const float LaneHeader = 26f;
        private const float SpecBoardHeight = 192f;

        private static readonly string[] SpecSubTips =
        {
            "Watch floor: the teams, their clocks, the posts they hold and the event log.",
            "The map abilities your held posts grant, plus zone fortification."
        };
        private static readonly string[] SpecTileKeys = { "READY", "IN FIELD", "POSTS", "READINESS" };

        private sealed class TeamRow
        {
            public Image Rail;
            public TMP_Text Name;
            public TMP_Text Rank;
            public TMP_Text Line;
            public Image Bar;
            public Image[] Pips;
            public string LastLine;
            public bool Compact;
        }

        private readonly GameObject[] specSubPages = new GameObject[2];
        private readonly TeamRow[] teamRows = new TeamRow[SpecOpsDetachment.TeamCount];
        private readonly Tile[] specTiles = new Tile[4];
        private readonly string[] specLoop = new string[LoopLines];
        private readonly float[] homeXs = new float[8];
        private readonly float[] homeZs = new float[8];
        private int specSub;
        private int specLoggedSerial = -1;

        private Image specRail;
        private TMP_Text specSummary, specAdvice, specTeamsNote, specActionsNote;
        private TMP_Text[] specLoopLabels;
        private Views.MiniTheatre specTheatre;
        private ArmedBanner specBanner;
        private readonly Image[] postCards = new Image[SpecOpsDetachment.TeamCount];
        private readonly Image[] postRails = new Image[SpecOpsDetachment.TeamCount];
        private readonly TMP_Text[] postTexts = new TMP_Text[SpecOpsDetachment.TeamCount];
        private AvButton openDeskButton;

        private void ResetSpecOpsPage()
        {
            for (int i = 0; i < specSubPages.Length; i++) specSubPages[i] = null;
            for (int i = 0; i < teamRows.Length; i++) teamRows[i] = null;
            for (int i = 0; i < specTiles.Length; i++) specTiles[i] = null;
            for (int i = 0; i < specLoop.Length; i++) specLoop[i] = null;
            specSub = SubStatus;
            specLoggedSerial = -1;
            specRail = null;
            specSummary = specAdvice = specTeamsNote = specActionsNote = null;
            specLoopLabels = null;
            specTheatre = null;
            specBanner = null;
            for (int i = 0; i < postRails.Length; i++)
            {
                postCards[i] = null;
                postRails[i] = null;
                postTexts[i] = null;
            }
            openDeskButton = null;
        }

        // ---- Build -------------------------------------------------------------------------------

        private void BuildSpecOpsPage()
        {
            var page = (RectTransform)shell.CreatePage(TabSpecOps, "SpecOpsPage").transform;
            // Each sub-page carries its own title row with the STATUS / ACTIONS toggle (M1).
            Rect subBody = shell.Body;
            for (int i = 0; i < specSubPages.Length; i++)
            {
                var go = new GameObject("SpecOps" + i, typeof(RectTransform));
                var rect = (RectTransform)go.transform;
                rect.SetParent(page, false);
                AvKit.Stretch(rect);
                specSubPages[i] = go;
            }

            BuildSpecStatus((RectTransform)specSubPages[SubStatus].transform, subBody);
            BuildSpecActions((RectTransform)specSubPages[SubActions].transform, subBody);
            SelectSpecSub(SubStatus);
            SpecLog("DETACHMENT ON THE NET · ALPHA AND BRAVO STANDING BY");
        }

        private void SelectSpecSub(int sub)
        {
            specSub = Mathf.Clamp(sub, 0, specSubPages.Length - 1);
            AvButton.ClearTooltip();
            for (int i = 0; i < specSubPages.Length; i++)
                if (specSubPages[i] != null) specSubPages[i].SetActive(i == specSub);
            nextRefresh = 0f;
        }

        private void BuildSpecStatus(RectTransform root, Rect body)
        {
            Rect content = PageFrame(root, body, OpsDomain.SpecialOperations, FieldWords.Title, SubStatus, SelectSpecSub, SpecSubTips, out _);
            float lanes = Mathf.Min(LaneHeader + TeamLaneMax * SpecOpsDetachment.TeamCount, content.height);
            Rect[] at = Stack(content, new[]
            {
                new StackPiece(1, lanes, 0f, false),
                new StackPiece(2, SpecBannerHeight, SpecBannerHeight, false),
                new StackPiece(3, SpecBoardHeight, 160f, false),
                new StackPiece(3, TileHeight + 20f, TileHeight + 20f, false),
                new StackPiece(4, 0f, 60f, true)
            }, 8f);

            // The hero: four team lanes, a mini operations timeline. OPEN DESK rides on its title row
            // so the desk is one click away at every panel height.
            RectTransform hero = Section(root, "TeamLanes", at[0]);
            float w = at[0].width;
            specTeamsNote = SingleLine(AvStyled.Label(hero, new Rect(0f, -2f, w - 128f, 16f), "", "section-title"));
            openDeskButton = AvStyled.Button(hero, new Rect(w - 120f, 0f, 120f, 22f), "OPEN DESK", "btn", OpenDesk,
                AvButtonStyle.Primary)
                .WithTooltip("The briefing table: raise teams, pick objectives, launch and recall missions.");
            bool compactGrid = at[0].height < LaneHeader + 26f * SpecOpsDetachment.TeamCount;
            float lane = compactGrid
                ? (at[0].height - LaneHeader) * 0.5f
                : Mathf.Clamp((at[0].height - LaneHeader) / SpecOpsDetachment.TeamCount, 26f, TeamLaneMax);
            for (int i = 0; i < teamRows.Length; i++)
                teamRows[i] = BuildTeamRow(hero, i,
                    compactGrid ? (i % 2) * (w * 0.5f + 2f) : 0f,
                    -LaneHeader - (compactGrid ? i / 2 : i) * lane,
                    compactGrid ? w * 0.5f - 2f : w, lane);

            if (at[1].height > 0f)
            {
                RectTransform banner = Section(root, "Summary", at[1]);
                specRail = InstrumentPlate(banner, new Rect(0f, 0f, at[1].width, at[1].height), AvTheme.RailInfo);
                specSummary = SingleLine(AvKit.Label(banner, "", new Rect(12f, -8f, at[1].width - 24f, 22f),
                    AvTheme.Dim, AvTokens.FontLead, FontStyles.Bold));
                specAdvice = Wrapped(AvStyled.Label(banner, new Rect(12f, -34f, at[1].width - 24f, at[1].height - 40f), "", "row-sub"));
            }

            if (at[2].height > 0f)
            {
                specTheatre = new Views.MiniTheatre();
                specTheatre.Build(Section(root, "Theatre", at[2]), new Rect(0f, 0f, at[2].width, at[2].height));
            }

            if (at[3].height > 0f)
            {
                RectTransform stamps = Section(root, "Stamps", at[3]);
                AvStyled.Label(stamps, new Rect(0f, 0f, at[3].width, 16f), "DETACHMENT · TEAMS · POSTS · GROUND READINESS",
                    "section-title");
                float tileWidth = (at[3].width - 12f) / 4f;
                for (int i = 0; i < specTiles.Length; i++)
                    specTiles[i] = BuildTile(stamps, new Rect(i * (tileWidth + 4f), -20f, tileWidth, TileHeight), SpecTileKeys[i]);
            }

            if (at[4].height > 0f)
                specLoopLabels = BuildLoopLines(Section(root, "Log", at[4]), at[4].width, at[4].height,
                    "EVENT LOG · DETACHMENT NET · NEWEST FIRST");
        }

        /// <summary>
        /// One lane: callsign and rank pips, the team's line, its rank, and a clock bar underneath. A
        /// tall lane gives the full line its own row; a 420 lane keeps one row with the headline.
        /// </summary>
        private static TeamRow BuildTeamRow(RectTransform parent, int team, float x, float y, float width, float height)
        {
            bool grid = width < 300f;
            var row = new TeamRow { Compact = height < 40f || grid };
            AvKit.Panel(parent, new Rect(x, y, width, height - 3f), AvTheme.SurfaceInert);
            AvKit.Rule(parent, new Rect(x + 3f, y, width - 3f, 1f), AvTheme.RailInfo.WithAlpha(0.55f));
            row.Rail = AvKit.Rule(parent, new Rect(x, y, 3f, height - 3f), AvTheme.RailInert);
            float top = row.Compact ? -(height - 3f - 18f) * 0.5f : -3f;
            Image insignia = AvKit.Panel(parent, new Rect(x + 7f, y + top - 1f, 18f, 18f), AvTheme.RailInfo,
                OpsSprites.Glyph(OpsSprites.G.Team));
            insignia.raycastTarget = false;
            row.Name = AvStyled.Label(parent, new Rect(x + 30f, y + top, grid ? 64f : 62f, 18f), FieldWords.Callsign(team), "row-name");
            row.Name.enableAutoSizing = true;
            row.Name.fontSizeMin = AvTokens.FontMicro;
            row.Name.fontSizeMax = row.Name.fontSize;
            row.Pips = new Image[grid ? 0 : FieldCatalog.MaxRank];
            for (int i = 0; i < row.Pips.Length; i++)
                row.Pips[i] = AvKit.Rule(parent, new Rect(x + 10f + i * 12f, y - height + 10f, 10f, 2f), AvTheme.RailInert);
            if (!grid)
            {
                row.Rank = SingleLine(AvStyled.Label(parent, new Rect(x + width - 158f, y + top, 150f, 18f), "", "row-value",
                    align: TextAlignmentOptions.MidlineRight));
                row.Rank.enableAutoSizing = true;
                row.Rank.fontSizeMin = AvTokens.FontMicro;
                row.Rank.fontSizeMax = row.Rank.fontSize;
            }
            row.Line = grid
                ? SingleLine(AvStyled.Label(parent, new Rect(x + 100f, y + top, width - 106f, 18f), "", "row-sub"))
                : row.Compact
                ? SingleLine(AvStyled.Label(parent, new Rect(x + 84f, y + top, width - 84f - 162f, 18f), "", "row-sub"))
                : SingleLine(AvStyled.Label(parent, new Rect(x + 84f, y + top - 17f, width - 92f, 16f), "", "row-sub"));
            row.Line.enableAutoSizing = true;
            row.Line.fontSizeMin = AvTokens.FontMicro;
            row.Line.fontSizeMax = row.Line.fontSize;
            if (!grid)
                row.Bar = AvKit.ProgressBar(parent, new Rect(x + 84f, y - height + 8f, width - 92f, 4f), 0f, AvTheme.RailInfo);
            return row;
        }

        private void BuildSpecActions(RectTransform root, Rect body)
        {
            int count = CountActions(TabSpecOps);
            Rect content = PageFrame(root, body, OpsDomain.SpecialOperations, "FIELD ABILITIES", SubActions, SelectSpecSub, SpecSubTips, out specActionsNote);
            specBanner = BuildArmedBanner(root, new Rect(content.x, content.y, content.width, BannerRowHeight),
                "OPEN DESK", OpenDesk, "Open the briefing desk to assign teams and create field posts.");
            var list = new Rect(content.x, content.y - BannerRowHeight - 8f, content.width, content.height - BannerRowHeight - 8f);
            int shown = Mathf.Max(1, count);
            float room = list.height - shown * RowHeight - SectionGap;
            if (room < PostsHeight)
            {
                RectTransform parent = BeginSub(root, list, shown * RowHeight, out float x, out float y, out float width);
                BuildActionRows(parent, TabSpecOps, x, y, width, "ARM");
                return;
            }
            // A tall panel adds who holds which post and what each grants (M2); the rows take the rest.
            float step = (list.height - PostsHeight - SectionGap) / shown;
            BuildActionRows(root, TabSpecOps, list.x, list.y, list.width, "ARM", step);
            BuildPostCards(Section(root, "Posts", new Rect(list.x, list.y - shown * step - SectionGap, list.width, PostsHeight)),
                list.width);
        }

        private const float PostCardHeight = 28f;
        private const float PostsHeight = 20f + PostCardHeight * SpecOpsDetachment.TeamCount + 6f + 18f * FieldCatalog.MissionCount;

        private void BuildPostCards(RectTransform parent, float w)
        {
            AvStyled.Label(parent, new Rect(0f, 0f, w, 16f), "HELD POSTS · WHAT EACH GRANTS", "section-title");
            for (int i = 0; i < postRails.Length; i++)
            {
                float y = -20f - i * PostCardHeight;
                postCards[i] = AvKit.Panel(parent, new Rect(0f, y, w, PostCardHeight - 4f), AvTheme.Unity(AvTokens.Surface),
                    AvSprites.Control);
                postCards[i].gameObject.SetActive(false);
                postRails[i] = AvKit.Rule(parent, new Rect(0f, y, 3f, PostCardHeight - 4f), AvTheme.RailInert);
                postTexts[i] = SingleLine(AvStyled.Label(parent, new Rect(10f, y - 4f, w - 14f, 16f), "", "row-sub"));
                postTexts[i].enableAutoSizing = true;
                postTexts[i].fontSizeMin = AvTokens.FontMicro;
                postTexts[i].fontSizeMax = postTexts[i].fontSize;
            }
            float key = -20f - postRails.Length * PostCardHeight - 6f;
            for (int m = 0; m < FieldCatalog.MissionCount; m++)
            {
                var mission = (FieldMission)m;
                var line = SingleLine(AvStyled.Label(parent, new Rect(0f, key - m * 18f, w, 16f),
                    FieldWords.Mission(mission) + " · " + FieldWords.Post(mission) + " · " +
                    FieldWords.PostGrant(mission).ToUpperInvariant(), "row-sub"));
                line.color = AvTheme.Dim;
                line.enableAutoSizing = true;
                line.fontSizeMin = AvTokens.FontMicro;
                line.fontSizeMax = line.fontSize;
            }
        }

        /// <summary>One line per team: a holding team's post, place, time left, reach and the ability it grants.</summary>
        private void PaintPostCards(SpecOpsDetachment detachment, double now)
        {
            if (postTexts[0] == null) return;
            for (int t = 0; t < postTexts.Length; t++)
            {
                FieldTeam team = detachment != null ? detachment.Team(t) : default;
                bool holding = detachment != null && team.State == TeamState.Holding;
                string text;
                if (holding)
                {
                    text = FieldWords.Callsign(t) + " · " + FieldWords.PostCode(team.Mission) + " " +
                           (string.IsNullOrEmpty(team.Target) ? "OBJECTIVE" : team.Target) + " · " +
                           FieldWords.Clock(detachment.Remaining(t, now)) + " LEFT · " +
                           FieldWords.PostGrant(team.Mission).ToUpperInvariant();
                }
                else
                    text = FieldWords.Callsign(t) + " · NO POST" +
                           (detachment != null && team.Formed ? " · " + FieldWords.State(team.State) : " · NOT RAISED");
                if (postTexts[t].text != text) postTexts[t].text = text;
                postTexts[t].color = holding ? AvTheme.TextPrimary : AvTheme.Dim;
                postRails[t].color = holding ? FieldTones.Post(team.Mission) : AvTheme.RailInert;
                if (!postCards[t].gameObject.activeSelf) postCards[t].gameObject.SetActive(true);
            }
        }

        // ---- Desk and log ------------------------------------------------------------------------

        private void OpenDesk()
        {
            if (FullscreenInput.AnyOpen && !Window.OpsWindow.IsOpen) return;
            OpenRoom(DeskRoom(), null, openDeskButton);
        }

        /// <summary>Work that must not wait for the page: turn new host notices into log lines.</summary>
        private void TickSpecOpsBackground()
        {
            if (specSubPages[SubStatus] == null) return;
            SpecOpsDetachment detachment = support.LocalDetachment;
            if (detachment == null) return;
            if (specLoggedSerial < 0)
            {
                specLoggedSerial = detachment.NoticeSerial;
                return;
            }
            int fresh = Mathf.Min(detachment.NoticeSerial - specLoggedSerial, detachment.NoticeCount);
            if (detachment.NoticeSerial < specLoggedSerial) fresh = 0;
            specLoggedSerial = detachment.NoticeSerial;
            for (int age = fresh - 1; age >= 0; age--)
            {
                FieldNotice notice = detachment.NoticeKind(age);
                int team = detachment.NoticeTeam(age);
                string line = FieldWords.Notice(notice, team, detachment.NoticeMission(age), detachment.Team(team).Target);
                if (line != null) SpecLog(FieldWords.Alarm(notice) ? "!! " + line : line);
            }
        }

        private void SpecLog(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            for (int i = specLoop.Length - 1; i > 0; i--) specLoop[i] = specLoop[i - 1];
            specLoop[0] = TheaterGrid.Elapsed(support != null ? support.OrbitNow : 0.0) + "  " + line;
        }

        // ---- Refresh -----------------------------------------------------------------------------

        private void RefreshSpecOps(bool bypass)
        {
            if (specSubPages[SubStatus] == null) return;
            SpecOpsDetachment detachment = support.LocalDetachment;
            double now = support.OrbitNow;
            if (specSub == SubStatus) RefreshSpecStatus(detachment, now);
            else RefreshSpecActions(detachment);
        }

        private void RefreshSpecStatus(SpecOpsDetachment detachment, double now)
        {
            bool live = detachment != null && detachment.Enabled && support.SpecOpsEnabled;
            if (specSummary != null)
            {
                specSummary.text = !support.SpecOpsEnabled || (detachment != null && !detachment.Enabled)
                    ? "OFFLINE · DISABLED IN HOST CONFIG" : FieldWords.Summary(detachment);
                specSummary.color = live ? AvTheme.TextPrimary : AvTheme.Dim;
                specAdvice.text = FieldWords.Advice(detachment, now);
                specRail.color = !live ? AvTheme.RailInert
                    : detachment.Posts() > 0 ? AvTheme.RailReady
                    : detachment.Count(TeamState.Ready) > 0 ? AvTheme.RailInfo : AvTheme.RailCaution;
            }

            specTeamsNote.text = detachment == null ? "TEAMS · AWAITING THEATER DATA"
                : "TEAMS · " + detachment.Formed + "/" + SpecOpsDetachment.TeamCount + " FORMED · BEST " +
                  FieldWords.Rank(detachment.BestRank);
            for (int i = 0; i < teamRows.Length; i++) PaintTeamRow(teamRows[i], detachment, i, now);

            if (!live)
            {
                for (int i = 0; i < specTiles.Length; i++) PaintTile(specTiles[i], "—", Tone.Locked);
            }
            else
            {
                int ready = detachment.Count(TeamState.Ready);
                int field = detachment.Count(TeamState.EnRoute) + detachment.Count(TeamState.OnTask) + detachment.Count(TeamState.Holding);
                int posts = detachment.Posts();
                PaintTile(specTiles[0], ready + (ready == 1 ? " TEAM" : " TEAMS"), ready > 0 ? Tone.Ready : Tone.Pending);
                PaintTile(specTiles[1], field > 0 ? field + " DEPLOYED" : "NONE", field > 0 ? Tone.Pending : Tone.Locked);
                PaintTile(specTiles[2], posts > 0 ? PostTiles(detachment) : "NONE", posts > 0 ? Tone.Ready : Tone.Locked);
                PaintTile(specTiles[3], detachment.GroundReadiness + " PER ORDER",
                    detachment.BestRank > 0 ? Tone.Ready : Tone.Pending);
            }

            if (specTheatre != null)
            {
                LocalHomes(out int homes);
                specTheatre.SetHomes(homeXs, homeZs, homes);
                specTheatre.Paint(detachment, now, !live ? "SPEC OPS OFFLINE" : "NO OBJECTIVE LISTED YET");
            }
            if (specLoopLabels != null) WriteLoop(specLoopLabels, specLoop);
        }

        private static string PostTiles(SpecOpsDetachment detachment)
        {
            string text = "";
            for (int m = 0; m < FieldCatalog.MissionCount; m++)
            {
                int count = detachment.Posts((FieldMission)m);
                if (count == 0) continue;
                text += (text.Length > 0 ? " · " : "") + count + " " + FieldWords.PostCode((FieldMission)m);
            }
            return text;
        }

        private static void PaintTeamRow(TeamRow row, SpecOpsDetachment detachment, int index, double now)
        {
            if (row == null) return;
            FieldTeam team = detachment != null ? detachment.Team(index) : default;
            bool formed = detachment != null && team.Formed;
            string line = detachment == null ? "AWAITING THEATER DATA"
                : row.Compact ? FieldWords.TeamHeadline(team, detachment.Remaining(index, now))
                : FieldWords.TeamLine(team, detachment.Remaining(index, now));
            if (row.LastLine != line) row.Line.text = row.LastLine = line;
            Color colour = formed ? FieldTones.State(team.State)
                : team.Last == MissionOutcome.Lost ? AvTheme.RailDanger : AvTheme.RailInert;
            row.Rail.color = colour;
            row.Line.color = formed ? (team.State == TeamState.Recovering ? AvTheme.Dim : colour)
                : team.Last == MissionOutcome.Lost ? AvTheme.RailDanger : AvTheme.Dim;
            row.Name.color = formed ? AvTheme.TextPrimary : AvTheme.Dim;
            if (row.Rank != null)
                row.Rank.text = !formed ? "—" : FieldWords.Rank(team.Rank) +
                    (team.Rank < FieldCatalog.MaxRank ? " · " + FieldCatalog.WinsToNext(team.Wins) + " TO NEXT" : "");
            for (int pip = 0; pip < row.Pips.Length; pip++)
                row.Pips[pip].color = formed && pip < team.Rank ? AvTheme.RailReady : AvTheme.RailInert;
            float progress = detachment != null ? detachment.Progress(index, now) : 0f;
            if (row.Bar != null)
            {
                row.Bar.fillAmount = team.State == TeamState.Holding ? 1f - progress : progress;
                row.Bar.color = colour;
            }
        }

        private void RefreshSpecActions(SpecOpsDetachment detachment)
        {
            if (specBanner == null) return;
            PaintPostCards(detachment, support.OrbitNow);
            int posts = detachment != null ? detachment.Posts() : 0;
            string title = "FIELD ABILITIES · " + posts + (posts == 1 ? " POST HELD" : " POSTS HELD");
            if (specActionsNote.text != title) specActionsNote.text = title;
            if (detachment == null || !detachment.Enabled || !support.SpecOpsEnabled)
                PaintArmedBanner(specBanner, TabSpecOps, "SPEC OPS IS OFF ON THIS SERVER", AvTheme.Dim, false);
            else if (posts == 0)
                PaintArmedBanner(specBanner, TabSpecOps, "NO POST HELD · A DESK MISSION THAT SUCCEEDS LEAVES ONE", AvTheme.Dim);
            else
                PaintArmedBanner(specBanner, TabSpecOps, "ARM, RIGHT-CLICK INSIDE A POST'S REACH · " + PostTiles(detachment) + " HELD",
                    AvTheme.RailReady);
        }

        /// <summary>The local faction's held airbases, for the board's orientation squares.</summary>
        private void LocalHomes(out int count)
        {
            count = 0;
            if (!GameManager.GetLocalPlayer<Player>(out Player player) || player == null || player.HQ == null) return;
            foreach (Airbase airbase in player.HQ.GetAirbases())
            {
                if (count >= homeXs.Length) break;
                if (airbase == null || airbase.AttachedAirbase || airbase.CurrentHQ != player.HQ) continue;
                GlobalPosition at = (airbase.center != null ? airbase.center.position : airbase.transform.position).ToGlobalPosition();
                homeXs[count] = at.x;
                homeZs[count] = at.z;
                count++;
            }
        }
    }
}
