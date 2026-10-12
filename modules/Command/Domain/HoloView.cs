using System;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The holo map's camera, as plain matrices (row-major, 16 floats, columns act on (east, up, north, 1)) so the mesh render and the marks drawn over it use
    /// the same numbers. The scene is metres from the player's aircraft: east, height above the sea, north. The camera looks at the sea-level point under the
    /// aircraft from <see cref="DistanceRadii"/> radii away, <see cref="TiltDegrees"/> above the horizon, through a narrow lens; heights are stretched
    /// <see cref="VerticalScale"/> times. Heading up turns the table by the aircraft's heading (north up keeps heading 0). The view is OpenGL-style (looks down -Z,
    /// +Y up), the way Unity's own view and projection matrices are, so the caller hands them to Unity as they are. No Unity types, so it is unit-tested.
    /// </summary>
    internal static class HoloView
    {
        internal const float TiltDegrees = 50f;
        internal const float FovDegrees = 24f;
        /// <summary>Camera distance from the table centre, in hologram radii: the whole disc fits with a margin.</summary>
        internal const float DistanceRadii = 5.5f;
        internal const float VerticalScale = 2.5f;
        /// <summary>A mark's height above the sea is drawn no taller than this many radii on the table (an aircraft far up would leave the picture).</summary>
        internal const float LiftCapRadii = .45f;
        internal const int Elements = 16;

        private const float Deg = MathF.PI / 180f;

        /// <summary>World-to-view matrix for a table turned by <paramref name="headingDegrees"/> (the aircraft's heading, 0 for north up).</summary>
        internal static void View(float headingDegrees, float radius, float[] m)
        {
            float s = MathF.Sin(headingDegrees * Deg), c = MathF.Cos(headingDegrees * Deg);
            float sinT = MathF.Sin(TiltDegrees * Deg), cosT = MathF.Cos(TiltDegrees * Deg);
            float d = DistanceRadii * radius;
            // across = e c - n s, ahead = e s + n c, table height = up * V; view y = height cos + ahead sin, view z = -(distance + ahead cos - height sin).
            m[0] = c; m[1] = 0f; m[2] = -s; m[3] = 0f;
            m[4] = sinT * s; m[5] = VerticalScale * cosT; m[6] = sinT * c; m[7] = 0f;
            m[8] = -cosT * s; m[9] = VerticalScale * sinT; m[10] = -cosT * c; m[11] = -d;
            m[12] = 0f; m[13] = 0f; m[14] = 0f; m[15] = 1f;
        }

        /// <summary>OpenGL perspective matrix for a square picture.</summary>
        internal static void Projection(float radius, float[] m)
        {
            float f = 1f / MathF.Tan(FovDegrees * .5f * Deg);
            float near = radius * .5f, far = radius * 12f;
            for (int i = 0; i < Elements; i++) m[i] = 0f;
            m[0] = f;
            m[5] = f;
            m[10] = -(far + near) / (far - near);
            m[11] = -2f * far * near / (far - near);
            m[14] = -1f;
        }

        /// <summary>The scene point on the picture: x and y run -1..1 (x right, y up) across the square; false for a point behind the camera.</summary>
        internal static bool Project(float headingDegrees, float radius, float east, float height, float north, out float x, out float y)
        {
            float s = MathF.Sin(headingDegrees * Deg), c = MathF.Cos(headingDegrees * Deg);
            float sinT = MathF.Sin(TiltDegrees * Deg), cosT = MathF.Cos(TiltDegrees * Deg);
            float across = east * c - north * s, ahead = east * s + north * c, table = height * VerticalScale;
            float depth = DistanceRadii * radius + ahead * cosT - table * sinT;
            float f = 1f / MathF.Tan(FovDegrees * .5f * Deg);
            x = y = 0f;
            if (depth < radius * .5f) return false;
            x = f * across / depth;
            y = f * (table * cosT + ahead * sinT) / depth;
            return true;
        }

        /// <summary>The scene offset (east, north) of a spot given on the table: <paramref name="across"/> metres right and <paramref name="ahead"/> metres forward of the aircraft as the picture is turned.</summary>
        internal static void FromTable(float headingDegrees, float across, float ahead, out float east, out float north)
        {
            float s = MathF.Sin(headingDegrees * Deg), c = MathF.Cos(headingDegrees * Deg);
            east = across * c + ahead * s;
            north = -across * s + ahead * c;
        }

        /// <summary>A mark's height above the sea as the table shows it: not below the sea, not past the lift cap (metres, before the vertical scale).</summary>
        internal static float MarkHeight(float heightAboveSea, float radius)
        {
            float cap = LiftCapRadii * radius / VerticalScale;
            return heightAboveSea < 0f ? 0f : heightAboveSea > cap ? cap : heightAboveSea;
        }

        /// <summary>Points of a circle of <paramref name="radius"/> metres round the aircraft, on the sea-level plane: east and north offsets, the first point due north, clockwise, the last equal to the first.</summary>
        internal static void Ring(float radius, int segments, float[] east, float[] north)
        {
            for (int i = 0; i <= segments; i++)
            {
                float a = i == segments ? 0f : i * (2f * MathF.PI / segments);
                east[i] = MathF.Sin(a) * radius;
                north[i] = MathF.Cos(a) * radius;
            }
        }
    }
}
