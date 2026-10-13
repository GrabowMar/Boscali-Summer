using System;
using BoscaliSummer.Core.Math;

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
    }
}
