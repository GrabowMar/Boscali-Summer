using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Cyber
{
    internal enum IntrusionPhase : byte { Idle = 0, Hopping = 1, Holding = 2, Ended = 3 }
    internal enum IntrusionEnd : byte { None = 0, Dropped = 1, Burned = 2, Traced = 3, Unpaid = 4, SourceDown = 5, NodeLost = 6, Scene = 7 }

    internal readonly struct HeldNode
    {
        public readonly int NodeId;
        public readonly NodeKind Kind;
        public readonly float Since;
        public HeldNode(int nodeId, NodeKind kind, float since) { NodeId = nodeId; Kind = kind; Since = since; }
    }

    /// <summary>What one <see cref="CyberIntrusion.Advance"/> did: a node arrived, the trace bar filled, how many 10 s upkeep ticks fell due.</summary>
    internal readonly struct IntrusionStep
    {
        public readonly int Arrived;
        public readonly bool Traced;
        public readonly int UpkeepTicks;
        public IntrusionStep(int arrived, bool traced, int upkeepTicks) { Arrived = arrived; Traced = traced; UpkeepTicks = upkeepTicks; }
    }

    /// <summary>Spec §1.3 numbers. Percent per second for trace; mission seconds for time; CR for money.</summary>
    internal static class CyberRules
    {
        public const int StartCost = 30, StartCostExploit = 23, UpkeepSeconds = 10, UpkeepPerNode = 2, MaxHeldPerFaction = 4, MaxHeldPerIntrusion = 4;
        public const float TraceMax = 100f, HeldTraceRate = 0.5f, DataCenterTraceFactor = 0.8f, DataCenterUpkeepFactor = 0.75f;
        public const float ExploitHopFactor = 0.75f, MaxAdvanceSeconds = 5f, MinTraceFactor = 0.25f, MaxTraceFactor = 3f;

        public static int IntrusionCap(int humans) => humans >= 5 ? 3 : 2;

        public static float HopSeconds(NodeKind kind, bool exploit)
        {
            float s = kind == NodeKind.Radar || kind == NodeKind.Relay ? 20f : kind == NodeKind.DataCenter ? 40f : 30f;
            return exploit ? s * ExploitHopFactor : s;
        }

        public static float HopTraceRate(NodeKind kind) => 1.5f;

        /// <summary>CYBER beats SPACE (the ring): a UPLINK node is an EXPLOIT target.</summary>
        public static bool Exploit(NodeKind kind) => kind == NodeKind.Uplink;

        public static int StartCostOf(bool exploit) => exploit ? StartCostExploit : StartCost;

        /// <summary>CR charged every <see cref="UpkeepSeconds"/> for <paramref name="held"/> nodes; a standing data center takes a quarter off.</summary>
        public static int Upkeep(int held, bool dataCenterUp)
        {
            if (held <= 0) return 0;
            double c = (double)UpkeepPerNode * held * (dataCenterUp ? DataCenterUpkeepFactor : 1.0);
            return (int)Math.Ceiling(c - 1e-9);
        }

        public static float ClampFactor(float f) =>
            float.IsNaN(f) || float.IsInfinity(f) ? 1f : Math.Max(MinTraceFactor, Math.Min(MaxTraceFactor, f));
    }

    /// <summary>One operator's intrusion: hop, hold, burn, drop, trace. Pure state; the desk decides who may do what.</summary>
    internal sealed class CyberIntrusion
    {
        private readonly List<HeldNode> held = new List<HeldNode>(CyberRules.MaxHeldPerIntrusion);
        private float last, upkeepClock;

        public CyberIntrusion(int id, ulong operatorId, int truckIndex, float now)
        {
            Id = id; Operator = operatorId; Truck = truckIndex; StartedAt = last = now;
        }

        public int Id { get; }
        public ulong Operator { get; }
        public int Truck { get; }
        public float StartedAt { get; }
        public float Trace { get; private set; }
        public int Hops { get; private set; }
        public IntrusionEnd EndReason { get; private set; }
        public int HopTarget { get; private set; }
        public NodeKind HopKind { get; private set; }
        public float HopStartedAt { get; private set; }
        public float HopEndsAt { get; private set; }
        public IReadOnlyList<HeldNode> Held => held;
        public int HeldCount => held.Count;
        public bool Ended => EndReason != IntrusionEnd.None;

        public IntrusionPhase Phase => Ended ? IntrusionPhase.Ended : HopTarget != 0 ? IntrusionPhase.Hopping : held.Count > 0 ? IntrusionPhase.Holding : IntrusionPhase.Idle;

        public bool Holds(int nodeId)
        {
            for (int i = 0; i < held.Count; i++) if (held[i].NodeId == nodeId) return true;
            return false;
        }

        public bool TryHeld(int nodeId, out HeldNode node)
        {
            for (int i = 0; i < held.Count; i++) if (held[i].NodeId == nodeId) { node = held[i]; return true; }
            node = default; return false;
        }

        /// <summary>0..1 progress of the running hop (0 when none).</summary>
        public float HopProgress(float now) =>
            HopTarget == 0 || HopEndsAt <= HopStartedAt ? 0f : Math.Max(0f, Math.Min(1f, (now - HopStartedAt) / (HopEndsAt - HopStartedAt)));

        public bool TryBeginHop(int nodeId, NodeKind kind, float seconds, float now)
        {
            if (Ended || nodeId <= 0 || HopTarget != 0 || held.Count >= CyberRules.MaxHeldPerIntrusion || Holds(nodeId) ||
                float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0f || float.IsNaN(now) || now < last) return false;
            HopTarget = nodeId; HopKind = kind; HopStartedAt = now; HopEndsAt = now + seconds;
            last = now;
            return true;
        }

        public void CancelHop() { HopTarget = 0; }

        /// <summary>Integrates trace and upkeep up to <paramref name="now"/> (at most 5 s per call, so a hitch cannot trace a pilot out). <paramref name="traceFactor"/> scales every trace gain.</summary>
        public IntrusionStep Advance(float now, float traceFactor)
        {
            if (Ended || float.IsNaN(now) || now <= last) return default;
            float factor = CyberRules.ClampFactor(traceFactor);
            float t = Math.Max(last, now - CyberRules.MaxAdvanceSeconds), end = now;
            int arrived = 0;
            float heldTime = 0f;
            if (HopTarget != 0)
            {
                float seg = Math.Min(end, HopEndsAt);
                if (seg > t)
                {
                    float rate = CyberRules.HopTraceRate(HopKind) + CyberRules.HeldTraceRate * held.Count;
                    if (held.Count > 0) heldTime += seg - t;
                    Trace += rate * factor * (seg - t);
                    t = seg;
                    if (Trace >= CyberRules.TraceMax) return Finish(IntrusionEnd.Traced, 0, true, heldTime, now);
                }
                if (end >= HopEndsAt)
                {
                    held.Add(new HeldNode(HopTarget, HopKind, HopEndsAt));
                    arrived = HopTarget; HopTarget = 0; Hops++;
                }
            }
            if (end > t && held.Count > 0)
            {
                Trace += CyberRules.HeldTraceRate * held.Count * factor * (end - t);
                heldTime += end - t;
                if (Trace >= CyberRules.TraceMax) return Finish(IntrusionEnd.Traced, 0, true, heldTime, now);
            }
            last = now;
            return new IntrusionStep(arrived, false, TakeUpkeep(heldTime));
        }

        private IntrusionStep Finish(IntrusionEnd why, int arrived, bool traced, float heldTime, float now)
        {
            Trace = Math.Min(Trace, CyberRules.TraceMax);
            EndReason = why; HopTarget = 0; held.Clear(); last = now;
            return new IntrusionStep(arrived, traced, 0);
        }

        private int TakeUpkeep(float heldTime)
        {
            upkeepClock += heldTime;
            int ticks = 0;
            while (upkeepClock >= CyberRules.UpkeepSeconds && ticks < 6) { upkeepClock -= CyberRules.UpkeepSeconds; ticks++; }
            if (upkeepClock > CyberRules.UpkeepSeconds) upkeepClock = CyberRules.UpkeepSeconds;
            return ticks;
        }

        /// <summary>Removes one held node. The intrusion ends when nothing is held and no hop runs.</summary>
        public bool Release(int nodeId, IntrusionEnd why)
        {
            for (int i = 0; i < held.Count; i++)
            {
                if (held[i].NodeId != nodeId) continue;
                held.RemoveAt(i);
                if (held.Count == 0 && HopTarget == 0) { EndReason = why; }
                return true;
            }
            return false;
        }

        /// <summary>Ends the whole intrusion (held nodes and hop).</summary>
        public void Finish(IntrusionEnd why)
        {
            if (Ended) return;
            EndReason = why; HopTarget = 0; held.Clear();
        }
    }
}
