using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace BoscaliSummer.Modules.Skylark
{
    /// <summary>Animate the art and allocate native fan authority; native components apply all forces and replicate controls.</summary>
    [DefaultExecutionOrder(-100)]
    internal sealed class SkylarkAnimation : MonoBehaviour
    {
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        [SerializeField] private Transform[] ducts, rotors, vanes, airbrakes, airbrakePistons, airbrakeLugs, flaps;
        [SerializeField] private Transform[] canopyBarrels, canopyRods, canopyBasePins, canopyBowPins;
        [SerializeField] private Renderer[] rotorMeshes, rotorBlur, rotorHubs;
        private Quaternion[] ductRest, rotorRest, vaneRest, airbrakeRest;
        private SkylarkHinge[] flapHinges;
        private Canopy primaryCanopy;
        private DuctedThrustSystem propulsion;
        private Aircraft aircraft;
        private Turbofan engine;
        private JetNozzle[] nozzles;
        private object flyByWire;
        private float alphaLimiter;
        private float phase, airbrakeAmount;
        private float lastBlurStrength = -1f;
        private MaterialPropertyBlock blurProperties;
        private Color blurTint;
        private bool balanced;

        internal float AirbrakeAngle => airbrakeAmount * 42f;
        internal float FlapAngle => flapHinges != null && flapHinges.Length > 0 && flapHinges[0] != null ? flapHinges[0].Amount * 24f : 0f;

        internal void Configure(Dictionary<string, Transform> nodes)
        {
            string[] suffixes = { "FL", "FR", "RL", "RR" };
            ducts = new Transform[4]; rotors = new Transform[4]; vanes = new Transform[8];
            rotorMeshes = new Renderer[4]; rotorBlur = new Renderer[4]; rotorHubs = new Renderer[4];
            for (int i = 0; i < 4; i++)
            {
                ducts[i] = nodes["Duct_" + suffixes[i]];
                rotors[i] = nodes["Rotor_" + suffixes[i]];
                rotorMeshes[i] = rotors[i].GetComponent<Renderer>();
                rotorBlur[i] = nodes["Rotor_" + suffixes[i] + "_Blur"].GetComponent<Renderer>();
                rotorHubs[i] = nodes["Rotor_" + suffixes[i] + "_Hub"].GetComponent<Renderer>();
                vanes[i * 2] = nodes["ExitVane_" + suffixes[i] + "_A"];
                vanes[i * 2 + 1] = nodes["ExitVane_" + suffixes[i] + "_B"];
            }
            Transform FindOpt(string k) => nodes.TryGetValue(k, out Transform t) ? t : null;
            airbrakes = new[] { FindOpt("Airbrake_L"), FindOpt("Airbrake_R") };
            airbrakePistons = new[] { FindOpt("AirbrakePiston_L"), FindOpt("AirbrakePiston_R") };
            airbrakeLugs = new[] { FindOpt("AirbrakeLug_L"), FindOpt("AirbrakeLug_R") };
            flaps = new[] { FindOpt("Flap_L"), FindOpt("Flap_R") };
            canopyBarrels = new[] { FindOpt("CanopyStrutBarrel_L"), FindOpt("CanopyStrutBarrel_R") };
            canopyRods = new[] { FindOpt("CanopyStrutRod_L"), FindOpt("CanopyStrutRod_R") };
            canopyBasePins = new[] { FindOpt("CanopyStrutBasePin_L"), FindOpt("CanopyStrutBasePin_R") };
            canopyBowPins = new[] { FindOpt("CanopyStrutBowPin_L"), FindOpt("CanopyStrutBowPin_R") };
        }

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            propulsion = GetComponent<DuctedThrustSystem>();
            nozzles = SkylarkTemplate.Get<JetNozzle[]>(propulsion, "nozzles");
            flyByWire = SkylarkTemplate.Get<object>(GetComponentInChildren<ControlsFilter>(), "flyByWire");
            alphaLimiter = SkylarkTemplate.Get<float>(flyByWire, "alphaLimiterStrength");
            engine = GetComponentInChildren<Turbofan>();
            Material blur = rotorBlur[0] != null ? rotorBlur[0].sharedMaterial : null;
            if (blur != null && blur.HasProperty(BaseColor))
            {
                blurProperties = new MaterialPropertyBlock();
                blurTint = blur.GetColor(BaseColor);
            }
            ductRest = new Quaternion[4]; rotorRest = new Quaternion[4]; vaneRest = new Quaternion[8];
            for (int i = 0; i < 4; i++) { ductRest[i] = ducts[i].localRotation; rotorRest[i] = rotors[i].localRotation; }
            for (int i = 0; i < 8; i++) vaneRest[i] = vanes[i].localRotation;
            airbrakeRest = new Quaternion[airbrakes != null ? airbrakes.Length : 0];
            for (int i = 0; i < airbrakeRest.Length; i++) if (airbrakes[i] != null) airbrakeRest[i] = airbrakes[i].localRotation;
            flapHinges = new SkylarkHinge[flaps != null ? flaps.Length : 0];
            for (int i = 0; i < flapHinges.Length; i++)
                if (flaps[i] != null) flapHinges[i] = new SkylarkHinge(transform, flaps[i], Vector3.right, -24f);
            Canopy[] canopies = SkylarkTemplate.Get<Canopy[]>(aircraft, "canopies");
            if (canopies != null && canopies.Length > 0) primaryCanopy = canopies[0];
        }

        private void FixedUpdate()
        {
            // Differential vertical thrust controls pitch/roll in hover. Fade it out as the fans turn forward,
            // otherwise a cruise roll command produces a large unintended differential-thrust yaw moment.
            float authority = .17f * Mathf.Sin(propulsion.angle * Mathf.Deg2Rad);
            float hoverBlend = Mathf.Pow(Mathf.Sin(propulsion.angle * Mathf.Deg2Rad), 2f)
                * (1f - Mathf.InverseLerp(35f, 70f, aircraft.speed));
            // Wing angle of attack is not meaningful during vertical flight. Keep the normal native
            // alpha limiter in wing-supported flight, but let the hover attitude loop arrest drift promptly.
            SkylarkTemplate.Set(flyByWire, "alphaLimiterStrength", alphaLimiter * (1f - hoverBlend));
            for (int i = 0; i < nozzles.Length; i++)
                if (nozzles[i] != null)
                {
                    SkylarkTemplate.Set(nozzles[i], "pitchThrust", i < 2 ? -authority : authority);
                    SkylarkTemplate.Set(nozzles[i], "rollThrust", i % 2 == 0 ? authority : -authority);
                }
        }

        private void LateUpdate()
        {
            if (!balanced && aircraft.partLookup.Count > 0)
            {
                // Balance the actual native dry/fuel/store masses, so selecting a light asymmetric loadout does not
                // require a permanent pitch/roll command. Only initialize once; damage must still remove authority.
                Vector3 center = aircraft.transform.InverseTransformPoint(PhysicsCenter(aircraft));
                Vector3 front = aircraft.transform.InverseTransformPoint(ducts[0].position);
                Vector3 rear = aircraft.transform.InverseTransformPoint(ducts[2].position);
                float forwardShare = Mathf.Clamp((center.z - rear.z) / (front.z - rear.z), .2f, .8f);
                for (int i = 0; i < 4; i++)
                {
                    float halfSpan = Mathf.Abs(i < 2 ? front.x : rear.x);
                    float rightShare = Mathf.Clamp01(.5f + center.x / (2f * halfSpan));
                    float proportion = (i < 2 ? forwardShare : 1f - forwardShare) * (i % 2 == 0 ? 1f - rightShare : rightShare);
                    SkylarkTemplate.Set(nozzles[i], "thrustProportion", proportion);
                }
                balanced = true;
            }
            float rpm = engine.GetRPMRatio();
            phase = (phase + rpm * 5400f * Time.deltaTime) % 360f;
            ControlInputs inputs = aircraft.GetInputs();
            bool blurred = rpm > .30f;
            float blurStrength = Mathf.Lerp(1.20f, 1.65f, Mathf.InverseLerp(.30f, 1f, rpm));
            bool updateBlur = blurred && blurProperties != null && Mathf.Abs(blurStrength - lastBlurStrength) > .02f;
            Color tint = blurTint;
            tint.a *= blurStrength;
            for (int i = 0; i < 4; i++)
            {
                if (ducts[i] == null || rotors[i] == null) continue;
                ducts[i].localRotation = ductRest[i] * Quaternion.AngleAxis(propulsion.angle - 90f, Vector3.right);
                rotors[i].localRotation = rotorRest[i] * Quaternion.AngleAxis(phase * (i == 0 || i == 3 ? 1f : -1f), Vector3.forward);
                if (rotorMeshes[i] != null) rotorMeshes[i].enabled = !blurred;
                if (rotorBlur[i] != null)
                {
                    rotorBlur[i].enabled = blurred;
                    if (updateBlur)
                    {
                        rotorBlur[i].GetPropertyBlock(blurProperties);
                        blurProperties.SetColor(BaseColor, tint);
                        rotorBlur[i].SetPropertyBlock(blurProperties);
                    }
                }
                if (rotorHubs[i] != null) rotorHubs[i].enabled = blurred;
                float vane = Mathf.Clamp(inputs.yaw * (i % 2 == 0 ? -15f : 15f), -15f, 15f);
                if (vanes[i * 2] != null) vanes[i * 2].localRotation = vaneRest[i * 2] * Quaternion.AngleAxis(vane, Vector3.right);
                if (vanes[i * 2 + 1] != null) vanes[i * 2 + 1].localRotation = vaneRest[i * 2 + 1] * Quaternion.AngleAxis(vane, Vector3.right);
            }
            if (updateBlur) lastBlurStrength = blurStrength;

            bool brakeTarget = !aircraft.disabled && (aircraft.speed > 18f && inputs.throttle <= .02f
                || aircraft.radarAlt < 1.5f && aircraft.speed > 4f && inputs.brake > .2f);
            float nextBrake = Mathf.MoveTowards(airbrakeAmount, brakeTarget ? 1f : 0f, Time.deltaTime * 3.2f);
            if (nextBrake != airbrakeAmount || airbrakeAmount > 0f)
            {
                airbrakeAmount = nextBrake;
                for (int i = 0; i < airbrakes.Length; i++)
                {
                    if (airbrakes[i] != null) airbrakes[i].localRotation = airbrakeRest[i] * Quaternion.AngleAxis(42f * airbrakeAmount, Vector3.right);
                    if (airbrakePistons[i] != null && airbrakeLugs[i] != null)
                        airbrakePistons[i].LookAt(airbrakeLugs[i].position, aircraft.transform.up);
                }
            }

            if (flapHinges != null)
            {
                bool flapTarget = !aircraft.disabled && (aircraft.gearState == LandingGear.GearState.LockedExtended || aircraft.gearState == LandingGear.GearState.Extending);
                for (int i = 0; i < flapHinges.Length; i++)
                    if (flapHinges[i] != null) flapHinges[i].Step(flapTarget ? 1f : 0f, 1.4f, Time.deltaTime);
            }

            if (primaryCanopy != null && !aircraft.HasEjected() && canopyBarrels != null)
            {
                float open = SkylarkTemplate.Get<float>(primaryCanopy, "openAmount");
                bool showStruts = open > .01f;
                for (int i = 0; i < canopyBarrels.Length; i++)
                {
                    if (canopyBarrels[i] != null)
                    {
                        if (canopyBarrels[i].gameObject.activeSelf != showStruts) canopyBarrels[i].gameObject.SetActive(showStruts);
                        if (showStruts && canopyBowPins[i] != null) canopyBarrels[i].LookAt(canopyBowPins[i].position, aircraft.transform.up);
                    }
                    if (canopyRods[i] != null)
                    {
                        if (canopyRods[i].gameObject.activeSelf != showStruts) canopyRods[i].gameObject.SetActive(showStruts);
                        if (showStruts && canopyBasePins[i] != null) canopyRods[i].LookAt(canopyBasePins[i].position, aircraft.transform.up);
                    }
                }
            }
        }

        internal static Vector3 PhysicsCenter(Aircraft aircraft)
        {
            Rigidbody[] bodies = aircraft.partLookup.Where(p => !p.IsDetached() && p.rb != null).Select(p => p.rb).Distinct().ToArray();
            float mass = bodies.Sum(rb => rb.mass);
            Vector3 moment = Vector3.zero;
            // Match the interpolated visual transforms used by the fan anchors; mixing worldCenterOfMass
            // from the physics step with a rendered root produces a false speed-dependent COM displacement.
            foreach (Rigidbody body in bodies) moment += body.transform.TransformPoint(body.centerOfMass) * body.mass;
            return mass > 0 ? moment / mass : aircraft.rb.worldCenterOfMass;
        }
    }
}
