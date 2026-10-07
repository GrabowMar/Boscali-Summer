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
        }
    }
}
