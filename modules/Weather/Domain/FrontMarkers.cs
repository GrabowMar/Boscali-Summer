using System;

namespace BoscaliSummer.Modules.Weather.Domain
{
    /// <summary>The four front markers of spec 5.3. The values are the order of the table there, not of the old FrontKind.</summary>
    internal enum MarkerKind : byte { Warm = 0, Cold = 1, Squall = 2, Occluded = 3 }

    /// <summary>What a marker adds at one signed distance, per unit strength: a thin overlay on the keyframe blend, never cover.</summary>
    internal struct LineEffect
    {
        /// <summary>mm/h of the rain core, before the cover gate (the field multiplies it by Gate(RainCover)).</summary>
        public float Rain;
        /// <summary>Gust in m/s, veer in degrees and the QNH dip in hPa. They feed WeatherPoint only, never level.windVelocity.</summary>
        public float Gust, VeerDegrees, QnhDip;
    }

    /// <summary>One marker of a tape: where its line stands (the metres along the tape of the boundary's ramp midpoint) and how hard it hits.</summary>
    internal readonly struct TapeMarker
    {
        public TapeMarker(MarkerKind kind, float s, float strength, int boundary)
        { Kind = kind; S = s; Strength = strength; Boundary = boundary; }

        public readonly MarkerKind Kind;
        public readonly float S, Strength;
        /// <summary>Index of the keyframe whose start boundary carries this marker.</summary>
        public readonly int Boundary;
    }

    /// <summary>
    /// <c>Line(kind, d)</c> of spec 5.3, the marker table and the beading shape. <c>d</c> is the signed distance in metres from the
    /// boundary's ramp midpoint, measured across the line: negative before the line arrives, positive after.
    /// The old one-sided exponential rain tails are gone: they were true steps.
    /// </summary>
    internal static class FrontLine
    {
        /// <summary>Beyond this many metres from a line no effect is evaluated. exp(-(60/12)^2) of the widest veer is below 1e-10.</summary>
        public const float Reach = 60000f;

        // The marker table (spec 5.3), one entry per MarkerKind in enum order: Warm, Cold, Squall, Occluded.
        private static readonly float[] RainTable = { 4f, 18f, 30f, 6f }, WidthTable = { 3000f, 2400f, 3900f, 3000f },
            GustTable = { 2f, 8f, 14f, 4f }, VeerTable = { 20f, 40f, 50f, 25f }, BiasTable = { 0f, 0.6f, 0.9f, 0.4f };

        /// <summary>Rain core: peak mm/h and sigma in metres (spec 5.3).</summary>
        public static float PeakRain(MarkerKind kind) => RainTable[(int)kind];

        public static float Sigma(MarkerKind kind) => WidthTable[(int)kind];

        public static float PeakGust(MarkerKind kind) => GustTable[(int)kind];

        public static float PeakVeer(MarkerKind kind) => VeerTable[(int)kind];

        public const float QnhDipHpa = 3f;

        /// <summary>The "Convective bias" column: how much the line raises the activation of the storm cells that already stand near it.</summary>
        public static float ConvectiveBias(MarkerKind kind) => BiasTable[(int)kind];

        /// <summary>The narrowest segment that may sit next to a marker: its core reaches 3 sigma (12 km for a warm or occluded line), and the wider
        /// ramps of a cold or squall boundary average up to 17 km with a neighbour (spec 5.3).</summary>
        public static float MinSegmentMetres(MarkerKind kind) => kind == MarkerKind.Cold || kind == MarkerKind.Squall ? 20000f : 12000f;

        /// <summary>The effect of one line of unit strength at signed distance <paramref name="d"/> metres. Cores are symmetric Gaussians;
        /// gust and veer are wider Gaussians (2 and 3 sigma), so a line is felt a few kilometres either side and no wind remains 20 km away.</summary>
        public static LineEffect Line(MarkerKind kind, float d)
        {
            var e = new LineEffect();
            float sigma = Sigma(kind);
            if (!(Math.Abs(d) < Reach)) return e;
            e.Rain = PeakRain(kind) * WeatherMath.Gauss(d, sigma);
            e.Gust = PeakGust(kind) * WeatherMath.Gauss(d, 2f * sigma);
            e.VeerDegrees = PeakVeer(kind) * WeatherMath.Gauss(d, 3f * sigma);
            e.QnhDip = QnhDipHpa * WeatherMath.Gauss(d, 15000f);
            return e;
        }

        /// <summary>Beading (spec 5.3): shape(d) = smoothstep(-10, 0, d) * (1 - smoothstep(15, 30, d)), d in kilometres. 0 by -10 km, 1 on [0, 15 km], 0 by +30 km.</summary>
        public static float BeadShape(float dKm) =>
            WeatherMath.Smoothstep(-10f, 0f, dKm) * (1f - WeatherMath.Smoothstep(15f, 30f, dKm));

        /// <summary>bias(d) = convectiveBias * shape(d), d in metres; added to a storm cell's Convective at its centre, never to a sample point.</summary>
        public static float Bias(MarkerKind kind, float dMetres) => ConvectiveBias(kind) * BeadShape(dMetres * 0.001f);
    }

    /// <summary>The markers of one tape, in increasing s, and the geometry that turns a tape position into a signed distance.</summary>
    internal sealed class MarkerSet
    {
        public static readonly MarkerSet Empty = new MarkerSet(Array.Empty<TapeMarker>());

        public MarkerSet(TapeMarker[] markers) { Items = markers ?? Array.Empty<TapeMarker>(); }

        public TapeMarker[] Items { get; }
        public int Count => Items.Length;

        /// <summary>d = (s - s_k) / sqrt(1 + M'(a)^2) for marker <paramref name="index"/> at the tape position <paramref name="s"/> and across-coordinate slope
        /// <paramref name="slope"/> (the meander derivative M'(a), at most 1 in magnitude).</summary>
        public float Distance(int index, float s, float slope) => (s - Items[index].S) / (float)Math.Sqrt(1f + slope * slope);

        /// <summary><see cref="BiasAt(float, float)"/> for the world point (x, z) at tape position <paramref name="s"/>, reading the meander slope from the frame only once a marker
        /// is near enough to use it (most cells stand far from every line, and the slope costs two cosines).</summary>
        public float BiasAt(float s, in TapeFrame frame, float x, float z)
        {
            float bias = 0f, slope = 0f;
            bool haveSlope = false;
            for (int i = 0; i < Items.Length; i++)
            {
                if (Math.Abs(s - Items[i].S) > 80000f) continue;
                if (!haveSlope) { slope = frame.MeanderSlope(frame.Across(x, z)); haveSlope = true; }
                bias += FrontLine.Bias(Items[i].Kind, Distance(i, s, slope)) * Items[i].Strength;
            }
            return bias;
        }
    }
}
