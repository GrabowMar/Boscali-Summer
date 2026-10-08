using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>MALD route: weave outbound along the launch bearing, then orbit until the motor quits.</summary>
    internal static class DecoyRoute
    {
        public const float Length = 80000f;
        public const float WeaveAmplitude = 1500f;
        public const float WeaveWavelength = 12000f;
        public const float Lookahead = 4000f;

        public static bool Outbound(float travelled) => travelled < Length;

        // Lateral offset (metres, +starboard) of the route point `Lookahead` ahead of `travelled`.
        // seed (0..1, per missile) phases and sizes the weave so a salvo does not fly in lockstep.
        public static float Lateral(float travelled, float seed = 0f) =>
            WeaveAmplitude * (1f + 0.2f * Mathf.Sin(seed * 6.283185f)) *
            Mathf.Sin(2f * Mathf.PI * (travelled + Lookahead) / WeaveWavelength + seed * 6.283185f);
    }
}
