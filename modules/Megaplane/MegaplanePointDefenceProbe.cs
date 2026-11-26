#if !PUBLIC_RELEASE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BoscaliSummer.Modules.Megaplane
{
    public static class MegaplanePointDefenceProbe
    {
        private sealed class Salvo
        {
            internal Missile Inbound, Crossing, Outbound, Friendly;
        }

        private static readonly ConditionalWeakTable<Aircraft, Salvo> Salvos = new();

        public static object Observe(Dictionary<string, object> args)
        {
            if (!args.TryGetValue("idUnit", out object unit) || unit is not Aircraft aircraft || aircraft == null ||
                !aircraft.IsServer || aircraft.NetworkHQ == null ||
                !aircraft.TryGetComponent(out MegaplaneDefence defence))
                throw new InvalidOperationException("The point-defence proof requires a live Groza on the host.");

            Salvo salvo = Salvos.GetOrCreateValue(aircraft);
            FactionHQ hq = aircraft.NetworkHQ;
            if (args.TryGetValue("launch", out object launch) && Convert.ToBoolean(launch))
            {
                Unit enemy = UnitRegistry.allUnits.FirstOrDefault(candidate => candidate != null && !candidate.disabled &&
                    candidate is not Missile && candidate.NetworkHQ != null && candidate.NetworkHQ != hq);
                if (enemy == null ||
                    !Encyclopedia.Lookup.TryGetValue("AAM2", out UnitDefinition definition) || definition is not MissileDefinition missile)
                    throw new InvalidOperationException("The point-defence proof requires AAM2 and an opposing unit.");

                Transform frame = aircraft.transform;
                Vector3 at = frame.position;
                Vector3 velocity = aircraft.rb.velocity;
                var spawner = NetworkSceneSingleton<Spawner>.i;
                Missile Spawn(Vector3 offset, bool closes, Unit target, Unit owner)
                {
                    Vector3 relativeVelocity = offset.normalized * (closes ? -340f : 340f);
                    Vector3 launchVelocity = velocity + relativeVelocity;
                    return spawner.SpawnMissile(missile, at + offset, Quaternion.LookRotation(launchVelocity),
                        launchVelocity, target, owner);
                }

                salvo.Inbound = Spawn((frame.right + frame.forward).normalized * 6200f + Vector3.up * 300f, true, aircraft, enemy);
                salvo.Crossing = Spawn(-frame.right * 6100f + Vector3.up * 200f, true, null, enemy);
                salvo.Outbound = Spawn(frame.right * 5700f + Vector3.up * 400f, false, null, enemy);
                salvo.Friendly = Spawn(-frame.forward * 5900f + Vector3.up * 200f, true, null, aircraft);
            }

            bool Live(Missile missile) => missile != null && !missile.disabled && missile.persistentID.IsValid;
            bool Tracked(Missile missile) => Live(missile) && hq.GetTrackingData(missile.persistentID)?.Observed() == true;
            MegaplaneBatteryMount[] lowerBatteries = aircraft.GetComponentsInChildren<MegaplaneBatteryMount>()
                .Where(mount => mount.name.StartsWith(MegaplaneGuns.Prefix + "CIWS", StringComparison.Ordinal) &&
                    mount.name.EndsWith("_Down", StringComparison.Ordinal)).ToArray();
            return new
            {
                tracked = defence.Tracked,
                live = Live(salvo.Inbound) && Live(salvo.Crossing) && Live(salvo.Outbound) && Live(salvo.Friendly),
                inbound = Tracked(salvo.Inbound),
                crossing = Tracked(salvo.Crossing),
                outbound = Tracked(salvo.Outbound),
                friendly = Tracked(salvo.Friendly),
                lowerBatteries = lowerBatteries.Length,
                lowerAxesDown = lowerBatteries.All(mount => mount.Turret != null && mount.Turret.transform != mount.transform &&
                    mount.Turret.transform.IsChildOf(mount.transform) && Vector3.Dot(mount.Turret.transform.up, aircraft.transform.up) < -.95f),
            };
        }
    }
}
#endif
