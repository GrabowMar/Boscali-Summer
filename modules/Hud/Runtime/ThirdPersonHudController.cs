using System.Reflection;
using HarmonyLib;
using NuclearOption.MissionEditorScripts;
using UnityEngine;
using BoscaliSummer.Features.Hud.Configuration;
using BoscaliSummer.Features.Hud.Presentation;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Runtime;
namespace BoscaliSummer.Features.Hud.Runtime
{
    [DefaultExecutionOrder(10000)]
    internal sealed class ThirdPersonHudController : MonoBehaviour, ISceneService, IThirdPersonHud
    {
        private static readonly FieldInfo CanvasField = AccessTools.Field(typeof(FlightHud), "canvas");
        public static ThirdPersonHudController Instance { get; private set; }
        private HudSettings settings;
        private GameObject hudCanvas, mapRoot;
        private bool hudWasActive, mapWasActive, ownsVisibility;
        private int readyFrame = -1;
        private float nextSample;
        private Aircraft owner;
        private ThirdPersonTargetBoard targetBoard;
        private FlightInstrumentView flightView;
        private readonly FlightTelemetry telemetry = new FlightTelemetry();
        private readonly MissileTelemetry missiles = new MissileTelemetry();
        private readonly NativeHudPresentation native = new NativeHudPresentation();
        public readonly NativeHudProjection Projection = new NativeHudProjection();
        private readonly ThirdPersonFlightCamera flightCamera = new ThirdPersonFlightCamera();
        public ThirdPersonFlightCamera FlightCamera => flightCamera;
        public bool IsEnabled => settings != null && settings.ThirdPersonHudEnabled.Value;
        public bool Active => isActiveAndEnabled && IsEnabled && !DynamicMap.mapMaximized && !GameplayUI.GameIsPaused && !PlayerSettings.cinematicMode && IsLocalExternal(SceneSingleton<CameraStateManager>.i, out _);
        public bool ModifyVanillaHud { get => settings != null && settings.ModifyVanillaHud.Value; set { if (settings != null) { settings.ModifyVanillaHud.Value = value; ApplyVisibility(); if (!value) flightView?.Hide(); } } }
        public bool NativeModificationsActive => Active && ModifyVanillaHud;
        // Correct our camera's projection even when instrument replacement is off.
        public bool CollectProjection => Active && (ModifyVanillaHud || FlightCameraEnabled);
        private bool CorrectProjection => Active && (ModifyVanillaHud || flightCamera.AppliedFrame == Time.frameCount);
        public HudBounds InstrumentBounds => targetBoard?.Bounds ?? default;
        public bool HidePitchLadder
        {
            get => settings != null && settings.ThirdPersonHidePitchLadder.Value;
            set { if (settings != null) settings.ThirdPersonHidePitchLadder.Value = value; }
        }

        public bool CameraFeedEnabled
        {
            get => settings != null && settings.ThirdPersonCameraEnabled.Value;
            set { if (settings != null) settings.ThirdPersonCameraEnabled.Value = value; }
        }

        public bool FlightCameraEnabled
        {
            get => settings != null && settings.ThirdPersonFlightCameraEnabled.Value;
            set { if (settings != null) settings.ThirdPersonFlightCameraEnabled.Value = value; }
        }

        public bool BoardEnabled { get => settings != null && settings.BoardEnabled.Value; set { if (settings != null) settings.BoardEnabled.Value = value; } }
        public bool AirframeEnabled { get => settings != null && settings.AirframeEnabled.Value; set { if (settings != null) settings.AirframeEnabled.Value = value; } }
        public bool ShotsEnabled { get => settings != null && settings.ThirdPersonShotsEnabled.Value; set { if (settings != null) settings.ThirdPersonShotsEnabled.Value = value; } }
        public bool MarkEnabled { get => settings != null && settings.MarkEnabled.Value; set { if (settings != null) settings.MarkEnabled.Value = value; } }
        public int FlightScaleStep { get => settings?.FlightScaleStep.Value ?? 1; set { if (settings != null) settings.FlightScaleStep.Value = Mathf.Clamp(value, 0, 3); } }
        public int FlightOpacityStep { get => settings?.FlightOpacityStep.Value ?? 0; set { if (settings != null) settings.FlightOpacityStep.Value = Mathf.Clamp(value, 0, 3); } }
        public int FlightContrast { get => settings?.FlightContrast.Value ?? 1; set { if (settings != null) settings.FlightContrast.Value = Mathf.Clamp(value, 0, 2); } }
        public int BoardCorner { get => settings?.BoardCorner.Value ?? 0; set { if (settings != null) settings.BoardCorner.Value = Mathf.Clamp(value, 0, 3); } }
        public int BoardScaleStep { get => settings?.BoardScaleStep.Value ?? 0; set { if (settings != null) settings.BoardScaleStep.Value = Mathf.Clamp(value, 0, 3); } }
        public int BoardOpacityStep { get => settings?.BoardOpacityStep.Value ?? 0; set { if (settings != null) settings.BoardOpacityStep.Value = Mathf.Clamp(value, 0, 3); } }
        public int BoardContrast { get => settings?.BoardContrast.Value ?? 0; set { if (settings != null) settings.BoardContrast.Value = Mathf.Clamp(value, 0, 2); } }
        public int BoardInsetX { get => settings?.BoardInsetX.Value ?? 0; set { if (settings != null) settings.BoardInsetX.Value = Mathf.Clamp(value, 0, 600); } }
        public int BoardInsetY { get => settings?.BoardInsetY.Value ?? 0; set { if (settings != null) settings.BoardInsetY.Value = Mathf.Clamp(value, 0, 600); } }
        public void ResetLayout()
        {
            BoardEnabled = AirframeEnabled = ShotsEnabled = MarkEnabled = CameraFeedEnabled = true;
            BoardCorner = BoardOpacityStep = BoardInsetX = BoardInsetY = 0;
            BoardScaleStep = BoardContrast = FlightScaleStep = FlightContrast = 1;
            FlightOpacityStep = 0;
        }


        private void Awake() => Instance = this;
        private void OnEnable() => Canvas.preWillRenderCanvases += BeforeCanvasRender;
        public void Configure(HudSettings config) => settings = config;
        public static bool IsLocalExternal(CameraStateManager cam, out Aircraft aircraft)
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            aircraft = hud != null ? hud.aircraft : null;
            return cam != null && aircraft != null && !aircraft.disabled && !aircraft.HasEjected() &&
                aircraft.Player != null && aircraft.Player.IsLocalPlayer && cam.followingUnit == aircraft &&
                aircraft.cockpit != null && !aircraft.cockpit.IsDetached() &&
                (cam.currentState == cam.orbitState || cam.currentState == cam.chaseState);
        }


        private void Update()
        {
            if (settings != null && !InputFieldChecker.InsideInputField && !GameplayUI.GameIsPaused && Input.GetKeyDown(settings.ThirdPersonHudKey.Value)) Toggle();
        }
        private void LateUpdate()
        {
            ApplyVisibility();
            if (!Active) { targetBoard?.Hide(); flightView?.Hide(); readyFrame = -1; return; }
            IsLocalExternal(SceneSingleton<CameraStateManager>.i, out Aircraft aircraft);
            if (owner != aircraft)
            { owner = aircraft; telemetry.Reset(); missiles.Read(null, false); nextSample = 0; Projection.Reset(); }
            if (!FlightCameraEnabled) flightCamera.Reset();
            if (!ModifyVanillaHud) flightView?.Hide();
            readyFrame = Time.frameCount;
            if (Time.unscaledTime < nextSample) return;
            { nextSample = Time.unscaledTime + .05f; telemetry.Read(aircraft); missiles.Read(aircraft, BoardEnabled && ShotsEnabled); }
            if (ModifyVanillaHud)
            {
                if (flightView == null) flightView = new FlightInstrumentView(transform);
                flightView.Present(telemetry.Flight, HidePitchLadder, settings.FlightScaleStep.Value, settings.FlightOpacityStep.Value, settings.FlightContrast.Value);
            }
            if (BoardEnabled && HudLayout.Opacity(BoardOpacityStep) > 0)
            {
                if (targetBoard == null) targetBoard = new ThirdPersonTargetBoard(transform);
                Texture picture = null; string mode = null;
                if (CameraFeedEnabled && aircraft.targetCam != null && NativeCamera.ReadMode(aircraft.targetCam) != TargetCam.CamMode.landingMode &&
                    NativeCamera.TryGet(aircraft, out Camera camera, out mode) && camera.targetTexture != null && camera.targetTexture.IsCreated()) picture = camera.targetTexture;
                targetBoard.Present(settings, telemetry.Systems, missiles, picture, mode, MarkEnabled ? MarkRow() : null);
            }
            else targetBoard?.Hide();
            readyFrame = Time.frameCount;
        }
        private static string MarkRow()
        {
            if (!ModServices.TryGet(out IObservationSource observations) ||
                !observations.TryGet(out ObservationPoint mark)) return null;
            float age = Time.unscaledTime - mark.RecordedAt;
            return "MARK " + UnitConverter.DistanceReading(mark.Range) + "  " +
                Mathf.Max(0f, age).ToString("0", System.Globalization.CultureInfo.InvariantCulture) + "s";
        }


        private void BeforeCanvasRender()
        {
            if (readyFrame != Time.frameCount || !CorrectProjection) return;
            CameraStateManager camera = SceneSingleton<CameraStateManager>.i;
            if (IsLocalExternal(camera, out Aircraft aircraft)) Projection.Render(aircraft, camera);
        }
        public void Toggle() => SetEnabled(!IsEnabled);
        public void SetEnabled(bool enabled) { if (settings == null) return; settings.ThirdPersonHudEnabled.Value = enabled; ApplyVisibility(); }
        public void ApplyVisibility()
        {
            if (!Active) { ReleaseVisibility(); return; }
            if (!ModifyVanillaHud) { ReleaseNativeVisibility(); return; }
            FlightHud hud = SceneSingleton<FlightHud>.i;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            Canvas canvas = hud != null ? CanvasField?.GetValue(hud) as Canvas : null;
            if (canvas == null || map == null) return;
            if (ownsVisibility && (hudCanvas != canvas.gameObject || mapRoot != map.gameObject)) ReleaseVisibility();
            if (!ownsVisibility)
            {
                hudCanvas = canvas.gameObject; mapRoot = map.gameObject;
                hudWasActive = hudCanvas.activeSelf; mapWasActive = mapRoot.activeSelf; ownsVisibility = true;
            }
            if (!hudCanvas.activeSelf) FlightHud.EnableCanvas(true);
            if (!mapRoot.activeSelf) DynamicMap.EnableCanvas(true);
            native.Apply(hud);
        }
        public void ReleaseVisibility()
        {
            ReleaseNativeVisibility(); Projection.Reset(); readyFrame = -1;
            targetBoard?.Hide(); flightView?.Hide();
        }
        private void ReleaseNativeVisibility()
        {
            native.Restore();
            if (ownsVisibility)
            {
                if (hudCanvas != null) hudCanvas.SetActive(hudWasActive);
                if (mapRoot != null && !DynamicMap.mapMaximized) mapRoot.SetActive(mapWasActive);
            }
            ownsVisibility = false; hudCanvas = mapRoot = null;
        }
        public void ResetForScene()
        {
            ReleaseVisibility(); native.Dispose(); flightCamera.Reset(); owner = null;
            telemetry.Reset(); missiles.Read(null, false); nextSample = 0;
            targetBoard?.Destroy(); targetBoard = null; flightView?.Destroy(); flightView = null;
        }
        private void OnDisable() { Canvas.preWillRenderCanvases -= BeforeCanvasRender; ResetForScene(); }
        private void OnDestroy() { ResetForScene(); if (Instance == this) Instance = null; }
    }
}
