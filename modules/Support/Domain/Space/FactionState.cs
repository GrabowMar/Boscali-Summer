using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>
    /// The header every faction-only full-snapshot state (CYBER, SOF, OPS) shares: the wire protocol byte, the host's send sequence and the host mission clock
    /// it was built on. <typeparamref name="T"/> supplies its own deep copy and its "did anything change" comparison.
    /// </summary>
    internal abstract class FactionStateData<T> where T : FactionStateData<T>
    {
        public byte Protocol;
        public int Seq;
        public float Now;

        public abstract T Clone();

        /// <summary>The fields that decide whether anything changed since the last send (everything but Seq and Now).</summary>
        public abstract bool SameAs(T other);
    }

    /// <summary>
    /// The client's copy of its own faction's state. Full snapshots only: anything not newer than the last one on this link is dropped, and times
    /// are moved onto the client's own mission clock when a snapshot is applied.
    /// </summary>
    internal abstract class FactionMirror<T> where T : FactionStateData<T>, new()
    {
        public T State { get; private set; } = new T();
        public bool Known { get; private set; }
        public int Seq { get; private set; }
        /// <summary>Highest sequence applied on this link; a faction or scene reset keeps it so an in-flight snapshot of the old view stays refused.</summary>
        public int Floor { get; private set; }

        public void Reset() { State = new T(); Known = false; Seq = 0; }

        public void ResetLink() { Reset(); Floor = 0; }

        /// <summary>False when a row list exceeds what the codec may carry (a hostile or corrupt snapshot).</summary>
        protected abstract bool Bounds(T d);

        /// <summary>The snapshot copied with every host-clock expiry moved by <paramref name="offset"/>, stamped with the client's clock.</summary>
        protected abstract T Shift(T d, float offset, float clientNow);

        public bool Apply(T d, byte protocol, float clientNow)
        {
            if (d == null || d.Protocol != protocol || d.Seq <= 0 || d.Seq <= Floor || !SpaceRules.MissionTime(clientNow)) return false;
            if (!Bounds(d)) return false;
            State = Shift(d, clientNow - d.Now, clientNow); Known = true; Seq = d.Seq; Floor = d.Seq;
            return true;
        }
    }

    /// <summary>Host side: per-member send gating (change-only, at most one message per 2 s, a fresh full on a new member or an explicit sync).</summary>
    internal sealed class FactionSubscriptions<T> where T : FactionStateData<T>
    {
        public const int MaxSubscribers = SpaceContacts.MaxPlayers;
        public const float MinGapSeconds = 2f;

        private sealed class Sub { public int Faction = int.MinValue; public float NextAt; public T Last; public bool Force = true; }
        private readonly Dictionary<ulong, Sub> subs = new Dictionary<ulong, Sub>();
        private readonly List<ulong> removed = new List<ulong>();
        private int seq;

        public int Count => subs.Count;

        public void Clear() { subs.Clear(); removed.Clear(); }

        /// <summary>The member asked for a fresh state (a client that lost its mirror): the next poll sends one.</summary>
        public void Resync(ulong player) { if (subs.TryGetValue(player, out Sub s)) { s.Force = true; s.NextAt = 0f; } }

        /// <summary>Drops every member that is not in <paramref name="keep"/> (this poll's roster).</summary>
        public void Prune(HashSet<ulong> keep)
        {
            removed.Clear();
            foreach (var pair in subs) if (keep == null || !keep.Contains(pair.Key)) removed.Add(pair.Key);
            for (int i = 0; i < removed.Count; i++) subs.Remove(removed[i]);
            removed.Clear();
        }

        /// <summary>
        /// The snapshot to send this member now, or null. <paramref name="current"/> is built for the member's faction; it is copied by the
        /// caller, never kept here beyond the comparison copy.
        /// </summary>
        public T Next(ulong player, int faction, T current, float now, float wall)
        {
            if (player == 0 || current == null || !SpaceRules.MissionTime(now) || !SpaceRules.MissionTime(wall) || seq == int.MaxValue) return null;
            if (!subs.TryGetValue(player, out Sub s))
            {
                if (subs.Count >= MaxSubscribers) return null;
                subs[player] = s = new Sub();
            }
            if (s.Faction != faction) { s.Faction = faction; s.Force = true; s.Last = null; }
            if (!s.Force && wall < s.NextAt) return null;
            if (!s.Force && s.Last != null && s.Last.SameAs(current)) return null;
            current.Seq = ++seq;
            current.Now = now;
            s.Last = current.Clone();
            s.Force = false;
            s.NextAt = wall + MinGapSeconds;
            return current;
        }
    }

    /// <summary>The rate gate every faction HUD notice tracker (CYBER, SOF, OPS) shares: QUIET drops a notice, and at most one shows every <see cref="GapSeconds"/>.</summary>
    internal abstract class NoticeTrackerBase
    {
        public const float GapSeconds = 3f;
        private float nextAt;

        protected void ResetGate() { nextAt = 0f; }

        /// <summary>True when a notice found by this observation may show now; it then starts the gap.</summary>
        protected bool Admit(bool found, float now, bool quiet)
        {
            if (quiet || !found || now < nextAt) return false;
            nextAt = now + GapSeconds;
            return true;
        }
    }
}
