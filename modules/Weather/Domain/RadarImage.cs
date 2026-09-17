namespace BoscaliSummer.Features.Weather.Domain
{
    /// <summary>
    /// The synthetic reflectivity picture the WEA radar paints: a bounded grid of 0..1 values
    /// sampled from the deterministic storm population and the one front. The presentation layer
    /// turns these numbers into a texture and a colour ramp; nothing Unity-shaped appears here on
    /// purpose, because the domain folder is fenced by an architecture test and the picture must
    /// be checkable without a renderer.
    ///
    /// <para>Every value is a pure function of (cells, front, window, resolution), so the host,
    /// a client and a late joiner derive the same image from the same field. No radar picture —
    /// and no echo in one — ever goes on the wire; the field it samples already does not.</para>
    ///
    /// <para>Column 0 is the west edge of the window and row 0 the north edge, so the grid reads
    /// the way a chart does. Each sample sits at the centre of its cell.</para>
    /// </summary>
    internal sealed class RadarImage
    {
        /// <summary>Longest side of the grid. A whole-map picture is legible well below this.</summary>
        public const int MaxResolution = 192;

        /// <summary>Shortest side worth allocating; a smaller request is refused, not clamped up.</summary>
        public const int MinResolution = 8;

        /// <summary>
        /// Below this a sample is clear air. The map shows through it and the picture draws
        /// nothing, which is also where the ramp's lightest band starts.
        /// </summary>
        public const float NoEcho = 0.08f;

        /// <summary>Peak frontal echo: a lifting boundary paints as hard as a strong cell.</summary>
        private const float ConvectiveBand = 0.85f;

        /// <summary>Layered frontal rain — a warm front glides, it does not build towers.</summary>
        private const float LayeredBand = 0.50f;

        /// <summary>
        /// What a cell contributes at the top of its own influence. A tower reads hotter than
        /// fair-weather cumulus of the same intensity, and a taller tower of the same kind reads
        /// hotter still, so the picture separates a squall line's cores from its stragglers.
        /// </summary>
        private const float WeightFloor = 0.55f;
        private const float WeightDepth = 0.45f;
        private const float WeightSupercell = 1f;
        private const float WeightTowering = 0.78f;
        private const float WeightCumulus = 0.55f;

        public int Width { get; private set; }

        public int Height { get; private set; }

        /// <summary>The sampled grid, row-major from the north-west corner. Length is Width*Height.</summary>
        public float[] Values { get; private set; }

        /// <summary>
        /// Size the grid. The buffer is replaced only when the requested area changes, so a
        /// repaint that keeps its window allocation-free. A degenerate request is refused and
        /// leaves the previous grid alone.
        /// </summary>
        public bool Resize(int width, int height)
        {
            if (width < MinResolution || height < MinResolution) return false;
            if (width > MaxResolution) width = MaxResolution;
            if (height > MaxResolution) height = MaxResolution;
            if (Values != null && width == Width && height == Height) return true;

            Width = width;
            Height = height;
            Values = new float[width * height];
            return true;
        }

        /// <summary>
        /// Sample the window centred on the map origin: <paramref name="halfX"/> metres either
        /// side of it and <paramref name="halfZ"/> metres north and south of it. A degenerate
        /// window clears the grid rather than leaving the last picture behind.
        /// </summary>
        public void Sample(StormCell[] cells, int count, in WeatherFront front, float halfX, float halfZ)
        {
            if (Values == null || Width <= 0 || Height <= 0) return;
            if (!IsUsable(halfX) || !IsUsable(halfZ))
            {
                Clear();
                return;
            }

            int usable = UsableCount(cells, count);
            float stepX = 2f * halfX / Width;
            float stepZ = 2f * halfZ / Height;

            for (int row = 0; row < Height; row++)
            {
                float z = halfZ - (row + 0.5f) * stepZ;
                int line = row * Width;
                for (int column = 0; column < Width; column++)
                {
                    float x = -halfX + (column + 0.5f) * stepX;
                    Values[line + column] = ReflectivityAt(cells, usable, in front, x, z);
                }
            }
        }

        /// <summary>
        /// Reflectivity at one point: the harder of the dominant cell's weighted influence and
        /// the frontal rain band, clamped to 0..1. The dominant cell is the one
        /// <see cref="StormField.StrongestAt"/> already picks for rain and haze, so the picture
        /// and the weather can never disagree about where the storm is.
        /// </summary>
        public static float ReflectivityAt(StormCell[] cells, int count, in WeatherFront front, float x, float z)
        {
            float value = FrontBand(in front, x, z);

            int usable = UsableCount(cells, count);
            if (usable > 0)
            {
                StormCell dominant = StormField.StrongestAt(cells, usable, x, z, out float influence);
                if (influence > 0f)
                {
                    float cell = influence * CellWeight(in dominant);
                    if (cell > value) value = cell;
                }
            }

            return WeatherRegimes.Clamp01(value);
        }

        /// <summary>
        /// How hard a cell paints at the top of its falloff. Kind sets the ceiling and the tower
        /// above the cloud base scales within it, so a low cumulus cannot read as a supercell.
        /// </summary>
        private static float CellWeight(in StormCell cell)
        {
            if (cell.Radius <= 0f || cell.Intensity <= 0f) return 0f;

            float kind = cell.Kind == StormKind.Supercell ? WeightSupercell
                : cell.Kind == StormKind.ToweringCumulus ? WeightTowering
                : WeightCumulus;
            float depth = WeatherRegimes.Clamp01((cell.TopHeight - cell.CloudBase) / StormField.SupercellTopMax);
            return kind * (WeightFloor + WeightDepth * depth);
        }

        /// <summary>
        /// The boundary's own rain, independent of any cell: a band centred on the front line and
        /// fading out across its width. This is what draws a squall line as a line rather than as
        /// a row of dots, and what a warm front's layered rain reads as.
        /// </summary>
        private static float FrontBand(in WeatherFront front, float x, float z)
        {
            float activity = front.InfluenceAt(x, z);
            if (activity <= 0f) return 0f;
            return activity * (FrontKinds.IsConvective(front.Kind) ? ConvectiveBand : LayeredBand);
        }

        private static bool IsUsable(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value) && value > 0f;

        private static int UsableCount(StormCell[] cells, int count)
        {
            if (cells == null || count <= 0) return 0;
            return count < cells.Length ? count : cells.Length;
        }

        private void Clear()
        {
            for (int i = 0; i < Values.Length; i++) Values[i] = 0f;
        }
    }
}
