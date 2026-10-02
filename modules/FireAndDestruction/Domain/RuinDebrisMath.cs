using System;

namespace BoscaliSummer.Modules.FireAndDestruction.Domain
{
    /// <summary>
    /// One ballistic drop onto an 8×8 height field. The shard then rests.
    /// No Unity and no physics scene: a city of ruins must not wake rigidbodies.
    /// </summary>
    internal static class RuinDebrisMath
    {
        internal const int Grid = 8;
        internal const int ShardsPerPile = 6;
        internal const int MaxPiles = 6;
        internal const float Gravity = 9.8f;
        internal const float StopSpeed = 0.22f;

        private const float Spill = 0.18f;
        private const int SlideSteps = 8;
        private const float SlideDt = 0.05f;

        internal struct Shard
        {
            public float X;
            public float Y;
            public float Z;
            public float Vx;
            public float Vy;
            public float Vz;
            public float Thick;
        }

        internal static void Clear(float[] pile)
        {
            if (pile == null) return;
            for (int i = 0; i < pile.Length; i++) pile[i] = 0f;
        }

        internal static void Drop(ref Shard s, float[] pile, float originX, float originZ, float span, float groundY)
        {
            if (span < 0.5f) span = 0.5f;
            if (s.Thick < 0.05f) s.Thick = 0.05f;
            float half = span * 0.48f;
            float minX = originX - half;
            float maxX = originX + half;
            float minZ = originZ - half;
            float maxZ = originZ + half;
            float floor = groundY + s.Thick * 0.5f;
            float drop = s.Y - floor;
            if (drop > 0f)
            {
                float disc = s.Vy * s.Vy + 2f * Gravity * drop;
                float t = (s.Vy + (float)Math.Sqrt(disc)) / Gravity;
                if (t < 0f) t = 0f;
                else if (t > 4f) t = 4f;
                s.X += s.Vx * t;
                s.Z += s.Vz * t;
            }
            s.X = Clamp(s.X, minX, maxX);
            s.Z = Clamp(s.Z, minZ, maxZ);
            s.Vy = 0f;
            bool field = pile != null && pile.Length >= Grid * Grid;
            for (int step = 0; step < SlideSteps; step++)
            {
                s.X = Clamp(s.X + s.Vx * SlideDt, minX, maxX);
                s.Z = Clamp(s.Z + s.Vz * SlideDt, minZ, maxZ);
                if (s.X <= minX || s.X >= maxX) s.Vx = 0f;
                if (s.Z <= minZ || s.Z >= maxZ) s.Vz = 0f;
                s.Y = groundY + (field ? pile[Cell(s.X, s.Z, originX, originZ, span)] : 0f) + s.Thick * 0.5f;
                s.Vx *= 0.42f;
                s.Vz *= 0.42f;
                if (s.Vx * s.Vx + s.Vz * s.Vz < StopSpeed * StopSpeed) break;
            }
            s.X = Clamp(s.X, minX, maxX);
            s.Z = Clamp(s.Z, minZ, maxZ);
            float piled = field ? pile[Cell(s.X, s.Z, originX, originZ, span)] : 0f;
            s.Y = groundY + piled + s.Thick * 0.5f;
            s.Vx = 0f;
            s.Vy = 0f;
            s.Vz = 0f;
            if (!field) return;
            int cell = Cell(s.X, s.Z, originX, originZ, span);
            pile[cell] += s.Thick;
            int ix = cell % Grid;
            int iz = cell / Grid;
            float spill = s.Thick * Spill;
            if (ix > 0) pile[cell - 1] += spill;
            if (ix + 1 < Grid) pile[cell + 1] += spill;
            if (iz > 0) pile[cell - Grid] += spill;
            if (iz + 1 < Grid) pile[cell + Grid] += spill;
        }

        private static int Cell(float x, float z, float originX, float originZ, float span)
        {
            float u = (x - originX) / span + 0.5f;
            float v = (z - originZ) / span + 0.5f;
            if (u < 0f) u = 0f;
            else if (u > 0.999f) u = 0.999f;
            if (v < 0f) v = 0f;
            else if (v > 0.999f) v = 0.999f;
            int ix = (int)(u * Grid);
            int iz = (int)(v * Grid);
            if (ix < 0) ix = 0;
            else if (ix >= Grid) ix = Grid - 1;
            if (iz < 0) iz = 0;
            else if (iz >= Grid) iz = Grid - 1;
            return iz * Grid + ix;
        }

        /// <summary>
        /// Shoves a resting shard along a flat push and holds it on a leash around its rest
        /// point. Returns a yaw in degrees so the shove reads as a twist, not a teleport.
        /// </summary>
        internal static float Kick(
            ref float x, ref float z, float pushX, float pushZ, float power,
            float homeX, float homeZ, float leash)
        {
            float mag = (float)Math.Sqrt(pushX * pushX + pushZ * pushZ);
            if (mag < 0.0001f) { pushX = 1f; pushZ = 0f; mag = 1f; }
            float reach = power < 1f ? 0.35f : Math.Min(1.6f, 0.4f + power * 0.035f);
            x += pushX / mag * reach;
            z += pushZ / mag * reach;
            if (leash < 0.5f) leash = 0.5f;
            float dx = x - homeX;
            float dz = z - homeZ;
            float dist = (float)Math.Sqrt(dx * dx + dz * dz);
            if (dist > leash)
            {
                float scale = leash / dist;
                x = homeX + dx * scale;
                z = homeZ + dz * scale;
            }
            return power < 1f ? 8f : (float)Math.Min(22f, 8f + power * 0.3f);
        }

        private static float Clamp(float value, float lo, float hi)
        {
            if (value < lo) return lo;
            if (value > hi) return hi;
            return value;
        }
    }
}
