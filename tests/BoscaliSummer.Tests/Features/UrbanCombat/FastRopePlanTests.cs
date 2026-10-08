using BoscaliSummer.Garrisons;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.UrbanCombat
{
    internal static class FastRopePlanTests
    {
        public static void Run()
        {
            TestAssert.That(FastRopePlan.ModeFor(1f) == null, "skids on the ground are no rope insertion");
            TestAssert.That(FastRopePlan.ModeFor(30f) == RopeMode.FastRope, "30 m fast-ropes");
            TestAssert.That(FastRopePlan.ModeFor(45f) == RopeMode.FastRope, "the fast-rope limit is inclusive");
            TestAssert.That(FastRopePlan.ModeFor(80f) == RopeMode.Rappel, "80 m rappels");
            TestAssert.That(FastRopePlan.ModeFor(121f) == null && FastRopePlan.ModeFor(float.NaN) == null, "too high or garbage refuses");

            var doors = new[] { new Vector3(-1.5f, 30f, 0f), new Vector3(1.5f, 30f, 0f) };
            var feet = new[] { new Vector3(-1.5f, 0f, 0f), new Vector3(1.5f, 0f, 0f) };
            var dest = new Vector3[8];
            for (int i = 0; i < 8; i++) dest[i] = new Vector3(Mathf.Sin(i) * 12f, 0f, Mathf.Cos(i) * 12f);

            FastRopePlan fast = FastRopePlan.Insertion(RopeMode.FastRope, 30f, feet, dest, false);
            FastRopePlan slow = FastRopePlan.Insertion(RopeMode.Rappel, 30f, feet, dest, false);
            TestAssert.That(slow.DescentSeconds > fast.DescentSeconds * 2f, "rappelling is much slower than fast-roping");
            TestAssert.That(fast.DescentSeconds < 20f, "a fast-rope squad of eight is down in under 20 s: " + fast.DescentSeconds);
            TestAssert.That(Mathf.Abs(FastRopePlan.InsertionSeconds(RopeMode.FastRope, 30f, 8) - fast.DescentSeconds) < 1e-4f &&
                Mathf.Abs(FastRopePlan.InsertionSeconds(RopeMode.Rappel, 30f, 8) - slow.DescentSeconds) < 1e-4f,
                "the server's outcome clock matches the squad the peers watch");

            // Pairs: both ropes are worked at once.
            TestAssert.That(fast.Sample(0, 0.5f, doors[0], doors[1]).Phase == RopePhase.Sliding &&
                fast.Sample(1, 0.5f, doors[0], doors[1]).Phase == RopePhase.Sliding, "a pair slides together");
            TestAssert.That(fast.Sample(2, 0.5f, doors[0], doors[1]).Phase == RopePhase.Aboard, "the next pair waits its turn");

            // Every trooper reaches its destination and holds there; nothing teleports on the way.
            for (int i = 0; i < 8; i++)
            {
                Vector3 previous = fast.Sample(i, 0f, doors[0], doors[1]).Position;
                bool held = false;
                float worst = 0f;
                for (float t = 0.05f; t < fast.TotalSeconds; t += 0.05f)
                {
                    RopeSample s = fast.Sample(i, t, doors[0], doors[1]);
                    if (s.Phase != RopePhase.Aboard) worst = Mathf.Max(worst, (s.Position - previous).magnitude);
                    previous = s.Position;
                    held |= s.Phase == RopePhase.Holding && (s.Position - dest[i]).magnitude < 0.01f;
                }
                TestAssert.That(held, $"trooper {i} holds at its destination");
                TestAssert.That(worst < 1f, $"trooper {i} moves continuously (worst {worst:0.00} m)");
            }

            // Entering a building: the squad disappears inside instead of holding outside.
            FastRopePlan entry = FastRopePlan.Insertion(RopeMode.FastRope, 30f, feet, dest, true);
            TestAssert.That(entry.Sample(0, entry.TotalSeconds - 0.01f, doors[0], doors[1]).Phase == RopePhase.Gone, "an entry ends inside");

            // The ropes hang from the live helicopter: a drifted door moves the sliding trooper with it.
            Vector3 drift = new Vector3(5f, 0f, 0f);
            Vector3 still = fast.Sample(0, 1f, doors[0], doors[1]).Position;
            Vector3 moved = fast.Sample(0, 1f, doors[0] + drift, doors[1] + drift).Position;
            TestAssert.That(moved.x > still.x + 1f, "a sliding trooper follows the drifting door");

            // Extraction: walk to the rope, clip in at stepped heights, ride up, end aboard.
            var sources = new Vector3[8];
            for (int i = 0; i < 8; i++) sources[i] = new Vector3(20f + i, 0f, 10f);
            FastRopePlan exfil = FastRopePlan.Extraction(30f, feet, sources);
            TestAssert.That(exfil.RopeOut(0f) == 1f && exfil.RopeOut(exfil.TotalSeconds) == 0f, "the ropes reel in by the end");
            bool rode = false;
            for (float t = 0f; t < exfil.TotalSeconds; t += 0.1f)
                rode |= exfil.Sample(7, t, doors[0], doors[1]).Phase == RopePhase.Riding;
            TestAssert.That(rode, "the last trooper rides the rope");
            for (int i = 0; i < 8; i++)
                TestAssert.That(exfil.Sample(i, exfil.TotalSeconds + 0.1f, doors[0], doors[1]).Phase == RopePhase.Gone, $"trooper {i} ends aboard");
        }
    }
}
