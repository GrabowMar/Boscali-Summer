using System;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The holo map's terrain tile: a square-celled lattice of nodes in ground axes, <c>2 * halfX + 1</c> by <c>2 * halfZ + 1</c>, snapped to the cell and centred on the aircraft within half a cell (vertex X and Z are metres from
    /// the snapped centre, Y the elevation in the game's global frame, one vertex per node). The cell size follows the picture's range (<see cref="CellMetres"/>: a fixed number of cells across twice the range), and the
    /// lattice is as big as the frame needs (<see cref="Extent"/>: the ground the frame sees at any heading, plus the rise of the hills before the frame's near edge), so the same lattice serves every heading and a
    /// turn is the view matrix, never a rebuild. One vertex buffer feeds three index sets (<see cref="BuildIndices"/>): the lines running east-west, the lines running north-south and the filled surface the lines hide
    /// behind. The picture is additive light on black, so the colours are premultiplied: land in the HUD ink, brighter with height (the highest ground in view at the bright tone, never over a floor of
    /// <see cref="MinimumSpan"/> metres above the sea, so a plain does not shine like a peak), the sea a faint flat grid. The tile has no edge of its own: the frame clips it and the render fades the last
    /// <see cref="FadeShare"/> of it (<see cref="EdgeFade"/>). No Unity types.
    /// </summary>
    internal static class HoloGrid
    {
        /// <summary>The land line at the sea's edge, as a share of the ink.</summary>
        internal const float LandFloor = .34f;
        /// <summary>The sea line, as a share of the ink.</summary>
        internal const float SeaStrength = .14f;
        /// <summary>Metres above the sea that count as the full height range at least.</summary>
        internal const float MinimumSpan = 300f;
        /// <summary>How far the bright tone is lifted from the ink toward white.</summary>
        internal const float BrightLift = .65f;
        /// <summary>A line running straight away from the viewer is drawn at this share of one running across the view; in between it is by the angle.</summary>
        internal const float AwayStrength = .45f;
        /// <summary>How far the lines are lifted above the surface they are drawn on, in cells, so they do not fight it for the depth buffer.</summary>
        internal const float LineLift = .1f;
        /// <summary>The share of the frame at each edge over which the picture fades to nothing.</summary>
        internal const float FadeShare = .05f;
        /// <summary>The most cells from the centre to a side of the lattice (a lattice has at most (2 * this + 1) squared nodes: they fit 16-bit indices).</summary>
        internal const int MaxHalf = 96;
        internal const int MaxSide = 2 * MaxHalf + 1;
        internal const int MaxNodes = MaxSide * MaxSide;
        /// <summary>The longest index sets: the lines of one direction and the triangles.</summary>
        internal const int MaxLineIndices = MaxSide * (MaxSide - 1) * 2;
        internal const int MaxTriangleIndices = (MaxSide - 1) * (MaxSide - 1) * 6;
        private const float SeaTolerance = .01f;
        private const float Deg = MathF.PI / 180f;

        /// <summary>
        /// What the depth buffer is cleared to, the far plane: 1 for an ordinary buffer, 0 for a reversed one (Direct3D 11: Unity's <c>SystemInfo.usesReversedZBuffer</c>, where the GPU projection matrix gives
        /// nearer points the greater depth and a nearer point passes a <c>GreaterEqual</c> test). The render sets its depth tests and this clear by the platform, since a test set through a material property is not turned around for it.
        /// </summary>
        internal static float DepthClear(bool reversedZ) => reversedZ ? 0f : 1f;

        /// <summary>Metres between nodes for a picture of <paramref name="rangeKm"/> kilometres from the aircraft to the frame's side, with <paramref name="cells"/> cells across twice that.</summary>
        internal static float CellMetres(int rangeKm, int cells) => rangeKm * 2000f / cells;

        /// <summary>Metres from the aircraft to the frame's side, at the aircraft's depth: the range.</summary>
        internal static float Radius(int rangeKm) => rangeKm * 1000f;

        /// <summary>The cell whose centre is nearest a global position (the lattice is refilled only when this changes).</summary>
        internal static void Snap(float globalX, float globalZ, float cellMetres, out int cellX, out int cellZ)
        {
            cellX = (int)MathF.Floor(globalX / cellMetres + .5f);
            cellZ = (int)MathF.Floor(globalZ / cellMetres + .5f);
        }

        /// <summary>The global position of a cell's centre along one axis.</summary>
        internal static float Anchor(int cell, float cellMetres) => cell * cellMetres;

        /// <summary>
        /// How far the lattice reaches from its centre so the frame is covered with ground: <paramref name="halfX"/> and <paramref name="halfZ"/> cells east-west and north-south. The frame's four corners look at ground
        /// (<see cref="HoloView.GroundAt"/>); the bottom ones are pushed back by the rise of <see cref="HoloView.MaxRelief"/> high ground, and one cell is added all round for the snap. North up the lattice is the
        /// rectangle that holds those corners (heading 0); heading up the frame turns with the aircraft, so it is the square that holds the circle through them. <paramref name="reach"/> is that circle's radius (plus a cell) either
        /// way: the ground farther from the aircraft than that is never seen. Give it the camera of the smallest frame the picture is drawn with (<see cref="HoloPresentation.ReferenceAspect"/>), which sees the most ground up and down.
        /// </summary>
        internal static void Extent(in HoloCamera camera, float cellMetres, bool northUp, out int halfX, out int halfZ, out float reach)
        {
            float across = 0f, behind = 0f, ahead = 0f, circle = 0f;
            float rise = HoloView.VerticalScale * HoloView.MaxRelief / MathF.Tan(camera.Tilt * Deg);
            for (int corner = 0; corner < 4; corner++)
            {
                float x = (corner & 1) == 0 ? -1f : 1f, y = (corner & 2) == 0 ? -1f : 1f;
                if (!HoloView.GroundAt(camera, x, y, out float a, out float b))
                {
                    a = x * 1e6f;
                    b = y * 1e6f;
                }
                if (y < 0f) b -= rise;
                across = Math.Max(across, Math.Abs(a));
                behind = Math.Max(behind, -b);
                ahead = Math.Max(ahead, b);
                circle = Math.Max(circle, MathF.Sqrt(a * a + b * b));
            }
            reach = circle + cellMetres;
            if (northUp)
            {
                halfX = Cells(across, cellMetres);
                halfZ = Cells(Math.Max(behind, ahead), cellMetres);
            }
            else halfX = halfZ = Cells(circle, cellMetres);
        }

        private static int Cells(float metres, float cellMetres) =>
            Math.Min(MaxHalf, Math.Max(2, (int)MathF.Ceiling(Math.Min(metres, 1e9f) / cellMetres) + 1));

        /// <summary>Nodes in a lattice of this reach.</summary>
        internal static int Nodes(int halfX, int halfZ) => (2 * halfX + 1) * (2 * halfZ + 1);

        /// <summary>
        /// Fills the three index sets of a lattice (node (i, j) is vertex j * (2 * halfX + 1) + i, i east, j north): the line list of the east-west lines, the line list of the north-south lines and the triangle list of the
        /// surface, each cell two triangles. The arrays are the caller's, long enough for <see cref="MaxLineIndices"/> and <see cref="MaxTriangleIndices"/>; the counts say how much of each is used.
        /// </summary>
        internal static void BuildIndices(int halfX, int halfZ, int[] eastWest, int[] northSouth, int[] triangles, out int eastWestCount, out int northSouthCount, out int triangleCount)
        {
            int sx = 2 * halfX + 1, sz = 2 * halfZ + 1, at = 0;
            for (int j = 0; j < sz; j++)
            for (int i = 0; i + 1 < sx; i++)
            {
                eastWest[at++] = j * sx + i;
                eastWest[at++] = j * sx + i + 1;
            }
            eastWestCount = at;
            at = 0;
            for (int i = 0; i < sx; i++)
            for (int j = 0; j + 1 < sz; j++)
            {
                northSouth[at++] = j * sx + i;
                northSouth[at++] = (j + 1) * sx + i;
            }
            northSouthCount = at;
            at = 0;
            for (int j = 0; j + 1 < sz; j++)
            for (int i = 0; i + 1 < sx; i++)
            {
                int a = j * sx + i, b = a + 1, c = a + sx, d = c + 1;
                triangles[at++] = a;
                triangles[at++] = c;
                triangles[at++] = b;
                triangles[at++] = b;
                triangles[at++] = c;
                triangles[at++] = d;
            }
            triangleCount = at;
        }

        /// <summary>
        /// Places every node of the lattice centred on a cell and reads its elevation (water at the field's sea level). Returns the highest elevation among the nodes within <paramref name="reach"/> metres of the centre
        /// (the ground the frame can see).
        /// </summary>
        internal static float FillHeights(TerrainHeightField field, float cellMetres, int cellX, int cellZ, int halfX, int halfZ, float reach, GhostVertex[] vertices)
        {
            float anchorX = Anchor(cellX, cellMetres), anchorZ = Anchor(cellZ, cellMetres), highest = field.SeaLevel, limit = reach * reach;
            int sx = 2 * halfX + 1, sz = 2 * halfZ + 1, at = 0;
            for (int j = 0; j < sz; j++)
            {
                float z = (j - halfZ) * cellMetres;
                for (int i = 0; i < sx; i++)
                {
                    float x = (i - halfX) * cellMetres;
                    float y = field.IsLand(anchorX + x, anchorZ + z) ? field.Sample(anchorX + x, anchorZ + z) : field.SeaLevel;
                    if (y > highest && x * x + z * z <= limit) highest = y;
                    ref GhostVertex v = ref vertices[at++];
                    v.X = x;
                    v.Y = y;
                    v.Z = z;
                }
            }
            return highest;
        }

        /// <summary>
        /// Colours the first <paramref name="count"/> vertices of a lattice placed by <see cref="FillHeights"/>: premultiplied ink (alpha 255), so the additive blend needs no alpha. <paramref name="highest"/> is what FillHeights
        /// returned. Returns how many vertices carry light.
        /// </summary>
        internal static int FillColours(float seaLevel, float highest, GhostTint ink, int count, GhostVertex[] vertices)
        {
            float span = Math.Max(highest - seaLevel, MinimumSpan);
            float brightR = ink.R + (255f - ink.R) * BrightLift, brightG = ink.G + (255f - ink.G) * BrightLift, brightB = ink.B + (255f - ink.B) * BrightLift;
            int lit = 0;
            for (int n = 0; n < count; n++)
            {
                ref GhostVertex v = ref vertices[n];
                float r, g, b;
                if (v.Y <= seaLevel + SeaTolerance)
                {
                    r = ink.R * SeaStrength;
                    g = ink.G * SeaStrength;
                    b = ink.B * SeaStrength;
                }
                else
                {
                    float t = Math.Min(Math.Max((v.Y - seaLevel) / span, 0f), 1f);
                    float level = LandFloor + (1f - LandFloor) * t;
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

        /// <summary>
        /// How strongly a grid line is drawn, as a share of full: lines running across the view (perpendicular to the way the camera looks) at full strength, lines running away from the viewer at
        /// <see cref="AwayStrength"/>, by the sine of the angle between them in between, so ridges read as stacked profiles. Both are bearings on the ground in degrees (0 north, 90 east); a line has no sense, so 180 degrees apart is the same.
        /// </summary>
        internal static float LineStrength(float lineBearingDegrees, float lookBearingDegrees)
        {
            float across = MathF.Abs(MathF.Sin((lineBearingDegrees - lookBearingDegrees) * Deg));
            return AwayStrength + (1f - AwayStrength) * across;
        }

        /// <summary>
        /// The picture's strength at a place across the frame, as a share of full: <paramref name="share"/> is how far in from the nearest edge it is as a share of the frame's width (or height); nothing at the edge, rising
        /// smoothly to full at <see cref="FadeShare"/> in and staying there.
        /// </summary>
        internal static float EdgeFade(float share)
        {
            float t = share / FadeShare;
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return t * t * (3f - 2f * t);
        }
    }
}
