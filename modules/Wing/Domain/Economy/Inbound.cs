using System.Globalization;

namespace BoscaliSummer.Modules.Wing.Domain
{
    /// <summary>Where a launch is on its way to the wing (spec WMC rebuild §SUPPLY INBOUND).</summary>
    public enum InboundPhase : byte { Queued, Spawning, Taxiing, Departing, Joining }

    /// <summary>SUPPLY's INBOUND list (design wmc-rebuild/supply-spawn-inbound §3): a launch is queued until its aircraft
    /// appears, spawning until it is a member and while parked, then taxiing, departing and joining, and inbound no longer
    /// once it reached its slot, fights, turns home, aborted or left.</summary>
    internal static class InboundRules
    {
        /// <summary>An ETA past this is shown as none.</summary>
        public const float MaxEtaSeconds = 99f * 60f;

        public static InboundPhase Phase(bool appeared, bool adopted, bool onGround, GroundPhase ground)
        {
            if (!appeared) return InboundPhase.Queued;
            if (!adopted) return InboundPhase.Spawning;
            if (!onGround) return InboundPhase.Joining;
            switch (ground)
            {
                case GroundPhase.Parked: return InboundPhase.Spawning;
                case GroundPhase.TaxiOut:
                case GroundPhase.HoldShort: return InboundPhase.Taxiing;
                default: return InboundPhase.Departing;
            }
        }

        public static bool StillInbound(bool alive, bool released, bool recovering, bool engaged, bool onGround, GroundPhase ground,
            BehaviourId behaviour)
        {
            if (!alive || released || recovering || engaged) return false;
            if (onGround) return ground != GroundPhase.Aborted;
            return behaviour == BehaviourId.Rejoin;
        }

        /// <summary>Only a joining member's intercept time; NaN otherwise.</summary>
        public static float Eta(InboundPhase phase, float interceptSeconds) =>
            phase == InboundPhase.Joining && interceptSeconds >= 0f && interceptSeconds < MaxEtaSeconds ? interceptSeconds : float.NaN;
    }

    internal static class InboundWords
    {
        public static string Phase(InboundPhase p)
        {
            switch (p)
            {
                case InboundPhase.Queued: return "QUEUED";
                case InboundPhase.Spawning: return "SPAWNING";
                case InboundPhase.Taxiing: return "TAXIING";
                case InboundPhase.Departing: return "DEPARTING";
                default: return "JOINING";
            }
        }

        public static string More(int n) => "+" + n.ToString(CultureInfo.InvariantCulture) + " MORE";
    }
}
