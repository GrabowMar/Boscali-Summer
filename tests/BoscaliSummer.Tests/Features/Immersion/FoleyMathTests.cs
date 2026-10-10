using BoscaliSummer.Modules.Immersion.Domain;

namespace BoscaliSummer.Tests.Features.Immersion
{
    internal static class FoleyMathTests
    {
        private const float Dt = 1f / 60f;

        public static void Run()
        {
            TestAssert.That(FoleyMath.Hit(0f, 25f) == HitSound.Thud && FoleyMath.Hit(8f, 25f) == HitSound.Thud,
                "A warhead-sized blast is a thud, whatever else hit with it.");
            TestAssert.That(FoleyMath.Hit(6f, 0f) == HitSound.Ping && FoleyMath.Hit(6f, 3f) == HitSound.Ping,
                "Bullets ping, explosive rounds included.");
            TestAssert.That(FoleyMath.Hit(0f, 0f) == HitSound.None && FoleyMath.Hit(0f, 2f) == HitSound.None,
                "Fire, collision and a faint blast make no hit sound.");
            TestAssert.That(FoleyMath.PingVolume(0.1f) >= .2f && FoleyMath.PingVolume(500f) <= .6f &&
                FoleyMath.PingVolume(40f) > FoleyMath.PingVolume(2f), "A ping is quiet, louder for a heavier round, and bounded.");

            var cruise = new StrainCreak();
            int idle = 0;
            for (int i = 0; i < 1800; i++) if (cruise.Step(1.7f + .7f * (float)System.Math.Sin(i * .05f), Dt)) idle++;
            TestAssert.That(idle == 0, "Gentle manoeuvring below 3 G never creaks the harness, got " + idle);

            var pull = new StrainCreak();
            int onset = 0, held = 0, release = 0;
            for (int i = 0; i < 180; i++) if (pull.Step(1f + 6f * i / 180f, Dt)) onset++;
            for (int i = 0; i < 600; i++) if (pull.Step(7f, Dt)) held++;
            for (int i = 0; i < 120; i++) if (pull.Step(7f - 6f * i / 120f, Dt)) release++;
            TestAssert.That(onset >= 1 && onset <= 4, "Pulling to 7 G creaks the harness a few times, got " + onset);
            TestAssert.That(held == 0, "A steady load is silent, got " + held);
            TestAssert.That(release >= 1 && release <= 3, "Unloading creaks too, got " + release);

            var shaken = new StrainCreak();
            int rapid = 0;
            for (int i = 0; i < 300; i++) if (shaken.Step(i / 12 % 2 == 0 ? 3f : 9f, Dt)) rapid++;
            TestAssert.That(rapid >= 2 && rapid <= 5, "Violent load changes are rate-limited, got " + rapid + " in 5 s");
            TestAssert.That(!shaken.Step(float.NaN, Dt), "A broken G reading makes no sound.");
        }
    }
}
