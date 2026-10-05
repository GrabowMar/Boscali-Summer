using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Sof
{
    /// <summary>What OVERLORD needs from the live game that the SOF desk does not know. The runtime implements it over the real enemy world; the offline sims over a scripted one.</summary>
    internal interface ISofWatchWorld
    {
        /// <summary>Connected humans of the faction.</summary>
        int Humans { get; }

        /// <summary>RECON reveals ground units through a native sighting (the same path a SPACE scan uses), so a faction with no humans may not run it.</summary>
        bool AllowRecon { get; }

        /// <summary>The vanilla value and weight class of a revealed ground contact; false when the host cannot resolve the unit.</summary>
        bool TryGround(in SofTarget target, out float value, out WatchKind kind);

        /// <summary>The faction's CYBER holds a node within 12 km of the point (the odds' ring boost).</summary>
        bool CyberNear(float x, float z);
    }

    internal enum SofWatchAction : byte { None = 0, Raise = 1, Mission = 2, Order = 3 }

    /// <summary>Why OVERLORD did nothing on a think. None when it acted.</summary>
    internal enum SofWatchWhy : byte { None, NotDue, Paced, Suspended, NoCamp, NoSlot, NoWork, Waiting, Resting, Failed }

    internal readonly struct SofWatchPlan
    {
        public readonly SofWatchAction Action;
        public readonly SofWatchWhy Why;
        public readonly int Slot, TargetId;
        public readonly MissionKind Mission;
        public readonly TeamVerb Verb;
        public readonly float X, Z;
        public readonly WatchCode Code;
        public readonly int A, B;
        public readonly SofOutcome Outcome;

        public SofWatchPlan(SofWatchAction action, SofWatchWhy why, int slot = 0, MissionKind mission = MissionKind.None, int targetId = 0, TeamVerb verb = TeamVerb.Cancel,
            float x = 0f, float z = 0f, WatchCode code = WatchCode.None, int a = 0, int b = 0, SofOutcome outcome = SofOutcome.None)
        { Action = action; Why = why; Slot = slot; Mission = mission; TargetId = targetId; Verb = verb; X = x; Z = z; Code = code; A = a; B = b; Outcome = outcome; }

        public string Reason => WatchWords.Reason(Code, A, B);

        internal static SofWatchPlan Idle(SofWatchWhy why) => new SofWatchPlan(SofWatchAction.None, why);
    }

    /// <summary>
    /// WATCH OFFICER OVERLORD for SOF (spec section 4), one faction. It works the same <see cref="SofDesk"/> a human operator does, as the reserved identity: it keeps at most one team of its own
    /// in the field, raises it when the camp is LIVE and there is work, sends it to LASE or RECON a high-value revealed contact near the front or to SABOTAGE a revealed enemy anchor when the
    /// odds are at least 55 %, and steers it with PUSH, HOLD and EXFIL by its exposure. It sees only <see cref="SofDesk.Visible"/> (what the faction has really sighted) and it never orders the helicopter
    /// lift: the lift is for human pilots. It has no wallet: raises and missions are free. Bounded by the pacer.
    /// </summary>
    internal sealed class SofWatchBrain
    {
        public const float ThinkSeconds = 2f, MinSabotageOdds = 55f, HighValueScore = 10f, NearFrontMeters = 20000f, MaxEtaSeconds = 900f, SabotageCooldownSeconds = 600f,
            TargetBackoffSeconds = 600f, FailBackoffSeconds = 60f, ReconSpacingMeters = 1500f, ReconStandOffMeters = 1900f, ReconRepeatSeconds = 300f, LostRestSeconds = 120f,
            UrgentFloor = 15f, ApproachMeters = 2600f, PushBelow = 20f, PushStopAbove = 35f, HoldAt = 50f, ResumeBelow = 30f, ExfilAt = 40f, PushMinMeters = 1500f, StrandedMeters = 300f, RisingEpsilon = 0.2f;
        public const float ReadyMaxExposure = 10f, CommitCeiling = 85f, CommitMargin = 15f, OnSiteHoldAt = 45f, CommitHoldSeconds = 90f, ProjectedExfil = 95f, ProjectedFloor = 40f, RiskRadiusMeters = 2000f, ArmourRadiusMeters = 1000f;
        public const int MaxBackoffs = 32, MaxRecon = 4, MaxRevealedNear = 3;
        private const ulong Me = SpaceContacts.WatchOfficerId;

        private readonly WatchIdle idle = new WatchIdle();
        private readonly Dictionary<int, float> backoff = new Dictionary<int, float>(MaxBackoffs);
        private readonly List<int> scratch = new List<int>(MaxBackoffs);
        private readonly float[] reconX = new float[MaxRecon], reconZ = new float[MaxRecon], reconAt = new float[MaxRecon];
        private int reconHead, reconCount;
        private float nextThinkAt, restUntil, sabotageAt = float.NegativeInfinity, clock;
        private float lastExposure = -1f, lastExposureAt;
        private int lastSlot = -1;

        public int Raises, Missions, Orders, Failures, Thinks;
        public SofWatchPlan Last { get; private set; }

        /// <summary>A human of this faction did a SOF verb (RAISE, an order, a mission or a divert): OVERLORD yields (60 s alone, 300 s with company).</summary>
        public void RecordHuman(float now) => idle.RecordHuman(now);

        public bool Idle(int humans, float now) => idle.Idle(humans, now);

        public bool Due(float now) => SpaceRules.MissionTime(now) && now >= nextThinkAt;

        public void Defer(float now, float seconds)
        {
            if (SpaceRules.MissionTime(now) && SpaceRules.Finite(seconds) && seconds > 0f) nextThinkAt = Math.Max(nextThinkAt, now + seconds);
        }

        public void Reset()
        {
            idle.Reset(); backoff.Clear();
            Array.Clear(reconAt, 0, reconAt.Length);
            reconHead = reconCount = 0;
            nextThinkAt = restUntil = clock = lastExposureAt = 0f; sabotageAt = float.NegativeInfinity; lastExposure = -1f; lastSlot = -1;
            Raises = Missions = Orders = Failures = Thinks = 0;
            Last = default;
        }

        // ---- The think -------------------------------------------------------------------------

        public SofWatchPlan Step(SofDesk desk, ISofWatchWorld world, WatchPacer pacer, bool aiFaction, float now)
        {
            if (!SpaceRules.MissionTime(now) || now < nextThinkAt) return SofWatchPlan.Idle(SofWatchWhy.NotDue);
            nextThinkAt = now + ThinkSeconds;
            clock = now;
            Thinks++;
            SofWatchPlan plan = Decide(desk, world, pacer, aiFaction, now);
            if (plan.Action == SofWatchAction.None) { Last = plan; return plan; }
            if (!pacer.CanAct(WatchDomain.Sof, aiFaction, now)) { Last = SofWatchPlan.Idle(SofWatchWhy.Paced); return Last; }
            Last = Execute(desk, plan, pacer, aiFaction, now);
            return Last;
        }

        private SofWatchPlan Decide(SofDesk desk, ISofWatchWorld world, WatchPacer pacer, bool aiFaction, float now)
        {
            Prune(now);
            int humans = world.Humans;
            SofTeam team = Mine(desk);
            if (team != null && team.State == TeamState.Lost) restUntil = Math.Max(restUntil, now + LostRestSeconds);
            pacer.SetUrgent(WatchDomain.Sof, false); // refreshed below only while a team is in danger: a stale reservation would hold CYBER off the shared limiter
            if (!idle.Idle(humans, now)) return SofWatchPlan.Idle(SofWatchWhy.Suspended);
            if (team == null || team.State == TeamState.Lost) return RaisePlan(desk, world, humans, now);
            if (team.Slot != lastSlot) { lastSlot = team.Slot; lastExposure = -1f; }
            float prior = lastExposure, priorAt = lastExposureAt;
            lastExposure = team.Exposure; lastExposureAt = now;
            switch (team.State)
            {
                case TeamState.Moving:
                case TeamState.Returning:
                case TeamState.OnSite:
                    return SteerPlan(desk, team, prior, priorAt, pacer, aiFaction, now);
                case TeamState.Ready:
                    return ReadyPlan(desk, world, team, now);
                default:
                    return SofWatchPlan.Idle(SofWatchWhy.Waiting); // raising, recovering, pinned (the COVER post is up) or carried by a human pilot
            }
        }

        private SofTeam Mine(SofDesk desk)
        {
            for (int i = 0; i < desk.Teams.Length; i++) if (desk.Teams[i].Active && desk.Teams[i].Raiser == Me) return desk.Teams[i];
            return null;
        }

        // ---- Raise -----------------------------------------------------------------------------

        private SofWatchPlan RaisePlan(SofDesk desk, ISofWatchWorld world, int humans, float now)
        {
            if (now < restUntil) return SofWatchPlan.Idle(SofWatchWhy.Resting);
            if (!desk.FobActive)
            {
                if (desk.Camps.Count == 0) return SofWatchPlan.Idle(SofWatchWhy.NoCamp);
                // RAISE only while the camp is LIVE: a damaged camp raises slowly and a down camp not at all.
                if (!desk.Camps.TryBest(out _, out _, out _, out AnchorHealth health) || health != AnchorHealth.Live) return SofWatchPlan.Idle(SofWatchWhy.NoCamp);
            }
            int cap = SofRules.TeamCap(humans, desk.FobActive) - (humans > 0 ? 1 : 0); // one slot always stays free for a human
            if (desk.ActiveCount >= cap) return SofWatchPlan.Idle(SofWatchWhy.NoSlot);
            if (!HasWork(desk, world, now)) return SofWatchPlan.Idle(SofWatchWhy.NoWork);
            return new SofWatchPlan(SofWatchAction.Raise, SofWatchWhy.None, 0, MissionKind.None, 0, TeamVerb.Cancel, 0f, 0f, WatchCode.SofRaise);
        }

        private bool HasWork(SofDesk desk, ISofWatchWorld world, float now)
        {
            var probe = new SofTeam { Slot = 0, Active = true, State = TeamState.Ready, X = 0f, Z = 0f, Ammo = 100f };
            if (desk.Camps.TryBest(out _, out float cx, out float cz, out _)) { probe.X = cx; probe.Z = cz; }
            if (desk.FobActive) { probe.X = desk.FobX; probe.Z = desk.FobZ; }
            return Choose(desk, world, probe, now).Action != SofWatchAction.None;
        }

        // ---- Missions --------------------------------------------------------------------------

        private SofWatchPlan ReadyPlan(SofDesk desk, ISofWatchWorld world, SofTeam team, float now)
        {
            bool away = BaseDistance(desk, team) > StrandedMeters;
            if (team.Ammo + 0.01f < SofDesk.AmmoPerMission)
                return away ? Exfil(team, WatchCode.SofExfilDone, (int)Math.Round(team.Ammo)) : SofWatchPlan.Idle(SofWatchWhy.Waiting);
            // A hot team does not start a new mission: out in the field it goes home to cool off, at the camp it waits until the exposure has fallen.
            if (team.Exposure > ReadyMaxExposure) return away ? Exfil(team, WatchCode.SofExfilDone, (int)Math.Round(team.Ammo)) : SofWatchPlan.Idle(SofWatchWhy.Waiting);
            SofWatchPlan pick = Choose(desk, world, team, now);
            if (pick.Action != SofWatchAction.None) return WithSlot(pick, team.Slot);
            return away ? Exfil(team, WatchCode.SofExfilDone, (int)Math.Round(team.Ammo)) : SofWatchPlan.Idle(SofWatchWhy.NoWork);
        }

        private static SofWatchPlan WithSlot(in SofWatchPlan p, int slot)
        {
            int a = p.Code == WatchCode.SofSabotage ? (p.A & ~3) | (slot & 3) : p.Code == WatchCode.SofLase || p.Code == WatchCode.SofRecon ? slot : p.A;
            return new SofWatchPlan(p.Action, p.Why, slot, p.Mission, p.TargetId, p.Verb, p.X, p.Z, p.Code, a, p.B);
        }

        /// <summary>The best mission for this team from what the faction has revealed: SABOTAGE an anchor at 55 % or better, else LASE a high-value contact near the front, else RECON a contact near the front.</summary>
        private SofWatchPlan Choose(SofDesk desk, ISofWatchWorld world, SofTeam team, float now)
        {
            IReadOnlyList<SofTarget> visible = desk.Visible;
            // SABOTAGE: the highest odds, then the more valuable anchor, then the nearer one.
            if (now - sabotageAt >= SabotageCooldownSeconds)
            {
                int bestIndex = -1, bestOdds = 0;
                for (int i = 0; i < visible.Count; i++)
                {
                    SofTarget t = visible[i];
                    if (t.Kind != TargetKind.Anchor || Backed(t.Id) || !Reachable(team, t) || RevealedNear(visible, world, t, RiskRadiusMeters, false) > MaxRevealedNear) continue;
                    int odds = SofRules.Odds(team.Exposure, RevealedNear(visible, world, t, ArmourRadiusMeters, true), false, SofRules.Exploit(t.Kind, t.Sub), world.CyberNear(t.X, t.Z));
                    if (odds < MinSabotageOdds) continue;
                    if (bestIndex < 0 || odds > bestOdds || (odds == bestOdds && AnchorRank(t.Sub) > AnchorRank(visible[bestIndex].Sub)) ||
                        (odds == bestOdds && AnchorRank(t.Sub) == AnchorRank(visible[bestIndex].Sub) && t.Id < visible[bestIndex].Id)) { bestIndex = i; bestOdds = odds; }
                }
                if (bestIndex >= 0)
                {
                    SofTarget t = visible[bestIndex];
                    return new SofWatchPlan(SofWatchAction.Mission, SofWatchWhy.None, team.Slot, MissionKind.Sabotage, t.Id, TeamVerb.Cancel, t.X, t.Z, WatchCode.SofSabotage, ((int)t.Sub << 2) | (team.Slot & 3), bestOdds);
                }
            }
            // LASE or RECON: ground contacts only, ranked like SPACE ranks a MARK (value x weight x nearness to the front).
            int pickIndex = -1; float pickScore = float.NegativeInfinity;
            for (int i = 0; i < visible.Count; i++)
            {
                SofTarget t = visible[i];
                if (t.Kind != TargetKind.Ground || Backed(t.Id) || t.Front > NearFrontMeters || !Reachable(team, t)) continue;
                if (!world.TryGround(t, out float value, out WatchKind kind) || RevealedNear(visible, world, t, RiskRadiusMeters, false) > MaxRevealedNear) continue;
                float score = WatchOfficerPolicy.Score(new WatchTarget(t.Id, t.X, t.Z, value, kind, false, false, float.MaxValue, t.Front));
                if (score > pickScore || (score == pickScore && t.Id < visible[pickIndex].Id)) { pickIndex = i; pickScore = score; }
            }
            if (pickIndex < 0) return SofWatchPlan.Idle(SofWatchWhy.NoWork);
            SofTarget g = visible[pickIndex];
            int km = (int)Math.Min(255f, Math.Round(Math.Max(0f, g.Front) / 1000f));
            if (pickScore >= HighValueScore)
                return new SofWatchPlan(SofWatchAction.Mission, SofWatchWhy.None, team.Slot, MissionKind.Lase, g.Id, TeamVerb.Cancel, g.X, g.Z, WatchCode.SofLase, team.Slot, km);
            if (world.AllowRecon)
            {
                // RECON takes a point, not a unit: the team stops 1.9 km short of the contact, inside the 2 km reveal radius and just inside the exposure circle, instead of walking onto it.
                float px = g.X, pz = g.Z, d = SofRules.Distance(team.X, team.Z, g.X, g.Z);
                if (d > ReconStandOffMeters) { float k = (d - ReconStandOffMeters) / d; px = team.X + (g.X - team.X) * k; pz = team.Z + (g.Z - team.Z) * k; }
                if (!ReconRecent(px, pz, now))
                    return new SofWatchPlan(SofWatchAction.Mission, SofWatchWhy.None, team.Slot, MissionKind.Recon, g.Id, TeamVerb.Cancel, px, pz, WatchCode.SofRecon, team.Slot, km);
            }
            return SofWatchPlan.Idle(SofWatchWhy.NoWork);
        }

        /// <summary>
        /// Revealed ground contacts within <paramref name="radius"/> of the target (itself excluded), or only the armoured ones. It reads what the faction has really sighted and nothing else:
        /// the enemy units OVERLORD has not seen are discovered the hard way, by the team's exposure.
        /// </summary>
        private static int RevealedNear(IReadOnlyList<SofTarget> visible, ISofWatchWorld world, in SofTarget at, float radius, bool armouredOnly)
        {
            int n = 0;
            for (int i = 0; i < visible.Count; i++)
            {
                SofTarget o = visible[i];
                if (o.Id == at.Id || o.Kind != TargetKind.Ground || SofRules.Distance(o.X, o.Z, at.X, at.Z) > radius) continue;
                if (armouredOnly && !(world.TryGround(o, out _, out WatchKind kind) && kind == WatchKind.Armour)) continue;
                n++;
            }
            return n;
        }

        private static int AnchorRank(AnchorSub sub) => sub == AnchorSub.DataCenter ? 4 : sub == AnchorSub.EwTruck ? 3 : sub == AnchorSub.Uplink ? 2 : 1;

        private static bool Reachable(SofTeam team, in SofTarget t) =>
            SofRules.Distance(team.X, team.Z, t.X, t.Z) / SofRules.SpeedMetresPerSecond <= MaxEtaSeconds * SofRules.PushSpeedFactor;

        // ---- Steering --------------------------------------------------------------------------

        private SofWatchPlan SteerPlan(SofDesk desk, SofTeam team, float prior, float priorAt, WatchPacer pacer, bool aiFaction, float now)
        {
            bool rising = prior >= 0f && team.Exposure > prior + RisingEpsilon;
            int exposure = (int)Math.Round(team.Exposure);
            // Where the exposure will stand by the time OVERLORD may act again: its climb so far, over the think cadence plus the pacer's wait (30 s shared for an AI faction).
            float rate = prior >= 0f && now > priorAt ? (team.Exposure - prior) / (now - priorAt) : 0f;
            float projected = team.Exposure + Math.Max(0f, rate) * (ThinkSeconds + 1f + pacer.WaitSeconds(WatchDomain.Sof, aiFaction, now));
            // A team that would be pinned inside one full AI gap needs the shared limiter now, and so does one on its last 2.6 km to a target (the exposure circle starts 2 km out and can climb
            // several points a second): CYBER may not take the limiter from under it.
            bool moving = team.State == TeamState.Moving || team.State == TeamState.OnSite;
            bool approaching = team.State == TeamState.Moving && team.HasDest && SofRules.Distance(team.X, team.Z, team.DestX, team.DestZ) <= ApproachMeters;
            pacer.SetUrgent(WatchDomain.Sof, aiFaction && moving && (approaching || (team.Exposure >= UrgentFloor && rising && team.Exposure + rate * (ThinkSeconds + 1f + WatchPacer.AiGap(0)) >= ProjectedExfil)));
            // A team that can still reach its target, hold there and end under the ceiling at the exposure rate it is climbing now is not withdrawn at 40 %: a lightly defended target is reachable.
            // The projected-95 % safety below still withdraws it whenever the pin is about to land inside one pacing wait.
            bool committed = Committed(team, rate, now);
            if (team.State != TeamState.Returning && ((team.Exposure >= ExfilAt && rising && !committed) || (team.Exposure >= ProjectedFloor && projected >= ProjectedExfil)))
                return Exfil(team, WatchCode.SofExfil, exposure);
            if (team.HoldOn && team.Exposure <= ResumeBelow) return Verb(team, TeamVerb.Hold, WatchCode.SofResume, exposure);
            // Inside a defended circle a push costs more exposure per metre than walking (x1.5 rate for x1.5 speed against a fixed recovery): slow down while climbing, and recover on site.
            if (team.PushOn && rising && committed) return Verb(team, TeamVerb.Push, WatchCode.SofResume, exposure);
            if (team.State == TeamState.OnSite && !team.HoldOn && rising && committed && team.Exposure >= OnSiteHoldAt) return Verb(team, TeamVerb.Hold, WatchCode.SofHold, exposure);
            // A climbing exposure is about to be a withdrawal: the slot is kept for that rather than spent on slowing down.
            if (team.PushOn && !rising && team.Exposure >= PushStopAbove) return Verb(team, TeamVerb.Push, WatchCode.SofResume, exposure);
            // HOLD only lets exposure fall (twice as fast) when nothing is near: a rising exposure means enemies are in range and standing still would only pin the team, so that case withdrew above.
            if (team.State == TeamState.Moving && !team.HoldOn && !rising && team.Exposure >= HoldAt) return Verb(team, TeamVerb.Hold, WatchCode.SofHold, exposure);
            bool far = team.HasDest && SofRules.Distance(team.X, team.Z, team.DestX, team.DestZ) >= PushMinMeters;
            if ((team.State == TeamState.Moving || team.State == TeamState.Returning) && !team.PushOn && !team.HoldOn && team.Exposure <= PushBelow && far)
                return Verb(team, TeamVerb.Push, WatchCode.SofPush, exposure);
            return SofWatchPlan.Idle(SofWatchWhy.Waiting);
        }

        private static bool Committed(SofTeam team, float rate, float now)
        {
            if (team.Mission == MissionKind.None || (team.State != TeamState.Moving && team.State != TeamState.OnSite)) return false;
            float eta = team.State == TeamState.Moving && team.HasDest
                ? SofRules.Distance(team.X, team.Z, team.DestX, team.DestZ) / (SofRules.SpeedMetresPerSecond * (team.PushOn ? SofRules.PushSpeedFactor : 1f)) : 0f;
            // On site OVERLORD HOLDs (twice the recovery), so the exposure that matters is the one the team brings to the target, plus a margin for the reaction time.
            return team.Exposure + Math.Max(0f, rate) * eta + CommitMargin <= CommitCeiling;
        }

        private static SofWatchPlan Verb(SofTeam team, TeamVerb verb, WatchCode code, int exposure) =>
            new SofWatchPlan(SofWatchAction.Order, SofWatchWhy.None, team.Slot, MissionKind.None, 0, verb, 0f, 0f, code, team.Slot, exposure);

        private static SofWatchPlan Exfil(SofTeam team, WatchCode code, int b) => Verb(team, TeamVerb.Exfil, code, b);

        private float BaseDistance(SofDesk desk, SofTeam team)
        {
            float best = desk.Camps.NearestStanding(team.X, team.Z, out _, out _);
            for (int i = 0; i < desk.Held.Count; i++) best = Math.Min(best, SofRules.Distance(team.X, team.Z, desk.Held[i].X, desk.Held[i].Z));
            return float.IsInfinity(best) ? SofRules.Distance(team.X, team.Z, team.HomeX, team.HomeZ) : best;
        }

        // ---- Execute ---------------------------------------------------------------------------

        private SofWatchPlan Execute(SofDesk desk, in SofWatchPlan plan, WatchPacer pacer, bool aiFaction, float now)
        {
            SofResult r;
            switch (plan.Action)
            {
                case SofWatchAction.Raise: r = desk.Raise(Me); break;
                case SofWatchAction.Mission: r = desk.Send(Me, plan.Slot, plan.Mission, plan.Mission == MissionKind.Recon ? 0 : plan.TargetId, plan.X, plan.Z); break;
                default: r = desk.Order(Me, plan.Slot, plan.Verb); break;
            }
            if (!r.Ok)
            {
                Failures++;
                if (plan.Action == SofWatchAction.Mission) Back(plan.TargetId, now + FailBackoffSeconds);
                Defer(now, 5f);
                return new SofWatchPlan(SofWatchAction.None, SofWatchWhy.Failed, plan.Slot, plan.Mission, plan.TargetId, plan.Verb, plan.X, plan.Z, plan.Code, plan.A, plan.B, r.Outcome);
            }
            pacer.NoteAct(WatchDomain.Sof, aiFaction, now);
            switch (plan.Action)
            {
                case SofWatchAction.Raise: Raises++; break;
                case SofWatchAction.Mission:
                    Missions++;
                    Back(plan.TargetId, now + TargetBackoffSeconds);
                    if (plan.Mission == MissionKind.Sabotage) sabotageAt = now;
                    if (plan.Mission == MissionKind.Recon) NoteRecon(plan.X, plan.Z, now);
                    break;
                default: Orders++; break;
            }
            return new SofWatchPlan(plan.Action, SofWatchWhy.None, plan.Slot, plan.Mission, plan.TargetId, plan.Verb, plan.X, plan.Z, plan.Code, plan.A, plan.B, r.Outcome);
        }

        // ---- Memory ----------------------------------------------------------------------------

        private bool Backed(int id) => backoff.TryGetValue(id, out float until) && until > clock;

        private void Back(int id, float until)
        {
            if (id <= 0 || (!backoff.ContainsKey(id) && backoff.Count >= MaxBackoffs)) return;
            backoff[id] = until;
        }

        private void NoteRecon(float x, float z, float now)
        {
            reconX[reconHead] = x; reconZ[reconHead] = z; reconAt[reconHead] = now;
            reconHead = (reconHead + 1) % MaxRecon;
            if (reconCount < MaxRecon) reconCount++;
        }

        private bool ReconRecent(float x, float z, float now)
        {
            for (int i = 0; i < reconCount; i++)
                if (now - reconAt[i] < ReconRepeatSeconds && SofRules.Distance(x, z, reconX[i], reconZ[i]) < ReconSpacingMeters) return true;
            return false;
        }

        private void Prune(float now)
        {
            if (backoff.Count == 0) return;
            scratch.Clear();
            foreach (var pair in backoff) if (now >= pair.Value) scratch.Add(pair.Key);
            for (int i = 0; i < scratch.Count; i++) backoff.Remove(scratch[i]);
        }
    }
}
