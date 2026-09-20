using BoscaliSummer.Features.Support.Domain.Cyber;
using BoscaliSummer.Features.Support.Runtime;

namespace BoscaliSummer.Features.Support.Domain
{
    /// <summary>Why an information operation cannot be tasked right now, in gate order.</summary>
    internal enum InfoGate : byte
    {
        Ready = 0,
        FacilityMissing = 1,
        StationMissing = 2,
        WrongPosture = 3,
        CommandCompromised = 4
    }

    /// <summary>
    /// Doctrine for CYBER › OPERATIONS: how each cyber operation is grouped and what gates it.
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

        /// <summary>
        /// Target-independent gates, in order: doctrine level, a compromised Cyber Command, then
        /// for station-backed operations a working jammer and one that is emitting (not EMCON).
        /// Whether that jammer reaches the target stays a host check.
        /// </summary>
        public static InfoGate Evaluate(InfoNetwork network, HackKind kind, CyberNetwork cyber, double now = 0.0)
        {
            if (network == null || network.Level(CyberCatalog.Facility(kind)) < CyberCatalog.RequiredLevel(kind))
                return InfoGate.FacilityMissing;
            if (cyber != null && cyber.CommandCompromised) return InfoGate.CommandCompromised;
            if (!EwPostures.StationBacked(kind)) return InfoGate.Ready;
            // A trace foothold is a backdoor into that network: no jammer of your own needed.
            if (cyber != null && cyber.AnyFoothold(now)) return InfoGate.Ready;
            if (cyber == null || !cyber.AnyWorking(CyberSiteKind.Jammer)) return InfoGate.StationMissing;
            return cyber.AnyEmittingJammer() ? InfoGate.Ready : InfoGate.WrongPosture;
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
                    return "NEEDS A WORKING JAM SITE NEAR TARGET";
                case InfoGate.WrongPosture:
                    return "EVERY JAMMER IS IN EMCON · SET ONE TO NOISE OR DECEPTION";
                case InfoGate.CommandCompromised:
                    return "CYBER COMMAND COMPROMISED · PATCH IT";
                default:
                    return null;
            }
        }
    }
}
