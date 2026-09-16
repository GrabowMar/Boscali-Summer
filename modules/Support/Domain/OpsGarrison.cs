using System;

namespace BoscaliSummer.Features.Support.Domain
{
    /// <summary>Wire-stable doctrine bytes. Order is the snapshot's level-array order.</summary>
    internal enum GarrisonUpgradeId : byte
    {
        FortificationDoctrine = 0,
        InsertionRigging = 1
    }

    internal readonly struct GarrisonUpgradeInfo
    {
        public readonly GarrisonUpgradeId Id;
        public readonly string Code;
        public readonly string Name;
        public readonly string Summary;

        /// <summary>What holding rank n buys; index 0 describes rank I.</summary>
        public readonly string[] Ranks;

        /// <summary>SOF tokens to reach rank n; index 0 is unused and zero.</summary>
        public readonly int[] Costs;

        public GarrisonUpgradeInfo(GarrisonUpgradeId id, string code, string name, string summary,
                                   string[] ranks, int[] costs)
        {
            Id = id;
            Code = code;
            Name = name;
            Summary = summary;
            Ranks = ranks;
            Costs = costs;
        }
    }

    /// <summary>
    /// The faction's base of operations: the standing improvements bought for the spec-ops
    /// detachment out of the SPEC OPS token reserve. A rank raises the ground-force effect
    /// one step — shells per fortification order, camps per fast-rope insertion — and the
    /// host is the only writer. Every value is bounded: two tracks, three ranks, ranks
    /// clamped on mirror.
    /// </summary>
    internal sealed class OpsGarrison
    {
        public const int MaxRank = 3;

        public static readonly GarrisonUpgradeInfo[] Upgrades =
        {
            new GarrisonUpgradeInfo(GarrisonUpgradeId.FortificationDoctrine, "FTD", "FORTIFICATION DOCTRINE",
                "Combat engineers harden a secured zone: every order occupies more positions.",
                new[] { "Fortify orders occupy 2 defensive positions.",
                        "Fortify orders occupy 3 defensive positions.",
                        "Fortify orders occupy 4 defensive positions." },
                new[] { 0, 2, 3, 4 }),
            new GarrisonUpgradeInfo(GarrisonUpgradeId.InsertionRigging, "RGD", "INSERTION RIGGING",
                "Rappel masters split a fast-rope stick across more encampments.",
                new[] { "Fast-rope insertions establish 2 encampments.",
                        "Fast-rope insertions establish 3 encampments.",
                        "Fast-rope insertions establish 4 encampments." },
                new[] { 0, 2, 3, 4 })
        };

        public static int UpgradeCount => Upgrades.Length;

        private readonly int[] ranks = new int[Upgrades.Length];

        public static GarrisonUpgradeInfo Info(GarrisonUpgradeId id) =>
            (int)id < Upgrades.Length ? Upgrades[(int)id] : default;

        /// <summary>UNTRAINED, or RANK I..III against the track width. Rows and host replies share it.</summary>
        public static string RankLabel(int rank) =>
            rank <= 0 ? "UNTRAINED" : "RANK " + Roman(Math.Min(rank, MaxRank)) + "/" + Roman(MaxRank);

        /// <summary>The effect of holding a rank in six words, for row status lines and host replies.</summary>
        public static string EffectLabel(GarrisonUpgradeId id, int rank)
        {
            int units = 1 + Math.Clamp(rank, 0, MaxRank);
            return id == GarrisonUpgradeId.FortificationDoctrine
                ? units + (units == 1 ? " POSITION/ORDER" : " POSITIONS/ORDER")
                : units + (units == 1 ? " CAMP/INSERTION" : " CAMPS/INSERTION");
        }

        private static string Roman(int rank) => rank == 1 ? "I" : rank == 2 ? "II" : "III";

        /// <summary>Rank held, 0 for untrained. An unknown byte reads as untrained.</summary>
        public int Rank(GarrisonUpgradeId id) =>
            (int)id < ranks.Length ? ranks[(int)id] : 0;

        public bool CanUpgrade(GarrisonUpgradeId id) =>
            (int)id < ranks.Length && ranks[(int)id] < MaxRank;

        /// <summary>SOF tokens for the next rank; 0 at max.</summary>
        public int NextCost(GarrisonUpgradeId id) =>
            CanUpgrade(id) ? Info(id).Costs[ranks[(int)id] + 1] : 0;

        /// <summary>Authority-free rank change; the host prices and charges separately.</summary>
        public bool TryUpgrade(GarrisonUpgradeId id)
        {
            if (!CanUpgrade(id)) return false;
            ranks[(int)id]++;
            return true;
        }

        /// <summary>Positions one accepted zone fortification occupies.</summary>
        public int FortificationShells => 1 + ranks[(int)GarrisonUpgradeId.FortificationDoctrine];

        /// <summary>Encampments one fast-rope insertion establishes.</summary>
        public int InsertionCamps => 1 + ranks[(int)GarrisonUpgradeId.InsertionRigging];

        /// <summary>Snapshot mirror. Missing or hostile bytes clamp to 0..MaxRank.</summary>
        public void Mirror(byte[] levels)
        {
            for (int i = 0; i < ranks.Length; i++)
                ranks[i] = levels != null && i < levels.Length ? Math.Min(MaxRank, (int)levels[i]) : 0;
        }

        public void Clear() => Array.Clear(ranks, 0, ranks.Length);
    }
}
