using BoscaliSummer.Modules.Kaiju.Domain;

namespace BoscaliSummer.Tests.Features.Kaiju
{
    /// <summary>Pins the kaiju's attack choice and what lost weak spots take away.</summary>
    internal static class KaijuRulesTests
    {
        private static KaijuDamage Whole() => new KaijuDamage
            { Throat = true, WristL = true, WristR = true, Eyes = 2, Sacs = 6, Knees = 2, Broken = 0, Total = 21 };

        private static KaijuSense At(float distance, float bearing) => new KaijuSense { Distance = distance, Bearing = bearing };

        public static void Run()
        {
            var rules = new KaijuRules();
            KaijuDamage d = Whole();
            TestAssert.That(rules.Choose(0f, At(float.PositiveInfinity, 0f), d) == KaijuAction.Walk, "no aircraft: keep walking");
            TestAssert.That(rules.Choose(0f, At(400f, 30f), d) == KaijuAction.SwipeR, "close on the right: right claw");
            TestAssert.That(rules.Choose(0f, At(400f, -30f), d) == KaijuAction.SwipeL, "close on the left: left claw");
            d.WristL = false;
            TestAssert.That(rules.Choose(0f, At(400f, -30f), d) != KaijuAction.SwipeL, "broken left wrist: no left swipe");
            d = Whole();
            TestAssert.That(rules.Choose(3f, At(5000f, 0f), d) == KaijuAction.Walk, "laser and spikes start on cooldown");
            TestAssert.That(rules.Choose(10f, At(5000f, 10f), d) == KaijuAction.LaserCharge, "ready, in range, in front: beam");
            TestAssert.That(rules.Choose(10f, At(5000f, 120f), d) == KaijuAction.Spike, "behind it: spikes instead of the beam");
            d.Throat = false;
            TestAssert.That(rules.Choose(10f, At(5000f, 10f), d) == KaijuAction.Spike, "throat gone: never the beam");
            d.Sacs = 0;
            TestAssert.That(rules.Choose(10f, At(5000f, 10f), d) == KaijuAction.Walk, "no throat, no sacs, not close: walk");

            d = Whole();
            rules.Commit(KaijuAction.LaserCharge, 10f, d);
            TestAssert.That(rules.Choose(11f, At(5000f, 0f), d) == KaijuAction.Walk, "beam just fired: spikes wait 9 s, beam 24 s");
            TestAssert.That(rules.Choose(20f, At(5000f, 0f), d) == KaijuAction.Spike, "spikes come back first");

            TestAssert.That(KaijuRules.Speed(d) == KaijuRules.WalkSpeed && KaijuRules.Speed(new KaijuDamage { Knees = 0 }) < KaijuRules.WalkSpeed * 0.5f,
                "both knees broken: it crawls");
            TestAssert.That(KaijuRules.LaserLag(new KaijuDamage { Eyes = 0 }) > KaijuRules.LaserLag(d), "blinded: the beam lags further");
            TestAssert.That(KaijuRules.SpikesPerVolley(d) == 6 && KaijuRules.SpikesPerVolley(new KaijuDamage { Sacs = 3, Broken = 11, Total = 21 }) == 6,
                "one spike per sac, doubled once half the weak spots are gone");
            TestAssert.That(!new KaijuDamage { Broken = 11 }.HeartExposed && new KaijuDamage { Broken = 12 }.HeartExposed,
                "the heart opens at twelve broken weak spots");
        }
    }
}
