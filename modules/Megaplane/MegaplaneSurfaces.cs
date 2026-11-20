using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>The G-240's control surfaces finally move: the art carries every surface with its origin on the hinge
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
        private float flap;   // 0 up .. 1 approach config (gear/lift flag, as the lift jets do)
        private readonly List<(Transform t, Quaternion rest, float sign)> ailerons = new List<(Transform, Quaternion, float)>();
        private readonly List<(Transform t, Quaternion rest, float sign)> spoilers = new List<(Transform, Quaternion, float)>();
        private readonly List<(Transform t, Quaternion rest, float sign)> rudders = new List<(Transform, Quaternion, float)>();
        private readonly List<(Transform t, Quaternion rest, float sign)> flapsInner = new List<(Transform, Quaternion, float)>();
        private readonly List<(Transform t, Quaternion rest, float sign)> flapsOuter = new List<(Transform, Quaternion, float)>();
        private readonly List<(Transform t, Quaternion rest, float sign)> elevators = new List<(Transform, Quaternion, float)>();

        private void Awake()
        {
            aircraft = GetComponent<Aircraft>();
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                string n = t.name;
                float side = n.EndsWith("_L") ? -1f : n.EndsWith("_R") ? 1f : 0f;
                if (n.StartsWith("Aileron_")) ailerons.Add((t, t.localRotation, side));
                else if (n.StartsWith("Spoilers_")) spoilers.Add((t, t.localRotation, side));
                else if (n.StartsWith("Spoiler_")) spoilers.Add((t, t.localRotation, side));   // pre-consolidation art
                else if (n.StartsWith("Rudder_")) rudders.Add((t, t.localRotation, side));
                else if (n.StartsWith("Flap_Inner_")) flapsInner.Add((t, t.localRotation, side));
                else if (n.StartsWith("Flap_Outer_")) flapsOuter.Add((t, t.localRotation, side));
                else if (n == "Elevator") elevators.Add((t, t.localRotation, 1f));
            }
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
            float aileron = Mathf.MoveTowards(0f, AileronMax * roll, rate);
            float elevator = Mathf.MoveTowards(0f, -ElevatorMax * pitch, rate);
            float rudder = Mathf.MoveTowards(0f, -RudderMax * yaw, rate);
            float inner = Mathf.MoveTowards(0f, FlapInnerMax * flap, rate);
            float outer = Mathf.MoveTowards(0f, FlapOuterMax * flap, rate);

            foreach ((Transform t, Quaternion rest, float sign) in ailerons) if (Alive(t)) t.localRotation = rest * Quaternion.AngleAxis(aileron * sign, Vector3.right);
            foreach ((Transform t, Quaternion rest, float sign) in spoilers)
            {
                // the wing going DOWN dumps its spoilers (747 roll practice), and both pop shallow in approach config
                if (Alive(t)) t.localRotation = rest * Quaternion.AngleAxis(SpoilerMax * Mathf.Max(0f, roll * sign) + 8f * flap, Vector3.right);
            }
            foreach ((Transform t, Quaternion rest, float sign) in rudders) if (Alive(t)) t.localRotation = rest * Quaternion.AngleAxis(rudder, Vector3.up);
            foreach ((Transform t, Quaternion rest, float sign) in flapsInner) if (Alive(t)) t.localRotation = rest * Quaternion.AngleAxis(-inner, Vector3.right);
            foreach ((Transform t, Quaternion rest, float sign) in flapsOuter) if (Alive(t)) t.localRotation = rest * Quaternion.AngleAxis(-outer, Vector3.right);
            foreach ((Transform t, Quaternion rest, float _) in elevators) if (Alive(t)) t.localRotation = rest * Quaternion.AngleAxis(elevator, Vector3.right);
        }

        private bool Alive(Transform t) => t != null && t.IsChildOf(transform);   // a shot-off panel keeps its own pose
    }
}
