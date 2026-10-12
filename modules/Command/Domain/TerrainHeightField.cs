using System;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The map's baked real elevation as a read-only sampler in the game's global frame: a square sheet of
    /// <c>side</c> x <c>side</c> samples centred on the global origin, one every <see cref="MetresPerSample"/> metres,
    /// heights in metres above the game datum. Water sits at <see cref="SeaLevel"/> and is the floor of every answer;
    /// a sample is land only where the mask says dry ground. Beyond the sheet there is only sea. The MFD relief hands
    /// out the arrays it loaded; the visor terrain ghost reads them. No Unity types, so it is unit-tested.
    /// </summary>
    internal sealed class TerrainHeightField
    {
        private readonly float[] heights;
        private readonly bool[] dry;
        private readonly float half;

        internal TerrainHeightField(float[] heights, bool[] dryLand, int side, float metresPerSample, float seaLevel)
        {
            if (heights == null || dryLand == null || side < 2 || heights.Length != side * side ||
                dryLand.Length != heights.Length || !(metresPerSample > 0f))
                throw new ArgumentException("A height field needs a square sheet of samples and a mask of the same size.");
            this.heights = heights;
            dry = dryLand;
            Side = side;
            MetresPerSample = metresPerSample;
            SeaLevel = seaLevel;
            half = (side - 1) * metresPerSample * .5f;
        }

        internal int Side { get; }
        internal float MetresPerSample { get; }
        /// <summary>Elevation of the water surface, and the lowest height <see cref="Sample"/> returns.</summary>
        internal float SeaLevel { get; }

        /// <summary>
        /// Bilinear elevation at a global position. A sea sample counts as <see cref="SeaLevel"/> (not its encoding
        /// floor), so a shore does not sag into the water; off the sheet it is <see cref="SeaLevel"/> too.
        /// </summary>
        internal float Sample(float globalX, float globalZ)
        {
            float sx = (globalX + half) / MetresPerSample, sz = (globalZ + half) / MetresPerSample;
            if (!(sx >= 0f && sx <= Side - 1 && sz >= 0f && sz <= Side - 1)) return SeaLevel;
            int x = Math.Min(Side - 2, (int)sx), z = Math.Min(Side - 2, (int)sz);
            float fx = sx - x, fz = sz - z;
            int at = z * Side + x;
            float a = Ground(at), b = Ground(at + 1), c = Ground(at + Side), d = Ground(at + Side + 1);
            float south = a + (b - a) * fx, north = c + (d - c) * fx;
            return south + (north - south) * fz;
        }

        /// <summary>True where the nearest sample is dry ground; false over water and off the sheet.</summary>
        internal bool IsLand(float globalX, float globalZ)
        {
            float sx = (globalX + half) / MetresPerSample, sz = (globalZ + half) / MetresPerSample;
            if (!(sx >= -.5f && sx < Side - .5f && sz >= -.5f && sz < Side - .5f)) return false;
            return dry[Math.Min(Side - 1, (int)(sz + .5f)) * Side + Math.Min(Side - 1, (int)(sx + .5f))];
        }

        private float Ground(int index) => dry[index] ? Math.Max(heights[index], SeaLevel) : SeaLevel;
    }
}
