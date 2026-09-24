using BoscaliSummer.Features.FireAndDestruction.Domain;

namespace BoscaliSummer.Tests.Features.FireAndDestruction
{
    internal static class CookoffTests
    {
        public static void Run()
        {
            uint a = CookoffPolicy.Hash(12, 4, 8);
            uint b = CookoffPolicy.Hash(12, 4, 8);
            uint c = CookoffPolicy.Hash(13, 4, 8);
            TestAssert.That(a == b && a != c, "cookoff hash is stable per wreck");

            int ups = CookoffPolicy.FollowUps(a);
            TestAssert.That(ups >= 0 && ups <= CookoffPolicy.MaxGeneration,
                "follow-up count stays inside the generation ceiling");

            float d1 = CookoffPolicy.DelaySeconds(a, 1);
            float d2 = CookoffPolicy.DelaySeconds(a, 2);
            TestAssert.That(d1 >= CookoffPolicy.MinDelay && d1 <= CookoffPolicy.MaxDelay &&
                d2 >= CookoffPolicy.MinDelay && d2 <= CookoffPolicy.MaxDelay,
                "follow-up delays stay in 2–8 s");
            TestAssert.That(d1 != d2, "generation salts the delay");

            float due1 = CookoffPolicy.DueAt(10f, a, 1);
            float due2 = CookoffPolicy.DueAt(10f, a, 2);
            TestAssert.That(due1 == 10f + d1, "first follow-up is born plus one delay");
            TestAssert.That(due2 == 10f + d1 + d2 && due2 > due1, "second follow-up stacks");
            TestAssert.That(CookoffPolicy.DueAt(10f, a, 0) == 10f, "generation 0 is immediate");

            int seenZero = 0, seenOne = 0, seenTwo = 0;
            for (int i = 0; i < 64; i++)
            {
                int n = CookoffPolicy.FollowUps(CookoffPolicy.Hash(i, i * 3, -i));
                if (n == 0) seenZero++;
                else if (n == 1) seenOne++;
                else seenTwo++;
            }
            TestAssert.That(seenZero > 0 && seenOne > 0 && seenTwo > 0,
                "follow-up count uses the whole 0–2 range");
        }
    }
}
