using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Game-logic flight in the BOTE manner (its PlayerShipPropulsion turns inputs into forces on the native Aircraft
    /// rigidbody where LocalSim is true). Flown like the 6,500 t it is: the stick sets a bank that the carrier rolls into at a
    /// few degrees a second and turns from, pitch sets a climb rate it reaches over seconds, throttle a speed it takes over a
    /// minute to reach; it flies nose high when slow and sways a little in the air. Neutral stick levels the wings and holds
    /// altitude. Damage (<see cref="MegaplaneDamage"/>) shows: a weak wing drops it into a turn and sinks it, lost pods on one
    /// side yaw it, a lost tailplane porpoises, a lost fin wallows. A disabled carrier glides down in a long burning dive.
    /// Lift jets (CL-1201 style, details/detail_lift.py) run while the gear is down: they carry about 55% of the weight, so
    /// the wing needs a third less speed (minimum 70 -> 45 m/s) and the carrier can climb and sink faster near the ground.
    /// Their intake doors and exhaust vanes open and the nozzles glow on every peer.</summary>
    internal sealed class MegaplaneFlight : MonoBehaviour
    {
        internal const float MinSpeed = 70f, MaxSpeed = 170f;          // m/s
        private const float MaxBank = 25f, MaxClimb = 12f;                // deg, m/s
        private const float RollRate = 3.5f, RollAccel = 2f, ClimbRamp = .8f;   // deg/s, deg/s^2, m/s^2: it leans in, it does not snap
        private const float SpeedGain = .25f, Accel = 1.2f, Decel = .8f;  // 1/s, m/s^2 along the path (about 80 s from slow to full)
        private const float VertGain = .4f, VertAccel = 1.5f, SideGain = .5f, LateralAccel = 4.2f;   // 1/s, m/s^2
        private const float AttitudeKp = .35f, AttitudeKd = 1.2f;         // rad/s^2 per rad, per rad/s (critically damped, slow)
        private const float CrashFade = 30f, SinkDrag = 9.81f / 3600f;   // s, lift gone; sink settles near 60 m/s
        internal const float LiftMinSpeed = 45f, LiftClimb = 20f;         // 70 m/s * sqrt(1 - 0.55)
        private const float LiftSpool = 1f / 4f, DoorOpen = -70f, VaneOpen = 60f;   // art angles: Blender +70 / -60 about X

        private Aircraft aircraft;
        private Rigidbody rb;
        private Transform[] pods;
        private Transform[] liftDoors, liftVanes, liftGlows;
        private Quaternion[] doorRest, vaneRest;
        private MegaplaneDamage damage;
        private bool holding;
        private float heading, altitude, bank, rollRate, climbCmd, crash;

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
            if (damage == null) damage = GetComponent<MegaplaneDamage>();
            // Native fuel burn (UnitPart.ModifyMass) writes one tank part's mass into the shared body; keep the carrier's.
            if (rb.mass != aircraft.definition.mass) rb.mass = aircraft.definition.mass;
            float dt = Time.fixedDeltaTime;
            float wingL = damage != null ? damage.LiftL : 1f, wingR = damage != null ? damage.LiftR : 1f;
            float pitchAuth = damage != null ? damage.PitchAuthority : 1f, yawAuth = damage != null ? damage.YawAuthority : 1f;
            Vector3 v = rb.velocity;
            float flatSpeed = Mathf.Max(new Vector2(v.x, v.z).magnitude, 30f);
            if (aircraft.disabled) { Crash(dt, wingL, wingR, flatSpeed); return; }
            crash = 0f;
            if (!holding)
            {
                heading = transform.eulerAngles.y;
                altitude = aircraft.GlobalPosition().y;
                bank = Mathf.DeltaAngle(0f, -transform.eulerAngles.z);
                holding = true;
            }
            // A pod that left with its damage island no longer pushes.
            int alive = 0, left = 0, right = 0, podsL = 0, podsR = 0;
            foreach (Transform p in pods)
            {
                bool isL = p != null && p.name.StartsWith("Pod_L");
                if (isL) podsL++; else podsR++;
                if (p == null || !p.IsChildOf(transform)) continue;
                alive++;
                if (isL) left++; else right++;
            }
            Thrust = pods.Length == 0 ? 1f : (float)alive / pods.Length;
            float asymThrust = (podsL > 0 ? (float)left / podsL : 1f) - (podsR > 0 ? (float)right / podsR : 1f);

            ControlInputs input = aircraft.GetInputs();
            float wing = (wingL + wingR) * .5f;
            float climbAuthority = Mathf.Clamp01(Thrust * 2f - .5f) * wing * wing * (.3f + .7f * pitchAuth);   // under a quarter of the pods it can only sink
            float lift = Lift * Thrust;   // the jets share the pods' bleed: lost pods, weaker lift
            float maxClimb = Mathf.Lerp(MaxClimb, LiftClimb, lift);
            float minSpeed = Mathf.Lerp(MinSpeed, LiftMinSpeed, lift) / Mathf.Sqrt(Mathf.Max(wing, .3f));
            if (Thrust < .25f) altitude -= 6f * dt;
            if (wing < .85f) altitude -= (.85f - wing) * 25f * dt;   // a shot-up wing cannot hold the weight

            // Pitch: the stick sets a climb rate, which a 6,500 t airframe reaches over seconds, not at once.
            float climbTarget = Mathf.Abs(input.pitch) > .05f ? -input.pitch * maxClimb * climbAuthority : 0f;
            climbCmd = Mathf.MoveTowards(climbCmd, climbTarget, ClimbRamp * dt);
            altitude += climbCmd * dt;

            // Roll: the stick sets a bank angle; the carrier rolls into it at a few degrees a second and turns from the bank.
            // A weak wing drops (list) and drags it into a turn the pilot has to hold off; a lost fin lets it wallow.
            float list = Mathf.Clamp((wingL - wingR) * 50f, -20f, 20f);
            float wallow = (1f - yawAuth) * 4f * Mathf.Sin(Time.time * 2f * Mathf.PI * .12f);
            float rollAuth = .5f + .5f * Mathf.Min(wingL, wingR);
            float targetBank = Mathf.Clamp(input.roll, -1f, 1f) * MaxBank * rollAuth + list + wallow;
            rollRate = Mathf.MoveTowards(rollRate, Mathf.Clamp((targetBank - bank) * .6f, -RollRate, RollRate), RollAccel * dt);
            bank += rollRate * dt;
            float turn = 9.81f * Mathf.Tan(bank * Mathf.Deg2Rad) / flatSpeed * Mathf.Rad2Deg;
            turn += (input.yaw * .4f * yawAuth + asymThrust * .5f) * Mathf.Clamp01(flatSpeed / 60f);
            // The heading may only run as fast as the flight path can follow (v * omega within the lateral limit), or it crabs.
            float maxTurn = LateralAccel / flatSpeed * Mathf.Rad2Deg;
            turn = Mathf.Clamp(turn, -maxTurn, maxTurn);
            heading = Mathf.Repeat(heading + turn * dt, 360f);

            float speed = Mathf.Lerp(minSpeed, MaxSpeed * (.5f + .5f * Thrust), Mathf.Clamp01(input.throttle));
            float climb = Mathf.Clamp(climbCmd + (altitude - aircraft.GlobalPosition().y) * .1f, -maxClimb, maxClimb * Mathf.Max(climbAuthority, .05f));
            Vector3 flat = Quaternion.Euler(0, heading, 0) * Vector3.forward;
            Vector3 error = flat * speed + Vector3.up * climb - v;
            // Separate limits along the path (thrust spools, drag bleeds), up/down and sideways: momentum you can feel.
            float along = Mathf.Clamp(Vector3.Dot(error, flat) * SpeedGain, -Decel, Accel * Mathf.Max(Thrust, .2f));
            float up = Mathf.Clamp(error.y * VertGain, -VertAccel, VertAccel);
            Vector3 side = error - flat * Vector3.Dot(error, flat) - Vector3.up * error.y;
            Vector3 accel = flat * along + Vector3.up * up + Vector3.ClampMagnitude(side * SideGain, LateralAccel);
            rb.AddForce(accel - Physics.gravity, ForceMode.Acceleration);

            // Attitude follows the motion: nose along the path (nose high when slow), banked as flown, a slow sway in the air.
            float slow = Mathf.InverseLerp(MaxSpeed, minSpeed, flatSpeed);
            float porpoise = (1f - pitchAuth) * 2.5f * Mathf.Sin(Time.time * 2f * Mathf.PI * .05f);
            float pitch = -Mathf.Asin(Mathf.Clamp(v.y / Mathf.Max(v.magnitude, 1f), -1f, 1f)) * Mathf.Rad2Deg - Mathf.Lerp(1f, 5f, slow) + porpoise;
            Steer(pitch + Sway(0f) * .3f, heading, bank + Sway(7f) * .6f, AttitudeKp, AttitudeKd);
        }

        /// <summary>Disabled: lift fades over half a minute, the nose drops and the weak wing goes down; pieces come off
        /// (MegaplaneDamage, host) on the way. Sink is held near what a falling wing that size reaches, so it is a long dive.</summary>
        private void Crash(float dt, float wingL, float wingR, float flatSpeed)
        {
            crash += dt;
            float support = Mathf.Clamp01(1f - crash / CrashFade) * .85f;
            Vector3 v = rb.velocity;
            rb.AddForce(-Physics.gravity * support + Vector3.up * (v.y < 0f ? v.y * v.y * SinkDrag : 0f), ForceMode.Acceleration);
            float side = wingL < wingR ? -1f : 1f;
            bank = Mathf.MoveTowards(bank, side * Mathf.Min(55f, 5f + crash * 2f), RollRate * dt);
            heading = Mathf.Repeat(heading + Mathf.Clamp(9.81f * Mathf.Tan(bank * Mathf.Deg2Rad) / flatSpeed * Mathf.Rad2Deg, -2f, 2f) * dt, 360f);
            float pitch = Mathf.Lerp(0f, 25f, crash / 20f);   // Unity Euler x: positive = nose down
            Steer(pitch, heading, bank, AttitudeKp * .6f, AttitudeKd * .8f);
        }

        private void Steer(float pitch, float yaw, float roll, float kp, float kd)
        {
            Quaternion error = Quaternion.Euler(pitch, yaw, -roll) * Quaternion.Inverse(rb.rotation);
            error.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) angle -= 360f;
            Vector3 torque = float.IsNaN(axis.x) ? Vector3.zero : axis * (angle * Mathf.Deg2Rad * kp);
            rb.AddTorque(torque - rb.angularVelocity * kd, ForceMode.Acceleration);
        }

        // Air is never still: a slow, small wander (-1..1, scaled to degrees by the caller), different per axis.
        private static float Sway(float seed) => (Mathf.PerlinNoise(Time.time * .07f + seed, seed * 3.1f) - .5f) * 2f;
    }
}
