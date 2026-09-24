using System;

namespace BoscaliSummer.Features.Support.Domain.SpecOps
{
    /// <summary>Where a team is in its cycle. Wire-stable.</summary>
    internal enum TeamState : byte
    {
        Unformed = 0,
        Ready = 1,
        EnRoute = 2,
        OnTask = 3,
        Holding = 4,
        Recovering = 5
    }

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
        NoBuildings = 5
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
        BadMission = 12
    }

    /// <summary>
    /// The numbers behind every mission and ability, in one table the host, the MFD and the desk
    /// share. Rank widens effects and lengthens posts; threat lowers the odds. Pure: nothing here
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
        public const float MinimumTravel = 30f;
        public const float MaximumTravel = 120f;
        public const float DefaultTravel = 60f;
        public const float OpRefreshSeconds = 30f;
        public const float SeizeRadius = 1500f;

        /// <summary>Base allocation to raise an empty slot, before the host's price multipliers.</summary>
        public const float RaiseCost = 1000f;

        public static bool KnownMission(byte value) => value < MissionCount;

        public static bool KnownKind(byte value) => value >= (byte)ObjectiveKind.Airfield && value <= (byte)ObjectiveKind.AirDefence;

        public static int BaseChance(FieldMission mission) =>
            mission == FieldMission.Recon ? 85 : mission == FieldMission.Sabotage ? 70 :
            mission == FieldMission.Steal ? 75 : 65;

        public static int LossBase(FieldMission mission) =>
            mission == FieldMission.Recon ? 4 : mission == FieldMission.Sabotage ? 8 :
            mission == FieldMission.Steal ? 6 : 10;

        public static float TaskSeconds(FieldMission mission) =>
            mission == FieldMission.Recon ? 30f : mission == FieldMission.Sabotage ? 45f :
            mission == FieldMission.Steal ? 45f : 60f;

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

        /// <summary>How far from a held post its ability (or FORTIFY, for a safehouse) may be used.</summary>
        public static float PostReach(FieldMission post) =>
            post == FieldMission.Recon ? 6000f : post == FieldMission.Sabotage ? 5000f :
            post == FieldMission.Steal ? 5000f : 3000f;

        public static float HoldSeconds(int rank) => 300f + 60f * Rank(rank);

        /// <summary>Travel from the nearest owned airbase: 20 s plus 2 s per kilometre, 30–120 s.</summary>
        public static float TravelSeconds(float metres)
        {
            if (float.IsNaN(metres) || float.IsInfinity(metres) || metres < 0f) return DefaultTravel;
            return Math.Max(MinimumTravel, Math.Min(MaximumTravel, 20f + metres / 1000f * 2f));
        }

        public static bool Allowed(FieldMission mission, ObjectiveKind kind) =>
            kind != ObjectiveKind.None && (mission != FieldMission.Seize || kind != ObjectiveKind.AirDefence);

        // ---- Odds ------------------------------------------------------------------------------

        public static int SuccessChance(FieldMission mission, int rank, int threat, bool scouted)
        {
            int value = BaseChance(mission) + 8 * Rank(rank) + (scouted ? 10 : 0) - Math.Min(40, 3 * Math.Max(0, threat));
            return Math.Max(5, Math.Min(95, value));
        }

        /// <summary>The probability the team is lost, never more than the failure share.</summary>
        public static int LossChance(FieldMission mission, int rank, int threat, int success)
        {
            int value = Math.Max(1, Math.Min(40, LossBase(mission) + 2 * Math.Max(0, threat) - 3 * Rank(rank)));
            return Math.Max(0, Math.Min(100 - Math.Max(0, Math.Min(100, success)), value));
        }

        /// <summary>One roll in [0,1): below success succeeds, the top loss share loses the team.</summary>
        public static MissionOutcome Resolve(int success, int loss, double roll)
        {
            if (double.IsNaN(roll) || roll < 0.0) roll = 0.0;
            if (roll >= 1.0) roll = 0.999999;
            if (roll < success / 100.0) return MissionOutcome.Success;
            if (roll >= 1.0 - loss / 100.0) return MissionOutcome.Lost;
            return MissionOutcome.Failed;
        }

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
        public static float StealIntel(int rank) => 100f + 25f * Rank(rank);

        private static int Rank(int rank) => Math.Max(0, Math.Min(MaxRank, rank));
    }
}
