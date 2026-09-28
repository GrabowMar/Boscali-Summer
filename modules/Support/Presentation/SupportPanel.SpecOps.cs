using BoscaliSummer.Features.Support.Domain;
using BoscaliSummer.Features.Support.Domain.Layout;
using BoscaliSummer.Features.Support.Domain.SpecOps;
using NOAvionics;
using NOAvionics.Ui;
using NuclearOption.Networking;
using UnityEngine;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// SPEC OPS — the detachment, shaped like SPACE and CYBER. STATUS is the glance: the four
    /// teams in words, four annunciators, the theatre board and the event log. ACTIONS is the
    /// flying half: SPOT and SUPPRESS (earned by held posts) and FORTIFY, plus what each held
    /// post grants. Every decision that grows those abilities — raising teams, choosing
    /// objectives, launching and recalling — lives at the briefing table
    /// (<see cref="Views.DeskView"/>). The MFD never spends allocation except by arming an
    /// ability. The log keeps running with the page closed.
    /// </summary>
    internal sealed partial class SupportPanel
    {
        private static readonly string[] SpecTileKeys = { "READY", "IN FIELD", "POSTS", "READINESS" };

        /// <summary>Hosts the other agent's <see cref="Views.MiniTheatre"/> board inside a kit v2
        /// part (spec §9.2: genuinely-data visuals stay as they are, hosted in a kit v2 wrapper).</summary>
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
        private AvSection specSection;
        private AvControl openDeskButton;
        private readonly AvRow[] specTeamRows = new AvRow[SpecOpsDetachment.TeamCount];
        private readonly AvControl[] specExecute = new AvControl[SpecOpsDetachment.TeamCount];
        private readonly AvControl[] specExtract = new AvControl[SpecOpsDetachment.TeamCount];
        private HintLine specSummary;
        private NoteText specAdvice;
        private TheatrePart specTheatre;
        private AvChip[] specTiles;
        private LogLines specLog;
        private HintLine specActionsHint;
        private readonly AvRow[] specPostRows = new AvRow[SpecOpsDetachment.TeamCount];

        private readonly string[] specLoop = new string[LoopLines];
        private readonly float[] homeXs = new float[8];
        private readonly float[] homeZs = new float[8];
        private int specLoggedSerial = -1;

        private void ResetSpecOpsPage()
        {
            specPage = null;
            specSection = null;
            openDeskButton = null;
            for (int i = 0; i < specTeamRows.Length; i++)
            {
                specTeamRows[i] = null;
                specExecute[i] = null;
                specExtract[i] = null;
                specPostRows[i] = null;
            }
            specSummary = null;
            specAdvice = null;
            specTheatre = null;
            specTiles = null;
            specLog = null;
            specActionsHint = null;
            for (int i = 0; i < specLoop.Length; i++) specLoop[i] = null;
            specLoggedSerial = -1;
        }

        // ---- Build -------------------------------------------------------------------------------

        private void BuildSpecOpsPage(AvFlow page)
        {
            specPage = page.Add(new OpsSubPage(page.Content, page.Ticker, page.Inner, AvIcon.UsersGroup, "SPEC OPS", sub =>
            {
                nextRefresh = 0f;
                shell.Page(TabSpecOps).RequestRelayout();
            }));
            BuildSpecStatusPage(specPage.Status);
            BuildSpecActionsPage(specPage.Actions);
            SpecLog("DETACHMENT ON THE NET · ALPHA AND BRAVO STANDING BY");
        }

        private void BuildSpecStatusPage(AvFlow status)
        {
            specSection = status.Section(AvIcon.UsersGroup, FieldWords.Title, "");
            AvButtons deskButtons = status.Buttons(new AvControl.Spec("OPEN DESK", OpenDesk, AvButtonStyle.Primary, AvIcon.ListDetails));
            openDeskButton = deskButtons.Controls[0];

            for (int i = 0; i < specTeamRows.Length; i++)
            {
                int team = i;
                specTeamRows[i] = status.Add(new AvRow(status.Content));
                specExecute[i] = specTeamRows[i].AddTrailing(new AvControl.Spec("EXECUTE",
                    () => RequestSpecOpsDirective(team, SpecOpsDirective.Execute), AvButtonStyle.Primary));
                specExtract[i] = specTeamRows[i].AddTrailing(new AvControl.Spec("EXTRACT",
                    () => RequestSpecOpsDirective(team, SpecOpsDirective.Extract), AvButtonStyle.Danger));
            }

            specSummary = status.Add(new HintLine(status.Content));
            specAdvice = status.Add(new NoteText(status.Content));

            status.Section(AvIcon.Map2, "THEATRE");
            specTheatre = status.Add(new TheatrePart(status.Content));

            status.Section(AvIcon.Flag, "DETACHMENT · TEAMS · POSTS · GROUND READINESS");
            specTiles = BuildChipRow(status, SpecTileKeys);

            status.Section(AvIcon.ListDetails, "EVENT LOG · DETACHMENT NET · NEWEST FIRST");
            specLog = status.Add(new LogLines(status.Content, LoopLines));
        }

        private void BuildSpecActionsPage(AvFlow actions)
        {
            actions.Section(AvIcon.Bolt, "FIELD ABILITIES", "");
            specActionsHint = actions.Add(new HintLine(actions.Content));
            BuildActionRows(actions, TabSpecOps, "ARM");

            actions.Section(AvIcon.Flag, "HELD POSTS · WHAT EACH GRANTS");
            for (int i = 0; i < specPostRows.Length; i++)
                specPostRows[i] = actions.Add(new AvRow(actions.Content));
        }

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

        private void RefreshSpecStatusPage(SpecOpsDetachment detachment, double now)
        {
            if (specSummary == null) return;
            bool live = detachment != null && detachment.Enabled && support.SpecOpsEnabled;
            string summary = !support.SpecOpsEnabled || (detachment != null && !detachment.Enabled)
                ? "OFFLINE · DISABLED IN HOST CONFIG" : FieldWords.Summary(detachment);
            AvState tone = !live ? AvState.Inert
                : detachment.Posts() > 0 ? AvState.Ready
                : detachment.Count(TeamState.Ready) > 0 ? AvState.Info : AvState.Caution;
            specSummary.Set(summary, tone);
            specAdvice.Set(FieldWords.Advice(detachment, now), tone);
            specSection.SetCaption(detachment == null ? "AWAITING THEATER DATA"
                : detachment.Formed + "/" + SpecOpsDetachment.TeamCount + " FORMED · BEST " + FieldWords.Rank(detachment.BestRank));

            for (int i = 0; i < specTeamRows.Length; i++) PaintTeamRow(i, detachment, now);

            if (!live)
            {
                for (int i = 0; i < specTiles.Length; i++) SetChip(specTiles[i], SpecTileKeys[i], "—", AvState.Inert);
            }
            else
            {
                int ready = detachment.Count(TeamState.Ready);
                int field = detachment.Count(TeamState.EnRoute) + detachment.Count(TeamState.Deciding) +
                    detachment.Count(TeamState.OnTask) + detachment.Count(TeamState.Holding);
                int posts = detachment.Posts();
                SetChip(specTiles[0], SpecTileKeys[0], ready + (ready == 1 ? " TEAM" : " TEAMS"), ready > 0 ? AvState.Ready : AvState.Info);
                SetChip(specTiles[1], SpecTileKeys[1], field > 0 ? field + " DEPLOYED" : "NONE", field > 0 ? AvState.Info : AvState.Inert);
                SetChip(specTiles[2], SpecTileKeys[2], posts > 0 ? PostTiles(detachment) : "NONE", posts > 0 ? AvState.Ready : AvState.Inert);
                SetChip(specTiles[3], SpecTileKeys[3], detachment.GroundReadiness + " PER ORDER", detachment.BestRank > 0 ? AvState.Ready : AvState.Info);
            }

            LocalHomes(out int homes);
            specTheatre.SetHomes(homeXs, homeZs, homes);
            specTheatre.Paint(detachment, now, !live ? "SPEC OPS OFFLINE" : "NO OBJECTIVE LISTED YET");
            specLog.Write(specLoop);
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

        private void PaintTeamRow(int index, SpecOpsDetachment detachment, double now)
        {
            AvRow row = specTeamRows[index];
            if (row == null) return;
            FieldTeam team = detachment != null ? detachment.Team(index) : default;
            bool formed = detachment != null && team.Formed;
            bool deciding = formed && team.State == TeamState.Deciding;
            string line = detachment == null ? "AWAITING THEATER DATA"
                : deciding ? "ARRIVED · " + FieldWords.Clock(detachment.Remaining(index, now))
                : TeamGlance(team, detachment.Remaining(index, now));
            string rank = !formed ? "—" : FieldWords.Rank(team.Rank) +
                (team.Rank < FieldCatalog.MaxRank ? " · " + FieldCatalog.WinsToNext(team.Wins) + " TO NEXT" : "");
            AvState tone = formed ? SpecTeamTone(team.State) : team.Last == MissionOutcome.Lost ? AvState.Danger : AvState.Inert;
            row.Set(FieldWords.Callsign(index), line, rank, tone);

            bool canOrder = deciding && detachment != null && !support.CommandPending && support.OpsStateFresh;
            specExecute[index].Rect.gameObject.SetActive(deciding);
            specExtract[index].Rect.gameObject.SetActive(deciding);
            specExecute[index].Interactable = canOrder && support.SpecOpsEnabled;
            specExtract[index].Interactable = canOrder;
        }

        /// <summary>The same hue every SPEC OPS surface uses for a team state
        /// (<see cref="FieldTones.State"/>), expressed as an <see cref="AvState"/> (R1).</summary>
        private static AvState SpecTeamTone(TeamState state)
        {
            switch (state)
            {
                case TeamState.Ready: return AvState.Ready;
                case TeamState.Holding: return AvState.Ready;
                case TeamState.EnRoute: return AvState.Info;
                case TeamState.Deciding: return AvState.Caution;
                case TeamState.OnTask: return AvState.Caution;
                default: return AvState.Inert;
            }
        }

        private static string TeamGlance(in FieldTeam team, double remaining)
        {
            if (team.State != TeamState.EnRoute && team.State != TeamState.OnTask)
                return FieldWords.TeamLine(team, remaining);
            return FieldWords.State(team.State) + " · " + FieldWords.Mission(team.Mission) + " · " +
                PlaceNames.Shorten(team.Target, 28) + " · " + FieldWords.Clock(remaining);
        }

        private void RefreshSpecActionsPage(SpecOpsDetachment detachment)
        {
            if (specActionsHint == null) return;
            int posts = detachment != null ? detachment.Posts() : 0;
            string hint;
            AvState tone;
            if (detachment == null || !detachment.Enabled || !support.SpecOpsEnabled)
            {
                hint = "SPEC OPS IS OFF ON THIS SERVER";
                tone = AvState.Inert;
            }
            else if (posts == 0)
            {
                hint = "NO POST HELD · A DESK MISSION THAT SUCCEEDS LEAVES ONE";
                tone = AvState.Inert;
            }
            else
            {
                hint = "ARM, RIGHT-CLICK INSIDE A POST'S REACH · " + PostTiles(detachment) + " HELD";
                tone = AvState.Ready;
            }
            specActionsHint.Set(hint, tone);
            PaintPostRows(detachment, support.OrbitNow);
        }

        /// <summary>One row per team: a holding team's post, place, time left and what it grants.</summary>
        private void PaintPostRows(SpecOpsDetachment detachment, double now)
        {
            for (int t = 0; t < specPostRows.Length; t++)
            {
                if (specPostRows[t] == null) continue;
                FieldTeam team = detachment != null ? detachment.Team(t) : default;
                bool holding = detachment != null && team.State == TeamState.Holding;
                string sub, value;
                if (holding)
                {
                    sub = FieldWords.PostCode(team.Mission) + " " + (string.IsNullOrEmpty(team.Target) ? "OBJECTIVE" : team.Target) +
                          " · " + FieldWords.PostGrant(team.Mission).ToUpperInvariant();
                    value = FieldWords.Clock(detachment.Remaining(t, now));
                }
                else
                {
                    sub = "NO POST" + (detachment != null && team.Formed ? " · " + FieldWords.State(team.State) : " · NOT RAISED");
                    value = "";
                }
                specPostRows[t].Set(FieldWords.Callsign(t), sub, value, holding ? AvState.Ready : AvState.Inert);
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
