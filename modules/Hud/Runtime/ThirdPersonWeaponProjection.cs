using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    // Read native cached world cues and update their screen geometry. UpdateWeaponDisplay,
    // DisplayLead and UpdatePipperPosition also advance native solvers/smoothing or change FOV.
    // None of those methods may be replayed during rendering.
    internal static class ThirdPersonWeaponProjection
    {
        private static readonly FieldInfo WeaponState = AccessTools.Field(typeof(CombatHUD), "weaponState");
        private static readonly FieldInfo GunDirection = AccessTools.Field(typeof(HUDBoresightState), "gunDirectionRelative");
        private static readonly FieldInfo Boresight = AccessTools.Field(typeof(HUDBoresightState), "boresight");
        private static readonly FieldInfo Lead = AccessTools.Field(typeof(HUDBoresightState), "projectedPosition");
        private static readonly FieldInfo LeadTarget = AccessTools.Field(typeof(HUDBoresightState), "targetPosition");
        private static readonly FieldInfo LeadLine = AccessTools.Field(typeof(HUDBoresightState), "line");
        private static readonly FieldInfo Controls = AccessTools.Field(typeof(HUDBoresightState), "controlsFilter");
        private static readonly FieldInfo AimAssist = AccessTools.Field(typeof(ControlsFilter), "aimAssist");
        private static readonly FieldInfo AimPoint = AccessTools.Field(AimAssist?.FieldType, "accurateAimpoint");
        private static readonly FieldInfo TurretCrosshairs = AccessTools.Field(typeof(HUDTurretState), "crosshairs");
        private static readonly FieldInfo BombImpact = AccessTools.Field(typeof(HUDBombingState), "ccipImpactPointSmoothed");
        private static readonly FieldInfo BombPipper = AccessTools.Field(typeof(HUDBombingState), "ccipPipper");
        private static readonly FieldInfo BombLine = AccessTools.Field(typeof(HUDBombingState), "ccipLine");
        private static readonly FieldInfo BombFallTime = AccessTools.Field(typeof(HUDBombingState), "ccipFallTime");
        private static readonly FieldInfo BombTarget = AccessTools.Field(typeof(HUDBombingState), "averageTargetPosition");
        private static readonly FieldInfo BombAlignment = AccessTools.Field(typeof(HUDBombingState), "alignmentBar");

        public static void Refresh(CombatHUD hud, Aircraft aircraft, Camera camera)
        {
            var state = WeaponState?.GetValue(hud) as HUDWeaponState;
            if (state is HUDBoresightState gun) RefreshGun(gun, hud, aircraft, camera);
            else if (state is HUDTurretState turret)
            {
                if (TurretCrosshairs?.GetValue(turret) is HUDTurretCrosshair[] crosshairs)
                    foreach (HUDTurretCrosshair crosshair in crosshairs)
                        if (crosshair != null) crosshair.Refresh(camera, out _);
            }
            else if (state is HUDBombingState bomb) RefreshBomb(bomb, aircraft, camera);
        }

        private static Vector3 ScreenPoint(Camera camera, Vector3 point)
        {
            Vector3 screen = camera.WorldToScreenPoint(point);
            screen.z = 0f;
            return screen;
        }

        private static void RefreshGun(HUDBoresightState state, CombatHUD hud, Aircraft aircraft, Camera camera)
        {
            var boresight = Boresight?.GetValue(state) as Image;
            if (boresight == null || !(GunDirection?.GetValue(state) is Vector3 relativeDirection)) return;
            Vector3 direction = aircraft.transform.TransformDirection(relativeDirection);
            boresight.transform.position = ScreenPoint(camera, aircraft.transform.position + direction * 3000f);
            boresight.enabled = !aircraft.gearDeployed && Vector3.Dot(camera.transform.forward, direction) > 0f;

            var lead = Lead?.GetValue(state) as Image;
            var target = LeadTarget?.GetValue(state) as Image;
            var line = LeadLine?.GetValue(state) as Image;
            var targets = hud.GetTargetList();
            if (lead == null || !lead.enabled || target == null || line == null ||
                targets == null || targets.Count == 0 || targets[0] == null) return;
            object controls = Controls?.GetValue(state);
            object aimAssist = controls != null ? AimAssist?.GetValue(controls) : null;
            if (aimAssist == null || !(AimPoint?.GetValue(aimAssist) is GlobalPosition aimPoint)) return;

            // Same native lead/lag geometry, reading its cached solution instead of GetAim()
            // (which advances aim smoothing and can run a new trajectory simulation).
            target.transform.position = ScreenPoint(camera, targets[0].transform.position);
            Vector3 aim = ScreenPoint(camera, aimPoint.ToLocalPosition());
            lead.transform.position = PlayerSettings.lagPip
                ? boresight.transform.position - aim + target.transform.position : aim;
            Vector3 from = PlayerSettings.lagPip ? Vector3.zero : target.transform.localPosition;
            Vector3 to = lead.transform.localPosition;
            Vector3 delta = from - to;
            line.transform.localPosition = (from + to) * .5f;
            line.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
            line.enabled = delta.magnitude > 14f;
            line.transform.localScale = new Vector3(Mathf.Max(0f, delta.magnitude - 14f), 1f, 1f);
        }

        private static void RefreshBomb(HUDBombingState state, Aircraft aircraft, Camera camera)
        {
            var alignment = BombAlignment?.GetValue(state) as Image;
            if (alignment != null && alignment.gameObject.activeSelf && BombTarget?.GetValue(state) is GlobalPosition target)
            {
                Vector3 delta = target - aircraft.GlobalPosition();
                Vector3 horizontal = delta;
                horizontal.y = 0f;
                float height = -delta.y + Vector3.Project(horizontal, aircraft.transform.forward).y;
                Vector3 position = target.ToLocalPosition();
                alignment.transform.position = ScreenPoint(camera, position + Vector3.up * height);
                Vector3 lower = ScreenPoint(camera, position + Vector3.up * height * .9f);
                Vector3 screenDelta = lower - alignment.transform.position;
                alignment.transform.eulerAngles = new Vector3(0f, 0f, -Mathf.Atan2(screenDelta.x, screenDelta.y) * Mathf.Rad2Deg + 180f);
            }

            var pipper = BombPipper?.GetValue(state) as Image;
            var line = BombLine?.GetValue(state) as Image;
            var fallTime = BombFallTime?.GetValue(state) as TextMeshProUGUI;
            if (pipper == null || line == null || fallTime == null || !(BombImpact?.GetValue(state) is Vector3 impact)) return;
            // Native trajectory/weapon validity survives a camera-only pipper hide. Its time
            // readout is cleared for invalid/disabled CCIP; visibility must use the final view.
            if (!fallTime.enabled) { pipper.enabled = line.enabled = false; return; }
            // The impact cache is global. Apply the CURRENT origin, never smooth it a second time.
            Vector3 positionWorld = impact + Datum.origin.position;
            if (Vector3.Dot((positionWorld - camera.transform.position).normalized, camera.transform.forward) < .5f)
            {
                pipper.enabled = line.enabled = false;
                return;
            }
            Vector3 point = ScreenPoint(camera, positionWorld);
            pipper.enabled = true;
            pipper.transform.position = point;
            Vector3 velocity = SceneSingleton<FlightHud>.i.velocityVector.transform.position;
            velocity.z = 0f;
            float scale = 1080f / Screen.height;
            Vector3 deltaScreen = velocity - point;
            line.transform.position = point + deltaScreen.normalized * 22f / scale;
            velocity -= deltaScreen.normalized * 8f / scale;
            line.enabled = Vector3.Dot(deltaScreen, velocity - line.transform.position) >= 0f;
            line.transform.eulerAngles = new Vector3(0f, 0f, -Mathf.Atan2(deltaScreen.x, deltaScreen.y) * Mathf.Rad2Deg);
            line.transform.localScale = new Vector3(1f, (line.transform.position - velocity).magnitude * scale, 1f);
        }
    }
}
