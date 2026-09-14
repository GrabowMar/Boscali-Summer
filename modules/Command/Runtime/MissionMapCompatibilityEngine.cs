using System;
using BepInEx.Logging;
using BoscaliSummer.Framework.Lifecycle;
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
        private ManualLogSource logger;

        public Vector2 ResolvedMapSize { get; private set; } = new Vector2(81920f, 81920f);
        private bool dimensionsResolved;

        public void Configure(ManualLogSource log)
        {
            logger = log;
        }

        public void ResetForScene()
        {
            ResolvedMapSize = new Vector2(81920f, 81920f);
            dimensionsResolved = false;
        }

        /// <summary>
        /// Normalizes theater bounds from MapSettings, DynamicMap, or LevelInfo.
        /// </summary>
        public Vector2 ResolveTheaterDimensions(DynamicMap dynamicMap)
        {
            if (dimensionsResolved) return ResolvedMapSize;
            try
            {
                MapSettings mapSettings = UnityEngine.Object.FindObjectOfType<MapSettings>();
                if (mapSettings != null && mapSettings.MapSize.x > 1000f && mapSettings.MapSize.y > 1000f)
                {
                    ResolvedMapSize = mapSettings.MapSize;
                    dimensionsResolved = true;
                    return ResolvedMapSize;
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning("[COM] Error reading MapSettings: " + ex.Message);
            }

            try
            {
                if (dynamicMap != null && dynamicMap.mapImage != null)
                {
                    RectTransform rect = dynamicMap.mapImage.GetComponent<RectTransform>();
                    if (rect != null && rect.sizeDelta.x > 100f && rect.sizeDelta.y > 100f)
                    {
                        float szX = (rect.sizeDelta.x / 900f) * 81920f;
                        float szY = (rect.sizeDelta.y / 900f) * 81920f;
                        if (szX > 1000f && szY > 1000f)
                        {
                            ResolvedMapSize = new Vector2(szX, szY);
                            dimensionsResolved = true;
                            return ResolvedMapSize;
                        }
                    }
                }
            }
            catch { }

            try
            {
                LevelInfo levelInfo = NetworkSceneSingleton<LevelInfo>.i;
                if (levelInfo != null && levelInfo.mapSize > 1000f)
                {
                    ResolvedMapSize = new Vector2(levelInfo.mapSize * 2f, levelInfo.mapSize);
                    dimensionsResolved = true;
                    return ResolvedMapSize;
                }
            }
            catch { }

            ResolvedMapSize = new Vector2(163840f, 81920f);
            return ResolvedMapSize;
        }

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
