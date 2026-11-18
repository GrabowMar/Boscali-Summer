namespace BoscaliSummer.Modules.Wing.Domain
{
    internal static class PlanEdit
    {
        /// <summary>An ORBIT step's default time, s.</summary>
        public static float OrbitSeconds = 180f;

        /// <summary>Where the step leaves its element: its last point (false for ATTACK, RTB, REFIT and FORM UP).</summary>
        public static bool EndPoint(PlanStep p, out float x, out float z)
        {
            x = z = 0f;
            if (p?.Points == null || p.Points.Length == 0 || p.Kind == PlanKind.Attack) return false;
            Waypoint w = p.Points[p.Points.Length - 1];
            x = w.X;
            z = w.Z;
            return true;
        }
    }
}
