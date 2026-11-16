using System.Collections.Generic;
using BoscaliSummer.Modules.Hud.Domain;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// The game's own fuel gauge (<c>FuelGauge</c>, left of the reticle) and throttle gauge (<c>ThrottleGauge</c>, right of it): where they are, what the throttle says, and the one way this module
    /// hides them behind its own segmented arcs (<see cref="ArcsHud"/>, 2026-11-16). Nothing native is ever moved, deactivated or switched off: the gauges keep running (their pointers, texts,
    /// colours and the game's own warnings are separate objects and untouched), and "hidden" means only that every <see cref="CanvasRenderer"/> under the two gauge objects has its alpha at 0.
    /// That is deliberately not a <see cref="CanvasGroup"/>: the third-person hider (<see cref="NativeFlightNumberHider"/>) puts a group on these same two objects in the external views and a
    /// second owner of a group there would fight it. <see cref="Show"/> puts every alpha back to 1, is cheap when nothing is hidden and is called the same frame the replacement stops drawing.
    /// <para>Geometry is measured, never assumed: the centre is the gauge's reading pivot (the point its pointer turns about), the circle's radius comes from where the middle of the arc's
    /// rectangle sits (<see cref="FlightArcs.RadiusFromRect"/>) and the axis is the direction from the pivot to that middle, so a HUD the player moved or rolled is followed every frame.</para>
    /// </summary>
    internal sealed class NativeFlightArcs
    {
        private static readonly AccessTools.FieldRef<FuelGauge, Transform> FuelPivot = NativeRefs.Field<FuelGauge, Transform>("fuelReadingPivot");
        private static readonly AccessTools.FieldRef<FuelGauge, Image> FuelArc = NativeRefs.Field<FuelGauge, Image>("fuelArc");
        private static readonly AccessTools.FieldRef<ThrottleGauge, Transform> ThrottlePivot = NativeRefs.Field<ThrottleGauge, Transform>("throttleReadingPivot");
        private static readonly AccessTools.FieldRef<ThrottleGauge, Transform> ThrottleBoundary = NativeRefs.Field<ThrottleGauge, Transform>("throttleBoundaryPivot");
        private static readonly AccessTools.FieldRef<ThrottleGauge, Image> ThrottleArc = NativeRefs.Field<ThrottleGauge, Image>("throttleArc");
        private static readonly AccessTools.FieldRef<ThrottleGauge, bool> ThrottleAfterburner = NativeRefs.Field<ThrottleGauge, bool>("afterburner");
        private static readonly AccessTools.FieldRef<ThrottleGauge, TextMeshProUGUI> ThrottleReading = NativeRefs.Field<ThrottleGauge, TextMeshProUGUI>("throttleReading");

        /// <summary>One arc's circle in canvas units: its centre, radius and axis (degrees counter-clockwise from the canvas's +x, the direction from the centre to the middle of the arc).</summary>
        internal struct Arc
        {
            public float X, Y, Radius, AxisDeg;
        }

        private FuelGauge fuel;
        private ThrottleGauge throttle;
        private readonly List<CanvasRenderer> fuelRenderers = new List<CanvasRenderer>(8), throttleRenderers = new List<CanvasRenderer>(12);
        private float nextFind;
        private bool hidden;

        /// <summary>Both gauges are live and their arcs are on screen: geometry for each, the throttle's detent fraction (NaN: none) and its native reading text. False means "do not draw, leave the game's gauges alone".</summary>
        public bool Measure(RectTransform canvasRoot, out Arc fuelArc, out Arc throttleArc, out float detent, out string throttleText)
        {
            fuelArc = throttleArc = default;
            detent = float.NaN;
            throttleText = null;
            if (canvasRoot == null || !Find()) return false;
            Transform fuelPivot = NativeRefs.Get(FuelPivot, fuel), throttlePivot = NativeRefs.Get(ThrottlePivot, throttle);
            Image fuelImage = NativeRefs.Get(FuelArc, fuel), throttleImage = NativeRefs.Get(ThrottleArc, throttle);
            if (fuelPivot == null || throttlePivot == null || !Up(fuelImage) || !Up(throttleImage)) return false;
            if (!Circle(canvasRoot, fuelPivot, fuelImage, FlightArcs.FuelNativeHalfDeg, out fuelArc)) return false;
            if (!Circle(canvasRoot, throttlePivot, throttleImage, FlightArcs.ThrottleNativeHalfDeg, out throttleArc)) return false;
            Transform boundary = NativeRefs.Get(ThrottleBoundary, throttle);
            if (boundary != null && NativeRefs.Get(ThrottleAfterburner, throttle)) detent = FlightArcs.DetentFraction(boundary.localEulerAngles.z);
            TextMeshProUGUI reading = NativeRefs.Get(ThrottleReading, throttle);
            throttleText = reading != null ? reading.text : null;
            return true;
        }

        /// <summary>The arc's image is drawing: enabled (the player's gauges option), in an active object, on a canvas that is on (the game switches the whole HUD canvas off in some views).</summary>
        private static bool Up(Image image) =>
            image != null && image.isActiveAndEnabled && image.canvas != null && image.canvas.isActiveAndEnabled;

        private static bool Circle(RectTransform canvasRoot, Transform pivot, Image arc, float nativeHalfDeg, out Arc circle)
        {
            circle = default;
            RectTransform rect = arc.rectTransform;
            Vector3 centre = canvasRoot.InverseTransformPoint(pivot.position);
            Vector3 middle = canvasRoot.InverseTransformPoint(rect.TransformPoint(rect.rect.center));
            float dx = middle.x - centre.x, dy = middle.y - centre.y, distance = Mathf.Sqrt(dx * dx + dy * dy);
            if (!(distance > 60f && distance < 1500f)) return false;
            circle = new Arc { X = centre.x, Y = centre.y, Radius = FlightArcs.RadiusFromRect(distance, nativeHalfDeg), AxisDeg = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg };
            return true;
        }

        /// <summary>Keep both gauges at hand: the live pair under the native flight HUD, searched again (once in two seconds at most) when one is gone or no longer active (a new aircraft's HUD extras spawn late).</summary>
        private bool Find()
        {
            if (fuel != null && throttle != null && fuel.gameObject.activeInHierarchy && throttle.gameObject.activeInHierarchy) return true;
            float now = Time.unscaledTime;
            if (now < nextFind) return false;
            nextFind = now + 2f;
            Show();
            fuel = null;
            throttle = null;
            fuelRenderers.Clear();
            throttleRenderers.Clear();
            FlightHud hud = SceneSingleton<FlightHud>.i;
            if (hud == null) return false;
            fuel = First<FuelGauge>(hud);
            throttle = First<ThrottleGauge>(hud);
            if (fuel == null || throttle == null) return false;
            fuel.GetComponentsInChildren(true, fuelRenderers);
            throttle.GetComponentsInChildren(true, throttleRenderers);
            return true;
        }

        private static T First<T>(Component hud) where T : Component
        {
            foreach (T candidate in hud.GetComponentsInChildren<T>(true))
                if (candidate.gameObject.activeInHierarchy) return candidate;
            return null;
        }

        /// <summary>Hide both gauges: every renderer's alpha at 0 (re-asserted each call, so a game that touched one is overruled the same frame). Call only while the replacement is up and drawing.</summary>
        public void Hide()
        {
            Set(fuelRenderers, 0f);
            Set(throttleRenderers, 0f);
            hidden = true;
        }

        /// <summary>Show both gauges again, the same frame. Cheap and idempotent: nothing is touched when nothing was hidden.</summary>
        public void Show()
        {
            if (!hidden) return;
            Set(fuelRenderers, 1f);
            Set(throttleRenderers, 1f);
            hidden = false;
        }

        /// <summary>The game's gauge texts and their renderer alphas, "name=alpha ..." (the sim probe's evidence that no native caption or percentage shows behind ours). Allocates: probe only.</summary>
        internal string TextAlphas()
        {
            var sb = new System.Text.StringBuilder();
            foreach (Component gauge in new Component[] { fuel, throttle })
            {
                if (gauge == null) continue;
                foreach (TMP_Text tmp in gauge.GetComponentsInChildren<TMP_Text>(true))
                    sb.Append(tmp.name).Append('[').Append(tmp.text).Append("]=").Append(tmp.canvasRenderer.GetAlpha().ToString("0.##")).Append(tmp.isActiveAndEnabled ? "" : "(off)").Append(' ');
            }
            return sb.ToString();
        }

        /// <summary>The gauges are hidden right now (the sim probe reads it).</summary>
        public bool Hidden => hidden;

        private static void Set(List<CanvasRenderer> renderers, float alpha)
        {
            for (int i = 0; i < renderers.Count; i++)
            {
                CanvasRenderer r = renderers[i];
                if (r != null && r.GetAlpha() != alpha) r.SetAlpha(alpha);
            }
        }

        /// <summary>Scene or feature teardown: everything back, nothing remembered.</summary>
        public void Dispose()
        {
            Show();
            fuel = null;
            throttle = null;
            fuelRenderers.Clear();
            throttleRenderers.Clear();
            nextFind = 0f;
        }
    }
}
