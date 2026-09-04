using System;
using UnityEngine;
using BoscaliSummer.Features.Support.Configuration;
using BoscaliSummer.Framework.Lifecycle;

namespace BoscaliSummer.Features.Support.Runtime
{
    /// <summary>
    /// Robust, non-allocating Third-Person HUD controller. Keeps the tactical flight HUD
    /// active in external orbit and chase camera states while decluttering the floating
    /// pitch ladder and respecting spectator isolation.
    /// </summary>
    internal sealed class ThirdPersonHudController : MonoBehaviour, ISceneService
    {
        public static ThirdPersonHudController Instance { get; private set; }

        private SupportSettings settings;
        private GameObject cachedHudRoot;
        private GameObject cachedPitchCompass;
        private bool isSpectating;

        public bool IsEnabled => settings != null && settings.ThirdPersonHudEnabled.Value;

        private void Awake()
        {
            Instance = this;
        }

        public void Configure(SupportSettings supportSettings)
        {
            settings = supportSettings;
        }

        public void ResetForScene()
        {
            cachedHudRoot = null;
            cachedPitchCompass = null;
            isSpectating = false;
        }

        private void Update()
        {
            if (settings == null) return;
            if (Input.GetKeyDown(settings.ThirdPersonHudKey.Value))
            {
                Toggle();
            }
        }

        public void Toggle()
        {
            if (settings == null) return;
            settings.ThirdPersonHudEnabled.Value = !settings.ThirdPersonHudEnabled.Value;
            ApplyVisibility();
        }

        public void SetEnabled(bool enabled)
        {
            if (settings == null) return;
            settings.ThirdPersonHudEnabled.Value = enabled;
            ApplyVisibility();
        }

        public void SetSpectating(bool spectating)
        {
            isSpectating = spectating;
            ApplyVisibility();
        }

        public void ApplyVisibility()
        {
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            CombatHUD hud = SceneSingleton<CombatHUD>.i;
            if (cam == null || hud == null) return;

            if (cachedHudRoot == null)
            {
                cachedHudRoot = hud.gameObject;
                Transform t = hud.transform.Find("HUDCenter/pitchCompassCenter");
                if (t == null) t = FindChildRecursive(hud.transform, "pitchCompassCenter");
                if (t != null) cachedPitchCompass = t.gameObject;
            }

            CameraBaseState current = cam.currentState;
            bool isCockpit = current == cam.cockpitState;
            bool isOrbitOrChase = current == cam.orbitState || current == cam.chaseState;

            if (isCockpit)
            {
                if (!isSpectating)
                {
                    if (cachedHudRoot != null && !cachedHudRoot.activeSelf) cachedHudRoot.SetActive(true);
                    if (cachedPitchCompass != null && !cachedPitchCompass.activeSelf) cachedPitchCompass.SetActive(true);
                }
            }
            else if (isOrbitOrChase && IsEnabled && !isSpectating)
            {
                if (cachedHudRoot != null && !cachedHudRoot.activeSelf) cachedHudRoot.SetActive(true);
                if (cachedPitchCompass != null)
                {
                    bool hidePitch = settings.ThirdPersonHidePitchLadder.Value;
                    if (cachedPitchCompass.activeSelf == hidePitch)
                        cachedPitchCompass.SetActive(!hidePitch);
                }
            }
            else if (!isCockpit)
            {
                if (cachedHudRoot != null && cachedHudRoot.activeSelf)
                    cachedHudRoot.SetActive(false);
            }
        }

        private static Transform FindChildRecursive(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name == name) return child;
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }
    }
}
