using NOAvionics;

namespace BoscaliSummer.Features.Command.Domain
{
    internal static class CommandScoring
    {
        public static float Bias(bool friendly, int analyzerId, int targetId,
            int doctrine, bool priority, bool aircraft, bool building, bool antiAir)
        {
            int[] wing = PresenceBoard.GetInts(PresenceBoard.WingMemberIds);
            // Protect the aircraft making the decision, including WC's HQ deconfliction pass.
            if (PresenceBoard.Contains(wing, analyzerId)) return 1f;
            return TheaterScoring.Bias(friendly, PresenceBoard.Contains(wing, targetId),
                doctrine, priority, aircraft, building, antiAir);
        }
    }
}
