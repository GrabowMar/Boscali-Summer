using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The holo map's terrain tile: a square-celled lattice of nodes in ground axes, <c>2 * halfX + 1</c> by <c>2 * halfZ + 1</c>, snapped to the cell and centred on the aircraft within half a cell (vertex X and Z are metres from
    /// the snapped centre, Y the elevation in the game's global frame, one vertex per node). The cell size follows the picture's range (<see cref="CellMetres"/>: a fixed number of cells across twice the range), and the
    /// lattice is as big as the frame needs (<see cref="Extent"/>: the ground the frame sees at any heading, plus the rise of the hills before the frame's near edge), so the same lattice serves every heading and a
    /// turn is the view matrix, never a rebuild. One vertex buffer feeds the index sets (<see cref="BuildIndices"/> for the surface, <see cref="BuildLines"/> for the lines after each fill): the land's lines running east-west and north-south, the sparse faint net over open water and the filled surface the lines hide
    /// behind. The picture is additive light on black, so the colours are premultiplied: land in the HUD ink, brighter with height (the highest ground in view at the bright tone, never over a floor of
    /// <see cref="MinimumSpan"/> metres above the sea, so a plain does not shine like a peak), the sea a faint flat grid. The tile has no edge of its own: the frame clips it and the render fades the last
    /// <see cref="FadeShare"/> of it (<see cref="EdgeFade"/>). No Unity types.
    /// </summary>
    internal static class HoloGrid
    {
        /// <summary>The land line at the sea's edge, as a share of the ink.</summary>
        internal const float LandFloor = .34f;
        /// <summary>
        /// What a sea node carries, as a share of the ink: the land floor, so a land line that ends in a sea node at the coast is as strong there as inland. The lines over water are not this: they are the
        /// <see cref="WaterEvery"/>th lines only, drawn at <see cref="WaterStrength"/> of it (<see cref="BuildLines"/>).
        /// </summary>
        internal const float SeaStrength = LandFloor;
        /// <summary>Over water only every this many lines of each family are drawn, counted by absolute cell index so the kept ones stay where they are as the aircraft moves.</summary>
        internal const int WaterEvery = 8;
        /// <summary>A water line's strength as a share of a land line's, both families alike (no emphasis for the way the camera looks over open water).</summary>
        internal const float WaterStrength = .35f;
        /// <summary>Metres above the sea that count as the full height range at least.</summary>
        internal const float MinimumSpan = 300f;
        /// <summary>How far the bright tone is lifted from the ink toward white.</summary>
        internal const float BrightLift = .65f;
        /// <summary>A line running straight away from the viewer is drawn at this share of one running across the view; in between it is by the angle.</summary>
        internal const float AwayStrength = .45f;
        /// <summary>How far the lines are lifted above the surface they are drawn on, in cells, so they do not fight it for the depth buffer.</summary>
        internal const float LineLift = .1f;
        /// <summary>The MINI's lines are drawn this much stronger than the BIG map's (the material's strength, not the height shading): the small picture has to hold its own over bright snow and cloud.</summary>
        internal const float MiniLineGain = 1.15f;
        /// <summary>The share of the frame at each edge over which the picture fades to nothing.</summary>
        internal const float FadeShare = .05f;
        /// <summary>The most cells from the centre to a side of the lattice (a lattice has at most (2 * this + 1) squared nodes: they fit 16-bit indices).</summary>
        internal const int MaxHalf = 96;
        internal const int MaxSide = 2 * MaxHalf + 1;
        internal const int MaxNodes = MaxSide * MaxSide;
        /// <summary>The longest index sets: the lines of one direction and the triangles.</summary>
        internal const int MaxLineIndices = MaxSide * (MaxSide - 1) * 2;
        internal const int MaxTriangleIndices = (MaxSide - 1) * (MaxSide - 1) * 6;
        /// <summary>The most vertices the tile's mesh holds: the lattice's nodes and, after them, the ones added where the shore cuts a segment (16-bit indices).</summary>
        internal const int MaxVertices = 65535;
        /// <summary>The shoreline's strength, as a share of the ink: well over the land floor, so the coast is the strongest line of the low ground.</summary>
        internal const float CoastStrength = .85f;
        /// <summary>The land is filled with this share of its lines' light (the sea stays black), so an island reads as a body and not only as a net.</summary>
        internal const float SurfaceFill = .22f;
        /// <summary>The relief's light: a slope facing the north-west is up to <see cref="ShadeCeiling"/> times as bright as level ground, one facing away down to <see cref="ShadeFloor"/> of it.</summary>
        internal const float ShadeFloor = .5f, ShadeCeiling = 1.5f;
        /// <summary>How many times the shore's place on a segment is halved: 4 is a sixteenth of a cell.</summary>
        private const int CoastSteps = 4;
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
            // Floor + 2, not ceiling + 1: a reach of a whole number of cells (the flat MINI's side) still leaves room for the half-cell snap.
            Math.Min(MaxHalf, Math.Max(2, (int)MathF.Floor(Math.Min(metres, 1e9f) / cellMetres) + 2));

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

        /// <summary>A node on the sea (at the sea's own level).</summary>
        internal static bool IsSea(float height, float seaLevel) => height <= seaLevel + SeaTolerance;

        /// <summary>Whether the line with this absolute cell index (east-west lines count rows, north-south ones columns) is one of the few drawn over water.</summary>
        internal static bool WaterLineKept(int absoluteLine) => ((absoluteLine % WaterEvery) + WaterEvery) % WaterEvery == 0;

        /// <summary>
        /// Sorts the lattice's line segments after its heights are filled, and draws the coast. A segment between two land nodes is a LAND line; one between two sea nodes is a WATER line, of which only every
        /// <see cref="WaterEvery"/>th line of each family is kept (<see cref="WaterLineKept"/>, by absolute cell index: <paramref name="cellX"/> / <paramref name="cellZ"/> are the lattice centre's); one between a land
        /// node and a sea node is cut where the shore crosses it (found on <paramref name="field"/>'s land mask by halving, to a sixteenth of a cell; the middle without a field): a vertex is added there, on the surface, and the land
        /// line ends in it, so the grid stops at the shore and not at the next node. The COAST joins those vertices cell by cell into the shoreline. Land segments are index pairs into their own arrays, both families
        /// of water lines go into <paramref name="water"/>, the shoreline into <paramref name="coast"/>. The added vertices follow the lattice's nodes in <paramref name="vertices"/> (<paramref name="vertexCount"/> is
        /// nodes plus those; a shore longer than the array has room for is left uncut). <paramref name="crossings"/> is scratch, <see cref="MaxLineIndices"/> long.
        /// </summary>
        internal static void BuildLines(TerrainHeightField field, float cellMetres, int halfX, int halfZ, int cellX, int cellZ, float seaLevel, GhostVertex[] vertices, int[] crossings,
            int[] landEastWest, int[] landNorthSouth, int[] water, int[] coast,
            out int landEastWestCount, out int landNorthSouthCount, out int waterCount, out int coastCount, out int vertexCount)
        {
            int sx = 2 * halfX + 1, sz = 2 * halfZ + 1, cx = sx - 1, cz = sz - 1, rows = cx * sz;
            float anchorX = Anchor(cellX, cellMetres), anchorZ = Anchor(cellZ, cellMetres);
            int land = 0, other = 0, wet = 0, next = sx * sz;
            for (int j = 0; j < sz; j++)
            {
                bool kept = WaterLineKept(cellZ + j - halfZ);
                for (int i = 0; i < cx; i++)
                {
                    int a = j * sx + i;
                    crossings[j * cx + i] = Segment(field, anchorX, anchorZ, seaLevel, vertices, a, a + 1, kept, landEastWest, ref land, water, ref wet, ref next);
                }
            }
            for (int j = 0; j < cz; j++)
            for (int i = 0; i < sx; i++)
            {
                int a = j * sx + i;
                crossings[rows + a] = Segment(field, anchorX, anchorZ, seaLevel, vertices, a, a + sx, WaterLineKept(cellX + i - halfX), landNorthSouth, ref other, water, ref wet, ref next);
            }
            // The shoreline: in each cell the shore enters and leaves through its cut sides (two of them, or all four on a saddle: then two corners are cut off).
            int shore = 0;
            for (int j = 0; j < cz; j++)
            for (int i = 0; i < cx; i++)
            {
                int south = crossings[j * cx + i], north = crossings[(j + 1) * cx + i], west = crossings[rows + j * sx + i], east = crossings[rows + j * sx + i + 1];
                int p = -1, q = -1, cuts = 0;
                if (south >= 0) { p = south; cuts++; }
                if (west >= 0) { if (p < 0) p = west; else q = west; cuts++; }
                if (north >= 0) { if (p < 0) p = north; else if (q < 0) q = north; cuts++; }
                if (east >= 0) { if (q < 0) q = east; cuts++; }
                if (cuts < 2 || shore + 4 > coast.Length) continue;
                coast[shore++] = p;
                coast[shore++] = q;
                if (cuts < 4) continue;
                coast[shore++] = north;
                coast[shore++] = east;
            }
            landEastWestCount = land;
            landNorthSouthCount = other;
            waterCount = wet;
            coastCount = shore;
            vertexCount = next;
        }

        /// <summary>One segment of the lattice into the list it belongs to; returns the vertex added where the shore cuts it, or -1 when it does not (or there was no room for one).</summary>
        private static int Segment(TerrainHeightField field, float anchorX, float anchorZ, float seaLevel, GhostVertex[] vertices, int a, int b, bool kept,
            int[] land, ref int landCount, int[] water, ref int waterCount, ref int next)
        {
            bool seaA = IsSea(vertices[a].Y, seaLevel), seaB = IsSea(vertices[b].Y, seaLevel);
            if (seaA == seaB)
            {
                if (!seaA)
                {
                    land[landCount++] = a;
                    land[landCount++] = b;
                }
                else if (kept)
                {
                    water[waterCount++] = a;
                    water[waterCount++] = b;
                }
                return -1;
            }
            if (next >= vertices.Length || next >= MaxVertices) return -1;
            int dryNode = seaA ? b : a;
            GhostVertex dry = vertices[dryNode], sea = vertices[seaA ? a : b];
            // The share of the way from the dry node to the sea one where the land ends.
            float low = 0f, high = 1f;
            if (field != null)
                for (int step = 0; step < CoastSteps; step++)
                {
                    float mid = (low + high) * .5f;
                    if (field.IsLand(anchorX + dry.X + (sea.X - dry.X) * mid, anchorZ + dry.Z + (sea.Z - dry.Z) * mid)) low = mid;
                    else high = mid;
                }
            float t = (low + high) * .5f;
            ref GhostVertex v = ref vertices[next];
            v.X = dry.X + (sea.X - dry.X) * t;
            v.Y = dry.Y + (sea.Y - dry.Y) * t;
            v.Z = dry.Z + (sea.Z - dry.Z) * t;
            land[landCount++] = dryNode;
            land[landCount++] = next;
            return next++;
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
                if (IsSea(v.Y, seaLevel))
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
        /// The last touch after <see cref="FillColours"/>, over all <paramref name="vertexCount"/> vertices <see cref="BuildLines"/> left: the relief's light on every land node (its colour times how much its slope, heights
        /// stretched as the picture stretches them, faces a sun in the north-west 45 degrees up; level ground is unchanged, within <see cref="ShadeFloor"/>..<see cref="ShadeCeiling"/>), alpha 0 on every sea node (the
        /// surface is drawn with its alpha, so the sea stays black while its faint lines keep their colour) and the shoreline's vertices in the ink at <see cref="CoastStrength"/>.
        /// </summary>
        internal static void Shade(int halfX, int halfZ, float cellMetres, float seaLevel, GhostTint ink, int vertexCount, GhostVertex[] vertices)
        {
            int sx = 2 * halfX + 1, sz = 2 * halfZ + 1, nodes = sx * sz;
            const float level = .70710678f;
            float slope = HoloView.VerticalScale / cellMetres;
            for (int j = 0; j < sz; j++)
            for (int i = 0; i < sx; i++)
            {
                int at = j * sx + i;
                ref GhostVertex v = ref vertices[at];
                if (IsSea(v.Y, seaLevel))
                {
                    v.A = 0;
                    continue;
                }
                int west = i > 0 ? 1 : 0, east = i < sx - 1 ? 1 : 0, south = j > 0 ? 1 : 0, north = j < sz - 1 ? 1 : 0;
                float gx = (vertices[at + east].Y - vertices[at - west].Y) * slope / Math.Max(1, east + west);
                float gz = (vertices[at + north * sx].Y - vertices[at - south * sx].Y) * slope / Math.Max(1, north + south);
                float light = (.5f * gx + level - .5f * gz) / (level * MathF.Sqrt(gx * gx + 1f + gz * gz));
                light = light < ShadeFloor ? ShadeFloor : light > ShadeCeiling ? ShadeCeiling : light;
                v.R = (byte)(Math.Min(v.R * light, 255f) + .5f);
                v.G = (byte)(Math.Min(v.G * light, 255f) + .5f);
                v.B = (byte)(Math.Min(v.B * light, 255f) + .5f);
                v.A = 255;
            }
            byte r = (byte)(ink.R * CoastStrength + .5f), g = (byte)(ink.G * CoastStrength + .5f), b = (byte)(ink.B * CoastStrength + .5f);
            for (int n = nodes; n < vertexCount; n++)
            {
                ref GhostVertex v = ref vertices[n];
                v.R = r;
                v.G = g;
                v.B = b;
                v.A = 255;
            }
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
