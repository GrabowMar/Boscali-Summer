using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>
    /// Hell's Army membership and map ink. HQ ownership also covers captured structures and reinforcements.
    /// </summary>
    internal static class HellArmy
    {
        /// <summary>Violet stays distinct from ordinary red hostiles; the pale selection is readable over the corruption fill.</summary>
        internal static readonly Color MapInk = new Color(0.82f, 0.32f, 1f, 1f);
        internal static readonly Color MapInkSelected = new Color(1f, 0.86f, 1f, 1f);

        private static readonly HashSet<Unit> Members = new HashSet<Unit>();
        private static readonly HashSet<Unit> Previews = new HashSet<Unit>();
        private static readonly Dictionary<Unit, RadGhostLook.Handle> Fallback = new Dictionary<Unit, RadGhostLook.Handle>();
        private static readonly List<Unit> Released = new List<Unit>();
        private static float nextRefresh;

        /// <summary>Hot path (every map icon colour): ownership plus the explicit membership used by preview tools.</summary>
        internal static bool IsHell(Unit unit) => unit != null &&
            (HellSide.Side.Owns(unit.NetworkHQ) || Previews.Count > 0 && Previews.Contains(unit));

        /// <summary>Puts <paramref name="unit"/> in the army: the hell look on, and its map icon recoloured now.</summary>
        internal static void Enlist(Unit unit)
        {
            if (unit == null) return;
            if (!HellSide.Side.Owns(unit.NetworkHQ)) Previews.Add(unit); // the explicit visual-preview tool does not change faction
            else Previews.Remove(unit);
            bool added = Members.Add(unit);
            Dress(unit);
            if (added) RecolourIcons();
        }

        internal static int Count => Members.Count;
        internal static int ShellCount => HellVehicleLook.Count;
        internal static int HiddenRenderers => HellVehicleLook.HiddenRenderers;

        /// <summary>One scan a second catches captured/reinforced vehicles, new damage meshes and ownership releases.</summary>
        internal static void Frame(float now)
        {
            if (now < nextRefresh) return;
            nextRefresh = now + 1f;
            HellVehicleLook.Refresh();
            Released.Clear();
            foreach (KeyValuePair<Unit, RadGhostLook.Handle> pair in Fallback)
                if (pair.Key == null || pair.Key.disabled || !IsHell(pair.Key)) Released.Add(pair.Key);
            foreach (Unit unit in Released) { Fallback[unit].Remove(); Fallback.Remove(unit); }
            Released.Clear();
            bool changed = Members.RemoveWhere(unit => unit == null || unit.disabled || !IsHell(unit)) > 0;
            Previews.RemoveWhere(unit => unit == null);
            if (UnitRegistry.allUnits != null)
                foreach (Unit unit in UnitRegistry.allUnits)
                {
                    if (unit == null || unit.disabled || !(unit is GroundVehicle || unit is Aircraft) || !HellSide.Side.Owns(unit.NetworkHQ)) continue;
                    Previews.Remove(unit);
                    changed |= Members.Add(unit);
                    Dress(unit);
                }
            if (changed) RecolourIcons();
        }

        private static void Dress(Unit unit)
        {
            if (unit is Building) return; // territory owns building materials; never cover structures with a vehicle hull
            if (HellVehicleLook.Apply(unit))
            {
                if (Fallback.TryGetValue(unit, out RadGhostLook.Handle previous)) { previous.Remove(); Fallback.Remove(unit); }
                return;
            }
            if (Fallback.ContainsKey(unit)) return;
            RadGhostLook.Handle handle = RadGhostLook.Apply(unit.gameObject, RadGhostLook.Profile.HellVehicle);
            if (handle != null) Fallback.Add(unit, handle);
        }

        /// <summary>Scene reset or teardown: the army is gone (the looks come off with <see cref="RadGhostLook.RemoveAll"/>).</summary>
        internal static void Clear()
        {
            HellVehicleLook.Clear();
            foreach (RadGhostLook.Handle handle in Fallback.Values) handle.Remove();
            Fallback.Clear();
            Previews.Clear();
            Members.Clear();
            Released.Clear();
            nextRefresh = 0f;
            RecolourIcons();
        }

        // ponytail: a scan of the map icons on each enlist; batch it per wave if a portal spawns dozens at once.
        internal static void RecolourIcons()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || map.mapIcons == null) return;
            foreach (MapIcon icon in map.mapIcons)
                if (icon != null) icon.UpdateColor();
        }
    }
}
