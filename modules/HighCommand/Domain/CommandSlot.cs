namespace BoscaliSummer.Features.HighCommand.Domain
{
    internal enum CommanderStatus : byte
    {
        Active = 0,
        InTransit = 1,
        Disrupted = 2,
        Kia = 3,
    }

    /// <summary>
    /// One fixed staff post. Slots are generated once per mission and never re-ordered:
    /// people are promoted between slots, the slot itself owns the role, site and rank.
    /// The destroyed post is the slot's asset, so succession is a personnel problem, not
    /// a tree surgery problem.
    /// </summary>
    internal sealed class CommandSlot
    {
        public int Id;
        public int ParentId;
        public int Tier;
        public string Role;
        public int SiteIndex;
        public string SiteName;
        public CommandPerson Person;
        public CommanderStatus Status;
        public float StatusUntil;

        /// <summary>0 = nobody's kill list; faction id + 1 while marked.</summary>
        public byte MarkedByFaction;

        public bool Alive => Status != CommanderStatus.Kia;

        public CommandSlot(int id, int parentId, int tier, string role, int siteIndex, string siteName)
        {
            Id = id; ParentId = parentId; Tier = tier; Role = role;
            SiteIndex = siteIndex; SiteName = siteName ?? "FIELD HQ";
            StatusUntil = 0f;
        }
    }
}
