using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>Invariant 1 (spec 9.6) at D != 0 through the pure field: <c>Sample(t+dt, p - u*v*dt)</c> equals <c>Sample(t, p)</c> within 1 m of tape displacement, for the
    /// outputs the spec lists. The pure field is built with <c>moving: true</c>; the live manager holds D until slice 5 wires the motion itself.</summary>
    internal static class TapeRigidityTests
    {
        public static void Run()
        {
            for (uint seed = 1; seed <= 24; seed++)
                foreach (byte story in new byte[] { WeatherKey.None, 0, 1, 2 })
                    Rigid(seed, story);
            HeldFieldIgnoresTheClock();
        }

        private static void Rigid(uint seed, byte story)
        {
            float v = 18f + 27f * (seed % 4) / 3f, heading = seed * 53f % 360f;
            var key = new WeatherKey(seed, 0f, true, (byte)(seed % 7), story: story, headingDeg: heading, offset: 640000f + 9000f * seed, systemSpeed: v);
            var a = new WeatherField(); var b = new WeatherField(); var probe = new WeatherField();
            const float t = 1200f, dt = 60f;
            a.Build(key, t, 81920f, 40960f, 12f, 1f, moving: true);
            b.Build(key, t + dt, 81920f, 40960f, 12f, 1f, moving: true);
            probe.Build(key, t, 81920f, 40960f, 12f, 1f, moving: true);
            TestAssert.That(a.Moving && Math.Abs(b.Frame.Offset - a.Frame.Offset - v * dt) < 0.5f, "D follows the clock when asked");
            WeatherMath.HeadingToVector(heading, out float ux, out float uz);
            for (int i = 0; i < 160; i++)
            {
                float x = WeatherMath.HashRange((uint)(seed * 31 + story), i, 1, 0, -70000f, 70000f), z = WeatherMath.HashRange((uint)(seed * 31 + story), i, 2, 0, -40000f, 40000f);
                WeatherPoint now = a.Sample(x, z), later = b.Sample(x - ux * v * dt, z - uz * v * dt);
                WeatherPoint lo = probe.Sample(x - ux, z - uz), hi = probe.Sample(x + ux, z + uz);
                Within(now.BackgroundCover, lo.BackgroundCover, hi.BackgroundCover, later.BackgroundCover, "BackgroundCover");
                Within(now.ClusterCover, lo.ClusterCover, hi.ClusterCover, later.ClusterCover, "ClusterCover");
                Within(now.CellCover, lo.CellCover, hi.CellCover, later.CellCover, "CellCover");
                Within(now.LowCover, lo.LowCover, hi.LowCover, later.LowCover, "LowCover");
                Within(now.CellShape, lo.CellShape, hi.CellShape, later.CellShape, "CellShape");
                Within(now.CloudBase, lo.CloudBase, hi.CloudBase, later.CloudBase, "CloudBase");
                Within(now.LowTop, lo.LowTop, hi.LowTop, later.LowTop, "LowTop");
                Within(now.RainRate, lo.RainRate, hi.RainRate, later.RainRate, "RainRate");
                Within(now.WindX, lo.WindX, hi.WindX, later.WindX, "WindX");
                Within(now.WindZ, lo.WindZ, hi.WindZ, later.WindZ, "WindZ");
                Within(now.Gust, lo.Gust, hi.Gust, later.Gust, "Gust");
                Within(now.Turbulence, lo.Turbulence, hi.Turbulence, later.Turbulence, "Turbulence");
                Within(now.LightningRate, lo.LightningRate, hi.LightningRate, later.LightningRate, "LightningRate");
                Within(now.CoreDepth, lo.CoreDepth, hi.CoreDepth, later.CoreDepth, "CoreDepth");
                Within(now.ConvectiveShare, lo.ConvectiveShare, hi.ConvectiveShare, later.ConvectiveShare, "ConvectiveShare");
                Within(now.Qnh, lo.Qnh, hi.Qnh, later.Qnh, "Qnh");
            }
        }

        /// <summary>The value later lies between the samples one metre either side of the original point, to rounding.</summary>
        private static void Within(float at, float low, float high, float later, string what)
        {
            float lo = Math.Min(at, Math.Min(low, high)), hi = Math.Max(at, Math.Max(low, high)), slack = 1e-3f + 1e-4f * Math.Abs(at);
            TestAssert.That(later >= lo - slack && later <= hi + slack, what + ": the slid sample matches within 1 m (" + later + " not in " + lo + ".." + hi + ")");
        }

        private static void HeldFieldIgnoresTheClock()
        {
            var key = new WeatherKey(5u, 0f, true, 2, offset: 300000f, systemSpeed: 40f);
            var early = new WeatherField(); var late = new WeatherField();
            early.Build(key, 100f, 81920f, 40960f);
            late.Build(key, 5000f, 81920f, 40960f);
            TestAssert.That(!early.Moving && early.Frame.Offset == late.Frame.Offset && early.Sample(1000f, 2000f).LowCover == late.Sample(1000f, 2000f).LowCover,
                "slice 4a holds D at the key's offset whatever the clock says");
        }
    }
}
