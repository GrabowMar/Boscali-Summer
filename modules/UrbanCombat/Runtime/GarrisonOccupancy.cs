using System.Collections.Generic;
using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Server-side occupancy for procedural MapBuilding shells, which do not expose
    /// NetworkHQ like authored Building units do.
    /// </summary>
    internal static class GarrisonOccupancy
    {
        // A plain map, not a marker component: Destroy() is deferred to the end of the frame,
        // so a zone cleared and re-garrisoned in one frame lost its new occupant when the old
        // marker finally died.
        private static readonly Dictionary<GameObject, FactionHQ> owners = new Dictionary<GameObject, FactionHQ>();

        public static void Set(GameObject shell, FactionHQ owner)
        {
            if (shell == null) return;
            if (owner != null) owners[shell] = owner;
            else owners.Remove(shell);
        }

        public static bool IsOccupied(GameObject shell) => shell != null && owners.ContainsKey(shell);

        public static void Clear(GameObject shell, FactionHQ owner)
        {
            // Reference check, not Unity's ==: a shell destroyed with its building must still leave the map.
            if (ReferenceEquals(shell, null)) return;
            if (owners.TryGetValue(shell, out FactionHQ current) && (owner == null || current == owner))
                owners.Remove(shell);
        }

        public static void Reset() => owners.Clear();
    }
}
