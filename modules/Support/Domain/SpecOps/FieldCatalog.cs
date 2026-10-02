using System;

namespace BoscaliSummer.Modules.Support.Domain.SpecOps
{
    /// <summary>Where a team is in its cycle. Wire-stable.</summary>
    internal enum TeamState : byte
    {
        Unformed = 0,
        Ready = 1,
        EnRoute = 2,
        OnTask = 3,
        Holding = 4,
        Recovering = 5,
        Deciding = 6
    }

    /// <summary>Host-authoritative choice after arrival; values are carried by OpsCommand 24.</summary>
    internal enum SpecOpsDirective : byte { Execute = 0, Extract = 1, Observe = 2, Advance = 3, Conceal = 4 }

    /// <summary>Team missions; wire-stable values also identify the post left by success.</summary>
    internal enum FieldMission : byte
    {
        Recon = 0,
        Sabotage = 1,
        Seize = 2,
        Steal = 3
    }

    /// <summary>What an objective is on the real map. Wire-stable.</summary>
    internal enum ObjectiveKind : byte
    {
        None = 0,
        Airfield = 1,
        Outpost = 2,
        Town = 3,
        AirDefence = 4
    }

    /// <summary>Map abilities a held post grants. FORTIFY stays the existing support action.</summary>
    internal enum FieldAbility : byte
    {
        Spot = 0,
        Suppress = 1,
        Skywatch = 2,
        Eavesdrop = 3,
        Hunt = 4
    }

    /// <summary>How a team's last mission ended. Wire-stable.</summary>
    internal enum MissionOutcome : byte
    {
        None = 0,
        Success = 1,
        Failed = 2,
        Lost = 3,
        Recalled = 4,
        NoBuildings = 5,
        Extracted = 6
    }

    /// <summary>Why the host refused a detachment order: <c>SupportResult.SpecOpsRefused + denial</c>.</summary>
    internal enum SpecOpsDenial : byte
    {
        None = 0,
        Disabled = 1,
        BadTeam = 2,
        Unformed = 3,
        AlreadyFormed = 4,
        Busy = 5,
        NotDeployed = 6,
        StaleObjective = 7,
        WrongObjective = 8,
        NoRadars = 9,
        SeizeUnavailable = 10,
        ObjectiveTaken = 11,
        BadMission = 12,
        BadDirective = 13,
        NotAtDecision = 14,
        Preparing = 15,
        Exposed = 16,
        OrderCoolingDown = 17,
        StaleOrder = 18,
        PostLimit = 19
    }

    /// <summary>
    /// The numbers behind every mission and ability, in one table the host, the MFD and the desk
    /// share. Operator quality widens effects; live pressure raises exposure. Posts have finite charges. Pure: nothing here
    /// touches the game.
    /// </summary>
    internal static class FieldCatalog
    {
        public const int MissionCount = 4;
        public const int AbilityCount = 5;
        public const int MaxRank = 3;
        public const int WinsPerRank = 2;

        /// <summary>Hostile ground units within this radius of an objective are its threat.</summary>
        public const float ThreatRadius = 2000f;

        /// <summary>Hostile ground radars within this radius make an objective sabotageable.</summary>
        public const float RadarRadius = 2500f;

        /// <summary>Hostile radars closer than this belong to one air-defence objective.</summary>
        public const float AirDefenceCluster = 3000f;

        public const float ScoutSeconds = 600f;
        public const float RecoverSeconds = 60f;
        public const float FailedRecoverSeconds = 120f;
        public const float ExtractSeconds = 20f;
        public const float DecisionSeconds = 240f;
        public const float OrderSeconds = 6f;
        public const float FastRouteSeconds = 3f;
        public const float CoveredRouteSeconds = 7f;
        public const int MinimumPreparation = 60;
        public const int MaximumExecuteExposure = 75;
        public const int MaximumPostCharges = 3;
        public const int MaximumHeldPosts = 2;
        public const float PostSeconds = 180f;
        public const float MinimumTravel = 12f;
        public const float MaximumTravel = 40f;
        public const float DefaultTravel = 20f;
        public const float OpRefreshSeconds = 30f;
        public const float SeizeRadius = 1500f;

        /// <summary>Base allocation to raise an empty slot, before the host's price multipliers.</summary>
        public const float RaiseCost = 1000f;

        public static bool KnownMission(byte value) => value < MissionCount;

        public static bool KnownKind(byte value) => value >= (byte)ObjectiveKind.Airfield && value <= (byte)ObjectiveKind.AirDefence;

        public static float TaskSeconds(FieldMission mission) => 8f;

        /// <summary>Base allocation for a mission, before the host's price multipliers.</summary>
        public static float MissionCost(FieldMission mission) =>
            mission == FieldMission.Recon ? 400f : mission == FieldMission.Sabotage ? 700f :
            mission == FieldMission.Steal ? 600f : 900f;

        public static float AbilityCost(FieldAbility ability) => ability == FieldAbility.Spot ? 250f :
            ability == FieldAbility.Skywatch ? 350f : ability == FieldAbility.Eavesdrop ? 300f :
            ability == FieldAbility.Hunt ? 650f : 500f;

        public static float AbilityRecharge(FieldAbility ability) => ability == FieldAbility.Spot ? 45f :
            ability == FieldAbility.Skywatch ? 60f : ability == FieldAbility.Eavesdrop ? 60f :
            ability == FieldAbility.Hunt ? 120f : 90f;

        /// <summary>The post that grants an ability.</summary>
        public static FieldMission PostFor(FieldAbility ability) =>
            ability == FieldAbility.Spot || ability == FieldAbility.Skywatch ? FieldMission.Recon :
            ability == FieldAbility.Eavesdrop ? FieldMission.Steal : FieldMission.Sabotage;

        /// <summary>Legacy radial display values; current delivery authority is a controlled OPS sector.</summary>
        public static float PostReach(FieldMission post) =>
            post == FieldMission.Recon ? 6000f : post == FieldMission.Sabotage ? 5000f :
            post == FieldMission.Steal ? 5000f : 3000f;

        public static float HoldSeconds(int rank) => PostSeconds;

        /// <summary>Travel from the nearest owned airbase: 10 s plus 1 s per kilometre, bounded to 12–40 s.</summary>
        public static float TravelSeconds(float metres)
        {
            if (float.IsNaN(metres) || float.IsInfinity(metres) || metres < 0f) return DefaultTravel;
            return Math.Max(MinimumTravel, Math.Min(MaximumTravel, 10f + metres / 1000f));
        }

        public static int Pressure(int threat, int radars) =>
            (int)Math.Min(30L, (long)Math.Max(0, threat) * 2 + (long)Math.Max(0, radars) * 3);

        public static int Quality(int preparation, int intel, int exposure) =>
            preparation >= 95 && intel >= 80 && exposure <= 30 ? 3 :
            preparation >= 80 && intel >= 60 && exposure <= 45 ? 2 :
            preparation >= 70 && intel >= 40 && exposure <= 60 ? 1 : 0;

        public static int PostCharges(int quality) => Math.Min(MaximumPostCharges, 1 + Math.Max(0, quality));
        public static int RequiredQuality(FieldAbility ability) => ability == FieldAbility.Hunt ? 2 :
            ability == FieldAbility.Skywatch ? 1 : 0;

        public static string PostSummary(FieldMission mission, int quality) =>
            mission == FieldMission.Recon ? quality >= 1 ? "SPOT + SKYWATCH" : "SPOT" :
            mission == FieldMission.Sabotage ? quality >= 2 ? "SUPPRESS + HUNT" : "SUPPRESS" :
            mission == FieldMission.Steal ? quality >= 2 ? "EAVESDROP + BLACK MARKET" : "EAVESDROP" : "SAFEHOUSE / FORTIFY SECTOR";

        public static bool Allowed(FieldMission mission, ObjectiveKind kind) =>
            kind != ObjectiveKind.None && (mission != FieldMission.Seize || kind != ObjectiveKind.AirDefence);

        public static int RankFor(int wins) => Math.Min(MaxRank, Math.Max(0, wins) / WinsPerRank);

        /// <summary>Successes still needed for the next rank; 0 at ELITE.</summary>
        public static int WinsToNext(int wins) =>
            RankFor(wins) >= MaxRank ? 0 : (RankFor(wins) + 1) * WinsPerRank - Math.Max(0, wins);

        // ---- Effect sizes ----------------------------------------------------------------------

        public static float ReconRadius(int rank) => 3000f + 500f * Rank(rank);
        public static float SabotageRadius(int rank) => 2500f + 500f * Rank(rank);
        public static float SabotageSeconds(int rank) => 120f + 30f * Rank(rank);
        public static int SeizeBuildings(int rank) => 1 + Rank(rank);
        public static float SpotRadius(int rank) => 2500f + 500f * Rank(rank);
        public static float SuppressRadius(int rank) => 2000f + 500f * Rank(rank);
        public static float SuppressSeconds(int rank) => 30f + 10f * Rank(rank);
        public static float SkywatchRadius(int rank) => 3500f + 500f * Rank(rank);
        public static float EavesdropRadius(int rank) => 3000f + 500f * Rank(rank);
        public static float HuntRadius(int rank) => 2500f + 500f * Rank(rank);
        public static float HuntSeconds(int rank) => 20f + 10f * Rank(rank);
        public static float StealIntel(int rank) => 40f + 20f * Rank(rank);

        private static int Rank(int rank) => Math.Max(0, Math.Min(MaxRank, rank));
    }
}
