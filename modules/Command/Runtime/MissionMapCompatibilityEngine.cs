using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Runtime
{
    /// <summary>
    /// Read-only adapter for theater dimensions, actual base ownership and objective
    /// ground observations from the synced world state. Neutral missions remain neutral
    /// until there is evidence of combat.
    /// </summary>
    internal sealed class MissionMapCompatibilityEngine : MonoBehaviour, ISceneService
    {
        public void ResetForScene() => TheaterFrame.Invalidate();

        /// <summary>The world span every theater overlay draws against.</summary>
        public Vector2 ResolveTheaterDimensions(DynamicMap dynamicMap) => TheaterFrame.Resolve(dynamicMap);

        /// <summary>Never borrow an arbitrary faction's intelligence for a spectator.</summary>
        public FactionHQ ResolvePlayerHq(DynamicMap dynamicMap)
        {
            return dynamicMap != null ? dynamicMap.HQ : null;
        }

        /// <summary>Uses the registered, fixed airbase catalogue, including neutral bases.</summary>
        public void ReconcileMissionNodes(TacticalSectorGrid grid, FactionHQ playerHq)
        {
            if (grid == null || playerHq == null) return;
            int examined = 0;
            foreach (Airbase airbase in FactionRegistry.airbaseLookup.Values)
            {
                if (++examined > TacticalSectorGrid.MaximumNodes) break;
                // A carrier cannot claim land or disclose its current position via an airbase.
                if (airbase == null || airbase.AttachedAirbase || airbase.UnitDestroyed()) continue;
                Transform anchor = airbase.center != null ? airbase.center : airbase.transform;
                Vector3 pos = anchor.GlobalPosition().AsVector3();
                SectorControl faction = airbase.CurrentHQ == null ? SectorControl.Neutral
                    : airbase.CurrentHQ == playerHq ? SectorControl.Friendly : SectorControl.Hostile;
                grid.RegisterNode(airbase.GetInstanceID(), airbase.name, pos.x, pos.z, faction, 0f, true);
            }
        }

        internal static bool TryGetGroundObservation(Unit unit, FactionHQ localHq, out Vector3 position, out float weight, out bool hostile)
        {
            position = default;
            weight = 0f;
            hostile = false;
            // Dismounted pilots are survivors, not capture infantry. Aircraft (including
            // parked aircraft) likewise never contribute ground-control pressure.
            if (unit == null || unit.disabled || localHq == null || unit.NetworkHQ == null ||
                !(unit is GroundVehicle || unit is Building)) return false;
            hostile = unit.NetworkHQ != localHq;
            // Objective theater state: the frontline reflects actual ground presence, not
            // either side's tracking knowledge, so both sides see the same cells.
            position = unit.GlobalPosition().AsVector3();
            weight = 2.5f;
            return true;
        }
    }
}
