using UnityEngine;

namespace BoscaliSummer.Garrisons
{
    /// <summary>
    /// Lightweight server-side marker for procedural MapBuilding shells, which do not
    /// expose NetworkHQ like authored Building units do.
    /// </summary>
    internal sealed class GarrisonOccupancy : MonoBehaviour
    {
        public FactionHQ Owner;

        private static readonly System.Collections.Generic.HashSet<GameObject> occupiedShells =
            new System.Collections.Generic.HashSet<GameObject>();

        public static void Set(GameObject shell, FactionHQ owner)
        {
            if (shell == null) return;
            GarrisonOccupancy marker = shell.GetComponent<GarrisonOccupancy>();
            if (marker == null) marker = shell.AddComponent<GarrisonOccupancy>();
            marker.Owner = owner;
            if (owner != null)
                occupiedShells.Add(shell);
            else
                occupiedShells.Remove(shell);
        }

        public static bool IsOccupied(GameObject shell)
        {
            return shell != null && occupiedShells.Contains(shell);
        }

        public static void Clear(GameObject shell, FactionHQ owner)
        {
            if (shell == null) return;
            GarrisonOccupancy marker = shell.GetComponent<GarrisonOccupancy>();
            if (marker != null && (owner == null || marker.Owner == owner))
            {
                occupiedShells.Remove(shell);
                Object.Destroy(marker);
            }
        }

        public static void Reset()
        {
            occupiedShells.Clear();
        }
    }
}
