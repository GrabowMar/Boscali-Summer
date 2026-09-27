using BoscaliSummer.Features.Performance.Domain;
using BoscaliSummer.Tests.Framework;

namespace BoscaliSummer.Tests.Features.Performance
{
    internal static class FrameBudgetTests
    {
        internal static void Run()
        {
            var policy = new FrameBudgetPolicy();
            Feed(policy, 0.05f, 82); // scene warm-up
            Feed(policy, 0.05f, 82);
            TestAssert.That(!policy.Reduced, "one slow window cannot reduce effects");
            Feed(policy, 0.05f, 82);
            TestAssert.That(policy.Reduced, "sustained slow flight reduces cosmetic budget");

            Feed(policy, 0.03f, 140);
            TestAssert.That(policy.Reduced, "middling frames do not oscillate the budget");
            Feed(policy, 0.02f, 610);
            TestAssert.That(!policy.Reduced, "sustained fast flight restores cosmetics");

            Feed(policy, 0.05f, 165);
            TestAssert.That(policy.Reduced, "slow flight may reduce again");
            policy.Observe(0.5f, true);
            TestAssert.That(policy.Reduced, "a loading hitch does not change the budget");
            policy.Observe(0.02f, false);
            TestAssert.That(!policy.Reduced, "pause or leaving flight restores the budget");

            var severe = new FrameBudgetPolicy();
            Feed(severe, 0.125f, 100);
            TestAssert.That(severe.Reduced, "sustained single-digit FPS remains eligible");
        }

        private static void Feed(FrameBudgetPolicy policy, float seconds, int frames)
        {
            for (int i = 0; i < frames; i++) policy.Observe(seconds, true);
        }
    }
}
