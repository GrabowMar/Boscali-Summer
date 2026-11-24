using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>The Groza's control surfaces carry each origin on its hinge
    /// line (build_megaplane wing()/tail()), and this drives them from the same replicated stick the flight model
    /// reads, so every peer animates the same deflections. Ailerons follow roll, roll spoilers pop on the down-going
    /// wing (747 practice), the elevator follows pitch, both rudders yaw together, and the flaps deploy with the
    /// networked gear/lift configuration at approach angles. Rates are sized for a 6,500 t airframe: fast enough to
    /// read, slow enough to feel massive. Visual only; nothing here touches the flight model.</summary>
    internal sealed class MegaplaneSurfaces : MonoBehaviour
    {
        private const float AileronMax = 12f, ElevatorMax = 10f, RudderMax = 10f, FlapInnerMax = 25f, FlapOuterMax = 20f, SpoilerMax = 45f;
        private const float SurfaceRate = 40f;   // deg/s commanded deflection
        private const float FlapSeconds = 9f;   // a 60 m flap set takes its time
        private Aircraft aircraft;
        private readonly Dictionary<Transform, AeroPart> actuatorOwners = new Dictionary<Transform, AeroPart>();
        private float flap;   // 0 up .. 1 approach config (gear/lift flag, as the lift jets do)
        private readonly List<(Transform t, Quaternion rest, Vector3 axis, float sign)> ailerons = new List<(Transform, Quaternion, Vector3, float)>();
        private readonly List<(Transform t, Quaternion rest, Vector3 axis, float sign)> spoilers = new List<(Transform, Quaternion, Vector3, float)>();
        private readonly List<(Transform t, Quaternion rest, float sign)> rudders = new List<(Transform, Quaternion, float)>();
        private readonly List<(Transform t, Quaternion rest, Vector3 axis)> flapsInner = new List<(Transform, Quaternion, Vector3)>();
        private readonly List<(Transform t, Quaternion rest, Vector3 axis)> flapsOuter = new List<(Transform, Quaternion, Vector3)>();
        private readonly List<(Transform t, Quaternion rest, float sign)> elevators = new List<(Transform, Quaternion, float)>();

#if !PUBLIC_RELEASE
        internal float InnerFlapAngle => flapsInner.Count > 0 && flapsInner[0].t != null
            ? Quaternion.Angle(flapsInner[0].rest, flapsInner[0].t.localRotation) : 0f;
#endif

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                float side = n.EndsWith("_L") ? -1f : n.EndsWith("_R") ? 1f : 0f;
                if (n.StartsWith("Aileron_")) ailerons.Add((t, t.localRotation, Hinge(t), side));
                else if (n.StartsWith("Spoilers_") || n.StartsWith("Spoiler_")) spoilers.Add((t, t.localRotation, Hinge(t), side));
                else if (n.StartsWith("Rudder_")) rudders.Add((t, t.localRotation, side));
                else if (n.StartsWith("Flap_Inner_")) flapsInner.Add((t, t.localRotation, Hinge(t)));
                else if (n.StartsWith("Flap_Outer_")) flapsOuter.Add((t, t.localRotation, Hinge(t)));
                else if (n == "Elevator") elevators.Add((t, t.localRotation, 1f));
                else continue;
                actuatorOwners.Add(t, t.GetComponentInParent<AeroPart>(true));
            }
        }

        private static Vector3 Hinge(Transform surface)
        {
            Transform marker = surface.Find("Axis_" + surface.name);
            return marker != null && marker.localPosition.sqrMagnitude > .0001f ? marker.localPosition.normalized : Vector3.right;
        }

        private void Update()
        {
            if (aircraft == null) return;
            ControlInputs input = aircraft.GetInputs();
            float dt = Time.deltaTime;
            flap = Mathf.MoveTowards(flap, aircraft.NetworkgearDeployed && !aircraft.disabled ? 1f : 0f, dt / FlapSeconds);
            float roll = Mathf.Clamp(input.roll, -1f, 1f), pitch = Mathf.Clamp(input.pitch, -1f, 1f), yaw = Mathf.Clamp(input.yaw, -1f, 1f);
            // a 6,500 t wing leans in: the surfaces lag the stick the way the airframe lags it in MegaplaneFlight
            float rate = SurfaceRate * dt;
            foreach ((Transform t, Quaternion rest, Vector3 axis, float sign) in ailerons) Deflect(t, rest, axis, AileronMax * roll * sign, rate);
            foreach ((Transform t, Quaternion rest, Vector3 axis, float sign) in spoilers)
            {
                // the wing going DOWN dumps its spoilers (747 roll practice), and both pop shallow in approach config
                Deflect(t, rest, axis, SpoilerMax * Mathf.Max(0f, roll * sign) + 8f * flap, rate);
            }
            foreach ((Transform t, Quaternion rest, float _) in rudders) Deflect(t, rest, Vector3.up, -RudderMax * yaw, rate);
            foreach ((Transform t, Quaternion rest, Vector3 axis) in flapsInner) Deflect(t, rest, axis, -FlapInnerMax * flap, rate);
            foreach ((Transform t, Quaternion rest, Vector3 axis) in flapsOuter) Deflect(t, rest, axis, -FlapOuterMax * flap, rate);
            foreach ((Transform t, Quaternion rest, float _) in elevators) Deflect(t, rest, Vector3.right, -ElevatorMax * pitch, rate);
        }

        private void Deflect(Transform t, Quaternion rest, Vector3 axis, float degrees, float step)
        {
            if (t == null || !t.IsChildOf(transform)) return;
            AeroPart owner = actuatorOwners[t];
            if (owner != null)
            {
                // A pierced actuator still moves, but loses travel and responds slowly.
                // A broken-off surface keeps its last deflection on the debris.
                if (owner.IsDetached() || owner.hitPoints <= 0f) return;
                float response = Mathf.InverseLerp(0f, 85f, owner.hitPoints);
                degrees *= Mathf.Lerp(.25f, 1f, response);
                step *= Mathf.Lerp(.12f, 1f, response);
            }
            t.localRotation = Quaternion.RotateTowards(t.localRotation, rest * Quaternion.AngleAxis(degrees, axis), step);
        }
    }
}
