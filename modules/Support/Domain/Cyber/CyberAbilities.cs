using System;

namespace BoscaliSummer.Modules.Support.Domain.Cyber
{
    /// <summary>Wire-stable cyber operation bytes. The eight map abilities.</summary>
    internal enum HackKind : byte
    {
        Ping = 0,
        Track = 1,
        Blackout = 2,
        Ghost = 3,
        Spoof = 4,

        /// <summary>Wide ground search; a deeper profile opens selective disruption.</summary>
        Scan = 5,

        /// <summary>Reveal and disrupt up to eight hostile ground vehicles for a short window.</summary>
        Hijack = 6,

        /// <summary>High-strength sensor disruption on four ground vehicles in a tight radius.</summary>
        Overload = 7
    }

    /// <summary>Quality-six payloads for the single expiring access package. Wire-stable.</summary>
    internal enum Capstone : byte
    {
        None = 0,

        /// <summary>Every hostile contact inside the location's radius is revealed.</summary>
        Reveal = 1,

        /// <summary>A virtual jammer suppresses hostile sensors inside the radius.</summary>
        Jammer = 2,

        /// <summary>Disrupt a bounded hostile ground cluster without dealing direct damage.</summary>
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
            new CyberAbilityInfo(HackKind.Ping, "PNG", "EMITTER PING",
                "Locate up to 48 hostile ground and ship radars that are on and working; radar silence defeats this sweep.", 2, 20f),
            new CyberAbilityInfo(HackKind.Track, "TRK", "TRACK UPLINK",
                "Streams air tracks for the window.", 3, 30f),
            new CyberAbilityInfo(HackKind.Blackout, "C2B", "RADAR BLACKOUT",
                "Jams hostile sensors; friends unaffected.", 3, 45f),
            new CyberAbilityInfo(HackKind.Ghost, "GST", "GHOST SHIELD",
                "Hostile tracks of friendly aircraft in 3 km go stale.", 3, 40f),
            new CyberAbilityInfo(HackKind.Spoof, "SPF", "SPOOF CONTACTS",
                "False tracks in 3 km. Q6 also breaks four non-nuclear active-radar missile locks; native seekers may recover.", 3, 45f),
            new CyberAbilityInfo(HackKind.Scan, "SCN", "NODE SCAN",
                "Reveal up to 48 hostile surface contacts in a wide area, including silent radars; aircraft excluded.", 2, 25f),
            new CyberAbilityInfo(HackKind.Hijack, "HJK", "HIJACK",
                "Reveal and blind up to eight hostile ground vehicles.", 3, 50f),
            new CyberAbilityInfo(HackKind.Overload, "OVL", "SENSOR OVERLOAD",
                "Strong sensor disruption on four vehicles in a tight area.", 3, 60f)
        };

        public static CyberAbilityInfo Info(HackKind kind) => Table[(int)kind];
        public static string Code(HackKind kind) => Info(kind).Code;
        public static string Name(HackKind kind) => Info(kind).Name;
        public static string Description(HackKind kind) => Info(kind).Summary;
        public static float Intel(HackKind kind) => Info(kind).Intel;
        public static byte RequiredStage(HackKind kind) => Info(kind).Stage;
        public static int RequiredQuality(HackKind kind) => kind == HackKind.Ping || kind == HackKind.Scan ? 0
            : kind == HackKind.Track || kind == HackKind.Blackout || kind == HackKind.Hijack ? 2 : 4;
        public static string Branch(HackKind kind) => kind == HackKind.Ping || kind == HackKind.Scan ||
            kind == HackKind.Hijack || kind == HackKind.Overload ? "INTRUSION" : "ELECTRONIC WARFARE";

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
                case HackKind.Overload: return 1500f;
                case HackKind.Ghost:
                case HackKind.Spoof: return 3000f;
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
                case HackKind.Blackout: return 20f;
                case HackKind.Overload: return 30f;
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
        public const int RequiredQuality = 6;
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
                case Capstone.Sabotage: return "NETWORK SHUTDOWN";
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
                    return "Reveal and disrupt eight ground vehicles in 3 km for 35 s; no direct damage.";
                default:
                    return string.Empty;
            }
        }

        public static bool Known(byte capstone) => capstone <= (byte)Capstone.Sabotage;
    }
}
