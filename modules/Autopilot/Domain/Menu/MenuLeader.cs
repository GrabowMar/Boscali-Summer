using System;

namespace BoscaliSummer.Modules.Autopilot.Domain
{
    /// <summary>The one polyline that ties the hub to the selected disc and the disc's plate to its dock (prototype renderLeader).</summary>
    internal static class MenuLeader
    {
        public const int MaxPoints = 6;

        /// <summary>
        /// Writes x,y pairs into <paramref name="pts"/> (room for <see cref="MaxPoints"/> * 2) and returns the point count:
        /// hub to the disc centre, out of the plate on its dock side, a
        /// 45 degree jog when the dock attachment is not level with the plate, then to the dock edge. The dock keeps its fixed top, so the
        /// attachment point is the disc's height clamped to the dock.
        /// </summary>
        public static int Compute(MenuLayout l, int cat, MenuCategory c, DockLayout dock, float[] pts)
        {
            float t = l.T, rho = l.Rho;
            int side = MenuLayout.DockSide(c);
            float x = l.DiscX[cat], y = l.DiscY[cat];
            MenuRect plate = l.Plate(cat, c.Badge);
            float yAtt = Math.Max(dock.Rect.T + 6f * t, Math.Min(dock.Rect.B - 6f * t, y));
            float xe = side > 0 ? dock.Rect.L : dock.Rect.R;
            float xp = side > 0 ? plate.R : plate.L;
            float exitY = Math.Max(y - plate.H / 2f + 4f * t, Math.Min(y + plate.H / 2f - 4f * t, yAtt));
            float dy = Math.Abs(yAtt - exitY), stub = 16f * rho;
            int n = 0;
            n = Add(pts, n, l.Cx, l.Cy);
            n = Add(pts, n, x, y);
            n = Add(pts, n, xp, exitY);
            if (dy > 0.5f)
            {
                float x1 = xp + side * stub, x2 = xe - side * stub, dxj = Math.Min(dy, Math.Max(0f, Math.Abs(x2 - x1)));
                n = Add(pts, n, x1, exitY);
                n = Add(pts, n, x1 + side * dxj, yAtt);
            }
            return Add(pts, n, xe, yAtt);
        }

        private static int Add(float[] pts, int n, float x, float y) { pts[n * 2] = x; pts[n * 2 + 1] = y; return n + 1; }
    }
}
