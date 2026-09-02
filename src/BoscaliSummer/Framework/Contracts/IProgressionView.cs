namespace BoscaliSummer.Framework.Contracts
{
    internal readonly struct ProgressionSkillView
    {
        public readonly byte Id;
        public readonly string Name;
        public readonly string Description;
        public readonly bool Unlocked;
        public readonly bool Available;

        public ProgressionSkillView(byte id, string name, string description, bool unlocked, bool available)
        {
            Id = id;
            Name = name;
            Description = description;
            Unlocked = unlocked;
            Available = available;
        }
    }

    internal interface IProgressionView
    {
        int Rank { get; }
        int AvailablePoints { get; }
        string Status { get; }
        ProgressionSkillView[] GetSkills();
        void RequestUnlock(byte skillId);
    }
}
