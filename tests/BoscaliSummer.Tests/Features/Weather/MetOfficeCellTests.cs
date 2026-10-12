using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The chart's storm cells come from the storm lattice (slice 3): active cells around the theater, nearest first, capped to the model, in the chart's frame (world km).</summary>
    internal static class MetOfficeCellTests
    {
        private const float Half = 40960f;

        public static void Run()
        {
            ClearSkyHasNoCells();
            StormCellsAreTheActiveLatticeCellsInWorldKm();
            CapAndOrderAreDeterministic();
            RotatedFrameStillPlacesCellsInWorldMetres();
        }

        private static WeatherField Storm(uint seed, float heading = 0f)
        {
            var field = new WeatherField();
            field.Build(new WeatherKey(seed, 0f, false, (byte)WeatherRegimeType.Storm, headingDeg: heading), 600f, Half, Half);
            return field;
        }

        private static void ClearSkyHasNoCells()
        {
            var clear = new WeatherField();
            clear.Build(new WeatherKey(7u, 0f, false, (byte)WeatherRegimeType.Clear), 600f, Half, Half);
            var m = new MetOfficeModel { CellCount = 5 };
            MetOfficeCells.Fill(m, clear);
            TestAssert.Eq(m.CellCount, 0, "a clear sky has no storm cells on the chart");
        }

        private static void StormCellsAreTheActiveLatticeCellsInWorldKm()
        {
            WeatherField field = Storm(31u);
            var m = new MetOfficeModel();
            MetOfficeCells.Fill(m, field);
            TestAssert.That(m.CellCount > 0 && m.CellCount <= MetOfficeModel.MaxCells, "a storm sky puts cells on the chart, never more than the model holds (" + m.CellCount + ")");
            for (int i = 0; i < m.CellCount; i++)
            {
                MetCell c = m.Cells[i];
                TestAssert.That(Math.Abs(c.X) - c.Radius <= MetChartMath.RangeKm && Math.Abs(c.Z) - c.Radius <= MetChartMath.RangeKm, "every listed cell touches the chart");
                TestAssert.That(c.Radius >= StormLattice.RadiusMin * 0.001f - 1e-3f && c.Radius <= StormLattice.RadiusMax * 0.001f + 1e-3f, "the radius is the lattice radius in km");
                // The same cell is the storm out at that world point: the nearest active cell to its own centre is itself.
                TestAssert.That(field.FindNearestCell(c.X * 1000f, c.Z * 1000f, out FieldFeature f, rings: 1)
                    && Math.Abs(f.X * 0.001f - c.X) < 0.01f && Math.Abs(f.Z * 0.001f - c.Z) < 0.01f && Math.Abs(f.Radius * 0.001f - c.Radius) < 0.01f,
                    "the chart cell is a real active lattice cell, placed in world km");
            }
        }

        private static void CapAndOrderAreDeterministic()
        {
            WeatherField field = Storm(32u);
            var a = new MetOfficeModel(); var b = new MetOfficeModel();
            MetOfficeCells.Fill(a, field);
            MetOfficeCells.Fill(b, field);
            TestAssert.Eq(a.CellCount, b.CellCount, "the same field gives the same count");
            float last = -1f;
            for (int i = 0; i < a.CellCount; i++)
            {
                TestAssert.That(a.Cells[i].X == b.Cells[i].X && a.Cells[i].Z == b.Cells[i].Z, "and the same cells in the same order");
                float d = a.Cells[i].X * a.Cells[i].X + a.Cells[i].Z * a.Cells[i].Z;
                TestAssert.That(d >= last, "nearest the theater centre first");
                last = d;
            }
            // A dense sky with more than twelve cells on the chart is cut to twelve, the nearest ones.
            int most = 0;
            for (uint s = 1; s <= 40; s++)
            {
                var m = new MetOfficeModel();
                MetOfficeCells.Fill(m, Storm(s * 7919u));
                most = Math.Max(most, m.CellCount);
                TestAssert.That(m.CellCount <= MetOfficeModel.MaxCells, "never past the model's array");
            }
            TestAssert.That(most == MetOfficeModel.MaxCells, "some storm skies fill the model (most seen " + most + ")");
        }

        private static void RotatedFrameStillPlacesCellsInWorldMetres()
        {
            foreach (float heading in new[] { 0f, 90f, 217f })
            {
                WeatherField field = Storm(33u, heading);
                var m = new MetOfficeModel();
                MetOfficeCells.Fill(m, field);
                for (int i = 0; i < m.CellCount; i++)
                    TestAssert.That(field.FindNearestCell(m.Cells[i].X * 1000f, m.Cells[i].Z * 1000f, out FieldFeature f, rings: 1)
                        && Math.Abs(f.X * 0.001f - m.Cells[i].X) < 0.01f, "heading " + heading + ": the chart cell stands on its world position");
            }
        }
    }
}
