using BoscaliSummer.Features.Support.Runtime;

namespace BoscaliSummer.Features.Support.Domain
{
    /// <summary>
    /// Wire-stable mode of a CYBER jammer site. One byte per site in the OPS snapshot; the
    /// host owns the value and a client never assumes a mode it was not sent.
    /// </summary>
    internal enum EwPosture : byte
    {
        /// <summary>EMCON: the jammer stays silent — no umbrella, nothing to hunt.</summary>
        SigintPassive = 0,

        /// <summary>Electronic attack: barrage noise; full ECM umbrella.</summary>
        NoiseJamming = 1,

        /// <summary>Electronic attack: false returns and track deception; half umbrella.</summary>
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

        /// <summary>How loud the jammer is on the enemy's receivers.</summary>
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
    /// The one table that decides what a jammer's mode does. The host's <c>HackAction</c> and the
    /// panel both read it, so a row never says READY for an operation the host would refuse.
    /// Any emitting jammer (NOISE or DECEPTION) backs every station-backed operation; the mode
    /// only sets how strong the ECM umbrella is and how loud the site is. EMCON backs nothing.
    /// </summary>
    internal static class EwPostures
    {
        /// <summary>A fresh jammer comes up in NOISE: full umbrella, and it backs every operation.</summary>
        public const EwPosture Default = EwPosture.NoiseJamming;

        public static readonly EwPostureInfo[] All =
        {
            new EwPostureInfo(EwPosture.SigintPassive, "EP", "EMC", "EMCON",
                "Silent. No umbrella, nothing for the enemy to hunt; backs no operation.", "SILENT"),
            new EwPostureInfo(EwPosture.NoiseJamming, "EA", "JAM", "NOISE",
                "Barrage noise. Full ECM umbrella; backs every station operation.", "HIGH"),
            new EwPostureInfo(EwPosture.GhostSpoofing, "EA", "DEC", "DECEPTION",
                "False returns. Half umbrella but quieter; backs every station operation.", "MODERATE")
        };

        public static EwPostureInfo Info(EwPosture posture) => All[(int)Clamp((byte)posture)];

        /// <summary>Out-of-range bytes from a stale or hostile peer read as the default.</summary>
        public static EwPosture Clamp(byte value) =>
            value <= (byte)EwPosture.GhostSpoofing ? (EwPosture)value : Default;

        /// <summary>True when the operation reaches through a CYBER jammer site.</summary>
        public static bool StationBacked(HackKind kind)
        {
            FacilityId facility = CyberCatalog.Facility(kind);
            return facility == FacilityId.Disrupt || facility == FacilityId.Ew;
        }

        /// <summary>ECM umbrella strength of a jammer in this mode, 0..1.</summary>
        public static float Umbrella(EwPosture posture) =>
            posture == EwPosture.NoiseJamming ? 1f : posture == EwPosture.GhostSpoofing ? 0.5f : 0f;

        /// <summary>A jammer radiating in this mode (anything but EMCON).</summary>
        public static bool Emitting(EwPosture posture) => posture != EwPosture.SigintPassive;

        public static bool Backs(EwPosture posture, HackKind kind) => !StationBacked(kind) || Emitting(posture);
    }
}
