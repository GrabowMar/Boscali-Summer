using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>ALE-X KITE tow geometry: fiber length, reel speed, where the decoy rides and when the fiber parts.</summary>
    internal static class TowedTrail
    {
        public const float IdleLine = 60f;       // nothing hunting the host
        public const float FarLine = 150f;       // a radar missile tracks the host inside FarRange
        public const float NearLine = 250f;      // ... inside NearRange
        public const float FarRange = 10000f;
        public const float NearRange = 4000f;
        public const float ReelSpeed = 40f;      // m/s, paying out and reeling in
        public const float DropPerMetre = 0.08f; // the decoy hangs 8 m under a 100 m fiber
        public const float Lag = 0.1f;           // s, how slowly the body swings after the host's turns
        public const float MinAgl = 50f;
        public const float NoThreat = float.MaxValue;
        internal static float SnapG = 7f; // the nomodkit sim raises it: its AI host pulls ~7 g after spawn

        /// <summary>Fiber length wanted for the nearest radar missile tracking the host (NoThreat when none).</summary>
        public static float LineFor(float threatRange) =>
            threatRange <= NearRange ? NearLine : threatRange <= FarRange ? FarLine : IdleLine;

        public static float Reel(float current, float wanted, float dt) => Mathf.MoveTowards(current, wanted, ReelSpeed * dt);

        public static float Drop(float line) => line * DropPerMetre;

        /// <summary>Decoy offset from the pod's exit point: straight astern of the host, a little below.</summary>
        public static Vector3 Offset(Vector3 forward, Vector3 up, float line) => -forward * line - up * Drop(line);

        /// <summary>First-order follow of the wanted offset. The offset is host-relative, so host speed adds no steady lag.</summary>
        public static Vector3 Follow(Vector3 current, Vector3 wanted, float dt) =>
            Vector3.Lerp(current, wanted, 1f - Mathf.Exp(-dt / Lag));

        public static bool Snaps(float gForce, float agl) => gForce > SnapG || agl < MinAgl;

        /// <summary>Manoeuvre load in g from a velocity change over dt (gravity excluded, as vanilla gForce).</summary>
        public static float GLoad(Vector3 previousVelocity, Vector3 velocity, float dt) =>
            dt > 0f ? (velocity - previousVelocity).magnitude / (dt * 9.81f) : 0f;
    }
}
