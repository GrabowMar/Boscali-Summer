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
        OpPlan = 17, OpFund = 18,
        /// <summary>Defence (protocol 42).</summary>
        CyberPurge = 19, SofSweep = 20
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

        public int Add(WatchDomain domain, WatchCode code, int a, int b)
        {
            if (seq == int.MaxValue) return seq;
            rows.Add(new WatchLogRow { Seq = ++seq, Domain = domain, Code = code, A = (byte)Math.Clamp(a, 0, 255), B = (byte)Math.Clamp(b, 0, 255) });
            while (rows.Count > Capacity) rows.RemoveAt(0);
            return seq;
        }

        public int CopyTo(List<WatchLogRow> into)
        {
            for (int i = 0; i < rows.Count; i++) into.Add(rows[i]);
            return rows.Count;
        }

        public void Clear() { rows.Clear(); seq = 0; }
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
                case WatchCode.OpPlan: return "PLAN " + OpsWords.Short((OpKind)Math.Clamp(a, 0, 3)) + " — LEADING " + b + " % OF OBJECTIVES";
                case WatchCode.OpFund: return "FUND " + OpsWords.Short((OpKind)Math.Clamp(a, 0, 3)) + " — LEADING, TREASURY ABOUT " + (b * 10).ToString(CultureInfo.InvariantCulture) + " CR";
                case WatchCode.CyberPurge: return "PURGE — " + a + " ENEMY HOLD" + (a == 1 ? "" : "S") + " ON OUR NETWORK";
                case WatchCode.SofSweep: return "SWEEP — QRF AFTER " + a + " HOSTILE TEAM" + (a == 1 ? "" : "S") + " NEAR OUR SITES";
                default: return "";
            }
        }

        public static string Domain(WatchDomain domain) => domain == WatchDomain.Cyber ? "CYBER" : domain == WatchDomain.Sof ? "SOF" : "OPERATION";

        private static string Node(int kind) => kind >= 0 && kind <= (int)NodeKind.DataCenter ? CyberNetWords.Name((NodeKind)kind) : "NODE";

        private static string Team(int slot) => SofRules.Callsign(slot);

        private static string Anchor(int sub) => sub == (int)AnchorSub.Uplink ? "UPLINK" : sub == (int)AnchorSub.EwTruck ? "EW TRUCK" : sub == (int)AnchorSub.DataCenter ? "DATA CENTER" : "CAMP";
    }

    /// <summary>The think cadence every OVERLORD brain shares: when it next thinks, and the cheap poll a host adapter uses while nothing can happen. OVERLORD is always on: a human's live intrusion or team is respected asset by asset, not by silencing the domain.</summary>
    internal abstract class WatchBrainBase
    {
        protected float nextThinkAt;

        /// <summary>Do not think again for <paramref name="seconds"/> (the adapter's cheap poll while nothing can happen).</summary>
        public void Defer(float now, float seconds)
        {
            if (SpaceRules.MissionTime(now) && float.IsFinite(seconds) && seconds > 0f) nextThinkAt = Math.Max(nextThinkAt, now + seconds);
        }

        public virtual void Reset() { nextThinkAt = 0f; }
    }

    /// <summary>
    /// The rate bounds of OVERLORD for one faction. A faction with humans is bounded per domain (one action every 10 s in CYBER and one in SOF); a faction with no humans runs
    /// both domains through one limiter (one domain action every 30 s).
    /// </summary>
    internal sealed class WatchPacer
    {
        public const float DomainGapSeconds = 10f, AiGapSeconds = 30f;
        private readonly float[] last = { float.NegativeInfinity, float.NegativeInfinity };
        private float lastAi = float.NegativeInfinity;
        private readonly bool[] urgent = new bool[2];

        public static float AiGap() => AiGapSeconds;

        public bool CanAct(WatchDomain domain, bool aiFaction, float now)
        {
            if (!SpaceRules.MissionTime(now) || (int)domain > 1) return false;
            if (aiFaction && urgent[1 - (int)domain] && !urgent[(int)domain]) return false; // the shared limiter is kept free for the other domain's action that cannot wait
            return aiFaction ? now - lastAi >= AiGapSeconds : now - last[(int)domain] >= DomainGapSeconds;
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
        public float WaitSeconds(WatchDomain domain, bool aiFaction, float now)
        {
            if (!SpaceRules.MissionTime(now) || (int)domain > 1) return 0f;
            float since = now - (aiFaction ? lastAi : last[(int)domain]);
            return Math.Max(0f, (aiFaction ? AiGapSeconds : DomainGapSeconds) - since);
        }

        public void Reset() { last[0] = last[1] = lastAi = float.NegativeInfinity; urgent[0] = urgent[1] = false; }
    }
}
