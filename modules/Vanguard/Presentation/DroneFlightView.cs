using BoscaliSummer.Core.Diagnostics;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Modules.Vanguard.Runtime;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BoscaliSummer.Modules.Vanguard.Presentation
{
    // The game's camera completes its own frame first. The drone lens uses the resulting local world.
    [DefaultExecutionOrder(50)]
    internal sealed class DroneFlightView : MonoBehaviour, ISceneService
    {
        private const float ChaseTilt = 12f;
        private Camera lens, stockCamera, stockCockpitCamera;
        private AudioListener listener, stockListener;
        private Canvas stockHud;
        private bool stockCameraEnabled, stockCockpitEnabled, stockListenerEnabled, stockHudEnabled;
        private float pan, tilt = ChaseTilt, distance = 1f, heading;

        internal static DroneFlightView Active { get; private set; }
        internal bool Presenting => lens != null && lens.isActiveAndEnabled;
        internal Camera Camera => Presenting ? lens : null;

        private void Awake() => Active = this;

        internal void ResetOrbit()
        {
            pan = 0f;
            tilt = ChaseTilt;
            distance = 1f;
        }

        internal void Orbit(Vector2 motion, float wheelSteps)
        {
            pan = Mathf.Repeat(pan + motion.x, 360f);
            tilt = Mathf.Clamp(tilt + motion.y, -75f, 75f);
            distance = Mathf.Clamp(distance - wheelSteps * 0.12f, 0.6f, 3f);
        }

        private void LateUpdate()
        {
            try { UpdateView(); }
            catch (System.Exception error)
            {
                End();
                PatchGuard.Report("DroneFlightView.LateUpdate", error);
                DronePilot pilot = DronePilot.Active;
                if (pilot != null) pilot.Release();
            }
        }

        private void UpdateView()
        {
            DronePilot pilot = DronePilot.Active;
            Missile drone = pilot != null ? pilot.Current : null;
            CameraStateManager scene = SceneSingleton<CameraStateManager>.i;
            Camera source = scene != null ? scene.mainCamera : null;
            if (Application.isBatchMode || drone == null || drone.disabled || source == null ||
                DynamicMap.mapMaximized || GameplayUI.GameIsPaused)
            {
                End();
                return;
            }

            if (stockCamera != source || lens == null)
            {
                End();
                Begin(scene, source);
            }

            Position(pilot, drone);
            // Other views can ask for the cockpit HUD while we render. Give it back once this lens closes.
            if (stockHud != null && stockHud.enabled)
            {
                stockHudEnabled = true;
                stockHud.enabled = false;
            }
        }

        private void Begin(CameraStateManager scene, Camera source)
        {
            if (lens == null)
            {
                var eye = new GameObject("BoscaliDroneFlightCamera");
                eye.SetActive(false);
                eye.tag = "MainCamera";
                eye.transform.SetParent(transform, false);
                lens = eye.AddComponent<Camera>();
                listener = eye.AddComponent<AudioListener>();
                lens.enabled = false;
                listener.enabled = false;
                eye.SetActive(true);
            }
            lens.CopyFrom(source);
            lens.enabled = false;
            lens.targetTexture = null;
            lens.nearClipPlane = 0.3f;
            lens.cullingMask &= ~(PhysicsLayers.HUDMask.value | PhysicsLayers.CockpitMask.value);

            UniversalAdditionalCameraData data = lens.GetUniversalAdditionalCameraData();
            if (source.TryGetComponent(out UniversalAdditionalCameraData sourceData))
            {
                data.renderPostProcessing = sourceData.renderPostProcessing;
                data.volumeLayerMask = sourceData.volumeLayerMask;
                data.renderShadows = sourceData.renderShadows;
                data.requiresDepthOption = sourceData.requiresDepthOption;
                data.requiresColorOption = sourceData.requiresColorOption;
                data.antialiasing = sourceData.antialiasing;
                data.antialiasingQuality = sourceData.antialiasingQuality;
            }
            LevelInfo level = NetworkSceneSingleton<LevelInfo>.i;
            if (level != null && level.PostProcessing != null)
            {
                data.renderPostProcessing = true;
                data.volumeLayerMask = data.volumeLayerMask.value | (1 << level.PostProcessing.gameObject.layer);
            }

            stockCamera = source;
            stockCameraEnabled = source.enabled;
            source.enabled = false;
            Camera cockpit = scene.cockpitCamRender;
            if (cockpit != null && cockpit != source &&
                (!cockpit.TryGetComponent(out UniversalAdditionalCameraData cockpitData) || cockpitData.renderType == CameraRenderType.Base))
            {
                stockCockpitCamera = cockpit;
                stockCockpitEnabled = cockpit.enabled;
                cockpit.enabled = false;
            }
            stockListener = source.GetComponent<AudioListener>();
            if (stockListener != null)
            {
                stockListenerEnabled = stockListener.enabled;
                stockListener.enabled = false;
            }
            FlightHud hud = SceneSingleton<FlightHud>.i;
            Transform center = hud != null ? hud.GetHUDCenter() : null;
            stockHud = center != null ? center.GetComponentInParent<Canvas>(true) : null;
            stockHudEnabled = stockHud != null && stockHud.enabled;
            listener.enabled = stockListenerEnabled;
            lens.enabled = true;
        }

        private void Position(DronePilot pilot, Missile drone)
        {
            Transform model = drone.transform;
            float radius = Mathf.Max(0.5f, drone.maxRadius);
            Vector3 forward = model.forward;
            if (forward.x * forward.x + forward.z * forward.z > 0.001f)
                heading = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            if (!pilot.ThirdPerson)
            {
                Transform mount = pilot.WeaponMount;
                bool gun = pilot.GunView && mount != null;
                Transform sight = gun ? mount : model;
                lens.fieldOfView = 62f;
                lens.transform.SetPositionAndRotation(sight.position + sight.forward * (gun ? 0.5f : radius + 0.5f), sight.rotation);
                return;
            }

            Quaternion orbit = Quaternion.Euler(tilt, heading + pan, 0f);
            Vector3 pivot = model.position + Vector3.up * Mathf.Max(0.5f, radius * 0.45f);
            Vector3 eye = pivot - orbit * Vector3.forward * Mathf.Max(6f, radius * 3.5f) * distance;
            if (Physics.Linecast(pivot, eye, out RaycastHit hit, PhysicsLayers.StaticsMask, QueryTriggerInteraction.Ignore))
                eye = hit.point + hit.normal * 0.4f;
            lens.fieldOfView = 68f;
            lens.transform.SetPositionAndRotation(eye, orbit);
        }

        internal void End()
        {
            if (lens != null) lens.enabled = false;
            if (listener != null) listener.enabled = false;
            if (stockCamera != null) stockCamera.enabled = stockCameraEnabled;
            if (stockCockpitCamera != null) stockCockpitCamera.enabled = stockCockpitEnabled;
            if (stockListener != null) stockListener.enabled = stockListenerEnabled;
            if (stockHud != null) stockHud.enabled = stockHudEnabled;
            stockCamera = stockCockpitCamera = null;
            stockListener = null;
            stockHud = null;
            stockCameraEnabled = stockCockpitEnabled = stockListenerEnabled = stockHudEnabled = false;
        }

        internal void HandBack(Aircraft aircraft)
        {
            End();
            CameraStateManager cameras = SceneSingleton<CameraStateManager>.i;
            if (aircraft == null || aircraft.disabled || aircraft.HasEjected() || cameras == null ||
                cameras.followingUnit == aircraft || cameras.currentState == null || NetworkSceneSingleton<LevelInfo>.i == null ||
                !GameManager.GetLocalAircraft(out Aircraft local) || local != aircraft) return;

            // A unit selected on the map can move the stock camera while the drone lens is parked.
            bool wasOpen = DynamicMap.mapMaximized;
            try
            {
                cameras.SetFollowingUnit(aircraft);
                cameras.SwitchState(cameras.cockpitState);
            }
            catch (System.Exception error) { PatchGuard.Report("DroneFlightView.HandBack", error); }
            finally
            {
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (wasOpen && !DynamicMap.mapMaximized && map != null) map.Maximize();
            }
        }

        public void ResetForScene()
        {
            End();
            if (lens != null) Destroy(lens.gameObject);
            lens = null;
            listener = null;
            ResetOrbit();
            heading = 0f;
        }

        private void OnDestroy()
        {
            ResetForScene();
            if (Active == this) Active = null;
        }

        private void OnDisable() => End();
    }
}
