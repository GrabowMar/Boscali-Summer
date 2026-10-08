using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Soft hover hold for the local pilot's helicopter while troops are on the ropes: a damped
    /// spring back to the release point, capped well below what the rotor can overpower. Runs only
    /// on the pilot's own machine (it owns the flight physics); any stick or collective input
    /// hands control straight back.
    /// </summary>
    internal sealed class HoverHold : MonoBehaviour
    {
        private const float Stiffness = 0.8f;
        private const float Damping = 1.6f;
        private const float MaxAcceleration = 4f;
        private const float StickRelease = 0.15f;

        private Aircraft aircraft;
        private GlobalPosition hold;
        private float until, throttle;

        public static void Engage(Aircraft aircraft, float seconds)
        {
            if (aircraft == null || aircraft.rb == null || !GameManager.GetLocalAircraft(out Aircraft local) || local != aircraft) return;
            HoverHold h = aircraft.GetComponent<HoverHold>() ?? aircraft.gameObject.AddComponent<HoverHold>();
            h.aircraft = aircraft;
            h.hold = aircraft.rb.position.ToGlobalPosition();
            h.until = Time.time + seconds;
            h.throttle = aircraft.GetInputs()?.throttle ?? 0f;
            Plugin.Logger.LogInfo($"[Air Assault] Hover hold engaged for {seconds:0} s; any stick or collective input releases it.");
        }

        private void FixedUpdate()
        {
            if (aircraft == null || aircraft.disabled || aircraft.rb == null || Time.time > until)
            {
                Destroy(this);
                return;
            }
            ControlInputs inputs = aircraft.GetInputs();
            if (inputs != null && (Mathf.Abs(inputs.pitch) > StickRelease || Mathf.Abs(inputs.roll) > StickRelease ||
                Mathf.Abs(inputs.throttle - throttle) > StickRelease))
            {
                Plugin.Logger.LogInfo("[Air Assault] Hover hold released by the pilot.");
                Destroy(this);
                return;
            }
            Rigidbody rb = aircraft.rb;
            Vector3 push = (hold.ToLocalPosition() - rb.position) * Stiffness - rb.velocity * Damping;
            rb.AddForce(Vector3.ClampMagnitude(push, MaxAcceleration), ForceMode.Acceleration);
        }
    }
}
