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
