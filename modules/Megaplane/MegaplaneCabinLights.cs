using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    /// <summary>Short-throw pool of light under each crew-deck ceiling lens.</summary>
    // CameraStateManager places the camera and applies the floating-origin shift in LateUpdate.
    [DefaultExecutionOrder(1000)]
    internal sealed class MegaplaneCabinLights : MonoBehaviour
    {
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");
        [SerializeField] private Aircraft carrier;
        [SerializeField] private AeroPart bridge;
        [SerializeField] private Light[] lamps;
        [SerializeField] private MeshRenderer liner;
        [SerializeField] private int lensSlot = -1;
        private MaterialPropertyBlock lensBlock;
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
            if (nodes.TryGetValue("FlightDeck_CabinLiner", out Transform shell) &&
                shell.TryGetComponent(out cabin.liner))
                cabin.lensSlot = System.Array.FindIndex(cabin.liner.sharedMaterials, m => m.name == "BS_Megaplane_CrewDome_Emit");
            var fixtures = new List<Light>(7);
            foreach (Transform lens in new[] { front, mid, aft })
            {
                bool center = lens == mid;
                fixtures.Add(MakeLamp(lens, Vector3.down, center ? new Color(.90f, .95f, 1f) : new Color(1f, .91f, .78f),
                    lens == front ? 4.5f : 7f, 6.4f, 142f, 105f));
            }
            foreach (string role in new[] { "Nav", "Comms", "Engineering", "Cargo" })
            {
                if (!nodes.TryGetValue("FlightDeck_TaskLamp_" + role, out Transform lens)) continue;
                float side = role == "Nav" || role == "Engineering" ? -1f : 1f;
                // Across the operator's shoulder to the controls. The fascia remains readable
                // and the light also picks out the chair and the clipped working cards.
                fixtures.Add(MakeLamp(lens, new Vector3(.42f * side, -1f, .16f),
                    new Color(1f, .93f, .83f), 5f, 3.7f, 125f, 90f));
            }
            cabin.lamps = fixtures.ToArray();
        }

        private static Light MakeLamp(Transform lens, Vector3 direction, Color tint, float intensity,
            float range, float outerAngle, float innerAngle)
        {
            var lamp = new GameObject("Cabin downlight").AddComponent<Light>();
            lamp.transform.SetParent(lens, false);
            lamp.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, direction);
            lamp.type = LightType.Spot;
            lamp.spotAngle = outerAngle;
            lamp.innerSpotAngle = innerAngle;
            lamp.range = range;
            lamp.color = tint;
            lamp.intensity = intensity;
            lamp.shadows = LightShadows.None;
            lamp.enabled = false;
            return lamp;
        }

        private void Start()
        {
            plant = carrier.GetComponent<MegaplaneReactor>();
            if (lensSlot < 0) return;
            lensBlock = new MaterialPropertyBlock();
            SetLens(false);
        }

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
            SetLens(value);
        }

        private void SetLens(bool on)
        {
            if (liner == null || lensBlock == null) return;
            liner.GetPropertyBlock(lensBlock, lensSlot);
            lensBlock.SetColor(Emission, on ? new Color(.42f, .40f, .34f) : Color.black);
            liner.SetPropertyBlock(lensBlock, lensSlot);
        }

        private void OnDisable() => SetLit(false);
    }
}
