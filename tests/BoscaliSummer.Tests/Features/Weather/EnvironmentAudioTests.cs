using System;
using BoscaliSummer.Modules.Immersion.Audio;
using BoscaliSummer.Modules.Weather.Audio;

namespace BoscaliSummer.Tests.Features.Weather
{
    internal static class EnvironmentAudioTests
    {
        public static void Run()
        {
            var ground = EnvironmentAudioMath.Rain(1f, false, 0f, 2f, 1f, 0f);
            var airborne = EnvironmentAudioMath.Rain(1f, false, 250f, 3000f, 1f, 0f);
            var sheltered = EnvironmentAudioMath.Rain(1f, false, 0f, 2f, 0f, 0f);
            TestAssert.That(ground.gain > airborne.gain * 5f && ground.cutoff > airborne.cutoff,
                "Nearby surface rain is brighter and stronger than free-air rain thousands of metres aloft");
            TestAssert.That(sheltered.gain > 0f && sheltered.gain < ground.gain * 0.3f && sheltered.cutoff == 900f,
                "A roof removes local impacts but retains quiet filtered rain outside");
            TestAssert.That(EnvironmentAudioMath.Rain(1f, true, 250f, 2f, 0f, 0f).gain == 0f,
                "Sheltered cockpit glass gets no incoming rain impact sound");
            TestAssert.That(EnvironmentAudioMath.Rain(0f, false, 250f, 0f, 1f, 0f).gain == 0f &&
                EnvironmentAudioMath.Rain(0f, true, 250f, 0f, 1f, 0f).gain == 0f,
                "Dry ground and cloud condensation cannot create falling-rain sound");
            TestAssert.That(EnvironmentAudioMath.Rain(1f, true, 250f, 2f, 1f, 0f).gain >
                EnvironmentAudioMath.Rain(1f, true, 0f, 2f, 1f, 0f).gain,
                "Canopy impact energy increases gently with incoming slipstream");
            TestAssert.That(EnvironmentAudioMath.Rain(1f, false, 0f, float.NaN, 1f, 0f).gain < ground.gain * 0.2f,
                "Unknown terrain distance uses quiet free-air sound rather than ground impacts");
            TestAssert.That(EnvironmentAudioMath.Rain(float.NaN, false, 0f, 0f, 1f, 0f).gain == 0f,
                "Invalid rain intensity cannot reach an AudioSource");
            float previousGain = float.MaxValue, previousCutoff = float.MaxValue;
            for (int height = 0; height <= 12000; height += 50)
            {
                var mix = EnvironmentAudioMath.Rain(1f, false, 250f, height, 1f, 100f);
                TestAssert.That(mix.gain <= previousGain && mix.cutoff <= previousCutoff,
                    "Surface impact gain/timbre fade continuously with observer height");
                previousGain = mix.gain; previousCutoff = mix.cutoff;
            }
            for (int time = 0; time <= 600; time++)
            {
                foreach (bool cockpit in new[] { false, true })
                {
                    var mix = EnvironmentAudioMath.Rain(1f, cockpit, 900f, 0f, 1f, time);
                    TestAssert.That(mix.gain >= 0f && mix.gain < 0.31f && mix.cutoff >= 900f && mix.cutoff <= 7500f,
                        "Irregular rain modulation retains source headroom and bounded spectral range");
                }
            }
            TestAssert.That(EnvironmentAudioMath.ThunderCutoff(100f, false) >
                EnvironmentAudioMath.ThunderCutoff(8000f, false), "Distant thunder loses its sharp crack");
            TestAssert.That(EnvironmentAudioMath.ThunderCutoff(100f, true) <
                EnvironmentAudioMath.ThunderCutoff(100f, false), "Cockpit shelter softens thunder independently of distance");
            TestAssert.That(EnvironmentAudioMath.ThunderCutoff(10000f, true) >= 600f,
                "Distant cockpit thunder retains its low rumble rather than becoming inaudible");

            var sea = CanopyWindMath.Target(250f, 2f, 1f, 0f, 0f);
            var high = CanopyWindMath.Target(250f, 2f, 0.15f, 0f, 0f);
            TestAssert.That(high.volume < sea.volume * 0.4f && high.pitch < sea.pitch,
                "Equivalent airspeed reduces canopy rush at high altitude without altering native aircraft audio");
            TestAssert.That(CanopyWindMath.Target(250f, 2f, 1f, 1f, 0f).volume < sea.volume * 0.4f,
                "Rain impact texture retains headroom beside wind instead of stacking two loud noise beds");
            TestAssert.That(CanopyWindMath.Target(39f, 0f, 1f, 0f, 0f).volume == 0f &&
                CanopyWindMath.Target(300f, 0f, 0f, 0f, 0f).volume == 0f,
                "Low dynamic pressure produces no supplemental cockpit wind");
            TestAssert.That(CanopyWindMath.Target(float.NaN, 2f, 1f, 0f, 0f).volume == 0f,
                "Invalid flight inputs cannot propagate NaN into the audio source");
            for (int time = 0; time <= 600; time++)
            {
                var wind = CanopyWindMath.Target(900f, 100f, 1.1f, 0f, time);
                TestAssert.That(wind.volume >= 0f && wind.volume <= 0.32f && wind.pitch >= 0.85f && wind.pitch <= 1.5f,
                    "Wind variation remains bounded even at maximum speed/gust/density");
            }
        }
    }
}
