using System;
using System.Globalization;

namespace BoscaliSummer.Modules.Support.Domain
{
    /// <summary>A deterministic cell of the global theater grid, shared by cyber and field control.</summary>
    internal readonly struct OpsSector : IEquatable<OpsSector>
    {
        public readonly int Column, Row;
        public OpsSector(int column, int row) { Column = column; Row = row; }
        public float MinX => Column * OpsSectors.Size;
        public float MinZ => Row * OpsSectors.Size;
        public float CenterX => MinX + OpsSectors.Size * .5f;
        public float CenterZ => MinZ + OpsSectors.Size * .5f;
        public string Code => (Column < 0 ? "W" : "E") + Math.Abs(Column).ToString("00", CultureInfo.InvariantCulture) + "/" +
            (Row < 0 ? "S" : "N") + Math.Abs(Row).ToString("00", CultureInfo.InvariantCulture);
        public bool Equals(OpsSector other) => Column == other.Column && Row == other.Row;
        public override bool Equals(object value) => value is OpsSector other && Equals(other);
        public override int GetHashCode() => unchecked(Column * 397 ^ Row);
    }

    /// <summary>Half-open 10 km sectors: edges belong to the cell east/north of the edge.
    /// Invalid coordinates never become a controlled sector; outer cells are not clamped.</summary>
    internal static class OpsSectors
    {
        public const float Size = 10000f;
        private const double CoordinateLimit = 40960000.0;
        public static bool TryLocate(float x, float z, out OpsSector sector)
        {
            sector = default;
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(z) || float.IsInfinity(z) ||
                Math.Abs((double)x) >= CoordinateLimit || Math.Abs((double)z) >= CoordinateLimit) return false;
            sector = new OpsSector((int)Math.Floor((double)x / Size), (int)Math.Floor((double)z / Size));
            return true;
        }
        public static bool Same(float aX, float aZ, float bX, float bZ) =>
            TryLocate(aX, aZ, out OpsSector a) && TryLocate(bX, bZ, out OpsSector b) && a.Equals(b);
        public static string Code(float x, float z) => TryLocate(x, z, out OpsSector sector) ? sector.Code : "--";
    }
}
