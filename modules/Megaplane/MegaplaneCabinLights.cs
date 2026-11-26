using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Short-throw pool of light under each crew-deck ceiling lens.</summary>
    // CameraStateManager places the camera and applies the floating-origin shift in LateUpdate.
    [DefaultExecutionOrder(1000)]
    internal sealed class MegaplaneCabinLights : MonoBehaviour
    {
        [SerializeField] private Aircraft carrier;
        [SerializeField] private AeroPart bridge;
        [SerializeField] private Light[] lamps;
        private MegaplaneReactor plant;
        private bool lit;
        private float nextCheck;

        internal static void Install(Aircraft aircraft, AeroPart section, Dictionary<string, Transform> nodes)
        {
            if (GameManager.IsHeadless ||
                !nodes.TryGetValue("FlightDeck_LampForward", out Transform front) ||
                !nodes.TryGetValue("FlightDeck_LampMid", out Transform mid) ||
                !nodes.TryGetValue("FlightDeck_LampAft", out Transform aft)) return;

            var cabin = mid.gameObject.AddComponent<MegaplaneCabinLights>();
            cabin.carrier = aircraft;
            cabin.bridge = section;
            cabin.lamps = new[] {
                Downlight(front, new Color(1f, .89f, .76f), 2.65f),
                Downlight(mid, new Color(1f, .86f, .68f), 3.75f),
                Downlight(aft, new Color(1f, .81f, .60f), 3.65f)
            };
        }

        private static Light Downlight(Transform lens, Color tint, float intensity)
        {
            var lamp = new GameObject("Cabin downlight").AddComponent<Light>();
            lamp.transform.SetParent(lens, false);
            lamp.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            lamp.type = LightType.Spot;
            lamp.spotAngle = 140f;
            lamp.range = 5.1f;
            lamp.color = tint;
            lamp.intensity = intensity;
            lamp.shadows = LightShadows.None;
            lamp.enabled = false;
            return lamp;
        }

        private void Start() => plant = carrier.GetComponent<MegaplaneReactor>();

        private void LateUpdate()
        {
            if (!lit && Time.unscaledTime < nextCheck) return;
            nextCheck = Time.unscaledTime + .15f;
            var view = SceneSingleton<CameraStateManager>.i;
            bool powered = carrier != null && !carrier.disabled && bridge != null && !bridge.IsDetached() &&
                           (plant == null || plant.AvailablePower > .04f);
            if (!powered || view == null || view.mainCamera == null) { SetLit(false); return; }

            // The middle lens is the local datum. Include both pilots' permitted head movement.
            Vector3 eye = transform.InverseTransformPoint(view.mainCamera.transform.position);
            SetLit(Mathf.Abs(eye.x) < 4.6f && eye.y > -2.95f && eye.y < .65f && eye.z > -4.8f && eye.z < 6.9f);
        }

        private void SetLit(bool value)
        {
            if (lit == value) return;
            lit = value;
            foreach (Light lamp in lamps)
                if (lamp != null) lamp.enabled = value;
        }

        private void OnDisable() => SetLit(false);
    }
}
