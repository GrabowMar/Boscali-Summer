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

        public static void Set(GameObject shell, FactionHQ owner)
        {
            if (shell == null) return;
            GarrisonOccupancy marker = shell.GetComponent<GarrisonOccupancy>();
            if (marker == null) marker = shell.AddComponent<GarrisonOccupancy>();
            marker.Owner = owner;
        }

        public static bool IsOccupied(GameObject shell)
        {
            GarrisonOccupancy marker = shell != null ? shell.GetComponent<GarrisonOccupancy>() : null;
            return marker != null && marker.Owner != null;
        }

        public static void Clear(GameObject shell, FactionHQ owner)
        {
            GarrisonOccupancy marker = shell != null ? shell.GetComponent<GarrisonOccupancy>() : null;
            if (marker != null && (owner == null || marker.Owner == owner))
                Object.Destroy(marker);
        }
    }
}
