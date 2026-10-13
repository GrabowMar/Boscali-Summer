using System;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The holo map's camera. The scene is metres from the player's aircraft: east, height above the sea, north. The table is turned by <see cref="Heading"/> (the aircraft's heading, 0 for north up) and the camera looks
    /// at the sea-level point <see cref="Pan"/> metres ahead of the aircraft on the turned table, from <see cref="Distance"/> metres away and <see cref="Tilt"/> degrees above the horizon, through a narrow lens
    /// (<see cref="HoloView.FovDegrees"/>) into a frame <see cref="Aspect"/> wide over high. <see cref="HoloView.Fit"/> makes one that covers its frame with ground and stands the aircraft where it is wanted.
    /// </summary>
    internal struct HoloCamera
    {
        public float Heading, Tilt, Distance, Pan, Aspect;
    }

    /// <summary>
    /// The holo map's view, as plain matrices (row-major, 16 floats, columns act on (east, up, north, 1)) so the mesh render and the marks drawn over it use the same numbers. Heights are stretched
    /// <see cref="VerticalScale"/> times. The view is OpenGL-style (looks down -Z, +Y up), the way Unity's own view and projection matrices are, so the caller hands them to Unity as they are. The lens is a
    /// vertical field of view; the frame's aspect widens the horizontal one, so a wider frame sees more across and no more up and down. No Unity types, so it is unit-tested.
    /// </summary>
    internal static class HoloView
    {
        internal const float FovDegrees = 24f;
        internal const float VerticalScale = 2.5f;
        /// <summary>A mark's height above the sea is drawn no taller than this many ranges on the table (an aircraft far up would leave the picture).</summary>
        internal const float LiftCapRadii = .45f;
        /// <summary>The highest ground the picture is made to cover behind the frame's bottom edge: ground this high looks higher on the screen than the sea does, so the grid reaches back that much farther.</summary>
        internal const float MaxRelief = 2500f;
        internal const int Elements = 16;
        /// <summary>The near and far planes, as shares of the camera's distance.</summary>
        internal const float NearShare = .3f, FarShare = 2.5f;

        private const float Deg = MathF.PI / 180f;

        /// <summary>The lens's focal length in units of the frame's half-height (1 over the tangent of half the field of view).</summary>
        internal static float Focal => 1f / MathF.Tan(FovDegrees * .5f * Deg);

        /// <summary>
        /// A camera at <paramref name="tiltDegrees"/> above the horizon for a frame of <paramref name="aspect"/> (width over height) that shows <paramref name="halfWidth"/> metres either side of the aircraft at the
        /// aircraft's own depth and stands the aircraft in the middle across and <paramref name="aircraftUp"/> of the frame's height up from its bottom edge. Looking ahead of the aircraft by <see cref="HoloCamera.Pan"/>
        /// puts it below the middle of the frame; heading 0, the caller turns the table.
        /// </summary>
        internal static HoloCamera Fit(float tiltDegrees, float aspect, float aircraftUp, float halfWidth)
        {
            float f = Focal, sinT = MathF.Sin(tiltDegrees * Deg), cosT = MathF.Cos(tiltDegrees * Deg);
            // The aircraft's depth makes the frame halfWidth wide there; the pan puts the aircraft at aircraftUp (its y on the picture is -f * pan * sin / depth).
            float depth = f * halfWidth / aspect;
            float pan = (1f - 2f * aircraftUp) * halfWidth / (aspect * sinT);
            return new HoloCamera { Tilt = tiltDegrees, Aspect = aspect, Pan = pan, Distance = depth + pan * cosT };
        }

        /// <summary>World-to-view matrix for the camera.</summary>
        internal static void View(in HoloCamera camera, float[] m)
        {
            float s = MathF.Sin(camera.Heading * Deg), c = MathF.Cos(camera.Heading * Deg);
            float sinT = MathF.Sin(camera.Tilt * Deg), cosT = MathF.Cos(camera.Tilt * Deg);
            float d = camera.Distance, p = camera.Pan;
            // across = e c - n s, ahead = e s + n c - pan, table height = up * V; view y = height cos + ahead sin, view z = -(distance + ahead cos - height sin).
            m[0] = c; m[1] = 0f; m[2] = -s; m[3] = 0f;
            m[4] = sinT * s; m[5] = VerticalScale * cosT; m[6] = sinT * c; m[7] = -p * sinT;
            m[8] = -cosT * s; m[9] = VerticalScale * sinT; m[10] = -cosT * c; m[11] = -d + p * cosT;
            m[12] = 0f; m[13] = 0f; m[14] = 0f; m[15] = 1f;
        }

        /// <summary>OpenGL perspective matrix for the camera's frame.</summary>
        internal static void Projection(in HoloCamera camera, float[] m)
        {
            float f = Focal, near = camera.Distance * NearShare, far = camera.Distance * FarShare;
            for (int i = 0; i < Elements; i++) m[i] = 0f;
            m[0] = f / camera.Aspect;
            m[5] = f;
            m[10] = -(far + near) / (far - near);
            m[11] = -2f * far * near / (far - near);
            m[14] = -1f;
        }

        /// <summary>The scene point on the picture: x and y run -1..1 (x right, y up) across the frame; false for a point behind the camera.</summary>
        internal static bool Project(in HoloCamera camera, float east, float height, float north, out float x, out float y)
        {
            float s = MathF.Sin(camera.Heading * Deg), c = MathF.Cos(camera.Heading * Deg);
            float sinT = MathF.Sin(camera.Tilt * Deg), cosT = MathF.Cos(camera.Tilt * Deg);
            float across = east * c - north * s, ahead = east * s + north * c - camera.Pan, table = height * VerticalScale;
            float depth = camera.Distance + ahead * cosT - table * sinT;
            float f = Focal;
            x = y = 0f;
            if (depth < camera.Distance * NearShare) return false;
            x = f / camera.Aspect * across / depth;
            y = f * (table * cosT + ahead * sinT) / depth;
            return true;
        }

        /// <summary>
        /// The sea-level ground a point of the frame looks at, on the turned table: <paramref name="across"/> metres right and <paramref name="ahead"/> metres forward of the aircraft (<paramref name="x"/> and
        /// <paramref name="y"/> run -1..1 across the frame as in <see cref="Project"/>). False for a point at or above the horizon.
        /// </summary>
        internal static bool GroundAt(in HoloCamera camera, float x, float y, out float across, out float ahead)
        {
            float sinT = MathF.Sin(camera.Tilt * Deg), cosT = MathF.Cos(camera.Tilt * Deg), f = Focal;
            float den = f * sinT - y * cosT;
            across = ahead = 0f;
            if (den <= 1e-4f) return false;
            float behind = y * camera.Distance / den;
            float depth = camera.Distance + behind * cosT;
            across = x * camera.Aspect * depth / f;
            ahead = behind + camera.Pan;
            return true;
        }

        /// <summary>The metres across the whole frame at a row of it (<paramref name="y"/>, -1 the bottom edge, 1 the top): the picture's scale there, for the scale bar. 0 for a row that sees no ground.</summary>
        internal static float WidthAt(in HoloCamera camera, float y)
        {
            float sinT = MathF.Sin(camera.Tilt * Deg), cosT = MathF.Cos(camera.Tilt * Deg), f = Focal;
            float den = f * sinT - y * cosT;
            if (den <= 1e-4f) return 0f;
            float depth = camera.Distance + y * camera.Distance / den * cosT;
            return 2f * camera.Aspect * depth / f;
        }

        /// <summary>
        /// Where a ray from the point (<paramref name="x0"/>, <paramref name="y0"/>) inside a rectangle along (<paramref name="dx"/>, <paramref name="dy"/>) leaves it: the north tick stands there. A point that is
        /// not inside, or no direction, gives the point itself.
        /// </summary>
        internal static void EdgePoint(float x0, float y0, float dx, float dy, float left, float bottom, float right, float top, out float x, out float y)
        {
            float t = float.MaxValue;
            if (dx > 1e-6f) t = Math.Min(t, (right - x0) / dx);
            else if (dx < -1e-6f) t = Math.Min(t, (left - x0) / dx);
            if (dy > 1e-6f) t = Math.Min(t, (top - y0) / dy);
            else if (dy < -1e-6f) t = Math.Min(t, (bottom - y0) / dy);
            if (t == float.MaxValue || t < 0f) t = 0f;
            x = x0 + dx * t;
            y = y0 + dy * t;
        }

        /// <summary>A mark's height above the sea as the table shows it: not below the sea, not past the lift cap (metres, before the vertical scale).</summary>
        internal static float MarkHeight(float heightAboveSea, float range)
        {
            float cap = LiftCapRadii * range / VerticalScale;
            return heightAboveSea < 0f ? 0f : heightAboveSea > cap ? cap : heightAboveSea;
        }

        /// <summary>The widest and narrowest field of view the MINI's view wedge draws, in degrees (a lens outside this is a broken reading, not a view).</summary>
        internal const float WedgeMinDegrees = 8f, WedgeMaxDegrees = 160f;

        /// <summary>A camera's horizontal field of view in degrees from its vertical one and its aspect ratio (width over height); 0 for a reading that is not a lens.</summary>
        internal static float HorizontalFov(float verticalDegrees, float aspect)
        {
            if (!(verticalDegrees > 0f && verticalDegrees < 180f && aspect > 0f)) return 0f;
            return 2f * MathF.Atan(MathF.Tan(verticalDegrees * .5f * Deg) * aspect) / Deg;
        }

        /// <summary>
        /// The camera's view wedge on the sea-level plane, as the arc that closes it: <paramref name="segments"/> + 1 east and north offsets (metres from the aircraft) on a circle of
        /// <paramref name="length"/> metres, from the left edge of the view (the first point) to the right (the last), the middle one on the bearing the camera looks along (degrees from north, clockwise).
        /// The two lines of the wedge run from the aircraft to the first and the last point. The field of view is held within <see cref="WedgeMinDegrees"/>..<see cref="WedgeMaxDegrees"/>.
        /// </summary>
        internal static void Wedge(float bearingDegrees, float fovDegrees, float length, int segments, float[] east, float[] north)
        {
            float half = Math.Min(WedgeMaxDegrees, Math.Max(WedgeMinDegrees, fovDegrees)) * .5f;
            for (int i = 0; i <= segments; i++)
            {
                float a = (bearingDegrees - half + i * (2f * half / segments)) * Deg;
                east[i] = MathF.Sin(a) * length;
                north[i] = MathF.Cos(a) * length;
            }
        }
    }
}
