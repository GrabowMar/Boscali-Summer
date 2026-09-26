using System;
using BoscaliSummer.Features.Intel.Domain;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.Intel
{
    /// <summary>The release-point search the veto, the ace commit and the staff's air components share.</summary>
    internal static class AttackProfileTests
    {
        public static void Run()
        {
            ABoltstrikeCoveredTargetIsRefusedAtEveryAltitude();
            AnR9OnlyTargetIsFlyableLowButNotAt300();
            TheArcFindsTheOpenSide();
            AnAttackerInsideARingStillGetsAProfile();
            StaleOnlyExposureIsFlagged();
            InvalidQueriesAreRefused();
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static void ABoltstrikeCoveredTargetIsRefusedAtEveryAltitude()
        {
            AirDefenceRing[] rings = { AirDefenceTests.Ring(0f, 0f, AirDefenceTests.SamRadar1, AirDefenceKind.RadarSam, true) };
            foreach (float agl in new[] { 2f, 30f, 60f, 300f, 1000f, 10000f, 20000f })
            {
                TestAssert.That(AttackProfileSearch.TryFind(rings, 1, 40000f, 0f, 0f, 0f, 8000f, agl, agl, out AttackProfile p),
                    "a profile is computed");
                TestAssert.That(p.ReleaseCovered && p.RadarConeMetres > 0f,
                    "every release point 8 km from a Boltstrike is inside its cone at " + agl + " m");
            }
        }

        private static void AnR9OnlyTargetIsFlyableLowButNotAt300()
        {
            AirDefenceRing[] rings = { AirDefenceTests.Ring(0f, 0f, AirDefenceTests.SamRadar2, AirDefenceKind.RadarSam, true) };
            AttackProfileSearch.TryFind(rings, 1, 40000f, 0f, 0f, 0f, 8000f, 60f, 60f, out AttackProfile low);
            TestAssert.That(low.RadarConeMetres == 0f && low.PointDefenceMetres == 0f && !low.ReleaseCovered,
                "at 60 m the R9 cone is 6 km: an 8 km standoff release is clean");
            TestAssert.That(Math.Abs(low.ReleaseX - 8000f) < 1f && Math.Abs(low.ReleaseZ) < 1f,
                "the clean direct approach is the first candidate");
            AttackProfileSearch.TryFind(rings, 1, 40000f, 0f, 0f, 0f, 8000f, 300f, 300f, out AttackProfile high);
            TestAssert.That(high.ReleaseCovered && high.RadarConeMetres > 0f,
                "at 300 m the cone is 30 km and every release point is inside it");
        }

        private static void TheArcFindsTheOpenSide()
        {
            // An IR SAM sits on the direct release point: 0° and ±20° release inside it, ±40°
            // clip it on the way in, and -60° is the first clean candidate in search order.
            AirDefenceRing[] rings = { AirDefenceTests.Ring(8000f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam) };
            TestAssert.That(AttackProfileSearch.TryFind(rings, 1, 40000f, 0f, 0f, 0f, 8000f, 60f, 60f, out AttackProfile p),
                "a profile is computed");
            TestAssert.That(p.PointDefenceMetres == 0f && p.RadarConeMetres == 0f && !p.ReleaseCovered,
                "the search finds a clean release point on the arc");
            TestAssert.That(Math.Abs(p.ReleaseX - 4000f) < 1f && Math.Abs(p.ReleaseZ + 6928.2f) < 1f,
                "the first clean candidate is -60°, still 8 km from the target");
        }

        private static void AnAttackerInsideARingStillGetsAProfile()
        {
            AirDefenceRing[] rings = { AirDefenceTests.Ring(0f, 0f, AirDefenceTests.SamIr1, AirDefenceKind.IrSam) };
            TestAssert.That(AttackProfileSearch.TryFind(rings, 1, 0f, 0f, 0f, 0f, 8000f, 60f, 60f, out AttackProfile p),
                "an attacker sitting on its target still gets an answer");
            TestAssert.That(Finite(p.ReleaseX) && Finite(p.ReleaseZ) && Finite(p.PointDefenceMetres) && Finite(p.RadarConeMetres),
                "no NaN leaks out of a zero-length approach bearing");
            TestAssert.That(p.PointDefenceMetres > 0f && !p.ReleaseCovered,
                "leaving the ring costs exposure; the 8 km release point is outside it");
            TestAssert.That(Math.Abs(MathF.Sqrt(p.ReleaseX * p.ReleaseX + p.ReleaseZ * p.ReleaseZ) - 8000f) < 1f,
                "the release point is on the release-range circle");
        }

        private static void StaleOnlyExposureIsFlagged()
        {
            AirDefenceRing[] stale = { AirDefenceTests.Ring(0f, 0f, AirDefenceTests.SamRadar1, AirDefenceKind.RadarSam, true, stale: true) };
            AttackProfileSearch.TryFind(stale, 1, 40000f, 0f, 0f, 0f, 8000f, 60f, 60f, out AttackProfile p);
            TestAssert.That(p.StaleOnly && p.ReleaseCovered, "exposure to a stale ring only is flagged StaleOnly");
            AirDefenceRing[] fresh = { AirDefenceTests.Ring(0f, 0f, AirDefenceTests.SamRadar1, AirDefenceKind.RadarSam, true) };
            AttackProfileSearch.TryFind(fresh, 1, 40000f, 0f, 0f, 0f, 8000f, 60f, 60f, out p);
            TestAssert.That(!p.StaleOnly, "a fresh ring is not");
            AttackProfileSearch.TryFind(Array.Empty<AirDefenceRing>(), 0, 40000f, 0f, 0f, 0f, 8000f, 60f, 60f, out p);
            TestAssert.That(!p.StaleOnly && p.RadarConeMetres == 0f && Math.Abs(p.ReleaseX - 8000f) < 1f,
                "no rings: a clean direct profile");
        }

        private static void InvalidQueriesAreRefused()
        {
            AirDefenceRing[] none = Array.Empty<AirDefenceRing>();
            TestAssert.That(!AttackProfileSearch.TryFind(none, 0, 40000f, 0f, 0f, 0f, 0f, 60f, 60f, out _), "a zero release range is refused");
            TestAssert.That(!AttackProfileSearch.TryFind(none, 0, float.NaN, 0f, 0f, 0f, 8000f, 60f, 60f, out _), "a NaN position is refused");
            TestAssert.That(!AttackProfileSearch.TryFind(none, 0, 40000f, 0f, 0f, 0f, 8000f, float.NaN, 60f, out _), "a NaN altitude is refused");
        }
    }
}
