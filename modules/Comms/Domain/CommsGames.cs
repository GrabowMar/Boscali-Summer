using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.Comms.Domain
{
    /// <summary>
    /// Rock, paper, scissors. The challenger's throw stays on the host until someone accepts,
    /// so nobody can read it off the wire and pick the counter.
    /// </summary>
    internal sealed class RpsChallenge
    {
        public uint Id;
        public ulong Challenger;
        public string ChallengerName;
        public int Faction;
        public CommsChannel Channel;
        public float Expires;

        /// <summary>Host only. Peers receive -1.</summary>
        internal int Throw = -1;

        /// <summary>1 when <paramref name="a"/> beats <paramref name="b"/>, -1 when it loses, 0 on a draw.</summary>
        public static int Resolve(int a, int b)
        {
            if (a == b) return 0;
            // Rock 0 beats scissors 2, paper 1 beats rock 0, scissors 2 beats paper 1.
            return (a + 3 - b) % 3 == 1 ? 1 : -1;
        }
    }

    /// <summary>One guess in a map hunt: who, and where they clicked.</summary>
    internal struct HuntGuess
    {
        public ulong Player;
        public string Name;
        public int X;
        public int Z;
    }

    /// <summary>A settled guess: the guess plus how far off it was, in metres.</summary>
    internal struct HuntPlacing
    {
        public HuntGuess Guess;
        public float Metres;
        public int Points;
    }

    /// <summary>
    /// Map hunt: one player hides a point on the tactical map, everyone else in the audience
    /// gets one click to find it, and the closest guesses score. It is a game about reading
    /// the map, which is also the skill a ping relies on.
    /// </summary>
    internal sealed class HuntRound
    {
        public const int MaxGuesses = 32;

        /// <summary>Points for first, second and third closest.</summary>
        public static readonly int[] Awards = { 3, 2, 1 };

        /// <summary>A guess this close is a bullseye and scores one extra point.</summary>
        public const float BullseyeMetres = 1500f;

        public uint Id;
        public ulong Author;
        public string AuthorName;
        public int Faction;
        public CommsChannel Channel;
        public float Ends;

        /// <summary>Host only until the reveal.</summary>
        internal int HiddenX;
        internal int HiddenZ;

        internal readonly List<HuntGuess> Guesses = new List<HuntGuess>();

        public int GuessCount => Guesses.Count;

        public bool HasGuessed(ulong player)
        {
            for (int i = 0; i < Guesses.Count; i++)
                if (Guesses[i].Player == player) return true;
            return false;
        }

        /// <summary>One click per player, never the hider's own, and a hard ceiling per round.</summary>
        public bool TryGuess(ulong player, string name, int x, int z)
        {
            if (player == Author || HasGuessed(player) || Guesses.Count >= MaxGuesses) return false;
            Guesses.Add(new HuntGuess { Player = player, Name = name, X = x, Z = z });
            return true;
        }

        /// <summary>Every guess, closest first, with the points it earned.</summary>
        public List<HuntPlacing> Rank()
        {
            var placings = new List<HuntPlacing>(Guesses.Count);
            float hx = StrokeCodec.Restore(HiddenX), hz = StrokeCodec.Restore(HiddenZ);
            for (int i = 0; i < Guesses.Count; i++)
            {
                float dx = StrokeCodec.Restore(Guesses[i].X) - hx;
                float dz = StrokeCodec.Restore(Guesses[i].Z) - hz;
                placings.Add(new HuntPlacing { Guess = Guesses[i], Metres = (float)Math.Sqrt(dx * dx + dz * dz) });
            }
            // Stable on ties: whoever clicked first keeps the better place.
            placings.Sort((a, b) =>
            {
                int byDistance = a.Metres.CompareTo(b.Metres);
                return byDistance != 0 ? byDistance : Order(a.Guess.Player).CompareTo(Order(b.Guess.Player));
            });
            for (int i = 0; i < placings.Count; i++)
            {
                HuntPlacing placing = placings[i];
                placing.Points = (i < Awards.Length ? Awards[i] : 0) + (placing.Metres <= BullseyeMetres ? 1 : 0);
                placings[i] = placing;
            }
            return placings;
        }

        private int Order(ulong player)
        {
            for (int i = 0; i < Guesses.Count; i++)
                if (Guesses[i].Player == player) return i;
            return int.MaxValue;
        }
    }

    /// <summary>One line on the leaderboard.</summary>
    internal struct ScoreRow
    {
        public ulong Player;
        public string Name;
        public int Points;
        public int Wins;
    }

    /// <summary>
    /// The session's fun points. Nothing buys anything with them; they exist so a hunt or a
    /// duel has a table to climb. Host-owned and broadcast whole, it is small by design.
    /// </summary>
    internal sealed class CommsScoreboard
    {
        public const int MaxRows = 64;

        private readonly List<ScoreRow> rows = new List<ScoreRow>();

        public IReadOnlyList<ScoreRow> Rows => rows;

        public void Award(ulong player, string name, int points, bool win)
        {
            if (points == 0 && !win) return;
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i].Player != player) continue;
                ScoreRow row = rows[i];
                row.Name = name ?? row.Name;
                row.Points += points;
                if (win) row.Wins++;
                rows[i] = row;
                Sort();
                return;
            }
            if (rows.Count >= MaxRows) return;
            rows.Add(new ScoreRow { Player = player, Name = name, Points = points, Wins = win ? 1 : 0 });
            Sort();
        }

        /// <summary>Replace the table wholesale: what a peer does with the host's broadcast.</summary>
        public void Set(IList<ScoreRow> table)
        {
            rows.Clear();
            if (table != null)
                for (int i = 0; i < table.Count && rows.Count < MaxRows; i++) rows.Add(table[i]);
            Sort();
        }

        public int RankOf(ulong player)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Player == player) return i + 1;
            return 0;
        }

        public int PointsOf(ulong player)
        {
            for (int i = 0; i < rows.Count; i++)
                if (rows[i].Player == player) return rows[i].Points;
            return 0;
        }

        public void Clear() => rows.Clear();

        private void Sort() => rows.Sort((a, b) =>
        {
            int byPoints = b.Points.CompareTo(a.Points);
            if (byPoints != 0) return byPoints;
            int byWins = b.Wins.CompareTo(a.Wins);
            return byWins != 0 ? byWins : string.CompareOrdinal(a.Name, b.Name);
        });
    }

    /// <summary>
    /// A per-sender token bucket. Every post costs tokens and the bucket refills at a steady
    /// rate, so a burst of pings in a fight is fine and a macro spamming the map is not.
    /// </summary>
    internal sealed class TokenBucket
    {
        private struct Bucket
        {
            public float Tokens;
            public float Stamp;
        }

        private readonly Dictionary<ulong, Bucket> buckets = new Dictionary<ulong, Bucket>();
        private readonly List<ulong> stale = new List<ulong>();
        private readonly float capacity;
        private readonly float refillPerSecond;
        private readonly int maxSenders;
        private float nextPrune;

        public TokenBucket(float capacity, float refillPerSecond, int maxSenders = 128)
        {
            this.capacity = Math.Max(1f, capacity);
            this.refillPerSecond = Math.Max(0.01f, refillPerSecond);
            this.maxSenders = Math.Max(1, maxSenders);
        }

        public bool TryTake(ulong sender, float now, float cost)
        {
            Prune(now);
            if (!buckets.TryGetValue(sender, out Bucket bucket))
            {
                if (buckets.Count >= maxSenders) return false;
                bucket = new Bucket { Tokens = capacity, Stamp = now };
            }
            bucket.Tokens = Math.Min(capacity, bucket.Tokens + Math.Max(0f, now - bucket.Stamp) * refillPerSecond);
            bucket.Stamp = now;
            if (bucket.Tokens < cost)
            {
                buckets[sender] = bucket;
                return false;
            }
            bucket.Tokens -= cost;
            buckets[sender] = bucket;
            return true;
        }

        public void Clear() => buckets.Clear();

        private void Prune(float now)
        {
            if (now < nextPrune) return;
            nextPrune = now + 30f;
            stale.Clear();
            float full = capacity / refillPerSecond;
            foreach (KeyValuePair<ulong, Bucket> pair in buckets)
                if (now - pair.Value.Stamp > full) stale.Add(pair.Key);
            for (int i = 0; i < stale.Count; i++) buckets.Remove(stale[i]);
        }
    }
}
