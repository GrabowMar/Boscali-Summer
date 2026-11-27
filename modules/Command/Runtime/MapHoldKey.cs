using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Ui;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Domain;
using BoscaliSummer.Modules.Command.Presentation;
using HarmonyLib;
using NuclearOption.MissionEditorScripts;
using NuclearOption.UI;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Runtime
{
    /// <summary>
    /// MAP HOLD KEY (default V): hold while flying to enlarge a translucent, decentered tactical Quick Map over the cockpit
    /// with scroll/+- zoom, N key North-Up/Heading-Up toggle, airbases, frontline/sector overlay, range rings, and grid/RTB cues.
    /// Runs after <c>DynamicMap.Update</c> and cleanly restores the corner minimap and camera zoom when released or when Maximize opens.
    /// </summary>
    [DefaultExecutionOrder(120)]
    internal sealed class MapHoldKey : MonoBehaviour, ISceneService
    {
        private const float CenterLiftShare = 0.055f;
        private const float BaseRefreshInterval = 0.5f;

        private static readonly AccessTools.FieldRef<DynamicMap, RectTransform> MapRect = AccessTools.FieldRefAccess<DynamicMap, RectTransform>("mapRectTransform");
        private static readonly AccessTools.FieldRef<DynamicMap, RectTransform> BackRect = AccessTools.FieldRefAccess<DynamicMap, RectTransform>("backgroundRectTransform");
        private static readonly AccessTools.FieldRef<DynamicMap, Dictionary<Airbase, AirbaseMapIcon>> AirbasesRef =
            AccessTools.FieldRefAccess<DynamicMap, Dictionary<Airbase, AirbaseMapIcon>>("airbaseIconLookup");
        private static readonly AccessTools.FieldRef<CameraCockpitState, float> CockpitFovAdj =
            AccessTools.FieldRefAccess<CameraCockpitState, float>("FOVAdjustment");
        private static readonly AccessTools.FieldRef<CameraOrbitState, float> OrbitDistAdj =
            AccessTools.FieldRefAccess<CameraOrbitState, float>("viewDistAdjust");

        internal static MapHoldKey Live { get; private set; }
        internal static bool ShowOverlays { get; private set; }

        private readonly QuickMapOverlay overlay = new QuickMapOverlay();

        private CommandSettings settings;
        private DynamicMap grown;
        private Image image;
        private float imageAlpha = 1f, savedScale = 2f;
        private bool savedBgEnabled;
        private Color savedBgColor;
        private Vector3 restWorldPos;

        private float progress;
        private float zoom = QuickMapMath.DefaultZoom;
        private bool northUp, zoomInitialized;
        private bool heldLastFrame;
        private bool showingAirbases;
        private float nextBaseSample;
        private Airbase cachedBase;
        private string cachedBaseName = "", cachedGrid = "";
        private float cachedBaseDist = float.NaN;
        private int cachedBaseBearing;

        // Dev sim automation overrides (in-memory only).
        internal bool? HoldOverride { get; set; }
        internal float? ZoomOverride { get; set; }
        internal bool? NorthUpOverride { get; set; }
        internal bool? OverlaysOverride { get; set; }

        internal bool Active => progress > 0.01f && grown != null;
        internal float Progress => progress;
        internal float ActiveZoom => Mathf.Clamp(ZoomOverride ?? zoom, QuickMapMath.MinZoom, QuickMapMath.MaxZoom);
        internal bool ActiveNorthUp => NorthUpOverride ?? northUp;
        internal bool ActiveOverlays => OverlaysOverride ?? settings?.QuickMapOverlays.Value ?? true;
        internal float LastSidePx { get; private set; }
        internal float LastRingStepMetres => overlay.LastRingStepMetres;
        internal string LastNearestBase => overlay.LastNearestBase;
        internal string LastGrid => overlay.LastGrid;

        public void Configure(CommandSettings config)
        {
            settings = config;
            Live = this;
        }

        private void LateUpdate()
        {
            if (settings == null) return;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            Aircraft aircraft = null;
            bool want = map != null && Wanted(map, out aircraft);

            if (grown != null && (map == null || grown != map || DynamicMap.mapMaximized))
            {
                RestoreImmediate();
                return;
            }

            if (want && grown == null) CaptureRestState(map);

            if (want)
            {
                if (!heldLastFrame)
                {
                    northUp = settings.QuickMapNorthUp.Value;
                    if (!zoomInitialized) { zoom = QuickMapMath.DefaultZoom; zoomInitialized = true; }
                    nextBaseSample = 0f;
                }
                HandleLiveControls();
            }
            heldLastFrame = want;

            if (grown == null) return;

            bool instant = settings.AvionicsReducedMotion.Value || (HoldOverride.HasValue);
            float dur = want ? QuickMapMath.OpenSeconds : QuickMapMath.CloseSeconds;
            if (instant) progress = want ? 1f : 0f;
            else progress = Mathf.MoveTowards(progress, want ? 1f : 0f, Time.unscaledDeltaTime / dur);

            if (progress <= 0.001f && !want)
            {
                RestoreImmediate();
                return;
            }

            PresentFrame(grown, aircraft, want);
        }

        private bool Wanted(DynamicMap map, out Aircraft aircraft)
        {
            aircraft = null;
            if (DynamicMap.mapMaximized || !map.gameObject.activeInHierarchy) return false;
            if (!GameManager.GetLocalAircraft(out aircraft) || aircraft == null || aircraft.disabled) return false;
            if (HoldOverride.HasValue) return HoldOverride.Value;
            KeyCode key = settings.MapHoldKey.Value;
            if (key == KeyCode.None || !Input.GetKey(key)) return false;
            if (InputFieldChecker.InsideInputField || !CockpitOverlayGate.NotSuppressed() || MapTool.Typing()) return false;
            return true;
        }

        private void CaptureRestState(DynamicMap map)
        {
            grown = map;
            image = map.mapImage != null ? map.mapImage.GetComponent<Image>() : null;
            imageAlpha = image != null ? image.color.a : 1f;
            savedScale = map.mapImage != null ? Mathf.Max(0.1f, map.mapImage.transform.localScale.x) : 2f;
            savedBgEnabled = map.mapBackground != null && map.mapBackground.enabled;
            savedBgColor = map.mapBackground != null ? map.mapBackground.color : Color.black;
            restWorldPos = map.transform.position;
        }

        private void HandleLiveControls()
        {
            float notches = 0f;
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                notches += scroll;
                CancelCameraWheelZoom(scroll);
            }
            if (Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadPlus)) notches += 1f;
            if (Input.GetKeyDown(KeyCode.Minus) || Input.GetKeyDown(KeyCode.KeypadMinus)) notches -= 1f;
            if (Mathf.Abs(notches) > 0.001f)
            {
                zoom = QuickMapMath.StepZoom(zoom, notches);
                if (ZoomOverride.HasValue) ZoomOverride = zoom;
            }
            if (Input.GetKeyDown(KeyCode.N))
            {
                northUp = !northUp;
                if (NorthUpOverride.HasValue) NorthUpOverride = northUp;
            }
        }

        private static void CancelCameraWheelZoom(float scroll)
        {
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null) return;
            if (cam.currentState is CameraCockpitState cockpit && CockpitFovAdj != null)
            {
                CockpitFovAdj(cockpit) += 5f * scroll;
            }
            else if (cam.currentState is CameraOrbitState orbit && OrbitDistAdj != null)
            {
                OrbitDistAdj(orbit) += 0.05f * scroll * 10f;
            }
        }

        private void PresentFrame(DynamicMap map, Aircraft aircraft, bool opening)
        {
            Canvas canvas = map.GetComponentInParent<Canvas>();
            if (canvas == null) return;

            float eased = QuickMapMath.Ease(progress, opening);
            MapTool.QuickMapActive = true;
            ShowOverlays = ActiveOverlays && eased > 0.15f;

            var screen = (RectTransform)canvas.rootCanvas.transform;
            float scale = Mathf.Max(1e-4f, MapRect(map).lossyScale.y);
            float sizeShare = Mathf.Clamp(settings.QuickMapSize.Value, 0.50f, 0.90f);
            float targetSide = screen.rect.height * screen.lossyScale.y * sizeShare / scale;
            float curSide = Mathf.Lerp(map.mapScaleMinimized, targetSide, eased);
            LastSidePx = curSide;

            RectTransform mapRect = MapRect(map);
            mapRect.sizeDelta = Vector2.one * curSide;
            BackRect(map).sizeDelta = mapRect.sizeDelta + new Vector2(20f, 20f);

            Vector3 liftedCenter = screen.position + Vector3.up * (screen.rect.height * screen.lossyScale.y * CenterLiftShare);
            map.transform.position = Vector3.Lerp(restWorldPos, liftedCenter, eased);

            float opacity = Mathf.Clamp(settings.QuickMapOpacity.Value, 0.20f, 0.90f);
            if (image != null)
            {
                Color c = image.color;
                c.a = imageAlpha * Mathf.Lerp(1f, opacity, eased);
                image.color = c;
            }
            if (map.mapBackground != null)
            {
                map.mapBackground.enabled = true;
                map.mapBackground.color = new Color(0.015f, 0.045f, 0.035f, Mathf.Clamp(opacity * 0.72f, 0.18f, 0.65f) * eased);
            }

            float targetZoom = ActiveZoom;
            float appliedScale = Mathf.Lerp(savedScale, targetZoom, eased);
            if (map.mapImage != null) map.mapImage.transform.localScale = Vector3.one * appliedScale;

            bool useNorth = ActiveNorthUp;
            float heading = aircraft != null ? aircraft.transform.eulerAngles.y : 0f;
            Transform camT = SceneSingleton<CameraStateManager>.i != null ? SceneSingleton<CameraStateManager>.i.transform : null;
            if (aircraft != null && map.mapImage != null && camT != null)
            {
                Vector3 camWorld = camT.position.ToGlobalPosition().AsVector3() * map.mapDisplayFactor;
                Vector3 fwd = aircraft.transform.forward;
                fwd.y = 0f;
                Vector3 fwdNorm = fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
                float lookAhead = Mathf.Lerp(QuickMapMath.CornerLookAheadMetres,
                    QuickMapMath.LookAheadMetres(curSide, map.mapDisplayFactor, appliedScale, useNorth), eased);
                Vector3 focus = useNorth ? camWorld : camWorld + fwdNorm * map.mapDisplayFactor * lookAhead;

                float rotZ = Mathf.LerpAngle(heading, useNorth ? 0f : heading, eased);
                map.mapImage.transform.eulerAngles = new Vector3(0f, 0f, rotZ);
                float bgScale = map.mapBackground != null ? map.mapBackground.transform.localScale.x : 1f;
                map.mapImage.transform.localPosition = (map.mapImage.transform.right * -focus.x + map.mapImage.transform.up * -focus.z) * appliedScale * bgScale;
                if (map.viewIndicator != null)
                {
                    map.viewIndicator.transform.localPosition = new Vector3(camWorld.x, camWorld.z, 0f);
                    map.viewIndicator.transform.eulerAngles = new Vector3(0f, 0f, rotZ - heading);
                }
            }

            SyncAirbaseVisibility(map, appliedScale);
            SampleTelemetry(map, aircraft);

            bool imperial = PlayerSettings.unitSystem == PlayerSettings.UnitSystem.Imperial;
            overlay.Present(mapRect, eased, curSide, appliedScale, map.mapDisplayFactor,
                useNorth, imperial, heading, cachedGrid, cachedBaseName, cachedBaseDist, cachedBaseBearing);
        }

        private void SyncAirbaseVisibility(DynamicMap map, float appliedScale)
        {
            bool show = ShowOverlays && SceneSingleton<MapOptions>.i != null && SceneSingleton<MapOptions>.i.showAirbaseIcon;
            if (show == showingAirbases) return;
            showingAirbases = show;
            Dictionary<Airbase, AirbaseMapIcon> lookup = AirbasesRef(map);
            if (lookup == null || map.mapImage == null) return;
            float invScale = 1f / Mathf.Max(0.1f, appliedScale);
            foreach (AirbaseMapIcon icon in lookup.Values)
                if (icon != null) icon.UpdateIcon(map.mapDisplayFactor, invScale, map.mapImage.transform, false);
        }

        private void SampleTelemetry(DynamicMap map, Aircraft aircraft)
        {
            float now = Time.unscaledTime;
            if (aircraft == null) return;
            if (now >= nextBaseSample)
            {
                nextBaseSample = now + BaseRefreshInterval;
                cachedGrid = map.gridLabels != null ? map.gridLabels.GetGridPosition(aircraft.GlobalPosition()) : "";
                if (aircraft.NetworkHQ != null)
                {
                    var query = new RunwayQuery { RunwayType = RunwayQueryType.Landing, MinSize = 0f, LandingSpeed = 0f, TailHook = false };
                    Airbase found = aircraft.NetworkHQ.GetNearestAirbase(aircraft.transform.position, query);
                    if (found != cachedBase)
                    {
                        cachedBase = found;
                        cachedBaseName = found != null ? MfdAirbaseFormatter.Format(found) : "";
                    }
                }
            }
            if (cachedBase != null && cachedBase.center != null)
            {
                Vector3 to = cachedBase.center.position - aircraft.transform.position;
                cachedBaseDist = new Vector2(to.x, to.z).magnitude;
                cachedBaseBearing = QuickMapMath.BearingDeg(to.x, to.z);
            }
            else
            {
                cachedBaseDist = float.NaN;
                cachedBaseBearing = 0;
            }
        }

        private void RestoreImmediate()
        {
            DynamicMap map = grown;
            grown = null;
            progress = 0f;
            heldLastFrame = false;
            MapTool.QuickMapActive = false;
            ShowOverlays = false;
            showingAirbases = false;
            LastSidePx = 0f;
            overlay.Hide();

            if (image != null)
            {
                Color c = image.color;
                c.a = imageAlpha;
                image.color = c;
            }
            image = null;

            if (map == null || DynamicMap.mapMaximized) return;
            map.transform.localPosition = Vector3.zero;
            MapRect(map).sizeDelta = Vector2.one * map.mapScaleMinimized;
            BackRect(map).sizeDelta = MapRect(map).sizeDelta + new Vector2(20f, 20f);
            if (map.mapImage != null) map.mapImage.transform.localScale = Vector3.one * savedScale;
            if (map.mapBackground != null)
            {
                map.mapBackground.enabled = savedBgEnabled;
                map.mapBackground.color = savedBgColor;
            }
            Dictionary<Airbase, AirbaseMapIcon> lookup = AirbasesRef(map);
            if (lookup != null)
                foreach (AirbaseMapIcon icon in lookup.Values)
                    if (icon != null && icon.gameObject.activeSelf) icon.gameObject.SetActive(false);
        }

        public void ResetForScene()
        {
            RestoreImmediate();
            HoldOverride = null;
            ZoomOverride = null;
            NorthUpOverride = null;
            OverlaysOverride = null;
            zoom = QuickMapMath.DefaultZoom;
            zoomInitialized = false;
            cachedBase = null;
            cachedBaseName = cachedGrid = "";
            overlay.Destroy();
        }

        private void OnDisable() => RestoreImmediate();

        private void OnDestroy()
        {
            if (Live == this) Live = null;
            ResetForScene();
        }
    }
}
