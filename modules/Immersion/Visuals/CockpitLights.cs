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

        private static readonly Color FlashColour = new Color(1f, 0.62f, 0.3f), CautionColour = new Color(1f, 0.08f, 0.04f);
        private const float CautionPeak = 0.5f;
        private MissileWarning warning;
        private Aircraft warnedFor;   // the component lookup runs only when the aircraft changes, by day too
        private const float FlashPeak = 2.2f, FlashRange = 4f;

        private Aircraft bound;
        private Light flood, flash;
        private float flicker, level = 1f;

        public float Intensity { get; private set; }

        /// <param name="sunHeight">Sine of the sun's elevation. The flood follows the sun, not the ambient: vanilla ambient sits near 0.2 by day
        /// under broken cloud, which left it on at 10:00 (live 2026-10-27).</param>
        public void Tick(Aircraft aircraft, Camera camera, float sunHeight, bool enabled, float dt)
        {
            // Off with the sun up; full from about 7 degrees below the horizon (the Weather dark-nights ramp).
            float night = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.12f, 0f, sunHeight));
            float burst = ExplosionFlash.Level(Time.time);
            // Master caution: a missile inbound pulses the pit red at 2 Hz. One list-count read; the game keeps the list.
            if (aircraft != warnedFor) { warnedFor = aircraft; warning = aircraft != null ? aircraft.GetComponent<MissileWarning>() : null; }
            bool inbound = warning != null && warning.knownMissiles != null && warning.knownMissiles.Count > 0;
            if (!enabled || aircraft == null || camera == null || (night <= 0.001f && burst <= 0.001f && !inbound)) { Release(); return; }
            if (aircraft != bound) Bind(aircraft, camera);
            if (flood == null) { Release(); return; }
            // The caution glow rides the downward flood cone, never a point light: the canopy glass sits outside the cone,
            // so it cannot mirror the glow (a point light threw big red highlights on the windscreen, live 2026-11-06).
            float caution = inbound ? CautionPeak * (0.55f + 0.45f * Mathf.Sin(Time.time * Mathf.PI * 4f)) : 0f;
            float blast = FlashPeak * burst;
            flash.enabled = blast > 0.001f;
            if (flash.enabled)
            {
                Vector3 toward = ExplosionFlash.At - camera.transform.position;
                flash.transform.position = camera.transform.position + (toward.sqrMagnitude > 1f ? toward.normalized : Vector3.up) * 1.6f;
                flash.intensity = blast * Mathf.Lerp(0.45f, 1f, night);
            }
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
            float warm = FloodPeak * Intensity, red = caution * level;
            flood.color = red > warm ? CautionColour : FloodColour;
            flood.intensity = Mathf.Max(warm, red);
            flood.enabled = flood.intensity > 0.001f;
        }

        /// <summary>A hit lands: the flood stutters for a moment.</summary>
        public void Kick(float seconds) => flicker = Mathf.Max(flicker, seconds);

        public void Release()
        {
            if (flood != null) Object.Destroy(flood.gameObject);
            if (flash != null) Object.Destroy(flash.gameObject);
            flood = flash = null;
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
            var burst = new GameObject("BoscaliCockpitFlash");
            burst.transform.SetParent(frame, false);
            flash = burst.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = FlashColour;
            flash.range = FlashRange;
            flash.shadows = LightShadows.None;
            flash.renderMode = LightRenderMode.ForcePixel;
            flash.enabled = false;
        }
    }

    /// <summary>
    /// The last nearby blast seen from the cockpit (2026-11-06), fed by the explosion audio patch: where it was and how hard it hit
    /// the eye. The cockpit flash light and the visor's auto-darkening read it; it decays in about half a second.
    /// </summary>
    internal static class ExplosionFlash
    {
        private const float Reach = 1800f, Decay = 0.18f;
        private static float strength, time = -10f;

        internal static Vector3 At { get; private set; }

        internal static void Report(Vector3 position, Vector3 eye)
        {
            float d = Vector3.Distance(position, eye);
            if (d >= Reach) return;
            float s = (1f - d / Reach) * (1f - d / Reach);
            if (s < Level(Time.time)) return;   // a weaker blast never replaces a fresher, stronger one
            strength = s;
            time = Time.time;
            At = position;
        }

        /// <summary>0..1 now: the blast's strength, decaying over about half a second.</summary>
        internal static float Level(float now) => now < time ? 0f : strength * Mathf.Exp(-(now - time) / Decay);
    }
}
