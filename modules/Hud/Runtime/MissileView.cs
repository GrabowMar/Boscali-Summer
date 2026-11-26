using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Missile POV camera: mounts the main camera on the nose of one of our missiles looking
    /// forward with all HUD/map overlays hidden. Saves the previous following unit + state on
    /// entry and restores both on exit; when the followed missile hits or is destroyed, returns
    /// to the player's aircraft automatically. Client-local, no networking.
    /// </summary>
    internal sealed class MissileView
    {
        private const float PovFov = 55f;
        private const float PovNearClip = 0.5f;

        private readonly MissilePovState povState;
        private Missile current;
        private Unit returnTo;
        private CameraBaseState returnState;
        private int index = -1;
        private bool exiting;

        public MissileView() => povState = new MissilePovState(this);

        public bool Active => current != null;

        public Missile Current => current;

        public int Index => index;

        public bool Enter(IReadOnlyList<Missile> missiles)
        {
            if (missiles == null || missiles.Count == 0) return false;
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null) return false;
            Missile latest = LastLive(missiles);
            if (latest == null) return false;
            if (current == null)
            {
                returnTo = cam.followingUnit != null ? cam.followingUnit : LocalAircraft();
                returnState = cam.currentState != povState ? cam.currentState : cam.cockpitState;
            }
            return Follow(cam, missiles, IndexOf(missiles, latest));
        }

        public bool Next(IReadOnlyList<Missile> missiles) => Step(missiles, 1, index >= 0 ? index : 0);

        public bool Prev(IReadOnlyList<Missile> missiles) => Step(missiles, -1, index >= 0 ? index : 0);

        public void Exit()
        {
            if (exiting) return;
            exiting = true;
            try
            {
                CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
                Missile was = current;
                Unit backUnit = returnTo;
                CameraBaseState backState = returnState;
                if (was != null) was.onDisableUnit -= OnFollowedDisabled;
                current = null;
                index = -1;
                returnTo = null;
                returnState = null;
                if (cam == null) return;
                // Restore when we still own the camera OR vanilla's Cam_OnFollowingUnitDisabled already dropped it to null/freeState.
                bool owned = was == null || cam.followingUnit == was || cam.followingUnit == null || cam.currentState == povState;
                if (!owned) return;
                Unit target = backUnit != null && !backUnit.disabled ? backUnit : LocalAircraft();
                if (target != null && !target.disabled)
                {
                    cam.SetFollowingUnit(target);
                    CameraBaseState state = backState != null && backState != povState ? backState : cam.cockpitState;
                    if (state != null && cam.currentState != state) cam.SwitchState(state);
                }
            }
            finally
            {
                exiting = false;
            }
        }

        /// <summary>Call each board tick: returns to plane if the missile ended, or releases if the pilot switched camera away.</summary>
        public void Tick(IReadOnlyList<Missile> missiles)
        {
            if (current == null) return;
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null)
            {
                current.onDisableUnit -= OnFollowedDisabled;
                current = null;
                index = -1;
                returnTo = null;
                returnState = null;
                return;
            }
            int at = IndexOf(missiles, current);
            if (current.disabled || at < 0)
            {
                Exit();
                return;
            }
            if (cam.followingUnit != current || cam.currentState != povState)
            {
                // Pilot switched camera manually (or a cutscene did): release quietly.
                current.onDisableUnit -= OnFollowedDisabled;
                current = null;
                index = -1;
                returnTo = null;
                returnState = null;
                return;
            }
            index = at;   // the list can reorder or shrink under us; the index must never go stale
        }

        private void OnFollowedDisabled(Unit unit)
        {
            if (ReferenceEquals(unit, current)) Exit();
        }

        private bool Step(IReadOnlyList<Missile> missiles, int delta, int start)
        {
            if (missiles == null || missiles.Count == 0) return false;
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null) return false;
            int count = missiles.Count;
            for (int step = 1; step <= count; step++)
            {
                int next = (start + delta * step) % count;
                if (next < 0) next += count;
                Missile candidate = missiles[next];
                if (candidate == null || candidate.disabled) continue;
                return Follow(cam, missiles, next);
            }
            return false;
        }

        private bool Follow(CameraStateManager cam, IReadOnlyList<Missile> missiles, int at)
        {
            if (at < 0 || at >= missiles.Count) return false;
            Missile m = missiles[at];
            if (m == null || m.disabled) return false;
            if (current == null)
            {
                returnTo = cam.followingUnit != null ? cam.followingUnit : LocalAircraft();
                returnState = cam.currentState != povState ? cam.currentState : cam.cockpitState;
            }
            else
            {
                current.onDisableUnit -= OnFollowedDisabled;
            }
            current = m;
            index = at;
            cam.SetFollowingUnit(m);
            // Subscribe after SetFollowingUnit so vanilla's Cam_OnFollowingUnitDisabled runs first and Exit() immediately re-attaches to the plane.
            m.onDisableUnit += OnFollowedDisabled;
            cam.SwitchState(povState);
            return true;
        }

        private static Aircraft LocalAircraft() =>
            GameManager.GetLocalAircraft(out Aircraft local) && local != null && !local.disabled ? local : null;

        private static Missile LastLive(IReadOnlyList<Missile> missiles)
        {
            for (int i = missiles.Count - 1; i >= 0; i--)
                if (missiles[i] != null && !missiles[i].disabled) return missiles[i];
            return null;
        }

        private static int IndexOf(IReadOnlyList<Missile> missiles, Missile m)
        {
            if (missiles == null) return -1;
            for (int i = 0; i < missiles.Count; i++)
                if (ReferenceEquals(missiles[i], m)) return i;
            return -1;
        }

        private sealed class MissilePovState : CameraBaseState
        {
            private readonly MissileView owner;
            private bool hidMap;

            public MissilePovState(MissileView owner) => this.owner = owner;

            public override void EnterState(CameraStateManager cam)
            {
                cam.followingUnit?.SetDoppler(false);
                cam.cameraPivot.SetParent(Datum.origin);
                cam.transform.SetParent(cam.cameraPivot);
                cam.transform.localPosition = Vector3.zero;
                cam.transform.localRotation = Quaternion.identity;
                cam.mainCamera.nearClipPlane = PovNearClip;
                cam.mainCamera.fieldOfView = PovFov;
                CameraStateManager.cameraMode = CameraMode.orbit;
                FlightHud.EnableCanvas(false);
                DynamicMap map = SceneSingleton<DynamicMap>.i;
                if (map != null && map.gameObject.activeSelf && !DynamicMap.mapMaximized)
                {
                    hidMap = true;
                    DynamicMap.EnableCanvas(false);
                }
                SnapPose(cam);
            }

            public override void LeaveState(CameraStateManager cam)
            {
                cam.mainCamera.fieldOfView = cam.desiredFOV;
                if (hidMap)
                {
                    hidMap = false;
                    DynamicMap map = SceneSingleton<DynamicMap>.i;
                    if (map != null && !DynamicMap.mapMaximized) DynamicMap.EnableCanvas(true);
                }
            }

            public override void UpdateState(CameraStateManager cam)
            {
                Missile m = owner.current;
                if (m == null || m.disabled)
                {
                    owner.Exit();
                    return;
                }
                SnapPose(cam);
            }

            public override void FixedUpdateState(CameraStateManager cam) { }

            private void SnapPose(CameraStateManager cam)
            {
                Missile m = owner.current;
                if (m == null || m.disabled) return;
                Transform t = m.transform;
                cam.cameraPivot.SetPositionAndRotation(t.position + t.forward * (m.maxRadius + 0.5f), t.rotation);
                cam.transform.localPosition = Vector3.zero;
                cam.transform.localRotation = Quaternion.identity;
                cam.cameraVelocity = m.rb != null ? m.rb.velocity : Vector3.zero;
            }
        }
    }
}
