using System;
using System.Collections.Generic;
using System.Globalization;

namespace BoscaliSummer.Modules.Support.Domain.Cyber
{
    /// <summary>One point on the intrusion map in screen pixels (y grows downward), or hidden.</summary>
    internal readonly struct MapPoint
    {
        public readonly float X, Y;
        public MapPoint(float x, float y) { X = x; Y = y; }
    }

    /// <summary>
    /// Fits the world points the NET page shows (trucks, data centers, visible nodes) into the map box: uniform scale, north up, padded, never
    /// smaller than a 20 km window so one lonely truck is not blown up into a continent. Pure; the page only draws what this returns.
    /// </summary>
    internal sealed class CyberMapProjection
    {
        public const float MinSpan = 20000f, Padding = 0.12f;
        private float minX, minZ, scale = 1f, offX, offY, height;

        public float Scale => scale;

        /// <summary>Metres to pixels for a world distance (a reach circle's radius).</summary>
        public float Pixels(float metres) => metres * scale;

        public void Fit(IReadOnlyList<MapPoint> worldXZ, float widthPx, float heightPx)
        {
            height = heightPx;
            float loX = float.MaxValue, hiX = float.MinValue, loZ = float.MaxValue, hiZ = float.MinValue;
            for (int i = 0; worldXZ != null && i < worldXZ.Count; i++)
            {
                MapPoint p = worldXZ[i];
                if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsInfinity(p.X) || float.IsInfinity(p.Y)) continue;
                loX = Math.Min(loX, p.X); hiX = Math.Max(hiX, p.X); loZ = Math.Min(loZ, p.Y); hiZ = Math.Max(hiZ, p.Y);
            }
            if (loX > hiX) { loX = -MinSpan * 0.5f; hiX = MinSpan * 0.5f; loZ = -MinSpan * 0.5f; hiZ = MinSpan * 0.5f; }
            float spanX = Math.Max(MinSpan, (hiX - loX) * (1f + 2f * Padding)), spanZ = Math.Max(MinSpan, (hiZ - loZ) * (1f + 2f * Padding));
            scale = Math.Max(1e-6f, Math.Min(widthPx / spanX, heightPx / spanZ));
            float cx = (loX + hiX) * 0.5f, cz = (loZ + hiZ) * 0.5f;
            minX = cx - widthPx * 0.5f / scale; minZ = cz - heightPx * 0.5f / scale;
            offX = 0f; offY = 0f;
        }

        public MapPoint ToScreen(float x, float z) => new MapPoint(offX + (x - minX) * scale, offY + height - (z - minZ) * scale);
    }

    /// <summary>The words and tones of the NET page. Pure so every string is testable and none is ever built from hidden state.</summary>
    internal static class CyberNetWords
    {
        public static string Code(NodeKind kind) =>
            kind == NodeKind.Radar ? "RDR" : kind == NodeKind.SamC2 ? "SAM" : kind == NodeKind.Relay ? "RLY" : kind == NodeKind.Uplink ? "UPL" : "DC";

        public static string Name(NodeKind kind) =>
            kind == NodeKind.Radar ? "RADAR" : kind == NodeKind.SamC2 ? "SAM C2" : kind == NodeKind.Relay ? "RELAY" : kind == NodeKind.Uplink ? "UPLINK" : "DATA CENTER";

        public static string Node(NodeKind kind, int id) => Name(kind) + " #" + id.ToString(CultureInfo.InvariantCulture);

        public static string Clock(float seconds)
        {
            if (float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f) seconds = 0f;
            int s = (int)Math.Ceiling(seconds);
            return (s / 60).ToString(CultureInfo.InvariantCulture) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        /// <summary>0 ready (green), 1 caution (amber), 2 danger (red): the trace bar and its number.</summary>
        public static int TraceTone(int trace) => trace >= 80 ? 2 : trace >= 50 ? 1 : 0;

        public static string AnchorLine(in CyberAnchorRow a, int ordinal, float now)
        {
            string name = (a.Kind == AnchorKind.EwTruck ? "EW TRUCK " : "DATA CENTER ") + (ordinal + 1);
            if (a.Health == AnchorHealth.Down) return name + " · DOWN · RESTORE " + a.Rebuild + " %";
            string state = a.Health == AnchorHealth.Damaged ? "DAMAGED" : "LIVE";
            if (a.Kind == AnchorKind.EwTruck)
            {
                float reach = AnchorRules.Reach(a.Health) / 1000f;
                string locked = a.LockUntil > now ? " · TRACED " + Clock(a.LockUntil - now) : "";
                return name + " · " + state + " · REACH " + reach.ToString("0", CultureInfo.InvariantCulture) + " KM" + locked;
            }
            return name + " · " + state + (a.Health == AnchorHealth.Live ? " · TRACE -20 %, UPKEEP -25 %" : "");
        }

        /// <summary>The header sub-line: what stands and what is held.</summary>
        public static string Sub(CyberStateData s)
        {
            if (s == null || !s.Active) return "NO EW ASSETS ONLINE";
            int trucks = 0, centers = 0;
            foreach (CyberAnchorRow a in s.Anchors)
                if (a.Health != AnchorHealth.Down) { if (a.Kind == AnchorKind.EwTruck) trucks++; else centers++; }
            int held = 0;
            foreach (CyberNodeRow n in s.Nodes) if (n.Held) held++;
            return trucks + " TRUCK" + (trucks == 1 ? "" : "S") + " · " + centers + " DATA CENTER" + (centers == 1 ? "" : "S") + " · " + s.Nodes.Count + " NODES · " + held + " HELD";
        }

        public static string EventLine(in CyberEventRow e, float now)
        {
            string node = e.Kind == CyberEventKind.Traced ? "" : Node(e.Node, e.NodeId);
            switch (e.Kind)
            {
                case CyberEventKind.HopStarted: return "HOP STARTED · " + node;
                case CyberEventKind.NodeHeld: return "NODE HELD · " + node;
                case CyberEventKind.Released:
                    return "NODE RELEASED · " + node + " · " + ReasonWord(e.Reason);
                default: return "INTRUSION TRACED · EW TRUCK REVEALED FOR 2:00";
            }
        }

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
            return Math.Max(0f, Math.Min(1f, 1f - left / x.HopSeconds));
        }
    }

    internal enum CyberNoticeKind : byte { None, Held, Traced }

    /// <summary>
    /// The pilot's CYBER HUD notice, derived only from the faction mirror: the operator's own NODE HELD and TRACED events, one line each, at most
    /// one every 3 s, silent on first sight of a mirror (a join never replays history). QUIET mode drops them.
    /// </summary>
    internal sealed class CyberNoticeTracker
    {
        public const float GapSeconds = 3f;
        private int lastSeq = -1;
        private float nextAt;

        public void Reset() { lastSeq = -1; nextAt = 0f; }

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
            if (quiet || found == CyberNoticeKind.None || now < nextAt) return CyberNoticeKind.None;
            nextAt = now + GapSeconds;
            return found;
        }
    }
}
