using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>CG-9 HAMILCAR cargo glider: identity, constants, reach and landing centre, spot search, speed and height path, phase machine, joint timeline, outer panel stow, HUD words.</summary>
    internal static class HamilcarTests
    {
        public static void Run()
        {
            Identity();
            Constants();
            ReachAndCentre();
            Candidates();
            Speeds();
            Heights();
            Phases();
            Poses();
            Stow();
            Hud();
        }

        private static void Identity()
        {
            TestAssert.That(VanguardKeys.Hamilcar == "VG_Hamilcar", "the jsonKey is VG_Hamilcar");
            TestAssert.That(VanguardKeys.RoleOf(VanguardKeys.Hamilcar) == VanguardRole.Lander, "HAMILCAR is the lander");
            TestAssert.That((int)VanguardRole.Lander == (int)VanguardRole.Gunship + 1, "Lander is appended at the end of the role enum");
            TestAssert.That(!VanguardKeys.IsGunDrone(VanguardRole.Lander), "a lander is not a gun drone");
        }

        private static void Constants()
        {
            TestAssert.That(HamilcarProfile.GlideRatio == 15f && HamilcarProfile.MaxReach == 45000f && HamilcarProfile.StandOff == 250f, "reach 15 : 1, 45 km cap, 250 m stand-off");
            TestAssert.That(HamilcarProfile.RingStep == 40f && HamilcarProfile.RingMax == 240f && HamilcarProfile.RingPoints == 8, "ring search 40 m steps to 240 m, 8 points");
            TestAssert.That(HamilcarProfile.BackStep == 150f && HamilcarProfile.BackMax == 1500f, "back-line search 150 m steps to 1.5 km");
            TestAssert.That(HamilcarProfile.GlideSpeed == 120f && HamilcarProfile.FinalSpeed == 60f && HamilcarProfile.TouchSpeed == 34f, "speeds 120 / 60 / 34 m/s");
            TestAssert.That(HamilcarProfile.GateRange == 700f && HamilcarProfile.GateHeight == 70f && HamilcarProfile.FlareRange == 160f && HamilcarProfile.FlareHeight == 9f,
                "gate 700 m / 70 m, flare 160 m / 9 m");
            TestAssert.That(HamilcarProfile.TouchSink == 1.5f && HamilcarProfile.SlideDecel == 12f && HamilcarProfile.MaxAccel == 25f && HamilcarProfile.MaxTurnRate == 12f,
                "touch sink 1.5 m/s, slide 12 m/s2, servo 25 m/s2, turn 12 deg/s");
            TestAssert.That(HamilcarProfile.DeployStart == 0.4f && HamilcarProfile.DeployEnd == 1.6f, "wing unfold 0.4..1.6 s after release");
            TestAssert.That(HamilcarProfile.UnloadSeconds == 3.2f && HamilcarProfile.RollOff == 6f && HamilcarProfile.HuskSeconds == 60f && HamilcarProfile.RestHeight == 0.03f,
                "unload 3.2 s, roll-off 6 m, husk 60 s, rest height 3 cm");
            TestAssert.Near(HamilcarProfile.SlideDistance, 48.17f, "slide distance 34^2 / (2 x 12)");
            TestAssert.Near(HamilcarProfile.SlideSeconds, 2.83f, "slide time 34 / 12");
            TestAssert.That(HamilcarProfile.PropRpm == 1500f && HamilcarProfile.PropSpinUp == 1f, "propeller 1500 rpm, up to speed in 1 s");
            TestAssert.Eq(HamilcarProfile.CandidateCount, 59, "1 centre + 6 rings x 8 + 10 back steps");
        }

        private static void ReachAndCentre()
        {
            TestAssert.Near(HamilcarProfile.Reach(0f), 0f, "no height, no reach");
            TestAssert.Near(HamilcarProfile.Reach(-50f), 0f, "below the spot: no reach");
            TestAssert.Near(HamilcarProfile.Reach(1000f), 15000f, "1000 m of height carries 15 km");
            TestAssert.Near(HamilcarProfile.Reach(3000f), 45000f, "3000 m reaches the cap exactly");
            TestAssert.Near(HamilcarProfile.Reach(8000f), 45000f, "reach is capped at 45 km");

            // Normal case: 250 m short of the target on the flat launch -> target line, at the target's height.
            Vector3 centre = HamilcarProfile.Centre(new Vector3(0f, 3000f, 0f), new Vector3(0f, 12f, 12000f), 30000f, out bool isShort);
            TestAssert.That(!isShort, "a target inside reach is not short");
            TestAssert.Near(centre.x, 0f, "centre x on the line");
            TestAssert.Near(centre.z, 11750f, "centre is 250 m short of a target 12 km out");
            TestAssert.Near(centre.y, 12f, "centre keeps the target's height");

            // Diagonal: the line is flat, the launch height does not shorten it.
            centre = HamilcarProfile.Centre(new Vector3(100f, 2500f, 200f), new Vector3(3100f, 0f, 4200f), 25000f, out isShort);
            TestAssert.That(!isShort, "a 5 km target is not short");
            TestAssert.Near(centre.x, 100f + 2850f, "diagonal centre x (0.6 of 4750 m)");
            TestAssert.Near(centre.z, 200f + 3800f, "diagonal centre z (0.8 of 4750 m)");
            TestAssert.Near(Vector2.Distance(new Vector2(centre.x, centre.z), new Vector2(3100f, 4200f)), HamilcarProfile.StandOff, "the centre is exactly the stand-off from the target");

            // Beyond reach: clamp on the line, flagged.
            centre = HamilcarProfile.Centre(new Vector3(0f, 1000f, 0f), new Vector3(0f, 0f, 20000f), HamilcarProfile.Reach(1000f), out isShort);
            TestAssert.That(isShort, "a 20 km target from 1000 m is short");
            TestAssert.Near(centre.z, 15000f, "clamped to the 15 km reach");
            TestAssert.Near(Vector2.Distance(new Vector2(centre.x, centre.z), new Vector2(0f, 20000f)), 5000f, "the short gap is the rest of the line");

            // The boundary: the landing centre exactly at reach is still a normal landing.
            HamilcarProfile.Centre(Vector3.zero, new Vector3(0f, 0f, 10250f), 10000f, out isShort);
            TestAssert.That(!isShort, "centre exactly at reach is not short");
            centre = HamilcarProfile.Centre(Vector3.zero, new Vector3(0f, 0f, 10251f), 10000f, out isShort);
            TestAssert.That(isShort && Mathf.Abs(centre.z - 10000f) < 0.01f, "one metre more is short and clamps to reach");

            // A target nearer than the stand-off: land on the launch point, never behind it.
            centre = HamilcarProfile.Centre(new Vector3(50f, 500f, 50f), new Vector3(50f, 0f, 150f), 5000f, out isShort);
            TestAssert.That(!isShort && Mathf.Abs(centre.x - 50f) < 0.01f && Mathf.Abs(centre.z - 50f) < 0.01f, "a target inside the stand-off lands at the launch point");
            centre = HamilcarProfile.Centre(new Vector3(7f, 500f, 9f), new Vector3(7f, 0f, 9f), 5000f, out isShort);
            TestAssert.That(!isShort && !float.IsNaN(centre.x) && !float.IsNaN(centre.z), "a target straight below has no direction but must not produce NaN");
        }

        private static void Candidates()
        {
            TestAssert.That(HamilcarProfile.Candidate(0, 0f) == Vector2.zero && HamilcarProfile.Candidate(0, 0.73f) == Vector2.zero, "candidate 0 is the centre whatever the seed");

            // Ring 1 at seed 0: (right, forward), starting dead ahead and turning clockwise.
            AssertVec(HamilcarProfile.Candidate(1, 0f), 0f, 40f, "ring 1 point 0 is 40 m ahead");
            AssertVec(HamilcarProfile.Candidate(3, 0f), 40f, 0f, "ring 1 point 2 is 40 m to the right");
            AssertVec(HamilcarProfile.Candidate(5, 0f), 0f, -40f, "ring 1 point 4 is 40 m behind");
            AssertVec(HamilcarProfile.Candidate(7, 0f), -40f, 0f, "ring 1 point 6 is 40 m to the left");
            AssertVec(HamilcarProfile.Candidate(2, 0f), 28.284f, 28.284f, "ring 1 point 1 is on the front-right diagonal");

            // Radii and counts: six rings of eight, 40 m apart; the seed only turns the ring.
            for (int i = 1; i <= 48; i++)
            {
                float radius = 40f * (1 + (i - 1) / 8);
                TestAssert.Near(HamilcarProfile.Candidate(i, 0f).magnitude, radius, "candidate " + i + " radius at seed 0");
                TestAssert.Near(HamilcarProfile.Candidate(i, 0.37f).magnitude, radius, "candidate " + i + " radius at seed .37");
            }
            AssertVec(HamilcarProfile.Candidate(48, 0f), -240f * Mathf.Sin(Mathf.PI / 4f), 240f * Mathf.Cos(Mathf.PI / 4f), "ring 6 point 7 is front-left at 240 m");
            AssertVec(HamilcarProfile.Candidate(1, 0.125f), 28.284f, 28.284f, "a seed of 1/8 turns the ring one point clockwise");
            AssertVec(HamilcarProfile.Candidate(1, 0.25f), 40f, 0f, "a seed of 1/4 turns the ring a quarter turn");
            TestAssert.That((HamilcarProfile.Candidate(5, 0.2f) - HamilcarProfile.Candidate(5, 0f)).magnitude > 1f, "a different seed gives different points");

            // Back line: toward the launch along the approach, 150 m steps to 1.5 km, never rotated by the seed.
            AssertVec(HamilcarProfile.Candidate(49, 0f), 0f, -150f, "first back step is 150 m behind the centre");
            AssertVec(HamilcarProfile.Candidate(53, 0.6f), 0f, -750f, "fifth back step, seed ignored");
            AssertVec(HamilcarProfile.Candidate(58, 0f), 0f, -1500f, "last back step is 1.5 km behind");
            TestAssert.That(HamilcarProfile.Candidate(-1, 0f) == Vector2.zero && HamilcarProfile.Candidate(59, 0f) == Vector2.zero, "out of range reads as the centre");

            // Every candidate is distinct from the others (no duplicated work in the search).
            for (int i = 0; i < HamilcarProfile.CandidateCount; i++)
                for (int j = i + 1; j < HamilcarProfile.CandidateCount; j++)
                    TestAssert.That((HamilcarProfile.Candidate(i, 0.3f) - HamilcarProfile.Candidate(j, 0.3f)).magnitude > 1f, "candidates " + i + " and " + j + " differ");
        }

        private static void Speeds()
        {
            // The cap: a glider only gains speed by descending.
            TestAssert.Near(HamilcarProfile.SpeedCap(0f, 0f), 35f, "a hover release starts at the 35 m/s floor");
            TestAssert.Near(HamilcarProfile.SpeedCap(20f, 0f), 35f, "a slow release is floored at 35 m/s");
            TestAssert.Near(HamilcarProfile.SpeedCap(120f, 0f), 120f, "a fast release keeps its speed at zero drop");
            TestAssert.Near(HamilcarProfile.SpeedCap(50f, -30f), 50f, "climbing does not subtract speed");
            TestAssert.Near(HamilcarProfile.SpeedCap(0f, 100f), Mathf.Sqrt(2f * 9.81f * 0.8f * 100f), "100 m of drop from rest: sqrt(2 g 0.8 h)");
            TestAssert.Near(HamilcarProfile.SpeedCap(50f, 100f), Mathf.Sqrt(2500f + 2f * 9.81f * 0.8f * 100f), "release speed and drop add as energy");
            TestAssert.Near(HamilcarProfile.SpeedCap(0f, 1000f), 125.28f, "1000 m of drop from rest passes the cruise speed");
            TestAssert.That(HamilcarProfile.SpeedCap(40f, 10f) > HamilcarProfile.SpeedCap(40f, 0f) && HamilcarProfile.SpeedCap(40f, 500f) > HamilcarProfile.SpeedCap(40f, 10f), "the cap grows with the drop");
            TestAssert.That(HamilcarProfile.SpeedCap(0f, 10f) >= 35f, "never below 35 m/s");

            // Phase speed.
            TestAssert.Near(HamilcarProfile.PhaseSpeed(HamilcarPhase.Release, 20000f), 120f, "release asks for cruise");
            TestAssert.Near(HamilcarProfile.PhaseSpeed(HamilcarPhase.Glide, 20000f), 120f, "glide is 120 m/s");
            TestAssert.Near(HamilcarProfile.PhaseSpeed(HamilcarPhase.Glide, 800f), 120f, "glide is 120 m/s at any range");
            TestAssert.Near(HamilcarProfile.PhaseSpeed(HamilcarPhase.Final, 700f), 120f, "final starts at 120 m/s at the gate");
            TestAssert.Near(HamilcarProfile.PhaseSpeed(HamilcarPhase.Final, 430f), 90f, "final is linear: 90 m/s half-way to the flare");
            TestAssert.Near(HamilcarProfile.PhaseSpeed(HamilcarPhase.Final, 160f), 60f, "final ends at 60 m/s at the flare start");
            TestAssert.Near(HamilcarProfile.PhaseSpeed(HamilcarPhase.Final, 900f), 120f, "final is clamped outside the gate");
            TestAssert.Near(HamilcarProfile.PhaseSpeed(HamilcarPhase.Final, 100f), 60f, "final is clamped inside the flare start");
            TestAssert.Near(HamilcarProfile.PhaseSpeed(HamilcarPhase.Flare, 160f), 60f, "flare starts at 60 m/s");
            TestAssert.Near(HamilcarProfile.PhaseSpeed(HamilcarPhase.Flare, 80f), 47f, "flare is linear: 47 m/s half-way");
            TestAssert.Near(HamilcarProfile.PhaseSpeed(HamilcarPhase.Flare, 0f), 34f, "flare ends at the 34 m/s touch speed");
            TestAssert.Near(HamilcarProfile.PhaseSpeed(HamilcarPhase.Slide, 0f), 34f, "the slide starts at the touch speed");
            TestAssert.Near(HamilcarProfile.PhaseSpeed(HamilcarPhase.Slide, HamilcarProfile.SlideDistance / 2f), Mathf.Sqrt(578f), "half the slide distance: sqrt(34^2 - 2 x 12 x 24.08)");
            TestAssert.That(HamilcarProfile.PhaseSpeed(HamilcarPhase.Slide, HamilcarProfile.SlideDistance) < 0.05f, "the slide reaches zero after 48.17 m");
            TestAssert.That(HamilcarProfile.PhaseSpeed(HamilcarPhase.Slide, 60f) == 0f, "the slide never goes negative");
            TestAssert.That(HamilcarProfile.PhaseSpeed(HamilcarPhase.Unload, 0f) == 0f && HamilcarProfile.PhaseSpeed(HamilcarPhase.Husk, 0f) == 0f && HamilcarProfile.PhaseSpeed(HamilcarPhase.Lost, 0f) == 120f,
                "stopped phases ask for zero; a lost glider keeps cruising");

            // The slide integrates to a stop inside the quoted distance and time.
            float slid = 0f, t = 0f, speed = HamilcarProfile.TouchSpeed;
            for (; t < 3.5f; t += 0.02f)
            {
                speed = HamilcarProfile.PhaseSpeed(HamilcarPhase.Slide, slid);
                slid += speed * 0.02f;
            }
            TestAssert.That(speed < 0.2f, "after 3.5 s the slide has stopped (" + speed + " m/s)");
            TestAssert.That(slid > HamilcarProfile.SlideDistance - 0.3f && slid < HamilcarProfile.SlideDistance + 0.3f, "it stops about 48 m after touchdown (" + slid + " m)");
        }

        private static void Heights()
        {
            TestAssert.Near(HamilcarProfile.PathHeight(HamilcarPhase.Final, 700f, 999f), 70f, "the gate line starts at 70 m over the touchdown point");
            TestAssert.Near(HamilcarProfile.PathHeight(HamilcarPhase.Final, 430f, 999f), 39.5f, "half-way down the gate line");
            TestAssert.Near(HamilcarProfile.PathHeight(HamilcarPhase.Final, 160f, 999f), 9f, "the gate line ends at 9 m at the flare start");
            TestAssert.Near(HamilcarProfile.PathHeight(HamilcarPhase.Final, 1000f, 999f), 70f, "the gate line is clamped outside the gate");
            TestAssert.Near(HamilcarProfile.PathHeight(HamilcarPhase.Flare, 160f, 999f), 9f, "the flare curve starts where the gate line ends");
            TestAssert.Near(HamilcarProfile.PathHeight(HamilcarPhase.Flare, 80f, 999f), 4.0147f, "flare curve half-way (80 m out)");
            TestAssert.Near(HamilcarProfile.PathHeight(HamilcarPhase.Flare, 0f, 999f), 0f, "the flare curve ends on the ground");
            TestAssert.Near(HamilcarProfile.PathHeight(HamilcarPhase.Flare, -5f, 999f), 0f, "past the touchdown point the path stays on the ground");

            // The glide line comes from the caller but never drops under the gate height before the gate.
            TestAssert.Near(HamilcarProfile.PathHeight(HamilcarPhase.Glide, 5000f, 640f), 640f, "glide follows the caller's slope");
            TestAssert.Near(HamilcarProfile.PathHeight(HamilcarPhase.Release, 5000f, 1200f), 1200f, "release follows the caller's slope");
            TestAssert.Near(HamilcarProfile.PathHeight(HamilcarPhase.Glide, 5000f, 30f), 70f, "glide never commands less than the gate height");
            TestAssert.That(HamilcarProfile.PathHeight(HamilcarPhase.Slide, 10f, 999f) == 0f && HamilcarProfile.PathHeight(HamilcarPhase.Unload, 0f, 999f) == 0f &&
                HamilcarProfile.PathHeight(HamilcarPhase.Husk, 0f, 999f) == 0f, "on the ground the path is the ground");

            // The sink limit: only as much sink as a steady 12 m/s2 brings back to the floor sink by the floor of the phase.
            TestAssert.Near(HamilcarProfile.SinkLimit(0f), 7f, "on the floor only the floor sink is left");
            TestAssert.Near(HamilcarProfile.SinkLimit(-20f), 7f, "under the floor it is the floor sink too");
            TestAssert.Near(HamilcarProfile.SinkLimit(54f), 43f, "54 m above the floor: 7 + sqrt(2 x 12 x 54)");
            TestAssert.That(HamilcarProfile.ArrestDecel <= HamilcarProfile.MaxAccel * 0.5f, "stopping the sink takes at most half of the servo's authority");
            for (float d = HamilcarProfile.FlareRange; d <= HamilcarProfile.GateRange; d += 20f)
                TestAssert.That(HamilcarProfile.PhaseSpeed(HamilcarPhase.Final, d) * (HamilcarProfile.GateHeight - HamilcarProfile.FlareHeight) /
                    (HamilcarProfile.GateRange - HamilcarProfile.FlareRange) <= HamilcarProfile.SinkLimit(HamilcarProfile.PathHeight(HamilcarPhase.Final, d, 999f) - HamilcarProfile.FlareHeight),
                    "the gate line's own sink is inside the limit " + d + " m out");

            // Walk the path at the commanded speed: height falls all the way, touchdown at 34 m/s and 1.5 m/s sink.
            float distance = HamilcarProfile.GateRange, height = HamilcarProfile.GateHeight, sink = 0f, speed;
            bool falling = true;
            while (true)
            {
                speed = HamilcarProfile.PhaseSpeed(distance > HamilcarProfile.FlareRange ? HamilcarPhase.Final : HamilcarPhase.Flare, distance);
                float next = distance - speed * 0.02f;
                if (next <= 0f) break; // the last full step before the ground
                float nextHeight = HamilcarProfile.PathHeight(next > HamilcarProfile.FlareRange ? HamilcarPhase.Final : HamilcarPhase.Flare, next, 999f);
                sink = (height - nextHeight) / 0.02f;
                if (nextHeight > height + 1e-4f) falling = false;
                height = nextHeight;
                distance = next;
            }
            TestAssert.That(falling, "the path only ever descends");
            TestAssert.That(height >= 0f && height < 0.1f, "the walk ends on the ground (" + height + " m left)");
            TestAssert.That(Mathf.Abs(speed - 34f) < 0.3f, "touchdown speed is about 34 m/s (" + speed + ")");
            TestAssert.That(Mathf.Abs(sink - 1.5f) < 0.2f, "touchdown sink is about 1.5 m/s (" + sink + ")");
        }

        private static void Phases()
        {
            HamilcarPhase Step(HamilcarPhase p, float age = 10f, float distance = 5000f, float agl = 500f, float speed = 120f, float phaseAge = 0f, bool lz = true) =>
                HamilcarProfile.Next(p, age, distance, agl, speed, phaseAge, lz);

            TestAssert.Eq(Step(HamilcarPhase.Release, age: 1.59f), HamilcarPhase.Release, "release lasts until the wing is out");
            TestAssert.Eq(Step(HamilcarPhase.Release, age: 1.6f), HamilcarPhase.Glide, "the wing is out at 1.6 s: glide");
            TestAssert.Eq(Step(HamilcarPhase.Glide, distance: 701f), HamilcarPhase.Glide, "glide outside the gate");
            TestAssert.Eq(Step(HamilcarPhase.Glide, distance: 700f), HamilcarPhase.Final, "final inside the 700 m gate");
            TestAssert.Eq(Step(HamilcarPhase.Final, distance: 161f), HamilcarPhase.Final, "final outside the flare start");
            TestAssert.Eq(Step(HamilcarPhase.Final, distance: 160f), HamilcarPhase.Flare, "flare inside 160 m");
            TestAssert.Eq(Step(HamilcarPhase.Flare, agl: 0.5f), HamilcarPhase.Flare, "flare until the keel is down");
            TestAssert.Eq(Step(HamilcarPhase.Flare, agl: 0.2f), HamilcarPhase.Flare, "0.2 m is still in the air");
            TestAssert.Eq(Step(HamilcarPhase.Flare, agl: 0.17f), HamilcarPhase.Slide, "0.18 m or less above the ground (rest height + 0.15): slide");
            TestAssert.Eq(Step(HamilcarPhase.Slide, speed: 0.3f), HamilcarPhase.Slide, "sliding while moving");
            TestAssert.Eq(Step(HamilcarPhase.Slide, speed: 0.2f), HamilcarPhase.Unload, "stopped at 0.2 m/s: unload");
            TestAssert.Eq(Step(HamilcarPhase.Unload, phaseAge: 3.19f, speed: 0f), HamilcarPhase.Unload, "unload takes 3.2 s");
            TestAssert.Eq(Step(HamilcarPhase.Unload, phaseAge: 3.2f, speed: 0f), HamilcarPhase.Husk, "3.2 s later it is a husk");
            TestAssert.Eq(Step(HamilcarPhase.Husk, phaseAge: 100f, speed: 0f), HamilcarPhase.Husk, "a husk stays a husk");
            TestAssert.Eq(Step(HamilcarPhase.Lost, agl: 0f, lz: false), HamilcarPhase.Lost, "lost is final");

            // Only one step per call, and the phases do not look ahead.
            TestAssert.Eq(Step(HamilcarPhase.Release, age: 5f, distance: 100f, agl: 0f), HamilcarPhase.Glide, "release goes through glide even when already at the flare range");
            TestAssert.Eq(Step(HamilcarPhase.Glide, age: 5f, distance: 100f, agl: 0f), HamilcarPhase.Final, "glide goes through final");
            TestAssert.Eq(Step(HamilcarPhase.Final, age: 5f, distance: 100f, agl: 0f), HamilcarPhase.Flare, "final goes through flare");
            TestAssert.Eq(Step(HamilcarPhase.Glide, speed: 0f), HamilcarPhase.Glide, "speed alone does not stop a gliding glider");
            TestAssert.Eq(Step(HamilcarPhase.Slide, agl: 0f, distance: 0f, speed: 20f), HamilcarPhase.Slide, "a slide does not end by position");

            // No landing zone: lost at 60 m from any airborne phase, never after touchdown.
            foreach (HamilcarPhase airborne in new[] { HamilcarPhase.Release, HamilcarPhase.Glide, HamilcarPhase.Final, HamilcarPhase.Flare })
            {
                TestAssert.Eq(Step(airborne, agl: 60f, lz: false, age: 0f), HamilcarPhase.Lost, airborne + " without a zone at 60 m is lost");
                TestAssert.That(Step(airborne, agl: 60.1f, lz: false, age: 0f, distance: 5000f) != HamilcarPhase.Lost, airborne + " without a zone at 61 m flies on");
                TestAssert.That(Step(airborne, agl: 10f, lz: true, age: 0f, distance: 5000f) != HamilcarPhase.Lost, airborne + " with a zone is never lost");
            }
            TestAssert.Eq(Step(HamilcarPhase.Glide, agl: 59f, lz: false, distance: 800f), HamilcarPhase.Lost, "lost beats the gate");
            TestAssert.Eq(Step(HamilcarPhase.Slide, agl: 0.1f, lz: false, speed: 10f), HamilcarPhase.Slide, "a slide without a zone is not lost");
            TestAssert.Eq(Step(HamilcarPhase.Unload, agl: 0.1f, lz: false, speed: 0f, phaseAge: 1f), HamilcarPhase.Unload, "unloading without a zone is not lost");
            TestAssert.Eq(Step(HamilcarPhase.Husk, agl: 0.1f, lz: false, speed: 0f), HamilcarPhase.Husk, "a husk is not lost");
        }

        private static void Poses()
        {
            // Wing fold follows the age: stowed (1) before 0.4 s, out (0) from 1.6 s, smooth in between.
            TestAssert.Near(HamilcarProfile.Pose(HamilcarPhase.Release, 0f, 0f).Fold, 1f, "stowed at release");
            TestAssert.Near(HamilcarProfile.Pose(HamilcarPhase.Release, 0.4f, 0.4f).Fold, 1f, "still stowed at the deploy start");
            TestAssert.Near(HamilcarProfile.Pose(HamilcarPhase.Release, 1.0f, 1.0f).Fold, 0.5f, "half folded half-way through the unfold");
            TestAssert.Near(HamilcarProfile.Pose(HamilcarPhase.Release, 1.6f, 1.6f).Fold, 0f, "out at the deploy end");
            TestAssert.Near(HamilcarProfile.Pose(HamilcarPhase.Glide, 30f, 28f).Fold, 0f, "out for the rest of the flight");
            TestAssert.Near(HamilcarProfile.Pose(HamilcarPhase.Unload, 200f, 1f).Fold, 0f, "still out while unloading");
            TestAssert.Near(HamilcarProfile.Pose(HamilcarPhase.Release, -1f, 0f).Fold, 1f, "a negative age reads stowed");
            float previous = 2f;
            for (float age = 0f; age <= 2f; age += 0.05f)
            {
                float fold = HamilcarProfile.Pose(HamilcarPhase.Release, age, age).Fold;
                TestAssert.That(fold <= previous + 1e-5f, "the fold never reopens (age " + age + ")");
                previous = fold;
            }

            // The cargo end stays shut until the unload.
            HamilcarPose cruise = HamilcarProfile.Pose(HamilcarPhase.Glide, 20f, 18f);
            TestAssert.That(cruise.Visor == 0f && cruise.Ramp == 0f && cruise.Roll == 0f, "glide: visor and ramp shut, no roll");
            HamilcarPose final = HamilcarProfile.Pose(HamilcarPhase.Final, 40f, 5f);
            TestAssert.That(final.Visor == 0f && final.Ramp == 0f && final.Roll == 0f, "final: all shut");
            HamilcarPose flare = HamilcarProfile.Pose(HamilcarPhase.Flare, 44f, 2f);
            TestAssert.That(flare.Visor == 0f && flare.Ramp == 0f && flare.Roll == 0f, "flare: all shut");
            TestAssert.That(HamilcarProfile.Pose(HamilcarPhase.Slide, 44f, 2f).Visor == 0f && HamilcarProfile.Pose(HamilcarPhase.Slide, 44f, 2f).Roll == 0f, "the cargo end is shut until the unload");

            // The propeller: still through the release, up to speed in the second after the wing is out, full until touchdown,
            // run down over the 2.83 s slide, still while unloading and on the husk.
            TestAssert.That(HamilcarProfile.Pose(HamilcarPhase.Release, 0f, 0f).Spin == 0f, "the propeller is still as it leaves the hold");
            TestAssert.That(HamilcarProfile.Pose(HamilcarPhase.Release, 1.0f, 1.0f).Spin == 0f, "still while the wing unfolds");
            TestAssert.That(HamilcarProfile.Pose(HamilcarPhase.Release, 1.6f, 1.6f).Spin == 0f, "still at the end of the unfold");
            TestAssert.Near(HamilcarProfile.Pose(HamilcarPhase.Glide, 2.1f, 0.5f).Spin, 0.5f, "half speed half a second after the wing is out");
            TestAssert.Near(HamilcarProfile.Pose(HamilcarPhase.Glide, 2.6f, 1f).Spin, 1f, "full speed a second after the wing is out");
            TestAssert.That(cruise.Spin == 1f && final.Spin == 1f && flare.Spin == 1f, "full speed through the glide, final and flare");
            TestAssert.Near(HamilcarProfile.Pose(HamilcarPhase.Slide, 46f, 0f).Spin, 1f, "still at full speed at touchdown");
            TestAssert.Near(HamilcarProfile.Pose(HamilcarPhase.Slide, 46f, HamilcarProfile.SlideSeconds / 2f).Spin, 0.5f, "half speed half-way through the slide");
            TestAssert.That(HamilcarProfile.Pose(HamilcarPhase.Slide, 49f, HamilcarProfile.SlideSeconds).Spin == 0f, "stopped by the end of the slide");
            TestAssert.That(HamilcarProfile.Pose(HamilcarPhase.Slide, 52f, 6f).Spin == 0f, "a long slide does not start it again");
            float turning = 2f;
            for (float t = 0f; t <= 3.4f; t += 0.02f)
            {
                float spin = HamilcarProfile.Pose(HamilcarPhase.Slide, 46f + t, t).Spin;
                TestAssert.That(spin <= turning + 1e-5f && spin >= 0f && spin <= 1f, "the propeller only runs down on the slide (t " + t + ")");
                turning = spin;
            }
            TestAssert.That(HamilcarProfile.Pose(HamilcarPhase.Unload, 50f, 0f).Spin == 0f && HamilcarProfile.Pose(HamilcarPhase.Unload, 50f, 2f).Spin == 0f,
                "the propeller does not turn while unloading");
            TestAssert.That(HamilcarProfile.Pose(HamilcarPhase.Husk, 90f, 0f).Spin == 0f, "the propeller does not turn on the husk");

            // The unload timeline (smoothstep): visor 0-1, ramp .6-1.4, roll 1.4-3.2.
            HamilcarPose u0 = HamilcarProfile.Pose(HamilcarPhase.Unload, 50f, 0f);
            TestAssert.That(u0.Visor == 0f && u0.Ramp == 0f && u0.Roll == 0f, "unload starts shut");
            HamilcarPose u3 = HamilcarProfile.Pose(HamilcarPhase.Unload, 50f, 0.3f);
            TestAssert.Near(u3.Visor, 0.216f, "visor 0.3 s in: smoothstep(0.3)");
            HamilcarPose u6 = HamilcarProfile.Pose(HamilcarPhase.Unload, 50f, 0.6f);
            TestAssert.Near(u6.Visor, 0.648f, "visor 0.6 s in: smoothstep(0.6)");
            TestAssert.Near(u6.Ramp, 0f, "ramp starts at 0.6 s");
            HamilcarPose u9 = HamilcarProfile.Pose(HamilcarPhase.Unload, 50f, 0.9f);
            TestAssert.Near(u9.Ramp, 0.3164f, "ramp 0.3 s in: smoothstep(0.375)");
            TestAssert.Near(HamilcarProfile.Pose(HamilcarPhase.Unload, 50f, 1.0f).Visor, 1f, "visor open at 1.0 s");
            HamilcarPose u14 = HamilcarProfile.Pose(HamilcarPhase.Unload, 50f, 1.4f);
            TestAssert.Near(u14.Ramp, 1f, "ramp down at 1.4 s");
            TestAssert.Near(u14.Roll, 0f, "the vehicle starts rolling at 1.4 s");
            TestAssert.Near(HamilcarProfile.Pose(HamilcarPhase.Unload, 50f, 2.3f).Roll, 0.5f, "half rolled at 2.3 s");
            HamilcarPose u32 = HamilcarProfile.Pose(HamilcarPhase.Unload, 50f, 3.2f);
            TestAssert.That(u32.Visor == 1f && u32.Ramp == 1f && u32.Roll == 1f, "everything is open and rolled out at 3.2 s");
            HamilcarPose husk = HamilcarProfile.Pose(HamilcarPhase.Husk, 90f, 0f);
            TestAssert.That(husk.Visor == 1f && husk.Ramp == 1f && husk.Roll == 1f && husk.Fold == 0f, "the husk stays open");

            // Nothing in the unload runs backwards or leaves 0..1.
            HamilcarPose last = u0;
            for (float t = 0f; t <= 3.4f; t += 0.02f)
            {
                HamilcarPose p = HamilcarProfile.Pose(HamilcarPhase.Unload, 50f, t);
                TestAssert.That(p.Visor >= last.Visor - 1e-5f && p.Ramp >= last.Ramp - 1e-5f && p.Roll >= last.Roll - 1e-5f, "the unload only opens (t " + t + ")");
                TestAssert.That(p.Visor >= 0f && p.Visor <= 1f && p.Ramp >= 0f && p.Ramp <= 1f && p.Roll >= 0f && p.Roll <= 1f &&
                    p.Spin == 0f && p.Fold >= 0f && p.Fold <= 1f, "joint fractions stay in 0..1 (t " + t + ")");
                last = p;
            }
        }

        // The outer panel stow as Unity turns it: Quaternion.AngleAxis is this formula (it cannot run outside the engine).
        private static Vector3 Turned(Vector3 v, Vector3 axis, float degrees)
        {
            float cos = Mathf.Cos(degrees * Mathf.Deg2Rad), sin = Mathf.Sin(degrees * Mathf.Deg2Rad);
            return v * cos + Vector3.Cross(axis, v) * sin + axis * (Vector3.Dot(axis, v) * (1f - cos));
        }

        private static void Stow()
        {
            // The glider's frame: x right (model +X), y up (model +Z), z forward (model +Y). hamilcar-model-v6.md section 3: a
            // stowed outer panel has swung aft and stands on edge beside its boom, leading edge down, upper surface outboard.
            Vector3 right = Vector3.right, up = Vector3.up, forward = Vector3.forward;
            TestAssert.That(HamilcarProfile.OuterStow == -120f, "the outer panels fold a third of a turn, Unity sense");
            TestAssert.Near(HamilcarProfile.OuterAxisR.magnitude, 1f, "starboard stow axis is a unit vector");
            TestAssert.Near(HamilcarProfile.OuterAxisL.magnitude, 1f, "port stow axis is a unit vector");
            AssertVec3(Turned(right, HamilcarProfile.OuterAxisR, HamilcarProfile.OuterStow), -forward, "starboard panel: the span swings aft");
            AssertVec3(Turned(forward, HamilcarProfile.OuterAxisR, HamilcarProfile.OuterStow), -up, "starboard panel: leading edge down");
            AssertVec3(Turned(up, HamilcarProfile.OuterAxisR, HamilcarProfile.OuterStow), right, "starboard panel: upper surface outboard");
            AssertVec3(Turned(-right, HamilcarProfile.OuterAxisL, HamilcarProfile.OuterStow), -forward, "port panel: the span swings aft");
            AssertVec3(Turned(forward, HamilcarProfile.OuterAxisL, HamilcarProfile.OuterStow), -up, "port panel: leading edge down");
            AssertVec3(Turned(up, HamilcarProfile.OuterAxisL, HamilcarProfile.OuterStow), -right, "port panel: upper surface outboard");
            AssertVec3(Turned(right, HamilcarProfile.OuterAxisR, 0f), right, "nothing moves with the fold at zero");
            // The two panels are mirror images: a mirrored turn keeps its angle and takes the mirrored axis reversed.
            AssertVec3(HamilcarProfile.OuterAxisL, -Vector3.Reflect(HamilcarProfile.OuterAxisR, right), "the stow axes mirror each other");
        }

        private static void Hud()
        {
            TestAssert.Eq(HamilcarProfile.HudText(12400f), "HAMILCAR 12.4 KM", "held line 12.4 km");
            TestAssert.Eq(HamilcarProfile.HudText(0f), "HAMILCAR 0.0 KM", "held line at zero");
            TestAssert.Eq(HamilcarProfile.HudText(4249f), "HAMILCAR 4.2 KM", "held line rounds down");
            TestAssert.Eq(HamilcarProfile.HudText(4251f), "HAMILCAR 4.3 KM", "held line rounds up");
            TestAssert.Eq(HamilcarProfile.HudText(100000f), "HAMILCAR 100.0 KM", "held line past 100 km");
            TestAssert.Eq(HamilcarProfile.Km(30000f), "30.0 KM", "km helper");

            TestAssert.Eq(HamilcarProfile.HudDetail(HamilcarPhase.Release), "GLIDE", "release reads GLIDE");
            TestAssert.Eq(HamilcarProfile.HudDetail(HamilcarPhase.Glide), "GLIDE", "glide reads GLIDE");
            TestAssert.Eq(HamilcarProfile.HudDetail(HamilcarPhase.Final), "FINAL", "final reads FINAL");
            TestAssert.Eq(HamilcarProfile.HudDetail(HamilcarPhase.Flare), "FLARE", "flare reads FLARE");
            TestAssert.Eq(HamilcarProfile.HudDetail(HamilcarPhase.Slide), "ROLLING OUT", "slide reads ROLLING OUT");
            TestAssert.Eq(HamilcarProfile.HudDetail(HamilcarPhase.Unload), "ROLLING OUT", "unload reads ROLLING OUT");
            TestAssert.Eq(HamilcarProfile.HudDetail(HamilcarPhase.Husk), "", "a husk has no held line");
            TestAssert.Eq(HamilcarProfile.HudDetail(HamilcarPhase.Lost), "", "a lost glider has no held line");

            TestAssert.Eq(HamilcarProfile.DownText, "HAMILCAR DOWN", "info notice title");
            TestAssert.Eq(HamilcarProfile.DownDetail, "HEXHOUND OUT", "info notice detail");
            TestAssert.Eq(HamilcarProfile.ShortText, "HAMILCAR SHORT", "caution notice title");
            TestAssert.Eq(HamilcarProfile.ShortDetail(4200f), "LZ 4.2 KM FROM TARGET", "caution notice detail");
            TestAssert.Eq(HamilcarProfile.ShortDetail(10000f), "LZ 10.0 KM FROM TARGET", "caution notice detail at 10 km");
            TestAssert.Eq(HamilcarProfile.LostText, "HAMILCAR LOST", "warning notice title");
            TestAssert.Eq(HamilcarProfile.LostDetail(false), "NO LANDING ZONE", "warning detail without a zone");
            TestAssert.Eq(HamilcarProfile.LostDetail(true), "SHOT DOWN", "warning detail when shot down");
        }

        private static void AssertVec(Vector2 actual, float x, float y, string message) =>
            TestAssert.That(Mathf.Abs(actual.x - x) < 0.01f && Mathf.Abs(actual.y - y) < 0.01f, message + " (got " + actual.ToString("F3") + ", want (" + x + ", " + y + "))");

        private static void AssertVec3(Vector3 actual, Vector3 want, string message) =>
            TestAssert.That((actual - want).magnitude < 0.001f, message + " (got " + actual.ToString("F3") + ", want " + want.ToString("F3") + ")");
    }
}
