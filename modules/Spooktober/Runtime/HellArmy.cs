using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Modules.Spooktober.Runtime
{
    /// <summary>
    /// Who belongs to Hell's Army on this peer: each member wears the hell look and draws black on the map
    /// (<see cref="Patches.HellMapColourPatch"/>). Local, no network. The faction that spawns them from the portal is not built yet.
    /// </summary>
    internal static class HellArmy
    {
        /// <summary>Map icon ink: black, and a deep ember red when selected (black on black would show no selection).</summary>
        internal static readonly Color MapInk = new Color(0.03f, 0.01f, 0.01f, 1f);
        internal static readonly Color MapInkSelected = new Color(0.55f, 0.05f, 0.02f, 1f);

        private static readonly HashSet<Unit> Members = new HashSet<Unit>();

        /// <summary>Hot path (every map icon colour): one set lookup, nothing while the army is empty.</summary>
        internal static bool IsHell(Unit unit) => Members.Count > 0 && unit != null && Members.Contains(unit);

        /// <summary>Puts <paramref name="unit"/> in the army: the hell look on, and its map icon recoloured now.</summary>
        internal static void Enlist(Unit unit)
        {
            if (unit == null) return;
            Members.RemoveWhere(m => m == null || m.disabled); // the dead leave the army, or it grows all fight long
            if (!Members.Add(unit)) return;
            RadGhostLook.Apply(unit.gameObject, RadGhostLook.Profile.HellVehicle);
            RecolourIcons();
        }

        internal static int Count => Members.Count;

        /// <summary>Scene reset or teardown: the army is gone (the looks come off with <see cref="RadGhostLook.RemoveAll"/>).</summary>
        internal static void Clear()
        {
            if (Members.Count == 0) return;
            Members.Clear();
            RecolourIcons();
        }

        // ponytail: a scan of the map icons on each enlist; batch it per wave if a portal spawns dozens at once.
        private static void RecolourIcons()
        {
            DynamicMap map = SceneSingleton<DynamicMap>.i;
            if (map == null || map.mapIcons == null) return;
            foreach (MapIcon icon in map.mapIcons)
                if (icon is UnitMapIcon unitIcon && unitIcon != null && unitIcon.unit != null) unitIcon.UpdateColor();
        }
    }
}
