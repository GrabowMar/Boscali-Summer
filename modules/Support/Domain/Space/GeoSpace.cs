using System;

namespace BoscaliSummer.Modules.Support.Domain.Space
{
    /// <summary>Geostationary-bird constants and maps shared by the host rules and the ORBIT view (engine-free).</summary>
    internal static class GeoSpace
    {
        /// <summary>Footprint radius per bird (OPTICAL, RADAR, KINETIC) as a share of the theatre width (optical / radar swath, kinetic strike reach).</summary>
        public static readonly float[] Reach = { 0.14f, 0.20f, 0.10f };
        public static readonly string[] Names = { "OPTICAL", "RADAR", "KINETIC" };
        /// <summary>The bird must have this much fuel (percent) for the SPACE director to start a burn.</summary>
        public const float DirectorMinFuel = 30f;
        /// <summary>Quiet time between two director burns of one bird (mission seconds).</summary>
        public const float DirectorGap = 180f;
        private const int Grid = 16383;

        public static float U(float x, float width) => Clamp01(x / Math.Max(1000f, width) + 0.5f);
        public static float V(float z, float height) => Clamp01(0.5f - z / Math.Max(1000f, height));
        private static float Clamp01(float x) => x < 0f ? 0f : x > 1f ? 1f : x;

        /// <summary>Wire form of a map point: two 14-bit cells in one non-negative int.</summary>
        public static int Pack(float u, float v) => (int)Math.Round(Clamp01(u) * Grid) | ((int)Math.Round(Clamp01(v) * Grid) << 14);

        public static bool TryUnpack(int packed, out float u, out float v)
        {
            u = v = 0f;
            if (packed < 0) return false;
            u = (packed & Grid) / (float)Grid; v = ((packed >> 14) & Grid) / (float)Grid;
            return true;
        }

        /// <summary>Why a burn was refused, or None when it started.</summary>
        public enum Refusal : byte { None, Dead, Moving, NoMove, NoFuel }

        public static Refusal Check(in GeoBird bird, bool alive, float now, float u, float v)
        {
            if (!alive) return Refusal.Dead;
            if (bird.Moving(now)) return Refusal.Moving;
            float cost = GeoBird.Cost(GeoBird.Distance(bird.U(now), bird.V(now), u, v));
            if (cost <= 0f) return Refusal.NoMove;
            return bird.Fuel < cost ? Refusal.NoFuel : Refusal.None;
        }

        /// <summary>
        /// The SPACE director's choice: the living, idle bird with fuel above <see cref="DirectorMinFuel"/> and past its quiet time whose footprint misses (u,v), nearest first.
        /// -1 when every bird covers the point, is busy, resting or too low.
        /// </summary>
        public static int PickBird(SpaceState state, float now, float[] restUntil, float u, float v)
        {
            int best = -1; float bestD = float.MaxValue;
            for (int i = 0; i < SpaceRules.BirdCount; i++)
            {
                var kind = (BirdKind)i;
                GeoBird g = state.Geo(kind);
                if (!state.HasBird(kind) || g.Moving(now) || g.Fuel <= DirectorMinFuel || now < restUntil[i] || g.Covers(now, u, v, Reach[i])) continue;
                float d = GeoBird.Distance(g.U(now), g.V(now), u, v);
                if (d < bestD && GeoBird.Cost(d) < g.Fuel - DirectorMinFuel * 0.5f) { bestD = d; best = i; }
            }
            return best;
        }

        /// <summary>Parked points over the faction's side of the front: from the own airbase centroid toward the enemy's, a different share per bird.</summary>
        public static void Defaults(float ownU, float ownV, float foeU, float foeV, float[] u, float[] v)
        {
            float[] share = { 0.40f, 0.30f, 0.50f };
            float du = foeU - ownU, dv = foeV - ownV, len = (float)Math.Sqrt(du * du + dv * dv);
            float pu = len > 0.001f ? -dv / len : 0f, pv = len > 0.001f ? du / len : 0f; // spread across the front, never stacked
            float[] side = { -0.08f, 0.08f, 0f };
            for (int i = 0; i < 3; i++)
            {
                u[i] = Math.Max(0.05f, Math.Min(0.95f, ownU + du * share[i] + pu * side[i]));
                v[i] = Math.Max(0.05f, Math.Min(0.95f, ownV + dv * share[i] + pv * side[i]));
            }
        }
    }
}
