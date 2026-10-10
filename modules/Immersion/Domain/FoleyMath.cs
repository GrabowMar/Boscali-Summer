using System;

namespace BoscaliSummer.Modules.Immersion.Domain
{
    internal enum HitSound { None, Ping, Thud }

    /// <summary>Which cockpit sound a hit on the airframe earns. Rough on purpose; the game already thuds on every bullet.</summary>
    internal static class FoleyMath
    {
        /// <summary>Net blast (hit points on one part) that marks a warhead rather than a cannon shell.</summary>
        public const float WarheadBlast = 10f;

        public static HitSound Hit(float pierce, float blast) =>
            blast >= WarheadBlast ? HitSound.Thud : pierce > 0f ? HitSound.Ping : HitSound.None;

        public static float PingVolume(float pierce) => .22f + .3f * Math.Max(0f, Math.Min(1f, pierce / 40f));
    }

    /// <summary>
    /// Harness creak trigger: fires when the load has moved 1.5 G since the last time, once it is a real load
    /// (3 G or more on either side), at most every 1.2 s.
    /// </summary>
    internal struct StrainCreak
    {
        private float level, cooldown;

        public bool Step(float g, float dt)
        {
            cooldown -= dt;
            if (!(Math.Abs(g - level) >= 1.5f)) return false; // also false for NaN
            bool loaded = Math.Max(g, level) >= 3f;
            level = g;
            if (!loaded || cooldown > 0f) return false;
            cooldown = 1.2f;
            return true;
        }
    }
}
