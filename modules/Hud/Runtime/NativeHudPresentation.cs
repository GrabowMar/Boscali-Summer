using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Hud.Runtime
{
    /// <summary>Temporarily hides replaced instruments, while native scripts and weapon cues keep running.</summary>
    internal sealed class NativeHudPresentation
    {
        private static readonly FieldInfo Compass = AccessTools.Field(typeof(FlightHud), "compass");
        private static readonly FieldInfo Pitch = AccessTools.Field(typeof(FlightHud), "pitchCompassCenter");
        private static readonly FieldInfo Airspeed = AccessTools.Field(typeof(SpeedGauge), "airspeedDisplay");
        private static readonly FieldInfo SpeedBorder = AccessTools.Field(typeof(SpeedGauge), "border");
        private static readonly FieldInfo AngleOfAttack = AccessTools.Field(typeof(AoADisplay), "AoAText");
        private readonly List<Hidden> hidden = new List<Hidden>(32);
        private float nextScan;
        private FlightHud owner;
        private struct Hidden { public CanvasGroup Group; public float Alpha; public bool Added; }
        public void Apply(FlightHud hud)
        {
            if (owner != hud) { Dispose(); owner = hud; }
            if (hud == null) return;
            if (Time.unscaledTime >= nextScan)
            {
                nextScan = Time.unscaledTime + 1;
                if (Compass?.GetValue(hud) is RawImage compass) Hide(compass.gameObject);
                if (Pitch?.GetValue(hud) is GameObject pitch) Hide(pitch);
                // Specific native instruments only: never hide weapon states, map, warnings or reticles.
                foreach (HUDApp app in hud.GetComponentsInChildren<HUDApp>(true))
                {
                    if (app is SpeedGauge)
                    { HideGraphic(app, Airspeed); HideGraphic(app, SpeedBorder); }
                    else if (app is AoADisplay) HideGraphic(app, AngleOfAttack);
                    else if (app is Altitude || app is Climbrate || app is Bearing || app is ArtificialHorizon ||
                        app is FuelGauge || app is ThrottleGauge || app is GIndicators || app is MachIndicator) Hide(app.gameObject);
                }
            }
            foreach (Hidden entry in hidden) if (entry.Group != null) entry.Group.alpha = 0;
        }
        private void HideGraphic(HUDApp app, FieldInfo field)
        {
            // Native stall/overspeed labels remain visible.
            if (field?.GetValue(app) is Graphic graphic) Hide(graphic.gameObject);
        }
        private void Hide(GameObject target)
        {
            if (hidden.Count >= 32) return;
            foreach (Hidden entry in hidden) if (entry.Group != null && entry.Group.gameObject == target) return;
            CanvasGroup group = target.GetComponent<CanvasGroup>(); bool added = group == null;
            if (added) group = target.AddComponent<CanvasGroup>();
            hidden.Add(new Hidden { Group = group, Alpha = group.alpha, Added = added }); group.alpha = 0;
        }
        public void Restore()
        {
            // Retain owned groups until teardown: disable/re-enable can occur in the same frame,
            // and Unity's deferred Destroy must not remove a group we have just rebound.
            foreach (Hidden entry in hidden) if (entry.Group != null) entry.Group.alpha = entry.Alpha;
        }
        public void Dispose()
        {
            Restore();
            foreach (Hidden entry in hidden) if (entry.Added && entry.Group != null) UnityEngine.Object.Destroy(entry.Group);
            hidden.Clear(); owner = null; nextScan = 0;
        }

    }

    /// <summary>Late visual projection only. Never reruns native input, weapons, apps or jamming simulation.</summary>
    internal sealed class NativeHudProjection
    {
        private static readonly Action<FlightHud> ProjectFlight = AccessTools.MethodDelegate<Action<FlightHud>>(AccessTools.Method(typeof(FlightHud), "Update"));
        private static readonly Func<CombatHUD, bool> ProjectTargetInfo = AccessTools.MethodDelegate<Func<CombatHUD, bool>>(AccessTools.Method(typeof(CombatHUD), "ShowTargetInfo"));
        private static readonly FieldInfo TargetInfo = AccessTools.Field(typeof(CombatHUD), "targetInfo");
        private readonly Dictionary<HUDUnitMarker, int> indices = new Dictionary<HUDUnitMarker, int>(256);
        private readonly HUDUnitMarker[] markers = new HUDUnitMarker[256];
        private readonly Vector3[] distortion = new Vector3[256];
        private int count, collectedFrame = -1, renderedFrame = -1, revision, renderedRevision = -1;
        private Matrix4x4 viewMatrix, projectionMatrix;
        private bool projecting;
        private Vector3 datum;
        private int width, height;
        public void Record(HUDUnitMarker marker)
        {
            if (projecting) return;
            if (collectedFrame != Time.frameCount) { Clear(); collectedFrame = Time.frameCount; }
            if (indices.TryGetValue(marker, out int index)) distortion[index] = Vector3.zero;
            else
            {
                if (count >= markers.Length) return;
                indices.Add(marker, count); markers[count] = marker; distortion[count++] = Vector3.zero;
            }
            // Every native write replaces the base projection; its previous distortion is spent.
            revision++;
        }
        public void RecordDistortion(HUDUnitMarker marker, Vector3 offset)
        {
            if (projecting || collectedFrame != Time.frameCount) return;
            if (indices.TryGetValue(marker, out int index)) { distortion[index] += offset; revision++; }
        }
        public void Render(Aircraft aircraft, CameraStateManager camera)
        {
            if (projecting || camera == null || camera.mainCamera == null || aircraft == null) return;
            Camera lens = camera.mainCamera;
            bool shifted = datum != Datum.originPosition || width != Screen.width || height != Screen.height;
            // ForceUpdateCanvases may run early. A frame number alone does not identify the final view.
            if (renderedFrame == Time.frameCount && renderedRevision == revision && !shifted &&
                viewMatrix == lens.worldToCameraMatrix && projectionMatrix == lens.projectionMatrix) return;
            projecting = true;
            try
            {
                if (shifted)
                {
                    datum = Datum.originPosition; width = Screen.width; height = Screen.height;
                    Canvas.ForceUpdateCanvases();
                }
                FlightHud hud = SceneSingleton<FlightHud>.i;
                if (hud != null && hud.isActiveAndEnabled) ProjectFlight(hud);
                if (collectedFrame != Time.frameCount || aircraft.NetworkHQ == null) return;
                GlobalPosition origin = camera.transform.GlobalPosition(); Vector3 forward = camera.transform.forward;
                for (int i = 0; i < count; i++)
                {
                    HUDUnitMarker marker = markers[i];
                    if (marker?.image == null || marker.unit == null) continue;
                    Color nativeColor = marker.image.color;
                    marker.UpdatePosition(aircraft.NetworkHQ, origin, forward);
                    marker.image.transform.position += distortion[i];
                    marker.image.color = nativeColor; // Preserve native ECM opacity/flash decisions.
                }
                // Native target text is a sibling, not a child of the selected marker.
                // Moving just the marker leaves its name/range one camera pose behind.
                CombatHUD combat = SceneSingleton<CombatHUD>.i;
                if (combat != null && combat.aircraft == aircraft && TargetInfo?.GetValue(combat) is TMPro.TMP_Text info)
                    info.enabled = ProjectTargetInfo(combat);
            }
            finally
            {
                viewMatrix = lens.worldToCameraMatrix; projectionMatrix = lens.projectionMatrix;
                renderedFrame = Time.frameCount; renderedRevision = revision; projecting = false;
            }
        }
        public void Reset() { Clear(); collectedFrame = renderedFrame = renderedRevision = -1; revision = 0; width = height = 0; }
        private void Clear() { Array.Clear(markers, 0, count); Array.Clear(distortion, 0, count); indices.Clear(); count = 0; }
    }
}
