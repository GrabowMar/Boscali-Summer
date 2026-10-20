using BoscaliSummer.Core.Math;
using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    /// <summary>MSL GUARD OFF and RESPONSE PRESS hold course with countermeasures only; INTERVAL scales the spacing preset;
    /// old WING/LEAD guards fly as ON.</summary>
    internal static class DefenceDoctrineTests
    {
        public static void Run()
        {
            // A member flying north at 200 m/s, a missile abeam to the east closing at 600 m/s; precision 1 reacts in 1 s.
            var s = new AircraftState { Pos = Vec3.Zero, Vel = new Vec3(0f, 0f, 200f), Fwd = new Vec3(0f, 0f, 1f) };
            var home = new RefState(new Vec3(0f, 0f, 200f), s.Vel, Vec3.Zero);

            DefenceCommand broke = Defend(3000f, WingMissileSeeker.Radar, false, false, s, home);
            TestAssert.That(broke.Active && broke.Full && !AtHome(broke, home), "default: a radar missile is notched at full power");

            DefenceCommand held = Defend(3000f, WingMissileSeeker.Radar, true, false, s, home);
            TestAssert.That(held.Active && AtHome(held, home) && !held.Full && !held.Idle && held.Countermeasures,
                "GUARD OFF: hold the slot, chaff inside 8 s, throttle left alone");
            TestAssert.That(!Defend(6000f, WingMissileSeeker.Radar, true, false, s, home).Countermeasures, "GUARD OFF: no chaff 10 s out");
            DefenceCommand heldIr = Defend(3000f, WingMissileSeeker.Infrared, true, false, s, home);
            TestAssert.That(heldIr.Active && AtHome(heldIr, home) && !heldIr.Idle && heldIr.Countermeasures, "GUARD OFF: flares, no idle");

            DefenceCommand pressed = Defend(3000f, WingMissileSeeker.Radar, false, true, s, home);
            TestAssert.That(pressed.Active && AtHome(pressed, home) && !pressed.Full && pressed.Countermeasures,
                "PRESS: hold heading against a radar missile 5 s out, chaff on");
            DefenceCommand late = Defend(1200f, WingMissileSeeker.Radar, false, true, s, home);
            TestAssert.That(late.Active && !AtHome(late, home), "PRESS: inside 3 s it breaks after all");
            DefenceCommand pressIr = Defend(3000f, WingMissileSeeker.Infrared, false, true, s, home);
            TestAssert.That(pressIr.Idle, "PRESS never holds against a heat-seeker");

            TestAssert.That(MissileDefence.HoldsCourse(true, false, WingMissileSeeker.Infrared, 1f) &&
                !MissileDefence.HoldsCourse(false, false, WingMissileSeeker.Radar, 10f) &&
                !MissileDefence.HoldsCourse(false, true, WingMissileSeeker.Radar, MissileDefence.PressBreakSeconds), "hold rule");

            TestAssert.Eq(WingDoctrineRules.SpacingScale(FormationInterval.Close), 0.75f, "CLOSE is 0.75 x the preset");
            TestAssert.Eq(WingDoctrineRules.SpacingScale(FormationInterval.Standard), 1f, "STD is the preset");
            TestAssert.Eq(WingDoctrineRules.SpacingScale(FormationInterval.Open), 1.5f, "OPEN is 1.5 x the preset");

            var legacy = new WingDoctrine(MissileGuard.Lead, MissileResponse.Break, FormationInterval.Standard, true,
                TargetPolicy.Cover, EngagementReach.Slot, WeaponsPolicy.Auto, RadarPolicy.On);
            TestAssert.That(legacy.Guard == MissileGuard.Self && legacy.PatternName == "ESCORT", "an old LEAD guard flies as ON");
            TestAssert.That(WingDoctrine.TryAxisValue(DoctrineAxis.Guard, "on", out byte on) && on == (byte)MissileGuard.Self,
                "\"guard on\" is understood");
        }

        /// <summary>A fresh defence's command after one 1 s step against a missile <paramref name="east"/> metres abeam.</summary>
        private static DefenceCommand Defend(float east, WingMissileSeeker seeker, bool guardOff, bool pressing,
            in AircraftState s, in RefState home)
        {
            var threat = new MissileThreat { Present = true, Id = 1, Pos = new Vec3(east, 0f, 0f), Vel = new Vec3(-600f, 0f, 0f), Seeker = seeker };
            return new MissileDefence().Step(threat, s, home, 1f, 1f, 0f, 0f, guardOff, pressing);
        }

        private static bool AtHome(in DefenceCommand c, in RefState home) => (c.Ref.Pos - home.Pos).Length < 1e-3f;
    }
}
