using BoscaliSummer.Features.Support.Runtime;

namespace BoscaliSummer.Features.Support.Domain
{
    /// <summary>Why an information operation cannot be tasked right now, in gate order.</summary>
    internal enum InfoGate : byte
    {
        Ready = 0,
        FacilityMissing = 1,
        StationMissing = 2,
        WrongPosture = 3
    }

    /// <summary>
    /// Doctrine for the INFO tab: how each cyber operation is grouped and what gates it.
    /// Names, prices and scaling stay in <see cref="CyberCatalog"/> and
    /// <see cref="InfoPowers"/>; this answers only "can it be tasked, and if not, why".
    /// Allocation and cooldown are checked by the caller, which owns the economy.
    /// </summary>
    internal static class InfoOperations
    {
        /// <summary>Short target-set tag shown beside the operation name.</summary>
        public static string Category(HackKind kind)
        {
            switch (kind)
            {
                case HackKind.Ping:
                case HackKind.Track: return "RECON";
                case HackKind.Blackout: return "C2";
                case HackKind.Ghost: return "IFF";
                default: return "DECEPTION";
            }
        }

        /// <summary>What the operation is aimed at, in the language of the target set.</summary>
        public static string Target(HackKind kind)
        {
            switch (kind)
            {
                case HackKind.Ping: return "Troop concentration sweep over an area.";
                case HackKind.Track: return "Hostile air track feed for the window.";
                case HackKind.Blackout: return "Hostile C2 sensors in radius; friends unaffected.";
                case HackKind.Ghost: return "Enemy tracks on your aircraft go stale.";
                default: return "Enemy picture receives a false contact.";
            }
        }

        public static InfoGate Evaluate(InfoNetwork network, HackKind kind, bool stationDeployed, EwPosture posture)
        {
            if (network == null || network.Level(CyberCatalog.Facility(kind)) < CyberCatalog.RequiredLevel(kind))
                return InfoGate.FacilityMissing;
            if (!EwPostures.StationBacked(kind)) return InfoGate.Ready;
            if (!stationDeployed) return InfoGate.StationMissing;
            return EwPostures.Backs(posture, kind) ? InfoGate.Ready : InfoGate.WrongPosture;
        }

        /// <summary>Operator copy for a closed gate; null when the gate is open.</summary>
        public static string Explain(InfoGate gate, HackKind kind)
        {
            switch (gate)
            {
                case InfoGate.FacilityMissing:
                    return "BUILD " + InfoNetwork.Facility(CyberCatalog.Facility(kind)).Name +
                           " LV" + CyberCatalog.RequiredLevel(kind);
                case InfoGate.StationMissing:
                    return "NEEDS EW STATION NEAR TARGET";
                case InfoGate.WrongPosture:
                    EwPosture? required = EwPostures.Required(kind);
                    return "SET EW POSTURE: " +
                           (required.HasValue ? EwPostures.Info(required.Value).Name : "ACTIVE");
                default:
                    return null;
            }
        }
    }
}
