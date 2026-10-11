namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>
    /// A legacy front (<see cref="FrontState"/>) as the chart's polyline in kilometres. This is the adapter's whole knowledge of how main stores a front: slice 4a deletes FrontState and
    /// replaces this one file, so the chart, the next-fronts card and the strip never learn the difference.
    /// </summary>
    internal static class MetFrontLine
    {
        /// <summary>The point at along-track distance a sits at n * offset(a) + t * a, where n is the front's normal and t its tangent (-nz, nx), so every point has SignedDistance zero.</summary>
        public static void From(FrontState f, MetFront into)
        {
            into.Kind = f.Kind == FrontKind.Warm ? MetFrontKind.Warm : f.Kind == FrontKind.Squall ? MetFrontKind.Squall : MetFrontKind.Cold;
            into.Strength = f.Strength;
            into.EtaMinutes = float.NaN;          // the legacy field never moves a front
            into.DirX = f.NormalX; into.DirZ = f.NormalZ;
            into.Count = MetFront.MaxPoints;
            float step = 2f * MetChartMath.RangeKm / (MetFront.MaxPoints - 1);
            for (int i = 0; i < into.Count; i++)
            {
                float along = -MetChartMath.RangeKm + i * step;
                float off = f.OffsetAtAlong(along * 1000f) * 0.001f;
                into.X[i] = f.NormalX * off - f.NormalZ * along;
                into.Z[i] = f.NormalZ * off + f.NormalX * along;
            }
        }
    }
}
