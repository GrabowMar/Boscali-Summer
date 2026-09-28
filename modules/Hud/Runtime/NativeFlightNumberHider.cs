using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Hud.Runtime
{
    /// <summary>
    /// Hides vanilla's own flight numbers (airspeed, altitude, climb, bearing, Mach, G, fuel,
    /// throttle, AoA, compass and the HMD boxes) while in third person only
    /// (docs/superpowers/specs/2026-09-28-wingview-third-person-design.md, "Third-person HUD").
    /// Never disables or destroys a native script or GameObject: it adds a <see cref="CanvasGroup"/>
    /// (or reuses one already present) on each specific leaf graphic and drives its alpha to 0,
    /// exactly the mechanism the retired <c>NativeHudPresentation</c> used (see
    /// <c>git show HEAD~:modules/Hud/Runtime/NativeHudPresentation.cs</c>). Stall and overspeed
    /// warning texts are never touched -- they are not in the hide list below.
    ///
    /// Discovery (walking the native hierarchy for the leaf graphics) runs only on an ownship
    /// change or a new <see cref="HUDAppManager"/> instance (HUDExtras spawn late); every other
    /// tick just flips already-found <see cref="CanvasGroup"/>s. <see cref="Tick"/> is driven
    /// from <c>HudBoard.LateUpdate</c> at the same cadence as everything else there.
    /// </summary>
    internal sealed class NativeFlightNumberHider
    {
        private static readonly FieldInfo CompassField = AccessTools.Field(typeof(FlightHud), "compass");
        private static readonly FieldInfo PitchCompassCenterField = AccessTools.Field(typeof(FlightHud), "pitchCompassCenter");
        private static readonly FieldInfo AirspeedDisplayField = AccessTools.Field(typeof(SpeedGauge), "airspeedDisplay");
        private static readonly FieldInfo SpeedBorderField = AccessTools.Field(typeof(SpeedGauge), "border");
        private static readonly FieldInfo AoATextField = AccessTools.Field(typeof(AoADisplay), "AoAText");
        private static readonly FieldInfo HmdSpeedField = AccessTools.Field(typeof(HeadMountedDisplay), "speed");
        private static readonly FieldInfo HmdAltitudeField = AccessTools.Field(typeof(HeadMountedDisplay), "altitude");
        private static readonly FieldInfo HmdBearingField = AccessTools.Field(typeof(HeadMountedDisplay), "bearing");
        private static readonly FieldInfo HmdHorizonField = AccessTools.Field(typeof(HeadMountedDisplay), "horizon");

        private const int Capacity = 32;

        private struct Hidden
        {
            public CanvasGroup Group;
            public float OriginalAlpha;
            public bool Added;
        }

        private readonly List<Hidden> hidden = new List<Hidden>(Capacity);

        private bool active;
        private bool discovered;
        private Aircraft lastAircraft;
        private HUDAppManager lastAppManager;

        /// <summary>Hide while <paramref name="wantActive"/> and an aircraft is present; restore
        /// (without destroying) the instant either goes false.</summary>
        public void Tick(bool wantActive, Aircraft aircraft)
        {
            HUDAppManager appManager = SceneSingleton<HUDAppManager>.i;
            if (aircraft != lastAircraft) discovered = false;
            if (appManager != lastAppManager && appManager != null) discovered = false;
            lastAircraft = aircraft;
            lastAppManager = appManager;

            if (!wantActive || aircraft == null)
            {
                if (active) Restore();
                active = false;
                return;
            }

            if (!discovered)
            {
                Discover();
                discovered = true;
            }

            active = true;
            for (int i = 0; i < hidden.Count; i++)
            {
                CanvasGroup group = hidden[i].Group;
                if (group != null && group.alpha != 0f) group.alpha = 0f;
            }
        }

        private void Discover()
        {
            FlightHud hud = SceneSingleton<FlightHud>.i;
            if (hud != null)
            {
                if (CompassField?.GetValue(hud) is RawImage compass) Hide(compass.gameObject);
                if (PitchCompassCenterField?.GetValue(hud) is GameObject pitch) Hide(pitch);

                foreach (HUDApp app in hud.GetComponentsInChildren<HUDApp>(true))
                {
                    if (app is SpeedGauge)
                    {
                        HideGraphicField(AirspeedDisplayField, app);
                        HideGraphicField(SpeedBorderField, app);
                    }
                    else if (app is AoADisplay)
                    {
                        HideGraphicField(AoATextField, app);
                    }
                    else if (app is Altitude || app is Climbrate || app is Bearing ||
                        app is MachIndicator || app is GIndicators || app is FuelGauge || app is ThrottleGauge)
                    {
                        Hide(app.gameObject);
                    }
                }
            }

            HeadMountedDisplay hmd = SceneSingleton<HeadMountedDisplay>.i;
            if (hmd != null)
            {
                HideHudAppField(HmdSpeedField, hmd);
                HideHudAppField(HmdAltitudeField, hmd);
                HideHudAppField(HmdBearingField, hmd);
                HideHudAppField(HmdHorizonField, hmd);
            }
        }

        private void HideGraphicField(FieldInfo field, HUDApp app)
        {
            if (field?.GetValue(app) is Graphic graphic) Hide(graphic.gameObject);
        }

        private void HideHudAppField(FieldInfo field, HeadMountedDisplay hmd)
        {
            if (field?.GetValue(hmd) is HUDApp app) Hide(app.gameObject);
        }

        private void Hide(GameObject target)
        {
            if (target == null || hidden.Count >= Capacity) return;
            for (int i = 0; i < hidden.Count; i++)
                if (hidden[i].Group != null && hidden[i].Group.gameObject == target) return;

            CanvasGroup group = target.GetComponent<CanvasGroup>();
            bool added = group == null;
            if (added) group = target.AddComponent<CanvasGroup>();
            hidden.Add(new Hidden { Group = group, OriginalAlpha = group.alpha, Added = added });
            group.alpha = 0f;
        }

        /// <summary>Exact restore: every group this hider owns goes back to the alpha it had
        /// before hiding. Groups are kept, not destroyed, so re-entering third person the same
        /// scene needs no rediscovery.</summary>
        public void Restore()
        {
            for (int i = 0; i < hidden.Count; i++)
                if (hidden[i].Group != null) hidden[i].Group.alpha = hidden[i].OriginalAlpha;
        }

        /// <summary>Destroys only the CanvasGroups this hider added itself, and forgets
        /// everything discovered. Called on scene reset and feature teardown -- never on an
        /// ordinary exit to cockpit, map or pause.</summary>
        public void Dispose()
        {
            Restore();
            for (int i = 0; i < hidden.Count; i++)
                if (hidden[i].Added && hidden[i].Group != null) Object.Destroy(hidden[i].Group);
            hidden.Clear();
            discovered = false;
            active = false;
            lastAircraft = null;
            lastAppManager = null;
        }
    }
}
