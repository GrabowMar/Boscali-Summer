using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>SKYWELL probe contact: capture zone behind the ramp, phase timings and abort rule (tanker frame).</summary>
    internal static class SkywellContact
    {
        public static readonly Vector3 ContactOffset = new Vector3(0f, -14f, -34f); // from the kit mount, tanker axes
        public const float CaptureRadius = 90f;
        public const float MaxClosing = 30f;
        public const float ReachSeconds = 3f;
        public const float LockTolerance = 1.5f;
        public const float AbortDistance = 25f;
        public const float ReleaseSeconds = 2f;

        /// <param name="relToContact">Receiver probe position minus the contact point, in the tanker frame.</param>
        public static bool Captures(Vector3 relToContact, float closing, bool needs) =>
            needs && relToContact.magnitude <= CaptureRadius && relToContact.z <= 5f && Mathf.Abs(closing) < MaxClosing;

        public static float TransferSeconds(float fuelNeededKg, int rounds) =>
            Mathf.Clamp(fuelNeededKg / 400f + rounds * 0.35f, 3f, 20f);

        public static bool Aborts(float distanceToContact, bool stickInput) => stickInput || distanceToContact > AbortDistance;
    }
}
