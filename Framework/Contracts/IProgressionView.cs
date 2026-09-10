namespace BoscaliSummer.Framework.Contracts
{
    internal readonly struct PerkView
    {
        public readonly byte Id;

        /// <summary>Presentation-only heading. Carries no data-model meaning.</summary>
        public readonly string Group;

        public readonly string Name;
        public readonly string Description;
        public readonly byte Cost;
        public readonly bool Unlocked;

        /// <summary>The player has enough unspent points to buy this perk right now.</summary>
        public readonly bool Affordable;

        public PerkView(
            byte id, string group, string name, string description, byte cost,
            bool unlocked, bool affordable)
        {
            Id = id;
            Group = group;
            Name = name;
            Description = description;
            Cost = cost;
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
