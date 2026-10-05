using System;
using BoscaliSummer.Modules.Weather.Domain;
using BoscaliSummer.Modules.Weather.Visuals;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The CPU mirror of the shader's cloud bodies: cover means that share of the sky is
    /// cloud, the rest stays open, and an empty map casts nothing.</summary>
    internal static class CloudBodiesTests
    {
        public static void Run()
        {
            byte[] noise = CloudNoise3D.Generate(64, 47);
            var bodies = new CloudBodies(noise, 64, new StateParams { LayerDepth = 1200f });
            float scattered = CloudFraction(bodies, 0.35f);
            float broken = CloudFraction(bodies, 0.65f);
            float overcast = CloudFraction(bodies, 0.95f);
            TestAssert.That(scattered > 0.12f && scattered < 0.55f, $"35 % cover should leave most sky open, got {scattered:F2}");
            TestAssert.That(broken > scattered + 0.1f, $"more cover makes more cloud ({scattered:F2} -> {broken:F2})");
            TestAssert.That(overcast > 0.85f, $"near-full cover closes the deck, got {overcast:F2}");
            TestAssert.That(CloudFraction(bodies, 0f) == 0f, "an empty map casts nothing");
            TestAssert.That(CloudBodies.CoverMask(0.2f, 0.3f) == 0f && CloudBodies.CoverMask(0.95f, 0.3f) > 0.5f,
                "cover thresholds bodies into separate clouds");

            // Stratus: a smooth deck at the same cover closes more of the sky than lumpy bodies.
            var stratus = new CloudBodies(noise, 64, new StateParams { LayerDepth = 1200f, LayerSmooth = 1f });
            TestAssert.That(CloudFraction(stratus, 0.65f) > broken, "a smooth stratus deck is more continuous");

            // The middle layer exists on its own: altostratus at 4 km with no low cloud.
            var alto = new CloudBodies(noise, 64, new StateParams { MidCover = 0.9f, MidSheet = 1f });
            int inside = 0;
            for (int i = 0; i < 40; i++) if (alto.MidLayer(i * 1500f, 4100f, i * 900f) > 0.02f) inside++;
            TestAssert.That(inside > 25, "altostratus is a near-continuous middle sheet, got " + inside + "/40");
            TestAssert.That(alto.MidLayer(0f, 1500f, 0f) == 0f && alto.MidLayer(0f, 7000f, 0f) == 0f,
                "the middle layer stays in its altitude band");

            // Set-pieces are mirrored as smooth envelopes: each formation reads as cloud on
            // the CPU where the shader draws it, with no effect far away. An empty weather
            // point isolates the hero contribution (no low deck, below the middle layer).
            var key = new WeatherKey(12345u, 0f, false, (byte)WeatherRegimeType.Storm, 5f, 60f,
                (byte)(Superstructures.StormEyeSet | Superstructures.LenticularSet), 0, true, 0f, 25000f, 0);
            var field = new WeatherField();
            field.Build(key, 900f, 60000f, 60000f, 13f);
            TestAssert.That(field.SuperstructureCount >= 2, "a storm holds set-pieces, got " + field.SuperstructureCount);
            var withHeroes = new CloudBodies(noise, 64, field.Params, field.PrevailingHeading, field.Split, 0f, field);
            var withoutHeroes = new CloudBodies(noise, 64, field.Params, field.PrevailingHeading, field.Split);
            var empty = new WeatherPoint();
            for (int i = 0; i < field.SuperstructureCount; i++)
            {
                Superstructure s = field.SuperstructureAt(i);
                float dirX = (float)Math.Cos(s.Heading), dirZ = (float)Math.Sin(s.Heading);
                if (s.Kind == SuperstructureKind.ShelfLine)
                {
                    float v = -s.Extent * 0.5f;
                    float d = withHeroes.Density(empty, s.X + dirZ * v, 3000f, s.Z - dirX * v, 0f);
                    TestAssert.That(d > 0.5f * s.Strength, $"shelf storm mass reads as cloud, got {d:F2}");
                }
                else if (s.Kind == SuperstructureKind.Supercell)
                {
                    float d = withHeroes.Density(empty, s.X, 3000f, s.Z, 0f);
                    TestAssert.That(d > 0.5f * s.Strength, $"supercell tower reads as cloud, got {d:F2}");
                }
                else if (s.Kind == SuperstructureKind.StormEye)
                {
                    float inner = s.Size * 1.04f;
                    float wallR = (inner + 2750f + s.Size + s.Extent * 0.8f) * 0.5f;
                    float wall = withHeroes.Density(empty, s.X + wallR, 3000f, s.Z, 0f);
                    TestAssert.That(wall > 0.5f * s.Strength, $"eyewall reads as cloud, got {wall:F2}");
                    float floorD = withHeroes.Density(empty, s.X, 1000f, s.Z, 0f);
                    TestAssert.That(floorD > 0.3f * s.Strength, $"eye floor reads as cloud, got {floorD:F2}");
                    float band = withHeroes.Density(empty, s.X + s.Size + s.Extent * 2f, 2000f, s.Z, 0f);
                    TestAssert.That(band > 0.2f * s.Strength, $"rain band reads as cloud, got {band:F2}");
                }
                else
                {
                    float d = withHeroes.Density(empty, s.X + dirX * s.Extent, s.Top + 700f, s.Z + dirZ * s.Extent, 0f);
                    TestAssert.That(d > 0.4f * s.Strength, $"lenticular reads as cloud, got {d:F2}");
                }
                float far = withHeroes.Density(empty, s.X + 200000f, 3000f, s.Z, 0f);
                float farPlain = withoutHeroes.Density(empty, s.X + 200000f, 3000f, s.Z, 0f);
                TestAssert.That(far == farPlain, "set-pieces stay inside their bounds");
            }
        }

        /// <summary>Share of columns with cloud 300 m above the base, over a 60 km square.</summary>
        private static float CloudFraction(CloudBodies bodies, float cover)
        {
            var point = new WeatherPoint { BackgroundCover = cover, CloudBase = 1500f, CloudTop = 3200f };
            int cloudy = 0, n = 0;
            for (int i = 0; i < 60; i++)
            for (int j = 0; j < 60; j++)
            {
                if (bodies.Density(point, i * 1000f + 13f, 1800f, j * 1000f + 7f, 0f) > 0.02f) cloudy++;
                n++;
            }
            return (float)cloudy / n;
        }
    }
}
