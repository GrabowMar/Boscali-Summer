
namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>One plot rectangle in panel pixels. Empty marks a degenerate layout.</summary>
    internal readonly struct PlotRect
    {
        public float X { get; }
        public float Y { get; }
        public float Width { get; }
        public float Height { get; }

        public PlotRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public static PlotRect Empty => new PlotRect(0f, 0f, 0f, 0f);

        public bool IsEmpty => Width <= 0f || Height <= 0f;
    }

    /// <summary>One mini-map point in theater metres (east, north). No Unity types.</summary>
    internal readonly struct PlotPoint
    {
        public float X { get; }
        public float Z { get; }

        public PlotPoint(float x, float z)
        {
            X = x;
            Z = z;
        }
    }

    /// <summary>
    /// The Operations Room plot as pure layout arithmetic: a real theater mini-map
    /// (control tint, vector front, coverage raster, operation arrows) instead of the
    /// static schematic. The target resolves only from an explicit position carried
    /// with the operation — never by matching label strings — so a duplicate, hidden
    /// or completed objective reads unresolved and pins no marker.
    /// </summary>
    internal static class RoomPlotModel
    {
        /// <summary>Front-trace stations the plot draws; longer traces decimate.</summary>
        internal const int MaxPlotPoints = 256;

        /// <summary>Operation axis arrows the plot draws at once.</summary>
        internal const int MaxArrows = 4;

        internal const float Padding = 8f;
        internal const float LegendHeight = 24f;

        /// <summary>
        /// The plot's map and legend rectangles inside the given panel size. A
        /// collapsed panel yields empty rects instead of dividing by nothing.
        /// </summary>
        public static void Layout(float width, float height, out PlotRect map, out PlotRect legend)
        {
            if (float.IsNaN(width) || float.IsInfinity(width) || float.IsNaN(height) ||
                float.IsInfinity(height) || width <= Padding * 2f || height <= Padding * 2f)
            {
                map = PlotRect.Empty;
                legend = PlotRect.Empty;
                return;
            }
            if (height <= Padding * 2f + LegendHeight)
            {
                map = new PlotRect(Padding, Padding, width - Padding * 2f, height - Padding * 2f);
                legend = PlotRect.Empty;
                return;
            }
            map = new PlotRect(Padding, Padding, width - Padding * 2f,
                height - Padding * 2f - LegendHeight);
            legend = new PlotRect(Padding, height - Padding - LegendHeight,
                width - Padding * 2f, LegendHeight);
        }

        /// <summary>
        /// Copies a front trace into the plot buffer, stride-sampling when it exceeds
        /// the ceiling. Allocation-free: both buffers are caller-owned. Reports how
        /// many points were written; a null or empty source writes nothing.
        /// </summary>
        public static int Decimate(PlotPoint[] source, int count, PlotPoint[] destination)
        {
            if (source == null || destination == null || count <= 0) return 0;
            int available = count > source.Length ? source.Length : count;
            if (available <= 0) return 0;
            int capacity = destination.Length > MaxPlotPoints ? MaxPlotPoints : destination.Length;
            if (capacity <= 0) return 0;
            if (available <= capacity)
            {
                for (int i = 0; i < available; i++) destination[i] = source[i];
                return available;
            }
            int stride = (available + capacity - 1) / capacity;
            if (stride < 1) stride = 1;
            int written = 0;
            for (int i = 0; i < available && written < capacity; i += stride)
                destination[written++] = source[i];
            return written;
        }

        /// <summary>
        /// Whether the operation's target resolves to a marker. An explicit finite
        /// position is required; anything else is unresolved and pins nothing.
        /// </summary>
        public static bool ResolveTarget(bool hasPosition, float x, float z) =>
            hasPosition && !float.IsNaN(x) && !float.IsInfinity(x) &&
            !float.IsNaN(z) && !float.IsInfinity(z);

        /// <summary>Axis arrows the plot draws: the ask, inside the ceiling.</summary>
        public static int ArrowCount(int requested) =>
            requested < 0 ? 0 : requested > MaxArrows ? MaxArrows : requested;
    }
}
