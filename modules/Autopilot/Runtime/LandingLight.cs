using BoscaliSummer.Core.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Modules.Autopilot.Runtime
{
    /// <summary>
    /// A switchable nose landing light on the local player's own aircraft: one forward-down
    /// spotlight parented to the airframe, so it rides floating-origin shifts with it. Purely
    /// local presentation (lights are never networked): other players do not see it. Off,
    /// ON, or AUTO (lit while the gear is down). At most one light exists; it is rebuilt when
    /// the local aircraft changes and dropped on scene reset.
    /// </summary>
    internal sealed class LandingLight : MonoBehaviour, ISceneService
    {
        private const float Range = 1400f;
        private const float SpotAngle = 34f;
        private const float InnerSpotAngle = 18f;
        private const float Intensity = 9f;
        private const float PitchDownDeg = 7f;
        private static readonly Color Warm = new Color(1f, 0.95f, 0.84f);

        private Light lamp;
        private Aircraft owner;

        public static LandingLight Instance { get; private set; }

        public bool On { get; private set; }
        public bool Auto { get; private set; }
        public bool Lit => lamp != null && lamp.enabled;

        public void Toggle() => On = !On;

        public void ToggleAuto() => Auto = !Auto;

        public void ResetForScene()
        {
            On = false;
            Drop();
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            Drop();
            if (Instance == this) Instance = null;
        }

        private void LateUpdate()
        {
            if (!On && !Auto)
            {
                if (lamp != null && lamp.enabled) lamp.enabled = false;
                return;
            }

            bool haveAircraft = GameManager.GetLocalAircraft(out Aircraft aircraft) && aircraft != null && !aircraft.disabled;
            bool want = haveAircraft && (On || (Auto && GearDown(aircraft)));
            if (!want)
            {
                if (lamp != null && lamp.enabled) lamp.enabled = false;
                return;
            }

            if (lamp == null || owner != aircraft) Build(aircraft);
            if (lamp != null && !lamp.enabled) lamp.enabled = true;
        }

        private static bool GearDown(Aircraft aircraft) =>
            aircraft.gearState == LandingGear.GearState.LockedExtended ||
            aircraft.gearState == LandingGear.GearState.Extending;

        private void Build(Aircraft aircraft)
        {
            Drop();
            owner = aircraft;
            var go = new GameObject("Boscali / Landing Light");
            go.transform.SetParent(aircraft.transform, false);
            go.transform.localPosition = new Vector3(0f, -0.8f, 3f);
            go.transform.localRotation = Quaternion.Euler(PitchDownDeg, 0f, 0f);
            lamp = go.AddComponent<Light>();
            lamp.type = LightType.Spot;
            lamp.range = Range;
            lamp.spotAngle = SpotAngle;
            lamp.innerSpotAngle = InnerSpotAngle;
            lamp.intensity = Intensity;
            lamp.color = Warm;
            lamp.shadows = LightShadows.None;
            lamp.renderMode = LightRenderMode.ForcePixel;
            lamp.bounceIntensity = 0f;
        }

        private void Drop()
        {
            if (lamp != null) Destroy(lamp.gameObject);
            lamp = null;
            owner = null;
        }
    }
}
