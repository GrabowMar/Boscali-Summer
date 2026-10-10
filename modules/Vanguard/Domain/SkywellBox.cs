using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>
    /// SKYWELL service box: a receiver is served while it holds loose formation in a generous box behind and below the
    /// tanker (kit mount frame: x right, y up, z forward, so behind is negative z). Flying into it IS the request; nothing
    /// ever moves the receiver. Pure maths (the domain tests run outside Unity: no native Quaternion calls here).
    /// </summary>
    internal static class SkywellBox
    {
        public const float BehindMin = 20f, BehindMax = 90f, HalfWidth = 30f, Below = 40f, Above = 10f;
        public const float MaxRelSpeed = 15f, MaxRollDeg = 30f;
        public const float KeepScale = 1.35f;     // a receiver already served may drift this much wider before the box is lost
        public const float HoldSeconds = 1f;      // inside this long before the glider is sent
        public const float LostSeconds = 1.5f;    // outside the keep box this long = BOX LOST

        /// <summary>Middle of the box: the formation slot an AI receiver (and the sim's seated jet) flies to.</summary>
        public static readonly Vector3 Centre = new Vector3(0f, -(Below - Above) * 0.5f, -(BehindMin + BehindMax) * 0.5f);

        /// <param name="rel">Receiver position minus the kit mount, in tanker axes.</param>
        /// <param name="relSpeed">Receiver velocity relative to the tanker, m/s.</param>
        /// <param name="rollDeg">Receiver roll relative to the tanker, degrees (either sign).</param>
        /// <param name="keep">Judge by the wider keep box (a receiver the glider already serves).</param>
        public static bool Inside(Vector3 rel, float relSpeed, float rollDeg, bool keep)
        {
            float k = keep ? KeepScale : 1f;
            Vector3 d = rel - Centre;
            return Mathf.Abs(d.x) <= HalfWidth * k &&
                   Mathf.Abs(d.y) <= (Below + Above) * 0.5f * k &&
                   Mathf.Abs(d.z) <= (BehindMax - BehindMin) * 0.5f * k &&
                   relSpeed <= MaxRelSpeed * k && Mathf.Abs(rollDeg) <= MaxRollDeg * k;
        }

        /// <summary>Roll of the receiver about its own nose relative to the tanker's horizon, from both aircraft's world axes.</summary>
        public static float Roll(Vector3 tankerRight, Vector3 tankerUp, Vector3 tankerFwd, Vector3 rxFwd, Vector3 rxUp)
        {
            var f = new Vector3(Vector3.Dot(rxFwd, tankerRight), Vector3.Dot(rxFwd, tankerUp), Vector3.Dot(rxFwd, tankerFwd));
            var u = new Vector3(Vector3.Dot(rxUp, tankerRight), Vector3.Dot(rxUp, tankerUp), Vector3.Dot(rxUp, tankerFwd));
            Vector3 level = Vector3.up - f * Vector3.Dot(Vector3.up, f);
            if (level.sqrMagnitude < 1e-4f) return 0f; // nose straight up or down: roll is meaningless
            return Vector3.SignedAngle(level, u, f);
        }
    }

    /// <summary>The tethered service glider: where it flies, how it follows, how long it takes to arrive.</summary>
    internal static class SkywellGlider
    {
        public const float Under = 3.5f;          // the glider's working joint hangs this far below the receiver's belly
        public const float ArmClearance = 2.5f;   // hand-off: shoulder this far below the rail (the arm reaches 3.65 m)
        public const float MaxRelSpeed = 12f;     // closing speed on the receiver, m/s
        public const float SmoothSeconds = 0.45f; // critically damped follow
        public const float TurnDegPerSecond = 90f;

        /// <summary>Tow position: where the glider loiters on its tether with nobody to serve (kit mount, tanker axes).</summary>
        public static readonly Vector3 Tow = new Vector3(0f, -11.5f, -30f);

        /// <summary>Seconds the server gives the glider to reach a receiver from the tow position.</summary>
        public static float ArrivalSeconds(float distance) => Mathf.Clamp(1f + distance / 10f, 2.5f, 7f);

        /// <summary>Hand-off height in the receiver's frame: the shoulder sits ArmClearance under the rail, but never closer than 1.8 m to the belly.</summary>
        public static float ServeHeight(float railY, float bellyY) => Mathf.Clamp(railY - ArmClearance, bellyY - 5f, bellyY - 1.8f);

        /// <summary>Distance from a point to a box (0 inside); the glider-to-airframe clearance.</summary>
        public static float BoxDistance(Vector3 point, Vector3 centre, Vector3 extents)
        {
            Vector3 d = point - centre;
            var out3 = new Vector3(Mathf.Max(0f, Mathf.Abs(d.x) - extents.x), Mathf.Max(0f, Mathf.Abs(d.y) - extents.y), Mathf.Max(0f, Mathf.Abs(d.z) - extents.z));
            return out3.magnitude;
        }

        /// <summary>
        /// One frame of the follow, critically damped in the target's own frame: <paramref name="targetFrom"/> is where the target
        /// was last frame, <paramref name="targetTo"/> where it is now (equal on a retarget, so the glider eases onto the new pose
        /// from its current velocity). The closing speed on the target never exceeds <paramref name="maxRel"/>.
        /// </summary>
        public static void Follow(ref Vector3 pos, ref Vector3 vel, Vector3 targetFrom, Vector3 targetTo, float dt, float smooth, float maxRel)
        {
            if (dt <= 1e-5f) return;
            smooth = Mathf.Max(1e-4f, smooth);
            Vector3 targetVel = (targetTo - targetFrom) / dt;
            Vector3 e = pos - targetFrom;       // error in the target's frame
            Vector3 vr = vel - targetVel;       // velocity relative to the target
            float omega = 2f / smooth, x = omega * dt;
            float damp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
            Vector3 change = Vector3.ClampMagnitude(e, maxRel * smooth);
            Vector3 hold = e - change;          // the part of the error the speed limit defers
            Vector3 temp = (vr + omega * change) * dt;
            vr = (vr - omega * temp) * damp;
            Vector3 next = hold + (change + temp) * damp;
            if (Vector3.Dot(e, next) < 0f)      // never overshoot the target
            {
                next = Vector3.zero;
                vr = Vector3.zero;
            }
            pos = targetTo + next;
            vel = targetVel + vr;
        }
    }

    /// <summary>
    /// Steering for an AI receiver (or the sim's seated jet) toward the box centre through the game's own autopilot target:
    /// fly parallel to the tanker, aim a bounded correction off its track from a look-ahead measured in seconds of flight,
    /// let throttle close the fore-aft gap and grant bank in proportion to the turn actually asked for.
    /// </summary>
    internal sealed class SkywellHold
    {
        public struct Command
        {
            public Vector3 Aim;      // world point to hand to the autopilot
            public float Throttle;
            public float Bank;       // bankAllowed, degrees
        }

        public const float LookAheadSeconds = 8f, MinLookAhead = 800f, MaxCorrectionSlope = 0.36f, CrossGain = 1.5f;
        public const float BaseThrottle = 0.6f, ThrottleGain = 0.04f, ThrottleIntegral = 0.02f;
        public const float AlongGain = 0.12f, MaxCatchUp = 30f, MaxDropBack = 15f;

        private float integral;

        public Command Step(Vector3 pos, Vector3 vel, Vector3 slot, Vector3 leaderVel, float dt)
        {
            float leaderSpeed = leaderVel.magnitude;
            Vector3 track = leaderSpeed > 1f ? leaderVel / leaderSpeed : (vel.sqrMagnitude > 1f ? vel.normalized : Vector3.forward);
            Vector3 err = slot - pos;
            float along = Vector3.Dot(err, track);
            Vector3 cross = err - along * track;       // only cross-track error steers; along-track is throttle's job
            float speed = vel.magnitude;
            float look = Mathf.Max(MinLookAhead, speed * LookAheadSeconds);
            Vector3 aim = pos + track * look + Vector3.ClampMagnitude(cross * CrossGain, look * MaxCorrectionSlope);

            float wanted = leaderSpeed + Mathf.Clamp(along * AlongGain, -MaxDropBack, MaxCatchUp);
            float speedErr = wanted - speed;
            integral = Mathf.Clamp(integral + ThrottleIntegral * speedErr * dt, -0.35f, 0.35f);

            Vector3 heading = speed > 1f ? vel / speed : track;
            float turn = Vector3.Angle(heading, aim - pos);
            return new Command
            {
                Aim = aim,
                Throttle = Mathf.Clamp01(BaseThrottle + ThrottleGain * speedErr + integral),
                Bank = Mathf.Clamp(8f + 2.5f * turn, 8f, 60f), // a command parallel to the velocity must not be allowed to roll the jet
            };
        }
    }
}
