using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    /// <summary>The member's behaviour graph (PilotMind): forced exits back to Rejoin decide at once, chosen switches keep their dwell.</summary>
    internal static class WingMindTests
    {
        private const float Dt = 0.02f;

        public static void Run()
        {
            // A missile broken against while the leader is on the ground (or too slow): back to the hold at the next decision,
            // not 2 s toward a slot nobody can hold.
            var mind = new PilotMind();
            mind.Force(BehaviourId.Defend);
            mind.Force(BehaviourId.Rejoin);
            var grounded = new MindInput { Role = Role.Slot, Spacing = 50f, LeaderFlying = false };
            float t = Fly(mind, grounded, BehaviourId.HoldOverhead, 3f);
            TestAssert.That(t <= PilotMind.DecisionPeriod + Dt, $"forced Rejoin leaves for HoldOverhead at the next decision (took {t:0.00} s)");

            // A chosen switch still dwells: the leader flying again does not pull it out of the hold before MinDwell.
            var flying = new MindInput { Role = Role.Slot, Spacing = 50f, LeaderFlying = true };
            t = Fly(mind, flying, BehaviourId.Rejoin, 5f);
            TestAssert.That(t >= PilotMind.MinDwell - Dt && t <= PilotMind.MinDwell + PilotMind.DecisionPeriod + Dt,
                $"HoldOverhead -> Rejoin waits out the dwell (took {t:0.00} s)");

            // Leader loss still wins over any dwell.
            var lost = new MindInput { Role = Role.Slot, Spacing = 50f, LeaderLost = true };
            t = Fly(mind, lost, BehaviourId.HoldOverhead, 1f);
            TestAssert.That(t <= Dt, $"leader lost -> HoldOverhead at once (took {t:0.00} s)");
        }

        /// <summary>Ticks until the mind is in <paramref name="want"/>: the seconds it took, or past the limit.</summary>
        private static float Fly(PilotMind mind, MindInput input, BehaviourId want, float limit)
        {
            float t = 0f;
            while (mind.Current != want && t <= limit)
            {
                mind.Tick(input, Dt, out _, out _);
                t += Dt;
            }
            return t;
        }
    }
}
