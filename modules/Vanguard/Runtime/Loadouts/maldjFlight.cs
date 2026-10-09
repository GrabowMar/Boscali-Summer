using System.Collections.Generic;
using BoscaliSummer.Core.Game;
using BoscaliSummer.Modules.Vanguard.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>MALD-J "SHADOW" flight: straight and level at 200 m/s so the jam lobe stays on what it was launched at.</summary>
    internal sealed partial class VanguardFlight
    {
        private Vector3 jamBearing; // fixed on the first plan

        private void PlanJammer(GlobalPosition pos)
        {
            if (jamBearing == Vector3.zero)
            {
                // Toward the launch target (the pilot's lock, or an AI launcher's current target), else the nose.
                Vector3 to = target != null && !target.disabled ? target.GlobalPosition() - pos : bearing;
                to.y = 0f;
                jamBearing = to.sqrMagnitude > 1f ? to.normalized : bearing;
                VanguardStats.Bump("maldj.launched");
            }
            GlobalPosition point = pos + jamBearing * 4000f;
            point.y = MaldjShadow.CruiseAltitude(launchPos.y);
            missile.SetThrottle(MaldjShadow.Throttle(missile.speed));
            missile.SetAimpoint(point, Vector3.zero);
        }

        // Secondary effect: close-in vanilla jam on enemy HQ radars in the lobe (Unit.Jam is a server RPC).
        private void JamNoise()
        {
            if (!missile.IsServer || !MaldjShadow.Jamming(missile.timeSinceSpawn)) return;
            FactionHQ own = missile.NetworkHQ;
            GlobalPosition self = missile.GlobalPosition();
            Vector3 forward = missile.transform.forward;
            foreach (FactionHQ hq in FactionRegistry.GetAllHQs())
            {
                if (hq == null || hq == own) continue;
                List<Radar> radars = GameAccess.GetHqRadars(hq);
                if (radars == null) continue;
                for (int i = 0; i < radars.Count; i++)
                {
                    Unit unit = radars[i] != null ? radars[i].GetAttachedUnit() : null;
                    if (unit == null || unit.disabled) continue;
                    Vector3 to = unit.GlobalPosition() - self;
                    if (to.sqrMagnitude > MaldjShadow.NoiseRange * MaldjShadow.NoiseRange || !MaldjShadow.InLobe(forward, to)) continue;
                    unit.Jam(new Unit.JamEventArgs { jammingUnit = missile, jamAmount = MaldjShadow.NoiseAmount });
                    VanguardStats.Bump("maldj.noise");
                }
            }
        }
    }

    /// <summary>
    /// Live MALD-J jammers on THIS peer (tracked from Missile.StartMissile, so clients have them too) and the shadow
    /// test the radar filter (MaldjShadowPatch) and the HUD share. Positions come from the synced missile transforms.
    /// </summary>
    internal static class MaldjField
    {
        public static readonly List<Missile> Jammers = new List<Missile>();

        public static bool Jamming(Missile jammer) =>
            jammer != null && !jammer.disabled && MaldjShadow.Jamming(jammer.timeSinceSpawn);

        public static GlobalPosition ScanPosition(Radar radar)
        {
            Transform scan = radar.GetScanPoint();
            if (scan != null) return scan.GlobalPosition();
            Unit unit = radar.GetAttachedUnit();
            return unit != null ? unit.GlobalPosition() : radar.transform.GlobalPosition();
        }

        /// <summary>Best state any jammer on the aircraft's side gives it against a radar at <paramref name="radar"/>.</summary>
        public static ShadowState Check(GlobalPosition radar, Aircraft aircraft, out float range)
        {
            Vector3 radarToAircraft = aircraft.GlobalPosition() - radar;
            range = radarToAircraft.magnitude;
            FactionHQ side = aircraft.NetworkHQ;
            ShadowState best = ShadowState.None;
            if (side == null) return best;
            for (int i = 0; i < Jammers.Count && best != ShadowState.Shadowed; i++)
            {
                Missile jammer = Jammers[i];
                if (!Jamming(jammer) || jammer.NetworkHQ != side) continue;
                ShadowState state = MaldjShadow.Classify(jammer.transform.forward, radar - jammer.GlobalPosition(), radarToAircraft);
                if (state > best) best = state;
            }
            return best;
        }

        /// <summary>Radar filter: true when this radar must not see this aircraft. Counts denials and burn-throughs.</summary>
        public static bool Hides(Radar radar, Aircraft aircraft)
        {
            ShadowState state = Check(ScanPosition(radar), aircraft, out _);
            if (state == ShadowState.Shadowed) VanguardStats.Bump("maldj.denied");
            else if (state == ShadowState.BurnThrough) VanguardStats.Bump("maldj.burnThrough");
            return state == ShadowState.Shadowed;
        }

        /// <summary>Radar position inside the lobe of a jamming MALD-J launched by <paramref name="owner"/>.</summary>
        public static bool InLobeOf(Unit owner, GlobalPosition radar)
        {
            for (int i = 0; i < Jammers.Count; i++)
            {
                Missile jammer = Jammers[i];
                if (Jamming(jammer) && jammer.owner == owner &&
                    MaldjShadow.InLobe(jammer.transform.forward, radar - jammer.GlobalPosition())) return true;
            }
            return false;
        }

        public static bool HasJamming(Unit owner)
        {
            for (int i = 0; i < Jammers.Count; i++)
                if (Jamming(Jammers[i]) && Jammers[i].owner == owner) return true;
            return false;
        }
    }
}
