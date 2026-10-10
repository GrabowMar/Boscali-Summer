using System;
using BoscaliSummer.Garrisons;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.UrbanCombat
{
    internal static class HaloGlidePlanTests
    {
        private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        public static void Run()
        {
            Func<Vector3, float> ground = p => 0f;
            var exit = new Vector3(0f, 3000f, 0f);
            var velocity = new Vector3(0f, 0f, 100f);
            var target = new Vector3(1500f, 0f, 3000f);

            HaloGlidePlan plan = HaloGlidePlan.Build(exit, velocity, target, ground, 8);
            TestAssert.That(!plan.LowDrop, "3 km exit glides");
            TestAssert.That(Flat(plan.StickLanding, target) < 1f, "a target in reach is hit by the first team");
            TestAssert.That(plan.TotalSeconds > 60f && plan.TotalSeconds < 600f, "a HALO stick resolves in minutes, not hours: " + plan.TotalSeconds);

            HaloGlidePlan again = HaloGlidePlan.Build(exit, velocity, target, ground, 8);
            for (int i = 0; i < 8; i++)
                for (float t = 0f; t < plan.TotalSeconds; t += 7.3f)
                    TestAssert.That(plan.Sample(i, t).Position == again.Sample(i, t).Position, "same inputs, same stick (every peer agrees)");

            // Reach: a target 40 km out is clamped onto the bearing at the glide's limit.
            var far = new Vector3(0f, 0f, 40000f);
            HaloGlidePlan clamped = HaloGlidePlan.Build(exit, velocity, far, ground, 4);
            float maxReach = (exit.y - HaloGlidePlan.ExitDrop) / HaloGlidePlan.GlideSink * HaloGlidePlan.GlideMaxSpeed + 400f;
            TestAssert.That(Flat(clamped.StickLanding, exit) <= maxReach, "landing stays inside glide reach");
            TestAssert.That(clamped.StickLanding.z > 3000f && Mathf.Abs(clamped.StickLanding.x) < 1f, "clamped landing keeps the bearing to the target");

            // No target: full reach straight down the heading.
            HaloGlidePlan blind = HaloGlidePlan.Build(exit, velocity, null, ground, 4);
            TestAssert.That(blind.StickLanding.z > 3000f && Mathf.Abs(blind.StickLanding.x) < 1f, "no designation glides down the heading");

            // Low drop: no glide phase, canopies straight off the ramp, short run.
            HaloGlidePlan low = HaloGlidePlan.Build(new Vector3(0f, 300f, 0f), velocity, target, ground, 4);
            TestAssert.That(low.LowDrop, "300 m exit is a low drop");
            bool glided = false;
            for (float t = 0f; t < low.TotalSeconds; t += 0.5f)
                glided |= low.Sample(0, t).Phase == HaloPhase.Glide;
            TestAssert.That(!glided, "a low drop never enters the wingsuit glide");
            TestAssert.That(Flat(low.StickLanding, new Vector3(0f, 0f, 100f)) <= HaloGlidePlan.CanopyRun * 2f + 200f, "a low drop lands within canopy reach");

            // Sixteen: four teams land on separate slots and pull at separate heights.
            HaloGlidePlan full = HaloGlidePlan.Build(exit, velocity, target, ground, 16);
            float[] pullHeight = new float[4];
            Vector3[] landing = new Vector3[4];
            for (int team = 0; team < 4; team++)
            {
                int lead = team * HaloGlidePlan.TeamSize;
                for (float t = 0f; t < full.TotalSeconds; t += 0.25f)
                {
                    HaloSample s = full.Sample(lead, t);
                    if (s.Phase == HaloPhase.Deploy && pullHeight[team] == 0f) pullHeight[team] = s.Position.y;
                    if (s.Phase == HaloPhase.Landed) landing[team] = s.Position;
                }
            }
            for (int a = 0; a < 4; a++)
                for (int b = a + 1; b < 4; b++)
                    TestAssert.That(Flat(landing[a], landing[b]) > 15f, $"teams {a} and {b} land on separate slots");
            TestAssert.That(pullHeight[3] > pullHeight[2] && pullHeight[2] > pullHeight[1] && pullHeight[1] > pullHeight[0],
                "later teams pull higher so the canopies stack");

            // Continuity: no teleports across phase changes (the visuals sample this every frame).
            for (int i = 0; i < 16; i++)
            {
                Vector3 previous = full.Sample(i, 0f).Position;
                float worst = 0f;
                for (float t = 0.05f; t < full.TotalSeconds - HaloGlidePlan.LandingHold; t += 0.05f)
                {
                    HaloSample s = full.Sample(i, t);
                    if (s.Phase == HaloPhase.Aboard) { previous = s.Position; continue; }
                    worst = Mathf.Max(worst, (s.Position - previous).magnitude);
                    previous = s.Position;
                }
                // 0.05 s at a 100 m/s exit plus the form-up catch-up stays under 10 m per step.
                TestAssert.That(worst < 10f, $"jumper {i} moves continuously (worst step {worst:0.0} m)");
            }

            // Landed jumpers stand on the ground they were given (roofs included).
            Func<Vector3, float> roof = p => Flat(p, target) < 40f ? 25f : 0f;
            HaloGlidePlan onRoof = HaloGlidePlan.Build(exit, velocity, target, roof, 4);
            TestAssert.That(Mathf.Abs(onRoof.Sample(0, onRoof.TotalSeconds - 1f).Position.y - (25f + HaloGlidePlan.PelvisHeight)) < 0.01f,
                "the lead stands on the roof at the target");
        }
    }
}
