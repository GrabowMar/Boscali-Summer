using UnityEngine;

namespace BoscaliSummer.Modules.SeaStructures.Runtime
{
    /// <summary>
    /// Marks a prefab object as one point of the structure's deterministic damage plan. The index is
    /// into <see cref="Domain.SeaDamagePlans"/>; every peer builds the same plan in the same order, so
    /// the serialized index is enough to bind the point's native part, mesh and capacity at spawn.
    /// </summary>
    internal sealed class SeaPointTag : MonoBehaviour
    {
        public int Index;
    }
}
