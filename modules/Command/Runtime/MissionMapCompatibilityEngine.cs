using System;
using BepInEx.Logging;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Runtime
{
    /// <summary>
    /// Read-only adapter for theater dimensions, actual base ownership and faction-known
    /// ground observations. Neutral missions remain neutral until there is evidence of combat.
    /// </summary>
    internal sealed class MissionMapCompatibilityEngine : MonoBehaviour, ISceneService
    {
        public static MissionMapCompatibilityEngine Active { get; private set; }

        private ManualLogSource logger;

        public Vector2 ResolvedMapSize { get; private set; } = new Vector2(81920f, 81920f);
        public Vector2 MapOffset { get; private set; } = Vector2.zero;
        public bool IsMissionReady { get; private set; }
        private bool dimensionsResolved;

        public void Configure(ManualLogSource log)
        {
            logger = log;
            Active = this;
        }

        public void ResetForScene()
        {
            ResolvedMapSize = new Vector2(81920f, 81920f);
            MapOffset = Vector2.zero;
            IsMissionReady = false;
            dimensionsResolved = false;
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
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
                    MapOffset = new Vector2(mapSettings.OffsetX, mapSettings.OffsetY);
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
            IsMissionReady = true;
        }

        internal static bool TryGetGroundObservation(Unit unit, FactionHQ localHq, out Vector3 position, out float weight, out bool hostile)
        {
            position = default;
            weight = 0f;
            hostile = false;
            if (unit == null || unit.disabled || localHq == null || unit.NetworkHQ == null ||
                !(unit is GroundVehicle || unit is Building || unit is PilotDismounted)) return false;
            hostile = unit.NetworkHQ != localHq;
            float confidence = 1f;
            if (hostile)
            {
                TrackingInfo track = localHq.GetTrackingData(unit.persistentID);
                if (track == null) return false;
                confidence = TacticalSectorGrid.ObservationConfidence(Time.timeSinceLevelLoad - track.lastSpottedTime);
                if (confidence <= 0f) return false;
                // GetPosition can itself update from the live transform; read the record only.
                position = track.lastKnownPosition.AsVector3();
            }
            else position = unit.GlobalPosition().AsVector3();
            weight = (unit is PilotDismounted ? 0.8f : 2.5f) * confidence;
            return true;
        }
    }
}
