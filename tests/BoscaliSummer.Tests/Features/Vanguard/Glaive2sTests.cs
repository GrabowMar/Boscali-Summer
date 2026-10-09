using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins GLAIVE-2S SPECTRE: pylon-turn geometry, HAND-OFF slide, port gun limits, bursts and HUD words.</summary>
    internal static class Glaive2sTests
    {
        private static bool Near(float a, float b, float eps = 1e-3f) => Mathf.Abs(a - b) <= eps;
        private static bool Near(Vector3 a, Vector3 b, float eps = 1e-3f) => (a - b).magnitude <= eps;

        public static void Run()
        {
            TestAssert.That(VanguardKeys.RoleOf(VanguardKeys.Glaive2S) == VanguardRole.Gunship, "VG_Glaive2S is the gunship");

            TestAssert.That(GlaiveProfile.Height(1000f) == 350f && GlaiveProfile.Height(20000f) == 800f &&
                GlaiveProfile.Height(100000f) == 1500f, "ingress height: 350 m floor, 4 % of range, 1500 m cap");

            TestAssert.That(GlaiveProfile.Capture(800f, 200f, 6f, 90f) && !GlaiveProfile.Capture(800f, 200f, 4f, 90f) &&
                !GlaiveProfile.Capture(1000f, 200f, 6f, 90f) && !GlaiveProfile.Capture(800f, 50f, 6f, 90f) &&
                !GlaiveProfile.Capture(800f, 200f, 6f, 170f), "capture: after 5 s, inside 900 m, above 70 m, under 160 m/s");

            // Orbit guidance: on the circle the waypoint is the pure tangent, and the tangent keeps the centre on PORT.
            Vector3 onCircle = GlaiveProfile.OrbitOffset(Vector3.right * 450f, Vector3.forward);
            TestAssert.That(Near(onCircle, new Vector3(0f, 0f, 250f)), "on the 450 m circle the waypoint is 250 m along the tangent");
            Vector3 heading = onCircle.normalized, toCentre = -Vector3.right;
            TestAssert.That(Vector3.Cross(heading, toCentre).y < 0f, "counter-clockwise orbit: the centre is on the left (port)");
            Vector3 outside = GlaiveProfile.OrbitOffset(Vector3.right * 900f, Vector3.forward);
            Vector3 inside = GlaiveProfile.OrbitOffset(Vector3.right * 100f, Vector3.forward);
            TestAssert.That(outside.x < 0f && inside.x > 0f && Near(GlaiveProfile.OrbitOffset(Vector3.zero, Vector3.zero).magnitude, 250f),
                "radial correction pulls in from outside, out from inside, finite at the centre");

            // Geometry budget: the far edge of the engagement disc stays inside gun range from anywhere on the orbit.
            float far = Mathf.Sqrt(Mathf.Pow(GlaiveProfile.OrbitRadius + GlaiveProfile.Reach, 2f) + Mathf.Pow(GlaiveProfile.OrbitHeight, 2f));
            float slant = Mathf.Sqrt(Mathf.Pow(GlaiveProfile.OrbitRadius, 2f) + Mathf.Pow(GlaiveProfile.OrbitHeight, 2f));
            TestAssert.That(far <= GlaiveProfile.GunRange && Near(slant, 570.09f, .05f), "orbit slant 570 m, far edge 873 m <= 900 m gun range");

            // HAND-OFF slides 60 m/s: 12 m per 0.2 s plan tick, height follows at once, short gaps close exactly.
            TestAssert.That(Near(GlaiveProfile.SlideStep(new Vector3(1000f, 5f, 0f), .2f), new Vector3(12f, 5f, 0f)) &&
                Near(GlaiveProfile.SlideStep(new Vector3(3f, 0f, -4f), .2f), new Vector3(3f, 0f, -4f)),
                "slide step caps the horizontal move at 60 m/s");
            float gap = 900f, t = 0f;
            while (gap > 1f && t < 60f) { gap -= GlaiveProfile.SlideStep(new Vector3(gap, 0f, 0f), .2f).x; t += .2f; }
            TestAssert.That(Near(t, 15f, .21f), "a 900 m hand-off completes in 15 s");

            TestAssert.That(GlaiveProfile.Throttle(90f) == .4f && Near(GlaiveProfile.Throttle(70f), .9f) && GlaiveProfile.Throttle(120f) == 0f,
                "propeller throttle holds 90 m/s");

            TestAssert.That(!GlaiveProfile.Burst(.5f) && GlaiveProfile.Burst(1.5f) && !GlaiveProfile.Burst(2.5f) &&
                GlaiveProfile.Burst(4.2f) && !GlaiveProfile.Burst(5.5f), "1 s bursts every 3 s, from 1 s after orbit entry");
            float perStation = GlaiveProfile.Endurance / GlaiveProfile.BurstPeriod * GlaiveProfile.BurstLength / GlaiveProfile.ShotInterval;
            TestAssert.That(Near(perStation, 250f, .5f) && perStation >= GlaiveProfile.Rounds,
                "5 rounds a burst: 240 rounds last ~144 s of the 150 s on station");

            TestAssert.That(GlaiveProfile.CanFire(570f, 340f, 90f, 1f) && !GlaiveProfile.CanFire(950f, 340f, 90f, 1f) &&
                !GlaiveProfile.CanFire(570f, 10f, 90f, 1f) && !GlaiveProfile.CanFire(570f, 340f, 200f, 1f) &&
                !GlaiveProfile.CanFire(570f, 340f, 90f, .99f) && !GlaiveProfile.CanFire(10f, 340f, 90f, 1f),
                "gun gate: 20-900 m, target 30 m below, 45-150 m/s, barrel within 3.6 deg");

            TestAssert.That(!GlaiveProfile.Retire(240, 10f) && GlaiveProfile.Retire(0, 10f) && GlaiveProfile.Retire(100, 150f),
                "retire when dry or after 150 s on station");
            TestAssert.That(GlaiveProfile.Crashing(30f, 6f) && !GlaiveProfile.Crashing(30f, 3f) && !GlaiveProfile.Crashing(100f, 60f),
                "scuttle below 35 m AGL after launch clearance");

            Vector2 port = GlaiveProfile.GunAngles(Vector3.left);
            Vector2 down45 = GlaiveProfile.GunAngles(new Vector3(0f, -1f, 1f));
            TestAssert.That(Near(port.x, -90f) && Near(port.y, 0f) && Near(down45.x, 0f) && Near(down45.y, 45f),
                "gun angles: port beam = yaw -90, 45 deg down = pitch +45");
            TestAssert.That(Near(GlaiveProfile.Barrel(-90f, 0f), Vector3.left) &&
                Near(GlaiveProfile.Barrel(-60f, 38f), GlaiveProfile.Barrel(-60f, 38f).normalized) &&
                Near(GlaiveProfile.GunAngles(GlaiveProfile.Barrel(-123f, 27f)), new Vector2(-123f, 27f)),
                "barrel direction round-trips through gun angles");
            TestAssert.That(GlaiveProfile.ClampGun(new Vector2(0f, 0f)) == new Vector2(-10f, 0f) &&
                GlaiveProfile.ClampGun(new Vector2(-90f, -50f)) == new Vector2(-90f, -30f) &&
                GlaiveProfile.ClampGun(new Vector2(170f, 95f)) == new Vector2(-10f, 80f), "gun traverses the port side only");

            TestAssert.That(GlaiveProfile.Bank(GlaivePhase.Orbit) == 30f && GlaiveProfile.Bank(GlaivePhase.Ingress) == 0f,
                "the drone banks into its pylon turn only on station");

            TestAssert.Eq(GlaiveProfile.HudText(GlaivePhase.Ingress, 240, 0f), "SPECTRE INBOUND", "HUD inbound");
            TestAssert.Eq(GlaiveProfile.HudText(GlaivePhase.Orbit, 200, 20f), "SPECTRE IN ORBIT", "HUD on station");
            TestAssert.Eq(GlaiveProfile.HudText(GlaivePhase.Orbit, 200, 1400f), "HAND-OFF 1.4 km", "HUD hand-off distance");
            TestAssert.Eq(GlaiveProfile.HudText(GlaivePhase.Depart, 0, 0f), "SPECTRE WINCHESTER", "HUD dry");
            TestAssert.Eq(GlaiveProfile.HudText(GlaivePhase.Depart, 60, 0f), "SPECTRE OFF STATION", "HUD time out");
        }

        private static bool Near(Vector2 a, Vector2 b) => (a - b).magnitude <= 1e-2f;
    }
}
