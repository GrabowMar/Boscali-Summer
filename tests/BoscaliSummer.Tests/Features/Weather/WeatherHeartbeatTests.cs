using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The 8 s key heartbeat (spec 9.4): it belongs to the host, not to a moving key.</summary>
    internal static class WeatherHeartbeatTests
    {
        public static void Run()
        {
            TestAssert.That(WeatherHeartbeat.PeriodSeconds == 8f, "the beat is 8 s");
            FirstBroadcastIsTheNextGatedFrame();
            HeartbeatIgnoresDynamic();
            OnlyTheReadyHostBeats();
            AResendKeepsTheInstance();
        }

        private static List<float> Simulate(float keyCreatedAt, float end, bool useDynamicWrongly = false)
        {
            // 50 frames per second; the key appears at keyCreatedAt with lastBroadcast = -999 (set at capture).
            var beats = new List<float>();
            float last = -999f;
            for (float now = 0f; now < end; now += 0.02f)
            {
                bool hasKey = now >= keyCreatedAt;
                if (WeatherHeartbeat.Due(true, hasKey, hasKey, now, last))
                {
                    beats.Add(now);
                    last = now;
                }
            }
            return beats;
        }

        private static void FirstBroadcastIsTheNextGatedFrame()
        {
            // A key created 10 s after ResetForScene is broadcast once at once, then every 8 s.
            List<float> beats = Simulate(10f, 45f);
            TestAssert.That(beats.Count >= 4, "four beats in 35 s, got " + beats.Count);
            TestAssert.That(beats[0] >= 10f && beats[0] < 10.05f, "the first broadcast is the first frame with a key, at " + beats[0]);
            for (int i = 1; i < beats.Count; i++)
            {
                float gap = beats[i] - beats[i - 1];
                TestAssert.That(gap > 8f && gap < 8.05f, "then every 8 s, gap " + gap);
            }
        }

        private static void HeartbeatIgnoresDynamic()
        {
            // The rule takes no Dynamic input at all: a host with TapeMoves off still sends its key, so a late
            // joiner is never left without one.
            TestAssert.That(WeatherHeartbeat.Due(true, true, true, 100f, 90f), "a held host beats");
            TestAssert.That(!WeatherHeartbeat.Due(true, true, true, 98f, 90f), "not before 8 s");
            TestAssert.That(!WeatherHeartbeat.Due(true, true, true, 98f, 90f) && WeatherHeartbeat.Due(true, true, true, 98.01f, 90f),
                "strictly after 8 s");
        }

        private static void OnlyTheReadyHostBeats()
        {
            TestAssert.That(!WeatherHeartbeat.Due(false, true, true, 100f, 0f), "a client never beats");
            TestAssert.That(!WeatherHeartbeat.Due(true, false, true, 100f, 0f), "no beat before the field is ready");
            TestAssert.That(!WeatherHeartbeat.Due(true, true, false, 100f, 0f), "no beat without a key");
        }

        private static void AResendKeepsTheInstance()
        {
            // Spec 10.1: a value-equal resend returns the same instance. The receiver's path is ApplyNetworkSync:
            // ToKey, then Reuse(current, incoming). 75 heartbeats (600 s at 8 s) of an unchanged key, held and moving.
            foreach (bool dynamic in new[] { false, true })
            {
                var key = new WeatherKey(9u, 10f, dynamic, 3, headingDeg: 45f, offset: 1000f);
                WeatherKey held = key;
                for (int beat = 0; beat < 75; beat++)
                    held = WeatherKeyWire.Reuse(held, WeatherKeyWire.ToKey(WeatherKeyWire.FromKey(key)));
                TestAssert.That(ReferenceEquals(held, key) && held.TapeId == key.TapeId,
                    "resends never replace the instance (dynamic " + dynamic + ")");
            }
            WeatherKey first = new WeatherKey(9u, 10f, true, 3);
            WeatherKey rebased = first.Rebased(500f, false);
            TestAssert.That(ReferenceEquals(WeatherKeyWire.Reuse(first, rebased), rebased), "a real change does replace it");
        }
    }
}
