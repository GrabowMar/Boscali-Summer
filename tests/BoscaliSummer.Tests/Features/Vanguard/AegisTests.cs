using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins AEGIS-3 SNAP: trigger verdicts, damage, contact, cover and fin motion.</summary>
    internal static class AegisTests
    {
        private static bool Near(float a, float b, float eps = 1e-3f) => Mathf.Abs(a - b) <= eps;

        public static void Run()
        {
            TestAssert.That(AegisSnap.MaxRange == 2500f && AegisSnap.MaxOffNose == 70f && AegisSnap.Lifetime == 8f &&
                AegisSnap.DefenceLifetime == 6f && AegisSnap.DamageScale == 2f, "snap envelope numbers follow MECHANICS.md");

            TestAssert.That(AegisSnap.Judge(true, true, 1800f, 20f, 3, false) == SnapVerdict.Fire, "locked aircraft at 1.8 km, 20 deg: fire");
            TestAssert.That(AegisSnap.Judge(true, true, 2500f, 70f, 3, false) == SnapVerdict.Fire, "envelope edges are inclusive");
            TestAssert.That(AegisSnap.Judge(true, true, 2600f, 20f, 3, false) == SnapVerdict.OutOfRange, "2.6 km is out of range");
            TestAssert.That(AegisSnap.Judge(true, true, 1800f, 75f, 3, false) == SnapVerdict.OffNose, "75 deg off the nose is refused");
            TestAssert.That(AegisSnap.Judge(true, false, 900f, 0f, 3, false) == SnapVerdict.NotAir, "ground units and missiles are refused");
            TestAssert.That(AegisSnap.Judge(false, false, 0f, 0f, 3, false) == SnapVerdict.NoTarget, "no lock, no shot");
            TestAssert.That(AegisSnap.Judge(true, true, 900f, 0f, 0, false) == SnapVerdict.Empty, "empty pod");
            TestAssert.That(AegisSnap.Judge(true, true, 900f, 0f, 1, true) == SnapVerdict.Reserved &&
                AegisSnap.Judge(false, false, 0f, 0f, 1, true) == SnapVerdict.Reserved, "last dart is held while a threat is inbound");
            TestAssert.That(AegisSnap.Judge(true, true, 900f, 0f, 2, true) == SnapVerdict.Fire &&
                AegisSnap.Judge(true, true, 900f, 0f, 1, false) == SnapVerdict.Fire, "only the last dart is reserved, only under threat");
            TestAssert.That(AegisSnap.Reason(SnapVerdict.OutOfRange, 3240f) == "RANGE 3.2 km > 2.5" &&
                AegisSnap.Reason(SnapVerdict.Fire, 0f) == null, "range reason uses invariant km");

            TestAssert.That(AegisSnap.ServerAccepts(2500f, 70f) && AegisSnap.ServerAccepts(2900f, 79f),
                "server takes a snap at the envelope edge plus latency slack");
            TestAssert.That(!AegisSnap.ServerAccepts(3100f, 20f) && !AegisSnap.ServerAccepts(1800f, 85f),
                "server refuses a snap far outside the range or the off-nose cone (flies as a plain defence dart)");

            var picker = new InterceptPicker();
            var fast = new List<ThreatView> { new ThreatView(7, 1200f, 600f) };
            TestAssert.That(new InterceptPicker().Pick(0f, new List<ThreatView> { new ThreatView(9, 3000f, 600f), new ThreatView(-41234, 900f, 600f) }) == 1,
                "a missile spawned at runtime has a negative instance id and is still picked (the pick is its index)");
            TestAssert.That(InterceptPicker.MaxRange >= 1100f + 100f && picker.Pick(0f, fast) == 0 &&
                new InterceptPicker().Pick(0f, new List<ThreatView> { new ThreatView(8, InterceptPicker.MaxRange + 50f, 600f) }) == -1,
                "defence window carries the cover's 0.15 s rail delay and still ends at MaxRange");
            TestAssert.That(new InterceptPicker().Pick(0f, new List<ThreatView> { new ThreatView(1, 2800f, 1300f) }) == 0 &&
                new InterceptPicker().Pick(0f, new List<ThreatView> { new ThreatView(1, 2800f, 600f) }) == -1,
                "the launch is timed on seconds to impact: a fast closer is engaged far out, a slow one at the same range waits");

            TestAssert.That(AegisSnap.Damage(50f, 600f, AegisSnap.DamageScale) == 200f && AegisSnap.Damage(50f, 100f, 2f) == 100f &&
                AegisSnap.Damage(50f, 3000f, 2f) == 400f && AegisSnap.Damage(50f, 600f, 1f) == 100f, "snap damage = defence dart x2, closure 1..4x");
            TestAssert.That(AegisSnap.ContactRadius(.5f) == 2f && AegisSnap.ContactRadius(7.5f) == 7.5f && AegisSnap.ContactRadius(40f) == 15f,
                "contact sphere = target bounding radius, 2..15 m");

            Vector3 previous = new Vector3(100f, 0f, 0f), current = new Vector3(30f, 0f, 0f), closing = new Vector3(-1500f, 0f, 0f);
            TestAssert.That(!AegisEnvelope.Contact(previous, current, 5f) && AegisSnap.Contact(previous, current, closing, .02f, 5f),
                "look-ahead scores the tick before the vanilla impact ray would");
            TestAssert.That(!AegisSnap.Contact(previous + Vector3.up * 10f, current + Vector3.up * 10f, closing, .02f, 5f),
                "a 10 m pass outside a 5 m sphere is a miss");

            TestAssert.That(AegisSnap.Door(-1f) == 0f && AegisSnap.Door(0f) == 0f && Near(AegisSnap.Door(.075f), .5f) &&
                AegisSnap.Door(AegisSnap.DoorLead) == 1f && AegisSnap.Door(AegisSnap.DoorHold) == 1f &&
                Near(AegisSnap.Door(.75f), .5f) && Near(AegisSnap.Door(.9f), 0f) && AegisSnap.Door(5f) == 0f,
                "cover: open by the 0.15 s rail delay, hold to 0.6 s, shut by 0.9 s");
            TestAssert.That(AegisSnap.DoorLead < AegisSnap.DoorHold, "the cover is fully open before the dart drops");
            TestAssert.That(AegisSnap.DoorSwing(.1f) == -100f && AegisSnap.DoorSwing(-.1f) == 100f, "free edge swings down on either side");

            TestAssert.That(AegisSnap.Fins(0f) == 0f && AegisSnap.Fins(.05f) == 0f && Near(AegisSnap.Fins(.125f), .5f) &&
                AegisSnap.Fins(.2f) == 1f, "fins snap open 0.05-0.2 s after release");
            float aft = AegisSnap.FinSwing(new Vector3(0f, 0f, -.1f), new Vector3(.06f, 0f, .02f), Vector3.forward, out Vector3 axis);
            TestAssert.That(Near(aft, 90f) && Near(axis.x, 0f) && Near(axis.y, -1f) && Near(axis.z, 0f),
                "aft-folded starboard fin swings 90 deg about -Y to stand radial");
            float fore = AegisSnap.FinSwing(new Vector3(0f, .1f, 0f) + Vector3.forward * .1f, new Vector3(0f, .06f, 0f), Vector3.forward, out _);
            TestAssert.That(Near(fore, 45f), "a fin folded 45 deg forward swings 45 deg");
            TestAssert.That(AegisSnap.FinSwing(new Vector3(.1f, 0f, 0f), new Vector3(.06f, 0f, 0f), Vector3.forward, out _) == 0f &&
                AegisSnap.FinSwing(new Vector3(0f, 0f, -.1f), new Vector3(0f, 0f, .3f), Vector3.forward, out _) == 0f,
                "a fin already radial, or a hinge on the body axis, does not move");
        }
    }
}
