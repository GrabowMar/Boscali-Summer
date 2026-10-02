using NOAvionics;
using System;
using BoscaliSummer.Modules.Support.Domain;
using BoscaliSummer.Modules.Support.Domain.Layout;
using BoscaliSummer.Modules.Support.Domain.SpecOps;
using BoscaliSummer.Modules.Support.Presentation.Viz;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Modules.Support.Presentation
{
    /// <summary>
    /// SPEC OPS — the detachment, shaped like SPACE and CYBER. STATUS leads with the four squad cards
    /// (state, where, rank, phase clock, EXECUTE / EXTRACT at a decision), then the readiness
    /// summary with OPEN DESK on the same card, the theatre plot, four chips, the event log and
    /// the named objective ledger. ACTIONS is the flying half: SPOT and SUPPRESS (earned by held
    /// posts) and FORTIFY, as tiles, plus what each held post grants, and the allocation history.
    /// Every decision that grows those abilities - raising teams, choosing objectives, launching
    /// and recalling - lives at the briefing table (<see cref="Views.DeskView"/>). The MFD never
    /// spends allocation except by arming an ability. The log keeps running with the page closed.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private const string SpecStatusHelp = "STATUS: the four teams and their clocks, the theatre plot, readiness, named objectives with threat and radar counts, and the event log. EXECUTE or EXTRACT appears on a team at its decision.";
        private const string SpecActionsHelp = "ACTIONS: held posts control their 10 km sector for matching abilities while charged and unexpired. Arm one, then right-click that sector. FORTIFY also works on owned ground.";

        private static readonly string[] SpecTileKeys = { "READY", "IN FIELD", "POSTS", "GROUND READINESS" };

        /// <summary>Hosts the <see cref="Views.MiniTheatre"/> board inside a kit v2 part.</summary>
        private sealed class TheatrePart : AvPart
        {
            private const float H = 150f;
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
        private AvControl openDeskButton;
        private AvList specObjectives;
        private SpecOpsDetachment listedDetachment;
        private AvLineChart specAllocChart;
        private SquadDeck specDeck;
        private BriefCard specBrief;
        private TheatrePart specTheatre;
        private AvChip[] specTiles;
        private LogTape specLog;
        private BriefCard specBanner;
        private readonly AvRow[] specPostRows = new AvRow[SpecOpsDetachment.TeamCount];

        private readonly string[] specLoop = new string[LoopLines];
        private readonly float[] homeXs = new float[8];
        private readonly float[] homeZs = new float[8];
        private int specLoggedSerial = -1;

        private void ResetSpecOpsPage()
        {
            specPage = null;
            specSection = specTheatreSection = specTilesSection = null;
            openDeskButton = null;
            specObjectives = null;
            listedDetachment = null;
            specAllocChart = null;
            specDeck = null;
            specBrief = null;
            specTheatre = null;
            specTiles = null;
            specLog = null;
            specBanner = null;
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
            specDeck = status.Add(new SquadDeck(status.Content, RequestSpecOpsDirectiveByIndex));
            specBrief = status.Add(new BriefCard(status.Content));
            openDeskButton = specBrief.AddControl(new AvControl.Spec("OPEN DESK", OpenDesk, AvButtonStyle.Primary, AvIcon.ListDetails));
            openDeskButton.Help = "The briefing table: raise teams, pick objectives, launch and recall missions. " + FieldWords.Legend();

            specTheatre = status.Add(new TheatrePart(status.Content));
            specTiles = BuildChipRow(status, SpecTileKeys, 2);
            specObjectives = status.Add(new AvList(status.Content, status.Ticker, 2, BindObjective));
            specLog = status.Add(new LogTape(status.Content, 3), 1f);
        }

        private void BuildSpecActionsPage(AvFlow actions)
        {
            specBanner = BuildArmedBanner(actions);
            AddReadyStrip(actions, TabSpecOps, CountActions(TabSpecOps));
            BuildActionRows(actions, TabSpecOps, "ARM");

            for (int i = 0; i < specPostRows.Length; i++)
                specPostRows[i] = actions.Add(new AvRow(actions.Content));
            specPostsEmpty = actions.Add(new BriefCard(actions.Content));
            specPostsEmpty.Set("NO POST HELD", "Posts are earned on successful field missions.", AvState.Inert);
            specAllocChart = AddTrend(actions);
        }

        private void RequestSpecOpsDirectiveByIndex(int team, bool execute) =>
            RequestSpecOpsDirective(team, execute ? SpecOpsDirective.Execute : SpecOpsDirective.Extract);

        private void RequestSpecOpsDirective(int team, SpecOpsDirective directive)
        {
            SpecOpsDetachment detachment = support.LocalDetachment;
            if (detachment == null || support.CommandPending || !support.OpsStateFresh ||
                (directive == SpecOpsDirective.Execute && !support.SpecOpsEnabled)) return;
            if (detachment.CheckDirective(team, directive, support.OrbitNow) != SpecOpsDenial.None) return;
            support.RequestSpecOpsDirective(team, directive);
            nextRefresh = 0f;
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
            specTheatreSection.SetShown(live);
            specBrief.ShowControl(live);
            specTheatre.SetShown(live);
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
                    off ? "HOST OFF" : "WAITING FOR THEATRE",
                    AvState.Inert);
                specPage.SetHeadline(off ? "OFFLINE" : "AWAITING DATA", AvState.Inert);
                listedDetachment = null;
                specObjectives.SetCount(0);
                return;
            }

            AvState tone = detachment.Posts() > 0 ? AvState.Ready
                : detachment.Count(TeamState.Ready) > 0 ? AvState.Info : AvState.Caution;
            specBrief.Set(FieldWords.Summary(detachment), FieldWords.Advice(detachment, now), tone);
            specPage.SetHeadline(detachment.Formed + "/" + SpecOpsDetachment.TeamCount + " FORMED · BEST " + FieldWords.Rank(detachment.BestRank), tone);

            for (int i = 0; i < SquadDeck.Slots; i++) PaintSquad(i, detachment, now);

            int ready = detachment.Count(TeamState.Ready);
            int field = detachment.Count(TeamState.EnRoute) + detachment.Count(TeamState.Deciding) +
                detachment.Count(TeamState.OnTask) + detachment.Count(TeamState.Holding);
            int posts = detachment.Posts();
            SetChip(specTiles[0], SpecTileKeys[0], ready + (ready == 1 ? " TEAM" : " TEAMS"), ready > 0 ? AvState.Ready : AvState.Info);
            SetChip(specTiles[1], SpecTileKeys[1], field > 0 ? field + " DEPLOYED" : "NONE", field > 0 ? AvState.Info : AvState.Inert);
            SetChip(specTiles[2], SpecTileKeys[2], posts > 0 ? PostTiles(detachment) : "NONE", posts > 0 ? AvState.Ready : AvState.Inert);
            SetChip(specTiles[3], SpecTileKeys[3], detachment.GroundReadiness + " PER ORDER", detachment.BestRank > 0 ? AvState.Ready : AvState.Info);

            PaintObjectives(detachment);
            LocalHomes(out int homes);
            specTheatre.SetHomes(homeXs, homeZs, homes);
            specTheatre.Paint(detachment, now, "NO OBJECTIVE LISTED YET");
        }

        /// <summary>Named host objectives, paged to the twelve-record bound.</summary>
        private void PaintObjectives(SpecOpsDetachment detachment)
        {
            listedDetachment = detachment;
            specObjectives.SetCount(detachment.ObjectiveCount);
        }

        private void BindObjective(int index, AvRow row)
        {
            if (listedDetachment == null || index >= listedDetachment.ObjectiveCount) return;
            FieldObjective objective = listedDetachment.Objective(index);
            int team = listedDetachment.TeamOn(objective.Anchor);
            string teamState = team >= 0 ? FieldWords.State(listedDetachment.Team(team).State) : "";
            string custody = objective.Friendly ? "FRIENDLY" : objective.Hostile ? "HOSTILE" : "NEUTRAL";
            string name = string.IsNullOrEmpty(objective.Name) ? "OBJECTIVE " + (index + 1) : objective.Name;
            bool known = listedDetachment.IntelKnown(objective.Anchor, support.OrbitNow) && objective.Threat != byte.MaxValue;
            string intelligence = known ? "THREAT " + objective.Threat + " · RADARS " + objective.Radars : "DEFENDERS UNCONFIRMED";
            row.Set(name, custody + (team >= 0 ? " · " + teamState : "") + " · " + intelligence,
                team >= 0 ? FieldWords.Callsign(team) : "UNASSIGNED",
                objective.Hostile ? AvState.Caution : objective.Friendly ? AvState.Ready : AvState.Info);
            row.Armed = team >= 0;
            row.Help = name + ": " + custody.ToLowerInvariant() + ". " + intelligence + ". " +
                (team >= 0 ? FieldWords.Callsign(team) + " · " + teamState + "." : "Assign a team from OPEN DESK; OBSERVE produces a field report.");
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
                    card.Line = lost ? "LOST · AWAITS TASKING" : "EMPTY SLOT · AWAITS TASKING";
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
                    card.Line = "PREP " + team.Preparation + " / INTEL " + team.Intel + " / EXP " + team.Exposure + " · OPEN DESK";
                    card.Deciding = true;
                    card.CanExecute = !support.CommandPending && support.OpsStateFresh && support.SpecOpsEnabled &&
                        team.RouteStep == 3 && detachment.CheckDirective(index, SpecOpsDirective.Execute, now) == SpecOpsDenial.None;
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
                    card.Line = FieldWords.PostCode(team.Mission) + " Q" + team.Quality + " · " + team.Charges + " CHARGES · " + target;
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
                text = "HOST OFF";
                tone = AvState.Inert;
            }
            else if (posts == 0)
            {
                headline = "NO POST HELD";
                text = "Posts are earned on successful field missions. FORTIFY still works on owned ground.";
                tone = AvState.Inert;
            }
            else
            {
                headline = posts + (posts == 1 ? " POST HELD" : " POSTS HELD") + " · " + PostTiles(detachment);
                text = "ARM · RIGHT-CLICK CONTROLLED SECTOR";
                tone = AvState.Ready;
            }
            PaintBanner(specBanner, TabSpecOps, headline, text, tone);
            specPage.SetHeadline(live ? posts + (posts == 1 ? " POST HELD" : " POSTS HELD") : "OFFLINE", live && posts > 0 ? AvState.Ready : AvState.Inert);
            PaintPostRows(detachment, support.OrbitNow, live);
            PaintTrend(specAllocChart, allocationTrend, "");
        }

        /// <summary>One row per formed team: the post it holds and what that grants, or where it is and when it lands.
        /// The ACTIONS page reads as a cause: which team's post unlocks which ability.</summary>
        private void PaintPostRows(SpecOpsDetachment detachment, double now, bool live)
        {
            for (int t = 0; t < specPostRows.Length; t++)
            {
                if (specPostRows[t] == null) continue;
                FieldTeam team = live ? detachment.Team(t) : default;
                bool show = live && team.Formed;
                specPostRows[t].SetShown(show);
                if (!show) continue;
                string callsign = FieldWords.Callsign(t);
                string target = PlaceNames.Shorten(string.IsNullOrEmpty(team.Target) ? "OBJECTIVE" : team.Target, 22);
                string clock = FieldWords.Clock(detachment.Remaining(t, now));
                switch (team.State)
                {
                    case TeamState.Holding:
                        specPostRows[t].Help = "HELD POST: " + FieldWords.PostGrant(team.Mission) +
                            " Controls sector " + OpsSectors.Code(team.X, team.Z) +
                            ". Each accepted ability spends one shared charge. Hostile pressure can force withdrawal before expiry.";
                        specPostRows[t].Set(callsign + " · " + FieldWords.PostCode(team.Mission) + " " + target,
                            "Q" + team.Quality + " · " + team.Charges + " CHARGES · SECTOR " + OpsSectors.Code(team.X, team.Z), clock, AvState.Ready);
                        break;
                    case TeamState.Ready:
                        specPostRows[t].Help = callsign + " is ready and holds no post. Send it to an objective from the desk; operator field work prepares a post with 1–3 charges for up to 180 s.";
                        specPostRows[t].Set(callsign + " · " + FieldWords.State(team.State), "NO POST · SEND IT FROM THE DESK", "", AvState.Info);
                        break;
                    default:
                        specPostRows[t].Help = callsign + " is " + FieldWords.State(team.State).ToLowerInvariant() +
                            ". A successful mission converts to a post that unlocks its ability.";
                        specPostRows[t].Set(callsign + " · " + FieldWords.State(team.State),
                            team.Deployed ? FieldWords.Mission(team.Mission) + " · " + target : "RECOVERING", clock,
                            team.Deployed ? AvState.Caution : AvState.Inert);
                        break;
                }
            }
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
