using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>
    /// The faction identity every peer derives the same way from the faction's own name (FNV-1a, never 0), so two peers agree on it whatever their local Unity instance ids are.
    /// It is written fixed-width right after the protocol byte of every faction state (CYBER, SOF, OPS, FRONTS and SPACE), followed by the host's scene epoch.
    /// </summary>
    internal static class FactionIdentity
    {
        /// <summary>Faction (4 bytes) and epoch (2 bytes).</summary>
        public const int Bytes = 6;

        public static uint Of(string name)
        {
            uint h = 2166136261u;
            if (name != null) for (int i = 0; i < name.Length; i++) h = unchecked((h ^ name[i]) * 16777619u);
            return h == 0u ? 1u : h;
        }

        public static void Write(ISpaceWriter w, uint faction, ushort epoch)
        {
            w.WriteByte((byte)faction); w.WriteByte((byte)(faction >> 8)); w.WriteByte((byte)(faction >> 16)); w.WriteByte((byte)(faction >> 24));
            w.WriteByte((byte)epoch); w.WriteByte((byte)(epoch >> 8));
        }

        /// <summary>False when the identity is truncated, names no faction or carries no epoch.</summary>
        public static bool Read(ISpaceReader r, out uint faction, out ushort epoch)
        {
            faction = 0; epoch = 0;
            if (r.Remaining < Bytes || !r.TryReadByte(out byte a) || !r.TryReadByte(out byte b) || !r.TryReadByte(out byte c) || !r.TryReadByte(out byte d) ||
                !r.TryReadByte(out byte lo) || !r.TryReadByte(out byte hi)) return false;
            faction = unchecked((uint)(a | (b << 8) | (c << 16) | (d << 24))); epoch = (ushort)(lo | (hi << 8));
            return faction != 0u && epoch != 0;
        }
    }

    /// <summary>The check every client mirror makes before a state may touch it, so a state built for another faction or an earlier host scene never shows.</summary>
    internal static class LinkIdentity
    {
        /// <summary>
        /// True when a state built for <paramref name="faction"/> in host scene <paramref name="epoch"/> may be considered by a client whose own faction is <paramref name="expected"/>
        /// and which has adopted <paramref name="adopted"/> on this link (0 = none yet): the faction must be the client's own (0 = not resolved yet, nothing is accepted) and the
        /// epoch the adopted one or a newer one, compared as a wrapping 16-bit serial number.
        /// </summary>
        public static bool Admits(uint faction, ushort epoch, uint expected, ushort adopted) =>
            expected != 0u && faction == expected && epoch != 0 && (adopted == 0 || ((epoch - adopted) & 0xFFFF) < 0x8000);
    }

    /// <summary>
    /// The header every faction-only full-snapshot state (CYBER, SOF, OPS, FRONTS) shares: the wire protocol byte, the faction and host scene it was built for, the host's send
    /// sequence and the host mission clock it was built on. <typeparamref name="T"/> supplies its own deep copy and its "did anything change" comparison.
    /// </summary>
    internal abstract class FactionStateData<T> where T : FactionStateData<T>
    {
        public byte Protocol;
        /// <summary>The stable wire identity of the faction this state was built for (<see cref="FactionIdentity.Of"/> of its name, never a Unity instance id); 0 is no faction.</summary>
        public uint Faction;
        /// <summary>The host's scene epoch when the state was built: it moves on at every scene reset, so a state of an earlier scene is told from the current one; 0 is none.</summary>
        public ushort Epoch;
        public int Seq;
        public float Now;

        /// <summary>The header after the protocol byte (and any flag byte) that every state shares: faction and epoch, the send sequence and the host mission clock.</summary>
        public void WriteHeader(ISpaceWriter w)
        {
            FactionIdentity.Write(w, Faction, Epoch);
            SpaceWire.WriteVar(w, (uint)Math.Max(0, Seq));
            SpaceWire.WriteFloat(w, Now);
        }

        /// <summary>False for a hostile or truncated header: no faction, no epoch, or a clock that is not a mission time.</summary>
        public bool ReadHeader(ISpaceReader r)
        {
            if (!FactionIdentity.Read(r, out uint faction, out ushort epoch) || !SpaceWire.ReadInt(r, out int seq) || !SpaceWire.ReadFloat(r, out float now) || !SpaceRules.MissionTime(now)) return false;
            Faction = faction; Epoch = epoch; Seq = seq; Now = now;
            return true;
        }

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
        /// <summary>The newest host scene epoch applied on this link (0 = none). Kept by a faction or scene reset like <see cref="Floor"/>: an earlier scene's state can never come back.</summary>
        public ushort Epoch { get; private set; }

        public void Reset() { State = new T(); Known = false; Seq = 0; }

        public void ResetLink() { Reset(); Floor = 0; Epoch = 0; }

        /// <summary>False when a row list exceeds what the codec may carry (a hostile or corrupt snapshot).</summary>
        protected abstract bool Bounds(T d);

        /// <summary>The snapshot copied with every host-clock expiry moved by <paramref name="offset"/>, stamped with the client's clock.</summary>
        protected abstract T Shift(T d, float offset, float clientNow);

        /// <param name="faction">The client's own faction on the wire (<see cref="FactionIdentity.Of"/>), resolved by the caller at the moment of the call; 0 refuses everything.</param>
        public bool Apply(T d, byte protocol, float clientNow, uint faction)
        {
            if (d == null || d.Protocol != protocol || d.Seq <= 0 || !LinkIdentity.Admits(d.Faction, d.Epoch, faction, Epoch) || !SpaceRules.MissionTime(clientNow)) return false;
            if (d.Epoch == Epoch && d.Seq <= Floor) return false; // a newer scene may restart the sequence; the same scene never goes back
            if (!Bounds(d)) return false;
            State = Shift(d, clientNow - d.Now, clientNow); Known = true; Seq = d.Seq; Floor = d.Seq; Epoch = d.Epoch;
            return true;
        }
    }

    /// <summary>Host side: per-member send gating (change-only, at most one message per 2 s, a fresh full on a new member or an explicit sync).</summary>
    internal sealed class FactionSubscriptions<T> where T : FactionStateData<T>
    {
        public const int MaxSubscribers = SpaceContacts.MaxPlayers;
        public const float MinGapSeconds = 2f;

        private sealed class Sub { public int Faction; public ushort Epoch; public float NextAt; public T Last; public bool Force = true; }
        private readonly Dictionary<ulong, Sub> subs = new Dictionary<ulong, Sub>();
        private readonly List<ulong> removed = new List<ulong>();
        private int seq;
        /// <summary>The host's scene epoch, set by the owner before every poll; a member's first state after it moves is a fresh full one stamped with it.</summary>
        public ushort Epoch { get; set; } = 1;

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
        /// caller, never kept here beyond the comparison copy. <paramref name="faction"/> is the member's stable faction identity (never 0); it and <see cref="Epoch"/> are stamped on the snapshot.
        /// </summary>
        public T Next(ulong player, int faction, T current, float now, float wall)
        {
            if (player == 0 || faction == 0 || current == null || !SpaceRules.MissionTime(now) || !SpaceRules.MissionTime(wall) || seq == int.MaxValue) return null;
            if (!subs.TryGetValue(player, out Sub s))
            {
                if (subs.Count >= MaxSubscribers) return null;
                subs[player] = s = new Sub();
            }
            if (s.Faction != faction || s.Epoch != Epoch) { s.Faction = faction; s.Epoch = Epoch; s.Force = true; s.Last = null; }
            if (!s.Force && wall < s.NextAt) return null;
            if (!s.Force && s.Last != null && s.Last.SameAs(current)) return null;
            current.Faction = unchecked((uint)faction);
            current.Epoch = Epoch;
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
