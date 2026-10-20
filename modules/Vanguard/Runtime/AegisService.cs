using BoscaliSummer.Core.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Scene reset hub for Vanguard server state (registries, orders, stats). AEGIS itself is manual-only:
    /// SNAP fires from AegisTrigger on the pilot's tap; nothing launches a dart on its own.
    /// </summary>
    internal sealed class AegisService : MonoBehaviour, ISceneService
    {
        public void ResetForScene()
        {
            VanguardRegistry.Clear();
            VanguardFlight.HawcxGlowing.Clear();
            DroneOrders.Clear();
            SteerOrders.Clear();
            DroneRoster.Clear();
            VanguardStats.Clear();
            AegisTrigger.Clear();
        }
    }
}
