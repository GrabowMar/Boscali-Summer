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
            foreach (string key in new[] { VanguardKeys.MaldX, VanguardKeys.MaldJ, VanguardKeys.Remora, VanguardKeys.HawcX, VanguardKeys.AegisDart,
                VanguardKeys.Glaive2A, VanguardKeys.Glaive2S, VanguardKeys.Orca, VanguardKeys.AleX, VanguardKeys.Kestrel, VanguardKeys.Hamilcar })
                TestAssert.That(VanguardPrices.Munition(key) > 0f && VanguardPrices.Munition(key) < 9f,
                    key + " is priced, and shooting it down pays less than a vanilla FGA-57 kill (value 9)");
            TestAssert.That(VanguardPrices.Munition("CruiseMissile1") == 0f && VanguardPrices.Gun("Gun57mm_Pod") == 0f &&
                VanguardPrices.Gun("WI_VG_Lance") > 0f, "only VANGUARD assets are repriced");

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

            TestAssert.That(VanguardKeys.IsSteerable(VanguardRole.Decoy) && VanguardKeys.IsSteerable(VanguardRole.Jammer) && VanguardKeys.IsSteerable(VanguardRole.Drone) && VanguardKeys.IsSteerable(VanguardRole.Glider) && VanguardKeys.IsSteerable(VanguardRole.Sentinel) && VanguardKeys.IsSteerable(VanguardRole.Gunship) && VanguardKeys.IsSteerable(VanguardRole.Scout) && VanguardKeys.IsSteerable(VanguardRole.Lander) && !VanguardKeys.IsSteerable(VanguardRole.Interceptor) && !VanguardKeys.IsSteerable(VanguardRole.Torpedo) && !VanguardKeys.IsSteerable(VanguardRole.Towed), "hand-steerable: every Vanguard flight but AEGIS darts, ORCA and the ALE-X kite");
            TestAssert.That(AegisEnvelope.Contact(new Vector3(1,0,0),new Vector3(-1,0,0),.2f) &&
                !AegisEnvelope.Contact(new Vector3(1,1,0),new Vector3(-1,1,0),.2f),
                "swept contact catches a real high-speed hit and rejects a nearby miss");
            TestAssert.That(AegisEnvelope.Contact(new Vector3(100,12,0),new Vector3(-100,12,0),AegisEnvelope.ProximityRadius) &&
                !AegisEnvelope.Contact(new Vector3(100,60,0),new Vector3(-100,60,0),AegisEnvelope.ProximityRadius) &&
                AegisEnvelope.ArmSeconds > AegisEnvelope.DropClearSeconds,
                "a near pass scores, a wide one does not, and nothing scores during launch clearance");

            TestAssert.That(SeductionRule.Seduces("ARH", 3000f, 0.1f) && !SeductionRule.Seduces("IR", 3000f, 0.1f),
                "only radar seekers are seduced");
            TestAssert.That(!SeductionRule.Seduces("ARH", 9000f, 0.1f) && !SeductionRule.Seduces("ARH", 3000f, 0.9f),
                "range and the roll both gate seduction");

            TestAssert.That(VanguardKeys.RoleOf(VanguardKeys.Glaive2A) == VanguardRole.Sentinel &&
                VanguardKeys.RoleOf(VanguardKeys.Glaive2S) == VanguardRole.Gunship &&
                VanguardKeys.RoleOf(VanguardKeys.Orca) == VanguardRole.Torpedo &&
                VanguardKeys.RoleOf(VanguardKeys.AleX) == VanguardRole.Towed, "batch-2 keys get their roles");
            TestAssert.That(LanceCapacitor.Tracer(0f) >= .5f && LanceCapacitor.Tracer(1f) <= 1.2f,
                "railgun uses a thin slug streak instead of an oversized beam");

            TestAssert.That(WaterRun.VerticalAccel(0f, 0f) < 0f && WaterRun.VerticalAccel(-12f, 0f) > 0f &&
                Mathf.Abs(WaterRun.VerticalAccel(WaterRun.Depth, 0f)) < 1e-3f, "depth spring holds -6 m");
            TestAssert.That(WaterRun.Accepts(true) && !WaterRun.Accepts(false), "ORCA swims only at ships");
            TestAssert.That(WaterRun.UnderKeel(8f, 10f) && WaterRun.UnderKeel(40f, 100f) && !WaterRun.UnderKeel(30f, 20f),
                "under-keel fuze: within max(12 m, half the hull radius)");
            TestAssert.That(Mathf.Abs(WaterRun.SnakeYaw(1f)) <= 30f && Mathf.Abs(WaterRun.SnakeYaw(1f)) > 25f &&
                Mathf.Abs(WaterRun.SnakeYaw(0f)) < 1e-3f, "snake search swings +/-30 deg");

            Vector3 trail = TowedTrail.Offset(Vector3.forward, Vector3.up, 100f);
            TestAssert.That((trail - new Vector3(0f, -8f, -100f)).magnitude < 1e-3f, "decoy trails 100 m of fiber behind, 8 m below");
            TestAssert.That(TowedTrail.Snaps(7.5f, 500f) && TowedTrail.Snaps(2f, 40f) && !TowedTrail.Snaps(4f, 500f),
                "cable snaps above 7 g or below 50 m");
            TestAssert.That(Mathf.Abs(TowedTrail.GLoad(Vector3.zero, new Vector3(0f, 0f, 9.81f * 7f * 0.25f), 0.25f) - 7f) < 0.01f &&
                TowedTrail.GLoad(new Vector3(200f, 0f, 0f), new Vector3(200f, 0f, 0f), 0.25f) == 0f &&
                TowedTrail.GLoad(Vector3.zero, Vector3.one, 0f) == 0f, "server-side g from velocity change (no divide by zero)");

            TestAssert.That(SeductionRule.SeducesTowed("ARH", 2000f, -0.8f, 0.3f) &&
                !SeductionRule.SeducesTowed("ARH", 2000f, 0.8f, 0.3f) &&
                !SeductionRule.SeducesTowed("IR", 2000f, -0.8f, 0.01f) &&
                !SeductionRule.SeducesTowed("ARH", 4000f, -0.8f, 0.01f), "towed decoy: radar, close, rear-biased");

            float stock = 100f, funds = 0f;
            int[] rounds = RearmAllocation.Allocate(new List<RearmAllocation.StationNeed>
            {
                new RearmAllocation.StationNeed { Missing = 5, MassPerRound = 30f, CostPerRound = 1f },
                new RearmAllocation.StationNeed { Missing = 4, MassPerRound = 5f, CostPerRound = 1f, Skip = true },
            }, ref stock, ref funds, true);
            TestAssert.That(rounds[0] == 3 && rounds[1] == 0 && Mathf.Abs(stock - 10f) < 1e-3f, "rearm is mass-limited; skipped stations get nothing");
            float stock2 = 1000f, funds2 = 2.5f;
            int[] paid = RearmAllocation.Allocate(new List<RearmAllocation.StationNeed>
            {
                new RearmAllocation.StationNeed { Missing = 5, MassPerRound = 10f, CostPerRound = 1f },
            }, ref stock2, ref funds2, false);
            TestAssert.That(paid[0] == 2 && Mathf.Abs(funds2 - 0.5f) < 1e-3f, "players are limited by funds");

            TestAssert.That(ArmIK.Solve(10f, 10f, new Vector3(0f, 0f, 20f), out _, out float straight) && Mathf.Abs(straight) < 1f,
                "fully extended arm is straight");
            TestAssert.That(!ArmIK.Solve(10f, 10f, new Vector3(0f, 0f, 25f), out _, out _), "target beyond reach");
            TestAssert.That(ArmIK.Solve(10f, 10f, new Vector3(0f, 0f, 10f), out _, out float bent) && Mathf.Abs(bent - 120f) < 1f,
                "half reach bends the elbow 120 deg");

            TestAssert.That(LanceCapacitor.Charge(0f) == 0f && LanceCapacitor.Charge(LanceCapacitor.FullChargeSeconds) == 1f &&
                LanceCapacitor.Charge(99f) == 1f, "charge fills over 2.5 s and clamps");
            TestAssert.That(LanceCapacitor.Power01(0f) == 0f && LanceCapacitor.Power01(1f) == 1f &&
                LanceCapacitor.Power01(0.5f) > 0.4f && LanceCapacitor.Power01(0.5f) < 0.6f, "power tapers 0..1");
            TestAssert.That(Mathf.Abs(LanceCapacitor.Tracer(0f) - .6f) < .001f && Mathf.Abs(LanceCapacitor.Tracer(1f) - 1.1f) < .001f,
                "tracer remains a narrow streak");
            TestAssert.That(LanceCapacitor.RechargeSeconds(0f) == 1f && LanceCapacitor.RechargeSeconds(1f) == 3.5f,
                "bigger shots recharge longer");

            TestAssert.That(DecoyRoute.Lateral(500f, 0f) == DecoyRoute.Lateral(500f) &&
                Mathf.Abs(DecoyRoute.Lateral(500f, 0.37f) - DecoyRoute.Lateral(500f)) > 1f &&
                Mathf.Abs(DecoyRoute.Lateral(500f, 0.37f)) <= DecoyRoute.WeaveAmplitude * 1.21f,
                "seeded weaves differ per missile but stay bounded");
            TestAssert.That(GlideProfile.Plan(30f, 300000f, 25000f, 0f).Lateral == GlideProfile.Plan(30f, 300000f, 25000f).Lateral &&
                GlideProfile.Plan(30f, 300000f, 25000f, 0.4f).Lateral != GlideProfile.Plan(30f, 300000f, 25000f).Lateral,
                "seeded glides phase-shift per missile");
            TestAssert.That(WaterRun.SnakeYaw(1f, 0f) == WaterRun.SnakeYaw(1f) &&
                WaterRun.SnakeYaw(1f, 0.75f) != WaterRun.SnakeYaw(1f), "seeded snake searches vary");

            TestAssert.That((TowedTrail.TrailPoint(new Vector3(5f, -1f, -6f), Vector3.forward, Vector3.up, 100f) - new Vector3(5f, -9f, -106f)).magnitude < 1e-3f,
                "towed decoy trails from its pylon anchor");
        }
    }
}
