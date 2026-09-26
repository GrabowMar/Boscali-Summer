using System;
using BoscaliSummer.Features.Intel.Domain;
using BoscaliSummer.Framework.Contracts;

namespace BoscaliSummer.Tests.Features.Intel
{
    /// <summary>
    /// The engagement model: vanilla's AnalyzeTarget cone, which weapon stations ring, and a
    /// ring's radius at a given height. Magnitudes are the shipped WeaponInfo assets.
    /// </summary>
    internal static class AirDefenceTests
    {
        // antiAir, maxRange, minAltitude, maxAltitude, minIR, minRadar, gun, jammer
        internal static readonly StationSample SamRadar1 = new StationSample(1.00f, 15000f, 2f, 20000f, 0f, 0f, false, false);
        internal static readonly StationSample SamRadar2 = new StationSample(1.00f, 50000f, 500f, 100000f, 0f, 0f, false, false);
        internal static readonly StationSample SamIr1 = new StationSample(0.79f, 5000f, 0f, 10000f, 0.05f, 0f, false, false);
        internal static readonly StationSample Gun57Spaag = new StationSample(0.68f, 5500f, 0f, 100000f, 0f, 0f, true, false);
        internal static readonly StationSample Gun30Spaag = new StationSample(0.63f, 4000f, 0f, 100000f, 0f, 0f, true, false);
        private static readonly StationSample Gun23 = new StationSample(0.32f, 3000f, 0f, 100000f, 0f, 0f, true, false);
        private static readonly StationSample TankGun = new StationSample(0.35f, 4500f, 0f, 200f, 0f, 0f, true, false);
        private static readonly StationSample CounterMissile = new StationSample(0.01f, 15000f, 0f, 100000f, 0f, 0f, false, false);
        private static readonly StationSample MlrsRocket = new StationSample(0.50f, 40000f, 0f, 1000f, 0f, 0f, false, false);
        private static readonly StationSample Arm1 = new StationSample(0f, 60000f, 0f, 100000f, 0f, 0.10f, false, false);
        private static readonly StationSample JammingPod = new StationSample(0f, 50000f, 0f, 100000f, 0f, 0.10f, false, true);

        public static void Run()
        {
            ConeRadiusMatchesAnalyzeTarget();
            EnvelopeEdgesNeverInventARing();
            StationsThatRingAndStationsThatDoNot();
            RadarGuidanceNeverComesFromMinRadar();
            CoverageAndExposureCountWhatTheyCover();
        }

        internal static AirDefenceRing Ring(float x, float z, StationSample band, AirDefenceKind kind,
            bool radarGuided = false, bool stale = false, int site = 1,
            RingSource source = RingSource.Tracked, bool confirmed = true) =>
            new AirDefenceRing(x, z, band.MaxRange, band.MinAltitude, band.MaxAltitude, kind, radarGuided, false,
                source, confirmed, 5f, stale, 1, site, radarGuided ? site : 0);

        private static bool Near(float a, float b, float tolerance = 0.5f) => a - b <= tolerance && b - a <= tolerance;

        private static void ConeRadiusMatchesAnalyzeTarget()
        {
            AirDefenceRing r9 = Ring(0f, 0f, SamRadar2, AirDefenceKind.RadarSam, true);
            TestAssert.That(Near(r9.EffectiveRadius(50f), 5000f), "R9 at 50 m reaches 5 km: 50 x 50000 / 500");
            TestAssert.That(Near(r9.EffectiveRadius(300f), 30000f), "R9 at 300 m reaches 30 km");
            TestAssert.That(Near(r9.EffectiveRadius(500f), 50000f) && Near(r9.EffectiveRadius(1000f), 50000f),
                "R9 is at full range from 500 m up");
            AirDefenceRing bolt = Ring(0f, 0f, SamRadar1, AirDefenceKind.RadarSam, true);
            foreach (float agl in new[] { 2f, 50f, 1000f, 20000f })
                TestAssert.That(Near(bolt.EffectiveRadius(agl), 15000f), "Boltstrike reaches 15 km at " + agl + " m");
            TestAssert.That(bolt.EffectiveRadius(20001f) == 0f, "above its ceiling a Boltstrike cannot engage");
            TestAssert.That(AirDefenceRing.EffectiveRadius(5000f, 0f, 0f, 50f) == 0f,
                "maxAltitude 0 means no air engagement, so no ring");
        }

        private static void EnvelopeEdgesNeverInventARing()
        {
            AirDefenceRing bolt = Ring(0f, 0f, SamRadar1, AirDefenceKind.RadarSam, true);
            AirDefenceRing ir = Ring(0f, 0f, SamIr1, AirDefenceKind.IrSam);
            TestAssert.That(bolt.EffectiveRadius(-5f) == 0f,
                "below ground reads as ground level: a cone with a floor has no reach there");
            TestAssert.That(Near(ir.EffectiveRadius(-5f), 5000f),
                "a floorless IR SAM still reaches its full range at ground level");
            TestAssert.That(ir.EffectiveRadius(float.NaN) == 0f && bolt.EffectiveRadius(float.NaN) == 0f,
                "an unknown height never yields a radius");
            TestAssert.That(bolt.EffectiveRadius(float.PositiveInfinity) == 0f, "infinite height is above every ceiling");
            TestAssert.That(AirDefenceRing.EffectiveRadius(float.NaN, 0f, 10000f, 100f) == 0f,
                "an unknown range never yields a radius");
        }

        private static void StationsThatRingAndStationsThatDoNot()
        {
            AirDefenceProfile boltstrike = AirDefenceProfile.Build(new[] { SamRadar1 }, 1, true);
            TestAssert.That(boltstrike.HasBand && boltstrike.Kind == AirDefenceKind.RadarSam && Near(boltstrike.MaxRange, 15000f),
                "Boltstrike rings as a radar SAM at 15 km");
            AirDefenceProfile ir = AirDefenceProfile.Build(new[] { SamIr1 }, 1, false);
            TestAssert.That(ir.HasBand && ir.Kind == AirDefenceKind.IrSam, "a seeker that needs an IR signature is an IR SAM");
            AirDefenceProfile spaag = AirDefenceProfile.Build(new[] { Gun57Spaag }, 1, false);
            TestAssert.That(spaag.HasBand && spaag.Kind == AirDefenceKind.Gun && Near(spaag.MaxRange, 5500f),
                "the 57 mm SPAAG rings as a gun at 5.5 km");
            TestAssert.That(AirDefenceProfile.Build(new[] { Gun30Spaag }, 1, false).HasBand,
                "the 30 mm SPAAG (0.63) is the weakest weapon that still rings");
            foreach (StationSample none in new[] { Gun23, TankGun, CounterMissile, MlrsRocket })
                TestAssert.That(!AirDefenceProfile.Build(new[] { none }, 1, false).HasBand,
                    "23 mm AAA, tank guns, CRAM/laser counter-missile systems and MLRS rockets draw no ring");
            AirDefenceProfile ship = AirDefenceProfile.Build(new[] { Gun57Spaag, SamIr1, SamRadar1 }, 3, true);
            TestAssert.That(ship.Kind == AirDefenceKind.RadarSam && Near(ship.MaxRange, 15000f),
                "a unit with several stations rings with its best: radar over IR over gun");
            AirDefenceProfile truck = AirDefenceProfile.Build(Array.Empty<StationSample>(), 0, true);
            TestAssert.That(!truck.HasBand && truck.IsSensor && truck.Kind == AirDefenceKind.Sensor && truck.IsAirDefence,
                "a radar truck is a sensor: SEAD value, no ring");
            TestAssert.That(!AirDefenceProfile.Build(null, 3, false).IsAirDefence, "no stations and no radar is not air defence");
        }

        private static void RadarGuidanceNeverComesFromMinRadar()
        {
            AirDefenceProfile r9Launcher = AirDefenceProfile.Build(new[] { SamRadar2 }, 1, false);
            TestAssert.That(r9Launcher.Kind == AirDefenceKind.RadarSam && !r9Launcher.RadarEmitter,
                "an R9 launcher has a radar-guided weapon but no radar of its own: guidance comes from its site");
            AirDefenceProfile seadJet = AirDefenceProfile.Build(new[] { Arm1 }, 1, false);
            TestAssert.That(seadJet.AntiRadiation && !seadJet.RadarEmitter && !seadJet.HasBand,
                "minRadar marks an anti-radiation store; it is never radar guidance");
            TestAssert.That(!AirDefenceProfile.Build(new[] { JammingPod }, 1, false).AntiRadiation,
                "a jamming pod also needs an emitter but is not an ARM");
        }

        private static void CoverageAndExposureCountWhatTheyCover()
        {
            AirDefenceRing[] rings =
            {
                Ring(0f, 0f, SamIr1, AirDefenceKind.IrSam),
                Ring(3000f, 0f, Gun57Spaag, AirDefenceKind.Gun),
                new AirDefenceRing(0f, 0f, 0f, 0f, 0f, AirDefenceKind.Sensor, false, true, RingSource.Tracked,
                    true, 1f, false, 0, 9, 0)
            };
            RingGeometry.Coverage(rings, rings.Length, 1000f, 0f, 100f, ThreatPictureLimits.AllRingsMask,
                out float depth, out int covering, out bool fresh);
            TestAssert.That(covering == 2 && Near(depth, 4000f) && fresh,
                "two rings cover the point; the deepest is 4 km inside the IR ring");
            RingGeometry.Coverage(rings, rings.Length, 1000f, 0f, 100f, ThreatPictureLimits.RadarConeMask,
                out _, out int radar, out _);
            TestAssert.That(radar == 0, "IR and gun rings are not radar cones");

            AirDefenceRing[] one = { Ring(0f, 0f, SamIr1, AirDefenceKind.IrSam) };
            float exposed = RingGeometry.SegmentExposure(one, 1, -10000f, 0f, 10000f, 0f, 100f,
                ThreatPictureLimits.AllRingsMask, out bool anyFresh);
            TestAssert.That(Near(exposed, 10000f, 1f) && anyFresh,
                "a 20 km leg through a 5 km IR ring is exposed for 10 km at 16 samples");
            TestAssert.That(RingGeometry.SegmentExposure(one, 1, -10000f, 0f, 10000f, 0f, 100f,
                ThreatPictureLimits.RadarConeMask, out _) == 0f, "the kind mask filters rings");
            AirDefenceRing[] stale = { Ring(0f, 0f, SamIr1, AirDefenceKind.IrSam, stale: true) };
            RingGeometry.SegmentExposure(stale, 1, -10000f, 0f, 10000f, 0f, 100f,
                ThreatPictureLimits.AllRingsMask, out bool staleFresh);
            TestAssert.That(!staleFresh, "exposure to a stale ring only is reported as such");
            TestAssert.That(ThreatPictureLimits.AllRingsMask == 15 &&
                (ThreatPictureLimits.AllRingsMask & ThreatPictureLimits.Bit(AirDefenceKind.Sensor)) == 0,
                "sensors never ring");
        }
    }
}
