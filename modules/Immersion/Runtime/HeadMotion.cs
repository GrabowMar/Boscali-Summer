using BoscaliSummer.Features.Immersion.Domain;
using UnityEngine;

namespace BoscaliSummer.Features.Immersion.Runtime
{
    /// <summary>
    /// Rotational head movement in the cockpit. The game already translates the eye point on a
    /// spring (<c>CameraCockpitState.camRelativePos</c>) but never rotates it. This measures the
    /// specific force and body rates on the cockpit rigidbody in FixedUpdate, runs a damped spring
    /// per axis in Update (smooth at any frame rate), and exposes the result as a local rotation
    /// that <c>CockpitCameraPatches</c> writes onto the camera pivot, which vanilla leaves at identity.
    /// </summary>
    internal sealed class HeadMotion
    {
        /// <summary>Anything above this is a respawn, teleport or physics glitch, not a manoeuvre.</summary>
        private const float MaxForceG = 15f;
        private const float FilterSeconds = 0.08f;

        private Rigidbody tracked;
        private Vector3 lastVelocity;
        private bool primed;
        private Vector3 force = Vector3.up;
        private Vector3 rateDeg;
        private float pitch, pitchVel, yaw, yawVel, roll, rollVel;

        // Automation only: pretend a specific force for a few seconds.
        private Vector3 debugForce;
        private float debugUntil = -1f;

        public Quaternion Offset { get; private set; } = Quaternion.identity;
        public Vector3 ForceG => force;
        public Vector3 AnglesDeg => new Vector3(pitch, yaw, roll);

        public void DebugForce(Vector3 forceG, float seconds)
        {
            debugForce = forceG;
            debugUntil = Time.unscaledTime + seconds;
        }

        /// <summary>FixedUpdate: sample the cockpit rigidbody.</summary>
        public void Measure(Aircraft aircraft, float dt)
        {
            Rigidbody rb = aircraft != null ? aircraft.CockpitRB() : null;
            if (rb == null || dt <= 0f)
            {
                tracked = null;
                primed = false;
                return;
            }
            if (rb != tracked)
            {
                tracked = rb;
                primed = false;
            }

            Vector3 velocity = rb.velocity;
            if (!primed)
            {
                lastVelocity = velocity;
                primed = true;
                return;
            }
            Vector3 accel = (velocity - lastVelocity) / dt;
            lastVelocity = velocity;

            Transform frame = rb.transform;
            Vector3 specific = frame.InverseTransformDirection(accel - Physics.gravity) / 9.81f;
            if (specific.magnitude > MaxForceG) return;
            Vector3 rates = frame.InverseTransformDirection(rb.angularVelocity) * Mathf.Rad2Deg;

            float k = Mathf.Clamp01(dt / FilterSeconds);
            force = Vector3.Lerp(force, specific, k);
            rateDeg = Vector3.Lerp(rateDeg, rates, k);
        }

        /// <summary>Update: move the head towards where the forces put it (or back to centre).</summary>
        public void Step(float dt, float strength, bool active)
        {
            float tp = 0f, ty = 0f, tr = 0f;
            if (active)
            {
                Vector3 f = Time.unscaledTime < debugUntil ? debugForce : force;
                // Unity rolls counter-clockwise about +z, so a right roll is a negative z rate.
                (tp, ty, tr) = ImmersionMath.HeadTarget(f.x, f.y, f.z, -rateDeg.z, rateDeg.y, strength);
            }
            (pitch, pitchVel) = ImmersionMath.SpringStep(pitch, pitchVel, tp, dt);
            (yaw, yawVel) = ImmersionMath.SpringStep(yaw, yawVel, ty, dt);
            (roll, rollVel) = ImmersionMath.SpringStep(roll, rollVel, tr, dt);
            // +roll means "tilt right", which is a negative Euler z.
            Offset = Quaternion.Euler(pitch, yaw, -roll);
        }

        public void Reset()
        {
            tracked = null;
            primed = false;
            force = Vector3.up;
            rateDeg = Vector3.zero;
            pitch = pitchVel = yaw = yawVel = roll = rollVel = 0f;
            Offset = Quaternion.identity;
        }
    }
}
