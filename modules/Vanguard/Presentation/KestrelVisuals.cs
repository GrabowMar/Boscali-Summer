using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    /// <summary>
    /// KS-01 KESTREL on every peer (design/vanguard-kestrel/notes/model.md): the FBX is exported stowed, so after release the
    /// boom runs out, the wing swivels and the port then the starboard panel swing aft, then the prop runs up. Once flying
    /// the sensor turret and eye follow the drone's look point, and while a unit is lased a thin red line runs from the
    /// laser lens to it with a dot on it. Joints are found by name under VanguardVisual; only localPosition / localRotation
    /// are written, about the missile's own axes taken into each parent frame at bind time (the FBX root carries the 100x
    /// scale and a 90 degree pitch, so no axis is hard-coded).
    /// </summary>
    internal sealed class KestrelVisuals : MonoBehaviour
    {
        // Seconds after release: model.md section 9 less the 3 s the sled takes to run out and let go.
        private const float TailFrom = .4f, TailTo = 1.4f, SwivelFrom = .9f, SwivelTo = 2f;
        private const float PortFrom = 1.8f, PortTo = 2.6f, StarboardFrom = 2.2f, StarboardTo = 3f, PropFrom = 2.8f, PropTo = 3.8f;
        private const float TailRun = 1.3f;   // m: the boom still overlaps its hull sleeve by 2.5 cm, never run it further
        private const float PropRate = 3000f; // deg/s at full run-up; a true 25 rev/s would only strobe at frame rate
        // The hull and the keel canoe block the aft hemisphere and the nose blocks high sight; the eye rests 25 deg down.
        private const float YawLimit = 150f, PitchDown = -90f, PitchUp = 20f, RestSight = -25f, YawRate = 120f, PitchRate = 90f;
        private const float BeamWidth = .3f, DotSize = 2.5f, DotLift = 1.2f;
        public const int JointsWanted = 7; // Tail, Prop, Wing, WingOuterL, WingOuterR, Turret, Eye
        private static readonly Vector3 Lens = new Vector3(.026f, .05f, .05f) * .01f; // laser lens in Eye joint units (model.md section 7)
        private static Material beamMaterial;
        private static bool warned; // one rig warning a session, not one a glider

        private Missile missile;
        private KestrelDrone drone;
        private Transform body, tail;
        private Vector3 tailRest, tailRun;
        private RigJoint wing, port, starboard, prop, turret, eye;
        private LineRenderer line, dot;
        private float tailShown, propAngle, yaw, pitch;

        /// <summary>Sim hook: how many of the JointsWanted joints the model has.</summary>
        internal int JointsFound
        {
            get
            {
                int found = tail != null ? 1 : 0;
                foreach (Transform part in new[] { wing.Part, port.Part, starboard.Part, prop.Part, turret.Part, eye.Part }) found += part != null ? 1 : 0;
                return found;
            }
        }

        /// <summary>Sim hook: the worst distance of an unfold joint from its deployed pose, in degrees (the boom in centimetres).</summary>
        internal float DeployError => Mathf.Max(Mathf.Max(wing.Off(90f), port.Off(180f)), Mathf.Max(starboard.Off(-180f),
            tail == null ? 0f : tail.parent.TransformVector(tail.localPosition - tailRest - tailRun).magnitude * 100f));

        internal static KestrelVisuals Attach(Missile missile, KestrelDrone drone)
        {
            KestrelVisuals view = missile.GetComponent<KestrelVisuals>();
            if (view != null) return view;
            view = missile.gameObject.AddComponent<KestrelVisuals>();
            view.Bind(missile, drone);
            return view;
        }

        private void Bind(Missile glider, KestrelDrone brain)
        {
            missile = glider;
            drone = brain;
            body = glider.transform;
            Transform visual = body.Find("VanguardVisual");
            Transform[] parts = visual != null ? visual.GetComponentsInChildren<Transform>(true) : new Transform[0];
            string missing = null;
            wing = RigJoint.Find(parts, "Wing", body.up, ref missing);
            port = RigJoint.Find(parts, "WingOuterL", body.up, ref missing);
            starboard = RigJoint.Find(parts, "WingOuterR", body.up, ref missing);
            prop = RigJoint.Find(parts, "Prop", body.forward, ref missing);
            turret = RigJoint.Find(parts, "Turret", body.up, ref missing);
            eye = RigJoint.Find(parts, "Eye", body.right, ref missing);
            foreach (Transform part in parts)
                if (part.name == "Tail" && part.parent != null)
                {
                    tail = part;
                    tailRest = part.localPosition;
                    tailRun = part.parent.InverseTransformVector(-body.forward * TailRun);
                    break;
                }
            if (tail == null) missing = missing == null ? "Tail" : missing + ", Tail";
            if (missing != null && !warned)
            {
                warned = true;
                Plugin.Logger?.LogWarning("[Vanguard] KESTREL rig has no " + missing + ": those parts stay still");
            }
        }

        private void LateUpdate()
        {
            if (missile == null || missile.disabled)
            {
                if (line != null) line.enabled = dot.enabled = false;
                enabled = false;
                return;
            }
            float age = missile.timeSinceSpawn, dt = Time.deltaTime;
            Unfold(age);
            Spin(age, dt);
            if (age >= PropTo) Aim(dt); // turret and eye are free once it is flying
            Beam();
        }

        private static float Ease(float age, float from, float to) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(from, to, age));

        // Wing +90 and panels +180 / -180 about the hull up (Blender Z -90 / -180 / +180, model.md section 4): both panels
        // sweep aft past the trailing edge, the port one first because its stack layer carries the slot for the starboard fin.
        private void Unfold(float age)
        {
            float slide = Ease(age, TailFrom, TailTo);
            if (tail != null && slide != tailShown)
            {
                tailShown = slide;
                tail.localPosition = tailRest + tailRun * slide;
            }
            wing.Turn(90f * Ease(age, SwivelFrom, SwivelTo));
            port.Turn(180f * Ease(age, PortFrom, PortTo));
            starboard.Turn(-180f * Ease(age, StarboardFrom, StarboardTo));
        }

        private void Spin(float age, float dt)
        {
            float run = Ease(age, PropFrom, PropTo);
            if (run <= 0f) return;
            propAngle = (propAngle + PropRate * run * dt) % 360f;
            prop.Turn(propAngle);
        }

        // Yaw about the hull up (+ = starboard), then pitch (+ = up) about the yawed right; the eye rests 25 deg down, so its
        // own pitch is the wanted sight angle less that. Stepped, not jumped, and never through the blocked aft hemisphere.
        private void Aim(float dt)
        {
            if (turret.Part == null || eye.Part == null) return;
            Vector3 to = drone.LookPoint.ToLocalPosition() - turret.Part.position;
            if (to.sqrMagnitude < 1f) return;
            Vector3 sight = body.InverseTransformDirection(to.normalized);
            float yawWant = Mathf.Clamp(Mathf.Atan2(sight.x, sight.z) * Mathf.Rad2Deg, -YawLimit, YawLimit);
            float pitchWant = Mathf.Clamp(Mathf.Asin(Mathf.Clamp(sight.y, -1f, 1f)) * Mathf.Rad2Deg, PitchDown, PitchUp) - RestSight;
            yaw = Mathf.MoveTowards(yaw, yawWant, YawRate * dt);
            pitch = Mathf.MoveTowards(pitch, pitchWant, PitchRate * dt);
            turret.Turn(yaw);
            eye.Turn(-pitch);
        }

        // Both strips live in the missile's own space, so a floating-origin shift cannot leave them behind.
        private void Beam()
        {
            Unit target = drone.Lased;
            if (target == null || target.disabled || eye.Part == null || GameManager.IsHeadless || (line == null && !MakeBeam()))
            {
                if (line != null) line.enabled = dot.enabled = false;
                return;
            }
            Vector3 from = body.InverseTransformPoint(eye.Part.TransformPoint(Lens));
            Vector3 to = body.InverseTransformPoint(target.transform.position + Vector3.up * DotLift);
            line.SetPosition(0, from);
            line.SetPosition(1, to);
            dot.SetPosition(0, to);
            dot.SetPosition(1, to + new Vector3(.01f, .01f, .01f)); // two near points with round caps draw one disc
            line.enabled = dot.enabled = true;
        }

        private bool MakeBeam()
        {
            if (beamMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) return false;
                // Additive, so the beam reads as a glow over the scene, not an opaque red bar (dim RGB = soft glow).
                beamMaterial = new Material(shader) { color = new Color(.6f, .04f, .03f) };
                LanceFxPlayer.MakeAdditive(beamMaterial);
            }
            line = Strip("KestrelLaser", BeamWidth, 0);
            line.widthCurve = AnimationCurve.Linear(0f, .25f, 1f, 1f); // pencil-thin at the lens, full width at the spot
            dot = Strip("KestrelDot", DotSize, 8);
            return true;
        }

        private LineRenderer Strip(string name, float width, int caps)
        {
            var go = new GameObject(name);
            go.transform.SetParent(body, false);
            LineRenderer strip = go.AddComponent<LineRenderer>();
            strip.useWorldSpace = false;
            strip.positionCount = 2;
            strip.widthMultiplier = width;
            strip.numCapVertices = caps;
            strip.sharedMaterial = beamMaterial;
            strip.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            strip.receiveShadows = false;
            strip.enabled = false;
            return strip;
        }
    }
}
