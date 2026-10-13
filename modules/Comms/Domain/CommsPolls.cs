using System.Collections.Generic;

namespace BoscaliSummer.Modules.Comms.Domain
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

        public void Add(CommsPoll poll)
        {
            if (poll == null) return;
            poll.Tally = poll.Tally ?? new int[poll.Options.Length];
            open.Add(poll);
        }

        public void Clear() => open.Clear();
    }
}
