using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    // Native AirbaseOverlay updates before the camera's LateUpdate. Reproject its cached
    // choice without advancing taxi/landing registration, reachedRunway or takeoff state.
    internal static class ThirdPersonAirbaseProjection
    {
        private static readonly FieldInfo Nearest = AccessTools.Field(typeof(AirbaseOverlay), "nearestAirbase");
        private static readonly FieldInfo Usage = AccessTools.Field(typeof(AirbaseOverlay), "runwayUsage");
        private static readonly FieldInfo Landing = AccessTools.Field(typeof(AirbaseOverlay), "landing");
        private static readonly FieldInfo Taxiing = AccessTools.Field(typeof(AirbaseOverlay), "taxiingToRunway");
        private static readonly FieldInfo Reached = AccessTools.Field(typeof(AirbaseOverlay), "reachedRunway");
        private static readonly FieldInfo Marker = AccessTools.Field(typeof(AirbaseOverlay), "airbaseMarker");
        private static readonly FieldInfo Label = AccessTools.Field(typeof(AirbaseOverlay), "airbaseLabel");
        private static readonly FieldInfo Glideslope = AccessTools.Field(typeof(AirbaseOverlay), "glideslope");
        private static readonly FieldInfo GlideslopeAim = AccessTools.Field(typeof(AirbaseOverlay), "glideslopeAimPoint");
        private static readonly Action<AirbaseOverlay, Airbase.Runway> DrawBorders =
            AccessTools.MethodDelegate<Action<AirbaseOverlay, Airbase.Runway>>(
                AccessTools.Method(typeof(AirbaseOverlay), "DrawRunwayBorders"));
        private static readonly Func<AirbaseOverlay, Aircraft, Airbase.Runway.RunwayUsage, bool> DrawGlide =
            AccessTools.MethodDelegate<Func<AirbaseOverlay, Aircraft, Airbase.Runway.RunwayUsage, bool>>(
                AccessTools.Method(typeof(AirbaseOverlay), "DrawGlideslope"));

        public static void Refresh(AirbaseOverlay overlay, Aircraft aircraft, Camera camera)
        {
            if (overlay == null || !overlay.isActiveAndEnabled || aircraft == null || camera == null) return;
            bool landing = Landing?.GetValue(overlay) is bool isLanding && isLanding;
            object cachedUsage = Usage?.GetValue(overlay);
            Airbase.Runway.RunwayUsage? usage = cachedUsage is Airbase.Runway.RunwayUsage value ? value :
                (Airbase.Runway.RunwayUsage?)null;
            RefreshMarker(overlay, aircraft, camera, landing, usage);

            if (landing && usage.HasValue && usage.Value.Runway != null)
                DrawBorders(overlay, usage.Value.Runway);
            var glide = Glideslope?.GetValue(overlay) as Image;
            var aim = GlideslopeAim?.GetValue(overlay) as Image;
            if (glide == null || aim == null) return;
            // GetGlideslopeAimpoint is pure geometry; unlike native PositionMarkers and
            // UpdateNearestAirbase, these draw methods never advance runway/flight state.
            bool show = usage.HasValue && usage.Value.Runway != null && aircraft.gearDeployed &&
                DrawGlide(overlay, aircraft, usage.Value);
            glide.enabled = aim.enabled = show;
        }

        private static void RefreshMarker(AirbaseOverlay overlay, Aircraft aircraft, Camera camera,
            bool landing, Airbase.Runway.RunwayUsage? usage)
        {
            var marker = Marker?.GetValue(overlay) as Image;
            var label = Label?.GetValue(overlay) as Graphic;
            if (marker == null || label == null) return;
            var nearest = Nearest?.GetValue(overlay) as Airbase;
            bool taxiing = Taxiing?.GetValue(overlay) is bool isTaxiing && isTaxiing;
            bool show = nearest != null && nearest.center != null && !landing &&
                (aircraft.radarAlt >= 1f || taxiing);
            Vector3 world = show ? nearest.center.position : Vector3.zero;
            if (show && aircraft.pilots != null && aircraft.pilots.Length > 0 && aircraft.pilots[0] != null &&
                aircraft.pilots[0].flightInfo != null && !aircraft.pilots[0].flightInfo.HasTakenOff)
            {
                bool reached = Reached?.GetValue(overlay) is bool hasReached && hasReached;
                Transform start = usage.HasValue && usage.Value.Runway != null ? usage.Value.GetStart() : null;
                show = taxiing && !reached && start != null;
                if (show) world = start.position;
            }
            show &= Vector3.Dot(world - camera.transform.position, camera.transform.forward) >= 0f;
            marker.enabled = label.enabled = show;
            if (!show) return;
            bool pinned = HUDFunctions.PinToScreenEdge(world, out Vector3 point, out _);
            marker.transform.position = point;
            label.transform.position = point - (pinned ? point.normalized * 50f : Vector3.up * 20f);
        }
    }
}
