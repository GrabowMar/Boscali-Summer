using BoscaliSummer.Core.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Modules.Vanguard.Runtime
{
    /// <summary>
    /// Scene reset hub for Vanguard server state (registries, orders, stats, AEGIS CM pod tracking).
    /// </summary>
    internal sealed class AegisService : MonoBehaviour, ISceneService
    {
        public void ResetForScene()
        {
            VanguardRegistry.Clear();
            VanguardFlight.HawcxGlowing.Clear();
            DroneOrders.Clear();
            DroneRoster.Clear();
            VanguardStats.Clear();
            AegisTrigger.Clear();
        }
    }
}
