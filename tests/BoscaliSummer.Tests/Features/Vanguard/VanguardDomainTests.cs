using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins VANGUARD flight plans and fire-control rules.</summary>
    internal static class VanguardDomainTests
    {
        public static void Run()
        {
            TestAssert.That(VanguardKeys.RoleOf(VanguardKeys.Remora) == VanguardRole.Drone &&
                VanguardKeys.RoleOf("CruiseMissile1") == VanguardRole.None, "only Vanguard keys get a role");

            GlidePlan boost = GlideProfile.Plan(5f, 300000f, 2000f);
            TestAssert.That(boost.Phase == GlidePhase.Boost && boost.Altitude == GlideProfile.Ceiling, "launch boosts to the ceiling");
            GlidePlan glide = GlideProfile.Plan(GlideProfile.BoostTime + GlideProfile.SkipPeriod / 4f, 300000f, 25000f);
            TestAssert.That(glide.Phase == GlidePhase.Glide && glide.Altitude > GlideProfile.Ceiling + 2900f, "glide skips above the ceiling");
            TestAssert.That(GlideProfile.Plan(100f, 20000f, 25000f).Phase == GlidePhase.Terminal, "inside 1.2x altitude it dives");
            float lateNear = Mathf.Abs(GlideProfile.Plan(GlideProfile.BoostTime + GlideProfile.WeavePeriod / 4f, 31000f, 25000f).Lateral);
            TestAssert.That(lateNear < 100f, "weave fades out before the dive");

            Vector3 slot0 = FormationSlot.Local(0), slot1 = FormationSlot.Local(1), slot2 = FormationSlot.Local(2);
            TestAssert.That(slot0.x > 0f && slot1.x < 0f && slot0.z < 0f, "first pair splits starboard/port behind lead");
            TestAssert.That(slot2.z < slot0.z && slot2.x > slot0.x, "second pair echelons further out and back");
            Vector3 world = FormationSlot.World(new Vector3(0, 1000, 0), Vector3.forward, Vector3.right, Vector3.up, 0);
            TestAssert.That(world == new Vector3(45f, 1000f, -55f), "world slot follows the launcher frame");

            TestAssert.That(DecoyRoute.Outbound(79000f) && !DecoyRoute.Outbound(81000f), "route length is 80 km");
            TestAssert.That(Mathf.Abs(DecoyRoute.Lateral(0f)) <= DecoyRoute.WeaveAmplitude, "weave stays inside its amplitude");

            var picker = new InterceptPicker();
            var threats = new List<ThreatView> { new ThreatView(1, 1800f, 600f), new ThreatView(2, 1200f, 700f),
                new ThreatView(3, 900f, -50f), new ThreatView(4, 200f, 900f), new ThreatView(5, 2500f, 900f) };
            TestAssert.That(picker.Pick(0f, threats) == 2, "nearest closing threat in envelope wins");
            picker.Fired(0f, 2);
            TestAssert.That(picker.Pick(2f, threats) == -1, "cooldown holds the next shot");
            TestAssert.That(picker.Pick(4f, threats) == 1, "an engaged threat is not double-tapped");
            TestAssert.That(picker.Pick(9f, threats) == 2, "a survivor is re-engaged after the memory lapses");

            TestAssert.That(SeductionRule.Seduces("ARH", 3000f, 0.1f) && !SeductionRule.Seduces("IR", 3000f, 0.1f),
                "only radar seekers are seduced");
            TestAssert.That(!SeductionRule.Seduces("ARH", 9000f, 0.1f) && !SeductionRule.Seduces("ARH", 3000f, 0.9f),
                "range and the roll both gate seduction");

            TestAssert.That(VanguardKeys.RoleOf(VanguardKeys.Glaive2A) == VanguardRole.Carrier &&
                VanguardKeys.RoleOf(VanguardKeys.Glaive2S) == VanguardRole.Carrier &&
                VanguardKeys.RoleOf(VanguardKeys.Orca) == VanguardRole.Torpedo &&
                VanguardKeys.RoleOf(VanguardKeys.AleX) == VanguardRole.Towed, "batch-2 keys get their roles");
            TestAssert.That(VanguardKeys.PayloadOf(VanguardKeys.Glaive2A) == "UGV1_grenade" &&
                VanguardKeys.PayloadOf(VanguardKeys.Glaive2S) == "UGV1_SAMx1" &&
                VanguardKeys.PayloadOf(VanguardKeys.Orca) == null, "only GLAIVE carries a payload");

            TestAssert.That(CarrierProfile.Height(20000f) > CarrierProfile.Height(5000f) &&
                CarrierProfile.Height(1000f) == CarrierProfile.ReleaseHeight, "glide slope, then 40 m inside 1.5 km");
            TestAssert.That(CarrierProfile.ShouldRelease(250f, 60f) && !CarrierProfile.ShouldRelease(250f, 300f) &&
                !CarrierProfile.ShouldRelease(900f, 40f), "release needs both close range and low height");
            var spots = CarrierProfile.DropCandidates();
            TestAssert.That(spots.Count > 20 && spots[0] == Vector2.zero, "drop search starts at the release point");
            float far = 0f;
            for (int i = 0; i < spots.Count; i++) far = Mathf.Max(far, spots[i].magnitude);
            TestAssert.That(far <= CarrierProfile.DropSearchRadius + 0.01f && spots[spots.Count - 1].magnitude > 100f &&
                spots[1].magnitude <= spots[spots.Count - 1].magnitude, "search spirals outward to 150 m");
            TestAssert.That(CarrierProfile.Spaced(new Vector2(0f, 0f), new List<Vector2> { new Vector2(25f, 0f) }) &&
                !CarrierProfile.Spaced(new Vector2(0f, 0f), new List<Vector2> { new Vector2(10f, 0f) }), "UGVs land 20 m apart");

            TestAssert.That(WaterRun.VerticalAccel(0f, 0f) < 0f && WaterRun.VerticalAccel(-12f, 0f) > 0f &&
                Mathf.Abs(WaterRun.VerticalAccel(WaterRun.Depth, 0f)) < 1e-3f, "depth spring holds -6 m");
            TestAssert.That(WaterRun.Accepts(true) && !WaterRun.Accepts(false), "ORCA swims only at ships");
            TestAssert.That(WaterRun.UnderKeel(8f, 10f) && WaterRun.UnderKeel(40f, 100f) && !WaterRun.UnderKeel(30f, 20f),
                "under-keel fuze: within max(12 m, half the hull radius)");
            TestAssert.That(Mathf.Abs(WaterRun.SnakeYaw(1f)) <= 30f && Mathf.Abs(WaterRun.SnakeYaw(1f)) > 25f &&
                Mathf.Abs(WaterRun.SnakeYaw(0f)) < 1e-3f, "snake search swings +/-30 deg");

            Vector3 trail = TowedTrail.Offset(Vector3.forward, Vector3.up);
            TestAssert.That(trail == new Vector3(0f, -8f, -100f), "decoy trails 100 m behind, 8 m below");
            TestAssert.That(TowedTrail.Snaps(7.5f, 500f) && TowedTrail.Snaps(2f, 40f) && !TowedTrail.Snaps(4f, 500f),
                "cable snaps above 7 g or below 50 m");

            TestAssert.That(SeductionRule.SeducesTowed("ARH", 2000f, -0.8f, 0.3f) &&
                !SeductionRule.SeducesTowed("ARH", 2000f, 0.8f, 0.3f) &&
                !SeductionRule.SeducesTowed("IR", 2000f, -0.8f, 0.01f) &&
                !SeductionRule.SeducesTowed("ARH", 4000f, -0.8f, 0.01f), "towed decoy: radar, close, rear-biased");
        }
    }
}
