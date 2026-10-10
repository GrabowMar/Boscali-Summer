using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Domain
{
    /// <summary>
    /// Formation hold for an AI receiver (or the sim's seated jet): a pure flight loop that writes the stick, not an autopilot target.
    /// The game's own AI aim loop (AutoAim) is tuned for corner speed and rings at a tanker's slow speed; this is the loop the nomodkit
    /// sim flies its own leaders with, which holds level flight for minutes: bank from a heading error with roll-rate damping, a
    /// vertical-speed command flown as a load factor (the fly-by-wire pitch stick is a g command) and a PI throttle. The outer loops turn
    /// the slot error into a heading, a vertical speed and a speed: sideways error becomes a relative sideways speed (first order), height
    /// error a relative climb rate, along-track error a closing speed that follows a braking profile (never closing faster than the
    /// receiver can stop in). The leader's own climb rate and heading are fed forward.
    /// </summary>
    internal sealed class SkywellHold
    {
        /// <summary>What the loop reads from the receiver, in world axes (the runtime measures these from its body).</summary>
        public struct FlightState
        {
            public Vector3 Pos, Vel;
            public float BankDeg;      // + rolled right
            public float RollRateDeg;  // + rolling right, deg/s
            public float Nz;           // measured load factor along the aircraft's up axis (g)
            public float Dt;
        }

        public struct Command
        {
            public float Roll, Pitch, Throttle;     // stick as the game reads it: + rolls right, + pitches the nose DOWN
            public float BankTarget, VsTarget, SpeedTarget; // what the loops are flying to (diagnostics)
        }

        // inner loops (the sim's ManeuverPilot values)
        public const float BankGain = 0.035f, RollDamping = 0.012f, BankPerHeadingDeg = 2f;
        public const float NzFromVs = 0.02f, NzGain = 0.25f, NzIntegral = 0.1f, NzFeedforward = 1f / 7f;
        // Speed: a lever that covers its whole range within 8 m/s of error around a trim that may sit anywhere on it. A jet at a tanker's
        // speed trims near 0.1, and the game opens the airbrakes only on a fully closed throttle: a loop biased to cruise power (the sim
        // leader's 0.6 with a +-0.4 integral) is 4 to 12 m/s too fast before it brakes at all, and runs under the tanker.
        public const float SpeedGain = 0.12f, SpeedIntegral = 0.02f, BaseThrottle = 0.3f, MinTrim = 0.05f, MaxTrim = 0.95f, MaxStartTrim = 0.4f;
        // outer loops
        public const float LateralTau = 8f, MaxLateralSpeed = 14f, MaxHeadingOffsetDeg = 25f;
        public const float VerticalTau = 8f, MaxClimbCorrection = 7f;
        // The braking profile asks for at most 2 x Decel (where its two branches meet, 156 m out): 1 m/s^2 of the 1.8 a closed throttle sheds.
        public const float AlongGain = 0.08f, MaxCatchUp = 25f, MaxDropBack = 6f, Decel = 0.5f;
        public const float MaxBank = 30f, SlowBank = 15f;

        private float nzI, trim;
        private bool started;

        public Command Step(in FlightState s, Vector3 slot, Vector3 leaderVel, float startThrottle = BaseThrottle, float floorSpeed = 0f)
        {
            float speed = s.Vel.magnitude, leaderSpeed = leaderVel.magnitude;
            var leaderH = new Vector3(leaderVel.x, 0f, leaderVel.z);
            float leaderHSpeed = leaderH.magnitude;
            var velH = new Vector3(s.Vel.x, 0f, s.Vel.z);
            Vector3 trackH = leaderHSpeed > 1f ? leaderH / leaderHSpeed : velH.sqrMagnitude > 1f ? velH.normalized : Vector3.forward;
            float heading = Mathf.Atan2(velH.x, velH.z) * Mathf.Rad2Deg;
            float trackHeading = Mathf.Atan2(trackH.x, trackH.z) * Mathf.Rad2Deg;

            Vector3 err = slot - s.Pos;
            var errH = new Vector3(err.x, 0f, err.z);
            var right = new Vector3(trackH.z, 0f, -trackH.x);
            float along = Vector3.Dot(errH, trackH);
            float lateral = Vector3.Dot(errH, right);           // + the slot is to the right of the track
            float tanG = leaderHSpeed > 1f ? leaderVel.y / leaderHSpeed : 0f;
            float vert = err.y - along * tanG;                  // height error against the leader's own flight line
            float v = Mathf.Max(speed, 60f);

            // ---- speed
            float closing = Mathf.Clamp(along * AlongGain, -MaxDropBack, MaxCatchUp);
            if (along > 0f) closing = Mathf.Min(closing, Mathf.Sqrt(2f * Decel * along)); // never closing faster than it can stop in
            float wanted = Mathf.Max(leaderSpeed + closing, floorSpeed);
            float speedErr = wanted - speed;
            if (!started)
            {
                started = true;
                // The pilot's own setting is the first guess of the trim, capped: an AI taken over at full power would otherwise hold 7 m/s
                // too much until the trim unwinds. ponytail: a fixed cap; estimate the trim from speed and drag if a heavy receiver needs more.
                trim = Mathf.Clamp(startThrottle, MinTrim, MaxStartTrim);
            }
            float throttle = Mathf.Clamp01(trim + SpeedGain * speedErr);
            // the trim learns only while the lever is off its stops: a long catch-up or a long brake must not wind it up
            if (throttle > 0f && throttle < 1f) trim = Mathf.Clamp(trim + SpeedIntegral * speedErr * s.Dt, MinTrim, MaxTrim);

            // ---- heading -> bank
            float sideways = Mathf.Clamp(lateral / LateralTau, -MaxLateralSpeed, MaxLateralSpeed);
            float offset = Mathf.Clamp(Mathf.Atan2(sideways, v) * Mathf.Rad2Deg, -MaxHeadingOffsetDeg, MaxHeadingOffsetDeg);
            float headingError = Mathf.DeltaAngle(heading, trackHeading + offset);
            float bankLimit = speed < floorSpeed * 1.15f ? SlowBank : MaxBank;
            float bankTarget = Mathf.Clamp(BankPerHeadingDeg * headingError, -bankLimit, bankLimit);
            float roll = Mathf.Clamp(BankGain * Mathf.DeltaAngle(s.BankDeg, bankTarget) - RollDamping * s.RollRateDeg, -1f, 1f);

            // ---- height -> vertical speed -> load factor
            float vsTarget = leaderVel.y + Mathf.Clamp(vert / VerticalTau, -MaxClimbCorrection, MaxClimbCorrection);
            float cosBank = Mathf.Max(0.3f, Mathf.Cos(bankTarget * Mathf.Deg2Rad));
            float nzCmd = Mathf.Clamp(1f / cosBank + NzFromVs * (vsTarget - s.Vel.y), -1f, 5f);
            float nzErr = nzCmd - s.Nz;
            nzI = Mathf.Clamp(nzI + NzIntegral * nzErr * s.Dt, -0.5f, 0.5f);
            float noseUp = Mathf.Clamp(NzFeedforward * (nzCmd - Mathf.Cos(s.BankDeg * Mathf.Deg2Rad)) + NzGain * nzErr + nzI, -1f, 1f);

            return new Command { Roll = roll, Pitch = -noseUp, Throttle = throttle, BankTarget = bankTarget, VsTarget = vsTarget, SpeedTarget = wanted };
        }
    }
}
