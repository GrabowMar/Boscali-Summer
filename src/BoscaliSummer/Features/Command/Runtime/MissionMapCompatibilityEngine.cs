using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;

namespace BoscaliSummer.Features.Command.Runtime
{
    /// <summary>
    /// Startup and runtime adapter that normalizes any mission data (Free Flight, Escalation, or
    /// player-made custom missions) into compatible theater map boundaries, factions, and strategic nodes.
    /// Guarantees that every mission generates organic Allied vs OPFOR frontlines even if no airbases exist.
    /// </summary>
    internal sealed class MissionMapCompatibilityEngine : MonoBehaviour, ISceneService
    {
        public static MissionMapCompatibilityEngine Active { get; private set; }

        private CommandSettings settings;
        private ManualLogSource logger;

        public Vector2 ResolvedMapSize { get; private set; } = new Vector2(81920f, 81920f);
        public Vector2 MapOffset { get; private set; } = Vector2.zero;
        public bool IsMissionReady { get; private set; }

        public void Configure(CommandSettings config, ManualLogSource log)
        {
            settings = config;
            logger = log;
            Active = this;
        }

        public void ResetForScene()
        {
            ResolvedMapSize = new Vector2(81920f, 81920f);
            MapOffset = Vector2.zero;
            IsMissionReady = false;
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
            try
            {
                MapSettings mapSettings = UnityEngine.Object.FindObjectOfType<MapSettings>();
                if (mapSettings != null && mapSettings.MapSize.x > 1000f && mapSettings.MapSize.y > 1000f)
                {
                    ResolvedMapSize = mapSettings.MapSize;
                    MapOffset = new Vector2(mapSettings.OffsetX, mapSettings.OffsetY);
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
                    return ResolvedMapSize;
                }
            }
            catch { }

            ResolvedMapSize = new Vector2(163840f, 81920f);
            return ResolvedMapSize;
        }

        /// <summary>
        /// Discovers the local player's HQ with robust fallbacks for custom missions.
        /// </summary>
        public FactionHQ ResolvePlayerHq(DynamicMap dynamicMap)
        {
            if (dynamicMap != null && dynamicMap.HQ != null)
                return dynamicMap.HQ;

            var allHqs = FactionRegistry.GetAllHQs();
            if (allHqs != null)
            {
                foreach (FactionHQ hq in allHqs)
                {
                    if (hq != null) return hq;
                }
            }

            return null;
        }

        /// <summary>
        /// Populates tactical sector grid nodes from all mission sources:
        /// 1. Airbases (assigned or claimed via unit proximity if neutral/disabled)
        /// 2. Forward Landing Zones & Encampments
        /// 3. Mission Objectives & Installations (Vehicle Depots, Factories, Naval Vessels)
        /// </summary>
        public void ReconcileMissionNodes(TacticalSectorGrid grid, FactionHQ playerHq)
        {
            if (grid == null) return;

            HashSet<int> seenNodeIds = new HashSet<int>();
            int friendlyNodeCount = 0;
            int hostileNodeCount = 0;

            // 1. Scan Airbases
            List<Airbase> allAirbases = new List<Airbase>(16);

            var allHqs = FactionRegistry.GetAllHQs();
            if (allHqs != null)
            {
                foreach (FactionHQ hq in allHqs)
                {
                    if (hq == null) continue;
                    var hqAirbases = hq.GetAirbases();
                    if (hqAirbases != null)
                    {
                        foreach (Airbase ab in hqAirbases)
                        {
                            if (ab != null && !ab.UnitDestroyed() && !allAirbases.Contains(ab))
                                allAirbases.Add(ab);
                        }
                    }
                }
            }

            Airbase[] sceneAirbases = UnityEngine.Object.FindObjectsOfType<Airbase>();
            if (sceneAirbases != null)
            {
                for (int i = 0; i < sceneAirbases.Length; i++)
                {
                    Airbase ab = sceneAirbases[i];
                    if (ab != null && !ab.UnitDestroyed() && !allAirbases.Contains(ab))
                        allAirbases.Add(ab);
                }
            }

            // Evaluate airbases
            for (int i = 0; i < allAirbases.Count; i++)
            {
                Airbase ab = allAirbases[i];
                if (ab == null || !seenNodeIds.Add(ab.GetInstanceID())) continue;

                Transform anchor = (ab.center != null) ? ab.center : ab.transform;
                Vector3 pos = anchor.GlobalPosition().AsVector3();

                SectorControl faction = SectorControl.Neutral;
                if (ab.CurrentHQ != null)
                {
                    faction = (playerHq != null && ab.CurrentHQ == playerHq) ? SectorControl.Friendly : SectorControl.Hostile;
                }
                else
                {
                    // Made-mission airbase with CurrentHQ == null:
                    // Check if player/friendly units or hostile units are stationed at this airbase
                    faction = InferAirbaseFaction(pos, playerHq);
                }

                if (faction == SectorControl.Friendly) friendlyNodeCount++;
                else if (faction == SectorControl.Hostile) hostileNodeCount++;

                grid.RegisterNode(ab.GetInstanceID(), ab.name ?? ("Airbase_" + ab.GetInstanceID()), pos.x, pos.z, faction, 0f, true);
            }

            // 2. Scan Ground Units for Forward LZs, Encampments, Ships & Depots
            List<Unit> allUnits = UnitRegistry.allUnits;
            if (allUnits != null)
            {
                for (int i = 0; i < allUnits.Count; i++)
                {
                    Unit u = allUnits[i];
                    if (u == null || u.disabled) continue;

                    bool isHostile = (playerHq != null && u.NetworkHQ != playerHq);
                    SectorControl unitFaction = isHostile ? SectorControl.Hostile : SectorControl.Friendly;
                    Vector3 pos = u.GlobalPosition().AsVector3();

                    // Forward LZ / Encampment
                    if (u is Building b && b.name != null && b.name.IndexOf("Encampment", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (seenNodeIds.Add(b.GetInstanceID()))
                        {
                            if (unitFaction == SectorControl.Friendly) friendlyNodeCount++;
                            else hostileNodeCount++;
                            grid.RegisterNode(b.GetInstanceID(), "FOB_" + b.GetInstanceID(), pos.x, pos.z, unitFaction, 0f, false);
                        }
                    }
                    // Naval Vessels (Aircraft Carriers, Cruisers) act as mobile strategic nodes
                    else if (u is Ship ship && seenNodeIds.Add(ship.GetInstanceID()))
                    {
                        if (unitFaction == SectorControl.Friendly) friendlyNodeCount++;
                        else hostileNodeCount++;
                        grid.RegisterNode(ship.GetInstanceID(), ship.unitName ?? "Fleet", pos.x, pos.z, unitFaction, 0f, true);
                    }
                }
            }

            // 3. Fallback for Made Missions without Airbases: Synthesize Strategic Nodes from Depots & Objectives
            if (friendlyNodeCount == 0 || hostileNodeCount == 0)
            {
                SynthesizeMissionStrategicNodes(grid, playerHq, seenNodeIds, ref friendlyNodeCount, ref hostileNodeCount);
            }

            IsMissionReady = true;
        }

        private SectorControl InferAirbaseFaction(Vector3 airbasePos, FactionHQ playerHq)
        {
            if (playerHq == null) return SectorControl.Neutral;

            float friendlyScore = 0f;
            float hostileScore = 0f;
            float checkDistSq = 3500f * 3500f; // 3.5km airfield perimeter

            List<Unit> allUnits = UnitRegistry.allUnits;
            if (allUnits != null)
            {
                for (int i = 0; i < allUnits.Count; i++)
                {
                    Unit u = allUnits[i];
                    if (u == null || u.disabled) continue;

                    Vector3 pos = u.GlobalPosition().AsVector3();
                    float dSq = (pos.x - airbasePos.x) * (pos.x - airbasePos.x) + (pos.z - airbasePos.z) * (pos.z - airbasePos.z);
                    if (dSq <= checkDistSq)
                    {
                        bool isHostile = (u.NetworkHQ != playerHq);
                        float weight = (u is Aircraft) ? 5.0f : ((u is Building) ? 3.0f : 1.0f);
                        if (isHostile) hostileScore += weight;
                        else friendlyScore += weight;
                    }
                }
            }

            if (friendlyScore > hostileScore && friendlyScore >= 2.0f)
                return SectorControl.Friendly;
            if (hostileScore > friendlyScore && hostileScore >= 2.0f)
                return SectorControl.Hostile;

            return SectorControl.Neutral;
        }

        private void SynthesizeMissionStrategicNodes(
            TacticalSectorGrid grid,
            FactionHQ playerHq,
            HashSet<int> seenIds,
            ref int friendlyCount,
            ref int hostileCount)
        {
            // A. Check Active In-Game Objective Markers
            try
            {
                ObjectiveMarker[] markers = UnityEngine.Object.FindObjectsOfType<ObjectiveMarker>();
                if (markers != null)
                {
                    for (int i = 0; i < markers.Length; i++)
                    {
                        ObjectiveMarker m = markers[i];
                        if (m == null) continue;

                        int synId = 900000 + m.GetInstanceID();
                        if (seenIds.Add(synId))
                        {
                            Vector3 pos = m.transform.GlobalPosition().AsVector3();
                            // Mission target objectives in Nuclear Option are standard hostile targets
                            SectorControl faction = SectorControl.Hostile;
                            hostileCount++;
                            grid.RegisterNode(synId, "Mission_Objective", pos.x, pos.z, faction, 0f, false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning("[COM] Objective marker scan notice: " + ex.Message);
            }

            // B. Check Major Infrastructure (VehicleDepot, factory, hangars)
            List<Unit> allUnits = UnitRegistry.allUnits;
            if (allUnits != null)
            {
                for (int i = 0; i < allUnits.Count; i++)
                {
                    Unit u = allUnits[i];
                    if (u == null || u.disabled) continue;

                    if (u is Building b && b.name != null)
                    {
                        bool isDepot = b.name.IndexOf("VehicleDepot", StringComparison.OrdinalIgnoreCase) >= 0;
                        bool isFactory = b.name.IndexOf("factory", StringComparison.OrdinalIgnoreCase) >= 0;

                        if (isDepot || isFactory)
                        {
                            int synId = 800000 + b.GetInstanceID();
                            if (seenIds.Add(synId))
                            {
                                bool isHostile = (playerHq != null && b.NetworkHQ != playerHq);
                                SectorControl faction = isHostile ? SectorControl.Hostile : SectorControl.Friendly;
                                if (faction == SectorControl.Hostile) hostileCount++;
                                else friendlyCount++;

                                Vector3 pos = b.GlobalPosition().AsVector3();
                                grid.RegisterNode(synId, b.name, pos.x, pos.z, faction, 0f, false);
                            }
                        }
                    }
                }
            }

            // C. Final fallback: centroid of friendly and hostile ground forces
            if (friendlyCount == 0 && allUnits != null)
            {
                Vector3 friendlyCenter = Vector3.zero;
                int count = 0;
                for (int i = 0; i < allUnits.Count; i++)
                {
                    Unit u = allUnits[i];
                    if (u == null || u.disabled || u is Aircraft || (playerHq != null && u.NetworkHQ != playerHq)) continue;
                    friendlyCenter += u.GlobalPosition().AsVector3();
                    count++;
                }
                if (count > 0)
                {
                    friendlyCenter /= count;
                    int synId = 700001;
                    if (seenIds.Add(synId))
                    {
                        friendlyCount++;
                        grid.RegisterNode(synId, "Allied_Base_Forces", friendlyCenter.x, friendlyCenter.z, SectorControl.Friendly, 0f, false);
                    }
                }
            }

            if (hostileCount == 0 && allUnits != null)
            {
                Vector3 hostileCenter = Vector3.zero;
                int count = 0;
                for (int i = 0; i < allUnits.Count; i++)
                {
                    Unit u = allUnits[i];
                    if (u == null || u.disabled || u is Aircraft || (playerHq != null && u.NetworkHQ == playerHq)) continue;
                    hostileCenter += u.GlobalPosition().AsVector3();
                    count++;
                }
                if (count > 0)
                {
                    hostileCenter /= count;
                    int synId = 700002;
                    if (seenIds.Add(synId))
                    {
                        hostileCount++;
                        grid.RegisterNode(synId, "OPFOR_Base_Forces", hostileCenter.x, hostileCenter.z, SectorControl.Hostile, 0f, false);
                    }
                }
            }
        }
    }
}
