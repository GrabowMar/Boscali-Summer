using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>
    /// What the feed may say about a contact before the MARK verdict. Deterministic per (contact, observation) so polling cannot
    /// average the noise away; DECOY reads exactly like a hostile so the verdict is the only thing that tells them apart.
    /// </summary>
    internal static class SpaceProbable
    {
        public static ProbableClass Of(ContactClass truth, int id, int generation, out byte percent)
        {
            uint h = Deterministic.Hash(id, generation, 0x5ACE);
            int roll = (int)(h % 100u);
            percent = (byte)(55 + (int)((h >> 8) % 36u)); // 55..90
            bool hostile = truth == ContactClass.EnemyGround || truth == ContactClass.Decoy;
            if (hostile) return roll < 62 ? ProbableClass.Hostile : roll < 86 ? ProbableClass.Unknown : ProbableClass.Neutral;
            if (truth == ContactClass.Friendly) return roll < 55 ? ProbableClass.Friendly : roll < 85 ? ProbableClass.Unknown : ProbableClass.Neutral;
            return roll < 55 ? ProbableClass.Neutral : roll < 85 ? ProbableClass.Unknown : ProbableClass.Hostile;
        }
    }

    /// <summary>The faction view one subscriber holds: the host's last-sent copy, and the client's mirror. Bounded by construction.</summary>
    internal sealed class SpaceFeedState
    {
        public bool Active, Feed;
        public SpaceFamilyState Family = SpaceFamilyState.Dark;
        public byte UplinksLive, UplinksTotal, LiveMarks;
        public TaskedOutcome Gate;
        public int GateDetail;
        public readonly List<FeedContact> Contacts = new List<FeedContact>();
        public readonly List<FeedMark> Marks = new List<FeedMark>();
        public readonly List<FeedPost> Posts = new List<FeedPost>();

        public bool ScalarsEqual(SpaceFeedState o) =>
            Active == o.Active && Feed == o.Feed && Family == o.Family && UplinksLive == o.UplinksLive &&
            UplinksTotal == o.UplinksTotal && LiveMarks == o.LiveMarks && Gate == o.Gate && GateDetail == o.GateDetail;

        public void CopyScalars(SpaceFeedState o)
        {
            Active = o.Active; Feed = o.Feed; Family = o.Family; UplinksLive = o.UplinksLive; UplinksTotal = o.UplinksTotal;
            LiveMarks = o.LiveMarks; Gate = o.Gate; GateDetail = o.GateDetail;
        }

        public void ClearRows() { Contacts.Clear(); Marks.Clear(); Posts.Clear(); }

        public SpaceFeedState Clone()
        {
            var copy = new SpaceFeedState();
            copy.CopyScalars(this);
            copy.Contacts.AddRange(Contacts); copy.Marks.AddRange(Marks); copy.Posts.AddRange(Posts);
            return copy;
        }

        public bool WithinBounds() => Contacts.Count <= SpaceWire.MaxContacts && Marks.Count <= SpaceWire.MaxMarks && Posts.Count <= SpaceWire.MaxPosts;
    }

    internal static class SpaceMirror
    {
        /// <summary>Reveals refresh constantly; a row is re-sent for its deadline only once the old one is a second stale.</summary>
        public const float ExpiryTolerance = 1f;
        public static bool SameExpiry(float a, float b) => Math.Abs(a - b) < ExpiryTolerance;
        /// <summary>The wire carries a decimetre; a smaller move is not worth a packet.</summary>
        public static bool SamePoint(float a, float b) => Math.Abs(a - b) < .1f;

        /// <summary>
        /// What the subscriber holds after <paramref name="sent"/>: previous rows minus removals plus upserts, never the host's
        /// newer values for rows that were within tolerance and so not sent. Keeping the host's baseline equal to the client's
        /// view stops small drifts from accumulating unseen.
        /// </summary>
        public static SpaceFeedState Advance(SpaceFeedState previous, SpaceFeedState current, SpaceStateData sent)
        {
            var next = new SpaceFeedState();
            next.CopyScalars(current);
            if (!current.Active || !current.Feed) return next;
            if (sent.Full || previous == null)
            {
                next.Contacts.AddRange(current.Contacts); next.Marks.AddRange(current.Marks); next.Posts.AddRange(current.Posts);
                return next;
            }
            next.Contacts.AddRange(previous.Contacts); next.Marks.AddRange(previous.Marks); next.Posts.AddRange(previous.Posts);
            for (int i = 0; i < sent.RemovedContacts.Count; i++) { int at = IndexOf(next.Contacts, sent.RemovedContacts[i]); if (at >= 0) next.Contacts.RemoveAt(at); }
            for (int i = 0; i < sent.RemovedMarks.Count; i++) { int at = IndexOf(next.Marks, sent.RemovedMarks[i]); if (at >= 0) next.Marks.RemoveAt(at); }
            for (int i = 0; i < sent.RemovedPosts.Count; i++) { int at = IndexOf(next.Posts, sent.RemovedPosts[i]); if (at >= 0) next.Posts.RemoveAt(at); }
            for (int i = 0; i < sent.Contacts.Count; i++) { int at = IndexOf(next.Contacts, sent.Contacts[i].Id); if (at >= 0) next.Contacts[at] = sent.Contacts[i]; else next.Contacts.Add(sent.Contacts[i]); }
            for (int i = 0; i < sent.Marks.Count; i++) { int at = IndexOf(next.Marks, sent.Marks[i].Id); if (at >= 0) next.Marks[at] = sent.Marks[i]; else next.Marks.Add(sent.Marks[i]); }
            for (int i = 0; i < sent.Posts.Count; i++) { int at = IndexOf(next.Posts, sent.Posts[i].CallId); if (at >= 0) next.Posts[at] = sent.Posts[i]; else next.Posts.Add(sent.Posts[i]); }
            return next;
        }

        /// <summary>
        /// Message that moves a subscriber from <paramref name="previous"/> to <paramref name="next"/>. A null previous (or
        /// <paramref name="full"/>) yields a full snapshot. Rows are never sent to a passive member (Feed false).
        /// </summary>
        public static SpaceStateData Diff(SpaceFeedState previous, SpaceFeedState next, byte protocol, int generation, float now, bool full)
        {
            var d = new SpaceStateData
            {
                Protocol = protocol, Full = full || previous == null, Generation = generation, Now = now,
                Active = next.Active, Feed = next.Feed, Family = next.Family, UplinksLive = next.UplinksLive, UplinksTotal = next.UplinksTotal,
                LiveMarks = next.LiveMarks, Gate = next.Gate, GateDetail = next.GateDetail
            };
            if (!next.Active || !next.Feed) return d;
            SpaceFeedState basis = d.Full ? null : previous;
            for (int i = 0; i < next.Contacts.Count; i++)
            {
                FeedContact c = next.Contacts[i];
                int at = basis == null ? -1 : IndexOf(basis.Contacts, c.Id);
                if (at < 0 || !basis.Contacts[at].SameAs(c)) d.Contacts.Add(c);
            }
            for (int i = 0; basis != null && i < basis.Contacts.Count; i++)
                if (IndexOf(next.Contacts, basis.Contacts[i].Id) < 0) d.RemovedContacts.Add(basis.Contacts[i].Id);
            for (int i = 0; i < next.Marks.Count; i++)
            {
                FeedMark m = next.Marks[i];
                int at = basis == null ? -1 : IndexOf(basis.Marks, m.Id);
                if (at < 0 || !basis.Marks[at].SameAs(m)) d.Marks.Add(m);
            }
            for (int i = 0; basis != null && i < basis.Marks.Count; i++)
                if (IndexOf(next.Marks, basis.Marks[i].Id) < 0) d.RemovedMarks.Add(basis.Marks[i].Id);
            for (int i = 0; i < next.Posts.Count; i++)
            {
                FeedPost p = next.Posts[i];
                int at = basis == null ? -1 : IndexOf(basis.Posts, p.CallId);
                if (at < 0 || !basis.Posts[at].SameAs(p)) d.Posts.Add(p);
            }
            for (int i = 0; basis != null && i < basis.Posts.Count; i++)
                if (IndexOf(next.Posts, basis.Posts[i].CallId) < 0) d.RemovedPosts.Add(basis.Posts[i].CallId);
            return d;
        }

        /// <summary>True when a delta would tell the subscriber nothing new.</summary>
        public static bool Quiet(SpaceFeedState previous, SpaceFeedState next, SpaceStateData delta) =>
            previous != null && previous.ScalarsEqual(next) && !delta.HasRows;

        internal static int IndexOf(List<FeedContact> list, int id) { for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return i; return -1; }
        internal static int IndexOf(List<FeedMark> list, int id) { for (int i = 0; i < list.Count; i++) if (list[i].Id == id) return i; return -1; }
        internal static int IndexOf(List<FeedPost> list, int id) { for (int i = 0; i < list.Count; i++) if (list[i].CallId == id) return i; return -1; }
    }

    /// <summary>
    /// The client's copy of its own faction's SPACE view. It trusts nothing about order: a delta that does not belong to the
    /// mirror's current generation is dropped and flags <see cref="NeedsFull"/>; a full from an older generation is dropped.
    /// Times are converted to the client's own mission clock when a row arrives, so <see cref="Prune"/> and the UI never mix domains.
    /// </summary>
    internal sealed class SpaceFeedMirror
    {
        private bool hasFull;
        public SpaceFeedState State { get; private set; } = new SpaceFeedState();
        public int Generation { get; private set; }
        /// <summary>A delta arrived that the mirror cannot place: ask the host for a fresh full.</summary>
        public bool NeedsFull { get; private set; }
        /// <summary>The headline (family, uplinks) has been heard from the host at least once since the last reset.</summary>
        public bool Known => hasFull;

        public void Reset()
        {
            State = new SpaceFeedState(); Generation = 0; hasFull = false; NeedsFull = false;
        }

        public bool Apply(SpaceStateData d, byte protocol, float clientNow)
        {
            if (d == null || d.Protocol != protocol || d.Generation <= 0 || !SpaceRules.MissionTime(clientNow)) return false;
            if (d.Full)
            {
                if (d.Generation < Generation) return false; // an older subscription (or old faction) can never restore state
                var fresh = new SpaceFeedState();
                if (!Merge(fresh, d, clientNow, true)) return false;
                State = fresh; Generation = d.Generation; hasFull = true; NeedsFull = false;
                return true;
            }
            if (!hasFull || d.Generation != Generation) { if (d.Generation >= Generation) NeedsFull = true; return false; }
            SpaceFeedState merged = State.Clone();
            if (!Merge(merged, d, clientNow, false)) { NeedsFull = true; return false; }
            State = merged;
            return true;
        }

        private static bool Merge(SpaceFeedState into, SpaceStateData d, float clientNow, bool full)
        {
            into.Active = d.Active; into.Feed = d.Feed; into.Family = d.Family; into.UplinksLive = d.UplinksLive;
            into.UplinksTotal = d.UplinksTotal; into.LiveMarks = d.LiveMarks; into.Gate = d.Gate; into.GateDetail = d.GateDetail;
            if (!d.Active || !d.Feed) { into.ClearRows(); return true; }
            float offset = clientNow - d.Now;
            if (full) into.ClearRows();
            for (int i = 0; i < d.RemovedContacts.Count; i++) { int at = SpaceMirror.IndexOf(into.Contacts, d.RemovedContacts[i]); if (at >= 0) into.Contacts.RemoveAt(at); }
            for (int i = 0; i < d.RemovedMarks.Count; i++) { int at = SpaceMirror.IndexOf(into.Marks, d.RemovedMarks[i]); if (at >= 0) into.Marks.RemoveAt(at); }
            for (int i = 0; i < d.RemovedPosts.Count; i++) { int at = SpaceMirror.IndexOf(into.Posts, d.RemovedPosts[i]); if (at >= 0) into.Posts.RemoveAt(at); }
            for (int i = 0; i < d.Contacts.Count; i++)
            {
                FeedContact c = d.Contacts[i]; c.Expires += offset;
                int at = SpaceMirror.IndexOf(into.Contacts, c.Id);
                if (at >= 0) into.Contacts[at] = c; else into.Contacts.Add(c);
            }
            for (int i = 0; i < d.Marks.Count; i++)
            {
                FeedMark m = d.Marks[i]; m.Expires += offset;
                int at = SpaceMirror.IndexOf(into.Marks, m.Id);
                if (at >= 0) into.Marks[at] = m; else into.Marks.Add(m);
            }
            for (int i = 0; i < d.Posts.Count; i++)
            {
                FeedPost p = d.Posts[i]; p.Expires += offset;
                int at = SpaceMirror.IndexOf(into.Posts, p.CallId);
                if (at >= 0) into.Posts[at] = p; else into.Posts.Add(p);
            }
            return into.WithinBounds(); // merged rows beyond the caps mean a corrupt stream: reject, never grow
        }

        /// <summary>Drops rows whose deadline passed on the client clock (the host also removes them, this just keeps them from lingering).</summary>
        public void Prune(float clientNow)
        {
            if (!SpaceRules.MissionTime(clientNow)) return;
            SpaceFeedState s = State;
            for (int i = s.Contacts.Count - 1; i >= 0; i--) if (clientNow >= s.Contacts[i].Expires) s.Contacts.RemoveAt(i);
            for (int i = s.Marks.Count - 1; i >= 0; i--) if (clientNow >= s.Marks[i].Expires) s.Marks.RemoveAt(i);
            for (int i = s.Posts.Count - 1; i >= 0; i--) if (clientNow >= s.Posts[i].Expires) s.Posts.RemoveAt(i);
        }
    }

    /// <summary>
    /// Host side: one record per faction member (passive headline) with an optional feed lease (rows). Faction membership comes
    /// from the caller each poll, never from the client, and a change of faction bumps the generation and forces a full.
    /// </summary>
    internal sealed class SpaceSubscriptions
    {
        public const int MaxSubscribers = SpaceContacts.MaxPlayers;
        public const float FeedLeaseSeconds = 20f, PollSeconds = .5f;

        private sealed class Sub
        {
            public int Faction = int.MinValue, Generation;
            public bool Feed, NeedFull = true, Seen;
            public float FeedUntil, NextAt;
            public SpaceFeedState Last;
        }

        private readonly Dictionary<ulong, Sub> subs = new Dictionary<ulong, Sub>();
        private readonly List<ulong> removed = new List<ulong>();
        private readonly byte protocol;
        private int generation;

        public SpaceSubscriptions(byte protocol) { this.protocol = protocol; }

        public int Count => subs.Count;
        /// <summary>Last generation handed out. Never decreases, not even across a scene reset.</summary>
        public int Generation => generation;

        public bool IsFeeding(ulong player, float now) =>
            subs.TryGetValue(player, out Sub s) && s.Feed && SpaceRules.MissionTime(now) && now < s.FeedUntil;

        /// <summary>Starts or refreshes a feed lease and forces a fresh full. False when the table is full of live members.</summary>
        public bool OpenFeed(ulong player, float now)
        {
            if (player == 0 || !SpaceRules.MissionTime(now)) return false;
            if (!subs.TryGetValue(player, out Sub s))
            {
                if (subs.Count >= MaxSubscribers) return false;
                subs[player] = s = new Sub();
            }
            s.Feed = true; s.NeedFull = true; s.FeedUntil = now + FeedLeaseSeconds;
            return true;
        }

        /// <summary>
        /// Operator input on an open feed keeps the lease. From a member with no open feed it is the client saying it lost sync
        /// (a rejected delta): the host answers with a fresh headline full. Unknown members are untouched.
        /// </summary>
        public void Touch(ulong player, float now)
        {
            if (!subs.TryGetValue(player, out Sub s) || !SpaceRules.MissionTime(now)) return;
            if (s.Feed && now < s.FeedUntil) s.FeedUntil = now + FeedLeaseSeconds;
            else s.NeedFull = true;
        }

        public void CloseFeed(ulong player)
        {
            if (subs.TryGetValue(player, out Sub s) && s.Feed) { s.Feed = false; s.NeedFull = true; }
        }

        public void Clear() { subs.Clear(); removed.Clear(); }

        public bool Remove(ulong player) => subs.Remove(player);

        /// <summary>Marks the start of a poll round; anything not polled before <see cref="EndRound"/> has left the faction.</summary>
        public void BeginRound() { foreach (var pair in subs) pair.Value.Seen = false; }

        public void EndRound()
        {
            removed.Clear();
            foreach (var pair in subs) if (!pair.Value.Seen) removed.Add(pair.Key);
            for (int i = 0; i < removed.Count; i++) subs.Remove(removed[i]);
            removed.Clear();
        }

        /// <summary>This member is still in the faction this round, even if nothing is due for them yet.</summary>
        public void Keep(ulong player) { if (subs.TryGetValue(player, out Sub s)) s.Seen = true; }

        /// <summary>True when this member could be sent something now (throttle or a pending full).</summary>
        public bool Due(ulong player, float now) =>
            !subs.TryGetValue(player, out Sub s) || s.NeedFull || !SpaceRules.MissionTime(now) || now >= s.NextAt ||
            (s.Feed && now >= s.FeedUntil);

        /// <summary>
        /// The message that brings this member's mirror up to <paramref name="current"/>, or null when nothing changed or the
        /// table is full. <paramref name="current"/> must carry rows only when <see cref="IsFeeding"/>; it is copied, never kept.
        /// A null/!Active current (the faction has no SPACE) still reaches the member so a stale mirror clears.
        /// </summary>
        public SpaceStateData Next(ulong player, int faction, SpaceFeedState current, float now)
        {
            if (player == 0 || current == null || !SpaceRules.MissionTime(now)) return null;
            if (!subs.TryGetValue(player, out Sub s))
            {
                if (subs.Count >= MaxSubscribers) return null;
                subs[player] = s = new Sub();
            }
            s.Seen = true;
            if (s.Feed && now >= s.FeedUntil) { s.Feed = false; s.NeedFull = true; }
            if (s.Faction != faction) { s.Faction = faction; s.NeedFull = true; s.Last = null; }
            if (!s.NeedFull && now < s.NextAt) return null;
            if (current.Feed && !s.Feed)
            {
                // Rows were built for a member without a lease: they never go out, only the headline does.
                current = current.Clone(); current.Feed = false; current.ClearRows();
            }
            s.NextAt = now + PollSeconds;
            if (s.NeedFull || s.Last == null)
            {
                if (generation == int.MaxValue) return null;
                SpaceStateData full = SpaceMirror.Diff(null, current, protocol, s.Generation = ++generation, now, true);
                s.Last = SpaceMirror.Advance(null, current, full); s.NeedFull = false;
                return full;
            }
            SpaceStateData delta = SpaceMirror.Diff(s.Last, current, protocol, s.Generation, now, false);
            if (SpaceMirror.Quiet(s.Last, current, delta)) return null;
            s.Last = SpaceMirror.Advance(s.Last, current, delta);
            return delta;
        }
    }
}
