using System;
using System.Globalization;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Domain.Cyber
{

    /// <summary>The words and tones of the NET page. Pure so every string is testable and none is ever built from hidden state.</summary>
    internal static class CyberNetWords
    {
        public static string Code(NodeKind kind) =>
            kind == NodeKind.Radar ? "RDR" : kind == NodeKind.SamC2 ? "SAM" : kind == NodeKind.Relay ? "RLY" : kind == NodeKind.Uplink ? "UPL" : "DC";

        public static string Name(NodeKind kind) =>
            kind == NodeKind.Radar ? "RADAR" : kind == NodeKind.SamC2 ? "SAM C2" : kind == NodeKind.Relay ? "RELAY" : kind == NodeKind.Uplink ? "UPLINK" : "DATA CENTER";

        public static string Node(NodeKind kind, int id) => Name(kind) + " #" + id.ToString(CultureInfo.InvariantCulture);

        /// <summary>0 ready (green), 1 caution (amber), 2 danger (red): the trace bar and its number.</summary>
        public static int TraceTone(int trace) => trace >= 80 ? 2 : trace >= 50 ? 1 : 0;

        public static string ReasonWord(IntrusionEnd why)
        {
            switch (why)
            {
                case IntrusionEnd.Burned: return "BURNED";
                case IntrusionEnd.Dropped: return "DROPPED";
                case IntrusionEnd.Traced: return "TRACED";
                case IntrusionEnd.Unpaid: return "UPKEEP UNPAID";
                case IntrusionEnd.SourceDown: return "EW TRUCK DOWN";
                case IntrusionEnd.NodeLost: return "NODE DESTROYED";
                case IntrusionEnd.Purged: return "PURGED BY THE ENEMY";
                default: return "ENDED";
            }
        }

        /// <summary>The hop box line: <c>HOPPING · SAM C2 #12 · 14 S</c>, <c>HOLDING 2 NODES</c>, <c>IDLE</c>.</summary>
        public static string IntrusionLine(in CyberIntrusionRow x, CyberStateData s, float now)
        {
            if (x.Phase == IntrusionPhase.Hopping)
            {
                NodeKind kind = NodeKind.Radar;
                foreach (CyberNodeRow n in s.Nodes) if (n.Id == x.HopTarget) { kind = n.Kind; break; }
                float left = Math.Max(0f, x.HopEndsAt - now);
                return "HOPPING · " + Node(kind, x.HopTarget) + " · " + (int)Math.Ceiling(left) + " S";
            }
            if (x.Phase == IntrusionPhase.Holding) return "HOLDING " + x.Held.Length + " NODE" + (x.Held.Length == 1 ? "" : "S");
            return x.Phase == IntrusionPhase.Ended ? "ENDED" : "IDLE";
        }

        /// <summary>0..1 hop progress for the progress bar; 0 when not hopping.</summary>
        public static float HopProgress(in CyberIntrusionRow x, float now)
        {
            if (x.Phase != IntrusionPhase.Hopping || x.HopSeconds == 0) return 0f;
            float left = x.HopEndsAt - now;
            return Math.Clamp(1f - left / x.HopSeconds, 0f, 1f);
        }
    }

    internal enum CyberNoticeKind : byte { None, Held, Traced }

    /// <summary>
    /// The pilot's CYBER HUD notice, derived only from the faction mirror: the operator's own NODE HELD and TRACED events, one line each, at most
    /// one every 3 s, silent on first sight of a mirror (a join never replays history). QUIET mode drops them.
    /// </summary>
    internal sealed class CyberNoticeTracker : NoticeTrackerBase
    {
        private int lastSeq = -1;

        public void Reset() { lastSeq = -1; ResetGate(); }

        public CyberNoticeKind Observe(bool known, CyberStateData state, float now, bool quiet)
        {
            if (!known || state == null) { Reset(); return CyberNoticeKind.None; }
            int newest = 0;
            foreach (CyberEventRow e in state.Events) newest = Math.Max(newest, e.Seq);
            if (lastSeq < 0) { lastSeq = newest; return CyberNoticeKind.None; }
            CyberNoticeKind found = CyberNoticeKind.None;
            foreach (CyberEventRow e in state.Events)
            {
                if (e.Seq <= lastSeq || !e.Own) continue;
                if (e.Kind == CyberEventKind.Traced) found = CyberNoticeKind.Traced;
                else if (e.Kind == CyberEventKind.NodeHeld && found != CyberNoticeKind.Traced) found = CyberNoticeKind.Held;
            }
            lastSeq = Math.Max(lastSeq, newest);
            return Admit(found != CyberNoticeKind.None, now, quiet) ? found : CyberNoticeKind.None;
        }
    }
}
