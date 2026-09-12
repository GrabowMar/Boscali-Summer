namespace BoscaliSummer.Framework.Contracts
{
    internal readonly struct PilotView
    {
        public readonly string Name, Callsign, Status, Background;
        public readonly bool Respawns;
        public readonly int Deaths, Generation;
        public PilotView(string name, string callsign, string status, bool respawns,
            int deaths, int generation, string background = "")
        { Name = name; Callsign = callsign; Status = status; Respawns = respawns;
          Deaths = deaths; Generation = generation; Background = background; }
    }

    internal readonly struct EnemyWingView
    {
        public readonly string Symbol, WingName, AceName, Skill, Status, TargetName;
        public readonly int Tier, MembersAlive, MemberCount, Returns, AbilityMask;
        public EnemyWingView(string symbol, string wingName, string aceName, int tier,
            string skill, string status, int membersAlive, int memberCount, string targetName, int returns, int abilityMask = 0)
        { Symbol = symbol; WingName = wingName; AceName = aceName; Tier = tier; Skill = skill;
          Status = status; MembersAlive = membersAlive; MemberCount = memberCount;
          TargetName = targetName; Returns = returns; AbilityMask = abilityMask; }
    }

    /// <summary>Server-derived pilot career and observed ace encounters; contains no orders or mutable state.</summary>
    internal interface ISquadView
    {
        PilotView Pilot { get; }
        bool HuntActive { get; }
        string Status { get; }
        int EnemyWingCount { get; }
        int ActiveEnemyWingIndex { get; }
        int ActiveHuntId { get; }
        EnemyWingView GetEnemyWing(int index);
        int GetBonusPoints(ulong playerId);
        int GetPilotGeneration(ulong playerId);
        int GetScoreOrigin(ulong playerId);
    }
}
