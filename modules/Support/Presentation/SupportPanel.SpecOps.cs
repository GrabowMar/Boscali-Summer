using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using BoscaliSummer.Features.Support.Presentation.Viz;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPEC OPS — the detachment, shaped like SPACE and CYBER. STATUS leads with the four squad cards
    /// (state, where, rank, phase clock, EXECUTE / EXTRACT at a decision), then OPEN DESK, the
    /// readiness summary and advice, the theatre plot, four chips and the event log. ACTIONS is the
    /// flying half: SPOT and SUPPRESS (earned by held posts) and FORTIFY, as tiles, plus what each held
    /// post grants. Every decision that grows those abilities — raising teams, choosing objectives,
    /// launching and recalling — lives at the briefing table (<see cref="Views.DeskView"/>). The MFD
    /// never spends allocation except by arming an ability. The log keeps running with the page closed.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const string SpecStatusHelp = "Watch floor: the teams, their clocks, the posts they hold and the event log.";
        private const string SpecActionsHelp = "The map abilities your held posts grant, plus zone fortification.";

        private static readonly string[] SpecTileKeys = { "READY", "IN FIELD", "POSTS", "GROUND READINESS" };

        /// <summary>Hosts the <see cref="Views.MiniTheatre"/> board inside a kit v2 part.</summary>
        private sealed class TheatrePart : AvPart
        {
            private const float H = 176f;
            private readonly Views.MiniTheatre theatre = new Views.MiniTheatre();
            private bool built;

            public TheatrePart(RectTransform parent) => Rect = AvLay.Child(parent, "Theatre");

            public override float Measure(float width) => H;

            public override void Place(AvSlot s)
            {
                base.Place(s);
                if (built) return;
                built = true;
                theatre.Build(Rect, new UnityEngine.Rect(0f, 0f, s.W, s.H));
            }

            public void SetHomes(float[] xs, float[] zs, int count) { if (built) theatre.SetHomes(xs, zs, count); }
            public void Paint(SpecOpsDetachment detachment, double now, string emptyText) { if (built) theatre.Paint(detachment, now, emptyText); }
        }

        private OpsSubPage specPage;
        private AvSection specSection, specTheatreSection, specTilesSection;
        private AvButtons specButtons;
        private AvControl openDeskButton;
        private SquadDeck specDeck;
        private BriefCard specBrief;
        private TheatrePart specTheatre;
        private AvChip[] specTiles;
        private LogTape specLog;
        private BriefCard specBanner;
        private AvSection specAbilitiesSection, specPostsSection;
        private readonly AvRow[] specPostRows = new AvRow[SpecOpsDetachment.TeamCount];
        private BriefCard specPostsEmpty;

        private readonly string[] specLoop = new string[LoopLines];
        private readonly float[] homeXs = new float[8];
        private readonly float[] homeZs = new float[8];
        private int specLoggedSerial = -1;

        private void ResetSpecOpsPage()
        {
            specPage = null;
            specSection = specTheatreSection = specTilesSection = null;
            specButtons = null;
            openDeskButton = null;
            specDeck = null;
            specBrief = null;
            specTheatre = null;
            specTiles = null;
            specLog = null;
            specBanner = null;
            specAbilitiesSection = specPostsSection = null;
            specPostsEmpty = null;
            for (int i = 0; i < specPostRows.Length; i++) specPostRows[i] = null;
            for (int i = 0; i < specLoop.Length; i++) specLoop[i] = null;
            specLoggedSerial = -1;
        }

        // ---- Build -------------------------------------------------------------------------------

        private void BuildSpecOpsPage(AvFlow page)
        {
            specPage = page.Add(new OpsSubPage(page.Content, page.Ticker, page.Inner, AvIcon.UsersGroup, "SPEC OPS",
                sub => nextRefresh = 0f, SpecStatusHelp, SpecActionsHelp));
            BuildSpecStatusPage(specPage.Status);
            BuildSpecActionsPage(specPage.Actions);
            SpecLog("DETACHMENT ON THE NET · ALPHA AND BRAVO STANDING BY");
        }

        private void BuildSpecStatusPage(AvFlow status)
        {
            specSection = status.Section(AvIcon.UsersGroup, FieldWords.Title, "");
            specDeck = status.Add(new SquadDeck(status.Content, RequestSpecOpsDirectiveByIndex));
            specButtons = status.Buttons(new AvControl.Spec("OPEN DESK", OpenDesk, AvButtonStyle.Primary, AvIcon.ListDetails));
            openDeskButton = specButtons.Controls[0];
            openDeskButton.Help = "The briefing table: raise teams, pick objectives, launch and recall missions. " + FieldWords.Legend();
            specBrief = status.Add(new BriefCard(status.Content));

            specTheatreSection = status.Section(AvIcon.Map2, "THEATRE", "");
            specTheatre = status.Add(new TheatrePart(status.Content));

            specTilesSection = status.Section(AvIcon.Flag, "READINESS", "");
            specTiles = BuildChipRow(status, SpecTileKeys, 2);

            status.Section(AvIcon.ListDetails, "EVENT LOG · DETACHMENT NET");
            specLog = status.Add(new LogTape(status.Content, LoopLines));
        }

        private void BuildSpecActionsPage(AvFlow actions)
        {
            specBanner = BuildArmedBanner(actions);
            specAbilitiesSection = actions.Section(AvIcon.Bolt, "FIELD ABILITIES", "");
            BuildActionRows(actions, TabSpecOps, "ARM");

            specPostsSection = actions.Section(AvIcon.Flag, "HELD POSTS");
            for (int i = 0; i < specPostRows.Length; i++)
                specPostRows[i] = actions.Add(new AvRow(actions.Content));
            specPostsEmpty = actions.Add(new BriefCard(actions.Content));
            specPostsEmpty.Set("NO POST HELD", "A desk mission that succeeds leaves one; it grants the abilities above.", AvState.Inert);
        }

        private void RequestSpecOpsDirectiveByIndex(int team, bool execute) =>
            RequestSpecOpsDirective(team, execute ? SpecOpsDirective.Execute : SpecOpsDirective.Extract);

        private void RequestSpecOpsDirective(int team, SpecOpsDirective directive)
        {
            SpecOpsDetachment detachment = support.LocalDetachment;
            if (detachment == null || support.CommandPending || !support.OpsStateFresh ||
                (directive == SpecOpsDirective.Execute && !support.SpecOpsEnabled)) return;
            if (detachment.CheckDirective(team, directive) != SpecOpsDenial.None) return;
            support.RequestSpecOpsDirective(team, directive);
            nextRefresh = 0f;
        }

        // ---- Desk and log ------------------------------------------------------------------------

        private void OpenDesk()
        {
            if (FullscreenInput.AnyOpen && !Window.OpsWindow.IsOpen) return;
            OpenRoom(DeskRoom(), null, openDeskButton != null ? openDeskButton.Rect : null);
        }

        /// <summary>Work that must not wait for the page: turn new host notices into log lines.</summary>
        private void TickSpecOpsBackground()
        {
            if (specPage == null) return;
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
            if (specPage == null) return;
            SpecOpsDetachment detachment = support.LocalDetachment;
            double now = support.OrbitNow;
            if (specPage.Sub == 0) RefreshSpecStatusPage(detachment, now);
            else RefreshSpecActionsPage(detachment);
        }

        private void SetSpecStatusParts(bool live)
        {
            specDeck.SetShown(live);
            specButtons.SetShown(live);
            specTheatreSection.SetShown(live);
            specTheatre.SetShown(live);
            specTilesSection.SetShown(live);
            foreach (AvChip chip in specTiles) chip.SetShown(live);
        }

        private void RefreshSpecStatusPage(SpecOpsDetachment detachment, double now)
        {
            if (specBrief == null) return;
            bool live = detachment != null && detachment.Enabled && support.SpecOpsEnabled;
            SetSpecStatusParts(live);
            specLog.Write(specLoop);
            if (!live)
            {
                bool off = detachment != null || !support.SpecOpsEnabled;
                specBrief.Set(off ? "SPEC OPS OFFLINE" : "AWAITING THEATER DATA",
                    off ? "The host has switched special operations off." : "The detachment appears once the host lists the theatre.",
                    AvState.Inert);
                specSection.SetCaption(off ? "OFFLINE" : "AWAITING DATA");
                return;
            }

            AvState tone = detachment.Posts() > 0 ? AvState.Ready
                : detachment.Count(TeamState.Ready) > 0 ? AvState.Info : AvState.Caution;
            specBrief.Set(FieldWords.Summary(detachment), FieldWords.Advice(detachment, now), tone);
            specSection.SetCaption(detachment.Formed + "/" + SpecOpsDetachment.TeamCount + " FORMED · BEST " + FieldWords.Rank(detachment.BestRank));

            for (int i = 0; i < SquadDeck.Slots; i++) PaintSquad(i, detachment, now);

            int ready = detachment.Count(TeamState.Ready);
            int field = detachment.Count(TeamState.EnRoute) + detachment.Count(TeamState.Deciding) +
                detachment.Count(TeamState.OnTask) + detachment.Count(TeamState.Holding);
            int posts = detachment.Posts();
            SetChip(specTiles[0], SpecTileKeys[0], ready + (ready == 1 ? " TEAM" : " TEAMS"), ready > 0 ? AvState.Ready : AvState.Info);
            SetChip(specTiles[1], SpecTileKeys[1], field > 0 ? field + " DEPLOYED" : "NONE", field > 0 ? AvState.Info : AvState.Inert);
            SetChip(specTiles[2], SpecTileKeys[2], posts > 0 ? PostTiles(detachment) : "NONE", posts > 0 ? AvState.Ready : AvState.Inert);
            SetChip(specTiles[3], SpecTileKeys[3], detachment.GroundReadiness + " PER ORDER", detachment.BestRank > 0 ? AvState.Ready : AvState.Info);

            specTheatreSection.SetCaption(detachment.ObjectiveCount + (detachment.ObjectiveCount == 1 ? " OBJECTIVE" : " OBJECTIVES"));
            LocalHomes(out int homes);
            specTheatre.SetHomes(homeXs, homeZs, homes);
            specTheatre.Paint(detachment, now, "NO OBJECTIVE LISTED YET");
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

        private void PaintSquad(int index, SpecOpsDetachment detachment, double now)
        {
            FieldTeam team = detachment.Team(index);
            bool formed = team.Formed;
            double remaining = detachment.Remaining(index, now);
            double total = team.PhaseEnd - team.PhaseStart;
            float elapsed = total > 0.0 ? Mathf.Clamp01((float)(1.0 - remaining / total)) : 1f;
            string target = PlaceNames.Shorten(string.IsNullOrEmpty(team.Target) ? "OBJECTIVE" : team.Target, 22);
            var card = new SquadCardData
            {
                Callsign = FieldWords.Callsign(index),
                Formed = formed,
                Rank = team.Rank,
                RankWord = FieldWords.Rank(team.Rank),
                NextText = team.Rank < FieldCatalog.MaxRank ? FieldCatalog.WinsToNext(team.Wins) + " TO NEXT" : "",
                Clock = "",
            };
            switch (team.State)
            {
                case TeamState.Unformed:
                    bool lost = team.Last == MissionOutcome.Lost;
                    card.StateWord = lost ? "LOST" : "EMPTY";
                    card.Tone = lost ? AvState.Danger : AvState.Inert;
                    card.Icon = lost ? AvIcon.Skull : AvIcon.Minus;
                    card.Line = lost ? "LOST · RAISE A NEW TEAM" : "EMPTY SLOT · RAISE IN THE DESK";
                    break;
                case TeamState.Ready:
                    card.StateWord = "READY";
                    card.Tone = AvState.Ready;
                    card.Icon = AvIcon.CircleCheck;
                    card.Line = detachment.ObjectiveCount > 0 ? "AWAITING ORDERS · " + detachment.ObjectiveCount + " LISTED" : "AWAITING ORDERS";
                    card.ShowBar = true;
                    card.Progress = 1f;
                    break;
                case TeamState.EnRoute:
                    card.StateWord = "EN ROUTE";
                    card.Tone = AvState.Info;
                    card.Icon = AvIcon.ArrowUpRight;
                    card.Line = FieldWords.Mission(team.Mission) + " · " + target;
                    card.ShowBar = true;
                    card.Progress = elapsed;
                    card.Clock = FieldWords.Clock(remaining);
                    break;
                case TeamState.Deciding:
                    card.StateWord = "AT SITE " + FieldWords.Clock(remaining);
                    card.Tone = AvState.Caution;
                    card.Icon = AvIcon.AlertTriangle;
                    card.Line = FieldWords.Mission(team.Mission) + " · " + target;
                    card.Deciding = true;
                    card.CanExecute = !support.CommandPending && support.OpsStateFresh && support.SpecOpsEnabled;
                    card.CanExtract = !support.CommandPending && support.OpsStateFresh;
                    break;
                case TeamState.OnTask:
                    card.StateWord = "ON TASK";
                    card.Tone = AvState.Caution;
                    card.Icon = AvIcon.Focus2;
                    card.Line = FieldWords.Mission(team.Mission) + " · " + target;
                    card.ShowBar = true;
                    card.Progress = elapsed;
                    card.Clock = FieldWords.Clock(remaining);
                    break;
                case TeamState.Holding:
                    card.StateWord = "HOLDING";
                    card.Tone = AvState.Ready;
                    card.Icon = AvIcon.Flag;
                    card.Line = FieldWords.PostCode(team.Mission) + " · " + target;
                    card.ShowBar = true;
                    card.Progress = 1f - elapsed;
                    card.Clock = FieldWords.Clock(remaining);
                    break;
                default:
                    card.StateWord = "RECOVERING";
                    card.Tone = AvState.Inert;
                    card.Icon = AvIcon.Refresh;
                    card.Line = FieldWords.TeamLine(team, remaining);
                    card.ShowBar = true;
                    card.Progress = elapsed;
                    card.Clock = FieldWords.Clock(remaining);
                    break;
            }
            specDeck.Set(index, card);
        }

        private void RefreshSpecActionsPage(SpecOpsDetachment detachment)
        {
            if (specBanner == null) return;
            int posts = detachment != null ? detachment.Posts() : 0;
            string headline, text;
            AvState tone;
            bool live = detachment != null && detachment.Enabled && support.SpecOpsEnabled;
            if (!live)
            {
                headline = "SPEC OPS IS OFF";
                text = "The host has switched special operations off on this server.";
                tone = AvState.Inert;
            }
            else if (posts == 0)
            {
                headline = "NO POST HELD";
                text = "A desk mission that succeeds leaves one. FORTIFY still works on owned ground.";
                tone = AvState.Inert;
            }
            else
            {
                headline = posts + (posts == 1 ? " POST HELD" : " POSTS HELD") + " · " + PostTiles(detachment);
                text = "Arm an ability, then right-click inside a post's reach.";
                tone = AvState.Ready;
            }
            PaintBanner(specBanner, TabSpecOps, headline, text, tone);
            PaintPostRows(detachment, support.OrbitNow, live);
        }

        /// <summary>One row per holding team: its post, the place, what it grants and the time left.</summary>
        private void PaintPostRows(SpecOpsDetachment detachment, double now, bool live)
        {
            int held = 0;
            for (int t = 0; t < specPostRows.Length; t++)
            {
                if (specPostRows[t] == null) continue;
                FieldTeam team = live ? detachment.Team(t) : default;
                bool holding = live && team.State == TeamState.Holding;
                specPostRows[t].SetShown(holding);
                if (!holding) continue;
                held++;
                specPostRows[t].Set(FieldWords.Callsign(t) + " · " + FieldWords.PostCode(team.Mission) + " " +
                        PlaceNames.Shorten(string.IsNullOrEmpty(team.Target) ? "OBJECTIVE" : team.Target, 22),
                    FieldWords.PostGrant(team.Mission).ToUpperInvariant(), FieldWords.Clock(detachment.Remaining(t, now)), AvState.Ready);
            }
            specPostsEmpty.SetShown(held == 0 && live);
            specPostsSection.SetShown(live);
            specAbilitiesSection.SetShown(true);
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
