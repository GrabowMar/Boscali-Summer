using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>GLAIVE-2A SENTINEL: phase machine, station-keeping loop, fire control.</summary>
    internal static class Glaive2aTests
    {
        public static void Run()
        {
            TestAssert.That(VanguardKeys.RoleOf(VanguardKeys.Glaive2A) == VanguardRole.Sentinel, "GLAIVE-2A is the sentinel");

            // Phase machine.
            TestAssert.Eq(SentinelProfile.Next(SentinelPhase.Launch, 2f, 5000f, 0f, 300, 0f), SentinelPhase.Launch, "boost holds 3 s");
            TestAssert.Eq(SentinelProfile.Next(SentinelPhase.Launch, 3f, 5000f, 0f, 300, 0f), SentinelPhase.Ingress, "boost ends at 3 s");
            TestAssert.Eq(SentinelProfile.Next(SentinelPhase.Ingress, 20f, 1300f, 0f, 300, 0f), SentinelPhase.Ingress, "cruise outside 1.2 km");
            TestAssert.Eq(SentinelProfile.Next(SentinelPhase.Ingress, 20f, 1200f, 0f, 300, 0f), SentinelPhase.Brake, "brake at 1.2 km");
            TestAssert.Eq(SentinelProfile.Next(SentinelPhase.Brake, 40f, 10f, .9f, 300, 0f), SentinelPhase.Brake, "park needs 1 s dwell");
            TestAssert.Eq(SentinelProfile.Next(SentinelPhase.Brake, 40f, 10f, 1f, 300, 0f), SentinelPhase.Hover, "on station");
            TestAssert.Eq(SentinelProfile.Next(SentinelPhase.Hover, 90f, 400f, 0f, 1, 149f), SentinelPhase.Hover, "a re-park keeps station");
            TestAssert.Eq(SentinelProfile.Next(SentinelPhase.Hover, 90f, 0f, 0f, 0, 10f), SentinelPhase.Drop, "dry drops");
            TestAssert.Eq(SentinelProfile.Next(SentinelPhase.Hover, 90f, 0f, 0f, 120, SentinelProfile.HoverSeconds), SentinelPhase.Drop, "hover clock drops");
            TestAssert.Eq(SentinelProfile.Next(SentinelPhase.Drop, 90f, 0f, 9f, 300, 0f), SentinelPhase.Drop, "drop is final");

            TestAssert.Near(SentinelProfile.NextDwell(0f, 100f, 3f, .2f), .2f, "dwell grows near and slow");
            TestAssert.Near(SentinelProfile.NextDwell(.8f, 160f, 3f, .2f), 0f, "dwell resets outside 150 m");
            TestAssert.Near(SentinelProfile.NextDwell(.8f, 20f, 7f, .2f), 0f, "dwell resets while still closing fast");

            // Station-keeping law.
            AssertVec(SentinelProfile.HoldAccel(new Vector3(1200f, 0f, 0f), new Vector3(110f, 0f, 0f)), new Vector3(-6f, 0f, 0f), "brakes at 6 m/s2 from cruise");
            AssertVec(SentinelProfile.HoldAccel(new Vector3(10f, 0f, 0f), Vector3.zero), new Vector3(2.8f, 0f, 0f), "creeps to the point");
            AssertVec(SentinelProfile.HoldAccel(Vector3.zero, new Vector3(2f, 0f, 0f)), new Vector3(-1.6f, 0f, 0f), "damps drift");
            AssertVec(SentinelProfile.HoldAccel(new Vector3(0f, 100f, 0f), Vector3.zero), new Vector3(0f, 6f, 0f), "climb clamped at 6 m/s2");
            AssertVec(SentinelProfile.HoldAccel(new Vector3(0f, -2f, 0f), Vector3.zero), new Vector3(0f, -.8f, 0f), "small sink");
            AssertVec(SentinelProfile.HoldAccel(new Vector3(0f, -1000f, 0f), new Vector3(0f, -40f, 0f)), Vector3.zero, "descent capped at 40 m/s");
            AssertVec(SentinelProfile.HoldAccel(new Vector3(0f, -150f, 0f), new Vector3(0f, -40f, 0f)), new Vector3(0f, 6f, 0f), "stopping curve brakes the drop 150 m out");
            Settles(new Vector3(-1200f, 150f, 0f), new Vector3(110f, 0f, 0f), 45f, "brake entry at cruise");
            Settles(new Vector3(-600f, 800f, 0f), new Vector3(150f, -80f, 0f), 30f, "diving in high and fast");
            Settles(new Vector3(0f, 1500f, 0f), Vector3.zero, 5f, "parked 1.5 km too high");

            AssertVec(SentinelProfile.Slide(new Vector3(1000f, 50f, 0f), .5f), new Vector3(20f, 0f, 0f), "park point slides at 40 m/s");
            AssertVec(SentinelProfile.Slide(new Vector3(5f, 0f, 0f), .5f), new Vector3(5f, 0f, 0f), "short slide lands exactly");
            AssertVec(SentinelProfile.ParkOffset(new Vector3(0f, 0f, 20000f), true, Vector3.right), new Vector3(0f, 0f, 8000f), "far target clamped to 8 km");
            AssertVec(SentinelProfile.ParkOffset(new Vector3(3000f, -900f, 0f), true, Vector3.right), new Vector3(3000f, 0f, 0f), "near target kept, flat");
            AssertVec(SentinelProfile.ParkOffset(Vector3.zero, false, Vector3.forward), new Vector3(0f, 0f, 3000f), "no target parks 3 km ahead");
            TestAssert.Near(SentinelProfile.Throttle(110f), .4f, "cruise throttle");
            TestAssert.Near(SentinelProfile.Throttle(200f), 0f, "inherited launch speed bleeds off");

            // Target rules.
            TestAssert.That(SentinelProfile.Engageable(true, 60f, 120f, false, float.MaxValue), "helicopter engaged");
            TestAssert.That(!SentinelProfile.Engageable(true, 139.9f, 5f, false, float.MaxValue), "landed aircraft ignored");
            TestAssert.That(!SentinelProfile.Engageable(true, 140f, 500f, false, float.MaxValue), "fast jets ignored");
            TestAssert.That(SentinelProfile.Engageable(false, 600f, 0f, true, 1500f), "missile at a guarded friendly");
            TestAssert.That(!SentinelProfile.Engageable(false, 600f, 0f, true, 1501f), "missile at a far friendly ignored");
            TestAssert.That(!SentinelProfile.Engageable(false, 10f, 0f, false, 0f), "ground units ignored");
            TestAssert.Near(SentinelProfile.TimeToImpact(3000f, 300f), 10f, "missile tti");
            TestAssert.Near(SentinelProfile.TimeToImpact(1000f, -50f), 50f, "receding aircraft floors closing at 20 m/s");
            TestAssert.That(SentinelProfile.TimeToImpact(3000f, 300f) < SentinelProfile.TimeToImpact(800f, 30f), "inbound missile outranks a hovering helicopter");
            TestAssert.That(SentinelProfile.Replaces(10f, 7.9f) && !SentinelProfile.Replaces(10f, 8.1f), "sticky lock needs 20 % more urgency");

            TestAssert.That(SentinelProfile.InArc(20f) && !SentinelProfile.InArc(20.1f) && SentinelProfile.InArc(-90f) && !SentinelProfile.InArc(-90.1f), "belly arc -90..+20");
            TestAssert.That(SentinelProfile.CanFire(1000f, -30f, .996f, true, 1f, 300), "clean shot");
            TestAssert.That(!SentinelProfile.CanFire(1501f, -30f, .996f, true, 1f, 300), "out of range");
            TestAssert.That(!SentinelProfile.CanFire(29f, -30f, .996f, true, 1f, 300), "inside min range");
            TestAssert.That(!SentinelProfile.CanFire(1000f, 25f, .996f, true, 1f, 300), "above the belly arc");
            TestAssert.That(!SentinelProfile.CanFire(1000f, -30f, .99f, true, 1f, 300), "turret still slewing");
            TestAssert.That(!SentinelProfile.CanFire(1000f, -30f, .996f, false, 1f, 300), "no line of sight");
            TestAssert.That(!SentinelProfile.CanFire(1000f, -30f, .996f, true, .5f, 300), "rotors not spun up");
            TestAssert.That(!SentinelProfile.CanFire(1000f, -30f, .996f, true, 1f, 0), "dry");
            TestAssert.Near(SentinelProfile.ShotDelay(11), .05f, "inside a burst");
            TestAssert.Near(SentinelProfile.ShotDelay(12), 1.1f, "burst of 12 then cool");
            TestAssert.Near(SentinelProfile.Rounds / (float)SentinelProfile.BurstRounds, 25f, "300 rounds = 25 bursts");

            Vector3 g = new Vector3(0f, -9.81f, 0f);
            AssertVec(SentinelProfile.Lead(new Vector3(0f, 0f, 1100f), new Vector3(30f, 0f, 0f), 1100f, g), new Vector3(30f, 4.905f, 1100f), "lead + drop over 1 s");
            AssertVec(SentinelProfile.Lead(new Vector3(0f, 0f, 3300f), Vector3.zero, 1100f, g), new Vector3(0f, 11.036f, 3300f), "flight time capped 1.5 s");
            Vector2 down = SentinelProfile.Angles(new Vector3(0f, -1f, 1f));
            TestAssert.Near(down.x, 0f, "dead ahead yaw");
            TestAssert.Near(down.y, -45f, "45 deg down");
            TestAssert.Near(SentinelProfile.Angles(new Vector3(1f, 0f, 0f)).x, 90f, "starboard is +90");
            TestAssert.Near(SentinelProfile.Angles(new Vector3(0f, 0f, -1f)).x, 180f, "astern");

            TestAssert.Near(SentinelProfile.Unfold(0f), 0f, "stowed at brake");
            TestAssert.Near(SentinelProfile.Unfold(.6f), .5f, "half out");
            TestAssert.Near(SentinelProfile.Unfold(2f), 1f, "deployed");
            TestAssert.Near(SentinelProfile.Spin(1.2f), 0f, "rotors start after unfold");
            TestAssert.Near(SentinelProfile.Spin(1.95f), .5f, "spooling");
            TestAssert.Near(SentinelProfile.Spin(5f), 1f, "full spin");
        }

        // Point mass relative to the park point with quadratic drag and a 0.3 m/s2 crosswind push, 150 s.
        private static void Settles(Vector3 pos, Vector3 vel, float parkWithin, string label)
        {
            Vector3 wind = new Vector3(0f, 0f, .3f);
            const float dt = .02f;
            float dwell = 0f, parkedAt = -1f, lowest = pos.y, overshoot = 0f;
            for (float t = 0f; t < 150f; t += dt)
            {
                Vector3 a = SentinelProfile.HoldAccel(-pos, vel) - vel * vel.magnitude * .002f + wind;
                vel += a * dt;
                pos += vel * dt;
                lowest = Mathf.Min(lowest, pos.y);
                overshoot = Mathf.Max(overshoot, pos.x);
                Vector3 flat = new Vector3(pos.x, 0f, pos.z);
                dwell = SentinelProfile.NextDwell(dwell, flat.magnitude, new Vector3(vel.x, 0f, vel.z).magnitude, dt);
                if (parkedAt < 0f && dwell >= SentinelProfile.ParkDwell) parkedAt = t;
            }
            TestAssert.That(parkedAt >= 0f && parkedAt < parkWithin, label + ": parks in time (got " + parkedAt + ")");
            TestAssert.That(pos.magnitude < 3f && vel.magnitude < .2f, label + ": holds station against wind (err " + pos + ")");
            TestAssert.That(lowest > -10f, label + ": never sinks below the hover height (lowest " + lowest + ")");
            TestAssert.That(overshoot < 40f, label + ": no big overshoot past the point (" + overshoot + ")");
        }

        private static void AssertVec(Vector3 actual, Vector3 expected, string message) =>
            TestAssert.That((actual - expected).magnitude < .01f, message + " (got " + actual.ToString("F3") + ", want " + expected.ToString("F3") + ")");
    }
}
