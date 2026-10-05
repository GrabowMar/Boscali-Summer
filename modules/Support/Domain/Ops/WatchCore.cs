using System;
using System.Collections.Generic;
using System.Globalization;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Ops
{
    /// <summary>Which desk an OVERLORD action belongs to. Wire value (2 bits); never renumber.</summary>
    internal enum WatchDomain : byte { Cyber = 0, Sof = 1, Ops = 2 }

    /// <summary>
    /// One reason per kind of action. Wire value (6 bits on a log row); never renumber. The two argument bytes of a row (<see cref="WatchLogRow.A"/>, <see cref="WatchLogRow.B"/>)
    /// fill the blanks of <see cref="WatchWords.Reason"/>, so the words are built from a few bytes and never travel as free text.
    /// </summary>
    internal enum WatchCode : byte
    {
        None = 0,
        CyberHop = 1, CyberDeeper = 2, CyberBurnTrace = 3, CyberBurnPilots = 4, CyberDropTrace = 5, CyberDropYield = 6, CyberBurnYield = 7,
        SofRaise = 8, SofLase = 9, SofRecon = 10, SofSabotage = 11, SofPush = 12, SofHold = 13, SofResume = 14, SofExfil = 15, SofExfilDone = 16,
        OpPlan = 17, OpFund = 18
    }

    /// <summary>One OVERLORD action as its own faction's console reads it: a sequence number, the desk, the reason code and two small arguments.</summary>
    internal struct WatchLogRow
    {
        public int Seq;
        public WatchDomain Domain;
        public WatchCode Code;
        public byte A, B;
    }

    /// <summary>The last three OVERLORD actions of one faction, newest last, each with a faction-wide sequence number. Host side; the mirror carries it to the faction only.</summary>
    internal sealed class WatchLogRing
    {
        public const int Capacity = 3;
        private readonly List<WatchLogRow> rows = new List<WatchLogRow>(Capacity);
        private int seq;

        public int Newest => seq;

        public int Add(WatchDomain domain, WatchCode code, int a, int b)
        {
            if (seq == int.MaxValue) return seq;
            rows.Add(new WatchLogRow { Seq = ++seq, Domain = domain, Code = code, A = Clamp(a), B = Clamp(b) });
            while (rows.Count > Capacity) rows.RemoveAt(0);
            return seq;
        }

        public int CopyTo(List<WatchLogRow> into)
        {
            for (int i = 0; i < rows.Count; i++) into.Add(rows[i]);
            return rows.Count;
        }

        public void Clear() { rows.Clear(); seq = 0; }

        private static byte Clamp(int v) => (byte)Math.Max(0, Math.Min(255, v));
    }

    /// <summary>The words of an OVERLORD action, one function for the host log, the sim transcripts and the faction console. Pure.</summary>
    internal static class WatchWords
    {
        public static string Reason(WatchCode code, int a, int b)
        {
            switch (code)
            {
                case WatchCode.CyberHop: return "HOP " + Node(a) + " — MOST VALUABLE REACHABLE NODE";
                case WatchCode.CyberDeeper: return "HOP DEEPER TO " + Node(a) + " — TRACE " + b + " %";
                case WatchCode.CyberBurnTrace: return "BURN " + Node(a) + " — TRACE " + b + " %";
                case WatchCode.CyberBurnPilots: return "BURN " + Node(a) + " — PILOTS " + b + " KM FROM THE NODE";
                case WatchCode.CyberDropTrace: return "DROP — TRACE " + b + " %, LEAVING BEFORE TRACED";
                case WatchCode.CyberDropYield: return "DROP — OPERATOR TAKING OVER";
                case WatchCode.CyberBurnYield: return "BURN " + Node(a) + " — OPERATOR TAKING OVER, PACKAGE KEPT";
                case WatchCode.SofRaise: return "RAISE TEAM — CAMP LIVE, NO TEAM IN THE FIELD";
                case WatchCode.SofLase: return "LASE — " + Team(a) + " ON A HIGH-VALUE CONTACT " + b + " KM FROM THE FRONT";
                case WatchCode.SofRecon: return "RECON — " + Team(a) + " FROM STAND-OFF, A CONTACT " + b + " KM FROM THE FRONT";
                case WatchCode.SofSabotage: return "SABOTAGE " + Anchor(a >> 2) + " — " + Team(a & 3) + ", ODDS " + b + " %";
                case WatchCode.SofPush: return "PUSH " + Team(a) + " — EXPOSURE " + b + " %, LONG WAY TO GO";
                case WatchCode.SofHold: return "HOLD " + Team(a) + " — EXPOSURE " + b + " %, LETTING IT FALL";
                case WatchCode.SofResume: return "RESUME " + Team(a) + " — EXPOSURE " + b + " %";
                case WatchCode.SofExfil: return "EXFIL " + Team(a) + " — EXPOSURE " + b + " % AND CLIMBING";
                case WatchCode.SofExfilDone: return "EXFIL " + Team(a) + " — NOTHING LEFT TO DO, AMMO " + b + " %";
                case WatchCode.OpPlan: return "PLAN " + OpsWords.Short((OpKind)Math.Max(0, Math.Min(3, a))) + " — LEADING " + b + " % OF OBJECTIVES";
                case WatchCode.OpFund: return "FUND " + OpsWords.Short((OpKind)Math.Max(0, Math.Min(3, a))) + " — LEADING, TREASURY ABOUT " + (b * 10).ToString(CultureInfo.InvariantCulture) + " CR";
                default: return "";
            }
        }

        /// <summary>The faction console line: <c>OVERLORD · &lt;reason&gt;</c>.</summary>
        public static string Line(in WatchLogRow row) => row.Code == WatchCode.None ? "" : "OVERLORD · " + Reason(row.Code, row.A, row.B);

        public static string Domain(WatchDomain domain) => domain == WatchDomain.Cyber ? "CYBER" : domain == WatchDomain.Sof ? "SOF" : "OPERATION";

        private static string Node(int kind) => kind >= 0 && kind <= (int)NodeKind.DataCenter ? CyberNetWords.Name((NodeKind)kind) : "NODE";

        private static string Team(int slot) => SofRules.Callsign(slot);

        private static string Anchor(int sub) => sub == (int)AnchorSub.Uplink ? "UPLINK" : sub == (int)AnchorSub.EwTruck ? "EW TRUCK" : sub == (int)AnchorSub.DataCenter ? "DATA CENTER" : "CAMP";
    }

    /// <summary>
    /// The idle rule of every domain OVERLORD staffs (core 2b, the same numbers as SPACE): a lone human holds a domain for 60 s after a domain verb, two or more for 300 s, and a
    /// faction with no humans has none. Only domain verbs count: firing a CALL, claiming a package or funding an operation never calls it.
    /// </summary>
    internal sealed class WatchIdle
    {
        private float lastHumanAt = float.NegativeInfinity;

        public void RecordHuman(float now)
        {
            if (SpaceRules.MissionTime(now) && now > lastHumanAt) lastHumanAt = now;
        }

        public bool Idle(int humans, float now)
        {
            if (humans <= 0) return true;
            return now - lastHumanAt >= (humans == 1 ? WatchOfficerPolicy.SoloIdleSeconds : WatchOfficerPolicy.GroupIdleSeconds);
        }

        public void Reset() { lastHumanAt = float.NegativeInfinity; }
    }

    /// <summary>
    /// The rate bounds of OVERLORD for one faction. A faction with humans is bounded per domain (one action every 10 s in CYBER and one in SOF); a faction with no humans runs
    /// both domains through one limiter (one domain action every 30 s, campaign: 30 s x (1 + 0.15 x players)) and its operation funding is one tap or plan every 60 s.
    /// </summary>
    internal sealed class WatchPacer
    {
        public const float DomainGapSeconds = 10f, AiGapSeconds = 30f, FundGapSeconds = 60f, CampaignPlayerFactor = 0.15f;
        private readonly float[] last = { float.NegativeInfinity, float.NegativeInfinity };
        private float lastAi = float.NegativeInfinity, lastFund = float.NegativeInfinity;
        private readonly bool[] urgent = new bool[2];

        public static float AiGap(int campaignPlayers) => AiGapSeconds * (1f + CampaignPlayerFactor * Math.Max(0, campaignPlayers));

        /// <summary>The gap that applies to this faction right now (what a projection must assume the next action waits for).</summary>
        public static float Gap(bool aiFaction, int campaignPlayers = 0) => aiFaction ? AiGap(campaignPlayers) : DomainGapSeconds;

        public bool CanAct(WatchDomain domain, bool aiFaction, float now, int campaignPlayers = 0)
        {
            if (!SpaceRules.MissionTime(now) || (int)domain > 1) return false;
            if (aiFaction && urgent[1 - (int)domain] && !urgent[(int)domain]) return false; // the shared limiter is kept free for the other domain's action that cannot wait
            return aiFaction ? now - lastAi >= AiGap(campaignPlayers) : now - last[(int)domain] >= DomainGapSeconds;
        }

        /// <summary>
        /// A domain's next action cannot wait a whole AI gap (a trace about to fill, a team about to be pinned): while this is set the other domain may not take the shared limiter, unless it is urgent
        /// itself. Only an AI faction shares a limiter; a faction with humans has one per domain, so it never needs this.
        /// </summary>
        public void SetUrgent(WatchDomain domain, bool value) { if ((int)domain <= 1) urgent[(int)domain] = value; }

        public void NoteAct(WatchDomain domain, bool aiFaction, float now)
        {
            if (!SpaceRules.MissionTime(now) || (int)domain > 1) return;
            if (aiFaction) lastAi = now; else last[(int)domain] = now;
        }

        /// <summary>Seconds until this faction may act again in the domain (0 when it may act now): what a trace projection must assume OVERLORD waits before it can drop.</summary>
        public float WaitSeconds(WatchDomain domain, bool aiFaction, float now, int campaignPlayers = 0)
        {
            if (!SpaceRules.MissionTime(now) || (int)domain > 1) return 0f;
            float since = now - (aiFaction ? lastAi : last[(int)domain]);
            return Math.Max(0f, (aiFaction ? AiGap(campaignPlayers) : DomainGapSeconds) - since);
        }

        public bool CanFund(float now) => SpaceRules.MissionTime(now) && now - lastFund >= FundGapSeconds;

        public void NoteFund(float now) { if (SpaceRules.MissionTime(now)) lastFund = now; }

        public void Reset() { last[0] = last[1] = lastAi = lastFund = float.NegativeInfinity; urgent[0] = urgent[1] = false; }
    }

    /// <summary>One operation slot as the AI doctrine reads it.</summary>
    internal readonly struct AiSlotView
    {
        public readonly OpKind Kind;
        public readonly OpState State;
        public AiSlotView(OpKind kind, OpState state) { Kind = kind; State = state; }
        /// <summary>The bar still takes CR: funding, stalled or broken.</summary>
        public bool Open => Kind != OpKind.None && (State == OpState.Funding || State == OpState.NeedsFunding || State == OpState.Broken);
        public bool Free => Kind == OpKind.None || State == OpState.Idle;
    }

    internal struct AiOpsFacts
    {
        /// <summary>Held objectives over objectives (0..1); NaN when the map has none to count.</summary>
        public float Share, RivalShare;
        public float Treasury;
        public bool CyberOnline, SofOnline, DataCenterUp;
        /// <summary>A SAM C2 node of the faction's own CYBER view, and a building its own SOF holds (0 when none).</summary>
        public int SamNodeId, HeldBuildingId;
        public AiSlotView Cyber, Sof;
        /// <summary>How many ASAT strikes this faction has planned already (rotates the satellite class).</summary>
        public int AsatsPlanned;
    }

    internal enum AiOpsAction : byte { None = 0, Plan = 1, Fund = 2 }

    internal enum AiOpsWhy : byte { None, NotLeading, Poor, NoOperation, Waiting }

    internal readonly struct AiOpsPlan
    {
        public readonly AiOpsAction Action;
        public readonly AiOpsWhy Why;
        public readonly OpKind Kind;
        public readonly OpDomain Domain;
        public readonly int Target;
        public readonly bool Large;
        public readonly WatchCode Code;
        public readonly int A, B;

        public AiOpsPlan(AiOpsAction action, AiOpsWhy why, OpKind kind = OpKind.None, OpDomain domain = OpDomain.Cyber, int target = 0, bool large = false,
            WatchCode code = WatchCode.None, int a = 0, int b = 0)
        { Action = action; Why = why; Kind = kind; Domain = domain; Target = target; Large = large; Code = code; A = a; B = b; }

        public string Reason => WatchWords.Reason(Code, A, B);
    }

    /// <summary>
    /// The doctrine of an AI-controlled faction's operations (core 7a: "leading and treasury over 500 means run a project"). It leads when it holds more than half of the objectives
    /// and more than any rival; it spends only while its treasury stays above 500 CR; it plans from what its own CYBER and SOF can legally see. Pure.
    /// </summary>
    internal static class AiRules
    {
        public const float LeadShare = 0.5f, TreasuryFloor = 500f, SeedPerMinute = 40f, TreasuryCap = 1500f;
        private static readonly int[] AsatClasses = { 1, 0, 2 };

        public static bool Leads(float share, float bestRival) =>
            !float.IsNaN(share) && !float.IsInfinity(share) && share > LeadShare && (float.IsNaN(bestRival) || share > bestRival);

        /// <summary>The flat seed an AI-controlled faction's treasury earns over <paramref name="seconds"/> (core 6.4: it has no humans to feed HQ FUND): 40 CR a minute, never past the cap.</summary>
        public static float SeedFor(float treasury, float seconds)
        {
            if (float.IsNaN(treasury) || float.IsNaN(seconds) || seconds <= 0f) return 0f;
            return Math.Max(0f, Math.Min(SeedPerMinute / 60f * Math.Min(seconds, 60f), TreasuryCap - Math.Max(0f, treasury)));
        }

        public static bool CanFund(float treasury) => !float.IsNaN(treasury) && treasury > TreasuryFloor;

        /// <summary>A tap of 50 CR when the treasury would still stand above the floor, else the 25 CR tap.</summary>
        public static bool LargeTap(float treasury) => treasury - OpsRules.FundLarge > TreasuryFloor;

        /// <summary>The satellite class an ASAT aims at: radar first, then optical, then kinetic. It reads no enemy state: a strike on a class that is dead fizzles.</summary>
        public static int AsatClass(int planned) => AsatClasses[Math.Max(0, planned) % AsatClasses.Length];

        public static AiOpsPlan Decide(in AiOpsFacts f)
        {
            if (!Leads(f.Share, f.RivalShare)) return new AiOpsPlan(AiOpsAction.None, AiOpsWhy.NotLeading);
            if (!CanFund(f.Treasury)) return new AiOpsPlan(AiOpsAction.None, AiOpsWhy.Poor);
            int pct = (int)Math.Round(Math.Max(0f, Math.Min(1f, f.Share)) * 100f);
            int treasury = (int)Math.Min(255f, Math.Round(f.Treasury / 10f));
            if (f.CyberOnline && f.Cyber.Open) return Fund(f.Cyber.Kind, OpDomain.Cyber, f.Treasury, treasury);
            if (f.SofOnline && f.Sof.Open) return Fund(f.Sof.Kind, OpDomain.Sof, f.Treasury, treasury);
            if (f.CyberOnline && f.DataCenterUp && f.Cyber.Free)
            {
                if (f.SamNodeId > 0) return new AiOpsPlan(AiOpsAction.Plan, AiOpsWhy.None, OpKind.ZeroDay, OpDomain.Cyber, f.SamNodeId, false, WatchCode.OpPlan, (int)OpKind.ZeroDay, pct);
                return new AiOpsPlan(AiOpsAction.Plan, AiOpsWhy.None, OpKind.Asat, OpDomain.Cyber, AsatClass(f.AsatsPlanned), false, WatchCode.OpPlan, (int)OpKind.Asat, pct);
            }
            if (f.SofOnline && f.Sof.Free && f.HeldBuildingId > 0)
                return new AiOpsPlan(AiOpsAction.Plan, AiOpsWhy.None, OpKind.Fob, OpDomain.Sof, f.HeldBuildingId, false, WatchCode.OpPlan, (int)OpKind.Fob, pct);
            return new AiOpsPlan(AiOpsAction.None, f.Cyber.Free || f.Sof.Free ? AiOpsWhy.NoOperation : AiOpsWhy.Waiting);
        }

        private static AiOpsPlan Fund(OpKind kind, OpDomain domain, float treasury, int treasuryTens) =>
            new AiOpsPlan(AiOpsAction.Fund, AiOpsWhy.None, kind, domain, 0, LargeTap(treasury), WatchCode.OpFund, (int)kind, treasuryTens);
    }
}
