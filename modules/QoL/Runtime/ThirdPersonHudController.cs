using System;
using System.Reflection;
using HarmonyLib;
using NuclearOption.MissionEditorScripts;
using UnityEngine;
using BoscaliSummer.Features.QoL.Configuration;
using BoscaliSummer.Framework.Contracts;
using BoscaliSummer.Framework.Lifecycle;

namespace BoscaliSummer.Features.QoL.Runtime
{
    [DefaultExecutionOrder(10000)]
    internal sealed class ThirdPersonHudController : MonoBehaviour, ISceneService, IThirdPersonHud
    {
        private static readonly FieldInfo CanvasField = AccessTools.Field(typeof(FlightHud), "canvas");
        private static readonly FieldInfo PitchField = AccessTools.Field(typeof(FlightHud), "pitchCompassCenter");
        private static readonly Action<FlightHud> UpdateFlightHud = AccessTools.MethodDelegate<Action<FlightHud>>(
            AccessTools.Method(typeof(FlightHud), "Update"));
        private static readonly Action<HeadMountedDisplay> UpdateHelmet = AccessTools.MethodDelegate<Action<HeadMountedDisplay>>(
            AccessTools.Method(typeof(HeadMountedDisplay), "Update"));
        private static readonly Action<CombatHUD> UpdateCombatHud = AccessTools.MethodDelegate<Action<CombatHUD>>(
            AccessTools.Method(typeof(CombatHUD), "LateUpdate"));
        public static ThirdPersonHudController Instance { get; private set; }
        private QoLSettings settings;
        private GameObject hudCanvas, mapRoot, pitch;
        private bool hudWasActive, mapWasActive, pitchWasActive, ownsVisibility;
        private bool refreshingHud;
        private int refreshedHudFrame = -1;
        private readonly Presentation.ThirdPersonCameraPanel cameraPanel = new Presentation.ThirdPersonCameraPanel();
        private readonly ThirdPersonFlightCamera flightCamera = new ThirdPersonFlightCamera();
        public ThirdPersonFlightCamera FlightCamera => flightCamera;
        public bool IsEnabled => settings != null && settings.ThirdPersonHudEnabled.Value;

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

        // Unity's Update runs before the orbit/chase pose and floating-origin shift in LateUpdate.
        // Keep native input/weapon updates single-pass as well as fixing their projections.
        public bool DeferNativeHud => isActiveAndEnabled && !refreshingHud && IsEnabled &&
            !DynamicMap.mapMaximized && !GameplayUI.GameIsPaused &&
            IsLocalExternal(SceneSingleton<CameraStateManager>.i, out _);

        private void Awake() => Instance = this;
        public void Configure(QoLSettings config) => settings = config;

        public static bool IsLocalExternal(CameraStateManager cam, out Aircraft aircraft)
        {
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            aircraft = hud != null ? hud.aircraft : null;
            return cam != null && aircraft != null && !aircraft.disabled && !aircraft.HasEjected() &&
                aircraft.Player != null && aircraft.Player.IsLocalPlayer && cam.followingUnit == aircraft &&
                aircraft.cockpit != null && !aircraft.cockpit.IsDetached() &&
                (cam.currentState == cam.orbitState || cam.currentState == cam.chaseState);
        }

        public void ResetForScene()
        {
            ReleaseVisibility();
            flightCamera.Reset();
            cameraPanel.Destroy();
        }

        private void Update()
        {
            if (settings == null) return;
            if (!InputFieldChecker.InsideInputField && !GameplayUI.GameIsPaused &&
                Input.GetKeyDown(settings.ThirdPersonHudKey.Value)) Toggle();
            ApplyVisibility();
        }

        private void LateUpdate()
        {
            ApplyVisibility();
            RefreshHudAfterCamera();
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            bool local = IsLocalExternal(cam, out Aircraft aircraft);
            var targets = local && aircraft.weaponManager != null ? aircraft.weaponManager.GetTargetList() : null;
            bool selected = targets != null && targets.Count > 0;
            bool visible = ThirdPersonCameraPolicy.ShouldShow(IsEnabled && settings.ThirdPersonCameraEnabled.Value,
                local, local, hudCanvas != null && hudCanvas.activeInHierarchy,
                DynamicMap.mapMaximized, GameplayUI.GameIsPaused, selected);
            cameraPanel.Refresh(visible ? aircraft : null, transform);
            if (!local || !FlightCameraEnabled) flightCamera.Reset();
        }

        private void RefreshHudAfterCamera()
        {
            if (!DeferNativeHud || refreshedHudFrame == Time.frameCount) return;
            refreshedHudFrame = Time.frameCount;
            refreshingHud = true;
            try
            {
                FlightHud hud = SceneSingleton<FlightHud>.i;
                HeadMountedDisplay helmet = SceneSingleton<HeadMountedDisplay>.i;
                CombatHUD combat = SceneSingleton<CombatHUD>.i;
                if (hud != null && hud.isActiveAndEnabled) UpdateFlightHud(hud);
                if (helmet != null && helmet.isActiveAndEnabled) UpdateHelmet(helmet);
                if (combat != null && combat.isActiveAndEnabled) UpdateCombatHud(combat);
            }
            finally { refreshingHud = false; }
        }

        public void Toggle() => SetEnabled(!IsEnabled);
        public void SetEnabled(bool enabled)
        {
            if (settings == null) return;
            settings.ThirdPersonHudEnabled.Value = enabled;
            ApplyVisibility();
        }

        public void ApplyVisibility()
        {
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (!IsEnabled || !IsLocalExternal(cam, out _) || DynamicMap.mapMaximized || GameplayUI.GameIsPaused)
            {
                ReleaseVisibility();
                return;
            }
            FlightHud hud = SceneSingleton<FlightHud>.i;
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            Canvas canvas = hud != null ? CanvasField?.GetValue(hud) as Canvas : null;
            if (canvas == null || map == null) return;
            if (ownsVisibility && (hudCanvas != canvas.gameObject || mapRoot != map.gameObject)) ReleaseVisibility();
            if (!ownsVisibility)
            {
                hudCanvas = canvas.gameObject;
                mapRoot = map.gameObject;
                pitch = PitchField?.GetValue(hud) as GameObject;
                hudWasActive = hudCanvas.activeSelf;
                mapWasActive = mapRoot.activeSelf;
                pitchWasActive = pitch != null && pitch.activeSelf;
                ownsVisibility = true;
            }
            if (!hudCanvas.activeSelf) FlightHud.EnableCanvas(true);
            if (!mapRoot.activeSelf) DynamicMap.EnableCanvas(true);
            if (pitch != null) pitch.SetActive(!settings.ThirdPersonHidePitchLadder.Value && pitchWasActive);
        }

        // Run before native transitions, so their new visibility takes precedence over our snapshot.
        public void ReleaseVisibility()
        {
            if (ownsVisibility)
            {
                if (pitch != null) pitch.SetActive(pitchWasActive);
                if (hudCanvas != null) hudCanvas.SetActive(hudWasActive);
                if (mapRoot != null && !DynamicMap.mapMaximized) mapRoot.SetActive(mapWasActive);
            }
            ownsVisibility = false;
            hudCanvas = mapRoot = pitch = null;
        }

        private void OnDisable() => ResetForScene();
        private void OnDestroy()
        {
            ResetForScene();
            if (Instance == this) Instance = null;
        }
    }
}
