using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Tests.Features.Vanguard
{
    /// <summary>Pins MALD-J SHADOW: flight numbers, mast/glow timing and the lobe / shadow / burn-through geometry.</summary>
    internal static class MaldjTests
    {
        public static void Run()
        {
            TestAssert.Eq(VanguardKeys.RoleOf(VanguardKeys.MaldJ), VanguardRole.Jammer, "MALD-J is the jammer");
            TestAssert.Near(MaldjShadow.CruiseAltitude(100f), 300f, "cruise floor");
            TestAssert.Near(MaldjShadow.CruiseAltitude(5000f), 5000f, "cruise holds launch altitude");
            TestAssert.Near(MaldjShadow.CruiseAltitude(12000f), 9000f, "cruise ceiling");
            TestAssert.Near(MaldjShadow.Throttle(200f), 0.5f, "cruise speed trims half throttle");
            TestAssert.Near(MaldjShadow.Throttle(210f), 0.25f, "fast eases off");
            TestAssert.Near(MaldjShadow.Throttle(150f), 1f, "slow is full throttle");
            TestAssert.Near(MaldjShadow.Throttle(260f), 0f, "very fast idles");
            TestAssert.Near(MaldjShadow.Lifetime, 240f, "four-minute endurance");

            TestAssert.Near(MaldjShadow.MastAngle(0.5f), 0f, "mast stowed before 1.0 s");
            TestAssert.Near(MaldjShadow.MastAngle(1.3f), 30f, "mast half up at 1.3 s");
            TestAssert.Near(MaldjShadow.MastAngle(2f), 60f, "mast fully raised");
            TestAssert.That(!MaldjShadow.Jamming(1.59f) && MaldjShadow.Jamming(1.6f), "lobe goes live with the mast up");
            TestAssert.Near(MaldjShadow.Glow(1f, 0f), 0.3f, "glow dim while deploying");
            TestAssert.Near(MaldjShadow.Glow(2f, 0f), 1.3f, "glow pulse mid");
            TestAssert.Near(MaldjShadow.Glow(2f, 1f / (4f * MaldjShadow.PulseHz)), 2.2f, "glow pulse peak");

            Vector3 north = Vector3.forward;
            TestAssert.That(MaldjShadow.InLobe(north, new Vector3(0f, -3000f, 20000f)), "radar dead ahead and below is in the lobe");
            TestAssert.That(MaldjShadow.InLobe(north, new Vector3(20000f, 0f, 20000f)), "45 deg off the nose is in the lobe");
            TestAssert.That(!MaldjShadow.InLobe(north, new Vector3(30000f, 0f, 17320f)), "60 deg off the nose is outside");
            TestAssert.That(!MaldjShadow.InLobe(north, new Vector3(0f, 0f, 36000f)), "beyond 35 km is outside");
            TestAssert.That(!MaldjShadow.InLobe(north, new Vector3(0f, 0f, -10000f)), "behind the jammer is outside");

            // Jammer at the origin heading north, enemy radar 20 km ahead.
            Vector3 radar = new Vector3(0f, 0f, 20000f);
            TestAssert.Eq(Shadow(radar, new Vector3(0f, 0f, -5000f)), ShadowState.Shadowed, "aircraft behind on the line is hidden");
            TestAssert.Eq(Shadow(radar, new Vector3(5000f, 0f, -5000f)), ShadowState.Shadowed, "11.3 deg off the line still hidden");
            TestAssert.Eq(Shadow(radar, new Vector3(7000f, 0f, -5000f)), ShadowState.None, "15.6 deg off the line is outside the shadow");
            TestAssert.Eq(Shadow(radar, new Vector3(0f, 0f, 12000f)), ShadowState.BurnThrough, "8 km from the radar burns through");
            TestAssert.Eq(Shadow(radar, new Vector3(0f, 0f, 10500f)), ShadowState.Shadowed, "9.5 km from the radar is still hidden");
            TestAssert.Eq(Shadow(new Vector3(0f, 0f, -20000f), new Vector3(0f, 0f, -40000f)), ShadowState.None, "a radar behind the jammer is not jammed");
            TestAssert.That(ShadowState.Shadowed > ShadowState.BurnThrough && ShadowState.BurnThrough > ShadowState.None, "best state wins by order");
        }

        private static ShadowState Shadow(Vector3 radar, Vector3 aircraft) =>
            MaldjShadow.Classify(Vector3.forward, radar, aircraft - radar);
    }
}
