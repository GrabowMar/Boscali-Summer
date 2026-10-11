using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The adapter's one piece of geometry: a legacy front drawn as a polyline lies exactly on the front.</summary>
    internal static class MetOfficeFrontTests
    {
        public static void Run()
        {
            PolylineLiesOnTheFront();
            KindsAndDirection();
            FieldFrontsFillTheChart();
        }

        private static FrontState Front(FrontKind kind, float heading, float offset, float amplitude)
        {
            WeatherMath.HeadingToVector(heading, out float nx, out float nz);
            return new FrontState { Kind = kind, NormalX = nx, NormalZ = nz, Offset = offset, Strength = 0.7f, Id = 1, MeanderAmplitude = amplitude, MeanderWavelength = 60000f, MeanderPhase = 0.8f };
        }

        private static void PolylineLiesOnTheFront()
        {
            foreach (float heading in new[] { 0f, 70f, 215f, 300f })
                foreach (float amplitude in new[] { 0f, 3000f })
                {
                    FrontState f = Front(FrontKind.Cold, heading, -30000f, amplitude);
                    var line = new MetFront();
                    MetFrontLine.From(f, line);
                    TestAssert.That(line.Count == MetFront.MaxPoints, "the polyline uses every point");
                    for (int i = 0; i < line.Count; i++)
                        TestAssert.That(Math.Abs(f.SignedDistance(line.X[i] * 1000f, line.Z[i] * 1000f)) < 25f,
                            "point " + i + " of the " + heading + " degree front sits on the line (" + f.SignedDistance(line.X[i] * 1000f, line.Z[i] * 1000f) + " m off)");
                    float dx = line.X[line.Count - 1] - line.X[0], dz = line.Z[line.Count - 1] - line.Z[0];
                    TestAssert.That(Math.Sqrt(dx * dx + dz * dz) >= 299f, "the line spans the whole chart");
                }
        }

        private static void KindsAndDirection()
        {
            var line = new MetFront();
            MetFrontLine.From(Front(FrontKind.Warm, 90f, 0f, 0f), line);
            TestAssert.That(line.Kind == MetFrontKind.Warm && Math.Abs(line.DirX - 1f) < 1e-4f && Math.Abs(line.DirZ) < 1e-4f && Math.Abs(line.Strength - 0.7f) < 1e-4f,
                "kind, strength and the direction the symbols face (the normal)");
            TestAssert.That(float.IsNaN(line.EtaMinutes), "a legacy front never arrives: ETA is unknown");
            MetFrontLine.From(Front(FrontKind.Squall, 90f, 0f, 0f), line);
            TestAssert.That(line.Kind == MetFrontKind.Squall, "a squall front stays a squall line");
            MetFrontLine.From(Front(FrontKind.Cold, 90f, 0f, 0f), line);
            TestAssert.That(line.Kind == MetFrontKind.Cold, "cold");
        }

        private static void FieldFrontsFillTheChart()
        {
            var field = new WeatherField();
            field.Build(new WeatherKey(90210u, 0f, false, (byte)WeatherRegimeType.Storm), 900f, 60000f, 60000f, 13f);
            TestAssert.That(field.FrontCount >= 1, "a storm sky has a front to draw");
            var model = new MetOfficeModel();
            for (int i = 0; i < field.FrontCount && i < MetOfficeModel.MaxFronts; i++) MetFrontLine.From(field.Front(i), model.Fronts[i]);
            model.FrontCount = Math.Min(field.FrontCount, MetOfficeModel.MaxFronts);
            var order = new int[MetOfficeModel.MaxFronts]; var dist = new float[MetOfficeModel.MaxFronts];
            TestAssert.That(MetChartMath.NextFronts(model, order, dist) == model.FrontCount, "every front of the field is listed in the next-fronts card");
        }
    }
}
