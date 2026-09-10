using UnityEngine;

namespace BoscaliSummer.Framework.Contracts
{
    /// <summary>
    /// Read-only cross-feature view of whether a civilian shell is owned by a gameplay
    /// system. Consumers must not depend on the provider's marker or manager types.
    /// </summary>
    internal interface IBuildingOccupancy
    {
        bool IsOccupied(GameObject shell);
    }
}
