using System;
using System.Runtime.InteropServices;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>One vertex of the terrain ghost's line mesh, laid out as the mesh's vertex buffer: position, then colour (16 bytes).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GhostVertex
    {
        public float X, Y, Z;
        public byte R, G, B, A;
    }

    /// <summary>A line colour without Unity: the pilot's HUD theme reaches the grid as three bytes.</summary>
    internal readonly struct GhostTint
    {
        public readonly byte R, G, B;
        public GhostTint(byte r, byte g, byte b) { R = r; G = g; B = b; }
    }

    /// <summary>
    /// The visor terrain ghost's wire relief: a square line grid of <see cref="Cells"/> x <see cref="Cells"/> cells of
    /// <see cref="CellMetres"/>, centred on the aircraft and snapped to the cell so the lines stay on the same ground
    /// as it flies. The caller owns two arrays: the <see cref="GhostVertex"/> buffer (<see cref="VertexCount"/>) and the
    /// index buffer from <see cref="BuildIndices"/>, built once. Every grid line is its own strip of vertices (the lines along
    /// X first, then the lines along Z), so a line over the sea can be thinned without touching the line that crosses it.
    /// Vertex X and Z are metres from the snapped centre, Y is the elevation in the game's global frame: a draw only
    /// needs the centre's local position as its translation.
    /// <para>Line strength (alpha) at strength 1 is <see cref="BaseAlpha"/>, times: an edge fade (full to 60 % of
    /// <see cref="Radius"/>, smoothly to zero at it); a near fade (zero at the aircraft, full from <see cref="NearMetres"/>
    /// away, measured in 3D); over the sea only every <see cref="SeaLineEvery"/>th line, at <see cref="SeaStrength"/>; and
    /// <see cref="CautionBoost"/> where the ground is higher than <see cref="CautionClearance"/> below the aircraft, which also
    /// takes the caution colour instead of the ink. Caution ground is never dimmed or thinned (it is the warning). The other (ink) ground gets a horizon fade
    /// (<see cref="HorizonFactor"/>): ground seen at 8 degrees or more below the aircraft's horizontal is at full strength, falling linearly (in slope) to
    /// <see cref="HorizonFloor"/> at 1 degree and less; and thins out with distance (<see cref="LineThinning"/>): beyond 4.5 km every second grid line
    /// is gone, beyond 7.5 km only every fourth remains, by the line's number on the ground, so the far lines do not converge into bright bundles.</para>
    /// </summary>
    internal static class TerrainGhostGrid
    {
        internal const int Cells = 64;
        internal const float CellMetres = 320f;
        /// <summary>Metres from the centre to the middle of a side: where the edge fade has reached zero.</summary>
        internal const float Radius = Cells * CellMetres * .5f;
        /// <summary>Nodes along one line.</summary>
        internal const int Side = Cells + 1;
        internal const int Nodes = Side * Side;
        internal const int VertexCount = 2 * Nodes;
        internal const int IndexCount = 4 * Cells * Side;
        internal const float BaseAlpha = .16f;
        internal const float FadeStart = .6f;
        internal const float NearMetres = 300f;
        internal const float CautionClearance = 150f;
        internal const float CautionBoost = 2f;
        internal const float SeaStrength = .3f;
        /// <summary>The depression angle (degrees below the aircraft's horizontal) from which a vertex is at full strength, the one at and under which it is at <see cref="HorizonFloor"/>, and that share.</summary>
        internal const float HorizonFullDegrees = 8f, HorizonFloorDegrees = 1f, HorizonFloor = .25f;
        /// <summary>Metres from the aircraft between which the odd lines fade out (half at the middle), and between which the lines that are not a multiple of four fade out.</summary>
        internal const float ThinHalfFrom = 4000f, ThinHalfTo = 5000f, ThinQuarterFrom = 7000f, ThinQuarterTo = 8000f;
        internal const int SeaLineEvery = 4;
        private const float SeaTolerance = .01f;

        /// <summary>The line list: one pair of indices per segment, never joining two lines.</summary>
        internal static int[] BuildIndices()
        {
            var indices = new int[IndexCount];
            int at = 0;
            for (int family = 0; family < 2; family++)
            for (int line = 0; line < Side; line++)
            for (int k = 0; k < Cells; k++)
            {
                int first = family * Nodes + line * Side + k;
                indices[at++] = first;
                indices[at++] = first + 1;
            }
            return indices;
        }

        /// <summary>
        /// The horizon fade for ground <paramref name="below"/> metres under the aircraft (negative: above it) and <paramref name="flatMetres"/> away along the ground: 1 from <see cref="HorizonFullDegrees"/> of depression,
        /// falling linearly to <see cref="HorizonFloor"/> at <see cref="HorizonFloorDegrees"/> and under (straight below is 90 degrees, level or higher is 0 or less).
        /// </summary>
        internal static float HorizonFactor(float below, float flatMetres)
        {
            // By slope, not angle: under 8 degrees the tangent is the angle to a quarter of a percent, and this runs for every node of the grid.
            if (!(below > HorizonFloorSlope * flatMetres)) return HorizonFloor;
            if (below >= HorizonFullSlope * flatMetres) return 1f;
            float t = (below / flatMetres - HorizonFloorSlope) / (HorizonFullSlope - HorizonFloorSlope);
            return HorizonFloor + (1f - HorizonFloor) * t;
        }

        /// <summary>tan of <see cref="HorizonFloorDegrees"/> and of <see cref="HorizonFullDegrees"/>.</summary>
        private const float HorizonFloorSlope = .0174551f, HorizonFullSlope = .1405408f;

        /// <summary>
        /// The share of a line kept <paramref name="metres"/> from the aircraft, for the line numbered <paramref name="number"/> on the ground (its absolute cell index, so the same lines stay as the aircraft moves): lines whose
        /// number is a multiple of four are always kept; those with an odd number fade out smoothly between <see cref="ThinHalfFrom"/> and <see cref="ThinHalfTo"/>; the others (2 mod 4) between
        /// <see cref="ThinQuarterFrom"/> and <see cref="ThinQuarterTo"/>.
        /// </summary>
        internal static float LineThinning(int number, float metres)
        {
            int mod = ((number % 4) + 4) % 4;
            if (mod == 0) return 1f;
            return mod == 2 ? 1f - Smooth((metres - ThinQuarterFrom) / (ThinQuarterTo - ThinQuarterFrom)) : 1f - Smooth((metres - ThinHalfFrom) / (ThinHalfTo - ThinHalfFrom));
        }

        /// <summary>The cell whose centre is nearest a global position: the grid is rebuilt only when this changes.</summary>
        internal static void Snap(float globalX, float globalZ, out int cellX, out int cellZ)
        {
            cellX = (int)MathF.Floor(globalX / CellMetres + .5f);
            cellZ = (int)MathF.Floor(globalZ / CellMetres + .5f);
        }

        /// <summary>The global position of a cell's centre along one axis.</summary>
        internal static float Anchor(int cell) => cell * CellMetres;

        /// <summary>Places every vertex of the grid centred on a cell and reads its elevation (water at the field's sea level).</summary>
        internal static void FillHeights(TerrainHeightField field, int cellX, int cellZ, GhostVertex[] vertices)
        {
            float anchorX = Anchor(cellX), anchorZ = Anchor(cellZ);
            for (int j = 0; j < Side; j++)
            for (int i = 0; i < Side; i++)
            {
                float x = (i - Cells / 2) * CellMetres, z = (j - Cells / 2) * CellMetres;
                float y = field.IsLand(anchorX + x, anchorZ + z) ? field.Sample(anchorX + x, anchorZ + z) : field.SeaLevel;
                ref GhostVertex along = ref vertices[j * Side + i];
                ref GhostVertex across = ref vertices[Nodes + i * Side + j];
                along.X = across.X = x;
                along.Y = across.Y = y;
                along.Z = across.Z = z;
            }
        }

        /// <summary>
        /// Colours every vertex for an aircraft at a global position and altitude, at an overall strength 0..1, over vertices
        /// placed by <see cref="FillHeights"/> for the same cell. Returns how many vertices are drawn in the caution colour.
        /// </summary>
        internal static int FillColours(int cellX, int cellZ, float planeX, float planeY, float planeZ, float strength,
            GhostTint ink, GhostTint caution, float seaLevel, GhostVertex[] vertices)
        {
            float eyeX = planeX - Anchor(cellX), eyeZ = planeZ - Anchor(cellZ);
            float level = Math.Min(Math.Max(strength, 0f), 1f) * BaseAlpha;
            float glowAbove = planeY - CautionClearance, radiusSquared = Radius * Radius, seaTop = seaLevel + SeaTolerance;
            int glowing = 0;
            // A node is two vertices, one on the line along X and one on the line along Z: the fades that depend only on where the node is are
            // worked out once for both, and a node that neither line draws (open sea between the kept lines, or beyond the radius) costs no maths.
            for (int j = 0; j < Side; j++)
            {
                // The line's number on the ground, so the lines kept over the sea stay put as the grid snaps along.
                int numberZ = cellZ + j - Cells / 2;
                float seaZ = SeaShare(numberZ);
                for (int i = 0; i < Side; i++)
                {
                    ref GhostVertex along = ref vertices[j * Side + i];
                    ref GhostVertex across = ref vertices[Nodes + i * Side + j];
                    int numberX = cellX + i - Cells / 2;
                    float dx = along.X - eyeX, dz = along.Z - eyeZ, flat = dx * dx + dz * dz, alphaZ = 0f, alphaX = 0f;
                    bool glow = along.Y > glowAbove;
                    if (flat < radiusSquared)
                    {
                        bool sea = along.Y <= seaTop;
                        float keepZ = sea ? seaZ : 1f, keepX = sea ? SeaShare(numberX) : 1f;
                        if (keepZ > 0f || keepX > 0f)
                        {
                            float dy = along.Y - planeY, away = MathF.Sqrt(flat);
                            float edge = 1f - Smooth((away / Radius - FadeStart) / (1f - FadeStart));
                            float near = Smooth(MathF.Sqrt(flat + dy * dy) / NearMetres);
                            float horizon = glow ? 0f : HorizonFactor(-dy, away);
                            alphaZ = level * edge * near * keepZ * (glow ? CautionBoost : horizon * LineThinning(numberZ, away));
                            alphaX = level * edge * near * keepX * (glow ? CautionBoost : horizon * LineThinning(numberX, away));
                        }
                    }
                    GhostTint tint = glow ? caution : ink;
                    along.R = across.R = tint.R;
                    along.G = across.G = tint.G;
                    along.B = across.B = tint.B;
                    along.A = (byte)(Math.Min(alphaZ, 1f) * 255f + .5f);
                    across.A = (byte)(Math.Min(alphaX, 1f) * 255f + .5f);
                    if (glow) glowing += (along.A > 0 ? 1 : 0) + (across.A > 0 ? 1 : 0);
                }
            }
            return glowing;
        }

        /// <summary>Over the sea only every <see cref="SeaLineEvery"/>th line is drawn, at <see cref="SeaStrength"/>.</summary>
        private static float SeaShare(int number) => ((number % SeaLineEvery) + SeaLineEvery) % SeaLineEvery == 0 ? SeaStrength : 0f;

        private static float Smooth(float t) => Scalar.SmoothStep(0f, 1f, t);
    }
}
