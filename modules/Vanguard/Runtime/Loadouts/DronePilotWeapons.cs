using System.Collections.Generic;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    internal sealed partial class VanguardFlight
    {
        private float nextPilotLock;

        private void TickPilotWeapons()
        {
            DroneWeapon trigger = DronePilot.Trigger(missile);
            if (role == VanguardRole.Sentinel) { sentinel?.TickPiloted(trigger == DroneWeapon.Gun); return; }
            if (role == VanguardRole.Gunship) { gunDrone?.TickPiloted(trigger == DroneWeapon.Gun); return; }
            if (role != VanguardRole.Drone) return;

            float now = Time.timeSinceLevelLoad;
            if (trigger == DroneWeapon.Gun && GunUsable &&
                RemoraArcher.CanGun(true, 0f, 0f, gunAmmo, now - lastGunShot, gunShots))
                FireRemoraGun(now, null);
            if (trigger != DroneWeapon.Guided || now < nextPilotLock) return;
            nextPilotLock = now + 0.2f;
            Unit locked = FindPilotLock(missile, droneGrid, out GlobalPosition known);
            if (locked != null) TryDroneShot(locked, known - missile.GlobalPosition(), manual: true);
        }

        internal static Unit FindPilotLock(Missile drone, List<Unit> candidates, out GlobalPosition aim)
        {
            aim = default;
            FactionHQ faction = drone.NetworkHQ;
            if (faction == null) return null;
            bool ground = RemoraBoard.IsManta(drone);
            float range = ground ? MantaRules.FireRange : RemoraArcher.FireRange;
            float nearestAngle = ground ? MantaRules.FireCone : RemoraArcher.FireCone;
            GlobalPosition position = drone.GlobalPosition();
            Vector3 forward = drone.transform.forward;
            Unit selected = null;
            BattlefieldGrid.GetUnitsInRangeNonAlloc(position, range, candidates);
            for (int i = 0; i < candidates.Count; i++)
            {
                Unit candidate = candidates[i];
                if (!Hostile(candidate, faction) || !(ground ? IsGroundTarget(candidate) : candidate is Aircraft || candidate is Missile) ||
                    !faction.TryGetKnownPosition(candidate, out GlobalPosition known)) continue;
                Vector3 delta = known - position;
                float angle = Vector3.Angle(forward, delta);
                if (delta.sqrMagnitude > range * range || angle > nearestAngle) continue;
                nearestAngle = angle;
                selected = candidate;
                aim = known;
            }
            return selected;
        }
    }
}
