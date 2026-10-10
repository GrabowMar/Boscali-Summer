using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Two-bone arm maths: in a vertical plane (angles in degrees) and in 3D (joint positions).</summary>
    internal static class ArmIK
    {
        /// <summary>Planar arm in the shoulder's vertical plane (+Z reach, +Y up), elbow up.</summary>
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

        /// <summary>
        /// Elbow position of a two-bone arm that reaches from <paramref name="shoulder"/> toward <paramref name="target"/>, the elbow
        /// pushed toward <paramref name="pole"/>. A target out of reach is clamped to the arm's reach along the same line; the end
        /// effector then sits at <paramref name="end"/>.
        /// </summary>
        public static Vector3 Elbow(Vector3 shoulder, Vector3 target, Vector3 pole, float upper, float lower, out Vector3 end)
        {
            Vector3 d = target - shoulder;
            float dist = d.magnitude;
            Vector3 dir = dist > 1e-4f ? d / dist : pole.sqrMagnitude > 1e-6f ? pole.normalized : Vector3.up;
            dist = Mathf.Clamp(dist, Mathf.Abs(upper - lower) + 1e-3f, upper + lower - 1e-3f);
            end = shoulder + dir * dist;
            float along = (upper * upper - lower * lower + dist * dist) / (2f * dist);
            float height = Mathf.Sqrt(Mathf.Max(0f, upper * upper - along * along));
            Vector3 perp = pole - dir * Vector3.Dot(pole, dir);
            if (perp.sqrMagnitude < 1e-6f) perp = Vector3.Cross(dir, Mathf.Abs(dir.y) < 0.9f ? Vector3.up : Vector3.right);
            return shoulder + dir * along + perp.normalized * height;
        }
    }
}
