using System.Globalization;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>Outcome of one AEGIS trigger press. Busy = trigger held or station cycling: nothing judged, nothing shown.</summary>
    internal enum SnapVerdict { Fire, Busy, Empty, Reserved, NoTarget, NotAir, OutOfRange, OffNose }

    /// <summary>
    /// AEGIS-3 "SNAP" (design/vanguard-remake/MECHANICS.md): a tap on the AEGIS station sends one dart at the locked
    /// air unit inside 2.5 km and +-70 deg of the nose. The automatic missile defence keeps priority: with a threat
    /// inbound the last dart is held for it.
    /// </summary>
    internal static class AegisSnap
    {
        public const float MaxRange = 2500f;
        public const float MaxOffNose = 70f;
        public const float Lifetime = 8f;          // snap dart
        public const float DefenceLifetime = 6f;   // automatic missile-defence dart
        public const float DamageScale = 2f;       // snap contact damage vs the defence dart
        public const float HeldGap = .15f;         // trigger calls closer than this are one held press (tap to fire)

        // Pod cell cover: opens over DoorLead (= the cell's rail delay, so it is open before the dart drops),
        // stays open until DoorHold after the trigger, then closes over DoorClose.
        public const float DoorLead = .15f;
        public const float DoorHold = .6f;
        public const float DoorClose = .3f;
        public const float DoorAngle = 100f;

        // Dart grid fins snap open between these ages after release.
        public const float FinStart = .05f;
        public const float FinEnd = .2f;

        public static SnapVerdict Judge(bool hasTarget, bool air, float range, float offNoseDeg, int ammo, bool threatInbound)
        {
            if (ammo <= 0) return SnapVerdict.Empty;
            if (ammo == 1 && threatInbound) return SnapVerdict.Reserved;
            if (!hasTarget) return SnapVerdict.NoTarget;
            if (!air) return SnapVerdict.NotAir;
            if (range > MaxRange) return SnapVerdict.OutOfRange;
            if (offNoseDeg > MaxOffNose) return SnapVerdict.OffNose;
            return SnapVerdict.Fire;
        }

        /// <summary>HUD detail under "SNAP — NO SOLUTION".</summary>
        public static string Reason(SnapVerdict verdict, float range)
        {
            switch (verdict)
            {
                case SnapVerdict.Empty: return "NO DARTS LEFT";
                case SnapVerdict.Reserved: return "LAST DART HELD FOR DEFENCE";
                case SnapVerdict.NoTarget: return "NO LOCKED TARGET";
                case SnapVerdict.NotAir: return "AIR TARGETS ONLY";
                case SnapVerdict.OutOfRange: return "RANGE " + Km(range) + " km > 2.5";
                case SnapVerdict.OffNose: return "TARGET OFF THE NOSE";
                default: return null;
            }
        }

        public static string Km(float metres) => (metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture);

        /// <summary>Hit-to-kill damage: the dart's own pierce scaled by closure, x2 for a snap.</summary>
        public static float Damage(float pierce, float closing, float scale) => pierce * Mathf.Clamp(closing / 300f, 1f, 4f) * scale;

        /// <summary>Snap contact sphere = the target's bounding radius, so the dart scores before the vanilla impact ray.</summary>
        public static float ContactRadius(float maxRadius) => Mathf.Clamp(maxRadius, 2f, 15f);

        /// <summary>
        /// Swept contact over the last tick plus a one-tick look-ahead along the closing velocity: the seeker hook runs
        /// before Missile.DetectCollisions, whose impact ray looks 1.1 ticks ahead.
        /// </summary>
        public static bool Contact(Vector3 previous, Vector3 current, Vector3 closingVelocity, float dt, float radius) =>
            AegisEnvelope.Contact(previous, current, radius) ||
            AegisEnvelope.Contact(current, current + closingVelocity * (dt * 1.1f), radius);

        /// <summary>Cover opening 0..1, <paramref name="sinceTrigger"/> seconds after the cell was fired.</summary>
        public static float Door(float sinceTrigger) => sinceTrigger < DoorHold
            ? Mathf.SmoothStep(0f, 1f, sinceTrigger / DoorLead)
            : 1f - Mathf.SmoothStep(0f, 1f, (sinceTrigger - DoorHold) / DoorClose);

        /// <summary>Hinge angle (deg, about the pod's +Z) that swings a cover's free edge down: the edge lies
        /// <paramref name="lateral"/> metres to starboard (+) or port (-) of its hinge.</summary>
        public static float DoorSwing(float lateral) => lateral >= 0f ? -DoorAngle : DoorAngle;

        public static float Fins(float age) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(FinStart, FinEnd, age));

        /// <summary>
        /// Folded fin -> radial. <paramref name="tip"/> = fin centre minus hinge, <paramref name="fromAxis"/> = hinge minus
        /// a point on the body axis. Returns the swing angle (0 when the fin already stands radial or the rig is degenerate)
        /// and the world hinge axis.
        /// </summary>
        public static float FinSwing(Vector3 tip, Vector3 fromAxis, Vector3 bodyForward, out Vector3 axis)
        {
            axis = Vector3.zero;
            Vector3 radial = Vector3.ProjectOnPlane(fromAxis, bodyForward);
            if (radial.sqrMagnitude < 1e-8f || tip.sqrMagnitude < 1e-8f) return 0f;
            Vector3 cross = Vector3.Cross(tip, radial);
            if (cross.sqrMagnitude < 1e-10f) return 0f;
            axis = cross.normalized;
            return Vector3.Angle(tip, radial);
        }
    }
}
