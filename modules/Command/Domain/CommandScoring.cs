using NOAvionics;

namespace BoscaliSummer.Features.Command.Domain
{
    internal static class CommandScoring
    {
        public static float Bias(bool friendly, bool analyzerIsWingman, bool targetIsWingman,
            int doctrine, bool priority, bool aircraft, bool building, bool antiAir)
        {
            // Protect the aircraft making the decision, including WC's HQ deconfliction pass.
            if (analyzerIsWingman) return 1f;
            return TheaterScoring.Bias(friendly, targetIsWingman,
                doctrine, priority, aircraft, building, antiAir);
        }
    }
}
