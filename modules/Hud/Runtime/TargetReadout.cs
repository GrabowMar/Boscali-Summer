using BoscaliSummer.Modules.Hud.Domain;
using UnityEngine;

namespace BoscaliSummer.Modules.Hud.Runtime
{
    /// <summary>
    /// The first selected target as the HUD reads it, the third-person target card's reader (name, range and closure): the first of the weapon
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
}
