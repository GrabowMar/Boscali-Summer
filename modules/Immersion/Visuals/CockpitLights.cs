using BoscaliSummer.Core.Game;
using UnityEngine;

namespace BoscaliSummer.Modules.Immersion.Visuals
{
    /// <summary>
    /// Night cockpit flood (2026-10-26, reworked 2026-10-27): one dim warm cone from just above the pilot's head, aimed down at the lap and
    /// the side consoles, fixed in the airframe (it never swims with head motion). It follows the sun, not the ambient, and goes dark under
    /// the goggles (real cockpits dim for NVG; vanilla's tube gain blew it up into a glare). A cone, not a point: the canopy glass sits
    /// outside it, so the glass cannot mirror it. No per-frame allocation.
    /// </summary>
    internal sealed class CockpitLights
    {
        // Live 2026-10-26/27: a point flood mirrored as one sharp highlight per canopy pane, and a green dash spill lit the pit and the
        // canopy rails green (worst under NVG gain). Both gone: a downward cone, no spill.
        private static readonly Vector3 FloodOffset = new Vector3(0f, 0.3f, -0.05f);
        private static readonly Color FloodColour = new Color(1f, 0.74f, 0.48f);
        private const float FloodPeak = 0.35f, FloodRange = 1.4f, FloodAngle = 95f;

        private Aircraft bound;
        private Light flood;
        private float flicker, level = 1f;

        public float Intensity { get; private set; }

        /// <param name="sunHeight">Sine of the sun's elevation. The flood follows the sun, not the ambient: vanilla ambient sits near 0.2 by day
        /// under broken cloud, which left it on at 10:00 (live 2026-10-27).</param>
        public void Tick(Aircraft aircraft, Camera camera, float sunHeight, bool enabled, float dt)
        {
            // Off with the sun up; full from about 7 degrees below the horizon (the Weather dark-nights ramp).
            float night = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0f, sunHeight));
            if (!enabled || aircraft == null || camera == null || night <= 0.001f) { Release(); return; }
            if (aircraft != bound) Bind(aircraft, camera);
            if (flood == null) { Release(); return; }
            // The pilot dims the pit for the goggles over about half a second, and brings it back the same way.
            bool goggles = GameAccess.TryGetNightVisionState(out _, out bool nvg) && nvg;
            level = Mathf.MoveTowards(level, goggles ? 0f : 1f, dt * 2f);
            float stutter = 1f;
            if (flicker > 0f)
            {
                flicker -= dt;
                stutter = Random.value < 0.35f ? 0.1f : 1f; // render-only; a hit browns the bus for a moment
            }
            Intensity = night * level * stutter;
            flood.intensity = FloodPeak * Intensity;
            flood.enabled = Intensity > 0.001f;
        }

        /// <summary>A hit lands: the flood stutters for a moment.</summary>
        public void Kick(float seconds) => flicker = Mathf.Max(flicker, seconds);

        public void Release()
        {
            if (flood != null) Object.Destroy(flood.gameObject);
            flood = null;
            bound = null;
            flicker = 0f;
            level = 1f;
            Intensity = 0f;
        }

        private void Bind(Aircraft aircraft, Camera camera)
        {
            Release();
            bound = aircraft;
            Transform frame = aircraft.transform;
            var go = new GameObject("BoscaliCockpitFlood");
            go.transform.SetParent(frame, false);
            go.transform.localPosition = frame.InverseTransformPoint(camera.transform.position) + FloodOffset;
            // Down and a little forward: the lap, the stick and the consoles either side.
            go.transform.localRotation = Quaternion.LookRotation(new Vector3(0f, -1f, 0.35f), Vector3.forward);
            flood = go.AddComponent<Light>();
            flood.type = LightType.Spot;
            flood.spotAngle = FloodAngle;
            flood.innerSpotAngle = FloodAngle * 0.4f;
            flood.color = FloodColour;
            flood.range = FloodRange;
            flood.intensity = 0f;
            flood.shadows = LightShadows.None;
            flood.renderMode = LightRenderMode.ForcePixel;
        }
    }
}
