using System;
using BoscaliSummer.Features.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>
    /// The threat-coverage maths against the game's own gates: the fourth-root signature term,
    /// the radio horizon, the slant-to-ground conversion and the two envelope ceilings. These are
    /// the numbers the coverage field is drawn from, so a silent sign or unit error here is a
    /// confident, plausible, wrong picture — exactly what a test should catch instead.
    /// </summary>
    internal static class ThreatEnvelopeTests
    {
        public static void Run()
        {
            // A radar at RCS 1.0 (the reference signature) sees to maxRange / minSignal.
            float reference = ThreatEnvelope.RadarRadius(
                new RadarEnvelope(200000f, 100f, 1f), new TargetSignature(1f, 8000f), 8020f);
            Close(reference, 200000f, "Reference signature sees to the nominal detection range");

            // The 0.25 exponent is the game's, not an approximation: 16x the signature is
            // exactly twice the range.
            float doubled = ThreatEnvelope.RadarRadius(
                new RadarEnvelope(200000f, 100f, 1f), new TargetSignature(16f, 8000f), 8020f);
            Close(doubled, 400000f, "Sixteen times the cross-section doubles the detection range");

            float quartered = ThreatEnvelope.RadarRadius(
                new RadarEnvelope(200000f, 100f, 1f), new TargetSignature(0.0625f, 8000f), 8020f);
            Close(quartered, 100000f, "A sixteenth of the cross-section halves the detection range");

            // The signal is measured along the slant, so height difference eats ground radius:
            // 20 km of slant from 8 km higher leaves sqrt(20^2 - 8^2) km of ground.
            float offset = ThreatEnvelope.RadarRadius(
                new RadarEnvelope(20000f, 100f, 1f), new TargetSignature(1f, 0f), 8000f);
            float expected = (float)Math.Sqrt(20000f * 20000f - 8000f * 8000f);
            Close(offset, expected, "Height difference converts slant reach into less ground radius");

            // Nothing is detected above the envelope: once the height difference exceeds the
            // slant reach the ring closes rather than lying about it.
            float above = ThreatEnvelope.RadarRadius(
                new RadarEnvelope(20000f, 100f, 1f), new TargetSignature(1f, 30000f), 0f);
            TestAssert.That(above == 0f, "A target above the envelope draws no coverage at all");

            // The radio horizon caps the ring however loud the radar is.
            float horizon = ThreatEnvelope.HorizonRadius(8000f, 8000f);
            float capped = ThreatEnvelope.RadarRadius(
                new RadarEnvelope(4000000f, 100f, 1f), new TargetSignature(1f, 8000f), 8000f);
            Close(capped, horizon, "The radio horizon caps a radar strong enough to outrange it");

            // No radar scans beyond twice its nominal range.
            float scan = ThreatEnvelope.RadarRadius(
                new RadarEnvelope(20000f, 100f, 0.01f), new TargetSignature(1f, 8000f), 8000f);
            Close(scan, 40000f, "The scan limit is twice the nominal range");

            // A threshold at or below zero has no failing distance; the scan bound decides.
            float noThreshold = ThreatEnvelope.RadarRadius(
                new RadarEnvelope(20000f, 100f, 0f), new TargetSignature(1f, 8000f), 8000f);
            Close(noThreshold, 40000f, "A non-positive threshold falls back to the scan limit");

            // A plateau that never clears the threshold means the radar never commits.
            TestAssert.That(ThreatEnvelope.RadarRadius(
                new RadarEnvelope(200000f, 0.5f, 1f), new TargetSignature(1f, 8000f), 8000f) == 0f,
                "A maxSignal below minSignal sees nothing at any range");

            // Degenerate inputs cannot produce a ring.
            TestAssert.That(ThreatEnvelope.RadarRadius(
                new RadarEnvelope(0f, 100f, 1f), new TargetSignature(1f, 8000f), 8000f) == 0f,
                "A radar without a nominal range draws nothing");
            TestAssert.That(ThreatEnvelope.RadarRadius(
                new RadarEnvelope(200000f, 100f, 1f), new TargetSignature(0f, 8000f), 8000f) == 0f,
                "A signature of zero is never detected");

            // Optical/IR: the nearer of the detector's own sweep and
            // visibility * magnification, whichever binds.
            Close(ThreatEnvelope.OpticalRadius(8000f, 4000f, 4f), 8000f,
                "The detector's own sweep caps optical range");
            Close(ThreatEnvelope.OpticalRadius(80000f, 4000f, 4f), 16000f,
                "Visibility times magnification sets optical range inside the sweep");
            Close(ThreatEnvelope.OpticalRadius(10000f, 20000f, 1f), 10000f,
                "The smaller of sweep and magnified visibility wins in either order");
            TestAssert.That(ThreatEnvelope.OpticalRadius(0f, 4000f, 4f) == 0f &&
                            ThreatEnvelope.OpticalRadius(8000f, 0f, 4f) == 0f &&
                            ThreatEnvelope.OpticalRadius(8000f, 4000f, 0f) == 0f,
                "A blind detector, an invisible target or no magnification draws no optical coverage");

            // Heat: full at the emitter, cold exactly at the envelope edge and past it. The cube
            // is the tuning: an envelope here can be wider than the theater, so the field has to
            // keep the near emitter loud and let the far half of the disc fall to nothing.
            Close(ThreatEnvelope.Heat01(1000f, 0f), 1f, "An emitter's own position is the hottest");
            Close(ThreatEnvelope.Heat01(1000f, 500f), 0.125f,
                "Halfway out, heat is one eighth: the cube, not a gentle curve");
            TestAssert.That(ThreatEnvelope.Heat01(1000f, 800f) < 0.01f,
                "Four fifths of the way out the field is near silent, so a theater-wide envelope cannot wash the map");
            TestAssert.That(ThreatEnvelope.Heat01(1000f, 1000f) == 0f &&
                            ThreatEnvelope.Heat01(1000f, 5000f) == 0f,
                "The envelope edge and everything past it is cold");
            TestAssert.That(ThreatEnvelope.Heat01(1000f, -5f) == 1f,
                "A distance behind the emitter cannot overshoot the peak");
            TestAssert.That(ThreatEnvelope.Heat01(0f, 0f) == 0f && ThreatEnvelope.Heat01(-1f, 0f) == 0f,
                "An emitter without an envelope contributes no heat");

            float previous = 1f;
            for (int step = 1; step <= 10; step++)
            {
                float heat = ThreatEnvelope.Heat01(1000f, step * 100f);
                TestAssert.That(heat <= previous, "Heat never rises with distance");
                previous = heat;
            }
        }

        private static void Close(float actual, float expected, string message)
        {
            float tolerance = Math.Max(1f, Math.Abs(expected) * 0.001f);
            TestAssert.That(Math.Abs(actual - expected) <= tolerance,
                message + " (expected " + expected.ToString("0") + ", got " + actual.ToString("0") + ")");
        }
    }
}
