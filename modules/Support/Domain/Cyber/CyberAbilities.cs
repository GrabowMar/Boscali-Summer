using System;

namespace BoscaliSummer.Features.Support.Domain.Cyber
{
    /// <summary>Wire-stable cyber operation bytes. The eight map abilities.</summary>
    internal enum HackKind : byte
    {
        Ping = 0,
        Track = 1,
        Blackout = 2,
        Ghost = 3,
        Spoof = 4,

        /// <summary>Sweep for hostile ground vehicles; revealed armor is worth a hijack.</summary>
        Scan = 5,

        /// <summary>Seize hostile ground vehicles: they halt, hold and go blind for the window.</summary>
        Hijack = 6,

        /// <summary>Detonate hostile ground vehicles in a tight radius.</summary>
        Overload = 7
    }

    /// <summary>Stage-4 capstones a hacked location can take. One per location, wire-stable.</summary>
    internal enum Capstone : byte
    {
        None = 0,

        /// <summary>Every hostile contact inside the location's radius is revealed.</summary>
        Reveal = 1,

        /// <summary>A virtual jammer suppresses hostile sensors inside the radius.</summary>
        Jammer = 2,

        /// <summary>Hostile ground vehicles inside a small radius are destroyed.</summary>
        Sabotage = 3
    }

    internal readonly struct CyberAbilityInfo
    {
        public readonly HackKind Kind;
        public readonly string Code;
        public readonly string Name;
        public readonly string Summary;

        /// <summary>Location stage that unlocks it: 2 basic, 3 mid.</summary>
        public readonly byte Stage;

        /// <summary>Intel paid per use.</summary>
        public readonly float Intel;

        public CyberAbilityInfo(HackKind kind, string code, string name, string summary, byte stage, float intel)
        {
            Kind = kind;
            Code = code;
            Name = name;
            Summary = summary;
            Stage = stage;
            Intel = intel;
        }
    }

    /// <summary>
    /// Names, intel prices and stage gates for the eight map abilities. The panel, the console and
    /// the host all read this one table, so a row never says READY for an order the host refuses.
    /// </summary>
    internal static class CyberCatalog
    {
        public static readonly CyberAbilityInfo[] Table =
        {
            new CyberAbilityInfo(HackKind.Ping, "PNG", "PING SWEEP",
                "Ground contacts in the target area.", 2, 20f),
            new CyberAbilityInfo(HackKind.Track, "TRK", "TRACK UPLINK",
                "Streams air tracks for the window.", 3, 30f),
            new CyberAbilityInfo(HackKind.Blackout, "C2B", "RADAR BLACKOUT",
                "Jams hostile sensors; friends unaffected.", 3, 45f),
            new CyberAbilityInfo(HackKind.Ghost, "GST", "GHOST SHIELD",
                "Hostile tracking of your aircraft goes stale.", 3, 40f),
            new CyberAbilityInfo(HackKind.Spoof, "SPF", "SPOOF CONTACTS",
                "Feeds the enemy a false contact.", 3, 45f),
            new CyberAbilityInfo(HackKind.Scan, "SCN", "NODE SCAN",
                "Sweep for hostile vehicles; revealed armor can be hijacked.", 2, 25f),
            new CyberAbilityInfo(HackKind.Hijack, "HJK", "HIJACK",
                "Seize hostile vehicles: they halt and hold blind.", 3, 50f),
            new CyberAbilityInfo(HackKind.Overload, "OVL", "OVERLOAD",
                "Detonate hostile vehicles in a tight radius.", 3, 60f)
        };

        public static CyberAbilityInfo Info(HackKind kind) => Table[(int)kind];
        public static string Code(HackKind kind) => Info(kind).Code;
        public static string Name(HackKind kind) => Info(kind).Name;
        public static string Description(HackKind kind) => Info(kind).Summary;
        public static float Intel(HackKind kind) => Info(kind).Intel;
        public static byte RequiredStage(HackKind kind) => Info(kind).Stage;

        /// <summary>Effect radius of the ability at the target, metres; 0 when it has no ring.</summary>
        public static float Radius(HackKind kind)
        {
            switch (kind)
            {
                case HackKind.Ping: return 4000f;
                case HackKind.Track: return 9000f;
                case HackKind.Blackout: return 6000f;
                case HackKind.Scan: return 6000f;
                case HackKind.Hijack: return 2500f;
                case HackKind.Overload: return 400f;
                default: return 0f;
            }
        }

        /// <summary>Effect duration of the ability, seconds; 0 when it is instant or open-ended.</summary>
        public static float Duration(HackKind kind)
        {
            switch (kind)
            {
                case HackKind.Track: return 16f;
                case HackKind.Ghost: return 16f;
                case HackKind.Spoof: return 16f;
                case HackKind.Hijack: return 20f;
                default: return 0f;
            }
        }

        /// <summary>Jamming strength pushed into hostile sensors by RADAR BLACKOUT.</summary>
        public const float BlackoutStrength = 800f;
    }

    /// <summary>The three stage-4 capstones: price, recharge and copy.</summary>
    internal static class Capstones
    {
        public const float Intel = 120f;
        public const float RechargeSeconds = 360f;

        public static readonly Capstone[] All = { Capstone.Reveal, Capstone.Jammer, Capstone.Sabotage };

        public static string Code(Capstone capstone)
        {
            switch (capstone)
            {
                case Capstone.Reveal: return "RVL";
                case Capstone.Jammer: return "VJM";
                case Capstone.Sabotage: return "SAB";
                default: return "---";
            }
        }

        public static string Name(Capstone capstone)
        {
            switch (capstone)
            {
                case Capstone.Reveal: return "REVEAL";
                case Capstone.Jammer: return "VIRTUAL JAMMER";
                case Capstone.Sabotage: return "SABOTAGE";
                default: return "CAPSTONE";
            }
        }

        public static string Summary(Capstone capstone)
        {
            switch (capstone)
            {
                case Capstone.Reveal:
                    return "Every hostile contact inside the location's radius is revealed.";
                case Capstone.Jammer:
                    return "A virtual jammer suppresses hostile sensors inside the radius for 45 s.";
                case Capstone.Sabotage:
                    return "Hostile ground vehicles in a small radius are destroyed.";
                default:
                    return string.Empty;
            }
        }

        public static bool Known(byte capstone) => capstone <= (byte)Capstone.Sabotage;
    }
}
