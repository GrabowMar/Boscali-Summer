using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// Missile follow-camera: points the orbit camera at one of our missiles, cycling with the
    /// C-menu. Saves the previous following unit + state on entry and restores both on exit;
    /// if the followed missile dies or leaves the tracked list, steps to the next live one or
    /// drops back to the aircraft. Client-local, no networking.
    /// </summary>
    internal sealed class MissileView
    {
        private Missile current;
        private Unit returnTo;
        private CameraBaseState returnState;
        private int index = -1;

        public bool Active => current != null;

        public Missile Current => current;

        public int Index => index;

        public bool Enter(IReadOnlyList<Missile> missiles)
        {
            if (missiles == null || missiles.Count == 0) return false;
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null) return false;
            Missile first = FirstLive(missiles);
            if (first == null) return false;
            if (current == null)
            {
                returnTo = cam.followingUnit;
                returnState = cam.currentState;
            }
            return Follow(cam, missiles, IndexOf(missiles, first));
        }

        public bool Next(IReadOnlyList<Missile> missiles) => Step(missiles, 1, index >= 0 ? index : 0);

        public bool Prev(IReadOnlyList<Missile> missiles) => Step(missiles, -1, index >= 0 ? index : 0);

        public void Exit()
        {
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            Missile was = current;
            current = null;
            index = -1;
            if (cam == null) { returnTo = null; returnState = null; return; }
            // Only restore when we still own the camera; the pilot may have switched away.
            if (was != null && cam.followingUnit == was)
            {
                if (returnTo != null && !returnTo.disabled) cam.SetFollowingUnit(returnTo);
                if (returnState != null) cam.SwitchState(returnState);
            }
            returnTo = null;
            returnState = null;
        }

        /// <summary>Call each board tick: drops a dead follow, and clears if the pilot took the camera back.</summary>
        public void Tick(IReadOnlyList<Missile> missiles)
        {
            if (current == null) return;
            CameraStateManager cam = SceneSingleton<CameraStateManager>.i;
            if (cam == null) { current = null; index = -1; return; }
            if (cam.followingUnit != current)
            {
                // Pilot switched camera manually (or a cutscene did): release quietly.
                current = null;
                index = -1;
                returnTo = null;
                returnState = null;
                return;
            }
            int at = IndexOf(missiles, current);
            if (current.disabled || at < 0)
            {
                // The followed one is gone: the missile that slid into its slot is next, not the one after it.
                if (!Step(missiles, 1, index - 1)) Exit();
                return;
            }
            index = at;   // the list can reorder or shrink under us; the index must never go stale
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
                returnTo = cam.followingUnit;
                returnState = cam.currentState;
            }
            current = m;
            index = at;
            cam.SetFollowingUnit(m);
            if (cam.orbitState != null) cam.SwitchState(cam.orbitState);
            return true;
        }

        private static Missile FirstLive(IReadOnlyList<Missile> missiles)
        {
            for (int i = 0; i < missiles.Count; i++)
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
    }
}
