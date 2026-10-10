using System;
using System.Collections.Generic;
using System.Reflection;
using BoscaliSummer.Core.Diagnostics;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Prepares all flight/combat/weapon cue geometry before the canvas rebuild/batch boundary.
    /// URP's main-camera begin callback is a fallback only if the rendered view changed after
    /// that preparation; it rebuilds the canvas before drawing. Auxiliary cameras never consume
    /// that refresh. Each canvas preparation repairs native writes from earlier that frame;
    /// repeated preparation retains the same random jamming sample.
    /// No native Update/LateUpdate, input, weapon solver or smoothing is replayed. Nothing is
    /// written outside the owned third-person gate. Both subscriptions belong to HudBoard's
    /// Configure/OnEnable/OnDisable/OnDestroy lifetime.
    /// </summary>
    internal sealed class ThirdPersonHudCenter
    {
        // Cached field refs (no boxing, no per-call reflection): this runs on every canvas preparation.
        private static readonly AccessTools.FieldRef<CombatHUD, List<HUDUnitMarker>> Markers = NativeRefs.Field<CombatHUD, List<HUDUnitMarker>>("markers");
        private static readonly AccessTools.FieldRef<FlightHud, Canvas> HudCanvas = NativeRefs.Field<FlightHud, Canvas>("canvas");
        private static readonly AccessTools.FieldRef<CombatHUD, float> JamAccumulation = NativeRefs.Field<CombatHUD, float>("jamAccumulation");
        private static readonly AccessTools.FieldRef<CombatHUD, ObjectiveOverlayManager> ObjectiveOverlayRef = NativeRefs.Field<CombatHUD, ObjectiveOverlayManager>("objectiveOverlay");
        private static readonly AccessTools.FieldRef<ObjectiveOverlayManager, Aircraft> ObjectiveAircraft = NativeRefs.Field<ObjectiveOverlayManager, Aircraft>("aircraft");
        private static readonly AccessTools.FieldRef<ObjectiveOverlayManager, List<ObjectiveOverlay>> ObjectiveItems = NativeRefs.Field<ObjectiveOverlayManager, List<ObjectiveOverlay>>("overlays");
        private static readonly AccessTools.FieldRef<ObjectiveOverlayManager, List<MissionPosition.PositionResult>> ObjectiveResults = NativeRefs.Field<ObjectiveOverlayManager, List<MissionPosition.PositionResult>>("resultCache");
        private static readonly AccessTools.FieldRef<CombatHUD, TextMeshProUGUI> TargetInfo = NativeRefs.Field<CombatHUD, TextMeshProUGUI>("targetInfo");
        private static readonly AccessTools.FieldRef<CombatHUD, TextMeshProUGUI> TargetText = NativeRefs.Field<CombatHUD, TextMeshProUGUI>("targetText");
        private static readonly AccessTools.FieldRef<CombatHUD, Transform> TargetArrowTail = NativeRefs.Field<CombatHUD, Transform>("targetArrowTail");
        private static readonly AccessTools.FieldRef<AllyInfo, TextMeshProUGUI> AllyInfoText = NativeRefs.Field<AllyInfo, TextMeshProUGUI>("hoveredAllyInfo");
        private static readonly AccessTools.FieldRef<AllyInfo, HUDUnitMarker> AllyInfoMarker = NativeRefs.Field<AllyInfo, HUDUnitMarker>("hoveredAllyMarker");
        private static readonly Action<CombatHUD> UpdateHitMarkers = AccessTools.MethodDelegate<Action<CombatHUD>>(
            AccessTools.Method(typeof(CombatHUD), "UpdateHitMarkers"));
        private static readonly Func<CombatHUD, bool> ShowTargetInfo = AccessTools.MethodDelegate<Func<CombatHUD, bool>>(
            AccessTools.Method(typeof(CombatHUD), "ShowTargetInfo"));

        private Func<bool> gate;
        private Func<Aircraft> ownAircraft;
        private bool subscribed;
        private bool rebuildingCanvas;
        private int lastProjectionFrame = -1;
        private Camera lastCamera;
        private Aircraft lastAircraft;
        private FlightHud lastFlightHud;
        private CombatHUD lastCombatHud;
        private Canvas lastCanvas;
        private AllyInfo allyInfo;
        private AirbaseOverlay airbaseOverlay;
        private Matrix4x4 lastView, lastProjection;
        private Rect lastPixelRect;
        private readonly Dictionary<HUDUnitMarker, JammingSample> jamming = new Dictionary<HUDUnitMarker, JammingSample>();

        private struct JammingSample
        {
            public Vector3 Offset;
            public float Alpha;
        }

        public void Subscribe(Func<bool> isThirdPersonActive, Func<Aircraft> ownAircraftGetter)
        {
            gate = isThirdPersonActive;
            ownAircraft = ownAircraftGetter;
            if (subscribed) return;
            Canvas.preWillRenderCanvases += OnPreWillRenderCanvases;
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            subscribed = true;
        }

        public void Unsubscribe()
        {
            if (!subscribed) return;
            Canvas.preWillRenderCanvases -= OnPreWillRenderCanvases;
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            subscribed = false;
            lastProjectionFrame = -1;
            lastCamera = null;
            lastAircraft = null;
            lastFlightHud = null;
            lastCombatHud = null;
            lastCanvas = null;
            allyInfo = null;
            airbaseOverlay = null;
            jamming.Clear();
        }

        private void OnPreWillRenderCanvases()
        {
            // An early forced canvas preparation can precede native Update. Always repair
            // its later projection writes at the actual end-of-frame canvas preparation.
            if (!rebuildingCanvas) RefreshProjection(true);
        }

        private void OnBeginCameraRendering(ScriptableRenderContext context, Camera renderingCamera)
        {
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            // Target, relief, cockpit-overlay and preview cameras must not consume the main
            // view's refresh. URP calls this after LateUpdate and the floating-origin shift.
            if (cam == null || renderingCamera != cam.mainCamera) return;
            // Normally preWillRender prepared this exact view already. A late projection/pose
            // change needs both reprojection AND a canvas rebuild; updating Transform alone
            // after UGUI preparation leaves the drawn batch one camera pose behind.
            if (RefreshProjection(false) && !CanvasUpdateRegistry.IsRebuildingGraphics() && !CanvasUpdateRegistry.IsRebuildingLayout())
            {
                rebuildingCanvas = true;
                try { Canvas.ForceUpdateCanvases(); }
                finally { rebuildingCanvas = false; }
            }
        }

        private bool RefreshProjection(bool force)
        {
            try
            {
                if (gate == null || !gate()) return false;
                Aircraft aircraft = ownAircraft?.Invoke();
                if (aircraft == null || aircraft.cockpit == null) return false;

                FlightHud hud = SceneSingleton<FlightHud>.i;
                CombatHUD combatHud = SceneSingleton<CombatHUD>.i;
                CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
                if (hud == null || cam == null || cam.mainCamera == null) return false;
                Camera camera = cam.mainCamera;
                Canvas canvas = NativeRefs.Get(HudCanvas, hud);
                Matrix4x4 view = camera.worldToCameraMatrix, projection = camera.projectionMatrix;
                bool newFrame = lastProjectionFrame != Time.frameCount;
                if (!force && !newFrame && lastCamera == camera && lastAircraft == aircraft &&
                    lastFlightHud == hud && lastCombatHud == combatHud && lastCanvas == canvas && lastView == view &&
                    lastProjection == projection && lastPixelRect == camera.pixelRect) return false;
                Transform hudCenter = hud.GetHUDCenter();
                if (hudCenter == null) return false;
                if (newFrame) jamming.Clear();
                if (lastCanvas != canvas)
                {
                    allyInfo = canvas != null ? canvas.GetComponentInChildren<AllyInfo>(true) : null;
                    airbaseOverlay = canvas != null ? canvas.GetComponentInChildren<AirbaseOverlay>(true) : null;
                }

                Transform cockpit = aircraft.cockpit.transform;
                Vector3 aimPoint = cockpit.position + cockpit.forward * 4000f;
                Vector3 screenPoint = cam.mainCamera.WorldToScreenPoint(aimPoint);
                if (Vector3.Dot(cam.transform.forward, aimPoint - cam.transform.position) > 0f)
                    screenPoint = Vector3.Scale(screenPoint, new Vector3(1f, 1f, 0f));
                hudCenter.position = screenPoint;
                hudCenter.rotation = Quaternion.identity; // Levelled: no cockpit-bank roll in third person.

                Image velocityVector = hud.velocityVector;
                Rigidbody rb = aircraft.CockpitRB();
                if (velocityVector != null && rb != null)
                {
                    bool show = rb.velocity.magnitude > 10f;
                    if (velocityVector.gameObject.activeSelf != show) velocityVector.gameObject.SetActive(show);
                    if (show)
                    {
                        Vector3 velocityPoint = cockpit.position + rb.velocity * 1000f;
                        Vector3 velocityScreen = cam.mainCamera.WorldToScreenPoint(velocityPoint);
                        if (Vector3.Dot(cam.transform.forward, rb.velocity) > 0f)
                            velocityScreen = Vector3.Scale(velocityScreen, new Vector3(1f, 1f, 0f));
                        velocityVector.transform.position = velocityScreen;
                    }
                }

                if (combatHud != null && combatHud.aircraft == aircraft)
                {
                    // Only native projection methods: CombatHUD.LateUpdate also reads input,
                    // updates target cameras and decays jamming, so it must never be replayed.
                    List<HUDUnitMarker> markers = NativeRefs.Get(Markers, combatHud);
                    float jam = NativeRefs.Get(JamAccumulation, combatHud);
                    GlobalPosition viewPosition = cam.transform.GlobalPosition();
                    if (markers != null)
                        foreach (HUDUnitMarker marker in markers)
                            if (marker != null && marker.image != null)
                            {
                                marker.UpdatePosition(aircraft.NetworkHQ, viewPosition, cam.transform.forward);
                                if (jam > 0f)
                                {
                                    // A second canvas preparation after camera movement must
                                    // keep this frame's native distortion, not roll random
                                    // offsets/alpha again or erase the effect while reprojecting.
                                    if (!jamming.TryGetValue(marker, out JammingSample sample))
                                    {
                                        Vector3 position = marker.image.transform.position;
                                        marker.JammingDistortion(jam);
                                        sample.Offset = marker.image.transform.position - position;
                                        sample.Alpha = marker.image.color.a;
                                        jamming[marker] = sample;
                                    }
                                    else
                                    {
                                        marker.image.transform.position += sample.Offset;
                                        Color colour = marker.image.color;
                                        colour.a = sample.Alpha;
                                        marker.image.color = colour;
                                    }
                                }
                            }
                    UpdateHitMarkers(combatHud);
                    RefreshTargetCaptions(combatHud);
                    RefreshAllyCaption();
                    ThirdPersonWeaponProjection.Refresh(combatHud, aircraft, cam.mainCamera);
                    ThirdPersonAirbaseProjection.Refresh(airbaseOverlay, aircraft, cam.mainCamera);
                    RefreshObjectives(combatHud, aircraft);
                }
                lastProjectionFrame = Time.frameCount;
                lastCamera = camera;
                lastAircraft = aircraft;
                lastFlightHud = hud;
                lastCombatHud = combatHud;
                lastCanvas = canvas;
                lastView = view;
                lastProjection = projection;
                lastPixelRect = camera.pixelRect;
                return true;
            }
            catch (Exception e)
            {
                PatchGuard.Report("Hud.ThirdPersonHudCenter", e);
                return false;
            }
        }

        private void RefreshAllyCaption()
        {
            if (allyInfo == null || !allyInfo.isActiveAndEnabled) return;
            TextMeshProUGUI info = NativeRefs.Get(AllyInfoText, allyInfo);
            HUDUnitMarker marker = NativeRefs.Get(AllyInfoMarker, allyInfo);
            if (info != null && info.enabled && marker != null && marker.image != null)
                info.transform.localPosition = marker.image.transform.localPosition;
        }

        private static void RefreshTargetCaptions(CombatHUD combatHud)
        {
            TextMeshProUGUI info = NativeRefs.Get(TargetInfo, combatHud);
            // Native's presentation-only seam validates the current known target/arrow,
            // refreshes caption content and restores the selected theme colour after jamming.
            if (info != null) info.enabled = ShowTargetInfo(combatHud);
            TextMeshProUGUI text = NativeRefs.Get(TargetText, combatHud);
            Transform tail = NativeRefs.Get(TargetArrowTail, combatHud);
            if (text != null && text.enabled && tail != null) text.transform.position = tail.position;
        }

        private static void RefreshObjectives(CombatHUD combatHud, Aircraft aircraft)
        {
            ObjectiveOverlayManager manager = NativeRefs.Get(ObjectiveOverlayRef, combatHud);
            if (manager == null || !manager.isActiveAndEnabled || NativeRefs.Get(ObjectiveAircraft, manager) != aircraft) return;
            List<ObjectiveOverlay> overlays = NativeRefs.Get(ObjectiveItems, manager);
            List<MissionPosition.PositionResult> results = NativeRefs.Get(ObjectiveResults, manager);
            if (overlays == null || results == null) return;
            for (int i = 0; i < overlays.Count && i < results.Count; i++)
            {
                ObjectiveOverlay overlay = overlays[i];
                if (overlay == null) continue;
                TextNoOverlap text = overlay.TextNoOverlap;
                if (text == null || text.Text == null) continue;
                Vector2 previousTarget = text.TargetPosition;
                Vector3 previousText = text.Text.transform.position;
                // Reuse native's cached mission result; do not query objectives, create items,
                // or repeat StopTextOverlap's smoothing/nudge decay. Move its existing label
                // offset with the newly projected pointer so camera motion adds no text lag.
                overlay.UpdateOverlay(results[i]);
                Vector2 delta = text.TargetPosition - previousTarget;
                text.PreviousPosition += delta;
                text.Text.transform.position = previousText + (Vector3)delta;
            }
        }
    }
}
