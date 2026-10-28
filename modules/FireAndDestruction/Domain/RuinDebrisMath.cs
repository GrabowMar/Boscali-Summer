using System;

namespace BoscaliSummer.Modules.FireAndDestruction.Domain
{
    /// <summary>
    /// One ballistic drop onto an 8×8 height field. The shard then rests.
    /// No Unity and no physics scene: a city of ruins must not wake rigidbodies.
    /// </summary>
    internal static class RuinDebrisMath
    {
        internal const int MaxPiles = 6;
        internal const float Gravity = 9.8f;

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
