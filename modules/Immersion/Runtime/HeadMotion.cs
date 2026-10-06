using BoscaliSummer.Modules.Immersion.Domain;
using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Runtime
{
    /// <summary>
    /// Rotational head movement in the cockpit.
    /// Measures specific force and angular velocity on the cockpit rigidbody in FixedUpdate,
    /// runs a critically damped spring per axis in Update, and outputs a local rotation offset.
    /// </summary>
    internal sealed class HeadMotion
    {
        private const float MaxForceG = 15f;
        private const float FilterSeconds = 0.08f;

        private Rigidbody tracked;
        private Vector3 lastVelocity;
        private Vector3 lastGlobalPosition;
        private bool primed;
        private Vector3 force = Vector3.up;
        private Vector3 rateDeg;
        private float pitch, pitchVel, yaw, yawVel, roll, rollVel;

        private Vector3 debugForce;
        private float debugUntil = -1f;
        public bool Discontinuity { get; private set; }

        public Quaternion Offset { get; private set; } = Quaternion.identity;
        public Vector3 ForceG => force;
        public Vector3 AnglesDeg => new Vector3(pitch, yaw, roll);

        public void Measure(Aircraft aircraft, float dt)
        {
            Discontinuity = false;
            Rigidbody rb = aircraft != null ? aircraft.CockpitRB() : null;
            if (rb == null || dt <= 0f)
            {
                tracked = null;
                primed = false;
                force = Vector3.up;
                rateDeg = Vector3.zero;
                return;
            }
            if (rb != tracked)
            {
                tracked = rb;
                primed = false;
                force = Vector3.up;
                rateDeg = Vector3.zero;
            }

            Vector3 velocity = rb.velocity;
            GlobalPosition global = rb.transform.GlobalPosition();
            Vector3 position = new Vector3(global.x, global.y, global.z);
            if (!primed)
            {
                lastVelocity = velocity;
                lastGlobalPosition = position;
                primed = true;
                return;
            }
            bool relocated = (position - (lastGlobalPosition + lastVelocity * dt)).sqrMagnitude > 250f * 250f;
            lastGlobalPosition = position;
            Vector3 accel = (velocity - lastVelocity) / dt;
            lastVelocity = velocity;

            Transform frame = rb.transform;
            Vector3 specific = frame.InverseTransformDirection(accel - Physics.gravity) / 9.81f;
            if (relocated || specific.magnitude > 100f || float.IsNaN(specific.sqrMagnitude))
            {
                force = Vector3.up;
                rateDeg = Vector3.zero;
                pitch = pitchVel = yaw = yawVel = roll = rollVel = 0f;
                Offset = Quaternion.identity;
                Discontinuity = true;
                return;
            }
            specific = Vector3.ClampMagnitude(specific, MaxForceG);
            Vector3 rates = frame.InverseTransformDirection(rb.angularVelocity) * Mathf.Rad2Deg;

            float k = Mathf.Clamp01(dt / FilterSeconds);
            force = Vector3.Lerp(force, specific, k);
            rateDeg = Vector3.Lerp(rateDeg, rates, k);
        }

        public void Step(float dt, float strength, bool active, float pilotStrain = 0f, bool comfort = false)
        {
            float tp = 0f, ty = 0f, tr = 0f;
            if (active)
            {
                Vector3 f = Time.unscaledTime < debugUntil ? debugForce : force;
                (tp, ty, tr) = ImmersionMath.HeadTarget(f.x, f.y, f.z, -rateDeg.z, rateDeg.y, strength);
                // Final event + weather composition is clamped by the manager.
                tp *= 0.28f;
                ty *= 0.28f;
                tr *= 0.28f;
                if (!comfort)
                    tp += ImmersionMath.BreathingOffset(Time.time, 13f + pilotStrain * 9f, 0.06f) * strength;
            }
            (pitch, pitchVel) = ImmersionMath.SpringStep(pitch, pitchVel, tp, dt);
            (yaw, yawVel) = ImmersionMath.SpringStep(yaw, yawVel, ty, dt);
            (roll, rollVel) = ImmersionMath.SpringStep(roll, rollVel, tr, dt);
            Offset = Quaternion.Euler(pitch, yaw, -roll);
        }

        public void Reset()
        {
            tracked = null;
            lastGlobalPosition = Vector3.zero;
            primed = false;
            force = Vector3.up;
            rateDeg = Vector3.zero;
            pitch = pitchVel = yaw = yawVel = roll = rollVel = 0f;
            Offset = Quaternion.identity;
            Discontinuity = false;
            debugUntil = -1f;
            debugForce = Vector3.zero;
        }
    }
}
