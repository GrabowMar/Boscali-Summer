using BoscaliSummer.Features.Support.Runtime;

namespace BoscaliSummer.Features.Support.Domain
{
    /// <summary>
    /// Wire-stable operating posture of a mobile EW station. One byte in the OPS snapshot;
    /// the host owns the value and a client never assumes a posture it was not sent.
    /// </summary>
    internal enum EwPosture : byte
    {
        /// <summary>Electronic support: receivers only, the station radiates nothing.</summary>
        SigintPassive = 0,

        /// <summary>Electronic attack: barrage noise against hostile radars.</summary>
        NoiseJamming = 1,

        /// <summary>Electronic attack: false returns and track deception.</summary>
        GhostSpoofing = 2
    }

    internal readonly struct EwPostureInfo
    {
        public readonly EwPosture Posture;

        /// <summary>Doctrinal family: ES (support) or EA (attack).</summary>
        public readonly string Discipline;

        public readonly string Code;
        public readonly string Name;
        public readonly string Summary;

        /// <summary>How loud the station is on the enemy's receivers.</summary>
        public readonly string Emissions;

        public EwPostureInfo(EwPosture posture, string discipline, string code, string name,
                             string summary, string emissions)
        {
            Posture = posture;
            Discipline = discipline;
            Code = code;
            Name = name;
            Summary = summary;
            Emissions = emissions;
        }
    }

    /// <summary>
    /// The one table that decides which station posture backs which cyber operation. The
    /// host's <c>HackAction</c> and the panel both read it, so a row never says READY for an
    /// operation the host would refuse.
    /// </summary>
    internal static class EwPostures
    {
        /// <summary>A freshly deployed station comes up jamming, the posture that backs the
        /// most commonly used station operation.</summary>
        public const EwPosture Default = EwPosture.NoiseJamming;

        public static readonly EwPostureInfo[] All =
        {
            new EwPostureInfo(EwPosture.SigintPassive, "ES", "SIG", "SIGINT PASSIVE",
                "Listen only. Station stays silent; backs no attack.", "SILENT"),
            new EwPostureInfo(EwPosture.NoiseJamming, "EA", "JAM", "NOISE JAMMING",
                "Barrage noise. Backs RADAR BLACKOUT.", "HIGH"),
            new EwPostureInfo(EwPosture.GhostSpoofing, "EA", "DEC", "GHOST SPOOFING",
                "False returns. Backs GHOST SHIELD and SPOOF CONTACTS.", "MODERATE")
        };

        public static EwPostureInfo Info(EwPosture posture) => All[(int)Clamp((byte)posture)];

        /// <summary>Out-of-range bytes from a stale or hostile peer read as the default.</summary>
        public static EwPosture Clamp(byte value) =>
            value <= (byte)EwPosture.GhostSpoofing ? (EwPosture)value : Default;

        /// <summary>True when the operation reaches through a physical EW station.</summary>
        public static bool StationBacked(HackKind kind)
        {
            FacilityId facility = CyberCatalog.Facility(kind);
            return facility == FacilityId.Disrupt || facility == FacilityId.Ew;
        }

        /// <summary>The posture a station-backed operation needs; null when it needs none.</summary>
        public static EwPosture? Required(HackKind kind)
        {
            if (!StationBacked(kind)) return null;
            return kind == HackKind.Blackout ? EwPosture.NoiseJamming : EwPosture.GhostSpoofing;
        }

        public static bool Backs(EwPosture posture, HackKind kind)
        {
            EwPosture? required = Required(kind);
            return !required.HasValue || required.Value == posture;
        }
    }
}
