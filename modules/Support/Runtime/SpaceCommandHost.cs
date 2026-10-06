using System.Collections.Generic;
using BoscaliSummer.Modules.Support.Domain.Cyber;
using BoscaliSummer.Modules.Support.Domain.Ops;
using BoscaliSummer.Modules.Support.Domain.Sof;
using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Modules.Support.Runtime
{
    /// <summary>
    /// The one evaluator every SPACE command goes through (remote sender, listen-host and singleplayer alike): rate limit, exact
    /// replay, changed-payload refusal, then the host decision. The sender's identity and faction come from the transport.
    /// </summary>
    internal sealed class SpaceCommandHost
    {
        /// <summary>SPACE request ids live in their own channel: they can never collide with CALLS request ids.</summary>
        public const int Channel = 2;
        /// <summary>A queued claim older than this (mission seconds) is settled from the desk's own receipt or dropped, so pinned entries can never refuse forever.</summary>
        public const float PendingSeconds = 60f;

        private readonly byte protocol;
        private readonly SpaceNetHost ports;
        private readonly SpaceCommandLimiter limiter = new SpaceCommandLimiter();
        private readonly SpaceReplayCache cache = new SpaceReplayCache();
        private readonly List<int> stale = new List<int>(SpaceReplayCache.PerPlayer);

        public SpaceCommandHost(byte protocol, SpaceNetHost ports) { this.protocol = protocol; this.ports = ports; }
        public SpaceReplayCache Cache => cache;

        /// <summary>Admission for the commands that carry no verdict (open, close, activity). False: drop silently.</summary>
        public bool Admit(ulong player, float wall) => limiter.Admit(player, wall);

        public void ResetForScene() { limiter.Clear(); cache.Clear(); }

        /// <summary>Runs a MARK, SEND or CLAIM. True when <paramref name="reply"/> must be sent back to the requester.</summary>
        public bool Handle(ulong player, in SpaceCommand command, int epoch, float wall, out SpaceReply reply)
        {
            reply = default;
            if (player == 0 || command.Protocol != protocol || !command.Mutating || command.RequestId <= 0) return false;
            if (!limiter.Admit(player, wall)) { reply = Refusal(command, RefusalKind.Limited); return true; }
            switch (cache.Find(player, epoch, Channel, command, out SpaceReply known))
            {
                case SpaceReplayCache.Lookup.Mismatch: reply = Refusal(command, RefusalKind.Changed); return true;
                case SpaceReplayCache.Lookup.Replay: reply = known.AsReplay(); return true;
                case SpaceReplayCache.Lookup.Pending:
                    // A queued claim: report the final verdict as soon as the host has one, else the pending receipt again.
                    if (ports.TryTaskedResult(player, command.RequestId, out TaskedResult now) && now.Final)
                    {
                        SpaceReply final = ForTasked(command.Kind, command.RequestId, now);
                        cache.Resolve(player, epoch, Channel, command.RequestId, final);
                        reply = final.AsReplay();
                    }
                    else reply = known.AsReplay();
                    return true;
            }
            if (!cache.Begin(player, epoch, Channel, command, ports.Now) && !(SettleStale(player, epoch) && cache.Begin(player, epoch, Channel, command, ports.Now)))
            {
                reply = Refusal(command, RefusalKind.Busy);
                return true;
            }
            try
            {
                reply = Execute(player, command);
                cache.Complete(player, epoch, Channel, command.RequestId, reply, reply.Pending);
            }
            catch
            {
                cache.Abort(player, epoch, Channel, command.RequestId);
                throw;
            }
            return true;
        }

        /// <summary>Old pending claims fall back to the desk: a final receipt completes the entry, none at all drops it.</summary>
        private bool SettleStale(ulong player, int epoch)
        {
            if (cache.StalePending(player, ports.Now, PendingSeconds, stale) == 0) return false;
            for (int i = 0; i < stale.Count; i++)
            {
                int request = stale[i];
                if (ports.TryTaskedResult(player, request, out TaskedResult result) && result.Final)
                    cache.Resolve(player, epoch, Channel, request, ForTasked(SpaceCommandKind.ClaimTasked, request, result));
                else
                    cache.Abort(player, epoch, Channel, request);
            }
            return true;
        }

        /// <summary>The reply for a queued claim that has just resolved (the host push). Records it for later replays.</summary>
        public SpaceReply Resolved(ulong player, int epoch, int requestId, in TaskedResult result)
        {
            SpaceReply final = ForTasked(SpaceCommandKind.ClaimTasked, requestId, result);
            cache.Resolve(player, epoch, Channel, requestId, final);
            return final;
        }

        private SpaceReply Execute(ulong player, in SpaceCommand c)
        {
            switch (c.Kind)
            {
                case SpaceCommandKind.Mark:
                    MarkVerdict verdict = ports.Mark(player, c.Target); // every id, valid or not, takes the same path
                    if (verdict == MarkVerdict.Capacity) verdict = MarkVerdict.NoContact; // a full table must not reveal an admitted id
                    return new SpaceReply(protocol, c.Kind, c.RequestId, (byte)verdict);
                case SpaceCommandKind.SendTasked:
                    return ForTasked(c.Kind, c.RequestId, ports.Send(player, c.RequestId, c.Ids));
                case SpaceCommandKind.CyberHop:
                case SpaceCommandKind.CyberBurn:
                case SpaceCommandKind.CyberDrop:
                    // Every node id, valid or not, takes the same path; the desk answers NO TARGET for unknown, hidden and foreign ids alike.
                    CyberResult done = ports.Cyber(player, c.Kind, c.Target);
                    return new SpaceReply(protocol, c.Kind, c.RequestId, (byte)done.Outcome, done.NodeId, done.Charged, done.Detail);
                case SpaceCommandKind.SofRaise:
                case SpaceCommandKind.SofOrder:
                case SpaceCommandKind.SofMission:
                case SpaceCommandKind.SofDivert:
                    // Every team and target id, valid or not, takes the same path; the desk answers NO TARGET for unknown, hidden and foreign ids alike.
                    SofResult ran = ports.Sof(player, c);
                    return new SpaceReply(protocol, c.Kind, c.RequestId, (byte)ran.Outcome, ran.Slot, ran.Charged, ran.Detail);
                case SpaceCommandKind.OpFund:
                case SpaceCommandKind.OpPlan:
                case SpaceCommandKind.OpCancel:
                    // Every target id, valid or not, takes the same path; the host answers NO TARGET for unknown, hidden and foreign ids alike.
                    OpResult did = ports.Ops(player, c);
                    return new SpaceReply(protocol, c.Kind, c.RequestId, (byte)did.Outcome, (int)did.Kind, did.Charged, did.Detail);
                default:
                    return ForTasked(c.Kind, c.RequestId, ports.Claim(player, c.RequestId, c.Target));
            }
        }

        private SpaceReply ForTasked(SpaceCommandKind kind, int requestId, in TaskedResult r)
        {
            string claimant = r.Outcome == TaskedOutcome.ClaimedByOther ? SpaceWire.Clean(ports.Claimant(r.CallId), SpaceReply.MaxClaimant) : "";
            return new SpaceReply(protocol, kind, requestId, (byte)r.Outcome, r.CallId, r.Charged, r.Detail, r.Replayed, claimant);
        }

        private enum RefusalKind : byte { Limited, Changed, Busy }

        /// <summary>
        /// Refusals carry no information about the target: a MARK that is limited answers RATE LIMITED regardless of the id, a
        /// replay with a changed payload answers like any invalid MARK, and the TASKED kinds use their ordinary typed refusals.
        /// Refusals are never stored, so a retry with the right payload or after the limit runs normally.
        /// </summary>
        private SpaceReply Refusal(in SpaceCommand c, RefusalKind why)
        {
            if (c.IsCyberVerb)
                return new SpaceReply(protocol, c.Kind, c.RequestId,
                    (byte)(why == RefusalKind.Limited ? CyberOutcome.RateLimited : why == RefusalKind.Changed ? CyberOutcome.NoTarget : CyberOutcome.Unavailable));
            if (c.IsSofVerb)
                return new SpaceReply(protocol, c.Kind, c.RequestId,
                    (byte)(why == RefusalKind.Limited ? SofOutcome.RateLimited : why == RefusalKind.Changed ? SofOutcome.NoTarget : SofOutcome.Unavailable));
            if (c.IsOpsVerb)
                return new SpaceReply(protocol, c.Kind, c.RequestId,
                    (byte)(why == RefusalKind.Limited ? OpOutcome.RateLimited : why == RefusalKind.Changed ? OpOutcome.NoTarget : OpOutcome.Unavailable));
            if (c.Kind == SpaceCommandKind.Mark)
                return new SpaceReply(protocol, c.Kind, c.RequestId, (byte)(why == RefusalKind.Limited ? MarkVerdict.RateLimited : MarkVerdict.NoContact));
            return new SpaceReply(protocol, c.Kind, c.RequestId,
                (byte)(why == RefusalKind.Changed ? TaskedOutcome.Unavailable : TaskedOutcome.Busy), c.Kind == SpaceCommandKind.ClaimTasked ? c.Target : 0);
        }
    }
}
