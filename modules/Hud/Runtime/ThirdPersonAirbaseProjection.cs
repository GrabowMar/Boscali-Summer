using System;
using TMPro;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    // Native AirbaseOverlay updates before the camera's LateUpdate. Reproject its cached
    // choice without advancing taxi/landing registration, reachedRunway or takeoff state.
    internal static class ThirdPersonAirbaseProjection
    {
        private static readonly AccessTools.FieldRef<AirbaseOverlay, Airbase> Nearest = NativeRefs.Field<AirbaseOverlay, Airbase>("nearestAirbase");
        private static readonly AccessTools.FieldRef<AirbaseOverlay, Airbase.Runway.RunwayUsage?> Usage = NativeRefs.Field<AirbaseOverlay, Airbase.Runway.RunwayUsage?>("runwayUsage");
        private static readonly AccessTools.FieldRef<AirbaseOverlay, bool> Landing = NativeRefs.Field<AirbaseOverlay, bool>("landing");
        private static readonly AccessTools.FieldRef<AirbaseOverlay, bool> Taxiing = NativeRefs.Field<AirbaseOverlay, bool>("taxiingToRunway");
        private static readonly AccessTools.FieldRef<AirbaseOverlay, bool> Reached = NativeRefs.Field<AirbaseOverlay, bool>("reachedRunway");
        private static readonly AccessTools.FieldRef<AirbaseOverlay, Image> Marker = NativeRefs.Field<AirbaseOverlay, Image>("airbaseMarker");
        private static readonly AccessTools.FieldRef<AirbaseOverlay, TextMeshProUGUI> Label = NativeRefs.Field<AirbaseOverlay, TextMeshProUGUI>("airbaseLabel");
        private static readonly AccessTools.FieldRef<AirbaseOverlay, Image> Glideslope = NativeRefs.Field<AirbaseOverlay, Image>("glideslope");
        private static readonly AccessTools.FieldRef<AirbaseOverlay, Image> GlideslopeAim = NativeRefs.Field<AirbaseOverlay, Image>("glideslopeAimPoint");
        private static readonly Action<AirbaseOverlay, Airbase.Runway> DrawBorders =
            AccessTools.MethodDelegate<Action<AirbaseOverlay, Airbase.Runway>>(
                AccessTools.Method(typeof(AirbaseOverlay), "DrawRunwayBorders"));
        private static readonly Func<AirbaseOverlay, Aircraft, Airbase.Runway.RunwayUsage, bool> DrawGlide =
            AccessTools.MethodDelegate<Func<AirbaseOverlay, Aircraft, Airbase.Runway.RunwayUsage, bool>>(
                AccessTools.Method(typeof(AirbaseOverlay), "DrawGlideslope"));

        public static void Refresh(AirbaseOverlay overlay, Aircraft aircraft, Camera camera)
        {
            if (overlay == null || !overlay.isActiveAndEnabled || aircraft == null || camera == null) return;
            bool landing = NativeRefs.Get(Landing, overlay);
            Airbase.Runway.RunwayUsage? usage = NativeRefs.Get(Usage, overlay);
            RefreshMarker(overlay, aircraft, camera, landing, usage);

            if (landing && usage.HasValue && usage.Value.Runway != null)
                DrawBorders(overlay, usage.Value.Runway);
            Image glide = NativeRefs.Get(Glideslope, overlay);
            Image aim = NativeRefs.Get(GlideslopeAim, overlay);
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
            Image marker = NativeRefs.Get(Marker, overlay);
            Graphic label = NativeRefs.Get(Label, overlay);
            if (marker == null || label == null) return;
            Airbase nearest = NativeRefs.Get(Nearest, overlay);
            bool taxiing = NativeRefs.Get(Taxiing, overlay);
            bool show = nearest != null && nearest.center != null && !landing &&
                (aircraft.radarAlt >= 1f || taxiing);
            Vector3 world = show ? nearest.center.position : Vector3.zero;
            if (show && aircraft.pilots != null && aircraft.pilots.Length > 0 && aircraft.pilots[0] != null &&
                aircraft.pilots[0].flightInfo != null && !aircraft.pilots[0].flightInfo.HasTakenOff)
            {
                bool reached = NativeRefs.Get(Reached, overlay);
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
