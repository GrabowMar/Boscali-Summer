using System;

namespace BoscaliSummer.Features.Support.Domain.SpecOps
{
    /// <summary>One team slot. Clocks are the host's absolute scene seconds (rebased on a mirror).</summary>
    internal struct FieldTeam
    {
        public TeamState State;
        public byte Rank;
        public byte Wins;
        public FieldMission Mission;
        public int Anchor;
        public float X, Z;
        public string Target;
        public double PhaseStart, PhaseEnd;
        public byte Chance, Loss;
        public MissionOutcome Last;

        public bool Formed => State != TeamState.Unformed;
        public bool Deployed => State == TeamState.EnRoute || State == TeamState.OnTask || State == TeamState.Holding;
    }

    /// <summary>A place on the real map a team can be sent. The host lists them; clients mirror.</summary>
    internal struct FieldObjective
    {
        public ObjectiveKind Kind;
        public int Anchor;
        public float X, Z;
        public byte Threat;
        public byte Radars;
        public bool Hostile;
        public string Name;
    }

    /// <summary>A mission the host just resolved, handed to the runtime that touches the world.</summary>
    internal readonly struct FieldResult
    {
        public readonly FieldMission Mission;
        public readonly MissionOutcome Outcome;
        public readonly float X, Z;
        public readonly int Rank;
        public readonly int Anchor;

        public FieldResult(FieldMission mission, MissionOutcome outcome, float x, float z, int rank, int anchor)
        {
            Mission = mission;
            Outcome = outcome;
            X = x;
            Z = z;
            Rank = rank;
            Anchor = anchor;
        }
    }

    internal enum FieldNotice : byte
    {
        None = 0,
        Raised = 1,
        Launched = 2,
        OnTask = 3,
        Success = 4,
        Failed = 5,
        Lost = 6,
        Recalled = 7,
        PostEnded = 8,
        Ready = 9,
        NoBuildings = 10
    }

    /// <summary>
    /// One faction's special-operations detachment: four team slots, the objectives the host has
    /// listed, scouting memory, two ability recharges and a notice ring. Missions resolve in the
    /// abstract — travel, task, one roll — and the runtime applies what a success does to the
    /// world. The host is the only writer; a client mirrors snapshot bytes and never ticks.
    /// Every value is bounded: four teams, twelve objectives, sixteen scouting marks, eight notices.
    /// </summary>
    internal sealed class SpecOpsDetachment
    {
        public const int TeamCount = 4;
        public const int StartingTeams = 2;
        public const int ObjectiveSlots = 12;
        public const int NoticeSlots = 8;
        public const int ScoutSlots = 16;
        public const int NameLength = 20;

        private readonly FieldTeam[] teams = new FieldTeam[TeamCount];
        private readonly FieldObjective[] objectives = new FieldObjective[ObjectiveSlots];
        private readonly FieldObjective[] incoming = new FieldObjective[ObjectiveSlots];
        private readonly int[] scoutAnchor = new int[ScoutSlots];
        private readonly double[] scoutUntil = new double[ScoutSlots];
        private readonly double[] abilityReady = new double[FieldCatalog.AbilityCount];
        private readonly FieldNotice[] noticeKind = new FieldNotice[NoticeSlots];
        private readonly byte[] noticeTeam = new byte[NoticeSlots];
        private readonly byte[] noticeMission = new byte[NoticeSlots];
        private int objectiveCount;
        private int incomingCount;
        private int noticeCount;

        public SpecOpsDetachment() => Clear();

        /// <summary>False while the host has SPEC OPS switched off; mirrored from the snapshot.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>True when the host can occupy buildings (Urban Combat garrisons on).</summary>
        public bool SeizeAvailable { get; set; } = true;

        public int NoticeSerial { get; private set; }
        public int NoticeCount => noticeCount;
        public int ObjectiveCount => objectiveCount;

        public FieldTeam Team(int index) => index >= 0 && index < TeamCount ? teams[index] : default;

        public FieldObjective Objective(int slot) => slot >= 0 && slot < objectiveCount ? objectives[slot] : default;

        public int SlotOf(int anchor)
        {
            for (int i = 0; i < objectiveCount; i++)
                if (objectives[i].Anchor == anchor) return i;
            return -1;
        }

        /// <summary>The team on an objective, or -1: one travelling or working there first, else one
        /// holding a post there.</summary>
        public int TeamOn(int anchor)
        {
            int working = WorkingOn(anchor);
            if (working >= 0) return working;
            for (int i = 0; i < TeamCount; i++)
                if (teams[i].State == TeamState.Holding && teams[i].Anchor == anchor) return i;
            return -1;
        }

        /// <summary>The team travelling to or working an objective, or -1. Only this blocks a second
        /// launch: a held post never stops a follow-up mission on the same place.</summary>
        public int WorkingOn(int anchor)
        {
            for (int i = 0; i < TeamCount; i++)
                if ((teams[i].State == TeamState.EnRoute || teams[i].State == TeamState.OnTask) && teams[i].Anchor == anchor)
                    return i;
            return -1;
        }

        public int Count(TeamState state)
        {
            int count = 0;
            for (int i = 0; i < TeamCount; i++)
                if (teams[i].State == state) count++;
            return count;
        }

        public int Formed => TeamCount - Count(TeamState.Unformed);

        /// <summary>The best rank among formed teams: the detachment's ground readiness.</summary>
        public int BestRank
        {
            get
            {
                int best = 0;
                for (int i = 0; i < TeamCount; i++)
                    if (teams[i].Formed && teams[i].Rank > best) best = teams[i].Rank;
                return best;
            }
        }

        /// <summary>Positions one fortification order occupies and camps one fast-rope insertion sets up.</summary>
        public int GroundReadiness => 1 + BestRank;

        public double Remaining(int team, double now) =>
            team >= 0 && team < TeamCount && PhaseTimed(teams[team].State) ? Math.Max(0.0, teams[team].PhaseEnd - now) : 0.0;

        /// <summary>0..1 through the current timed phase; 0 for an untimed one.</summary>
        public float Progress(int team, double now)
        {
            if (team < 0 || team >= TeamCount || !PhaseTimed(teams[team].State)) return 0f;
            double span = teams[team].PhaseEnd - teams[team].PhaseStart;
            if (span <= 0.0) return 1f;
            return (float)Math.Max(0.0, Math.Min(1.0, (now - teams[team].PhaseStart) / span));
        }

        public bool Scouted(int anchor, double now) => ScoutRemaining(anchor, now) > 0.0;

        public double ScoutRemaining(int anchor, double now)
        {
            for (int i = 0; i < ScoutSlots; i++)
                if (scoutAnchor[i] == anchor && scoutUntil[i] > now) return scoutUntil[i] - now;
            return 0.0;
        }

        // ---- Odds for a prospective launch -----------------------------------------------------

        public int ChanceFor(int team, FieldMission mission, int slot, double now)
        {
            if (slot < 0 || slot >= objectiveCount) return 0;
            FieldObjective objective = objectives[slot];
            return FieldCatalog.SuccessChance(mission, Team(team).Rank, objective.Threat, Scouted(objective.Anchor, now));
        }

        public int LossFor(int team, FieldMission mission, int slot, double now) =>
            slot < 0 || slot >= objectiveCount ? 0
                : FieldCatalog.LossChance(mission, Team(team).Rank, objectives[slot].Threat, ChanceFor(team, mission, slot, now));

        // ---- Checks (the host re-runs every one) ------------------------------------------------

        public SpecOpsDenial CheckRaise(int team)
        {
            if (!Enabled) return SpecOpsDenial.Disabled;
            if (team < 0 || team >= TeamCount) return SpecOpsDenial.BadTeam;
            return teams[team].Formed ? SpecOpsDenial.AlreadyFormed : SpecOpsDenial.None;
        }

        public SpecOpsDenial CheckLaunch(int team, FieldMission mission, int anchor)
        {
            if (!Enabled) return SpecOpsDenial.Disabled;
            if (team < 0 || team >= TeamCount) return SpecOpsDenial.BadTeam;
            if (!FieldCatalog.KnownMission((byte)mission)) return SpecOpsDenial.BadMission;
            if (!teams[team].Formed) return SpecOpsDenial.Unformed;
            if (teams[team].State != TeamState.Ready) return SpecOpsDenial.Busy;
            int slot = SlotOf(anchor);
            if (slot < 0) return SpecOpsDenial.StaleObjective;
            FieldObjective objective = objectives[slot];
            if (!FieldCatalog.Allowed(mission, objective.Kind)) return SpecOpsDenial.WrongObjective;
            if (mission == FieldMission.Sabotage && objective.Radars == 0) return SpecOpsDenial.NoRadars;
            if (mission == FieldMission.Seize && !SeizeAvailable) return SpecOpsDenial.SeizeUnavailable;
            return WorkingOn(anchor) >= 0 ? SpecOpsDenial.ObjectiveTaken : SpecOpsDenial.None;
        }

        public SpecOpsDenial CheckRecall(int team)
        {
            if (team < 0 || team >= TeamCount) return SpecOpsDenial.BadTeam;
            if (!teams[team].Formed) return SpecOpsDenial.Unformed;
            return teams[team].Deployed ? SpecOpsDenial.None : SpecOpsDenial.NotDeployed;
        }

        // ---- Host mutations -------------------------------------------------------------------

        public bool TryRaise(int team)
        {
            if (CheckRaise(team) != SpecOpsDenial.None) return false;
            teams[team] = new FieldTeam { State = TeamState.Ready, Anchor = 0, Target = "" };
            Notify(FieldNotice.Raised, team, 0);
            return true;
        }

        /// <summary>Launch with the odds fixed now; <paramref name="travelMetres"/> is the distance from
        /// the nearest owned airbase (negative when there is none). The manager prices and charges.</summary>
        public SpecOpsDenial TryLaunch(int team, FieldMission mission, int anchor, float travelMetres, double now)
        {
            SpecOpsDenial denial = CheckLaunch(team, mission, anchor);
            if (denial != SpecOpsDenial.None) return denial;
            int slot = SlotOf(anchor);
            FieldObjective objective = objectives[slot];
            int chance = ChanceFor(team, mission, slot, now);
            FieldTeam value = teams[team];
            value.State = TeamState.EnRoute;
            value.Mission = mission;
            value.Anchor = anchor;
            value.X = objective.X;
            value.Z = objective.Z;
            value.Target = objective.Name ?? "";
            value.Chance = (byte)chance;
            value.Loss = (byte)FieldCatalog.LossChance(mission, value.Rank, objective.Threat, chance);
            value.PhaseStart = now;
            value.PhaseEnd = now + FieldCatalog.TravelSeconds(travelMetres);
            value.Last = MissionOutcome.None;
            teams[team] = value;
            Notify(FieldNotice.Launched, team, (byte)mission);
            return SpecOpsDenial.None;
        }

        public bool TryRecall(int team, double now)
        {
            if (CheckRecall(team) != SpecOpsDenial.None) return false;
            FieldTeam value = teams[team];
            value.Last = MissionOutcome.Recalled;
            Rest(ref value, now, FieldCatalog.RecoverSeconds);
            teams[team] = value;
            Notify(FieldNotice.Recalled, team, (byte)value.Mission);
            return true;
        }

        /// <summary>
        /// Host: advance every team. A finished task rolls once through <paramref name="roll"/>
        /// (uniform in [0,1)); a success is handed to <paramref name="apply"/>, which touches the
        /// world and says whether the effect landed (a SEIZE with no building to take did not).
        /// </summary>
        public void Tick(double now, Func<double> roll, Func<FieldResult, bool> apply)
        {
            for (int i = 0; i < TeamCount; i++)
            {
                FieldTeam team = teams[i];
                // At most one transition per team per tick keeps a stalled host from skipping phases silently.
                if (!PhaseTimed(team.State) || now < team.PhaseEnd) continue;
                switch (team.State)
                {
                    case TeamState.EnRoute:
                        team.State = TeamState.OnTask;
                        team.PhaseStart = now;
                        team.PhaseEnd = now + FieldCatalog.TaskSeconds(team.Mission);
                        teams[i] = team;
                        Notify(FieldNotice.OnTask, i, (byte)team.Mission);
                        break;
                    case TeamState.OnTask:
                        Resolve(i, now, roll, apply);
                        break;
                    case TeamState.Holding:
                        Rest(ref team, now, FieldCatalog.RecoverSeconds);
                        teams[i] = team;
                        Notify(FieldNotice.PostEnded, i, (byte)team.Mission);
                        break;
                    case TeamState.Recovering:
                        team.State = TeamState.Ready;
                        team.PhaseStart = team.PhaseEnd = 0.0;
                        team.Anchor = 0;
                        teams[i] = team;
                        Notify(FieldNotice.Ready, i, 0);
                        break;
                }
            }
            for (int i = 0; i < ScoutSlots; i++)
                if (scoutUntil[i] > 0.0 && scoutUntil[i] <= now)
                {
                    scoutUntil[i] = 0.0;
                    scoutAnchor[i] = 0;
                }
        }

        private void Resolve(int index, double now, Func<double> roll, Func<FieldResult, bool> apply)
        {
            FieldTeam team = teams[index];
            MissionOutcome outcome = FieldCatalog.Resolve(team.Chance, team.Loss, roll != null ? roll() : 0.0);
            if (outcome == MissionOutcome.Lost)
            {
                teams[index] = new FieldTeam { State = TeamState.Unformed, Last = MissionOutcome.Lost, Target = team.Target, Mission = team.Mission };
                Notify(FieldNotice.Lost, index, (byte)team.Mission);
                return;
            }
            if (outcome == MissionOutcome.Failed)
            {
                team.Last = MissionOutcome.Failed;
                Rest(ref team, now, FieldCatalog.FailedRecoverSeconds);
                teams[index] = team;
                Notify(FieldNotice.Failed, index, (byte)team.Mission);
                return;
            }

            var result = new FieldResult(team.Mission, outcome, team.X, team.Z, team.Rank, team.Anchor);
            bool landed = apply == null || apply(result);
            if (landed)
            {
                if (team.Wins < byte.MaxValue) team.Wins++;
                team.Rank = (byte)FieldCatalog.RankFor(team.Wins);
                if (team.Mission == FieldMission.Recon) MarkScouted(team.Anchor, now);
                team.Last = MissionOutcome.Success;
                team.State = TeamState.Holding;
                team.PhaseStart = now;
                team.PhaseEnd = now + FieldCatalog.HoldSeconds(team.Rank);
                teams[index] = team;
                Notify(FieldNotice.Success, index, (byte)team.Mission);
            }
            else
            {
                team.Last = MissionOutcome.NoBuildings;
                Rest(ref team, now, FieldCatalog.RecoverSeconds);
                teams[index] = team;
                Notify(FieldNotice.NoBuildings, index, (byte)team.Mission);
            }
        }

        /// <summary>Host, every two seconds: replace the objective list. Bounded; bad entries dropped.</summary>
        public void BeginObjectives() => incomingCount = 0;

        public void ReportObjective(ObjectiveKind kind, int anchor, float x, float z, int threat, int radars,
                                    bool hostile, string name)
        {
            if (incomingCount >= ObjectiveSlots || kind == ObjectiveKind.None || !Finite(x) || !Finite(z)) return;
            for (int i = 0; i < incomingCount; i++)
                if (incoming[i].Anchor == anchor) return;
            incoming[incomingCount++] = new FieldObjective
            {
                Kind = kind, Anchor = anchor, X = x, Z = z,
                Threat = (byte)Math.Max(0, Math.Min(99, threat)),
                Radars = (byte)Math.Max(0, Math.Min(99, radars)),
                Hostile = hostile, Name = Clip(name)
            };
        }

        public void EndObjectives()
        {
            Array.Copy(incoming, objectives, incomingCount);
            for (int i = incomingCount; i < ObjectiveSlots; i++) objectives[i] = default;
            objectiveCount = incomingCount;
        }

        // ---- Posts and abilities -----------------------------------------------------------------

        /// <summary>Teams holding a post of this kind.</summary>
        public int Posts(FieldMission post)
        {
            int count = 0;
            for (int i = 0; i < TeamCount; i++)
                if (teams[i].State == TeamState.Holding && teams[i].Mission == post) count++;
            return count;
        }

        public int Posts()
        {
            int count = 0;
            for (int i = 0; i < TeamCount; i++)
                if (teams[i].State == TeamState.Holding) count++;
            return count;
        }

        /// <summary>The highest-ranked team holding <paramref name="post"/> whose reach covers the point, or -1.</summary>
        public int Covering(FieldMission post, float x, float z)
        {
            float reach = FieldCatalog.PostReach(post);
            int best = -1;
            for (int i = 0; i < TeamCount; i++)
            {
                FieldTeam team = teams[i];
                if (team.State != TeamState.Holding || team.Mission != post) continue;
                float dx = team.X - x, dz = team.Z - z;
                if (dx * dx + dz * dz > reach * reach) continue;
                if (best < 0 || team.Rank > teams[best].Rank) best = i;
            }
            return best;
        }

        public float AbilityRechargeRemaining(FieldAbility ability, double now)
        {
            int index = (int)ability;
            return index < 0 || index >= abilityReady.Length ? 0f : (float)Math.Max(0.0, abilityReady[index] - now);
        }

        public bool TryUseAbility(FieldAbility ability, double now)
        {
            int index = (int)ability;
            if (index < 0 || index >= abilityReady.Length || abilityReady[index] > now) return false;
            abilityReady[index] = now + FieldCatalog.AbilityRecharge(ability);
            return true;
        }

        // ---- Notices -------------------------------------------------------------------------------

        /// <summary>Newest first.</summary>
        public FieldNotice NoticeKind(int age) => age >= 0 && age < noticeCount ? noticeKind[age] : FieldNotice.None;

        public int NoticeTeam(int age) => age >= 0 && age < noticeCount ? noticeTeam[age] : -1;

        public FieldMission NoticeMission(int age) =>
            age >= 0 && age < noticeCount && noticeMission[age] < FieldCatalog.MissionCount ? (FieldMission)noticeMission[age] : FieldMission.Recon;

        private void Notify(FieldNotice kind, int team, byte mission)
        {
            for (int i = NoticeSlots - 1; i > 0; i--)
            {
                noticeKind[i] = noticeKind[i - 1];
                noticeTeam[i] = noticeTeam[i - 1];
                noticeMission[i] = noticeMission[i - 1];
            }
            noticeKind[0] = kind;
            noticeTeam[0] = (byte)team;
            noticeMission[0] = mission;
            noticeCount = Math.Min(NoticeSlots, noticeCount + 1);
            NoticeSerial++;
        }

        // ---- Snapshot -------------------------------------------------------------------------------

        public void Export(double now, SpecOpsSnapshot into)
        {
            into.Clear();
            into.Flags = (byte)((Enabled ? 1 : 0) | (SeizeAvailable ? 2 : 0));
            for (int i = 0; i < TeamCount; i++)
            {
                FieldTeam team = teams[i];
                into.TeamState[i] = (byte)team.State;
                into.TeamRank[i] = team.Rank;
                into.TeamWins[i] = team.Wins;
                into.TeamMission[i] = (byte)team.Mission;
                into.TeamChance[i] = team.Chance;
                into.TeamLoss[i] = team.Loss;
                into.TeamLast[i] = (byte)team.Last;
                into.TeamAnchor[i] = team.Anchor;
                into.TeamX[i] = team.X;
                into.TeamZ[i] = team.Z;
                into.TeamTarget[i] = Clip(team.Target);
                bool timed = PhaseTimed(team.State);
                into.TeamRemaining[i] = timed ? (float)Math.Max(0.0, team.PhaseEnd - now) : 0f;
                into.TeamDuration[i] = timed ? (float)Math.Max(0.0, team.PhaseEnd - team.PhaseStart) : 0f;
            }
            into.ObjectiveCount = (byte)objectiveCount;
            for (int i = 0; i < objectiveCount; i++)
            {
                FieldObjective objective = objectives[i];
                into.ObjectiveKind[i] = (byte)objective.Kind;
                into.ObjectiveAnchor[i] = objective.Anchor;
                into.ObjectiveX[i] = objective.X;
                into.ObjectiveZ[i] = objective.Z;
                into.ObjectiveThreat[i] = objective.Threat;
                into.ObjectiveRadars[i] = objective.Radars;
                into.ObjectiveHostile[i] = objective.Hostile;
                into.ObjectiveScout[i] = (float)ScoutRemaining(objective.Anchor, now);
                into.ObjectiveName[i] = Clip(objective.Name);
            }
            for (int a = 0; a < abilityReady.Length; a++)
                into.AbilityRecharge[a] = AbilityRechargeRemaining((FieldAbility)a, now);
            into.NoticeSerial = NoticeSerial;
            into.NoticeCount = (byte)noticeCount;
            for (int i = 0; i < NoticeSlots; i++)
            {
                into.NoticeKind[i] = (byte)noticeKind[i];
                into.NoticeTeam[i] = noticeTeam[i];
                into.NoticeMission[i] = noticeMission[i];
            }
        }

        /// <summary>Client: rebuild from a host snapshot. Out-of-range values are clamped or dropped.</summary>
        public void Mirror(SpecOpsSnapshot from, double now)
        {
            if (from == null) return;
            Enabled = (from.Flags & 1) != 0;
            SeizeAvailable = (from.Flags & 2) != 0;
            for (int i = 0; i < TeamCount; i++)
            {
                FieldTeam team = teams[i];
                byte state = from.TeamState[i];
                team.State = state <= (byte)TeamState.Recovering ? (TeamState)state : TeamState.Unformed;
                team.Rank = (byte)Math.Min(FieldCatalog.MaxRank, (int)from.TeamRank[i]);
                team.Wins = from.TeamWins[i];
                team.Mission = FieldCatalog.KnownMission(from.TeamMission[i]) ? (FieldMission)from.TeamMission[i] : FieldMission.Recon;
                team.Chance = (byte)Math.Min(100, (int)from.TeamChance[i]);
                team.Loss = (byte)Math.Min(100 - team.Chance, (int)from.TeamLoss[i]);
                team.Last = from.TeamLast[i] <= (byte)MissionOutcome.NoBuildings ? (MissionOutcome)from.TeamLast[i] : MissionOutcome.None;
                team.Anchor = from.TeamAnchor[i];
                team.X = Finite(from.TeamX[i]) ? from.TeamX[i] : 0f;
                team.Z = Finite(from.TeamZ[i]) ? from.TeamZ[i] : 0f;
                team.Target = Clip(from.TeamTarget[i]);
                if (PhaseTimed(team.State))
                {
                    float duration = Finite(from.TeamDuration[i]) ? Math.Max(0f, Math.Min(from.TeamDuration[i], 3600f)) : 0f;
                    float remaining = Finite(from.TeamRemaining[i]) ? Math.Max(0f, Math.Min(from.TeamRemaining[i], duration)) : 0f;
                    double end = now + remaining;
                    if (Math.Abs(team.PhaseEnd - end) > 0.5) team.PhaseEnd = end;
                    team.PhaseStart = team.PhaseEnd - duration;
                }
                else
                {
                    team.PhaseStart = team.PhaseEnd = 0.0;
                }
                teams[i] = team;
            }

            int count = Math.Min((int)from.ObjectiveCount, ObjectiveSlots);
            int kept = 0;
            Array.Clear(scoutAnchor, 0, ScoutSlots);
            Array.Clear(scoutUntil, 0, ScoutSlots);
            int scouts = 0;
            for (int i = 0; i < count; i++)
            {
                if (!FieldCatalog.KnownKind(from.ObjectiveKind[i]) || !Finite(from.ObjectiveX[i]) || !Finite(from.ObjectiveZ[i])) continue;
                objectives[kept++] = new FieldObjective
                {
                    Kind = (ObjectiveKind)from.ObjectiveKind[i], Anchor = from.ObjectiveAnchor[i],
                    X = from.ObjectiveX[i], Z = from.ObjectiveZ[i],
                    Threat = (byte)Math.Min(99, (int)from.ObjectiveThreat[i]),
                    Radars = (byte)Math.Min(99, (int)from.ObjectiveRadars[i]),
                    Hostile = from.ObjectiveHostile[i], Name = Clip(from.ObjectiveName[i])
                };
                float scout = from.ObjectiveScout[i];
                if (Finite(scout) && scout > 0f && scouts < ScoutSlots)
                {
                    scoutAnchor[scouts] = from.ObjectiveAnchor[i];
                    scoutUntil[scouts] = now + Math.Min(scout, FieldCatalog.ScoutSeconds);
                    scouts++;
                }
            }
            for (int i = kept; i < ObjectiveSlots; i++) objectives[i] = default;
            objectiveCount = kept;

            for (int a = 0; a < abilityReady.Length; a++)
            {
                float left = from.AbilityRecharge[a];
                abilityReady[a] = Finite(left) && left > 0f
                    ? now + Math.Min(left, FieldCatalog.AbilityRecharge((FieldAbility)a))
                    : 0.0;
            }

            noticeCount = Math.Min((int)from.NoticeCount, NoticeSlots);
            for (int i = 0; i < NoticeSlots; i++)
            {
                byte kind = from.NoticeKind[i];
                noticeKind[i] = i < noticeCount && kind <= (byte)FieldNotice.NoBuildings ? (FieldNotice)kind : FieldNotice.None;
                noticeTeam[i] = (byte)Math.Min(TeamCount - 1, (int)from.NoticeTeam[i]);
                noticeMission[i] = (byte)Math.Min(FieldCatalog.MissionCount - 1, (int)from.NoticeMission[i]);
            }
            NoticeSerial = from.NoticeSerial;
        }

        /// <summary>Back to a fresh detachment: ALPHA and BRAVO formed at RECRUIT, nothing listed.</summary>
        public void Clear()
        {
            for (int i = 0; i < TeamCount; i++)
                teams[i] = new FieldTeam { State = i < StartingTeams ? TeamState.Ready : TeamState.Unformed, Target = "" };
            Array.Clear(objectives, 0, ObjectiveSlots);
            Array.Clear(incoming, 0, ObjectiveSlots);
            Array.Clear(scoutAnchor, 0, ScoutSlots);
            Array.Clear(scoutUntil, 0, ScoutSlots);
            Array.Clear(abilityReady, 0, abilityReady.Length);
            Array.Clear(noticeKind, 0, NoticeSlots);
            Array.Clear(noticeTeam, 0, NoticeSlots);
            Array.Clear(noticeMission, 0, NoticeSlots);
            objectiveCount = incomingCount = noticeCount = 0;
            NoticeSerial = 0;
            Enabled = true;
            SeizeAvailable = true;
        }

        // ---- Helpers ---------------------------------------------------------------------------------

        private void MarkScouted(int anchor, double now)
        {
            int slot = -1;
            double oldest = double.MaxValue;
            for (int i = 0; i < ScoutSlots; i++)
            {
                if (scoutAnchor[i] == anchor) { slot = i; break; }
                if (scoutUntil[i] < oldest) { oldest = scoutUntil[i]; slot = i; }
            }
            scoutAnchor[slot] = anchor;
            scoutUntil[slot] = now + FieldCatalog.ScoutSeconds;
        }

        private static void Rest(ref FieldTeam team, double now, float seconds)
        {
            team.State = TeamState.Recovering;
            team.PhaseStart = now;
            team.PhaseEnd = now + seconds;
        }

        private static bool PhaseTimed(TeamState state) =>
            state == TeamState.EnRoute || state == TeamState.OnTask || state == TeamState.Holding || state == TeamState.Recovering;

        internal static string Clip(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= NameLength ? value : value.Substring(0, NameLength);
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>Fixed-size wire image of one detachment; clocks relative to the host's moment.</summary>
    internal sealed class SpecOpsSnapshot
    {
        public byte Flags;
        public readonly byte[] TeamState = new byte[SpecOpsDetachment.TeamCount];
        public readonly byte[] TeamRank = new byte[SpecOpsDetachment.TeamCount];
        public readonly byte[] TeamWins = new byte[SpecOpsDetachment.TeamCount];
        public readonly byte[] TeamMission = new byte[SpecOpsDetachment.TeamCount];
        public readonly byte[] TeamChance = new byte[SpecOpsDetachment.TeamCount];
        public readonly byte[] TeamLoss = new byte[SpecOpsDetachment.TeamCount];
        public readonly byte[] TeamLast = new byte[SpecOpsDetachment.TeamCount];
        public readonly int[] TeamAnchor = new int[SpecOpsDetachment.TeamCount];
        public readonly float[] TeamX = new float[SpecOpsDetachment.TeamCount];
        public readonly float[] TeamZ = new float[SpecOpsDetachment.TeamCount];
        public readonly float[] TeamRemaining = new float[SpecOpsDetachment.TeamCount];
        public readonly float[] TeamDuration = new float[SpecOpsDetachment.TeamCount];
        public readonly string[] TeamTarget = new string[SpecOpsDetachment.TeamCount];

        public byte ObjectiveCount;
        public readonly byte[] ObjectiveKind = new byte[SpecOpsDetachment.ObjectiveSlots];
        public readonly int[] ObjectiveAnchor = new int[SpecOpsDetachment.ObjectiveSlots];
        public readonly float[] ObjectiveX = new float[SpecOpsDetachment.ObjectiveSlots];
        public readonly float[] ObjectiveZ = new float[SpecOpsDetachment.ObjectiveSlots];
        public readonly byte[] ObjectiveThreat = new byte[SpecOpsDetachment.ObjectiveSlots];
        public readonly byte[] ObjectiveRadars = new byte[SpecOpsDetachment.ObjectiveSlots];
        public readonly bool[] ObjectiveHostile = new bool[SpecOpsDetachment.ObjectiveSlots];
        public readonly float[] ObjectiveScout = new float[SpecOpsDetachment.ObjectiveSlots];
        public readonly string[] ObjectiveName = new string[SpecOpsDetachment.ObjectiveSlots];

        public readonly float[] AbilityRecharge = new float[FieldCatalog.AbilityCount];

        public int NoticeSerial;
        public byte NoticeCount;
        public readonly byte[] NoticeKind = new byte[SpecOpsDetachment.NoticeSlots];
        public readonly byte[] NoticeTeam = new byte[SpecOpsDetachment.NoticeSlots];
        public readonly byte[] NoticeMission = new byte[SpecOpsDetachment.NoticeSlots];

        public void Clear()
        {
            Flags = 0;
            Array.Clear(TeamState, 0, TeamState.Length);
            Array.Clear(TeamRank, 0, TeamRank.Length);
            Array.Clear(TeamWins, 0, TeamWins.Length);
            Array.Clear(TeamMission, 0, TeamMission.Length);
            Array.Clear(TeamChance, 0, TeamChance.Length);
            Array.Clear(TeamLoss, 0, TeamLoss.Length);
            Array.Clear(TeamLast, 0, TeamLast.Length);
            Array.Clear(TeamAnchor, 0, TeamAnchor.Length);
            Array.Clear(TeamX, 0, TeamX.Length);
            Array.Clear(TeamZ, 0, TeamZ.Length);
            Array.Clear(TeamRemaining, 0, TeamRemaining.Length);
            Array.Clear(TeamDuration, 0, TeamDuration.Length);
            Array.Clear(TeamTarget, 0, TeamTarget.Length);
            ObjectiveCount = 0;
            Array.Clear(ObjectiveKind, 0, ObjectiveKind.Length);
            Array.Clear(ObjectiveAnchor, 0, ObjectiveAnchor.Length);
            Array.Clear(ObjectiveX, 0, ObjectiveX.Length);
            Array.Clear(ObjectiveZ, 0, ObjectiveZ.Length);
            Array.Clear(ObjectiveThreat, 0, ObjectiveThreat.Length);
            Array.Clear(ObjectiveRadars, 0, ObjectiveRadars.Length);
            Array.Clear(ObjectiveHostile, 0, ObjectiveHostile.Length);
            Array.Clear(ObjectiveScout, 0, ObjectiveScout.Length);
            Array.Clear(ObjectiveName, 0, ObjectiveName.Length);
            Array.Clear(AbilityRecharge, 0, AbilityRecharge.Length);
            NoticeSerial = 0;
            NoticeCount = 0;
            Array.Clear(NoticeKind, 0, NoticeKind.Length);
            Array.Clear(NoticeTeam, 0, NoticeTeam.Length);
            Array.Clear(NoticeMission, 0, NoticeMission.Length);
        }
    }
}
