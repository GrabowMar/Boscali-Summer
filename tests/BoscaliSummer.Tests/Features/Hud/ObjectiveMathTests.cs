using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The OBJECTIVE page's rules: the plot's range, bearings, the air-defence reach at the pilot's height, the plate's line.</summary>
    internal static class ObjectiveMathTests
    {
        public static void Run()
        {
            Scale();
            Geometry();
            Air();
            Plate();
            Wrapping();
        }

        private static void Wrapping()
        {
            var lines = new string[3];
            TestAssert.Eq(ObjectiveMath.Wrap("Survey the strike site for 20 continuous seconds.", 17, lines), 3, "A sentence wraps onto the lines it needs");
            TestAssert.That(lines[0] == "Survey the strike" && lines[1] == "site for 20" && lines[2] == "continuous…", "Cut at spaces, the last line says it runs on: " + lines[0] + "|" + lines[1] + "|" + lines[2]);
            TestAssert.Eq(ObjectiveMath.Wrap("Neutralize the marked target.", 17, lines), 2, "A short one takes two");
            TestAssert.That(lines[0] == "Neutralize the" && lines[1] == "marked target.", "...and fits whole: " + lines[0] + "|" + lines[1]);
            TestAssert.Eq(ObjectiveMath.Wrap("", 17, lines), 0, "Nothing wraps to nothing");
            TestAssert.Eq(ObjectiveMath.Wrap(null, 17, lines), 0, "...and so does null");
            TestAssert.Eq(ObjectiveMath.Wrap("Supercalifragilisticexpialidocious", 10, lines), 3, "A long word is cut where it falls");
        }

        private static void Scale()
        {
            TestAssert.Near(ObjectiveMath.Need(1.5f, 0f), 3.75f, "The zone ring wants 40 percent of the plot");
            TestAssert.Near(ObjectiveMath.Need(0.1f, 9f), 10f, "A far pilot wants the plot to reach him");
            TestAssert.Near(ObjectiveMath.Range(0f, 3.75f), 5f, "From nothing the range is the smallest step that holds the need");
            TestAssert.Near(ObjectiveMath.Range(5f, 6f), 8f, "It grows at once when the pilot leaves the plot");
            TestAssert.Near(ObjectiveMath.Range(8f, 5.5f), 8f, "...and holds while the need is still near the step below");
            TestAssert.Near(ObjectiveMath.Range(8f, 3.6f), 8f, "...and while it only dips a little under it");
            TestAssert.Near(ObjectiveMath.Range(8f, 2f), 3.2f, "...and shrinks once the need is well under the step below");
            TestAssert.Near(ObjectiveMath.Range(1.2f, 0.1f), 1.2f, "The smallest range is the floor");
            TestAssert.Near(ObjectiveMath.Range(200f, 900f), 200f, "...and the largest the ceiling");
            float r = 5f;
            for (int i = 0; i < 30; i++) r = ObjectiveMath.Range(r, 4.4f + (i % 2) * 0.3f);
            TestAssert.Near(r, 5f, "A pilot circling the plot's edge does not make the scale breathe");
        }

        private static void Geometry()
        {
            TestAssert.Near(ObjectiveMath.Bearing(0f, 10f), 0f, "North");
            TestAssert.Near(ObjectiveMath.Bearing(10f, 0f), 90f, "East");
            TestAssert.Near(ObjectiveMath.Bearing(0f, -10f), 180f, "South");
            TestAssert.Near(ObjectiveMath.Bearing(-10f, 0f), 270f, "West");
            TestAssert.Near(ObjectiveMath.Relative(10f, 350f), 20f, "A bearing relative to the nose wraps");
            TestAssert.Near(ObjectiveMath.Relative(350f, 10f), 340f, "...the other way too");
            TestAssert.Near(ObjectiveMath.Distance(3f, 4f, 0f, 0f), 5f, "Distance");
            TestAssert.Near(ObjectiveMath.ScaleBar(28f, false, 80f), 2000f, "A 2 km bar at the prototype's scale");
            TestAssert.Near(ObjectiveMath.ScaleBar(10f, false, 80f), 5000f, "...a longer one when zoomed out");
            TestAssert.Near(ObjectiveMath.ScaleBar(1000f, false, 80f), 100f, "...and the shortest when it never fits");
            TestAssert.Near(ObjectiveMath.ScaleBar(28f, true, 80f), 1852f, "Imperial bars are nautical miles");
        }

        private static AirDefenceRing Ring(float x, float z, float range, float minAlt, AirDefenceKind kind = AirDefenceKind.RadarSam, float maxAlt = 12000f) =>
            new AirDefenceRing(x, z, range, minAlt, maxAlt, kind, true, true, RingSource.Tracked, true, 0f, false, 1, 1, 0);

        private static void Air()
        {
            AirDefenceRing sam = Ring(-3900f, 700f, 6000f, 1500f);
            SamExposure low = ObjectiveMath.Expose(sam, 0f, 0f, 690f);
            TestAssert.Near(low.DistanceM, (float)Math.Sqrt(3900f * 3900f + 700f * 700f), "Distance to the ring's centre");
            TestAssert.Near(low.ReachM, 6000f * 690f / 1500f, "The reach shrinks with height above ground");
            TestAssert.That(!low.Inside, "Low and 4 km out the pilot is masked");
            TestAssert.Near(low.MaskM, 1500f * low.DistanceM / 6000f, "The mask height is minAlt over range times distance");
            SamExposure high = ObjectiveMath.Expose(sam, 0f, 0f, 1800f);
            TestAssert.That(high.Inside && high.ReachM == 6000f, "High and inside the range he is exposed at the full reach");
            TestAssert.Near(ObjectiveMath.Expose(sam, -3900f, 700f, 100f).DistanceM, 0f, "On top of the site");
            TestAssert.Near(ObjectiveMath.Expose(sam, 0f, 0f, 50f).BearingDeg, ObjectiveMath.Bearing(-3900f, 700f), "The bearing points at the site");
            TestAssert.That(ObjectiveMath.Expose(Ring(0f, 0f, 2500f, 0f, AirDefenceKind.Gun), 1000f, 0f, 10f).Inside, "A gun has no cone: the whole range reaches the ground");
            TestAssert.That(ObjectiveMath.Radar(sam) && ObjectiveMath.Radar(Ring(0f, 0f, 1f, 0f, AirDefenceKind.Unknown)) && !ObjectiveMath.Radar(Ring(0f, 0f, 1f, 0f, AirDefenceKind.Gun)) && !ObjectiveMath.Radar(Ring(0f, 0f, 1f, 0f, AirDefenceKind.IrSam)), "Radar rings draw as cones");
            var rings = new[] { Ring(0f, 9000f, 1f, 0f, AirDefenceKind.Gun), Ring(-3900f, 700f, 6000f, 1500f), Ring(0f, 20000f, 8000f, 1500f) };
            TestAssert.Eq(ObjectiveMath.NearestRadar(rings, 3, 0f, 0f), 1, "The nearest radar ring, not the nearest ring");
            TestAssert.Eq(ObjectiveMath.NearestRadar(rings, 1, 0f, 0f), -1, "No radar ring: none");
            TestAssert.That(ObjectiveMath.Kind(AirDefenceKind.RadarSam) == "SAM" && ObjectiveMath.Kind(AirDefenceKind.Gun) == "GUN" && ObjectiveMath.Kind(AirDefenceKind.IrSam) == "IR SAM", "Ring words");
        }

        private static ObjectiveScope Scope(ObjectiveContact contact = ObjectiveContact.Fixed, bool gates = true, bool range = true, bool alt = true, bool los = true, bool track = true, float progress = 0f)
        {
            var phases = new[] { new ObjectivePhase(0, "STRIKE", "", ObjectivePhaseStatus.Done, 1f), new ObjectivePhase(1, "SURVEY", "", ObjectivePhaseStatus.Current, progress) };
            return new ObjectiveScope { Contact = contact, HasGates = gates, GateRange = range, GateAlt = alt, GateLos = los, GateTrack = track, Phases = phases, PhaseCount = 2, Current = 1 };
        }

        private static void Plate()
        {
            TestAssert.Eq(ObjectiveMath.Line(Scope()), ObjectiveLine.Next, "Nothing wrong and nothing counted: the next step");
            TestAssert.Eq(ObjectiveMath.Line(Scope(progress: 0.4f)), ObjectiveLine.Progress, "A hold that counts says so");
            TestAssert.Eq(ObjectiveMath.Line(Scope(contact: ObjectiveContact.Lost)), ObjectiveLine.ContactLost, "A lost contact comes first");
            TestAssert.Eq(ObjectiveMath.Line(Scope(los: false)), ObjectiveLine.NoSightline, "In the zone, high, tracked and still not counting: no sightline");
            TestAssert.Eq(ObjectiveMath.Line(Scope(alt: false, los: false)), ObjectiveLine.TooLow, "Too low outranks the sightline");
            TestAssert.Eq(ObjectiveMath.Line(Scope(track: false, los: false)), ObjectiveLine.NoTrack, "...and so does a stale contact");
            TestAssert.Eq(ObjectiveMath.Line(Scope(range: false, alt: false, los: false)), ObjectiveLine.Next, "Outside the zone the gates are not the pilot's problem yet");
            TestAssert.Eq(ObjectiveMath.Line(Scope(gates: false, alt: false)), ObjectiveLine.Next, "A phase the gates do not decide never complains");
            TestAssert.That(ObjectiveMath.Warns(ObjectiveLine.NoSightline) && ObjectiveMath.Warns(ObjectiveLine.TooLow) && ObjectiveMath.Warns(ObjectiveLine.ContactLost) && !ObjectiveMath.Warns(ObjectiveLine.Next) && !ObjectiveMath.Warns(ObjectiveLine.Progress), "Which lines caution");
            TestAssert.That(ObjectiveMath.LineWord(ObjectiveLine.NoSightline) == "NO SIGHTLINE - CLIMB" && ObjectiveMath.LineWord(ObjectiveLine.Next) == null, "The plate's words");
            TestAssert.Near(ObjectiveMath.Progress(Scope(progress: 0.64f)), 0.64f, "Progress of the current phase");
            TestAssert.Near(ObjectiveMath.Progress(default), 0f, "No phases, no progress");
            TestAssert.That(ObjectiveMath.Contact(ObjectiveContact.LastKnown) == "LAST KNOWN" && ObjectiveMath.Contact(ObjectiveContact.Fixed) == "FIXED", "Contact words");
            TestAssert.That(ObjectiveMath.DeadlineLow(59f) && !ObjectiveMath.DeadlineLow(61f) && !ObjectiveMath.DeadlineLow(0f), "The deadline cautions under a minute");
        }
    }
}
