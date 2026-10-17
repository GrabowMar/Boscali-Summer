using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// How far a drop released here can travel to a landing zone, in flat metres: the HALO
    /// wingsuit glide (plus its canopy run; a low drop only gets the canopy), and the HAMILCAR
    /// glide. The glider numbers mirror HamilcarProfile (GlideRatio / MaxReach) - Vanguard owns
    /// that profile, this copy only feeds the pilot's drop cue. Pure.
    /// </summary>
    internal static class LandingZoneReach
    {
        public const float GliderRatio = 15f;
        public const float GliderMax = 45000f;

        public static float Halo(float heightAboveZone) =>
            heightAboveZone < HaloGlidePlan.LowDropHeight
                ? HaloGlidePlan.CanopyRun * 2f
                : HaloGlidePlan.GlideRatio * Mathf.Max(0f, heightAboveZone - HaloGlidePlan.PullHeight) + HaloGlidePlan.CanopyRun;

        public static float Glider(float heightAboveZone) =>
            Mathf.Clamp(GliderRatio * Mathf.Max(0f, heightAboveZone), 0f, GliderMax);

        /// <summary>Drop now and the load comes down on the zone; farther out it falls short.</summary>
        public static bool InWindow(float flatDistance, float reach) => flatDistance <= reach;
    }
}
