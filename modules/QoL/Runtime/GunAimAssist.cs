using BoscaliSummer.Features.QoL.Configuration;
using BoscaliSummer.Framework.Lifecycle;
using NuclearOption.MissionEditorScripts;
using NuclearOption.UI;
using UnityEngine;

namespace BoscaliSummer.Features.QoL.Runtime
{
    internal sealed class GunAimAssist : MonoBehaviour, ISceneService
    {
        public static GunAimAssist Instance { get; private set; }
        private QoLSettings settings;
        private Aircraft owner;
        private Unit target;
        private FactionHQ faction;
        private WeaponStation station;
        private GlobalPosition aim;
        private float sampledAt;

        private void Awake() => Instance = this;
        public void Configure(QoLSettings config) => settings = config;

        private bool CanAssist(Aircraft aircraft)
        {
            CameraStateManager view = SceneSingleton<CameraStateManager>.i;
            return settings != null && settings.Enabled.Value && settings.GunAimAssist.Value &&
                !Application.isBatchMode && !GameplayUI.GameIsPaused && !DynamicMap.mapMaximized &&
                !InputFieldChecker.InsideInputField && !RadialMenuMain.IsInUse() && !LeaderboardMenu.IsOpen() &&
                GameManager.flightControlsEnabled && !Cursor.visible &&
                aircraft != null && GameManager.IsLocalAircraft(aircraft) && aircraft.Player != null &&
                aircraft.Player.IsLocalPlayer && !aircraft.disabled && !aircraft.HasEjected() &&
                aircraft.cockpit != null && !aircraft.cockpit.IsDetached() && aircraft.flightAssist &&
                aircraft.GetControlsFilter() != null && !aircraft.IsAutoHoverEnabled() &&
                aircraft.radarAlt > 5f && aircraft.speed > 25f && view != null && view.followingUnit == aircraft &&
                (CameraStateManager.cameraMode == CameraMode.cockpit ||
                 CameraStateManager.cameraMode == CameraMode.orbit || CameraStateManager.cameraMode == CameraMode.chase);
        }

        private static bool SelectedEnemy(Aircraft aircraft, Unit candidate, WeaponStation weapon)
        {
            if (candidate == null || candidate.disabled || candidate.NetworkHQ == null || aircraft.NetworkHQ == null ||
                DynamicMap.GetFactionMode(candidate.NetworkHQ) != FactionMode.Enemy ||
                aircraft.weaponManager == null || aircraft.weaponManager.currentWeaponStation != weapon ||
                weapon == null || weapon.HasTurret() || weapon.Weapons.Count == 0 ||
                !(weapon.Weapons[0] is Gun gun) || gun.GetAmmoTotal() <= 0) return false;
            var selected = aircraft.weaponManager.GetTargetList();
            var tracking = aircraft.NetworkHQ.GetTrackingData(candidate.persistentID);
            return selected.Count > 0 && selected[0] == candidate && tracking != null &&
                Time.timeSinceLevelLoad - tracking.lastSpottedTime >= 0f &&
                Time.timeSinceLevelLoad - tracking.lastSpottedTime <= 0.5f;
        }

        // Borrow the HUD's existing solution. Never call GetAim or simulate another trajectory.
        public void Capture(Aircraft aircraft, Unit candidate, GlobalPosition? aimPoint)
        {
            if (!CanAssist(aircraft)) { ResetForScene(); return; }
            WeaponStation weapon = aircraft.weaponManager?.currentWeaponStation;
            if (!aimPoint.HasValue || !SelectedEnemy(aircraft, candidate, weapon)) { ResetForScene(); return; }
            owner = aircraft;
            faction = aircraft.NetworkHQ;
            target = candidate;
            station = weapon;
            aim = aimPoint.Value;
            sampledAt = Time.timeSinceLevelLoad;
        }

        public void Apply(Aircraft aircraft, float pilotStrength)
        {
            if (!CanAssist(aircraft) || aircraft != owner || aircraft.NetworkHQ != faction || !(pilotStrength >= 0.2f) ||
                Time.timeSinceLevelLoad - sampledAt > 0.15f || Time.timeSinceLevelLoad < sampledAt ||
                !SelectedEnemy(aircraft, target, station)) { ResetForScene(); return; }
            Transform gun = station.Weapons[0].transform;
            Vector3 direction = aim - gun.GlobalPosition();
            if (direction.sqrMagnitude < 1f || Vector3.Dot(gun.forward, direction) <= 0f) return;
            float angle = Vector3.Angle(gun.forward, direction);
            if (angle >= GunAimAssistPolicy.ConeDegrees) return;
            Transform body = aircraft.cockpit.transform;
            Vector3 rate = body.InverseTransformDirection(aircraft.CockpitRB().angularVelocity);
            float pitch = TargetCalc.GetAngleOnAxis(gun.forward, direction, body.right);
            float yaw = TargetCalc.GetAngleOnAxis(gun.forward, direction, body.up);
            ControlInputs inputs = aircraft.GetInputs();
            // Native player input is freshly written before this hook; no correction accumulates.
            // Yield both axes during a deliberate bank or pull-away manoeuvre.
            if (Mathf.Abs(inputs.roll) >= 0.35f || Mathf.Abs(inputs.pitch) >= 0.35f ||
                Mathf.Abs(inputs.yaw) >= 0.35f || inputs.pitch * pitch < -0.005f || inputs.yaw * yaw < -0.005f) return;
            float strength = settings.GunAimAssistStrength.Value;
            inputs.pitch = Mathf.Clamp(inputs.pitch + GunAimAssistPolicy.Correction(pitch, angle, inputs.pitch, rate.x, strength), -1f, 1f);
            inputs.yaw = Mathf.Clamp(inputs.yaw + GunAimAssistPolicy.Correction(yaw, angle, inputs.yaw, rate.y, strength), -1f, 1f);
        }

        private void Update()
        {
            if (owner != null && (!CanAssist(owner) || owner.NetworkHQ != faction ||
                Time.timeSinceLevelLoad - sampledAt > 0.15f)) ResetForScene();
        }

        public void ResetForScene() { owner = null; target = null; faction = null; station = null; aim = default; sampledAt = 0f; }
        private void OnDisable() => ResetForScene();
        private void OnDestroy() { ResetForScene(); if (Instance == this) Instance = null; }
    }
}
