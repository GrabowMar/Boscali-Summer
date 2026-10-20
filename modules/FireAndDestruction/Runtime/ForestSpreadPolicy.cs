namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Fuel rule for one fire-front crossing: may the fire in <see cref="FireFrontCell"/>
    /// enter the neighbour across one shared edge? Unity-free so tests can compile it:
    /// <c>ImpactFireManager</c> owns the ignition, this owns the question of whether
    /// continuous fuel connects the two cells.
    ///
    /// Each indexed position is a whole tree clump (the vanilla TreeClump mesh spans
    /// roughly 49 m), so clump centres in continuous forest sit 10–30 m apart and the
    /// exact edge midpoint usually has no centre within a single-tree radius. Sampling
    /// only that midpoint at 8 m rejected about 93% of crossings on the real Terrain1
    /// position data and fronts died in their first cell. The edge is therefore sampled
    /// along its length; a genuinely cleared edge (no fuel near any sample, e.g. the
    /// two lone clumps of the firebreak check, whose centres are ~15 m from the edge)
    /// still refuses the crossing.
    /// </summary>
    internal static class ForestSpreadPolicy
    {
        /// <summary>Distance from a shared-edge sample that still counts as fuel touching the edge.</summary>
        internal const float EdgeSampleRadius = 12f;

        /// <summary>Distance from the crossing midpoint (edge to destination centre) that still counts as continuous fuel.</summary>
        internal const float CrossingSampleRadius = 14f;

        internal static bool HasCrossingFuel(ForestIndex index, FireFrontCell cell, int edge)
        {
            FireFrontCell.Point a = cell.Vertices[edge];
            FireFrontCell.Point b = cell.Vertices[(edge + 1) % cell.Count];
            FireFrontCell.Point other = FireFrontCell.Seed(
                (int)(cell.Neighbours[edge] >> 32), (int)cell.Neighbours[edge]);
            float midX = cell.Center.X + (a.X + b.X) * 0.5f;
            float midZ = cell.Center.Z + (a.Z + b.Z) * 0.5f;

            // Fuel must touch the shared edge somewhere along its length, not only at
            // its midpoint: clump centres rarely sit exactly on the bisector.
            bool edgeFuel = false;
            for (int sample = 1; sample <= 3 && !edgeFuel; sample++)
            {
                float t = sample * 0.25f;
                edgeFuel = index.ContainsWithin(
                    cell.Center.X + a.X + (b.X - a.X) * t,
                    cell.Center.Z + a.Z + (b.Z - a.Z) * t,
                    EdgeSampleRadius);
            }
            if (!edgeFuel) return false;

            // Fuel must continue across the crossing and fill the destination cell,
            // so shorelines and cleared strips wider than the samples stop the front
            // instead of being jumped.
            if (!index.ContainsWithin((midX + other.X) * 0.5f, (midZ + other.Z) * 0.5f,
                    CrossingSampleRadius))
                return false;
            return index.Contains(other.X, other.Z);
        }
    }
}
