using System;
using BoscaliSummer.Core.Math;

namespace BoscaliSummer.Modules.Command.Domain
{
    /// <summary>
    /// The relief map's orthographic orbit camera, in relief model units, with exact solvers
    /// that keep a ground point under the cursor through pan, zoom and orbit.
    ///
    /// <para>The camera sits on a fixed orbit around a focus on the y = 0 plane and looks at
    /// it: yaw turns about +y (0 = north up), pitch is the elevation angle. Viewport
    /// coordinates run 0..1 from the bottom-left, like Unity's. Because the projection is
    /// orthographic, moving the focus by a ground vector moves every unprojected point by
    /// exactly that vector, so each solver is one unproject and one shift.</para>
    /// </summary>
    internal sealed class ReliefRig
    {
        internal const float MinZoom = 1f;
        internal const float MaxZoom = 40f;
        internal const float MinPitch = 20f;
        internal const float MaxPitch = 85f;
        internal const float DefaultPitch = 40f;
        private const float FitMargin = .92f;
        private const float MinimumSize = 10f;

        internal float FocusX;
        internal float FocusZ;
        internal float Yaw;
        internal float Pitch = DefaultPitch;
        internal float Zoom = MinZoom;

        /// <summary>Sheet half extents and highest terrain point, in model units.</summary>
        internal float HalfX = 1f, HalfZ = 1f, Top;
        /// <summary>Viewport width over height.</summary>
        internal float Aspect = 1f;

        /// <summary>Orthographic half height that fits the whole sheet at zoom 1.</summary>
        internal float FitSize
        {
            get
            {
                Basis(out float rx, out float rz, out float ux, out float uy, out float uz,
                    out _, out _, out _);
                float width = 0f, height = 0f;
                for (int h = 0; h <= 1; h++)
                for (int z = -1; z <= 1; z += 2)
                for (int x = -1; x <= 1; x += 2)
                {
                    float px = x * HalfX, py = h * Top, pz = z * HalfZ;
                    width = Math.Max(width, Math.Abs(px * rx + pz * rz));
                    height = Math.Max(height, Math.Abs(px * ux + py * uy + pz * uz));
                }
                return Math.Max(MinimumSize, Math.Max(width / Math.Max(.01f, Aspect), height) / FitMargin);
            }
        }

        /// <summary>Orthographic half height at the current zoom.</summary>
        internal float Size => FitSize / Zoom;

        /// <summary>Highest zoom that still shows at least the minimum view.</summary>
        internal float ZoomLimit => Math.Max(MinZoom, Math.Min(MaxZoom, FitSize / MinimumSize));

        internal void Basis(out float rx, out float rz, out float ux, out float uy, out float uz,
            out float fx, out float fy, out float fz)
        {
            double yaw = Yaw * Math.PI / 180d, pitch = Pitch * Math.PI / 180d;
            float sy = (float)Math.Sin(yaw), cy = (float)Math.Cos(yaw);
            float sp = (float)Math.Sin(pitch), cp = (float)Math.Cos(pitch);
            rx = cy; rz = -sy;
            ux = sp * sy; uy = cp; uz = sp * cy;
            fx = cp * sy; fy = -sp; fz = cp * cy;
        }

        internal void Project(float px, float py, float pz, out float vx, out float vy)
        {
            Basis(out float rx, out float rz, out float ux, out float uy, out float uz,
                out _, out _, out _);
            float size = Size;
            float dx = px - FocusX, dz = pz - FocusZ;
            vx = .5f + (dx * rx + dz * rz) / (2f * size * Aspect);
            vy = .5f + (dx * ux + py * uy + dz * uz) / (2f * size);
        }

        /// <summary>The point on the plane y = <paramref name="height"/> under a viewport point.</summary>
        internal void Unproject(float vx, float vy, float height, out float px, out float pz)
        {
            Basis(out float rx, out float rz, out float ux, out float uy, out float uz,
                out float fx, out float fy, out float fz);
            float size = Size;
            float sx = (vx - .5f) * 2f * size * Aspect, sy = (vy - .5f) * 2f * size;
            float ox = FocusX + rx * sx + ux * sy, oy = uy * sy, oz = FocusZ + rz * sx + uz * sy;
            float t = (height - oy) / fy;
            px = ox + fx * t;
            pz = oz + fz * t;
        }

        /// <summary>Shift the focus so the ground point (x, h, z) sits under viewport (vx, vy).</summary>
        internal void Grab(float x, float height, float z, float vx, float vy)
        {
            Unproject(vx, vy, height, out float cx, out float cz);
            FocusX += x - cx;
            FocusZ += z - cz;
            ClampFocus();
        }

        /// <summary>Zoom while the ground under viewport (vx, vy) at plane height stays put.</summary>
        internal void ZoomAt(float zoom, float vx, float vy, float height)
        {
            Unproject(vx, vy, height, out float ax, out float az);
            Zoom = Scalar.Clamp(zoom, MinZoom, ZoomLimit);
            Grab(ax, height, az, vx, vy);
        }

        /// <summary>Turn and tilt while the pivot keeps its spot on screen.</summary>
        internal void OrbitAbout(float yawDelta, float pitchDelta, float x, float height, float z)
        {
            Project(x, height, z, out float vx, out float vy);
            Yaw = WrapAngle(Yaw + yawDelta);
            Pitch = Scalar.Clamp(Pitch + pitchDelta, MinPitch, MaxPitch);
            Zoom = Scalar.Clamp(Zoom, MinZoom, ZoomLimit);
            Grab(x, height, z, vx, vy);
        }

        internal void ClampFocus()
        {
            FocusX = Scalar.Clamp(FocusX, -HalfX, HalfX);
            FocusZ = Scalar.Clamp(FocusZ, -HalfZ, HalfZ);
        }

        internal void Reset()
        {
            FocusX = FocusZ = Yaw = 0f;
            Pitch = DefaultPitch;
            Zoom = MinZoom;
        }

        /// <summary>Exponential approach; the same total time gives the same result at any frame rate.</summary>
        internal static float Damp(float current, float target, float dt, float tau) =>
            tau <= 0f ? target : target + (current - target) * (float)Math.Exp(-dt / tau);

        internal static float DampAngle(float current, float target, float dt, float tau) =>
            target + WrapAngle(current - target) * (tau <= 0f ? 0f : (float)Math.Exp(-dt / tau));

        internal static float WrapAngle(float degrees)
        {
            float wrapped = (degrees + 180f) % 360f;
            if (wrapped < 0f) wrapped += 360f;
            return wrapped - 180f;
        }
    }
}
