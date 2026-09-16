namespace BoscaliSummer.Framework.Contracts
{
    internal readonly struct PerkView
    {
        /// <summary>Sentinel for "this perk is a branch root".</summary>
        public const byte NoPrerequisite = 255;

        /// <summary>Nothing in the way, or the grade is simply already owned.</summary>
        public const byte BlockNone = 0;

        /// <summary>The previous grade in the lane is not committed yet.</summary>
        public const byte BlockGrade = 1;

        /// <summary>This career already holds its full set of support authorisations.</summary>
        public const byte BlockCap = 2;

        /// <summary>Not enough unspent points.</summary>
        public const byte BlockPoints = 3;

        public readonly byte Id;

        /// <summary>Presentation-only heading. Carries no data-model meaning.</summary>
        public readonly string Group;

        /// <summary>Presentation-only lane inside a group; the mini-tree a row hangs off.</summary>
        public readonly string Branch;

        public readonly string Name;
        public readonly string Description;
        public readonly byte Cost;

        /// <summary>Perk that must be committed first, or <see cref="NoPrerequisite"/>.</summary>
        public readonly byte Prerequisite;

        /// <summary>
        /// Why the host would refuse this grade right now, one of the <c>Block</c> constants.
        /// The host re-checks the same rule on the unlock request.
        /// </summary>
        public readonly byte Block;

        public readonly bool Unlocked;

        /// <summary>
        /// Nothing blocks the grade and it is not owned, so the host would accept it.
        /// </summary>
        public readonly bool Affordable;

        public PerkView(
            byte id, string group, string branch, string name, string description, byte cost,
            byte prerequisite, byte block, bool unlocked, bool affordable)
        {
            Id = id;
            Group = group;
            Branch = branch;
            Name = name;
            Description = description;
            Cost = cost;
            Prerequisite = prerequisite;
            Block = block;
            Unlocked = unlocked;
            Affordable = affordable;
        }
    }

    internal interface IProgressionView
    {
        int Rank { get; }
        int Score { get; }
        int EarnedPoints { get; }
        int AvailablePoints { get; }

        /// <summary>
        /// The configured point ceiling, so a view can size its budget readout to the server's
        /// setting instead of assuming the shipped default.
        /// </summary>
        int MaximumPoints { get; }
        string Status { get; }
        bool UnlockPending { get; }

        /// <summary>Score required for each perk point, so a view can render a score-progress bar.</summary>
        int ScorePerPoint { get; }
        PerkView[] GetPerks();
        void RequestUnlock(byte perkId);

        /// <summary>
        /// Name of the perk that grants a support capability, so the support page can say what
        /// authorises an action without importing the perk catalogue.
        /// </summary>
        string PerkNameFor(string capability);

        /// <summary>
        /// Drives the snapshot poll. The client refreshes its perk state only while a view is
        /// open, so a closed panel costs no traffic and an open one can never show stale state.
        /// </summary>
        void SetViewOpen(bool open);
    }
}
