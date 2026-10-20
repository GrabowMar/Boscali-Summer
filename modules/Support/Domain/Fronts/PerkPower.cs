using System;

namespace BoscaliSummer.Modules.Support.Domain.Fronts
{
    /// <summary>
    /// What a front's POWER (<see cref="FrontSuperiority.Power"/>, 0.6 .. 1.5) and one room fact do to a perk. Pure: the actions pass the power the host read from the front book and the fact the host read
    /// from the room (birds covering the aim, nodes held, camps alive, teams afield), and take back a number.
    /// </summary>
    internal static class PerkPower
    {
        /// <summary>A whole-number effect (strikes in a salvo, units followed): the base times the power, rounded, never below one and never above <paramref name="cap"/>.</summary>
        public static int Count(int baseCount, float power, int cap = int.MaxValue) =>
            Math.Max(1, Math.Min(cap, (int)Math.Round(baseCount * Sane(power), MidpointRounding.AwayFromZero)));

        /// <summary>A rod's blast damage scale: the power itself (0.6 .. 1.5). The radius stays at the stand-off the RodGuard clears.</summary>
        public static float Damage(float power) => Sane(power);

        /// <summary>A pilot's cooldown while the front is ahead: seconds / power above 1, unchanged at or below 1.</summary>
        public static float Cooldown(float seconds, float power)
        {
            power = Sane(power);
            return power > 1f ? seconds / power : seconds;
        }

        /// <summary>A jam's strength: a factor below 1 (RADAR BLIND 0.4) deepens with the power (1.5 takes 0.4 to 0.1, 0.6 only to 0.64); a factor of 1 or more is no jam and passes through.</summary>
        public static float JamFactor(float baseFactor, float power)
        {
            if (!(baseFactor < 1f)) return baseFactor;
            return Math.Max(0.1f, Math.Min(1f, 1f - (1f - baseFactor) * Sane(power)));
        }

        /// <summary>SAT CAMERA reach: +15 % for every other own satellite that also covers the aim (the camera's own bird is the first).</summary>
        public static float SatCameraRadius(float radius, int birdsCovering) => radius * (1f + 0.15f * Math.Max(0, birdsCovering - 1));

        /// <summary>SAM NET DOWN length: +15 % per enemy node the faction's CYBER holds, at most x1.6.</summary>
        public static float SamNetDuration(float seconds, int heldEnemyNodes) => seconds * Math.Min(1.6f, 1f + 0.15f * Math.Max(0, heldEnemyNodes));

        /// <summary>SOF odds: never above 95 % and never below 20 %.</summary>
        public static float ClampOdds(float odds) => float.IsNaN(odds) ? 0.2f : Math.Max(0.2f, Math.Min(0.95f, odds));

        /// <summary>SABOTAGE STRIKE odds: 70 % (the SOF base) times the power, +5 % per live own camp (at most +15 %).</summary>
        public static float SabotageOdds(float power, int liveCamps) => ClampOdds(0.7f * Sane(power) + Math.Min(0.15f, 0.05f * Math.Max(0, liveCamps)));

        /// <summary>OVERWATCH: how far from the aim a moving group is looked for (m), how many units are followed and for how long (s).</summary>
        public static float OverwatchRadius(float power) => 3000f * Sane(power);

        public static int OverwatchUnits(float power) => Count(4, power);

        public static float OverwatchSeconds(float power) => 60f * Sane(power);

        private static float Sane(float power) => float.IsNaN(power) || float.IsInfinity(power) || power <= 0f ? 1f : Math.Max(0.6f, Math.Min(1.5f, power));
    }
}
