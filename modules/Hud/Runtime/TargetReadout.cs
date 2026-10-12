using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// The first selected target as the HUD reads it, one reader for the third-person target card (range and closure) and the cockpit's NAV DATA block (name, bearing, range and closure): the first of the weapon
    /// manager's target list, its range, and the closing speed from two reads of the same target (positive = closing; NaN until there are two, and when the target changes). The name is upper-cased once per target,
    /// not once per read. Client-local, read-only.
    /// </summary>
    internal sealed class TargetReadout
    {
        private Unit target, named;
        private float lastRange, lastTime;
        private string name;

        /// <summary>The first selected target and its range (metres) and closure; false (and NaN) when nothing is selected.</summary>
        public bool Read(Aircraft aircraft, out Unit unit, out float range, out float closure)
        {
            unit = null;
            range = closure = float.NaN;
            if (aircraft == null || aircraft.weaponManager == null) { target = null; return false; }
            var targets = aircraft.weaponManager.GetTargetList();
            if (targets == null || targets.Count == 0 || targets[0] == null) { target = null; return false; }
            unit = targets[0];
            range = Vector3.Distance(aircraft.transform.position, unit.transform.position);
            float now = Time.unscaledTime;
            if (ReferenceEquals(unit, target) && now > lastTime + 0.05f) closure = (lastRange - range) / (now - lastTime);
            target = unit;
            lastRange = range;
            lastTime = now;
            return true;
        }

        /// <summary>The target's name in capitals ("TGT" when the game gives it none): built when the target changes, the same string until then.</summary>
        public string NameOf(Unit unit)
        {
            if (unit == null) return null;
            if (!ReferenceEquals(unit, named))
            {
                named = unit;
                name = unit.definition != null && !string.IsNullOrEmpty(unit.definition.unitName) ? unit.definition.unitName.ToUpperInvariant() : "TGT";
            }
            return name;
        }

        public void Release()
        {
            target = named = null;
            name = null;
        }
    }

    /// <summary>
    /// Reads the NAV DATA block's tick of data (<see cref="NavInput"/>) out of what the board already knows: the fuel watch's nearest field, endurance and flags, the selected target, and the game's mission clock. Cockpit
    /// view only: in the external views the third-person cluster's target card owns the bottom-right corner, so there is nothing to read.
    /// </summary>
    internal static class NavReader
    {
        public static void Read(Aircraft aircraft, BoardFrame frame, bool cockpitView, TargetReadout targets)
        {
            frame.Nav = default;
            frame.CockpitView = cockpitView;
            if (!cockpitView || aircraft == null) { targets.Release(); return; }
            ref NavInput nav = ref frame.Nav;
            nav.Units = frame.Units;
            FuelState fuel = frame.Fuel;
            if (fuel.Valid)
            {
                nav.HasFuel = true;
                nav.EnduranceS = fuel.EnduranceS;
                nav.Bingo = fuel.Bingo;
                nav.Joker = fuel.Joker;
                if (!string.IsNullOrEmpty(fuel.Field) && float.IsFinite(fuel.FieldM))
                {
                    nav.HasHome = true;
                    nav.HomeName = fuel.Field;
                    nav.HomeM = fuel.FieldM;
                    nav.HomeBearingDeg = fuel.FieldBearingDeg;
                }
            }
            if (targets.Read(aircraft, out Unit unit, out float range, out float closure))
            {
                Vector3 to = unit.transform.position - aircraft.transform.position;
                nav.HasTarget = true;
                nav.TargetName = targets.NameOf(unit);
                nav.TargetM = range;
                nav.TargetBearingDeg = (Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg % 360f + 360f) % 360f;
                nav.ClosureMps = closure;
            }
            nav.HasClock = true;
            nav.ClockS = BoscaliSummer.Core.Game.MissionTimeSource.Now();
        }
    }
}
