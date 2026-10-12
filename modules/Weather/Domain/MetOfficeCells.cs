using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// The chart's storm cells (slice 3): the active cells of the storm lattice around the theater centre, nearest first, cut to what the model holds, in world km.
    /// The pure half of <c>MetOfficeAdapter.Fill</c> (the adapter is its only caller and the window's only contact with the field).
    /// </summary>
    internal static class MetOfficeCells
    {
        private const float M = 1000f;
        // The chart is a 300 km square: its corner is 212 km from the centre, and a cell can reach a further 5.3 km in. Enumerate reads a 25 x 25 block (192 km guaranteed), so
        // the very corners of a chart may miss a sliver of a cell (ponytail: widen EnumerateHalf only if a corner cell ever matters).
        private const float Range = MetChartMath.RangeKm * M * 1.4143f + StormLattice.RadiusMax;
        [ThreadStatic] private static StormCell[] found;

        public static void Fill(MetOfficeModel m, WeatherField field)
        {
            m.CellCount = 0;
            if (field == null || !field.IsBuilt || !field.Lattice.IsValid) return;
            StormCell[] cells = found ?? (found = new StormCell[StormLattice.EnumerateCap]);
            field.Frame.ToTape(0f, 0f, out float qx, out float qz);
            int n = StormLattice.Enumerate(field.Lattice, qx, qz, Range, cells);
            for (int i = 0; i < n && m.CellCount < MetOfficeModel.MaxCells; i++)
            {
                field.Frame.FromTape(cells[i].X, cells[i].Z, out float x, out float z);
                float xKm = x / M, zKm = z / M, rKm = cells[i].Radius / M;
                // Off the chart is not on the chart: the cells counter and the nearest-first cap only see what is drawn.
                if (Math.Abs(xKm) - rKm > MetChartMath.RangeKm || Math.Abs(zKm) - rKm > MetChartMath.RangeKm) continue;
                m.Cells[m.CellCount++] = new MetCell { X = xKm, Z = zKm, Radius = rKm, Severe = cells[i].Severe };
            }
        }
    }
}
