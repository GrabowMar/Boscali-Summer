using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// The native outputs by peer role (spec 8.1) and invariant 6 (9.6): <c>level.conditions</c> never exceeds 0.6 and its eased rate stays within 0.04/s while a pace-6 front and an
    /// aircraft at 250 m/s cross a tape; the sun gate of vanilla never flips; a server's natives are a pure function of the key and the clock; a held tape still follows the camera.
    /// </summary>
    internal static class NativeWeatherTests
    {
        private static readonly NativeBaseline Mission = NativeBaseline.Mission(-9f, 3f, 0.1f);

        public static void Run()
        {
            CapAndMapping();
            ServerIsAPureFunctionOfKeyAndTime();
            ClientFollowsTheCameraEvenWhenHeld();
            ClientRecoversTheMissionWind();
            EasingRates();
            WindNeverVeersOrReverses();
            FrontAndFastAircraftNeverFlipTheSun();
        }

        private static void CapAndMapping()
        {
            // The literals are pinned here, once: every other check compares against the vanilla gate (0.6) and the spec rates, never against a value the code could have changed with them.
            TestAssert.That(NativeWeather.ConditionsCap == 0.6f && NativeWeather.ConditionsRate == 0.04f && NativeWeather.CloudHeightRate == 50f && NativeWeather.WindRate == 4f && NativeWeather.TurbulenceRate == 0.1f,
                "the cap is vanilla's strict gate (conditions > 0.6 kills the sun) and the rates are the 60 s transition of 8.1: 0.04/s, 50 m/s, 4 m/s2, 0.1/s");
            TestAssert.That(NativeWeather.Conditions(1f, 0f, Mission) == 0.6f, "a full overcast is capped at exactly vanilla's gate (Near's 0.01 tolerance would let 0.605 through and grey the fog)");
            TestAssert.Near(NativeWeather.Conditions(0.3f, 0f, Mission), 0.3f, "a mid cover passes through");
            TestAssert.Near(NativeWeather.Conditions(0.3f, 1f, Mission), 0.135f, "the convective term takes 55% off (cell rain makes its own haze)");
            TestAssert.Near(NativeWeather.Conditions(0f, 0f, Mission), 0.05f, "the host's clearest-sky entry is the floor");
            TestAssert.Near(NativeWeather.Conditions(float.NaN, 0f, Mission), 0.05f, "a non-finite cover reads as clear, not as NaN");
            var wild = new NativeBaseline(0f, 0f, 0f, 0f, 0.9f, 0.1f, 0.5f, 1f);
            TestAssert.That(NativeWeather.Conditions(0.5f, 0f, wild) <= 0.6f, "swapped min and max still cap");
            for (int i = 0; i < 4000; i++)
            {
                float cover = WeatherMath.HashRange(7u, i, 1, 0, -0.5f, 1.5f), convective = WeatherMath.HashRange(7u, i, 2, 0, -0.5f, 1.5f);
                float c = NativeWeather.Conditions(cover, convective, new NativeBaseline(0f, 0f, 0f, 0f, WeatherMath.HashRange(7u, i, 3, 0, 0f, 0.4f), WeatherMath.HashRange(7u, i, 4, 0, 0.5f, 1f), 0.5f, 1f));
                TestAssert.That(c >= 0f && c <= 0.6f && !(c > 0.6f), "conditions stay inside 0-0.6, the literal vanilla gate, for every input (case " + i + ": " + c + ")");
            }
        }

        private static WeatherKey Key(uint seed, byte story, float heading, float offset, bool dynamic = false, float speed = 27f) =>
            new WeatherKey(seed, 0f, dynamic, 1, story: story, headingDeg: heading, offset: offset, systemSpeed: speed);

        private static WeatherField Built(WeatherKey key, float time = 0f, bool moving = false)
        {
            var field = new WeatherField();
            field.Build(key, time, 81920f, 40960f, 12f, 1f, moving);
            return field;
        }

        private static void ServerIsAPureFunctionOfKeyAndTime()
        {
            for (uint seed = 1; seed <= 12; seed++)
                for (byte cls = 0; cls < 7; cls++)
                {
                    var key = new WeatherKey(seed, 0f, false, cls, offset: 150000f + 9000f * seed);
                    WeatherField a = Built(key);
                    for (int i = 0; i < 20; i++) a.Sample(i * 3000f - 30000f, i * 1100f);   // warm every cache with other points first
                    WeatherField b = Built(key, 4000f);
                    NativeValues x = NativeWeather.Server(a, Mission), y = NativeWeather.Server(b, Mission);
                    TestAssert.That(x.Conditions == y.Conditions && x.CloudHeight == y.CloudHeight && x.WindX == y.WindX && x.WindZ == y.WindZ && x.Turbulence == y.Turbulence,
                        "a held server's natives are the same at any clock and any cache state (seed " + seed + ", class " + cls + ")");
                    TestAssert.That(x.Conditions <= 0.6f && x.CloudHeight >= SkySummary.MinDeck && x.CloudHeight <= SkySummary.MaxDeck, "and stay inside their ranges");
                }
            var moving = Key(9u, 0, 200f, 100000f, true, 30f);
            NativeValues first = NativeWeather.Server(Built(moving, 600f, true), Mission), again = NativeWeather.Server(Built(moving, 600f, true), Mission);
            TestAssert.That(first.Conditions == again.Conditions && first.WindX == again.WindX, "a moving server's natives are a function of (key, mission time)");
        }

        /// <summary>The theater centre stands 30 km before the first marker of a Frontal system; the camera is either there or 50 km upwind, past the line.</summary>
        private static void ClientFollowsTheCameraEvenWhenHeld()
        {
            int differing = 0, runs = 0;
            for (uint seed = 1; seed <= 40; seed++)
            {
                WeatherTape tape = WeatherTape.For(Key(seed, 0, 250f, 0f));
                if (tape.Markers.Count == 0) continue;
                float meander = tape.Frame(250f, 0f).Meander(0f);
                WeatherKey held = Key(seed, 0, 250f, tape.Markers.Items[0].S - 30000f - meander);
                WeatherField field = Built(held);
                TestAssert.That(!held.Dynamic && !field.Moving, "a held tape");
                WeatherMath.HeadingToVector(250f, out float ux, out float uz);
                NativeValues here = NativeWeather.Client(field, 0f, 0f, 0f, 0f, Mission);
                NativeValues past = NativeWeather.Client(field, ux * 50000f, uz * 50000f, ux * 50000f, uz * 50000f, Mission);
                runs++;
                if (Math.Abs(here.Conditions - past.Conditions) > 0.03f || Math.Abs(here.CloudHeight - past.CloudHeight) > 100f) differing++;
                TestAssert.That(here.Conditions <= 0.6f && past.Conditions <= 0.6f, "client conditions obey the cap");
                NativeValues server = NativeWeather.Server(field, Mission);
                TestAssert.That(Math.Abs(server.WindX - here.WindX) < 12f && !float.IsNaN(here.WindX + here.WindZ + here.Turbulence), "client wind is a finite scale of the mission wind");
            }
            TestAssert.That(runs >= 20 && differing >= runs / 2, "a held tape still follows the camera: the sky across a front differs in " + differing + " of " + runs + " cases (no drive gate)");
        }

        /// <summary>
        /// A late joiner may have captured the host's already-scaled wind (the wind fields of <c>LevelInfo</c> are SyncVars the host writes): the baseline derived from the message target is the mission's either way,
        /// and a client at the theater centre then follows the host's wind and turbulence exactly instead of scaling them twice.
        /// </summary>
        private static void ClientRecoversTheMissionWind()
        {
            int cases = 0;
            for (uint seed = 1; seed <= 30; seed++)
                foreach (float variability in new[] { 0f, 0.5f, 1f })
                {
                    WeatherField field = Built(Key(seed, (byte)(seed % 2), 250f, 30000f * (seed % 7)));
                    var mission = new NativeBaseline(-9f, 0f, 3f, 0.1f, 0.05f, 0.88f, variability, 1f);
                    NativeValues sent = NativeWeather.Server(field, mission);   // what the host's message carries
                    var scaled = new NativeBaseline(sent.WindX, 0f, sent.WindZ, sent.Turbulence, 0.05f, 0.88f, variability, 1f);   // what a joiner reads if the host's SyncVars arrive first
                    foreach (NativeBaseline captured in new[] { mission, scaled })
                    {
                        NativeBaseline got = NativeWeather.MissionFromTargets(field.Regional(), captured, sent.WindX, sent.WindZ, sent.Turbulence);
                        TestAssert.That(Math.Abs(got.WindX - -9f) < 1e-3f && Math.Abs(got.WindZ - 3f) < 1e-3f, "the mission wind is recovered from the message target (seed " + seed + ", variability " + variability + ")");
                        NativeValues mine = NativeWeather.Client(field, 0f, 0f, 0f, 0f, got);
                        TestAssert.That(Math.Abs(mine.WindX - sent.WindX) < 1e-3f && Math.Abs(mine.WindZ - sent.WindZ) < 1e-3f && Math.Abs(mine.Turbulence - sent.Turbulence) < 1e-4f,
                            "a client at the theater centre follows the host's wind and turbulence exactly, with no second scaling (seed " + seed + ", variability " + variability + ")");
                        cases++;
                    }
                }
            TestAssert.That(cases == 180, "the inversion ran for both kinds of captured baseline (" + cases + ")");
            NativeBaseline kept = NativeWeather.MissionFromTargets(Built(Key(1u, 0, 250f, 0f)).Regional(), Mission, float.NaN, float.NaN, float.NaN);
            TestAssert.That(kept.WindX == Mission.WindX && kept.WindZ == Mission.WindZ && kept.Turbulence == Mission.Turbulence, "a non-finite message keeps what was captured");
        }

        private static void EasingRates()
        {
            var from = new NativeValues { Conditions = 0.05f, CloudHeight = 800f, WindX = -9f, WindZ = 3f, Turbulence = 0.1f };
            var to = new NativeValues { Conditions = 0.6f, CloudHeight = 9000f, WindX = 6f, WindY = 1f, WindZ = -20f, Turbulence = 0.9f };
            NativeValues now = from;
            const float dt = 0.05f;
            float worstC = 0f, worstH = 0f, worstW = 0f, worstT = 0f;
            for (int i = 0; i < 4000; i++)
            {
                NativeValues target = (i / 400) % 2 == 0 ? to : from;
                NativeValues next = NativeWeather.Ease(now, target, dt);
                worstC = Math.Max(worstC, Math.Abs(next.Conditions - now.Conditions) / dt);
                worstH = Math.Max(worstH, Math.Abs(next.CloudHeight - now.CloudHeight) / dt);
                worstT = Math.Max(worstT, Math.Abs(next.Turbulence - now.Turbulence) / dt);
                float dx = next.WindX - now.WindX, dy = next.WindY - now.WindY, dz = next.WindZ - now.WindZ;
                worstW = Math.Max(worstW, (float)Math.Sqrt(dx * dx + dy * dy + dz * dz) / dt);
                now = next;
            }
            // Literal rates, with only float rounding of (next - now) / dt as slack (about 1.2e-6 at 0.6): a rate of 0.08 or 0.041 fails.
            TestAssert.That(worstC <= 0.04f + 1e-5f && worstC > 0.039f, "conditions move at 0.04/s (worst " + worstC + ")");
            TestAssert.That(worstH <= 50f + 1e-2f && worstW <= 4f + 1e-3f && worstT <= 0.1f + 1e-4f,
                "base 50 m/s, wind 4 m/s2, turbulence 0.1/s (worst " + worstH + ", " + worstW + ", " + worstT + ")");
            TestAssert.That(NativeWeather.Ease(from, to, 0f).Conditions == from.Conditions, "no time, no step");
            var broken = new NativeValues { Conditions = float.NaN, CloudHeight = float.NaN, Turbulence = float.NaN, WindX = 6f, WindZ = -20f };
            NativeValues held = NativeWeather.Ease(from, broken, 0.05f);   // Math.Sign(NaN) throws: a NaN target must hold the current value instead
            TestAssert.That(held.Conditions == from.Conditions && held.CloudHeight == from.CloudHeight && held.Turbulence == from.Turbulence, "a NaN target holds the current value instead of throwing every frame");
            NativeValues settled = from;
            for (int i = 0; i < 4000; i++) settled = NativeWeather.Ease(settled, to, 0.05f);
            TestAssert.That(settled.Conditions == to.Conditions && settled.WindZ == to.WindZ && settled.CloudHeight == to.CloudHeight, "it arrives and stops");
        }

        private static void WindNeverVeersOrReverses()
        {
            for (uint seed = 1; seed <= 30; seed++)
            {
                WeatherTape tape = WeatherTape.For(Key(seed, 1, 120f, 0f));
                if (tape.Markers.Count == 0) continue;
                float meander = tape.Frame(120f, 0f).Meander(0f);
                WeatherField field = Built(Key(seed, 1, 120f, tape.Markers.Items[0].S - meander));   // the centre stands on the cold or squall line
                WeatherPoint at = field.Sample(0f, 0f);
                NativeWeather.Wind(at, Mission, out float x, out float y, out float z);
                float cross = x * 3f - z * -9f, dot = x * -9f + z * 3f;
                TestAssert.That(Math.Abs(cross) < 1e-3f * (Math.Abs(dot) + 1f) && dot > 0f, "the native wind keeps the mission direction through a front's veer (seed " + seed + ")");
                TestAssert.That(y == 0f, "vertical wind is the mission's");
            }
        }

        /// <summary>Invariant 6: a pace-6 front (45 m/s) crosses the centre while an aircraft flies at 250 m/s across the theater; the eased conditions never pass 0.6, never move faster than
        /// 0.04/s, and the vanilla gate (conditions above 0.6, which kills the native sun) never opens.</summary>
        private static void FrontAndFastAircraftNeverFlipTheSun()
        {
            int fronts = 0;
            for (uint seed = 1; seed <= 12; seed++)
            {
                foreach (byte story in new byte[] { 0, 1 })
                {
                    WeatherTape tape = WeatherTape.For(Key(seed, story, 90f, 0f));
                    if (tape.Markers.Count == 0) continue;
                    fronts++;
                    float meander = tape.Frame(90f, 0f).Meander(0f);
                    // D starts 70 km before the first marker line and runs at 45 m/s for 40 minutes: the whole approach, the line and the wake pass the centre.
                    WeatherKey key = Key(seed, story, 90f, tape.Markers.Items[0].S - 70000f - meander, true, 45f);
                    WeatherField field = new WeatherField();
                    NativeValues server = default, client = default;
                    bool started = false;
                    float worstServer = 0f, worstClient = 0f, peak = 0f, lastServer = 0f, lastClient = 0f;
                    for (int second = 0; second < 2400; second++)
                    {
                        field.Build(key, second, 81920f, 40960f, 12f, 1f, true);
                        NativeValues serverTarget = NativeWeather.Server(field, Mission);
                        float ax = -60000f + 250f * (second % 480), az = 5000f;   // an aircraft at 250 m/s crossing the theater again and again
                        NativeValues clientTarget = NativeWeather.Client(field, ax, az, ax, az, Mission);
                        if (!started) { server = serverTarget; client = clientTarget; started = true; lastServer = server.Conditions; lastClient = client.Conditions; }
                        for (int tick = 0; tick < 20; tick++)
                        {
                            server = NativeWeather.Ease(server, serverTarget, 0.05f);
                            client = NativeWeather.Ease(client, clientTarget, 0.05f);
                            worstServer = Math.Max(worstServer, Math.Abs(server.Conditions - lastServer) / 0.05f);
                            worstClient = Math.Max(worstClient, Math.Abs(client.Conditions - lastClient) / 0.05f);
                            lastServer = server.Conditions; lastClient = client.Conditions;
                            peak = Math.Max(peak, Math.Max(server.Conditions, client.Conditions));
                        }
                    }
                    TestAssert.That(peak <= 0.6f && !(peak > 0.6f), "the sun gate (conditions > 0.6) never opens (peak " + peak + ", seed " + seed + ", story " + story + ")");
                    TestAssert.That(worstServer <= 0.04f + 1e-5f && worstClient <= 0.04f + 1e-5f,
                        "eased conditions move at most 0.04/s (server " + worstServer + ", client " + worstClient + ")");
                }
            }
            TestAssert.That(fronts >= 12, "the sweep crossed at least twelve fronts (" + fronts + ")");
        }
    }
}
