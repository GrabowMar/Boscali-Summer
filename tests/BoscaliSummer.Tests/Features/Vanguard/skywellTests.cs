using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins the SKYWELL v5 service zone, tender timeline and follow, hand-off clock, pit-stop planner and HUD copy with numbers.</summary>
    internal static class SkywellTests
    {
        private static bool Near(float a, float b, float eps = 0.01f) => Mathf.Abs(a - b) <= eps;

        private static PitStation A => new PitStation { Index = 0, Missing = 2, Full = 4, MassPerRound = 120f, CostPerRound = 0.1f };

        private static PitStation B => new PitStation { Index = 2, Missing = 1, Full = 2, MassPerRound = 120f, CostPerRound = 0.1f };

        private static PitStation One(int index) => new PitStation { Index = index, Missing = 1, Full = 2, MassPerRound = 100f };

        private static List<PitJob> Plan(PitOrder order, float need, float fraction, int priority = -1, float fuelStock = 8000f,
            int magazine = SkywellMagazine.Rounds, float funds = 0f, bool free = true, params PitStation[] stations)
        {
            var jobs = new List<PitJob>();
            int count = PitPlan.Plan(order, need, fraction, fuelStock, stations, priority, magazine, funds, free, jobs);
            TestAssert.That(count == jobs.Count, "Plan returns the list length");
            return jobs;
        }

        /// <summary>The service zone rule: where a receiver is served, how it is kept, how roll is read, what the director says.</summary>
        private static void RunZone()
        {
            Vector3 tow = SkywellZone.Tow;
            TestAssert.That(Near(tow.x, 0f) && Near(tow.y, -15f) && Near(tow.z, -40f), "the tow position is 40 m astern and 15 m below the deck");
            TestAssert.That(SkywellZone.Contains(tow, 0f, 0f) && SkywellZone.Contains(SkywellZone.HoldSlot, 5f, 10f), "the tow position and the AI hold slot are in the zone");
            TestAssert.That(SkywellZone.Contains(tow + new Vector3(120f, 0f, 0f), 0f, 0f) && !SkywellZone.Contains(tow + new Vector3(121f, 0f, 0f), 0f, 0f),
                "the zone is 120 m around the tow position");
            TestAssert.That(SkywellZone.Contains(tow, 14.9f, 0f) && !SkywellZone.Contains(tow, 15.1f, 0f), "relative speed must stay under 15 m/s");
            TestAssert.That(SkywellZone.Contains(tow, 0f, 24f) && SkywellZone.Contains(tow, 0f, -24f) && !SkywellZone.Contains(tow, 0f, 26f) && !SkywellZone.Contains(tow, 0f, -26f),
                "roll must stay under 25 deg either way");
            TestAssert.That(SkywellZone.Contains(new Vector3(0f, -15f, -25f), 0f, 0f) && !SkywellZone.Contains(new Vector3(0f, -15f, -24f), 0f, 0f),
                "never closer than 25 m astern of the deck (tail fin, open ramp)");
            TestAssert.That(SkywellZone.Contains(new Vector3(0f, 4f, -40f), 0f, 0f) && !SkywellZone.Contains(new Vector3(0f, 5f, -40f), 0f, 0f), "never more than 4 m above the deck");
            TestAssert.That(SkywellZone.Contains(new Vector3(0f, -15f, -145f), 0f, 0f) && !SkywellZone.Contains(new Vector3(0f, -15f, -159f), 0f, 0f),
                "inside the radius but beyond the tether's reach is outside");

            // the hold timer: per kit, a gap shorter than the grace does not restart it
            var hold = new ZoneHold();
            TestAssert.That(!hold.Update(true, 10f) && !hold.Update(true, 10.5f) && hold.Update(true, 11.05f), "inside for a second: held");
            var gap = new ZoneHold();
            gap.Update(true, 0f);
            gap.Update(false, 0.25f);
            TestAssert.That(!gap.Update(true, 0.5f) && gap.Update(true, 1.05f), "a gap shorter than 0.3 s (replica noise, one gust) does not restart the hold");
            var lapse = new ZoneHold();
            lapse.Update(true, 0f);
            lapse.Update(false, 0.5f);
            TestAssert.That(!lapse.Update(true, 1.2f) && !lapse.Update(true, 2.0f) && lapse.Update(true, 2.3f), "a longer gap restarts it");
            var exit = new ZoneExit();
            TestAssert.That(!exit.Update(true, 5f) && !exit.Update(false, 5f) && !exit.Update(false, 6.9f) && exit.Update(false, 7.1f) && !exit.Update(true, 7.2f) &&
                !exit.Update(false, 7.3f), "outside for more than 2 s breaks the service; coming back resets it");

            // break lanes: four distinct slots, every one clear of the zone centre and of the others
            bool lanesClear = true;
            for (int i = 0; i < 4; i++)
            {
                if (Vector3.Distance(SkywellZone.BreakSlot(i), tow) < 150f) lanesClear = false;
                for (int j = i + 1; j < 4; j++)
                    if (Vector3.Distance(SkywellZone.BreakSlot(i), SkywellZone.BreakSlot(j)) < 25f) lanesClear = false;
            }
            TestAssert.That(lanesClear, "break lanes: 150 m aside and 15 m up or down, never in the slot or in each other");

            // roll relative to the tanker, from world axes (tanker flying level, heading +z)
            Vector3 r = Vector3.right, u = Vector3.up, f = Vector3.forward;
            Vector3 up30 = (u * Mathf.Cos(30f * Mathf.Deg2Rad) + r * Mathf.Sin(30f * Mathf.Deg2Rad)).normalized;
            Vector3 upMinus30 = (u * Mathf.Cos(30f * Mathf.Deg2Rad) - r * Mathf.Sin(30f * Mathf.Deg2Rad)).normalized;
            TestAssert.That(Near(SkywellZone.Roll(r, u, f, f, u), 0f) && Near(Mathf.Abs(SkywellZone.Roll(r, u, f, f, up30)), 30f, 0.05f) &&
                Near(SkywellZone.Roll(r, u, f, f, up30), -SkywellZone.Roll(r, u, f, f, upMinus30), 0.05f), "level reads 0, 30 deg of bank reads 30, left and right read opposite");
            Vector3 tr = (r * Mathf.Cos(30f * Mathf.Deg2Rad) - u * Mathf.Sin(30f * Mathf.Deg2Rad)).normalized;
            Vector3 tu = Vector3.Cross(f, tr).normalized;
            TestAssert.That(Near(SkywellZone.Roll(tr, tu, f, f, tu), 0f, 0.05f) && Near(SkywellZone.Roll(r, u, f, Vector3.up, -f), 0f),
                "the same bank as the tanker is 0; nose straight up is 0");

            // the director: worst axis first, like a tanker's belly lights
            TestAssert.That(SkywellZone.Director(tow, 0f, 0f, 0f) == "HOLD STEADY", "in the zone: HOLD STEADY");
            TestAssert.That(SkywellZone.Director(new Vector3(0f, -15f, -10f), 0f, 0f, 0f).StartsWith("DROP BACK"), "under the tail: DROP BACK first");
            TestAssert.That(SkywellZone.Director(tow + new Vector3(200f, 0f, 0f), 0f, 0f, 0f) == "LEFT 80 m" && SkywellZone.Director(tow + new Vector3(0f, 0f, -200f), 0f, 0f, 0f) == "CLOSE 80 m",
                "80 m outside to the right: LEFT 80 m; astern: CLOSE 80 m");
            TestAssert.That(SkywellZone.Director(tow, 20f, 5f, 0f) == "SLOW" && SkywellZone.Director(tow, 20f, -5f, 0f) == "FAST" && SkywellZone.Director(tow, 0f, 0f, 30f) == "LEVEL WINGS",
                "speed and roll cues");
            TestAssert.That(SkywellZone.MaxTankerSpeed < SkywellZone.AutoReelSpeed && SkywellZone.StowAltitude < SkywellZone.DeployAltitude,
                "the HUD warns before the winch reels in; a deployed kit stows below its deploy altitude");
        }

        /// <summary>The tender: deploy timeline, flight states, follow, working heights, tether, hand-off clock, magazine.</summary>
        private static void RunTender()
        {
            // timeline: nothing unfolds while the deck still holds the tender (wings stand 1 m tall inside the bay)
            TestAssert.That(SkywellTender.Progress(TenderState.Stowed, 5f) == 0f && Near(SkywellTender.Progress(TenderState.Deploying, 4.5f), 0.5f) &&
                SkywellTender.Progress(TenderState.Stowing, 9f) == 0f && SkywellTender.Progress(TenderState.Service, 0f) == 1f, "deploy takes 9 s; stowing runs it backwards");
            bool heldFolded = true;
            for (float p = 0f; p <= 1f; p += 0.01f)
                if (SkywellTender.Attach(p) > 0f && (SkywellTender.Spread(p) > 0f || SkywellTender.NacelleOut(p) > 0f || SkywellTender.DeckOut(p) < 1f && SkywellTender.Attach(p) < 1f))
                    heldFolded = false;
            TestAssert.That(heldFolded, "nothing unfolds while the deck still holds the tender");
            TestAssert.That(Near(SkywellTender.DeckOut(0.25f), 1f, 1e-3f) && Near(SkywellTender.Attach(0.4f), 0f, 1e-3f) && Near(SkywellTender.Spread(0.7f), 1f, 1e-3f) &&
                Near(SkywellTender.NacelleOut(0.8f), 1f, 1e-3f) && Near(SkywellTender.Spool(0.95f), 1f, 1e-3f), "deck out, tender let go, then wings, nacelles and fans, in that order");
            TestAssert.That(SkywellTender.Tilt(TenderState.Stowed, 0f, 0f) == SkywellTender.FoldedTilt && SkywellTender.Tilt(TenderState.Service, 1f, 0f) == 0f &&
                SkywellTender.Tilt(TenderState.Tow, 1f, 0f) == 75f && Near(SkywellTender.Tilt(TenderState.Approach, 1f, SkywellTender.MaxRelSpeed), 60f),
                "fans forward when stowed and in tow, vertical on station");
            TestAssert.That(SkywellTender.FanPower(TenderState.Stowed, 0f) == 0f && SkywellTender.FanPower(TenderState.Approach, 1f) == 1f, "fans off on the deck, full power on the approach");
            TestAssert.That(!SkywellTender.Deployed(TenderState.Stowed) && SkywellTender.Deployed(TenderState.Stowing) && SkywellTender.Flying(TenderState.Tow) &&
                SkywellTender.Flying(TenderState.Return) && !SkywellTender.Flying(TenderState.Deploying) && SkywellTender.Serving(TenderState.Station) &&
                !SkywellTender.Serving(TenderState.Return) && SkywellTender.Moving(TenderState.Stowing) && !SkywellTender.Moving(TenderState.Tow), "state groups");
            TestAssert.That(SkywellRig.WingFoldDeg == 90f && SkywellRig.NacelleStowedTilt == SkywellTender.FoldedTilt && SkywellMagazine.Rounds == 6, "rig conventions the model is built to");

            // follow a stationary target: arrives, never overshoots, never faster than 12 m/s closing
            Vector3 start = new Vector3(0f, -11.5f, -30f), target = new Vector3(8f, -22f, -55f);
            Vector3 pos = start, vel = Vector3.zero;
            float maxSpeed = 0f, closest = float.MaxValue, t5 = -1f;
            bool overshoot = false;
            for (int i = 0; i < 60 * 12; i++)
            {
                SkywellTender.Follow(ref pos, ref vel, target, target, 1f / 60f, SkywellTender.SmoothSeconds, SkywellTender.MaxRelSpeed);
                maxSpeed = Mathf.Max(maxSpeed, vel.magnitude);
                float d = Vector3.Distance(pos, target);
                if (d > closest + 0.02f) overshoot = true; // it only ever gets closer
                closest = Mathf.Min(closest, d);
                if (t5 < 0f && d < 5f) t5 = (i + 1) / 60f;
            }
            TestAssert.That(Vector3.Distance(pos, target) < 0.05f && vel.magnitude < 0.05f && !overshoot, "the follow settles on a still target with no overshoot");
            TestAssert.That(maxSpeed <= SkywellTender.MaxRelSpeed * 1.02f, "closing speed stays within 12 m/s (peak " + maxSpeed + ")");
            float allowance = SkywellTender.ArrivalSeconds(Vector3.Distance(start, target));
            TestAssert.That(t5 > 0f && t5 <= allowance, "the tender is within 5 m before the server's arrival allowance (" + t5 + " s of " + allowance + " s)");
            // retarget mid-flight: the velocity stays continuous (no snap)
            pos = start; vel = Vector3.zero;
            for (int i = 0; i < 60; i++) SkywellTender.Follow(ref pos, ref vel, target, target, 1f / 60f, SkywellTender.SmoothSeconds, SkywellTender.MaxRelSpeed);
            Vector3 before = vel, beforePos = pos, other = target + new Vector3(-2f, 0f, 1.5f);
            SkywellTender.Follow(ref pos, ref vel, other, other, 1f / 60f, SkywellTender.SmoothSeconds, SkywellTender.MaxRelSpeed);
            TestAssert.That(Vector3.Distance(vel, before) < 3f && Vector3.Distance(pos, beforePos) < 0.5f, "a retarget eases in: no velocity or position jump");
            // a target moving at 9 m/s (receiver drifting in tanker space): the tender rides along with no steady lag
            pos = Vector3.zero; vel = Vector3.zero;
            Vector3 tgt = new Vector3(0f, -4f, 10f), tgtVel = new Vector3(2f, 0f, -9f);
            for (int i = 0; i < 60 * 8; i++)
            {
                Vector3 next = tgt + tgtVel / 60f;
                SkywellTender.Follow(ref pos, ref vel, tgt, next, 1f / 60f, SkywellTender.SmoothSeconds, SkywellTender.MaxRelSpeed);
                tgt = next;
            }
            TestAssert.That(Vector3.Distance(pos, tgt) < 0.1f, "tracks a moving receiver with no steady lag (gap " + Vector3.Distance(pos, tgt) + ")");
            // frame-rate independent: 30 fps and 120 fps settle together
            Vector3 p30 = start, v30 = Vector3.zero, p120 = start, v120 = Vector3.zero;
            for (int i = 0; i < 30 * 3; i++) SkywellTender.Follow(ref p30, ref v30, target, target, 1f / 30f, SkywellTender.SmoothSeconds, SkywellTender.MaxRelSpeed);
            for (int i = 0; i < 120 * 3; i++) SkywellTender.Follow(ref p120, ref v120, target, target, 1f / 120f, SkywellTender.SmoothSeconds, SkywellTender.MaxRelSpeed);
            TestAssert.That(Vector3.Distance(p30, p120) < 0.6f, "the follow does not depend on the frame rate");
            // from the tow position to a far corner of the zone: in place inside the server's allowance
            Vector3 far = new Vector3(60f, -40f, -120f);
            pos = SkywellZone.Tow; vel = Vector3.zero;
            float tFar = -1f;
            for (int i = 0; i < 60 * 14 && tFar < 0f; i++)
            {
                SkywellTender.Follow(ref pos, ref vel, far, far, 1f / 60f, SkywellTender.SmoothSeconds, SkywellTender.MaxRelSpeed);
                if (Vector3.Distance(pos, far) < SkywellTender.ReachGap) tFar = (i + 1) / 60f;
            }
            float farAllowance = SkywellTender.ArrivalSeconds(Vector3.Distance(SkywellZone.Tow, far));
            TestAssert.That(SkywellZone.Contains(far, 0f, 0f) && tFar > 0f && tFar <= farAllowance, "from the tow to a far corner the tender is in place inside the allowance (" + tFar + " s of " + farAllowance + " s)");
            TestAssert.That(SkywellTender.ArrivalSeconds(0f) == 2.5f && SkywellTender.ArrivalSeconds(1000f) == 12f && Near(SkywellTender.ArrivalSeconds(60f), 7f),
                "arrival allowance is 2 s + distance at the tender's own top speed, 2.5..12 s");

            // where the working joints sit: reach limited, never within 1.8 m of the belly, never more than 5 m under it
            TestAssert.That(Near(SkywellTender.WorkHeight(0.5f, -1.5f, 2.9f, SkywellTender.ArmClearance), -3.3f), "a rail above the keel line: shoulder held 1.8 m under the belly");
            TestAssert.That(Near(SkywellTender.WorkHeight(-1.5f, -1.5f, 2.9f, SkywellTender.ArmClearance), -3.82f) && Near(SkywellTender.WorkHeight(-1f, -1.5f, 4f, SkywellTender.ArmClearance), -3.5f),
                "the shoulder sits 2.5 m under the rail, or 80 % of a shorter arm's reach");
            TestAssert.That(Near(SkywellTender.WorkHeight(-20f, -1.5f, 4f, SkywellTender.ArmClearance), -6.5f), "never lower than 5 m under the belly");
            TestAssert.That(Near(SkywellTender.BoxDistance(new Vector3(0f, -5f, 0f), Vector3.zero, new Vector3(5f, 2f, 8f)), 3f) &&
                Near(SkywellTender.BoxDistance(new Vector3(1f, 0f, 1f), Vector3.zero, new Vector3(5f, 2f, 8f)), 0f) &&
                Near(SkywellTender.BoxDistance(new Vector3(8f, -5f, 0f), Vector3.zero, new Vector3(5f, 2f, 8f)), Mathf.Sqrt(18f)), "box distance: 3 m under, 0 inside, corner case");

            // tether: never past its length; the line droops by its sag at mid-span, and under a receiver until it comes level into the nose
            Vector3 winch = new Vector3(0f, 0.3f, -0.4f);
            TestAssert.That(Near(Vector3.Distance(winch, SkywellTender.ClampTether(winch, new Vector3(0f, -200f, -300f), SkywellZone.MaxTether)), SkywellZone.MaxTether, 0.01f) &&
                SkywellTender.ClampTether(winch, SkywellZone.Tow, SkywellZone.MaxTether) == SkywellZone.Tow, "the tender is pulled back onto the tether's reach, and left alone inside it");
            Vector3 a = Vector3.zero, b = new Vector3(0f, -15f, -40f);
            float sag = SkywellTender.Sag(42.7f, 0f);
            TestAssert.That(SkywellTender.Sag(42.7f, 1f) < sag && Vector3.Distance(SkywellTender.Bezier(a, SkywellTender.SagControl(a, b, sag, Vector3.up), b, 0.5f), (a + b) * 0.5f - Vector3.up * sag) < 1e-3f,
                "a slack line sags more than a taut one; the control point gives the sag at mid-span");
            Vector3 served = SkywellTender.TetherControl(a, b, 1f, 1f, Vector3.up);
            TestAssert.That(SkywellTender.TetherControl(a, b, 1f, 0f, Vector3.up) == SkywellTender.SagControl(a, b, SkywellTender.Sag(Vector3.Distance(a, b), 1f), Vector3.up) &&
                Near(served.y, b.y, 1e-3f) && Near(SkywellTender.Bezier(a, served, b, 0.5f).y, -7.5f - 15f / 4f, 1e-3f) &&
                SkywellTender.TetherControl(a, new Vector3(0f, 1f, -40f), 0f, 1f, Vector3.up) == SkywellTender.SagControl(a, new Vector3(0f, 1f, -40f), SkywellTender.Sag(Vector3.Distance(a, new Vector3(0f, 1f, -40f)), 0f), Vector3.up),
                "with no receiver the tether sags by its tension alone; under one it sags a quarter of the winch's height (it comes level into the nose); a nose above the winch adds nothing");

            // flying home, and the low-pass on a replicated receiver
            Vector3 left = new Vector3(0f, -22f, -75f);
            TestAssert.That(Near(Vector3.Distance(SkywellTender.HomeEstimate(left, 2f), left), 2f * SkywellTender.ReturnSpeed) &&
                Near(Vector3.Distance(SkywellTender.HomeEstimate(left, 100f), SkywellZone.Tow), 0f), "the tender flies home at 14 m/s and stops at the tow position");
            Vector3 smooth = Vector3.zero;
            for (int i = 0; i < 6; i++) smooth = SkywellTender.LowPass(smooth, Vector3.right, 1f / 60f, SkywellTender.TargetSmoothSeconds);
            TestAssert.That(smooth.x > 0.2f && smooth.x < 0.7f && Vector3.Distance(SkywellTender.LowPass(smooth, Vector3.right * 3f, 0f, 0.1f), smooth) < 1e-4f,
                "0.1 s after a 1 m step the low-pass is about 60 % of the way (" + smooth.x + "); a zero frame changes nothing");

            // hand-off clock: lift 0.8, latch 0.4 (commit at its end), lower 0.8, launcher index 0.5
            TestAssert.That(Near(SkywellHandoff.Total, 2.5f) && Near(SkywellHandoff.CommitAt, 1.2f) && Near(PitPlan.HandoffSeconds, SkywellHandoff.Total), "a hand-off takes 2.5 s and commits at 1.2 s");
            TestAssert.That(Near(SkywellHandoff.Reach(0f), 0f) && Near(SkywellHandoff.Reach(0.8f), 1f) && Near(SkywellHandoff.Reach(1.2f), 1f) && Near(SkywellHandoff.Reach(2.0f), 0f),
                "the gripper rises to the pylon in 0.8 s, holds through the latch, is back down by 2.0 s");
            TestAssert.That(SkywellHandoff.Carrying(1.19f) && !SkywellHandoff.Carrying(1.2f) && Near(SkywellHandoff.Seat(1.0f), 1f) && SkywellHandoff.Seat(0.5f) == 0f &&
                Near(SkywellHandoff.IndexStep(2.0f), 0f) && Near(SkywellHandoff.IndexStep(2.5f), 1f), "the round leaves the gripper at the commit; the launcher indexes in the last 0.5 s");
            TestAssert.That(Near(SkywellHandoff.Boom(0.25f, 6f), 0f) && Near(SkywellHandoff.Boom(0.9f, 6f), 1f) && Near(SkywellHandoff.Boom(6.9f, 6f), 1f) && Near(SkywellHandoff.Boom(7.3f, 6f), 0f),
                "the boom plugs by 0.9 s, holds through the flow, unplugs in 0.4 s");
            TestAssert.That(SkywellMagazine.NextSlot(6) == 0 && SkywellMagazine.NextSlot(5) == 1 && SkywellMagazine.NextSlot(0) == 6 && SkywellMagazine.Full(0, 6) &&
                !SkywellMagazine.Full(0, 5) && SkywellMagazine.Full(1, 5), "the magazine is served slot 0 first; spent slots are empty");
        }

        /// <summary>
        /// The tether as it is drawn: 16 points from the winch to the tender's nose, for a tender flying from the tow position to its place
        /// under a fighter anywhere in the zone (deck axes). One sagging line: it only ever runs toward the nose (along the chord and along
        /// the tanker's track: no hairpin, nothing astern of the nose), no joint is a kink, it never jumps, and it stays clear of the receiver.
        /// </summary>
        private static void RunTether()
        {
            const int points = 16, steps = 200;
            Vector3 winch = new Vector3(0f, 0.45f, -0.4f), half = new Vector3(5.5f, 1.75f, 8f);  // a fighter's half size
            Vector3 nose = new Vector3(0f, -half.y - SkywellTender.Under, 2.26f);                // the tender's nose from the centre of the fighter it holds under
            Vector3[] receivers = { SkywellZone.HoldSlot, new Vector3(0f, -100f, -60f), new Vector3(0f, 2f, -30f), new Vector3(60f, -20f, -50f), new Vector3(-40f, -60f, -110f) };
            foreach (Vector3 rx in receivers)
            {
                TestAssert.That(SkywellZone.Contains(rx, 0f, 0f), "tether case " + rx + " is a receiver in the zone");
                foreach (float under in new[] { 0f, 0.5f, 1f })
                    foreach (float tension in new[] { 0.25f, 1f })
                    {
                        bool toward = true;
                        float turn = 0f, jump = 0f;
                        Vector3 lastMid = Vector3.zero, b = winch, control = winch;
                        for (int step = 0; step <= steps; step++)
                        {
                            b = Vector3.Lerp(SkywellZone.Tow + Vector3.forward * nose.z, rx + nose, step / (float)steps);
                            control = SkywellTender.TetherControl(winch, b, tension, under, Vector3.up);
                            Vector3 chord = (b - winch).normalized, before = winch, heading = Vector3.zero;
                            for (int i = 1; i < points; i++)
                            {
                                Vector3 p = SkywellTender.Bezier(winch, control, b, i / (points - 1f));
                                if (Vector3.Dot(p - before, chord) <= 0f || p.z >= before.z) toward = false;
                                if (i > 1) turn = Mathf.Max(turn, Vector3.Angle(heading, p - before));
                                heading = p - before;
                                before = p;
                            }
                            TestAssert.That(Vector3.Distance(before, b) < 1e-3f, "the line ends on the tender's nose");
                            Vector3 mid = SkywellTender.Bezier(winch, control, b, 0.5f);
                            if (step > 0) jump = Mathf.Max(jump, Vector3.Distance(mid, lastMid));
                            lastMid = mid;
                        }
                        string name = "tether to " + rx + " (under " + under + ", tension " + tension + "): ";
                        TestAssert.That(toward, name + "every point is nearer the nose than the one before, along the chord and along the track");
                        TestAssert.That(turn < 20f, name + "no joint turns more than 20 deg (worst " + turn + ")");
                        TestAssert.That(jump < 1f, name + "the line never jumps as the tender flies in (worst " + jump + " m in a " + Vector3.Distance(SkywellZone.Tow, rx) / steps + " m step)");
                        if (under < 1f) continue;
                        TestAssert.That(Clearance(winch, control, b, rx, half) > 2f, name + "on station the line stays clear of the receiver (nearest " + Clearance(winch, control, b, rx, half) + " m)");
                    }
            }
            // why the line sags deeper under a receiver: left to its tension alone, it cuts through the nose of one far below the tanker
            Vector3 deep = receivers[1];
            TestAssert.That(Clearance(winch, SkywellTender.TetherControl(winch, deep + nose, 0.25f, 0f, Vector3.up), deep + nose, deep, half) == 0f,
                "without the receiver's share of sag the line would cross a receiver 100 m below the tanker");
        }

        /// <summary>Nearest the drawn line comes to a receiver's airframe box, m (0 = through it).</summary>
        private static float Clearance(Vector3 a, Vector3 control, Vector3 b, Vector3 centre, Vector3 half)
        {
            float clear = float.MaxValue;
            for (int i = 0; i <= 60; i++) clear = Mathf.Min(clear, SkywellTender.BoxDistance(SkywellTender.Bezier(a, control, b, i / 60f), centre, half));
            return clear;
        }

        private static SkywellHold.FlightState Level(Vector3 pos, Vector3 vel) =>
            new SkywellHold.FlightState { Pos = pos, Vel = vel, BankDeg = 0f, RollRateDeg = 0f, Nz = 1f, Dt = 0.02f };

        /// <summary>Hold the same inputs for two seconds: the throttle's integral takes over from the pilot's own setting.</summary>
        private static SkywellHold.Command Settle(Vector3 pos, Vector3 vel, Vector3 slot, Vector3 leaderVel, float start = SkywellHold.BaseThrottle, float floor = 0f)
        {
            var hold = new SkywellHold();
            SkywellHold.Command cmd = default;
            for (int i = 0; i < 100; i++) cmd = hold.Step(Level(pos, vel), slot, leaderVel, start, floor);
            return cmd;
        }

        /// <summary>The stick loop that keeps an AI receiver (and the sim's seated jet) in the zone.</summary>
        private static void RunHold()
        {
            Vector3 leaderVel = new Vector3(0f, 0f, 110f);
            // on the slot, level and matched: the stick stays put and the throttle stays where the pilot had it
            SkywellHold.Command settled = new SkywellHold().Step(Level(Vector3.zero, leaderVel), Vector3.zero, leaderVel);
            TestAssert.That(Mathf.Abs(settled.Roll) < 0.02f && Mathf.Abs(settled.Pitch) < 0.02f && Near(settled.Throttle, SkywellHold.BaseThrottle, 0.03f),
                "on the slot: no roll, no pitch, cruise throttle");
            SkywellHold.Command trimmed = new SkywellHold().Step(Level(Vector3.zero, leaderVel), Vector3.zero, leaderVel, 0.3f);
            SkywellHold.Command hot = new SkywellHold().Step(Level(Vector3.zero, leaderVel), Vector3.zero, leaderVel, 1f);
            TestAssert.That(Near(trimmed.Throttle, 0.3f, 0.03f) && Near(hot.Throttle, SkywellHold.MaxStartTrim, 0.03f),
                "on the slot the throttle starts at the pilot's own setting, but never above the start cap");
            // slot to the right and ahead: roll right, push; to the left: roll left
            SkywellHold.Command right = Settle(Vector3.zero, leaderVel, new Vector3(40f, 0f, 200f), leaderVel);
            SkywellHold.Command left = Settle(Vector3.zero, leaderVel, new Vector3(-40f, 0f, 200f), leaderVel);
            TestAssert.That(right.Roll > 0.05f && left.Roll < -0.05f && Near(right.Roll, -left.Roll, 1e-4f) && right.Throttle > SkywellHold.BaseThrottle + 0.1f,
                "slot ahead and to the right: roll right and push; to the left: roll left");
            // a slot far above asks for a bounded climb: nose up (negative stick), vertical-speed target capped
            SkywellHold.Command up = new SkywellHold().Step(Level(Vector3.zero, leaderVel), new Vector3(0f, 400f, 0f), leaderVel);
            TestAssert.That(up.Pitch < -0.02f && up.Pitch > -0.3f && Near(up.VsTarget, SkywellHold.MaxClimbCorrection, 0.01f), "a slot far above: a gentle climb (vs " + up.VsTarget + ")");
            SkywellHold.Command down = new SkywellHold().Step(Level(Vector3.zero, leaderVel), new Vector3(0f, -400f, 0f), leaderVel);
            TestAssert.That(down.Pitch > 0.02f && Near(down.VsTarget, -SkywellHold.MaxClimbCorrection, 0.01f), "a slot far below: nose down");
            // the leader's own climb is fed forward
            SkywellHold.Command climbing = new SkywellHold().Step(Level(Vector3.zero, leaderVel), Vector3.zero, new Vector3(0f, 6f, 110f));
            TestAssert.That(climbing.VsTarget > 5.9f, "a leader climbing 6 m/s is followed at 6 m/s");
            // a slot behind: ease off, but never below the leader less the drop-back allowance
            SkywellHold.Command behind = Settle(Vector3.zero, leaderVel, new Vector3(0f, 0f, -150f), leaderVel);
            TestAssert.That(behind.Throttle < SkywellHold.BaseThrottle - 0.1f && Near(behind.SpeedTarget, 110f - SkywellHold.MaxDropBack, 0.01f), "slot behind: throttle back, at most 6 m/s slower");
            // the stall floor: never commanded slower than it, bank held down in slow flight
            SkywellHold.Command floor = Settle(Vector3.zero, new Vector3(0f, 0f, 100f), new Vector3(0f, 0f, -150f), new Vector3(0f, 0f, 100f), SkywellHold.BaseThrottle, 100f);
            TestAssert.That(floor.SpeedTarget >= 100f && floor.Throttle >= SkywellHold.BaseThrottle - 0.05f, "at the stall floor it never throttles back below the trim");
            SkywellHold.Command slowTurn = new SkywellHold().Step(Level(Vector3.zero, new Vector3(0f, 0f, 100f)), new Vector3(900f, 0f, 0f), new Vector3(0f, 0f, 100f), SkywellHold.BaseThrottle, 100f);
            TestAssert.That(Mathf.Abs(slowTurn.BankTarget) <= SkywellHold.SlowBank + 0.01f, "slow flight: no hard turns");
            // far off to the side: the heading offset is bounded, the bank too
            SkywellHold.Command far = new SkywellHold().Step(Level(Vector3.zero, leaderVel), new Vector3(900f, 0f, 0f), leaderVel);
            TestAssert.That(far.BankTarget <= SkywellHold.MaxBank + 0.01f && far.Roll <= 1f && far.Roll > 0f, "a slot far to the side: bank bounded at 30 deg");
            // along-track error steers nothing, only the throttle
            SkywellHold.Command along = Settle(Vector3.zero, leaderVel, new Vector3(0f, 0f, 500f), leaderVel);
            TestAssert.That(Mathf.Abs(along.Roll) < 0.02f && Mathf.Abs(along.Pitch) < 0.02f && along.Throttle > SkywellHold.BaseThrottle + 0.1f, "along-track error is throttle's job, not steering's");
            // the braking profile: 20 m short of the slot at +20 m/s: no more push
            SkywellHold.Command brake = Settle(Vector3.zero, new Vector3(0f, 0f, 130f), new Vector3(0f, 0f, 20f), leaderVel);
            TestAssert.That(brake.Throttle < SkywellHold.BaseThrottle + 0.05f, "20 m short of the slot at +20 m/s: no more push");

            // closing the loop on a point mass with a bank lag, a g-command pitch response and the FS-20's measured engine and airbrake
            ClosedLoop("from 500 m astern, 60 m aside and 30 m above", new Vector3(60f, 30f, -500f), 135f, 0f, new Vector3(0f, 0f, 130f), 100f);
            ClosedLoop("from ahead and below", new Vector3(-50f, -60f, 150f), 140f, 0f, new Vector3(0f, 0f, 130f), 100f);
            ClosedLoop("behind a leader that climbs 3 m/s", new Vector3(40f, 0f, -300f), 130f, 0f, new Vector3(0f, 3f, 130f), 100f);
            ClosedLoop("taken over at full power, 300 m astern and 25 m/s fast", new Vector3(0f, 0f, -300f), 155f, 0f, new Vector3(0f, 0f, 130f), 100f, 1f);
        }

        /// <summary>One receiver flies SkywellHold on a point-mass plant for 120 s and must end on the slot, never slow or steep.</summary>
        private static void ClosedLoop(string name, Vector3 start, float speed0, float heading0, Vector3 leaderVel, float floorSpeed, float lever = 0.15f)
        {
            const float g = 9.81f, dt = 0.02f;
            var hold = new SkywellHold();
            Vector3 pos = start, leader = Vector3.zero;
            float v = speed0, psi = heading0 * Mathf.Deg2Rad, gamma = 0f, phi = 0f, rollRate = 0f, nz = 1f;
            float minSpeed = 999f, maxBank = 0f, maxGamma = 0f, worst = 0f;
            float thrust = lever, ahead = 0f; // engine state; the farthest the receiver ever got ahead of the slot
            for (int i = 0; i < 50 * 120; i++)
            {
                leader += leaderVel * dt;
                var vel = new Vector3(v * Mathf.Cos(gamma) * Mathf.Sin(psi), v * Mathf.Sin(gamma), v * Mathf.Cos(gamma) * Mathf.Cos(psi));
                SkywellHold.Command cmd = hold.Step(new SkywellHold.FlightState
                {
                    Pos = pos, Vel = vel, BankDeg = phi * Mathf.Rad2Deg, RollRateDeg = rollRate, Nz = nz, Dt = dt,
                }, leader, leaderVel, lever, floorSpeed);
                // plant: roll rate follows the stick with a 0.15 s lag; load factor follows the g command with 0.25 s. Speed as measured on the
                // FS-20 at 6 km and 130-150 m/s: the engine follows the lever with a 1.5 s lag and trims level flight at about 0.13, full power
                // gains 2 m/s^2, and only a closed throttle opens the airbrakes (the game does that itself) and sheds 1.8 m/s^2
                rollRate += (120f * cmd.Roll - rollRate) * (dt / 0.15f);
                phi += rollRate * Mathf.Deg2Rad * dt;
                float nzTarget = Mathf.Cos(phi) + 7f * -cmd.Pitch;
                nz += (nzTarget - nz) * (dt / 0.25f);
                gamma += g * (nz * Mathf.Cos(phi) - Mathf.Cos(gamma)) / v * dt;
                psi += g * nz * Mathf.Sin(phi) / (v * Mathf.Max(0.2f, Mathf.Cos(gamma))) * dt;
                thrust += (cmd.Throttle - thrust) * (dt / 1.5f);
                float accel = cmd.Throttle <= 0f ? -1.8f : 2.3f * thrust - 0.3f;
                v = Mathf.Max(40f, v + (accel - g * Mathf.Sin(gamma)) * dt);
                pos += new Vector3(v * Mathf.Cos(gamma) * Mathf.Sin(psi), v * Mathf.Sin(gamma), v * Mathf.Cos(gamma) * Mathf.Cos(psi)) * dt;
                minSpeed = Mathf.Min(minSpeed, v);
                maxBank = Mathf.Max(maxBank, Mathf.Abs(phi * Mathf.Rad2Deg));
                maxGamma = Mathf.Max(maxGamma, Mathf.Abs(gamma * Mathf.Rad2Deg));
                if (i > 50 * 90) worst = Mathf.Max(worst, Vector3.Distance(pos, leader));
                if (start.z < 0f) ahead = Mathf.Max(ahead, pos.z - leader.z);
            }
            TestAssert.That(worst < 20f, "hold " + name + ": within 20 m of the slot over the last 30 s (worst " + worst + " m)");
            TestAssert.That(ahead < 12f, "hold " + name + ": called in from astern it never runs past the slot toward the tanker (" + ahead + " m ahead)");
            TestAssert.That(minSpeed > floorSpeed - 3f && maxBank < 45f && maxGamma < 15f,
                "hold " + name + ": never below the floor (min " + minSpeed + "), bank " + maxBank + ", path angle " + maxGamma);
        }

        public static void Run()
        {
            // durations
            TestAssert.That(Near(PitPlan.FuelSeconds(0f), 2.7f) && Near(PitPlan.FuelSeconds(2000f), 6.5f) &&
                Near(PitPlan.FuelSeconds(2400f), 7.5f) && Near(PitPlan.FuelSeconds(8000f), 13.5f), "fuel job: plug 0.9 + flow (clamped 1.2..12 s) + unplug 0.6");
            TestAssert.That(PitPlan.FuelFirst(0.34f) && !PitPlan.FuelFirst(0.35f), "fuel goes first below 35 %");

            // both, comfortable fuel -> rounds first (emptiest, tie broken by index), fuel last; a move to another station or the receptacle adds 1.3 s
            List<PitJob> both = Plan(PitOrder.Both, 2400f, 0.55f, -1, 8000f, 6, 0f, true, A, B);
            TestAssert.That(both.Count == 4 && both[0].Kind == PitJobKind.Handoff && both[1].Kind == PitJobKind.Handoff &&
                both[2].Kind == PitJobKind.Handoff && both[3].Kind == PitJobKind.Fuel, "both: three hand-offs then fuel");
            TestAssert.That(both[0].Station == 0 && both[1].Station == 0 && both[2].Station == 2 && both[3].Station == -1, "stations 0,0,2 then fuel");
            TestAssert.That(Near(both[0].Seconds, 2.5f) && Near(both[1].Seconds, 2.5f) && Near(both[2].Seconds, 3.8f) && Near(both[3].Seconds, 8.8f) &&
                Near(PitPlan.Total(both), 17.6f) && Near(PitPlan.Total(both, 2), 12.6f), "job seconds 2.5 2.5 3.8 8.8, total 17.6, 12.6 left from job 2");
            TestAssert.That(Near(both[0].Commit, 1.2f) && Near(both[2].Commit, 2.5f) && Near(both[3].Commit, 8.2f), "commits: the latch end of a hand-off, the unplug of the fuel job, after the flight there");

            // priority station first; low fuel -> fuel first
            List<PitJob> priority = Plan(PitOrder.Both, 2400f, 0.55f, 2, 8000f, 6, 0f, true, A, B);
            TestAssert.That(priority[0].Station == 2 && priority[1].Station == 0 && priority[2].Station == 0 && Near(priority[1].Seconds, 3.8f) &&
                Near(PitPlan.Total(priority), 17.6f), "priority 2: stations 2,0,0 then fuel, total 17.6");
            List<PitJob> low = Plan(PitOrder.Both, 2400f, 0.20f, -1, 8000f, 6, 0f, true, A, B);
            TestAssert.That(low.Count == 4 && low[0].Kind == PitJobKind.Fuel && Near(low[0].Seconds, 7.5f) && low[1].Kind == PitJobKind.Handoff && Near(low[1].Seconds, 3.8f) &&
                Near(PitPlan.Total(low), 17.6f), "fuel fraction 0.20: fuel first (the tender flies straight to the receptacle), then the rounds");

            // single-purpose orders and fuel thresholds
            List<PitJob> fuelOnly = Plan(PitOrder.Fuel, 2400f, 0.55f, -1, 8000f, 6, 0f, true, A, B);
            TestAssert.That(fuelOnly.Count == 1 && fuelOnly[0].Kind == PitJobKind.Fuel, "order FUEL plans only the fuel job");
            List<PitJob> munitions = Plan(PitOrder.Munitions, 2400f, 0.55f, -1, 8000f, 6, 0f, true, A, B);
            TestAssert.That(munitions.Count == 3 && Near(PitPlan.Total(munitions), 8.8f), "order MUNITIONS plans three hand-offs, 8.8 s");
            TestAssert.That(Plan(PitOrder.Both, 30f, 0.55f, -1, 8000f, 6, 0f, true, A, B).Count == 3, "a need under 50 kg plans no fuel job");
            TestAssert.That(Plan(PitOrder.Both, 2400f, 0.55f, -1, 0.5f, 6, 0f, true, A, B).Count == 3, "an empty fuel stock plans no fuel job");

            // the magazine limits the rounds; players are limited by funds
            List<PitJob> thin = Plan(PitOrder.Both, 2400f, 0.55f, -1, 8000f, 1, 0f, true, A, B);
            TestAssert.That(thin.Count == 2 && thin[0].Kind == PitJobKind.Handoff && thin[0].Station == 0 && thin[1].Kind == PitJobKind.Fuel &&
                Near(PitPlan.Total(thin), 11.3f), "one round left in the magazine: one hand-off (A) then fuel, 11.3 s");
            TestAssert.That(Plan(PitOrder.Munitions, 0f, 1f, -1, 8000f, 0, 0f, true, A, B).Count == 0, "an empty magazine plans no hand-off");
            List<PitJob> poor = Plan(PitOrder.Munitions, 0f, 0.55f, -1, 8000f, 6, 0.15f, false, A, B);
            TestAssert.That(poor.Count == 1 && poor[0].Station == 0, "0.15 funds at 0.1 per round buys exactly one round");
            float fundsLeft = 0.15f;
            TestAssert.That(PitPlan.Afford(A, 2, ref fundsLeft, false) == 1 && Near(fundsLeft, 0.05f, 1e-4f), "Afford charges funds for the one affordable round");

            // cassettes, skipped and full stations
            var belt = new PitStation { Index = 1, Missing = 300, Full = 400, MassPerRound = 0.024f, Belt = true };
            List<PitJob> gun = Plan(PitOrder.Munitions, 0f, 1f, -1, 8000f, 6, 0f, true, belt, A);
            TestAssert.That(gun.Count == 3 && gun[0].Kind == PitJobKind.Cassette && gun[0].Rounds == 300 && Near(gun[0].Seconds, 2.5f),
                "a belt station swaps as one 300-round cassette in 2.5 s and takes one magazine slot");
            var cargo = new PitStation { Index = 3, Missing = 1, Full = 1, MassPerRound = 100f, Skip = true };
            var full = new PitStation { Index = 4, Missing = 0, Full = 2, MassPerRound = 100f };
            TestAssert.That(Plan(PitOrder.Munitions, 0f, 1f, -1, 8000f, 6, 0f, true, cargo, full).Count == 0, "skipped and full stations are never planned");

            // caps: a stop is at most the magazine, and at most 30 s; a truncated plan says so (the HUD tells the pilot to fly the zone again)
            var many = new PitStation { Index = 0, Missing = 40, Full = 40, MassPerRound = 1f };
            TestAssert.That(Plan(PitOrder.Munitions, 0f, 1f, -1, 8000f, 6, 0f, true, many).Count == SkywellMagazine.Rounds, "a stop never hands over more than the magazine holds");
            PitStation[] six = { One(0), One(1), One(2), One(3), One(4), One(5) };
            var jobs = new List<PitJob>();
            PitPlan.Plan(PitOrder.Both, 8000f, 0.55f, 8000f, six, -1, 6, 0f, true, jobs, out bool capped);
            TestAssert.That(capped && jobs.Count == 6 && jobs[5].Kind == PitJobKind.Handoff && PitPlan.Total(jobs) <= PitPlan.MaxSeconds, "six rails and a full tank do not fit 30 s: the fuel job is dropped and the plan says so");
            PitPlan.Plan(PitOrder.Both, 8000f, 0.10f, 8000f, six, -1, 6, 0f, true, jobs, out capped);
            TestAssert.That(capped && jobs.Count == 5 && jobs[0].Kind == PitJobKind.Fuel && PitPlan.Total(jobs) <= PitPlan.MaxSeconds, "low on fuel: fuel first, then the hand-offs that fit");
            PitPlan.Plan(PitOrder.Both, 2400f, 0.55f, 8000f, new[] { A, B }, -1, 6, 0f, true, jobs, out capped);
            TestAssert.That(!capped && jobs.Count == 4, "an 18 s stop is not capped");

            // hot-break timing
            TestAssert.That(Near(PitPlan.TimeToImpact(Vector3.zero, Vector3.zero, new Vector3(0f, 0f, -3000f), new Vector3(0f, 0f, 600f)), 5f) &&
                Near(PitPlan.TimeToImpact(new Vector3(0f, 0f, 100f), new Vector3(0f, 0f, 200f), new Vector3(0f, 0f, -2900f), new Vector3(0f, 0f, 500f)), 10f),
                "a missile 3 km out closing at 600 m/s impacts in 5 s; closing speed is relative to the host");
            TestAssert.That(float.IsPositiveInfinity(PitPlan.TimeToImpact(Vector3.zero, Vector3.zero, new Vector3(0f, 0f, -3000f), new Vector3(0f, 0f, -600f))) &&
                float.IsPositiveInfinity(PitPlan.TimeToImpact(Vector3.zero, Vector3.zero, new Vector3(0f, 0f, -3000f), new Vector3(0f, 0f, 15f))),
                "a missile flying away, or closing under 20 m/s, is no threat");

            RunZone();
            RunTender();
            RunTether();
            RunHold();

            // published codes and HUD copy
            ushort[] flags = { PitFlags.HotBreak, PitFlags.Nothing, PitFlags.FuelStockOut, PitFlags.MunitionStockOut, PitFlags.Capped, PitFlags.ZoneLost, PitFlags.OverSpeed, PitFlags.Refused, PitFlags.AutoReel };
            int union = 0;
            bool distinct = true;
            foreach (ushort flag in flags)
            {
                if (flag == 0 || (flag & (flag - 1)) != 0 || (union & flag) != 0) distinct = false;
                union |= flag;
            }
            TestAssert.That(distinct, "every pit flag is its own bit");
            TestAssert.That(PitRelease.None == 0 && PitRelease.Served != PitRelease.ZoneLost && PitRelease.HotBreak != PitRelease.Aborted && PitRelease.Aborted <= 4, "release reasons are distinct small codes");
            TestAssert.That(PitCopy.Pips(2, 5) == "[##>--]" && PitCopy.Pips(0, 4) == "[>----]" && PitCopy.Pips(4, 4) == "[#####]" && PitCopy.Pips(0, 0) == "[>----]",
                "pips: [##>--] at job 3 of 5");
            TestAssert.That(PitCopy.Kg(2344f) == "2,340" && PitCopy.OrderLabel(PitOrder.Munitions) == "MUNITIONS ONLY" && PitCopy.OrderLabel(PitOrder.Both) == "BOTH" &&
                PitCopy.Rounds(4) == "RDS 4/6" && PitCopy.Rounds(9) == "RDS 6/6", "HUD kilograms round to 10 with a thousands comma; order words; magazine count");
        }
    }
}
