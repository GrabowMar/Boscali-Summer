using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>GLAIVE glide profile: a shallow slope to the target, 40 m in the last 1.5 km, release over it.</summary>
    internal static class CarrierProfile
    {
        public const float ReleaseHeight = 40f;
        public const float FinalRange = 1500f;
        public const float ReleaseRange = 300f;
        public const float ReleaseMaxAgl = 120f;
        public const int PayloadCount = 2;

        /// <summary>Metres above the target point to aim at, at this flat range.</summary>
        public static float Height(float range) =>
            range > FinalRange ? Mathf.Clamp(range * 0.08f, 150f, 3000f) : ReleaseHeight;

        public static bool ShouldRelease(float range, float agl) => range < ReleaseRange && agl < ReleaseMaxAgl;

        public const float DropSearchRadius = 150f;
        public const float DropSpacing = 20f;

        /// <summary>Ground offsets (metres, x = right, y = forward) tried for UGV placement: the release point, then rings out to 150 m.</summary>
        public static List<Vector2> DropCandidates()
        {
            var spots = new List<Vector2> { Vector2.zero };
            for (float r = 25f; r <= DropSearchRadius; r += 25f)
            {
                int n = Mathf.Max(6, Mathf.RoundToInt(2f * Mathf.PI * r / 25f));
                for (int i = 0; i < n; i++)
                {
                    float a = 2f * Mathf.PI * i / n;
                    spots.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
                }
            }
            return spots;
        }

        public static bool Spaced(Vector2 spot, List<Vector2> taken)
        {
            for (int i = 0; i < taken.Count; i++)
                if ((taken[i] - spot).sqrMagnitude < DropSpacing * DropSpacing) return false;
            return true;
        }
    }
}
