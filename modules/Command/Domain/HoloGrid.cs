using System;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The holo map's wire relief: the same 64 x 64 cell line grid the terrain ghost uses (<see cref="TerrainGhostGrid"/> owns the topology, the vertex layout and
    /// the index buffer) with the cell size a parameter, so one mesh serves the 10, 20 and 40 km pictures. The grid is snapped to the cell and centred on the aircraft
    /// within half a cell; vertex X and Z are metres from the snapped centre, Y is the elevation in the game's global frame. The picture is additive light on black, so
    /// the colours are premultiplied: land in the HUD ink, brighter with height (the highest ground in view at the bright tone, never over a floor of <see cref="MinimumSpan"/>
    /// metres above the sea, so a plain does not shine like a peak), the sea a faint flat grid, and a circular edge fade that makes the picture a disc. No Unity types.
    /// </summary>
    internal static class HoloGrid
    {
        /// <summary>The land line at the sea's edge, as a share of the ink.</summary>
        internal const float LandFloor = .34f;
        /// <summary>The sea line, as a share of the ink.</summary>
        internal const float SeaStrength = .14f;
        /// <summary>The share of the radius where the edge fade starts.</summary>
        internal const float EdgeStart = .78f;
        /// <summary>Metres above the sea that count as the full height range at least.</summary>
        internal const float MinimumSpan = 300f;
        /// <summary>How far the bright tone is lifted from the ink toward white.</summary>
        internal const float BrightLift = .65f;
        private const float SeaTolerance = .01f;

        /// <summary>Metres between grid lines for a picture of <paramref name="rangeKm"/> kilometres from the centre to the edge.</summary>
        internal static float CellMetres(int rangeKm) => rangeKm * 2000f / TerrainGhostGrid.Cells;

        /// <summary>Metres from the centre to the edge for the same picture.</summary>
        internal static float Radius(int rangeKm) => rangeKm * 1000f;

        /// <summary>The cell whose centre is nearest a global position (the grid is refilled only when this changes).</summary>
        internal static void Snap(float globalX, float globalZ, float cellMetres, out int cellX, out int cellZ)
        {
            cellX = (int)MathF.Floor(globalX / cellMetres + .5f);
            cellZ = (int)MathF.Floor(globalZ / cellMetres + .5f);
        }

        /// <summary>The global position of a cell's centre along one axis.</summary>
        internal static float Anchor(int cell, float cellMetres) => cell * cellMetres;

        /// <summary>Places every vertex of the grid centred on a cell and reads its elevation (water at the field's sea level). Returns the highest elevation placed.</summary>
        internal static float FillHeights(TerrainHeightField field, float cellMetres, int cellX, int cellZ, GhostVertex[] vertices)
        {
            const int cells = TerrainGhostGrid.Cells, side = TerrainGhostGrid.Side, nodes = TerrainGhostGrid.Nodes;
            float anchorX = Anchor(cellX, cellMetres), anchorZ = Anchor(cellZ, cellMetres), highest = field.SeaLevel;
            for (int j = 0; j < side; j++)
            for (int i = 0; i < side; i++)
            {
                float x = (i - cells / 2) * cellMetres, z = (j - cells / 2) * cellMetres;
                float y = field.IsLand(anchorX + x, anchorZ + z) ? field.Sample(anchorX + x, anchorZ + z) : field.SeaLevel;
                if (y > highest) highest = y;
                ref GhostVertex along = ref vertices[j * side + i];
                ref GhostVertex across = ref vertices[nodes + i * side + j];
                along.X = across.X = x;
                along.Y = across.Y = y;
                along.Z = across.Z = z;
            }
            return highest;
        }

        /// <summary>
        /// Colours every vertex of a grid placed by <see cref="FillHeights"/>: premultiplied ink (alpha 255), so the additive blend needs no alpha. <paramref name="highest"/>
        /// is what FillHeights returned. Returns how many vertices carry light.
        /// </summary>
        internal static int FillColours(float cellMetres, float seaLevel, float highest, GhostTint ink, GhostVertex[] vertices)
        {
            float radius = TerrainGhostGrid.Cells * cellMetres * .5f, span = Math.Max(highest - seaLevel, MinimumSpan);
            float bright = BrightLift;
            float brightR = ink.R + (255f - ink.R) * bright, brightG = ink.G + (255f - ink.G) * bright, brightB = ink.B + (255f - ink.B) * bright;
            int lit = 0;
            for (int n = 0; n < vertices.Length; n++)
            {
                ref GhostVertex v = ref vertices[n];
                float distance = MathF.Sqrt(v.X * v.X + v.Z * v.Z) / radius;
                float fade = 1f - Smooth((distance - EdgeStart) / (1f - EdgeStart));
                float r, g, b;
                if (v.Y <= seaLevel + SeaTolerance)
                {
                    float level = SeaStrength * fade;
                    r = ink.R * level;
                    g = ink.G * level;
                    b = ink.B * level;
                }
                else
                {
                    float t = Math.Min(Math.Max((v.Y - seaLevel) / span, 0f), 1f);
                    float level = (LandFloor + (1f - LandFloor) * t) * fade;
                    r = (ink.R + (brightR - ink.R) * t) * level;
                    g = (ink.G + (brightG - ink.G) * t) * level;
                    b = (ink.B + (brightB - ink.B) * t) * level;
                }
                v.R = (byte)(Math.Min(r, 255f) + .5f);
                v.G = (byte)(Math.Min(g, 255f) + .5f);
                v.B = (byte)(Math.Min(b, 255f) + .5f);
                v.A = 255;
                if (v.R + v.G + v.B > 0) lit++;
            }
            return lit;
        }

        private static float Smooth(float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - 2f * t);
        }
    }
}
