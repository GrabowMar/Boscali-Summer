using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Two-bone arm in the shoulder's vertical plane (+Z reach, +Y up), elbow up; angles in degrees.</summary>
    internal static class ArmIK
    {
        /// <returns>False when the target is out of reach (the arm then points straight at it).</returns>
        public static bool Solve(float upper, float lower, Vector3 target, out float shoulderPitch, out float elbow)
        {
            float d = new Vector2(target.z, target.y).magnitude;
            float aim = Mathf.Atan2(target.y, Mathf.Max(target.z, 1e-4f)) * Mathf.Rad2Deg;
            bool reachable = d <= upper + lower + 1e-3f && d >= Mathf.Abs(upper - lower) - 1e-3f;
            d = Mathf.Clamp(d, Mathf.Abs(upper - lower) + 1e-3f, upper + lower);
            float interior = Mathf.Acos(Mathf.Clamp((upper * upper + lower * lower - d * d) / (2f * upper * lower), -1f, 1f)) * Mathf.Rad2Deg;
            float lift = Mathf.Acos(Mathf.Clamp((upper * upper + d * d - lower * lower) / (2f * upper * d), -1f, 1f)) * Mathf.Rad2Deg;
            elbow = 180f - interior;
            shoulderPitch = aim + lift;
            return reachable;
        }
    }
}
