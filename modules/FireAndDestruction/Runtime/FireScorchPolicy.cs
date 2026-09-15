namespace BoscaliSummer.Fire
{
    /// <summary>
    /// Sizing maths for one burn site: how many trees the fire consumes, how large the
    /// vanilla blast-map ash bed is painted, and how large the persistent ground soot decal
    /// is. Unity-free so tests can compile it.
    /// </summary>
    internal static class FireScorchPolicy
    {
        /// <summary>
        /// Input radius for the tree-removing <c>BlastManager.AddBlast</c> call. Vanilla
        /// multiplies by 2 before <c>TreeRenderer.ClearTrees</c> applies its own 0.3 factor,
        /// so the actual cleared radius is about 0.6 of this value (~0.43 m at scale 1,
        /// 80% smaller than the earlier 3.6 m input). A fire consumes a few trees at its
        /// core, it does not clear a stand.
        /// </summary>
        internal const float TreeClearBlastBaseRadius = 0.72f;

        /// <summary>
        /// Radius of the soft ash bed drawn directly into the vanilla blast map. This
        /// bypasses <c>AddBlast</c>, so it never removes a tree no matter how large it is.
        /// The vanilla blast map resolves one texel per 160 m, so a nuke-grade stamp is
        /// needed for the burnt ground to actually read as gray soil; marks much below
        /// ~200 m vanish into a single soft texel.
        /// </summary>
        internal const float BurnMarkBaseRadius = 260f;

        /// <summary>Diameter of the visible soot decal stamped on the burnt ground.</summary>
        internal const float ScarBaseDiameter = 45f;

        /// <summary>
        /// Distance from a burn core to a lobe decal, as a fraction of the scar diameter.
        /// The lobes must overlap the core decal: offsets were once derived from the
        /// whole-site ash radius, so a burn site left three disconnected specks roughly
        /// 100 m apart that read as no soot at all from the air.
        /// </summary>
        internal const float ScarLobeDownwind = 0.52f;
        internal const float ScarLobeCrosswind = 0.40f;

        internal static float TreeClearBlastRadius(float clusterScale) =>
            TreeClearBlastBaseRadius * ClusterGrowth(clusterScale);

        internal static float BurnMarkRadius(float clusterScale) =>
            BurnMarkBaseRadius * ClusterGrowth(clusterScale);

        internal static float ScarDiameter(float clusterScale) =>
            ScarBaseDiameter * ClusterGrowth(clusterScale);

        /// <summary>
        /// A merged/spread fire front grows its burn footprint by at most 30% so a long
        /// campaign cannot turn a single front into a landscape-wide scar.
        /// </summary>
        private static float ClusterGrowth(float clusterScale) =>
            0.85f + 0.15f * Clamp(clusterScale, 1f, 3f);

        private static float Clamp(float value, float min, float max) =>
            value < min ? min : value > max ? max : value;
    }
}
