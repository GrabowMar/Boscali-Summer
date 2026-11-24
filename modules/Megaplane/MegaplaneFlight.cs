using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Game-logic flight in the BOTE manner (its PlayerShipPropulsion turns inputs into forces on the native Aircraft
    /// rigidbody where LocalSim is true). Flown like a big transport: the stick sets a bank (to 40 deg) that it rolls into at
    /// about 12 deg/s and turns from, pitch sets a climb rate reached in a few seconds, throttle a speed it takes under a
    /// minute to reach; it flies nose high when slow and sways a little in the air. Neutral stick levels the wings and holds
    /// altitude. Damage (<see cref="MegaplaneDamage"/>) shows: a weak wing rolls it toward that side and sinks it, lost pods
    /// cost speed and yaw it, a lost tailplane porpoises, lost fins fishtail and wander. A disabled carrier glides down in a long burning dive.
    /// Lift jets (CL-1201 style, details/detail_lift.py) run while the gear is down: they carry about 55% of the weight, so
    /// the wing needs a third less speed (minimum 70 -> 45 m/s) and the carrier can climb and sink faster near the ground.
    /// Their intake doors and exhaust vanes open and the nozzles glow on every peer.</summary>
    internal sealed class MegaplaneFlight : MonoBehaviour
    {
        internal const float MinSpeed = 70f, MaxSpeed = 170f;          // m/s
        // Big-transport numbers (C-5 / An-225 class): 40 deg bank, ~12 deg/s roll, 1.3 g turns, 20 m/s climb. Heavy, not sluggish.
        private const float MaxBank = 40f, MaxClimb = 20f;                // deg, m/s
        private const float RollRate = 12f, RollAccel = 14f, ClimbRamp = 4f;    // deg/s, deg/s^2, m/s^2
        private const float SpeedGain = .35f, Accel = 2.2f, Decel = 1.6f; // 1/s, m/s^2 along the path (about 45 s from slow to full)
        private const float VertGain = .9f, VertAccel = 4f, SideGain = 1f, LateralAccel = 8.5f;   // 1/s, m/s^2 (8.5 = 40 deg bank)
        private const float AttitudeKp = 1.6f, AttitudeKd = 2.5f;         // rad/s^2 per rad, per rad/s (about critically damped)
        private const float RudderRate = 2.5f;                            // deg/s of flat turn from full rudder
        private const float CrashFade = 30f, SinkDrag = 9.81f / 3600f;   // s, lift gone; sink settles near 60 m/s
        internal const float LiftMinSpeed = 45f, LiftClimb = 20f;         // 70 m/s * sqrt(1 - 0.55)
        private const float LiftSpool = 1f / 4f, DoorOpen = -70f, VaneOpen = 60f;   // art angles: Blender +70 / -60 about X

        private Aircraft aircraft;
        private Rigidbody rb;
        private AeroPart[] podOwners;
        private bool[] portPod;
        private MegaplaneNoseFan[] noseFans;
        private Transform[] liftDoors, liftVanes, liftGlows;
        private Quaternion[] doorRest, vaneRest;
        private MegaplaneDamage damage;
        private MegaplaneReactor reactor;
        private bool holding;
        private float heading, altitude, bank, rollRate, climbCmd, crash;

        internal float Heading => heading;
        internal float HoldAltitude => altitude;
        internal float Thrust { get; private set; } = 1f;
        internal static float PodHealth(AeroPart owner) => owner == null || owner.IsDetached() ? 0f : Mathf.Clamp01(owner.hitPoints * .01f);
        internal float Lift { get; private set; }   // lift jets 0 off .. 1 full
        internal int LiftBanks => liftGlows?.Length ?? 0;
        internal float NoseAssist { get; private set; }
        internal float ForwardSpeed { get; private set; }
        private const float AssistPitch = 3f, AssistClimb = 3f;
        internal MegaplaneNoseFan[] NoseFans => noseFans;

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            rb = GetComponent<Rigidbody>();
            reactor = GetComponent<MegaplaneReactor>();
            noseFans = GetComponentsInChildren<MegaplaneNoseFan>(true);
            Transform[] all = GetComponentsInChildren<Transform>(true);
            var pods = all.Where(t => t.name.StartsWith("Pod_") && !t.name.EndsWith("_Nozzle")).ToArray();
            podOwners = pods.Select(p => p.GetComponentInParent<AeroPart>(true)).ToArray();
            portPod = pods.Select(p => p.name.StartsWith("Pod_L")).ToArray();
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
            // The native aircraft synchronizes gear, pilot axes, velocity and part damage to observers.
            // Horizontal forward travel keeps the fans off while parked, reversing, or falling vertically.
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            ForwardSpeed = Vector3.Dot(rb.velocity, forward);
            ControlInputs input = aircraft.GetInputs();
            float speedWindow = Mathf.InverseLerp(30f, 45f, ForwardSpeed) * Mathf.InverseLerp(107f, 78f, ForwardSpeed);
            float demand = !aircraft.disabled && aircraft.NetworkgearDeployed
                ? speedWindow * Mathf.InverseLerp(.16f, .57f, input.throttle) * Mathf.InverseLerp(.08f, .55f, -input.pitch)
                : 0f;
            NoseAssist = 0f;
            foreach (MegaplaneNoseFan fan in noseFans)
            {
                if (fan == null) continue;
                fan.Step(demand, Time.deltaTime);
                NoseAssist += .5f * fan.Power;
            }
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
            float left = 0f, right = 0f;
            int podsL = 0, podsR = 0;
            for (int i = 0; i < podOwners.Length; i++)
            {
                if (portPod[i]) { podsL++; left += PodHealth(podOwners[i]); }
                else { podsR++; right += PodHealth(podOwners[i]); }
            }
            Thrust = podOwners.Length == 0 ? 1f : (left + right) / podOwners.Length;
            if (reactor != null) Thrust *= reactor.AvailablePower;
            float asymThrust = (podsL > 0 ? left / podsL : 1f) - (podsR > 0 ? right / podsR : 1f);

            ControlInputs input = aircraft.GetInputs();
            float wing = (wingL + wingR) * .5f;
            float climbAuthority = Mathf.Clamp01(Thrust * 2f - .5f) * wing * wing * (.3f + .7f * pitchAuth);   // under a quarter of the pods it can only sink
            float lift = Lift * Thrust;   // the jets share the pods' bleed: lost pods, weaker lift
            float maxClimb = Mathf.Lerp(MaxClimb, LiftClimb, lift);
            float minSpeed = Mathf.Lerp(MinSpeed, LiftMinSpeed, lift) / Mathf.Sqrt(Mathf.Max(wing, .3f));
            if (Thrust < .5f) altitude -= (.5f - Thrust) * 24f * dt;   // under half the pods it cannot hold height
            if (wing < .98f) altitude -= (.98f - wing) * 45f * dt;     // every lost panel costs lift: it sinks until the pilot trades speed

            // Pitch: the stick sets a climb rate, reached in a few seconds. A lost tailplane makes the stick vague.
            float noseLift = NoseAssist * climbAuthority;
            float climbTarget = Mathf.Abs(input.pitch) > .05f ? -input.pitch * maxClimb * climbAuthority : 0f;
            if (input.pitch < -.05f) climbTarget += noseLift * AssistClimb;
            climbCmd = Mathf.MoveTowards(climbCmd, climbTarget, ClimbRamp * dt);
            altitude += climbCmd * dt;

            // Roll: the stick sets a bank angle; the carrier rolls into it at a few degrees a second and turns from the bank.
            // A weak wing drops (list) and drags it into a turn the pilot has to hold off; a lost fin lets it wallow.
            // Lift lost on one side rolls it hard toward that side (an outer panel ~10 deg, an inner wing far more).
            float list = Mathf.Clamp((wingL - wingR) * 140f, -60f, 60f);
            // No fins: directionally unstable, the tail wanders and drags the heading with it (Dutch roll, growing fishtail).
            float wallow = (1f - yawAuth) * 10f * Sway(31f) + (1f - yawAuth) * 3f * Mathf.Sin(Time.time * 2f * Mathf.PI * .15f);
            float rollAuth = .4f + .6f * Mathf.Min(wingL, wingR);
            float targetBank = Mathf.Clamp(input.roll, -1f, 1f) * MaxBank * rollAuth + list + wallow;
            float rate = RollRate * rollAuth;
            rollRate = Mathf.MoveTowards(rollRate, Mathf.Clamp((targetBank - bank) * 1.2f, -rate, rate), RollAccel * dt);
            bank += rollRate * dt;
            float turn = 9.81f * Mathf.Tan(bank * Mathf.Deg2Rad) / flatSpeed * Mathf.Rad2Deg;
            turn += (input.yaw * RudderRate * yawAuth + asymThrust * 2.5f + (1f - yawAuth) * 2f * Sway(53f)) * Mathf.Clamp01(flatSpeed / 60f);
            // The heading may only run as fast as the flight path can follow (v * omega within the lateral limit), or it crabs.
            float maxTurn = LateralAccel / flatSpeed * Mathf.Rad2Deg;
            turn = Mathf.Clamp(turn, -maxTurn, maxTurn);
            heading = Mathf.Repeat(heading + turn * dt, 360f);

            float topSpeed = MaxSpeed * (.3f + .7f * Thrust) * (.88f + .12f * yawAuth);   // fewer pods, less speed; a sideslipping hull drags
            float speed = Mathf.Lerp(minSpeed, Mathf.Max(topSpeed, minSpeed), Mathf.Clamp01(input.throttle));
            float climb = Mathf.Clamp(climbCmd + (altitude - aircraft.GlobalPosition().y) * .1f, -maxClimb, maxClimb * Mathf.Max(climbAuthority, .05f) + noseLift * AssistClimb);
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
            float porpoise = (1f - pitchAuth) * 7f * Mathf.Sin(Time.time * 2f * Mathf.PI * .07f);
            float slip = (1f - yawAuth) * 9f * Sway(71f) + asymThrust * 4f;   // nose off the path: fishtail without fins, crab on asymmetric thrust
            float pitch = -Mathf.Asin(Mathf.Clamp(v.y / Mathf.Max(v.magnitude, 1f), -1f, 1f)) * Mathf.Rad2Deg - Mathf.Lerp(1f, 5f, slow) + porpoise;
            Steer(pitch - noseLift * AssistPitch + Sway(0f) * .3f, heading + slip, bank + Sway(7f) * .6f, AttitudeKp, AttitudeKd);
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
