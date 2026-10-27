using UnityEngine;

namespace BoscaliSummer.Modules.CombatAdjust.Domain
{
    internal enum Fault { None, DudFuse, SeekerFail }

    /// <summary>Guided missiles that fail on their own: one roll per missile at launch, split evenly between a dud fuse and a wandering seeker.</summary>
    internal static class MissileFaults
    {
        private const uint Salt = 0xFA17u;

        /// <summary>
        /// One roll per missile. A seeker that never sets a proximity fuse (laser, cruise, glide bomb) cannot have a dud fuse, so its whole
        /// failure share wanders instead and the advertised rate holds for every guided weapon.
        /// </summary>
        public static Fault Roll(uint missileId, float failChance, bool fusable = true)
        {
            float r = Seeded.Roll01(missileId, Salt, 1);
            if (r >= failChance) return Fault.None;
            return fusable && r < failChance * 0.5f ? Fault.DudFuse : Fault.SeekerFail;
        }

        /// <summary>Seconds after launch a failing seeker starts to wander (1-4 s).</summary>
        public static float FailDelay(uint missileId) => Seeded.Range(missileId, Salt, 2, 1f, 4f);

        /// <summary>Aim error of a failed seeker: grows 15 m/s, up to 400 m.</summary>
        public static float FailRadius(float secondsSinceFail) => Mathf.Clamp(15f * secondsSinceFail, 0f, 400f);
    }
}
