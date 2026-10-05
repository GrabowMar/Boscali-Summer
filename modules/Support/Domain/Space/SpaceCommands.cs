using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>
    /// Per-player token bucket for SPACE commands (wall time: this is transport abuse control, not gameplay).
    /// Bounded to <see cref="SpaceContacts.MaxPlayers"/> records; only an idle record is ever evicted, a full table of busy
    /// players refuses a stranger rather than resetting anyone's limit.
    /// </summary>
    internal sealed class SpaceCommandLimiter
    {
        public const int Burst = 10, MaxPlayers = SpaceContacts.MaxPlayers;
        public const float RefillPerSecond = 5f;

        private struct Bucket { public float Tokens, At; }
        private readonly Dictionary<ulong, Bucket> buckets = new Dictionary<ulong, Bucket>();

        public int Count => buckets.Count;

        public bool Admit(ulong player, float wall)
        {
            if (player == 0 || !SpaceRules.MissionTime(wall)) return false;
            if (!buckets.TryGetValue(player, out Bucket b))
            {
                if (buckets.Count >= MaxPlayers && !EvictIdle(wall)) return false;
                b = new Bucket { Tokens = Burst, At = wall };
            }
            float elapsed = Math.Max(0f, wall - b.At);
            b.Tokens = Math.Min(Burst, b.Tokens + elapsed * RefillPerSecond);
            b.At = Math.Max(b.At, wall);
            bool ok = b.Tokens >= 1f;
            if (ok) b.Tokens -= 1f;
            buckets[player] = b;
            return ok;
        }

        public void Clear() => buckets.Clear();

        private bool EvictIdle(float wall)
        {
            ulong victim = 0; float oldest = float.PositiveInfinity; bool found = false;
            foreach (var pair in buckets)
            {
                bool idle = pair.Value.Tokens + Math.Max(0f, wall - pair.Value.At) * RefillPerSecond >= Burst;
                if (idle && pair.Value.At < oldest) { oldest = pair.Value.At; victim = pair.Key; found = true; }
            }
            return found && buckets.Remove(victim);
        }
    }

    /// <summary>
    /// Exact-result replay for SPACE requests: at most <see cref="PerPlayer"/> receipts for each of
    /// <see cref="MaxPlayers"/> identities, keyed by (scene epoch, request channel, request id). A receipt remembers what was
    /// asked (kind and payload fingerprint) so the same id with a different payload is refused, never executed. Pending
    /// entries (a queued claim, or a request in progress) are never evicted; only completed old receipts are.
    /// </summary>
    internal sealed class SpaceReplayCache
    {
        public const int MaxPlayers = SpaceContacts.MaxPlayers, PerPlayer = 32;

        public enum Lookup : byte { Miss, Replay, Pending, Mismatch }

        private readonly struct Key : IEquatable<Key>
        {
            public readonly int Epoch, Channel, Request;
            public Key(int epoch, int channel, int request) { Epoch = epoch; Channel = channel; Request = request; }
            public bool Equals(Key o) => Epoch == o.Epoch && Channel == o.Channel && Request == o.Request;
            public override bool Equals(object obj) => obj is Key k && Equals(k);
            public override int GetHashCode() => unchecked(Epoch * 397 ^ Channel * 31 ^ Request);
        }

        private sealed class Entry
        {
            public Key Key;
            public SpaceCommandKind Kind;
            public int Fingerprint;
            public SpaceReply Reply;
            public bool Pending;
            public float At;
        }

        private sealed class Ring { public readonly List<Entry> Entries = new List<Entry>(PerPlayer); public long Touched; }

        private readonly Dictionary<ulong, Ring> players = new Dictionary<ulong, Ring>();
        private long tick;

        public int PlayerCount => players.Count;
        public int ReceiptCount(ulong player) => players.TryGetValue(player, out Ring r) ? r.Entries.Count : 0;

        public Lookup Find(ulong player, int epoch, int channel, in SpaceCommand command, out SpaceReply reply)
        {
            reply = default;
            Entry e = Get(player, new Key(epoch, channel, command.RequestId));
            if (e == null) return Lookup.Miss;
            if (e.Kind != command.Kind || e.Fingerprint != command.Fingerprint()) return Lookup.Mismatch;
            reply = e.Reply;
            return e.Pending ? Lookup.Pending : Lookup.Replay;
        }

        /// <summary>Claims a slot before the request runs, so a request that cannot be remembered is never executed. False: refuse it.</summary>
        public bool Begin(ulong player, int epoch, int channel, in SpaceCommand command, float now)
        {
            if (player == 0 || command.RequestId <= 0) return false;
            if (!players.TryGetValue(player, out Ring ring))
            {
                if (players.Count >= MaxPlayers && !EvictPlayer()) return false;
                players[player] = ring = new Ring();
            }
            ring.Touched = ++tick;
            if (ring.Entries.Count >= PerPlayer)
            {
                int victim = -1;
                for (int i = 0; i < ring.Entries.Count; i++) if (!ring.Entries[i].Pending) { victim = i; break; }
                if (victim < 0) return false;
                ring.Entries.RemoveAt(victim);
            }
            ring.Entries.Add(new Entry
            {
                Key = new Key(epoch, channel, command.RequestId), Kind = command.Kind, Fingerprint = command.Fingerprint(), Pending = true, At = now,
                Reply = new SpaceReply(command.Protocol, command.Kind, command.RequestId, 0)
            });
            return true;
        }

        /// <summary>Records the verdict. <paramref name="pending"/> keeps the entry live until <see cref="Resolve"/> (a queued claim).</summary>
        public void Complete(ulong player, int epoch, int channel, int request, in SpaceReply reply, bool pending)
        {
            Entry e = Get(player, new Key(epoch, channel, request));
            if (e == null) return;
            e.Reply = reply; e.Pending = pending;
        }

        /// <summary>Request ids of this player's pending entries that have waited longer than <paramref name="ttl"/> mission seconds.</summary>
        public int StalePending(ulong player, float now, float ttl, List<int> into)
        {
            into.Clear();
            if (players.TryGetValue(player, out Ring ring))
                for (int i = 0; i < ring.Entries.Count; i++)
                    if (ring.Entries[i].Pending && !(now - ring.Entries[i].At < ttl)) into.Add(ring.Entries[i].Key.Request);
            return into.Count;
        }

        /// <summary>The queued claim reached its final verdict. False when the receipt was already gone.</summary>
        public bool Resolve(ulong player, int epoch, int channel, int request, in SpaceReply final)
        {
            Entry e = Get(player, new Key(epoch, channel, request));
            if (e == null) return false;
            e.Reply = final; e.Pending = false;
            return true;
        }

        /// <summary>The request never ran (it threw): forget its placeholder so a retry can run.</summary>
        public void Abort(ulong player, int epoch, int channel, int request)
        {
            if (!players.TryGetValue(player, out Ring ring)) return;
            var key = new Key(epoch, channel, request);
            for (int i = 0; i < ring.Entries.Count; i++)
                if (ring.Entries[i].Key.Equals(key)) { ring.Entries.RemoveAt(i); return; }
        }

        public void Clear() { players.Clear(); }
        public void Forget(ulong player) => players.Remove(player);

        private Entry Get(ulong player, in Key key)
        {
            if (!players.TryGetValue(player, out Ring ring)) return null;
            for (int i = 0; i < ring.Entries.Count; i++) if (ring.Entries[i].Key.Equals(key)) return ring.Entries[i];
            return null;
        }

        /// <summary>Makes room for a stranger by dropping the least recently used identity that holds no live request.</summary>
        private bool EvictPlayer()
        {
            ulong victim = 0; long oldest = long.MaxValue; bool found = false;
            foreach (var pair in players)
            {
                bool live = false;
                for (int i = 0; i < pair.Value.Entries.Count; i++) if (pair.Value.Entries[i].Pending) { live = true; break; }
                if (!live && pair.Value.Touched < oldest) { oldest = pair.Value.Touched; victim = pair.Key; found = true; }
            }
            return found && players.Remove(victim);
        }
    }

    /// <summary>What the host decides for a SPACE command. Nothing here accepts a faction, price, class or favourite from the client.</summary>
    internal interface ISpaceCommandPorts
    {
        /// <summary>Host mission time (seconds), for the pending-entry deadline.</summary>
        float Now { get; }
        MarkVerdict Mark(ulong player, int contactId);
        TaskedResult Send(ulong player, int requestId, int[] markIds);
        /// <summary>No favourite flag: the host holds no knowledge of a pilot's CALLS favourites, so arbitration is receipt order.</summary>
        TaskedResult Claim(ulong player, int requestId, int postId);
        bool TryTaskedResult(ulong player, int requestId, out TaskedResult result);
        /// <summary>Callsign of whoever holds the post, for the CLAIMED BY words (null/empty when unknown).</summary>
        string Claimant(int callId);
    }

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
        private readonly ISpaceCommandPorts ports;
        private readonly SpaceCommandLimiter limiter = new SpaceCommandLimiter();
        private readonly SpaceReplayCache cache = new SpaceReplayCache();
        private readonly List<int> stale = new List<int>(SpaceReplayCache.PerPlayer);

        public SpaceCommandHost(byte protocol, ISpaceCommandPorts ports) { this.protocol = protocol; this.ports = ports; }

        public SpaceCommandLimiter Limiter => limiter;
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
            if (c.Kind == SpaceCommandKind.Mark)
                return new SpaceReply(protocol, c.Kind, c.RequestId, (byte)(why == RefusalKind.Limited ? MarkVerdict.RateLimited : MarkVerdict.NoContact));
            return new SpaceReply(protocol, c.Kind, c.RequestId,
                (byte)(why == RefusalKind.Changed ? TaskedOutcome.Unavailable : TaskedOutcome.Busy), c.Kind == SpaceCommandKind.ClaimTasked ? c.Target : 0);
        }
    }

    /// <summary>
    /// In-process replies (singleplayer and the listen-host player) are queued and delivered on the next frame, never from inside
    /// the request call: the requester only learns its request id when the call returns, so a verdict raised earlier would find
    /// nobody waiting for it. Bounded; a full queue refuses (the caller logs) rather than growing.
    /// </summary>
    internal sealed class SpaceReplyQueue
    {
        public const int Capacity = 64;
        private readonly Queue<SpaceReply> queue = new Queue<SpaceReply>(Capacity);

        public int Count => queue.Count;
        public bool Enqueue(in SpaceReply reply)
        {
            if (queue.Count >= Capacity) return false;
            queue.Enqueue(reply);
            return true;
        }
        public bool TryDequeue(out SpaceReply reply)
        {
            reply = default;
            if (queue.Count == 0) return false;
            reply = queue.Dequeue();
            return true;
        }
        public void Clear() => queue.Clear();
    }
}
