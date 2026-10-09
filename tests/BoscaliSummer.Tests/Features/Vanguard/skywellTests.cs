using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins the SKYWELL pit-stop planner, hot-break timing, staging curves and HUD pips with numbers.</summary>
    internal static class SkywellTests
    {
        private static bool Near(float a, float b, float eps = 0.01f) => Mathf.Abs(a - b) <= eps;

        private static PitStation A => new PitStation { Index = 0, Missing = 2, Full = 4, MassPerRound = 120f, CostPerRound = 0.1f };

        private static PitStation B => new PitStation { Index = 2, Missing = 1, Full = 2, MassPerRound = 120f, CostPerRound = 0.1f };

        private static List<PitJob> Plan(PitOrder order, float need, float fraction, int priority = -1, float fuelStock = 8000f,
            float munStock = 1500f, float funds = 0f, bool free = true, params PitStation[] stations)
        {
            var jobs = new List<PitJob>();
            int count = PitPlan.Plan(order, need, fraction, fuelStock, stations, priority, munStock, funds, free, jobs);
            TestAssert.That(count == jobs.Count, "Plan returns the list length");
            return jobs;
        }

        /// <summary>The service box rule: where a receiver is served, how it is kept, how roll is read.</summary>
        private static void RunBox()
        {
            Vector3 mid = SkywellBox.Centre;
            TestAssert.That(Near(mid.x, 0f) && Near(mid.y, -15f) && Near(mid.z, -55f), "box centre is 55 m behind and 15 m below the mount");
            TestAssert.That(SkywellBox.Inside(mid, 0f, 0f, false), "the centre is inside");
            TestAssert.That(SkywellBox.Inside(new Vector3(0f, -15f, -20f), 5f, 10f, false) && SkywellBox.Inside(new Vector3(0f, -15f, -90f), 5f, 10f, false),
                "20 m and 90 m behind are the box's near and far faces");
            TestAssert.That(!SkywellBox.Inside(new Vector3(0f, -15f, -19f), 5f, 0f, false) && !SkywellBox.Inside(new Vector3(0f, -15f, -91f), 5f, 0f, false),
                "19 m and 91 m behind are outside");
            TestAssert.That(SkywellBox.Inside(new Vector3(30f, -15f, -55f), 5f, 0f, false) && SkywellBox.Inside(new Vector3(-30f, -15f, -55f), 5f, 0f, false) &&
                !SkywellBox.Inside(new Vector3(31f, -15f, -55f), 5f, 0f, false) && !SkywellBox.Inside(new Vector3(-31f, -15f, -55f), 5f, 0f, false), "lateral limit is +-30 m");
            TestAssert.That(SkywellBox.Inside(new Vector3(0f, 10f, -55f), 5f, 0f, false) && SkywellBox.Inside(new Vector3(0f, -40f, -55f), 5f, 0f, false) &&
                !SkywellBox.Inside(new Vector3(0f, 11f, -55f), 5f, 0f, false) && !SkywellBox.Inside(new Vector3(0f, -41f, -55f), 5f, 0f, false), "vertical limit is +10 / -40 m");
            TestAssert.That(SkywellBox.Inside(mid, 14.9f, 0f, false) && !SkywellBox.Inside(mid, 15.1f, 0f, false), "relative speed must stay under 15 m/s");
            TestAssert.That(SkywellBox.Inside(mid, 0f, 29f, false) && SkywellBox.Inside(mid, 0f, -29f, false) &&
                !SkywellBox.Inside(mid, 0f, 31f, false) && !SkywellBox.Inside(mid, 0f, -31f, false), "roll must stay under 30 deg either way");
            // keep box: a receiver already served may wander 35 % wider before the box is lost
            var drift = new Vector3(38f, -15f, -55f);
            TestAssert.That(!SkywellBox.Inside(drift, 5f, 0f, false) && SkywellBox.Inside(drift, 5f, 0f, true), "the keep box is wider than the entry box (hysteresis)");
            TestAssert.That(SkywellBox.Inside(mid, 19f, 38f, true) && !SkywellBox.Inside(mid, 21f, 0f, true) && !SkywellBox.Inside(mid, 0f, 41f, true), "keep box scales speed and roll too");
            TestAssert.That(!SkywellBox.Inside(new Vector3(0f, -15f, 0f), 0f, 0f, true), "right under the tanker's tail is never the box");
            TestAssert.That(SkywellBox.HoldSeconds >= 0.5f && SkywellBox.LostSeconds >= 1f, "a fly-through does not start a stop; a gust does not lose the box");

            // roll relative to the tanker, from world axes (tanker flying level, heading +z)
            Vector3 r = Vector3.right, u = Vector3.up, f = Vector3.forward;
            TestAssert.That(Near(SkywellBox.Roll(r, u, f, f, u), 0f), "level receiver: roll 0");
            Vector3 up30 = (u * Mathf.Cos(30f * Mathf.Deg2Rad) + r * Mathf.Sin(30f * Mathf.Deg2Rad)).normalized;
            TestAssert.That(Near(Mathf.Abs(SkywellBox.Roll(r, u, f, f, up30)), 30f, 0.05f), "30 deg of bank reads 30");
            TestAssert.That(Near(SkywellBox.Roll(r, u, f, f, up30), -SkywellBox.Roll(r, u, f, f, (u * Mathf.Cos(30f * Mathf.Deg2Rad) - r * Mathf.Sin(30f * Mathf.Deg2Rad)).normalized), 0.05f),
                "left and right bank read opposite");
            // the tanker banked 30 deg with the receiver in step: relative roll 0
            Vector3 tr = (r * Mathf.Cos(30f * Mathf.Deg2Rad) - u * Mathf.Sin(30f * Mathf.Deg2Rad)).normalized;
            Vector3 tu = Vector3.Cross(f, tr).normalized;
            TestAssert.That(Near(SkywellBox.Roll(tr, tu, f, f, tu), 0f, 0.05f), "relative roll: same bank as the tanker is 0");
            TestAssert.That(Near(SkywellBox.Roll(r, u, f, Vector3.up, -f), 0f), "nose straight up: roll is meaningless, 0");
        }

        /// <summary>The glider's follow, pose helpers and timing.</summary>
        private static void RunGlider()
        {
            // follow a stationary target: arrives, never overshoots, never faster than 12 m/s closing
            Vector3 pos = new Vector3(0f, -11.5f, -30f), vel = Vector3.zero, target = new Vector3(8f, -22f, -55f);
            float maxSpeed = 0f, closest = float.MaxValue;
            bool overshoot = false;
            Vector3 start = pos;
            for (int i = 0; i < 60 * 12; i++)
            {
                SkywellGlider.Follow(ref pos, ref vel, target, target, 1f / 60f, SkywellGlider.SmoothSeconds, SkywellGlider.MaxRelSpeed);
                maxSpeed = Mathf.Max(maxSpeed, vel.magnitude);
                float d = Vector3.Distance(pos, target);
                if (d > closest + 0.02f) overshoot = true; // it only ever gets closer
                closest = Mathf.Min(closest, d);
            }
            TestAssert.That(Vector3.Distance(pos, target) < 0.05f && vel.magnitude < 0.05f, "the follow settles on a still target");
            TestAssert.That(maxSpeed <= SkywellGlider.MaxRelSpeed * 1.02f, "closing speed stays within 12 m/s (peak " + maxSpeed + ")");
            TestAssert.That(!overshoot, "critically damped: no overshoot");
            // time to come within 5 m of a target ~28 m away: inside the server's arrival allowance
            pos = start; vel = Vector3.zero;
            float t5 = -1f;
            for (int i = 0; i < 60 * 12 && t5 < 0f; i++)
            {
                SkywellGlider.Follow(ref pos, ref vel, target, target, 1f / 60f, SkywellGlider.SmoothSeconds, SkywellGlider.MaxRelSpeed);
                if (Vector3.Distance(pos, target) < 5f) t5 = (i + 1) / 60f;
            }
            float distance = Vector3.Distance(start, target);
            TestAssert.That(t5 > 0f && t5 <= SkywellGlider.ArrivalSeconds(distance), "the glider is within 5 m before the server's arrival allowance (" + t5 + " s of " + SkywellGlider.ArrivalSeconds(distance) + " s)");
            // retarget mid-flight: the velocity stays continuous (no snap)
            pos = start; vel = Vector3.zero;
            for (int i = 0; i < 60; i++) SkywellGlider.Follow(ref pos, ref vel, target, target, 1f / 60f, SkywellGlider.SmoothSeconds, SkywellGlider.MaxRelSpeed);
            Vector3 before = vel, beforePos = pos;
            Vector3 other = target + new Vector3(-2f, 0f, 1.5f);
            SkywellGlider.Follow(ref pos, ref vel, other, other, 1f / 60f, SkywellGlider.SmoothSeconds, SkywellGlider.MaxRelSpeed);
            TestAssert.That(Vector3.Distance(vel, before) < 3f && Vector3.Distance(pos, beforePos) < 0.5f, "a retarget eases in: no velocity or position jump");
            // a target moving at 9 m/s (receiver drifting in tanker space): the glider rides along with no steady lag
            pos = new Vector3(0f, 0f, 0f); vel = Vector3.zero;
            Vector3 tgt = new Vector3(0f, -4f, 10f), tgtVel = new Vector3(2f, 0f, -9f);
            for (int i = 0; i < 60 * 8; i++)
            {
                Vector3 next = tgt + tgtVel / 60f;
                SkywellGlider.Follow(ref pos, ref vel, tgt, next, 1f / 60f, SkywellGlider.SmoothSeconds, SkywellGlider.MaxRelSpeed);
                tgt = next;
            }
            TestAssert.That(Vector3.Distance(pos, tgt) < 0.1f, "tracks a moving receiver with no steady lag (gap " + Vector3.Distance(pos, tgt) + ")");
            // frame-rate independent: 30 fps and 120 fps settle together
            Vector3 p30 = start, v30 = Vector3.zero, p120 = start, v120 = Vector3.zero;
            for (int i = 0; i < 30 * 3; i++) SkywellGlider.Follow(ref p30, ref v30, target, target, 1f / 30f, SkywellGlider.SmoothSeconds, SkywellGlider.MaxRelSpeed);
            for (int i = 0; i < 120 * 3; i++) SkywellGlider.Follow(ref p120, ref v120, target, target, 1f / 120f, SkywellGlider.SmoothSeconds, SkywellGlider.MaxRelSpeed);
            TestAssert.That(Vector3.Distance(p30, p120) < 0.6f, "the follow does not depend on the frame rate");

            // pose helpers
            TestAssert.That(Near(SkywellGlider.ServeHeight(0.5f, -1.5f), -3.3f), "a rail above the keel line: shoulder held 1.8 m under the belly");
            TestAssert.That(Near(SkywellGlider.ServeHeight(-1f, -1.5f), -3.5f) && Near(SkywellGlider.ServeHeight(-3.5f, -1.5f), -6f), "otherwise the shoulder sits 2.5 m under the rail");
            TestAssert.That(SkywellGlider.ServeHeight(5f, -1.5f) <= -1.5f - 1.8f + 0.001f, "never closer than 1.8 m to the belly");
            TestAssert.That(SkywellGlider.ServeHeight(-20f, -1.5f) >= -1.5f - 5f - 0.001f, "never lower than 5 m under the belly");
            TestAssert.That(Near(SkywellGlider.BoxDistance(new Vector3(0f, -5f, 0f), Vector3.zero, new Vector3(5f, 2f, 8f)), 3f) &&
                Near(SkywellGlider.BoxDistance(new Vector3(1f, 0f, 1f), Vector3.zero, new Vector3(5f, 2f, 8f)), 0f) &&
                Near(SkywellGlider.BoxDistance(new Vector3(8f, -5f, 0f), Vector3.zero, new Vector3(5f, 2f, 8f)), Mathf.Sqrt(18f)), "box distance: 3 m under, 0 inside, corner case");
            TestAssert.That(SkywellGlider.ArrivalSeconds(0f) == 2.5f && SkywellGlider.ArrivalSeconds(1000f) == 7f && Near(SkywellGlider.ArrivalSeconds(40f), 5f), "arrival allowance is 1 s + 10 m/s, 2.5..7 s");
            TestAssert.That(SkywellGlider.Under >= 3f && SkywellGlider.Under <= 4f && SkywellGlider.MaxRelSpeed == 12f, "glider hangs about 3.5 m under the belly and closes at most 12 m/s");
        }

        /// <summary>The autopilot hold that keeps an AI receiver (and the sim's seated jet) in the box.</summary>
        private static void RunHold()
        {
            var hold = new SkywellHold();
            Vector3 leaderVel = new Vector3(0f, 0f, 110f);
            // on the slot, level and matched: no steering, no roll authority
            SkywellHold.Command settled = hold.Step(Vector3.zero, leaderVel, Vector3.zero, leaderVel, 0.02f);
            TestAssert.That(Vector3.Angle(settled.Aim, Vector3.forward) < 0.5f && settled.Bank <= 8.5f && Near(settled.Throttle, SkywellHold.BaseThrottle, 0.02f),
                "on the slot: aim along the track, minimum bank, cruise throttle");
            // slot to the left and ahead: aim bends left, throttle up
            var ahead = new SkywellHold();
            SkywellHold.Command c = ahead.Step(Vector3.zero, leaderVel, new Vector3(-40f, 0f, 200f), leaderVel, 0.02f);
            TestAssert.That(c.Aim.x < -1f && c.Aim.z > 700f && c.Throttle > SkywellHold.BaseThrottle + 0.1f && c.Bank > 10f, "slot ahead and to the left: bend left, push, bank up");
            // slot behind: ease off
            var behind = new SkywellHold();
            SkywellHold.Command e = behind.Step(Vector3.zero, leaderVel, new Vector3(0f, 0f, -150f), leaderVel, 0.02f);
            TestAssert.That(e.Throttle < SkywellHold.BaseThrottle - 0.1f, "slot behind: throttle back");
            // slot far to the side: the bent aim is bounded, look-ahead is at least 800 m, along-track error never steers
            var far = new SkywellHold();
            SkywellHold.Command w = far.Step(Vector3.zero, leaderVel, new Vector3(900f, 0f, 0f), leaderVel, 0.02f);
            float look = Mathf.Max(SkywellHold.MinLookAhead, 110f * SkywellHold.LookAheadSeconds);
            TestAssert.That(Mathf.Abs(w.Aim.x) <= look * SkywellHold.MaxCorrectionSlope + 0.1f && w.Aim.z >= look - 1f, "the correction is bounded and the look-ahead is seconds of flight");
            var along = new SkywellHold();
            SkywellHold.Command a = along.Step(Vector3.zero, leaderVel, new Vector3(0f, 0f, 500f), leaderVel, 0.02f);
            TestAssert.That(Mathf.Abs(a.Aim.x) < 0.01f && Mathf.Abs(a.Aim.y) < 0.01f, "along-track error is throttle's job, not steering's");
            TestAssert.That(a.Throttle <= 1f && a.Throttle >= 0f && c.Bank <= 60f, "throttle and bank stay in range");
            // closing the loop on a point mass that obeys the commanded aim and throttle: it ends up on the slot
            Vector3 p = new Vector3(60f, 25f, -400f), v = new Vector3(0f, 0f, 118f);
            Vector3 tankerAt = Vector3.zero;
            var loop = new SkywellHold();
            for (int i = 0; i < 50 * 90; i++)
            {
                tankerAt += leaderVel * 0.02f;
                SkywellHold.Command cmd = loop.Step(p, v, tankerAt, leaderVel, 0.02f);
                Vector3 want = (cmd.Aim - p).normalized * v.magnitude;
                v = Vector3.Lerp(v, want, 0.06f); // managed turn-rate stand-in (RotateTowards is a native call)
                v = v.normalized * Mathf.Max(60f, v.magnitude + (cmd.Throttle - SkywellHold.BaseThrottle) * 25f * 0.02f);
                p += v * 0.02f;
            }
            TestAssert.That(Vector3.Distance(p, tankerAt) < 25f, "the hold settles on the slot (" + Vector3.Distance(p, tankerAt) + " m off)");
        }

        public static void Run()
        {
            // 1-2: durations
            TestAssert.That(Near(PitPlan.FuelSeconds(0f), 2.7f) && Near(PitPlan.FuelSeconds(2000f), 6.5f) &&
                Near(PitPlan.FuelSeconds(2400f), 7.5f) && Near(PitPlan.FuelSeconds(8000f), 13.5f), "fuel job: plug 0.9 + flow (clamped 1.2..12 s) + unplug 0.6");
            TestAssert.That(Near(PitPlan.HandoffSeconds(true, false), 1.7f) && Near(PitPlan.HandoffSeconds(false, false), 1.5f) &&
                Near(PitPlan.HandoffSeconds(true, true), 2.4f), "handoff 1.7 s first, 1.5 s follow-on (arm down 0.5 + up 0.6 + latch 0.3), cassette 2.4 s");
            TestAssert.That(PitPlan.NextHandoff >= PitStaging.Down + PitStaging.Up + PitStaging.Latch &&
                PitPlan.FirstHandoff >= PitStaging.Slide + PitStaging.Up + PitStaging.Latch, "every handoff is long enough for slide/down, up and latch");
            TestAssert.That(PitPlan.FuelFirst(0.34f) && !PitPlan.FuelFirst(0.35f), "fuel goes first below 35 %");

            // 3: both, comfortable fuel -> rounds first (emptiest tie broken by index), fuel last
            List<PitJob> both = Plan(PitOrder.Both, 2400f, 0.55f, -1, 8000f, 1500f, 0f, true, A, B);
            TestAssert.That(both.Count == 4 && both[0].Kind == PitJobKind.Handoff && both[1].Kind == PitJobKind.Handoff &&
                both[2].Kind == PitJobKind.Handoff && both[3].Kind == PitJobKind.Fuel, "both: three handoffs then fuel");
            TestAssert.That(both[0].Station == 0 && both[1].Station == 0 && both[2].Station == 2 && both[3].Station == -1, "stations 0,0,2 then fuel");
            TestAssert.That(Near(both[0].Seconds, 1.7f) && Near(both[1].Seconds, 1.5f) && Near(both[2].Seconds, 1.7f) && Near(both[3].Seconds, 7.5f) &&
                Near(PitPlan.Total(both), 12.4f), "job seconds 1.7 1.5 1.7 7.5, total 12.4");
            TestAssert.That(Near(PitPlan.Total(both, 2), 9.2f), "remaining time from job 2 is 9.2 s");

            // 4: priority station first
            List<PitJob> priority = Plan(PitOrder.Both, 2400f, 0.55f, 2, 8000f, 1500f, 0f, true, A, B);
            TestAssert.That(priority[0].Station == 2 && priority[1].Station == 0 && priority[2].Station == 0 &&
                Near(priority[0].Seconds, 1.7f) && Near(priority[1].Seconds, 1.7f) && Near(priority[2].Seconds, 1.5f) &&
                Near(PitPlan.Total(priority), 12.4f), "priority 2: stations 2,0,0 then fuel, total 12.4");

            // 5: low fuel -> fuel first
            List<PitJob> low = Plan(PitOrder.Both, 2400f, 0.20f, -1, 8000f, 1500f, 0f, true, A, B);
            TestAssert.That(low.Count == 4 && low[0].Kind == PitJobKind.Fuel && low[1].Kind == PitJobKind.Handoff && Near(PitPlan.Total(low), 12.4f),
                "fuel fraction 0.20: fuel first");

            // 6: single-purpose orders and fuel thresholds
            List<PitJob> fuelOnly = Plan(PitOrder.Fuel, 2400f, 0.55f, -1, 8000f, 1500f, 0f, true, A, B);
            TestAssert.That(fuelOnly.Count == 1 && fuelOnly[0].Kind == PitJobKind.Fuel, "order FUEL plans only the fuel job");
            List<PitJob> munitions = Plan(PitOrder.Munitions, 2400f, 0.55f, -1, 8000f, 1500f, 0f, true, A, B);
            TestAssert.That(munitions.Count == 3 && Near(PitPlan.Total(munitions), 4.9f), "order MUNITIONS plans three handoffs, 4.9 s");
            TestAssert.That(Plan(PitOrder.Both, 30f, 0.55f, -1, 8000f, 1500f, 0f, true, A, B).Count == 3, "a need under 50 kg plans no fuel job");
            TestAssert.That(Plan(PitOrder.Both, 2400f, 0.55f, -1, 0.5f, 1500f, 0f, true, A, B).Count == 3, "an empty fuel stock plans no fuel job");

            // 7: munitions stock limits the rounds
            List<PitJob> thin = Plan(PitOrder.Both, 2400f, 0.55f, -1, 8000f, 130f, 0f, true, A, B);
            TestAssert.That(thin.Count == 2 && thin[0].Kind == PitJobKind.Handoff && thin[0].Station == 0 && thin[1].Kind == PitJobKind.Fuel &&
                Near(PitPlan.Total(thin), 9.2f), "130 kg of rounds loads one round (A) then fuel, 9.2 s");

            // 8: players are limited by funds
            List<PitJob> poor = Plan(PitOrder.Munitions, 0f, 0.55f, -1, 8000f, 1500f, 0.15f, false, A, B);
            TestAssert.That(poor.Count == 1 && poor[0].Station == 0, "0.15 funds at 0.1 per round buys exactly one round");
            float stockLeft = 1500f, fundsLeft = 0.15f;
            TestAssert.That(PitPlan.Take(A, 2, ref stockLeft, ref fundsLeft, false) == 1 && Near(fundsLeft, 0.05f, 1e-4f) && Near(stockLeft, 1380f),
                "Take charges stock and funds for the one affordable round");

            // 9-10: cassettes, skipped and full stations
            var belt = new PitStation { Index = 1, Missing = 300, Full = 400, MassPerRound = 0.024f, Belt = true };
            List<PitJob> gun = Plan(PitOrder.Munitions, 0f, 1f, -1, 8000f, 1500f, 0f, true, belt);
            TestAssert.That(gun.Count == 1 && gun[0].Kind == PitJobKind.Cassette && gun[0].Rounds == 300 && Near(gun[0].Seconds, 2.4f),
                "a belt station swaps as one 300-round cassette in 2.4 s");
            var cargo = new PitStation { Index = 3, Missing = 1, Full = 1, MassPerRound = 100f, Skip = true };
            var full = new PitStation { Index = 4, Missing = 0, Full = 2, MassPerRound = 100f };
            TestAssert.That(Plan(PitOrder.Munitions, 0f, 1f, -1, 8000f, 1500f, 0f, true, cargo, full).Count == 0, "skipped and full stations are never planned");

            // 11: caps
            var many = new PitStation { Index = 0, Missing = 40, Full = 40, MassPerRound = 1f };
            TestAssert.That(Plan(PitOrder.Munitions, 0f, 1f, -1, 8000f, 1500f, 0f, true, many).Count == 14, "a stop never exceeds 14 jobs");
            List<PitJob> capped = Plan(PitOrder.Both, 8000f, 0.10f, -1, 8000f, 1500f, 0f, true, many);
            TestAssert.That(capped.Count == 11 && PitPlan.Total(capped) <= 30f && capped[0].Kind == PitJobKind.Fuel, "capped plan: fuel first, 11 jobs fit 30 s");
            var heavy = new PitStation { Index = 0, Missing = 20, Full = 20, MassPerRound = 1f, Belt = false };
            List<PitJob> timed = Plan(PitOrder.Both, 8000f, 0.10f, -1, 8000f, 1500f, 0f, true, heavy, B);
            TestAssert.That(PitPlan.Total(timed) <= PitPlan.MaxSeconds + 1e-3f, "total pit time stays within 30 s");

            // 11b: a truncated plan says so (the HUD tells the pilot to dock again), a plan that fits does not
            var cappedJobs = new List<PitJob>();
            PitPlan.Plan(PitOrder.Munitions, 0f, 1f, 8000f, new[] { many }, -1, 1500f, 0f, true, cappedJobs, out bool wasCapped);
            TestAssert.That(wasCapped && cappedJobs.Count == 14, "40 missing rounds: 14 jobs kept and the plan reports it was capped");
            PitPlan.Plan(PitOrder.Both, 2400f, 0.55f, 8000f, new[] { A, B }, -1, 1500f, 0f, true, cappedJobs, out bool smallCapped);
            TestAssert.That(!smallCapped && cappedJobs.Count == 4, "a 12 s stop is not capped");
            const byte others = PitFlags.HotBreak | PitFlags.Nothing | PitFlags.FuelStockOut | PitFlags.MunitionStockOut;
            TestAssert.That((PitFlags.Capped & others) == 0 && PitFlags.Capped != 0, "the capped flag is its own bit");

            // 12: live-state clamp at commit
            float stock = 130f, funds = 0f;
            TestAssert.That(PitPlan.Take(A, 2, ref stock, ref funds, true) == 1 && Near(stock, 10f), "Take(2) with 130 kg returns 1 and leaves 10 kg");

            // 13: hot-break timing
            TestAssert.That(Near(PitPlan.TimeToImpact(Vector3.zero, Vector3.zero, new Vector3(0f, 0f, -3000f), new Vector3(0f, 0f, 600f)), 5f),
                "missile 3 km out at 600 m/s impacts in 5.0 s");
            TestAssert.That(Near(PitPlan.TimeToImpact(Vector3.zero, Vector3.zero, new Vector3(0f, 0f, -6000f), new Vector3(0f, 0f, 600f)), 10f),
                "missile 6 km out at 600 m/s impacts in 10.0 s");
            TestAssert.That(float.IsPositiveInfinity(PitPlan.TimeToImpact(Vector3.zero, Vector3.zero, new Vector3(0f, 0f, -3000f), new Vector3(0f, 0f, -600f))),
                "a missile flying away never impacts");
            TestAssert.That(float.IsPositiveInfinity(PitPlan.TimeToImpact(Vector3.zero, Vector3.zero, new Vector3(0f, 0f, -3000f), new Vector3(0f, 0f, 15f))),
                "closing under 20 m/s is no threat");
            TestAssert.That(Near(PitPlan.TimeToImpact(new Vector3(0f, 0f, 100f), new Vector3(0f, 0f, 200f), new Vector3(0f, 0f, -2900f), new Vector3(0f, 0f, 500f)), 10f),
                "closing speed is relative to the host velocity (3000 m at 300 m/s)");

            // 14: staging curves: slide in from Home, then up 0.6 / latch 0.3; a follow-on drops the arm 0.5 first
            TestAssert.That(Near(PitStaging.Reach(0f, PitFrom.Home), 0f) && Near(PitStaging.Reach(PitStaging.Slide, PitFrom.Home), 0f) &&
                Near(PitStaging.Reach(PitStaging.Slide + PitStaging.Up, PitFrom.Home), 1f) && Near(PitStaging.Reach(1.6f, PitFrom.Home), 1f),
                "home: arm waits 0.7 s for the glider, rises 0.6 s, holds the latch");
            TestAssert.That(Near(PitStaging.Reach(0f, PitFrom.Again), 1f) && Near(PitStaging.Reach(PitStaging.Down, PitFrom.Again), 0f) &&
                Near(PitStaging.Reach(1.15f, PitFrom.Again), 1f) && Near(PitStaging.Reach(PitPlan.NextHandoff, PitFrom.Again), 1f),
                "follow-on: arm drops in 0.5 s, rises by 1.15 s, latched at the commit");
            bool monotonic = true;
            float last = -1f;
            for (float t = PitStaging.Down + 0.05f; t < 1.2f; t += 0.02f)
            {
                float reach = PitStaging.Reach(t, PitFrom.Again);
                if (reach < last - 1e-4f) monotonic = false;
                last = reach;
            }
            TestAssert.That(monotonic, "the lift never dips on the way up");
            TestAssert.That(Near(PitStaging.RoundScale(0.30f, true), 0f) && Near(PitStaging.RoundScale(0.55f, true), 1f) && Near(PitStaging.RoundScale(0f, false), 1f),
                "cradle round regrows 0.30..0.55 s after a commit");
            TestAssert.That(Near(PitStaging.Connected(0.25f, 6f), 0f) && Near(PitStaging.Connected(0.9f, 6f), 1f) && Near(PitStaging.Connected(6.9f, 6f), 1f) &&
                Near(PitStaging.Connected(7.3f, 6f), 0f), "boom plugs by 0.9 s, holds through the flow, unplugs in 0.4 s");

            // 15: the retired helper stays pinned, the HUD pips read as pit lane progress
            TestAssert.That(SkywellContact.TransferSeconds(8000f, 0) == 20f && SkywellContact.TransferSeconds(0f, 0) == 3f, "retired TransferSeconds keeps its 3..20 clamp");
            TestAssert.That(PitCopy.Pips(2, 5) == "[##>--]" && PitCopy.Pips(0, 4) == "[>----]" && PitCopy.Pips(4, 4) == "[#####]" && PitCopy.Pips(0, 0) == "[>----]",
                "pips: [##>--] at job 3 of 5");
            RunBox();
            RunGlider();
            RunHold();
            TestAssert.That((PitFlags.BoxLost & (PitFlags.HotBreak | PitFlags.Nothing | PitFlags.FuelStockOut | PitFlags.MunitionStockOut | PitFlags.Capped)) == 0 &&
                PitFlags.BoxLost != 0, "the box-lost flag is its own bit");
            TestAssert.That(PitCopy.Kg(2344f) == "2,340" && PitCopy.OrderLabel(PitOrder.Munitions) == "MUNITIONS ONLY" && PitCopy.OrderLabel(PitOrder.Both) == "BOTH",
                "HUD kilograms round to 10 with a thousands comma; order words");
        }
    }
}
