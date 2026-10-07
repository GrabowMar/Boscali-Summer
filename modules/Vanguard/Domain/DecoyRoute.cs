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
        public static float Lateral(float travelled) =>
            WeaveAmplitude * Mathf.Sin(2f * Mathf.PI * (travelled + Lookahead) / WeaveWavelength);
    }
}
