using System;
using System.Collections.Generic;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    internal enum ContactClass : byte { EnemyGround, Neutral, Friendly, Decoy, PlayerAircraft }
    // Capacity is retained for id stability but Mark never returns it: it would reveal that an id is admitted.
    internal enum MarkVerdict : byte { NoContact, Confirmed, Neutral, Friendly, Decoy, RateLimited, Capacity }

    internal readonly struct SpaceMark
    {
        public readonly int Id;
        public readonly float X, Z, ExpiresAt;
        public readonly bool Moving;
        public readonly BirdKind Source;

        public SpaceMark(int id, float x, float z, bool moving, float expiresAt, BirdKind source)
        {
            Id = id; X = x; Z = z; Moving = moving; ExpiresAt = expiresAt; Source = source;
        }
    }

    /// <summary>Host-only truth; the faction mirror must derive its probable classification separately.</summary>
    internal readonly struct SpaceContact
    {
        public readonly int Id, TypeId, ObservationGeneration;
        public readonly ContactClass Classification;
        public readonly float X, Z, ObservedAt, ExpiresAt;
        public readonly bool Moving;
        public readonly BirdKind Source;

        internal SpaceContact(int id, ContactClass classification, int typeId, float x, float z,
            bool moving, float observedAt, BirdKind source, int generation)
        {
            Id = id; Classification = classification; TypeId = typeId; X = x; Z = z;
            Moving = moving; ObservedAt = observedAt; ExpiresAt = observedAt + SpaceContacts.RevealSeconds;
            Source = source; ObservationGeneration = generation;
        }
    }

    internal readonly struct SpaceMarkProvenance
    {
        public readonly int ObservationGeneration, TypeId, EarnedEffort;
        public readonly ulong Player;

        internal SpaceMarkProvenance(int generation, int typeId, ulong player, int earnedEffort)
        {
            ObservationGeneration = generation; TypeId = typeId; Player = player; EarnedEffort = earnedEffort;
        }
    }

    /// <summary>One faction's admitted observations and immutable confirmed ground points.</summary>
    internal sealed class SpaceContacts
    {
        public const int MaxReveals = 48, MaxMarks = 12, MaxPlayers = 128, AttemptsPerMinute = 6;
        public const float RevealSeconds = 20f, MovingMarkSeconds = 180f, StaticMarkSeconds = 480f, AttemptWindowSeconds = 60f;
        private const float MaximumCoordinate = 10000000f;

        private readonly struct MarkRecord
        {
            public readonly SpaceMark Mark;
            public readonly SpaceMarkProvenance Provenance;
            public readonly float ConfirmedAt;
            public MarkRecord(in SpaceMark mark, in SpaceMarkProvenance provenance, float confirmedAt)
            {
                Mark = mark; Provenance = provenance; ConfirmedAt = confirmedAt;
            }
        }

        private sealed class PlayerRecord
        {
            public int Effort;
            private readonly float[] attempts = new float[AttemptsPerMinute];
            private int head, count;
            private float lastAttempt = float.NegativeInfinity;

            public bool Attempt(float now)
            {
                if (now < lastAttempt) return false;
                int recent = 0;
                for (int i = 0; i < count; i++)
                    if (now - attempts[i] < AttemptWindowSeconds) recent++;
                attempts[head] = now;
                head = (head + 1) % attempts.Length;
                if (count < attempts.Length) count++;
                lastAttempt = now;
                return recent < AttemptsPerMinute;
            }

            public void AddEffort(int amount)
            {
                long next = (long)Effort + amount;
                Effort = (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, next));
            }
        }

        private readonly Dictionary<int, SpaceContact> reveals = new Dictionary<int, SpaceContact>();
        private readonly Dictionary<int, int> confirmed = new Dictionary<int, int>();
        private readonly Dictionary<int, MarkRecord> marks = new Dictionary<int, MarkRecord>();
        private readonly Dictionary<ulong, PlayerRecord> players = new Dictionary<ulong, PlayerRecord>();
        private readonly List<int> expired = new List<int>(MaxReveals);
        private int nextGeneration;
        private int stateGeneration = 1;
        private bool retired;

        public int Count => reveals.Count;
        public int Generation => retired ? 0 : stateGeneration;
        public int MarkCount => marks.Count;
        public int PlayerCount => players.Count;
        public int ConfirmedObservationCount => confirmed.Count;
        public int Effort(ulong player) => players.TryGetValue(player, out PlayerRecord record) ? record.Effort : 0;

        public bool Reveal(int id, ContactClass classification, int typeId, float x, float z, bool moving,
            float now, BirdKind source = BirdKind.Optical)
        {
            if (retired || id <= 0 || typeId < 0 || (byte)classification >= (byte)ContactClass.PlayerAircraft ||
                !Coordinate(x) || !Coordinate(z) || !SpaceRules.MissionTime(now) ||
                !Deadline(now, RevealSeconds, out _) || (source != BirdKind.Optical && source != BirdKind.Radar)) return false;
            Prune(now);
            int generation;
            if (reveals.TryGetValue(id, out SpaceContact existing))
            {
                if (now < existing.ObservedAt) return false;
                generation = existing.ObservationGeneration;
            }
            else
            {
                if (reveals.Count >= MaxReveals || nextGeneration == int.MaxValue) return false;
                generation = ++nextGeneration;
                confirmed.Remove(id);
            }
            reveals[id] = new SpaceContact(id, classification, typeId, x, z, moving, now, source, generation);
            if (classification != ContactClass.EnemyGround) marks.Remove(id);
            return true;
        }

        public bool Forget(int id) => RemoveReveal(id);

        public bool TryReveal(int id, float now, out SpaceContact contact)
        {
            contact = default;
            if (!SpaceRules.MissionTime(now) || !reveals.TryGetValue(id, out SpaceContact found)) return false;
            if (now < found.ObservedAt) return false;
            if (now >= found.ExpiresAt) { RemoveReveal(id); return false; }
            contact = found;
            return true;
        }

        public MarkVerdict Mark(ulong player, int id, float now, bool recentInput)
        {
            if (retired || player == 0 || !SpaceRules.MissionTime(now)) return MarkVerdict.NoContact;
            Prune(now);
            bool admitted = reveals.TryGetValue(id, out SpaceContact contact) && now >= contact.ObservedAt;
            // No verdict before the quota check may depend on whether the id is admitted, or probing leaks live ids.
            if (!players.TryGetValue(player, out PlayerRecord record))
            {
                if (players.Count >= MaxPlayers) return MarkVerdict.NoContact;
                players[player] = record = new PlayerRecord();
            }
            if (!record.Attempt(now)) return MarkVerdict.RateLimited;
            if (!admitted) return MarkVerdict.NoContact;
            if (contact.Classification != ContactClass.EnemyGround)
            {
                record.AddEffort(-2);
                return contact.Classification switch
                {
                    ContactClass.Neutral => MarkVerdict.Neutral,
                    ContactClass.Friendly => MarkVerdict.Friendly,
                    ContactClass.Decoy => MarkVerdict.Decoy,
                    _ => MarkVerdict.NoContact
                };
            }
            if (confirmed.TryGetValue(id, out int generation) && generation == contact.ObservationGeneration)
                return marks.ContainsKey(id) ? MarkVerdict.Confirmed : MarkVerdict.NoContact;
            // A full table answers NO CONTACT: Capacity here would confirm the id is a live enemy contact. The
            // faction's live MARK count is public state the feed shows without any per-id verdict.
            if (marks.Count >= MaxMarks && !marks.ContainsKey(id)) return MarkVerdict.NoContact;
            float lifetime = contact.Moving ? MovingMarkSeconds : StaticMarkSeconds;
            if (!Deadline(now, lifetime, out float expiresAt)) return MarkVerdict.NoContact;
            var mark = new SpaceMark(id, contact.X, contact.Z, contact.Moving, expiresAt, contact.Source);
            int effort = recentInput ? 1 : 0;
            var provenance = new SpaceMarkProvenance(contact.ObservationGeneration, contact.TypeId, player, effort);
            marks[id] = new MarkRecord(mark, provenance, now);
            confirmed[id] = contact.ObservationGeneration;
            record.AddEffort(effort);
            return MarkVerdict.Confirmed;
        }

        public bool TryMark(int id, float now, out SpaceMark mark)
        {
            mark = default;
            if (!TryRecord(id, now, out MarkRecord record)) return false;
            mark = record.Mark;
            return true;
        }

        public bool TryProvenance(int id, float now, out SpaceMarkProvenance provenance)
        {
            provenance = default;
            if (!TryRecord(id, now, out MarkRecord record)) return false;
            provenance = record.Provenance;
            return true;
        }

        public void Prune(float now)
        {
            if (!SpaceRules.MissionTime(now)) return;
            expired.Clear();
            foreach (var pair in reveals) if (now >= pair.Value.ExpiresAt) expired.Add(pair.Key);
            for (int i = 0; i < expired.Count; i++) RemoveReveal(expired[i]);
            expired.Clear();
            foreach (var pair in marks) if (now >= pair.Value.Mark.ExpiresAt) expired.Add(pair.Key);
            for (int i = 0; i < expired.Count; i++) marks.Remove(expired[i]);
            expired.Clear();
        }

        public void Clear()
        {
            reveals.Clear(); confirmed.Clear(); marks.Clear(); players.Clear(); expired.Clear();
            if (stateGeneration == int.MaxValue) retired = true;
            else stateGeneration++;
            // Observation ids never restart inside this object; old immutable provenance cannot match a new observation.
        }

        private bool TryRecord(int id, float now, out MarkRecord record)
        {
            record = default;
            if (!SpaceRules.MissionTime(now) || !marks.TryGetValue(id, out MarkRecord found)) return false;
            if (now < found.ConfirmedAt) return false;
            if (now >= found.Mark.ExpiresAt) { marks.Remove(id); return false; }
            record = found;
            return true;
        }

        private static bool Coordinate(float value) => SpaceRules.Finite(value) && Math.Abs(value) <= MaximumCoordinate;
        private bool RemoveReveal(int id)
        {
            confirmed.Remove(id);
            return reveals.Remove(id);
        }
        private static bool Deadline(float now, float lifetime, out float deadline)
        {
            deadline = now + lifetime;
            return SpaceRules.Finite(deadline) && deadline > now;
        }
    }
}
