using System;
using BoscaliSummer.Features.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>
    /// Canopy rain behaves like water on glass: beads while hovering, races aft in flight,
    /// sheds at speed, conserves water when drops merge, never exceeds its pool.
    /// </summary>
    internal static class DropSimTests
    {
        private static CanopyForces Level(float airspeed, float rain) => new CanopyForces
        {
            Airspeed = airspeed,
            GravityRight = 0f,
            GravityUp = -9.81f,
            GravityForward = 0f,
            RainRate = rain,
        };

        public static void Run()
        {
            PhysicsScalesWithSize();
            HoverBeads();
            CruiseRacesAft();
            HighSpeedSheds();
            MergeConservesWater();
            PoolIsBounded();
        }

        private static void PhysicsScalesWithSize()
        {
            var sim = new DropSim(64, 1.2f, 1.4f);
            CanopyForces hover = Level(0f, 10f);
            // On the steep windscreen, gravity moves a big drop but not a small one.
            sim.Accelerations(hover, 0f, 0.05f, 0.002f, out float ax, out float ay);
            float net = (float)Math.Sqrt(ax * ax + ay * ay);
            TestAssert.That(net > DropSim.StickK / (0.002f * 0.002f), "a 2 mm drop slides down the windscreen");
            TestAssert.That(net < DropSim.StickK / (0.0006f * 0.0006f), "a 0.6 mm bead stays put");
            TestAssert.That(ay < 0f, "gravity pulls windscreen water forward/down while hovering");

            // Airflow at 60 m/s moves a 1 mm drop aft even against the windscreen slope.
            sim.Accelerations(Level(60f, 10f), 0f, 0.05f, 0.001f, out _, out float fast);
            TestAssert.That(fast > DropSim.StickK / (0.001f * 0.001f), "60 m/s pushes a 1 mm drop aft");
        }

        private static void HoverBeads()
        {
            var sim = new DropSim(600, 1.2f, 1.4f, 42u);
            CanopyForces f = Level(0f, 15f);
            for (int i = 0; i < 600; i++) sim.Step(f, 1f / 60f);
            TestAssert.That(sim.Count > 100, "hovering in rain collects drops: " + sim.Count);
            int stillOnTop = 0, topDrops = 0;
            for (int i = 0; i < sim.Count; i++)
            {
                Drop d = sim.Get(i);
                if (d.Y > 0.5f * sim.Length && d.Y < 0.8f * sim.Length && Math.Abs(d.X) < 0.1f)
                {
                    topDrops++;
                    if (!d.Moving) stillOnTop++;
                }
            }
            TestAssert.That(topDrops == 0 || stillOnTop >= topDrops / 2, "beads on the flat top stay put while hovering");
        }

        private static void CruiseRacesAft()
        {
            var sim = new DropSim(600, 1.2f, 1.4f, 7u);
            CanopyForces f = Level(90f, 15f);
            float meanVy = 0f;
            int samples = 0;
            for (int i = 0; i < 300; i++)
            {
                sim.Step(f, 1f / 60f);
                for (int k = 0; k < sim.Count; k++)
                {
                    Drop d = sim.Get(k);
                    if (d.Moving)
                    {
                        meanVy += d.VY;
                        samples++;
                    }
                }
            }
            TestAssert.That(samples > 0 && meanVy / samples > 0.2f, "in flight the water races aft");
        }

        private static void HighSpeedSheds()
        {
            var hover = new DropSim(600, 1.2f, 1.4f, 9u);
            var fast = new DropSim(600, 1.2f, 1.4f, 9u);
            for (int i = 0; i < 600; i++)
            {
                hover.Step(Level(0f, 10f), 1f / 60f);
                fast.Step(Level(250f, 10f), 1f / 60f);
            }
            TestAssert.That(fast.Count < hover.Count / 2, $"fast flight clears the glass ({fast.Count} vs {hover.Count})");

            // And water left from rain blows off once the rain stops.
            for (int i = 0; i < 300; i++) hover.Step(Level(250f, 0f), 1f / 60f);
            TestAssert.That(hover.Count < 20, "the glass clears after the rain at speed: " + hover.Count);
        }

        private static void MergeConservesWater()
        {
            var sim = new DropSim(64, 1.2f, 1.4f, 3u);
            // Rain-free still air: two overlapping drops placed through a zero-rain step.
            CanopyForces calm = Level(0f, 0f);
            calm.GravityUp = 0f;
            sim.Place(new Drop { X = 0f, Y = 0.7f, Radius = 0.001f });
            sim.Place(new Drop { X = 0.0005f, Y = 0.7f, Radius = 0.001f });
            float before = 2f * 0.001f * 0.001f * 0.001f;
            sim.Step(calm, 0.001f);
            TestAssert.That(sim.Count == 1, "touching drops merge");
            float r = sim.Get(0).Radius;
            float after = r * r * r;
            TestAssert.That(Math.Abs(after - before) / before < 0.02f, "merging conserves water");
        }

        private static void PoolIsBounded()
        {
            var sim = new DropSim(200, 1.2f, 1.4f, 5u);
            CanopyForces storm = Level(20f, 150f);
            storm.CloudDepth = 1f;
            for (int i = 0; i < 1200; i++)
            {
                sim.Step(storm, 1f / 30f);
                TestAssert.That(sim.Count <= sim.Capacity, "pool never exceeds capacity");
                TestAssert.That(sim.Wetness >= 0f && sim.Wetness <= 1f, "wetness in range");
            }
        }
    }
}
