using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Game-logic flight in the BOTE manner (its PlayerShipPropulsion turns inputs into forces on the native Aircraft
    /// rigidbody where LocalSim is true). Stick and rudder command a turn rate, pitch a climb rate, throttle a speed; neutral
    /// stick holds heading and altitude. Lost engine pods cut speed and climb; a disabled carrier stops flying and falls.
    /// Lift jets (CL-1201 style, details/detail_lift.py) run while the gear is down: they carry about 55% of the weight, so
    /// the wing needs a third less speed (minimum 70 -> 45 m/s) and the carrier can climb and sink faster near the ground.
    /// Their intake doors and exhaust vanes open and the nozzles glow on every peer.</summary>
    internal sealed class MegaplaneFlight : MonoBehaviour
    {
        internal const float MinSpeed = 70f, MaxSpeed = 170f;          // m/s
        private const float MaxTurn = 3f, MaxBank = 25f, MaxClimb = 12f;   // deg/s, deg, m/s
        private const float SpeedGain = .35f, MaxAccel = 6f;              // 1/s, m/s^2
        private const float AttitudeKp = .8f, AttitudeKd = 1.8f;           // rad/s^2 per rad, per rad/s
        internal const float LiftMinSpeed = 45f, LiftClimb = 20f;         // 70 m/s * sqrt(1 - 0.55)
        private const float LiftSpool = 1f / 4f, DoorOpen = -70f, VaneOpen = 60f;   // art angles: Blender +70 / -60 about X

        private Aircraft aircraft;
        private Rigidbody rb;
        private Transform[] pods;
        private Transform[] liftDoors, liftVanes, liftGlows;
        private Quaternion[] doorRest, vaneRest;
        private bool holding;
        private float heading, altitude;

        internal float Heading => heading;
        internal float HoldAltitude => altitude;
        internal float Thrust { get; private set; } = 1f;
        internal float Lift { get; private set; }   // lift jets 0 off .. 1 full
        internal int LiftBanks => liftGlows?.Length ?? 0;

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            rb = GetComponent<Rigidbody>();
            Transform[] all = GetComponentsInChildren<Transform>(true);
            pods = all.Where(t => t.name.StartsWith("Pod_") && !t.name.EndsWith("_Nozzle")).ToArray();
            liftDoors = all.Where(t => t.name.StartsWith("LiftDoor_")).ToArray();
            liftVanes = all.Where(t => t.name.StartsWith("LiftVane_")).ToArray();
            liftGlows = all.Where(t => t.name.StartsWith("LiftGlow_")).ToArray();
            doorRest = liftDoors.Select(t => t.localRotation).ToArray();
            vaneRest = liftVanes.Select(t => t.localRotation).ToArray();
            foreach (Transform g in liftGlows) g.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (aircraft == null) return;
            bool on = aircraft.NetworkgearDeployed && !aircraft.disabled && LiftBanks > 0;
            Lift = Mathf.MoveTowards(Lift, on ? 1f : 0f, LiftSpool * Time.deltaTime);
            float open = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(Lift * 3f));   // doors lead the spool-up
            for (int i = 0; i < liftDoors.Length; i++) if (liftDoors[i] != null) liftDoors[i].localRotation = doorRest[i] * Quaternion.AngleAxis(DoorOpen * open, Vector3.right);
            for (int i = 0; i < liftVanes.Length; i++) if (liftVanes[i] != null) liftVanes[i].localRotation = vaneRest[i] * Quaternion.AngleAxis(VaneOpen * open, Vector3.right);
            bool glow = Lift > .5f;
            foreach (Transform g in liftGlows) if (g != null && g.gameObject.activeSelf != glow) g.gameObject.SetActive(glow);
        }

        private void FixedUpdate()
        {
            if (aircraft == null || rb == null || !aircraft.LocalSim || rb.isKinematic) { holding = false; return; }
            if (aircraft.disabled) return;   // nothing holds it up any more
            // Native fuel burn (UnitPart.ModifyMass) writes one tank part's mass into the shared body; keep the carrier's.
            if (rb.mass != aircraft.definition.mass) rb.mass = aircraft.definition.mass;
            if (!holding)
            {
                heading = transform.eulerAngles.y;
                altitude = aircraft.GlobalPosition().y;
                holding = true;
            }
            // A pod that left with its damage island no longer pushes.
            int alive = 0;
            foreach (Transform p in pods) if (p != null && p.IsChildOf(transform)) alive++;
            Thrust = pods.Length == 0 ? 1f : (float)alive / pods.Length;

            ControlInputs input = aircraft.GetInputs();
            float dt = Time.fixedDeltaTime;
            float climbAuthority = Mathf.Clamp01(Thrust * 2f - .5f);   // under a quarter of the pods it can only sink
            float lift = Lift * Thrust;   // the jets share the pods' bleed: lost pods, weaker lift
            float maxClimb = Mathf.Lerp(MaxClimb, LiftClimb, lift), minSpeed = Mathf.Lerp(MinSpeed, LiftMinSpeed, lift);
            if (Mathf.Abs(input.pitch) > .05f) altitude += -input.pitch * maxClimb * climbAuthority * dt;
            if (Thrust < .25f) altitude -= 6f * dt;
            float speed = Mathf.Lerp(minSpeed, MaxSpeed * (.5f + .5f * Thrust), Mathf.Clamp01(input.throttle));
            // The heading may only run as fast as the flight path can follow (v * omega within most of MaxAccel), or it crabs.
            float maxTurn = Mathf.Min(MaxTurn, .7f * MaxAccel / Mathf.Max(speed, 1f) * Mathf.Rad2Deg);
            float turn = Mathf.Clamp(input.roll + .5f * input.yaw, -1f, 1f) * maxTurn;
            heading = Mathf.Repeat(heading + turn * dt, 360f);

            float climb = Mathf.Clamp((altitude - aircraft.GlobalPosition().y) * .15f, -maxClimb, maxClimb * climbAuthority);
            Vector3 flat = Quaternion.Euler(0, heading, 0) * Vector3.forward;
            Vector3 target = flat * speed + Vector3.up * climb;
            Vector3 accel = Vector3.ClampMagnitude((target - rb.velocity) * SpeedGain, MaxAccel);
            rb.AddForce(accel - Physics.gravity, ForceMode.Acceleration);

            // Attitude follows the motion: nose along the flight path, banked into the turn.
            float bank = Mathf.Clamp(Mathf.Atan(speed * turn * Mathf.Deg2Rad / 9.81f) * Mathf.Rad2Deg, -MaxBank, MaxBank);
            float pitch = -Mathf.Asin(Mathf.Clamp(climb / Mathf.Max(speed, 1f), -1f, 1f)) * Mathf.Rad2Deg;
            Quaternion error = Quaternion.Euler(pitch, heading, -bank) * Quaternion.Inverse(rb.rotation);
            error.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            Vector3 torque = float.IsNaN(axis.x) ? Vector3.zero : axis * (angle * Mathf.Deg2Rad * AttitudeKp);
            rb.AddTorque(torque - rb.angularVelocity * AttitudeKd, ForceMode.Acceleration);
        }
    }
}
