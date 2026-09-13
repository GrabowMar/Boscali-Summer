using System;

namespace BoscaliSummer.Features.Support.Runtime
{
    internal enum FacilityId : byte
    {
        Sigint = 0,
        Crypto = 1,
        Disrupt = 2,
        Ew = 3
    }

    /// <summary>Wire-stable cyber operation bytes. Each maps to one facility tier.</summary>
    internal enum HackKind : byte
    {
        Ping = 0,
        Track = 1,
        Blackout = 2,
        Ghost = 3,
        Spoof = 4
    }

    internal readonly struct FacilityInfo
    {
        public readonly FacilityId Id;
        public readonly string Code;
        public readonly string Name;
        public readonly string Summary;
        public readonly string[] Levels;
        public readonly float[] Costs;
        public readonly FacilityId Prerequisite;
        public readonly byte PrerequisiteLevel;

        public FacilityInfo(FacilityId id, string code, string name, string summary,
                            string[] levels, float[] costs, FacilityId prerequisite = FacilityId.Sigint,
                            byte prerequisiteLevel = 0)
        {
            Id = id;
            Code = code;
            Name = name;
            Summary = summary;
            Levels = levels;
            Costs = costs;
            Prerequisite = prerequisite;
            PrerequisiteLevel = prerequisiteLevel;
        }
    }

    /// <summary>Everything the facility levels bend: hack reach, duration, cost and cooldown.</summary>
    internal readonly struct InfoPowers
    {
        public readonly int Sigint;
        public readonly int Crypto;
        public readonly int Disrupt;
        public readonly int Ew;

        public InfoPowers(int sigint, int crypto, int disrupt, int ew)
        {
            Sigint = sigint;
            Crypto = crypto;
            Disrupt = disrupt;
            Ew = ew;
        }

        public int Tier => Sigint + Crypto + Disrupt + Ew;

        public bool Has(HackKind kind)
        {
            switch (kind)
            {
                case HackKind.Ping: return Sigint >= 1;
                case HackKind.Track: return Sigint >= 2;
                case HackKind.Blackout: return Disrupt >= 1;
                case HackKind.Ghost: return Ew >= 1;
                case HackKind.Spoof: return Ew >= 2;
                default: return false;
            }
        }

        public float RevealRadius => 3500f + 1500f * Sigint;
        public float TrackRadius => 8000f + 2500f * Sigint;
        public float TrackDuration => 12f + 4f * Sigint;
        public float JamRadius => 5000f + 2500f * Disrupt;
        public float JamStrength => 600f + 200f * Disrupt;
        public float DeceptionDuration => 12f + 5f * Ew;
        public float CooldownScale => Math.Max(0.5f, 1f - 0.15f * Crypto);
        public float CostScale => Math.Max(0.5f, 1f - 0.12f * Crypto);
    }

    /// <summary>
    /// One faction's cyber infrastructure. Levels are bought with allocation during a
    /// mission and never decay; the catalogue and scaling live here so the panel, the host
    /// and the pure tests all agree on one table.
    /// </summary>
    internal sealed class InfoNetwork
    {
        public const int MaxLevel = 3;

        public static readonly FacilityInfo[] Facilities =
        {
            new FacilityInfo(FacilityId.Sigint, "SIG", "SIGINT ARRAY",
                "Signals collection. Feeds every operation.",
                new[]
                {
                    "Unlocks PING SWEEP.",
                    "Unlocks TRACK UPLINK.",
                    "Wider, longer sweeps."
                },
                new[] { 0f, 450f, 850f, 1400f }),
            new FacilityInfo(FacilityId.Crypto, "CRY", "CRYPTO FARM",
                "Codebreaking compute. Cheaper, faster hacks.",
                new[]
                {
                    "-12% cost · -15% cooldown",
                    "-24% cost · -30% cooldown",
                    "-36% cost · -45% cooldown"
                },
                new[] { 0f, 550f, 1000f, 1600f }, FacilityId.Sigint, 1),
            new FacilityInfo(FacilityId.Disrupt, "C2D", "C2 DISRUPTOR",
                "Offensive electronic attack on hostile sensors.",
                new[]
                {
                    "Unlocks RADAR BLACKOUT.",
                    "Wider, stronger jamming.",
                    "Theater-grade jamming."
                },
                new[] { 0f, 650f, 1100f, 1700f }, FacilityId.Crypto, 1),
            new FacilityInfo(FacilityId.Ew, "EWD", "EW DIVISION",
                "Deception warfare: falsify enemy tracks.",
                new[]
                {
                    "Unlocks GHOST SHIELD.",
                    "Unlocks SPOOF CONTACTS.",
                    "Longer deception windows."
                },
                new[] { 0f, 600f, 1050f, 1650f }, FacilityId.Sigint, 1)
        };

        private readonly int[] levels = new int[4];

        public int Level(FacilityId id) => levels[(int)id];
        public InfoPowers Powers => new InfoPowers(levels[0], levels[1], levels[2], levels[3]);

        public static FacilityInfo Facility(FacilityId id) => Facilities[(int)id];

        public float UpgradeCost(FacilityId id)
        {
            int level = Level(id);
            if (level >= MaxLevel) return 0f;
            return Facility(id).Costs[level + 1];
        }

        public bool CanUpgrade(FacilityId id)
        {
            int level = Level(id);
            if (level >= MaxLevel) return false;
            FacilityInfo facility = Facility(id);
            return facility.PrerequisiteLevel == 0 ||
                   Level(facility.Prerequisite) >= facility.PrerequisiteLevel;
        }

        /// <summary>Requirement copy for a locked build, or null when the build is open.</summary>
        public string Requirement(FacilityId id)
        {
            int level = Level(id);
            if (level >= MaxLevel) return "MAXIMUM LEVEL";
            FacilityInfo facility = Facility(id);
            if (CanUpgrade(id)) return null;
            return "REQUIRES " + Facility(facility.Prerequisite).Name + " LV" + facility.PrerequisiteLevel;
        }

        /// <summary>Authority-free level change; the host checks cost and charges separately.</summary>
        public bool TryUpgrade(FacilityId id)
        {
            if (!CanUpgrade(id)) return false;
            levels[(int)id]++;
            return true;
        }

        /// <summary>Snapshot mirror; a client never mutates past what the host sent.</summary>
        public void Mirror(byte sigint, byte crypto, byte disrupt, byte ew)
        {
            levels[0] = Clamp(sigint);
            levels[1] = Clamp(crypto);
            levels[2] = Clamp(disrupt);
            levels[3] = Clamp(ew);
        }

        public void Clear()
        {
            levels[0] = levels[1] = levels[2] = levels[3] = 0;
        }

        private static int Clamp(byte level) => Math.Max(0, Math.Min(MaxLevel, (int)level));
    }

    /// <summary>Names, base prices and gates for the five cyber operations.</summary>
    internal static class CyberCatalog
    {
        public static readonly HackKind[] All =
        {
            HackKind.Ping, HackKind.Track, HackKind.Blackout, HackKind.Ghost, HackKind.Spoof
        };

        public static string Code(HackKind kind)
        {
            switch (kind)
            {
                case HackKind.Ping: return "PNG";
                case HackKind.Track: return "TRK";
                case HackKind.Blackout: return "C2B";
                case HackKind.Ghost: return "GST";
                case HackKind.Spoof: return "SPF";
                default: return "HACK";
            }
        }

        public static string Name(HackKind kind)
        {
            switch (kind)
            {
                case HackKind.Ping: return "PING SWEEP";
                case HackKind.Track: return "TRACK UPLINK";
                case HackKind.Blackout: return "RADAR BLACKOUT";
                case HackKind.Ghost: return "GHOST SHIELD";
                case HackKind.Spoof: return "SPOOF CONTACTS";
                default: return "CYBER OPERATION";
            }
        }

        public static string Description(HackKind kind)
        {
            switch (kind)
            {
                case HackKind.Ping:
                    return "Reveal ground contacts in a target area.";
                case HackKind.Track:
                    return "Stream air tracks for the operation window.";
                case HackKind.Blackout:
                    return "Jam hostile sensors; friends unaffected.";
                case HackKind.Ghost:
                    return "Hostile tracking of your aircraft goes stale.";
                case HackKind.Spoof:
                    return "Feed the enemy a false track position.";
                default: return string.Empty;
            }
        }

        public static float BaseCost(HackKind kind)
        {
            switch (kind)
            {
                case HackKind.Ping: return 400f;
                case HackKind.Track: return 650f;
                case HackKind.Blackout: return 900f;
                case HackKind.Ghost: return 700f;
                case HackKind.Spoof: return 900f;
                default: return 500f;
            }
        }

        public static FacilityId Facility(HackKind kind)
        {
            switch (kind)
            {
                case HackKind.Ping:
                case HackKind.Track: return FacilityId.Sigint;
                case HackKind.Blackout: return FacilityId.Disrupt;
                default: return FacilityId.Ew;
            }
        }

        public static byte RequiredLevel(HackKind kind)
        {
            switch (kind)
            {
                case HackKind.Track: return 2;
                case HackKind.Spoof: return 2;
                default: return 1;
            }
        }
    }
}
