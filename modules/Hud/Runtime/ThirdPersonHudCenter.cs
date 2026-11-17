using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Ui;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// THE NATIVE REPAIR (AV LAYER 2.1): the game projects its HUD (unit markers, hit markers, captions, weapon cues, airbase, objectives, FlightHud's centre/FPM/ladder)
    /// in Update/LateUpdate against the camera pose of before <c>CameraStateManager.LateUpdate</c> (shake, head, FOV, pan): one step behind what is drawn. This reprojects
    /// all of it in <see cref="HudLayer.Repair"/> (the late point, after the final pose), in the cockpit and the external views alike. Cockpit: FlightHud.Update is replayed
    /// (pure presentation). External: the centre is levelled and the velocity vector forced, as before. Unit markers are projected once per frame: while
    /// <see cref="OwnsMarkers"/> the game's own <c>UpdatePosition</c>/<c>JammingDistortion</c> calls inside <c>CombatHUD.UpdateMarkers</c> are skipped
    /// (<c>Patches/MarkerPatches.cs</c>) and this runs them, with the range ink (<see cref="MarkerInk"/>) in between. FAIL OPEN: the skip holds only when last frame's
    /// repair projected the markers and the layer saw no throw, so one failed frame hands placement back to the game. No native Update/LateUpdate input, weapon solver or
    /// smoothing is replayed.
    /// </summary>
    internal static class ThirdPersonHudCenter
    {
        // Cached field refs (no boxing, no per-call reflection): this runs on every late pass.
        private static readonly AccessTools.FieldRef<CombatHUD, List<HUDUnitMarker>> Markers = NativeRefs.Field<CombatHUD, List<HUDUnitMarker>>("markers");
        private static readonly AccessTools.FieldRef<FlightHud, Canvas> HudCanvas = NativeRefs.Field<FlightHud, Canvas>("canvas");
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
        // FlightHud.Update is pure presentation (HUDCenter/FPM position, centre and ladder roll, ladder scale, compass uvRects, an idempotent SetActive): safe to replay.
        private static readonly Action<FlightHud> FlightHudUpdate = AccessTools.MethodDelegate<Action<FlightHud>>(
            AccessTools.Method(typeof(FlightHud), "Update"));

        private static Func<Aircraft> own;
        private static Func<bool> external;
        private static bool installed;
        private static int markersFrame = -1;
        private static Canvas lastCanvas;
        private static AllyInfo allyInfo;
        private static AirbaseOverlay airbaseOverlay;

        /// <summary>
        /// The game's own marker projection is skipped now: inside <c>CombatHUD.UpdateMarkers</c>, the layer on, and last frame's repair both projected the markers and
        /// finished without a throw (fail open).
        /// </summary>
        public static bool OwnsMarkers
        {
            get
            {
                if (!HudLayer.InNativeMarkers || !HudLayer.Enabled) return false;
                int last = Time.frameCount - 1;
                return markersFrame == last && HudLayer.RepairOkFrame == last;
            }
        }

        /// <summary>
        /// Repair the native HUD of the aircraft <paramref name="ownAircraft"/> answers (null: nothing this frame, e.g. no HUD up or not the pilot's own view) at the late
        /// point; <paramref name="externalView"/> true = it is seen from outside (levelled centre, forced velocity vector), false = the cockpit (FlightHud replay). Idempotent.
        /// </summary>
        public static void Install(Func<Aircraft> ownAircraft, Func<bool> externalView)
        {
            own = ownAircraft;
            external = externalView;
            if (installed) return;
            HudLayer.Repair += RefreshProjection;
            installed = true;
        }

        public static void Uninstall()
        {
            if (!installed) return;
            HudLayer.Repair -= RefreshProjection;
            installed = false;
            own = null;
            external = null;
            markersFrame = -1;
            lastCanvas = null;
            allyInfo = null;
            airbaseOverlay = null;
            MarkerInk.Watch(null);
        }

        private static void RefreshProjection(HudView view)
        {
            // Throws reach the layer on purpose: it logs them and stops RepairOkFrame, which hands the markers back to the game (fail open).
            Aircraft aircraft = own?.Invoke();
            if (aircraft == null || aircraft.cockpit == null) return;
            FlightHud hud = SceneSingleton<FlightHud>.i;
            CombatHUD combatHud = SceneSingleton<CombatHUD>.i;
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            Camera camera = view.Cam;
            if (hud == null || cam == null || camera == null) return;

            if (external != null && external()) LevelCentre(hud, aircraft, cam, camera);
            else if (hud.isActiveAndEnabled) FlightHudUpdate(hud);

            if (combatHud == null || combatHud.aircraft != aircraft) return;
            Canvas canvas = NativeRefs.Get(HudCanvas, hud);
            if (lastCanvas != canvas)
            {
                allyInfo = canvas != null ? canvas.GetComponentInChildren<AllyInfo>(true) : null;
                airbaseOverlay = canvas != null ? canvas.GetComponentInChildren<AirbaseOverlay>(true) : null;
                lastCanvas = canvas;
            }

            // Only native projection methods: CombatHUD.LateUpdate also reads input, updates target cameras and decays jamming, so it is never replayed.
            List<HUDUnitMarker> markers = NativeRefs.Get(Markers, combatHud);
            float jam = combatHud.jamAccumulation;
            GlobalPosition viewPosition = cam.transform.GlobalPosition();
            Vector3 forward = cam.transform.forward;
            FactionHQ hq = aircraft.NetworkHQ;
            MarkerInk.Watch(aircraft);
            if (markers != null)
                for (int i = 0; i < markers.Count; i++)
                {
                    HUDUnitMarker marker = markers[i];
                    if (marker == null || marker.image == null) continue;
                    marker.UpdatePosition(hq, viewPosition, forward);
                    MarkerInk.Apply(marker, hq, viewPosition, jam);
                    if (jam > 0f) marker.JammingDistortion(jam);
                }
            markersFrame = view.Frame;

            UpdateHitMarkers(combatHud);
            RefreshTargetCaptions(combatHud);
            RefreshAllyCaption();
            ThirdPersonWeaponProjection.Refresh(combatHud, aircraft, camera);
            ThirdPersonAirbaseProjection.Refresh(airbaseOverlay, aircraft, camera);
            RefreshObjectives(combatHud, aircraft);
        }

        /// <summary>External views only: the HUD centre on the nose aim point without the cockpit's bank roll, and the velocity vector the game leaves to the cockpit.</summary>
        private static void LevelCentre(FlightHud hud, Aircraft aircraft, CameraStateManager cam, Camera camera)
        {
            Transform hudCenter = hud.GetHUDCenter();
            if (hudCenter == null) return;
            Transform cockpit = aircraft.cockpit.transform;
            Vector3 aimPoint = cockpit.position + cockpit.forward * 4000f;
            Vector3 screenPoint = camera.WorldToScreenPoint(aimPoint);
            if (Vector3.Dot(cam.transform.forward, aimPoint - cam.transform.position) > 0f) screenPoint.z = 0f;
            hudCenter.position = screenPoint;
            hudCenter.rotation = Quaternion.identity;

            Image velocityVector = hud.velocityVector;
            Rigidbody rb = aircraft.CockpitRB();
            if (velocityVector == null || rb == null) return;
            bool show = rb.velocity.magnitude > 10f;
            if (velocityVector.gameObject.activeSelf != show) velocityVector.gameObject.SetActive(show);
            if (!show) return;
            Vector3 velocityScreen = camera.WorldToScreenPoint(cockpit.position + rb.velocity * 1000f);
            if (Vector3.Dot(cam.transform.forward, rb.velocity) > 0f) velocityScreen.z = 0f;
            velocityVector.transform.position = velocityScreen;
        }

        private static void RefreshAllyCaption()
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
