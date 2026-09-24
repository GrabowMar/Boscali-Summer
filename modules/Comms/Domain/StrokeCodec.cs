using System;
using System.Collections.Generic;

namespace BoscaliSummer.Features.Comms.Domain
{
    /// <summary>
    /// Map geometry on the wire: world metres east/north of the map centre, quantised to a
    /// small grid so a stroke costs a byte or two per point instead of eight.
    ///
    /// <para>Points are interleaved <c>x0, z0, x1, z1, …</c>. A drawn stroke is thinned on the
    /// sender (Ramer–Douglas–Peucker at a screen-pixel tolerance), capped, quantised, and then
    /// delta-coded by the transport, so a long doodle across the theater stays well under one
    /// datagram. The host re-validates everything it is handed; nothing here trusts a peer.</para>
    /// </summary>
    internal static class StrokeCodec
    {
        /// <summary>Metres per quantum. Well under one screen pixel at the tightest map zoom.</summary>
        public const float Quantum = 4f;

        /// <summary>
        /// Largest point count one stroke may carry. Delta-coded, a stroke this long still fits
        /// one reliable datagram; a thinned freehand line rarely needs a third of it.
        /// </summary>
        public const int MaxPoints = 192;

        /// <summary>
        /// Coordinates beyond this many metres from the map centre are rejected. The largest
        /// stock theater spans 164 km; this leaves room for any custom map without letting a
        /// forged packet push geometry to infinity.
        /// </summary>
        public const float WorldLimit = 400000f;

        private const int QuantumLimit = (int)(WorldLimit / Quantum);

        public static int Quantise(float metres)
        {
            if (float.IsNaN(metres)) return 0;
            float q = metres / Quantum;
            if (q > QuantumLimit) return QuantumLimit;
            if (q < -QuantumLimit) return -QuantumLimit;
            return (int)Math.Round(q);
        }

        public static float Restore(int quantised) => quantised * Quantum;

        /// <summary>Quantise interleaved world points, dropping consecutive duplicates.</summary>
        public static int[] Encode(float[] worldXz)
        {
            if (worldXz == null || worldXz.Length < 2) return Array.Empty<int>();
            int pairs = Math.Min(worldXz.Length / 2, MaxPoints);
            var buffer = new List<int>(pairs * 2);
            int lastX = int.MinValue, lastZ = int.MinValue;
            for (int i = 0; i < pairs; i++)
            {
                int x = Quantise(worldXz[i * 2]);
                int z = Quantise(worldXz[i * 2 + 1]);
                if (x == lastX && z == lastZ) continue;
                buffer.Add(x);
                buffer.Add(z);
                lastX = x;
                lastZ = z;
            }
            return buffer.ToArray();
        }

        /// <summary>A single quantised point: pings, stickers, labels and hunt marks.</summary>
        public static int[] Point(float x, float z) => new[] { Quantise(x), Quantise(z) };

        /// <summary>
        /// The shape check the host runs before it stores anything: an even count, inside the
        /// point budget, every coordinate inside the world limit.
        /// </summary>
        public static bool Valid(int[] points, int minPoints, int maxPoints)
        {
            if (points == null || (points.Length & 1) != 0) return false;
            int pairs = points.Length / 2;
            if (pairs < minPoints || pairs > maxPoints) return false;
            for (int i = 0; i < points.Length; i++)
                if (points[i] > QuantumLimit || points[i] < -QuantumLimit) return false;
            return true;
        }

        /// <summary>Absolute quantised points to first-absolute-then-delta form, for the wire.</summary>
        public static int[] ToDeltas(int[] absolute)
        {
            if (absolute == null || absolute.Length < 2) return absolute ?? Array.Empty<int>();
            var deltas = new int[absolute.Length];
            deltas[0] = absolute[0];
            deltas[1] = absolute[1];
            for (int i = 2; i + 1 < absolute.Length; i += 2)
            {
                deltas[i] = absolute[i] - absolute[i - 2];
                deltas[i + 1] = absolute[i + 1] - absolute[i - 1];
            }
            return deltas;
        }

        public static int[] FromDeltas(int[] deltas)
        {
            if (deltas == null || deltas.Length < 2) return deltas ?? Array.Empty<int>();
            var absolute = new int[deltas.Length];
            absolute[0] = deltas[0];
            absolute[1] = deltas[1];
            for (int i = 2; i + 1 < deltas.Length; i += 2)
            {
                absolute[i] = unchecked(absolute[i - 2] + deltas[i]);
                absolute[i + 1] = unchecked(absolute[i - 1] + deltas[i + 1]);
            }
            return absolute;
        }

        /// <summary>
        /// Thin an interleaved polyline to the points that carry its shape. Iterative, so a
        /// long stroke cannot recurse deep; the ends are always kept. When the result is still
        /// over <see cref="MaxPoints"/> the tolerance doubles until it fits.
        /// </summary>
        public static float[] Simplify(float[] xz, float tolerance)
        {
            if (xz == null) return Array.Empty<float>();
            int count = xz.Length / 2;
            if (count <= 2) return Copy(xz, count);

            float tol = Math.Max(0f, tolerance);
            for (int attempt = 0; attempt < 24; attempt++)
            {
                bool[] keep = Mark(xz, count, tol);
                int kept = 0;
                for (int i = 0; i < count; i++) if (keep[i]) kept++;
                if (kept <= MaxPoints || attempt == 23)
                {
                    var result = new float[Math.Min(kept, MaxPoints) * 2];
                    int w = 0;
                    for (int i = 0; i < count && w < result.Length; i++)
                    {
                        if (!keep[i]) continue;
                        result[w++] = xz[i * 2];
                        result[w++] = xz[i * 2 + 1];
                    }
                    return result;
                }
                tol = tol <= 0f ? Quantum : tol * 2f;
            }
            return Copy(xz, Math.Min(count, MaxPoints));
        }

        private static bool[] Mark(float[] xz, int count, float tolerance)
        {
            var keep = new bool[count];
            keep[0] = true;
            keep[count - 1] = true;
            var stack = new Stack<(int, int)>();
            stack.Push((0, count - 1));
            float tolSq = tolerance * tolerance;
            while (stack.Count > 0)
            {
                (int first, int last) = stack.Pop();
                if (last - first < 2) continue;
                float bestSq = -1f;
                int best = -1;
                for (int i = first + 1; i < last; i++)
                {
                    float d = SegmentDistanceSq(xz, i, first, last);
                    if (d > bestSq)
                    {
                        bestSq = d;
                        best = i;
                    }
                }
                if (best < 0 || bestSq <= tolSq) continue;
                keep[best] = true;
                stack.Push((first, best));
                stack.Push((best, last));
            }
            return keep;
        }

        private static float SegmentDistanceSq(float[] xz, int p, int a, int b)
        {
            float px = xz[p * 2], pz = xz[p * 2 + 1];
            float ax = xz[a * 2], az = xz[a * 2 + 1];
            float bx = xz[b * 2], bz = xz[b * 2 + 1];
            float dx = bx - ax, dz = bz - az;
            float lengthSq = dx * dx + dz * dz;
            float t = lengthSq > 1e-6f ? ((px - ax) * dx + (pz - az) * dz) / lengthSq : 0f;
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            float cx = ax + dx * t - px, cz = az + dz * t - pz;
            return cx * cx + cz * cz;
        }

        private static float[] Copy(float[] xz, int pairs)
        {
            var result = new float[pairs * 2];
            Array.Copy(xz, result, result.Length);
            return result;
        }
    }

    /// <summary>The drawing tools that are shapes, not free strokes.</summary>
    internal enum CommsShape : byte
    {
        Line = 0,
        Arrow = 1,
        Circle = 2,
        Box = 3,
    }

    /// <summary>
    /// Turns a two-point drag into the polyline(s) the board stores. Shapes travel as ordinary
    /// strokes, so the host, the wire and every peer only ever know one geometry type.
    /// </summary>
    internal static class CommsShapes
    {
        public const int CircleSegments = 40;

        /// <summary>One or two interleaved polylines for a drag from (ax, az) to (bx, bz).</summary>
        public static float[][] Build(CommsShape shape, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            switch (shape)
            {
                case CommsShape.Arrow:
                {
                    if (length < 1e-3f) return new[] { new[] { ax, az, bx, bz } };
                    float head = Math.Min(length * 0.3f, Math.Max(length * 0.12f, 600f));
                    float ux = dx / length, uz = dz / length;
                    // 28 degrees either side of the shaft, pointing back from the tip.
                    const float cos = 0.8829f, sin = 0.4695f;
                    float lx = -(ux * cos - uz * sin) * head, lz = -(uz * cos + ux * sin) * head;
                    float rx = -(ux * cos + uz * sin) * head, rz = -(uz * cos - ux * sin) * head;
                    return new[]
                    {
                        new[] { ax, az, bx, bz },
                        new[] { bx + lx, bz + lz, bx, bz, bx + rx, bz + rz },
                    };
                }
                case CommsShape.Circle:
                {
                    // Dragged from the centre out to the rim: the radius a SAM ring is read by.
                    var ring = new float[(CircleSegments + 1) * 2];
                    for (int i = 0; i <= CircleSegments; i++)
                    {
                        double angle = i * Math.PI * 2.0 / CircleSegments;
                        ring[i * 2] = ax + (float)Math.Cos(angle) * length;
                        ring[i * 2 + 1] = az + (float)Math.Sin(angle) * length;
                    }
                    return new[] { ring };
                }
                case CommsShape.Box:
                    return new[] { new[] { ax, az, bx, az, bx, bz, ax, bz, ax, az } };
                default:
                    return new[] { new[] { ax, az, bx, bz } };
            }
        }
    }
}
