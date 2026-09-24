using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.Comms.Domain
{
    /// <summary>
    /// One vote in progress or settled. The host keeps who voted for what; peers only ever
    /// see the tally, plus the one choice they made themselves.
    /// </summary>
    internal sealed class CommsPoll
    {
        public const int MinOptions = 2;
        public const int MaxOptions = 4;

        public uint Id;
        public ulong Author;
        public string AuthorName;
        public int Faction;
        public CommsChannel Channel;
        public string Question;
        public string[] Options;
        public int[] Tally;
        public float Closes;
        public bool Closed;

        /// <summary>This peer's own choice, or -1. Kept by the peer, never sent back down.</summary>
        public int LocalVote = -1;

        /// <summary>Host only: every ballot, so a changed mind moves one vote instead of adding one.</summary>
        internal readonly Dictionary<ulong, int> Ballots = new Dictionary<ulong, int>();

        public int Total
        {
            get
            {
                int total = 0;
                if (Tally != null) for (int i = 0; i < Tally.Length; i++) total += Tally[i];
                return total;
            }
        }

        /// <summary>The winning option, or -1 with no votes or a tie at the top.</summary>
        public int Leader
        {
            get
            {
                if (Tally == null) return -1;
                int best = -1, bestVotes = 0;
                bool tie = false;
                for (int i = 0; i < Tally.Length; i++)
                {
                    if (Tally[i] > bestVotes)
                    {
                        best = i;
                        bestVotes = Tally[i];
                        tie = false;
                    }
                    else if (Tally[i] == bestVotes && bestVotes > 0)
                    {
                        tie = true;
                    }
                }
                return tie ? -1 : best;
            }
        }

        /// <summary>The settled line the feed prints when the poll closes.</summary>
        public string Verdict()
        {
            int total = Total;
            if (total == 0) return "NO VOTES";
            int leader = Leader;
            if (leader < 0) return "TIE (" + total + " VOTES)";
            return Options[leader] + " " + Tally[leader] + "/" + total;
        }
    }

    /// <summary>
    /// The host's open polls and the rules on them: a question and 2–4 distinct options, one
    /// open poll per author, a small ceiling on open polls overall, and ballots only from the
    /// audience that can see the poll.
    /// </summary>
    internal sealed class CommsPollBook
    {
        public const int MaxOpen = 4;

        private readonly List<CommsPoll> open = new List<CommsPoll>(MaxOpen);

        public IReadOnlyList<CommsPoll> Open => open;

        public CommsPoll Find(uint id)
        {
            for (int i = 0; i < open.Count; i++)
                if (open[i].Id == id) return open[i];
            return null;
        }

        public bool HasOpenBy(ulong author)
        {
            for (int i = 0; i < open.Count; i++)
                if (open[i].Author == author) return true;
            return false;
        }

        /// <summary>Clean and check a proposed poll. Null question or options mean it is refused.</summary>
        public static bool TryShape(string question, string[] options, out string cleanQuestion, out string[] cleanOptions)
        {
            cleanQuestion = CommsText.Clean(question, CommsText.MaxQuestion);
            cleanOptions = null;
            if (cleanQuestion.Length == 0 || options == null) return false;

            var result = new List<string>(CommsPoll.MaxOptions);
            for (int i = 0; i < options.Length && result.Count < CommsPoll.MaxOptions; i++)
            {
                string option = CommsText.Clean(options[i], CommsText.MaxOption);
                if (option.Length == 0 || result.Contains(option)) continue;
                result.Add(option);
            }
            if (result.Count < CommsPoll.MinOptions) return false;
            cleanOptions = result.ToArray();
            return true;
        }

        public string Refusal(ulong author)
        {
            if (HasOpenBy(author)) return "YOU ALREADY HAVE A POLL OPEN";
            if (open.Count >= MaxOpen) return "TOO MANY POLLS OPEN";
            return null;
        }

        public void Add(CommsPoll poll)
        {
            if (poll == null) return;
            poll.Tally = poll.Tally ?? new int[poll.Options.Length];
            open.Add(poll);
        }

        /// <summary>
        /// Record or move one ballot. Returns false when the option is out of range, the poll
        /// is closed, or the voter's ballot already says exactly this.
        /// </summary>
        public static bool Vote(CommsPoll poll, ulong voter, int option)
        {
            if (poll == null || poll.Closed || option < 0 || option >= poll.Options.Length) return false;
            if (poll.Ballots.TryGetValue(voter, out int previous))
            {
                if (previous == option) return false;
                poll.Tally[previous] = Math.Max(0, poll.Tally[previous] - 1);
            }
            poll.Ballots[voter] = option;
            poll.Tally[option]++;
            return true;
        }

        public bool Close(uint id)
        {
            CommsPoll poll = Find(id);
            if (poll == null) return false;
            poll.Closed = true;
            open.Remove(poll);
            return true;
        }

        /// <summary>Close every poll whose time is up; report them so the host can announce each.</summary>
        public void Expire(float now, List<CommsPoll> closed)
        {
            for (int i = open.Count - 1; i >= 0; i--)
            {
                if (open[i].Closes > now) continue;
                open[i].Closed = true;
                closed?.Add(open[i]);
                open.RemoveAt(i);
            }
        }

        public void Clear() => open.Clear();
    }
}
