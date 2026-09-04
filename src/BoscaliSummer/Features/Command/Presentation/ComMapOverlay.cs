using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Features.Command.Patches;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    internal sealed class ComMapOverlay : MonoBehaviour, ISceneService
    {
        private const int BaseTextureWidth = 512;

        private CommandSettings settings;
        private CommandManager command;
        private ManualLogSource logger;

        private DynamicMap dynamicMap;
        private GameObject overlayObj;
        private RawImage overlayImage;
        private Texture2D overlayTexture;
        private TacticalSectorGrid sectorGrid;
        private AiOrderExtractor orderExtractor;
        private MissionMapCompatibilityEngine compatibilityEngine;

        // Pooled Vector Objects
        private sealed class PooledVector
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Line;
        }

        private sealed class PooledMarker
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Graphic;
        }

        private readonly List<PooledVector> vectorPool = new List<PooledVector>(48);
        private readonly List<PooledVector> thrustPool = new List<PooledVector>(12);
        private readonly List<PooledMarker> clashPool = new List<PooledMarker>(16);
        private readonly List<PooledMarker> nodeRingPool = new List<PooledMarker>(16);

        private readonly List<AiTaskingOrder> activeOrders = new List<AiTaskingOrder>(48);

        private float nextGridUpdate;
        private float nextVectorUpdate;
        private bool isMapMaximized;
        private bool initialized;

        public bool ShowSectors = true;
        public bool ShowFrontlines = true;
        public bool ShowThreatRings = false;
        public bool ShowNodes = true;
        public bool ShowClashes = true;
        public bool ShowAttackRoutes = true;
        public bool ShowAiOrders = true;

        public void Configure(CommandSettings config, CommandManager manager, MissionMapCompatibilityEngine compat, ManualLogSource log)
        {
            settings = config;
            command = manager;
            compatibilityEngine = compat;
            logger = log;

            ShowSectors = settings.FrontlinesOverlay.Value;
            ShowFrontlines = settings.FrontlinesOverlay.Value;
            ShowThreatRings = settings.RadarCoverageOverlay.Value;
            ShowAiOrders = settings.AiOrdersOverlay.Value;
            ShowNodes = true;
            ShowClashes = true;
            ShowAttackRoutes = true;

            int res = settings.GridResolution.Value;
            sectorGrid = new TacticalSectorGrid(res > 0 ? res : TacticalSectorGrid.DefaultResolution, 100000f);
            orderExtractor = new AiOrderExtractor(settings.MaxOrderVectors.Value);

            DynamicMapMaximizePatch.OnMaximized += HandleMapMaximized;
            DynamicMapMinimizePatch.OnMinimized += HandleMapMinimized;
        }

        public void ResetForScene()
        {
            if (overlayObj != null)
            {
                Destroy(overlayObj);
                overlayObj = null;
            }
            overlayImage = null;

            if (overlayTexture != null)
            {
                Destroy(overlayTexture);
                overlayTexture = null;
            }

            for (int i = 0; i < vectorPool.Count; i++)
            {
                if (vectorPool[i].Root != null) Destroy(vectorPool[i].Root);
            }
            vectorPool.Clear();

            for (int i = 0; i < thrustPool.Count; i++)
            {
                if (thrustPool[i].Root != null) Destroy(thrustPool[i].Root);
            }
            thrustPool.Clear();

            for (int i = 0; i < clashPool.Count; i++)
            {
                if (clashPool[i].Root != null) Destroy(clashPool[i].Root);
            }
            clashPool.Clear();

            for (int i = 0; i < nodeRingPool.Count; i++)
            {
                if (nodeRingPool[i].Root != null) Destroy(nodeRingPool[i].Root);
            }
            nodeRingPool.Clear();

            if (sectorGrid != null)
            {
                sectorGrid.ResetAll();
            }

            dynamicMap = null;
            initialized = false;
            isMapMaximized = false;
            nextGridUpdate = 0f;
            nextVectorUpdate = 0f;
        }

        private void OnDestroy()
        {
            DynamicMapMaximizePatch.OnMaximized -= HandleMapMaximized;
            DynamicMapMinimizePatch.OnMinimized -= HandleMapMinimized;
            ResetForScene();
        }

        private void HandleMapMaximized()
        {
            isMapMaximized = true;
            nextGridUpdate = 0f;
            nextVectorUpdate = 0f;
            if (overlayObj != null) overlayObj.SetActive(true);
        }

        private void HandleMapMinimized()
        {
            isMapMaximized = false;
            if (overlayObj != null) overlayObj.SetActive(false);
            HideAllVectors();
        }

        private void Update()
        {
            if (settings == null || !settings.Enabled.Value) return;

            if (!initialized)
            {
                TryInitialize();
                return;
            }

            if (dynamicMap == null) return;
            isMapMaximized = DynamicMap.mapMaximized;

            // Strict visibility control: Overlay is strictly for the Maximized Theater Map!
            // When minimized (cockpit flight HUD), overlay MUST stay disabled so it never pollutes the cockpit MFD.
            if (overlayObj != null && overlayObj.activeSelf != isMapMaximized)
            {
                overlayObj.SetActive(isMapMaximized);
            }

            if (!isMapMaximized)
            {
                HideAllVectors();
                return;
            }

            // Ensure overlay stays parented to mapImage with stretch anchors
            if (overlayObj != null && dynamicMap.mapImage != null && overlayObj.transform.parent != dynamicMap.mapImage.transform)
            {
                overlayObj.transform.SetParent(dynamicMap.mapImage.transform, false);
                RectTransform overlayRect = overlayObj.GetComponent<RectTransform>();
                overlayRect.anchorMin = Vector2.zero;
                overlayRect.anchorMax = Vector2.one;
                overlayRect.offsetMin = Vector2.zero;
                overlayRect.offsetMax = Vector2.zero;
                overlayRect.localScale = Vector3.one;
                overlayRect.localPosition = Vector3.zero;
            }

            float now = Time.unscaledTime;

            if (now >= nextGridUpdate)
            {
                nextGridUpdate = now + settings.GridRefreshInterval.Value;
                UpdateSectorGrid();
            }

            if (now >= nextVectorUpdate)
            {
                nextVectorUpdate = now + settings.VectorRefreshInterval.Value;
                UpdateTacticalVectors();
            }
        }

        private static Vector2 GetLoadedMapSize(DynamicMap map)
        {
            try
            {
                MapSettings mapSettings = UnityEngine.Object.FindObjectOfType<MapSettings>();
                if (mapSettings != null && mapSettings.MapSize.x > 1000f && mapSettings.MapSize.y > 1000f)
                {
                    return mapSettings.MapSize;
                }
            }
            catch { }

            try
            {
                if (map != null && map.mapImage != null)
                {
                    RectTransform rect = map.mapImage.GetComponent<RectTransform>();
                    if (rect != null && rect.sizeDelta.x > 100f && rect.sizeDelta.y > 100f)
                    {
                        // DynamicMap sets: sizeDelta = Vector2.one * (mapSettings.MapSize / 81920f) * 900f
                        // Therefore: MapSize = (sizeDelta / 900f) * 81920f
                        float szX = (rect.sizeDelta.x / 900f) * 81920f;
                        float szY = (rect.sizeDelta.y / 900f) * 81920f;
                        if (szX > 1000f && szY > 1000f)
                        {
                            return new Vector2(szX, szY);
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
                    return new Vector2(levelInfo.mapSize * 2f, levelInfo.mapSize);
                }
            }
            catch { }

            return new Vector2(163840f, 81920f);
        }

        private void EnsureTexture(int texW, int texH)
        {
            if (overlayTexture != null && overlayTexture.width == texW && overlayTexture.height == texH)
                return;

            if (overlayTexture != null)
            {
                Destroy(overlayTexture);
            }

            overlayTexture = new Texture2D(texW, texH, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // Immediately clear to transparent pixels so the overlay never shows uninitialized grey memory
            Color32[] transparentPixels = new Color32[texW * texH];
            overlayTexture.SetPixels32(transparentPixels);
            overlayTexture.Apply(false);

            if (overlayImage != null)
            {
                overlayImage.texture = overlayTexture;
            }
        }

        private void TryInitialize()
        {
            dynamicMap = SceneSingleton<DynamicMap>.i;
            if (dynamicMap == null || dynamicMap.mapImage == null) return;

            RectTransform mapImageRect = dynamicMap.mapImage.GetComponent<RectTransform>();
            if (mapImageRect == null) return;

            Vector2 mapSize = (compatibilityEngine != null)
                ? compatibilityEngine.ResolveTheaterDimensions(dynamicMap)
                : GetLoadedMapSize(dynamicMap);
            sectorGrid.SetWorldSize(mapSize.x, mapSize.y);

            int texW = BaseTextureWidth;
            int texH = (int)Math.Max(128, Math.Round(texW * (mapSize.y / mapSize.x)));
            EnsureTexture(texW, texH);

            overlayObj = new GameObject("ComSectorGridOverlay", typeof(RectTransform), typeof(RawImage));
            overlayObj.transform.SetParent(dynamicMap.mapImage.transform, false);

            RectTransform overlayRect = overlayObj.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.pivot = mapImageRect.pivot;
            overlayRect.localScale = Vector3.one;
            overlayRect.localPosition = Vector3.zero;

            overlayImage = overlayObj.GetComponent<RawImage>();
            overlayImage.texture = overlayTexture;
            overlayImage.raycastTarget = false;
            overlayObj.SetActive(DynamicMap.mapMaximized);

            // Draw overlay directly above terrain image
            overlayObj.transform.SetAsFirstSibling();

            // Initialize Vector Pools under iconLayer
            Transform vectorParent = dynamicMap.iconLayer != null
                ? dynamicMap.iconLayer.transform
                : dynamicMap.mapTransform;

            // 1. AI Flight Orders Pool
            int flightPoolSize = settings.MaxOrderVectors.Value;
            for (int i = 0; i < flightPoolSize; i++)
            {
                GameObject vecObj = new GameObject("ComFlightVec_" + i, typeof(RectTransform), typeof(Image));
                vecObj.transform.SetParent(vectorParent, false);

                RectTransform r = vecObj.GetComponent<RectTransform>();
                r.pivot = new Vector2(0f, 0.5f);
                r.anchorMin = new Vector2(0.5f, 0.5f);
                r.anchorMax = new Vector2(0.5f, 0.5f);

                Image img = vecObj.GetComponent<Image>();
                img.raycastTarget = false;

                vecObj.SetActive(false);
                vectorPool.Add(new PooledVector { Root = vecObj, Rect = r, Line = img });
            }

            // 2. Attack Thrust Arrows Pool
            for (int i = 0; i < 12; i++)
            {
                GameObject thrustObj = new GameObject("ComAttackThrust_" + i, typeof(RectTransform), typeof(Image));
                thrustObj.transform.SetParent(vectorParent, false);

                RectTransform r = thrustObj.GetComponent<RectTransform>();
                r.pivot = new Vector2(0f, 0.5f);
                r.anchorMin = new Vector2(0.5f, 0.5f);
                r.anchorMax = new Vector2(0.5f, 0.5f);

                Image img = thrustObj.GetComponent<Image>();
                img.raycastTarget = false;

                thrustObj.SetActive(false);
                thrustPool.Add(new PooledVector { Root = thrustObj, Rect = r, Line = img });
            }

            // 3. Combat Clash Hotspot Markers Pool
            for (int i = 0; i < 16; i++)
            {
                GameObject clashObj = new GameObject("ComClashMarker_" + i, typeof(RectTransform), typeof(Image));
                clashObj.transform.SetParent(vectorParent, false);

                RectTransform r = clashObj.GetComponent<RectTransform>();
                r.pivot = new Vector2(0.5f, 0.5f);
                r.anchorMin = new Vector2(0.5f, 0.5f);
                r.anchorMax = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(16f, 16f);

                Image img = clashObj.GetComponent<Image>();
                img.raycastTarget = false;

                clashObj.SetActive(false);
                clashPool.Add(new PooledMarker { Root = clashObj, Rect = r, Graphic = img });
            }

            // 4. Contested Node Rings Pool
            for (int i = 0; i < 16; i++)
            {
                GameObject ringObj = new GameObject("ComNodeRing_" + i, typeof(RectTransform), typeof(Image));
                ringObj.transform.SetParent(vectorParent, false);

                RectTransform r = ringObj.GetComponent<RectTransform>();
                r.pivot = new Vector2(0.5f, 0.5f);
                r.anchorMin = new Vector2(0.5f, 0.5f);
                r.anchorMax = new Vector2(0.5f, 0.5f);
                r.sizeDelta = new Vector2(28f, 28f);

                Image img = ringObj.GetComponent<Image>();
                img.raycastTarget = false;

                ringObj.SetActive(false);
                nodeRingPool.Add(new PooledMarker { Root = ringObj, Rect = r, Graphic = img });
            }

            initialized = true;
            logger?.LogInfo("[COM] Running With Rifles Tactical Grid Overlay initialized (" + sectorGrid.Resolution + "x" +
                sectorGrid.Resolution + " sectors, " + mapSize.x + "x" + mapSize.y + "m theater).");
        }

        private void UpdateSectorGrid()
        {
            if (dynamicMap == null) return;

            try
            {
                FactionHQ localHq = (compatibilityEngine != null)
                    ? compatibilityEngine.ResolvePlayerHq(dynamicMap)
                    : dynamicMap.HQ;
                if (localHq == null) return;

                Vector2 mapSize = (compatibilityEngine != null)
                    ? compatibilityEngine.ResolveTheaterDimensions(dynamicMap)
                    : GetLoadedMapSize(dynamicMap);

                sectorGrid.SetWorldSize(mapSize.x, mapSize.y);
                sectorGrid.Clear();

                int texW = BaseTextureWidth;
                int texH = (int)Math.Max(128, Math.Round(texW * (mapSize.y / mapSize.x)));
                EnsureTexture(texW, texH);

                // 1. Reconcile Mission Strategic Nodes (Airbases, Depots, Factories, Objectives)
                if (compatibilityEngine != null)
                {
                    compatibilityEngine.ReconcileMissionNodes(sectorGrid, localHq);
                }
                else
                {
                    // Fallback Airbase Scan
                    HashSet<int> seenAirbases = new HashSet<int>();
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
                                    if (ab == null || ab.UnitDestroyed() || !seenAirbases.Add(ab.GetInstanceID())) continue;
                                    Transform anchor = (ab.center != null) ? ab.center : ab.transform;
                                    Vector3 pos = anchor.GlobalPosition().AsVector3();
                                    SectorControl faction = SectorControl.Neutral;
                                    if (ab.CurrentHQ != null)
                                    {
                                        faction = (ab.CurrentHQ == localHq) ? SectorControl.Friendly : SectorControl.Hostile;
                                    }
                                    sectorGrid.RegisterNode(ab.GetInstanceID(), ab.name ?? "Airbase", pos.x, pos.z, faction, 0f, true);
                                }
                            }
                        }
                    }
                }

                // 2. Troops on the Ground, Combat Armor & SAM Sites
                List<Unit> allUnits = UnitRegistry.allUnits;
                if (allUnits != null)
                {
                    for (int i = 0; i < allUnits.Count; i++)
                    {
                        Unit u = allUnits[i];
                        if (u == null || u.disabled || u is Aircraft || u is Missile) continue;

                        bool isHostile = u.NetworkHQ != localHq;
                        // Fog of War: enemy units only exert sector presence if tracked by our sensors/HQ
                        if (isHostile && !localHq.IsTargetBeingTracked(u)) continue;

                        Vector3 pos = u.GlobalPosition().AsVector3();
                        float combatWeight = 1.0f;

                        if (u is Ship)
                        {
                            combatWeight = 4.0f;
                        }
                        else if (u is GroundVehicle)
                        {
                            combatWeight = 2.5f;
                        }
                        else if (u is PilotDismounted)
                        {
                            combatWeight = 0.8f;
                        }
                        else if (u is Building)
                        {
                            combatWeight = 2.5f;
                        }

                        sectorGrid.AddTroopPresence(pos.x, pos.z, combatWeight, isHostile);

                        // Threat envelope check: SAM sites with active radar
                        if (ShowThreatRings && isHostile && u.definition != null && u.definition.roleIdentity.antiAir > 0.5f)
                        {
                            sectorGrid.AddThreatBubble(pos.x, pos.z, 14000f, true);
                        }
                    }
                }

                // 3. Early Warning Radar Networks
                if (ShowThreatRings && GameAccess.HqSensorsAvailable)
                {
                    List<Radar> radars = GameAccess.GetHqRadars(localHq);
                    if (radars != null)
                    {
                        for (int i = 0; i < radars.Count; i++)
                        {
                            Radar r = radars[i];
                            if (r == null) continue;
                            Vector3 pos = r.transform.GlobalPosition().AsVector3();
                            float range = r.GetRadarRange();
                            sectorGrid.AddThreatBubble(pos.x, pos.z, range > 2000f ? range : 25000f, false);
                        }
                    }
                }

                // 4. Evaluate Sectors (Wavefront Growth + 66% Superiority Rule + Frontlines)
                sectorGrid.EvaluateSectors();
                command?.SyncSectorTelemetry(sectorGrid);

                // 5. Fast Procedural Texture Bake
                Color32[] pixels = sectorGrid.BakeTexture(
                    texW,
                    texH,
                    ShowSectors,
                    ShowFrontlines,
                    ShowThreatRings,
                    settings.OverlayOpacity.Value);

                overlayTexture.SetPixels32(pixels);
                overlayTexture.Apply(false);
            }
            catch (Exception ex)
            {
                logger?.LogWarning("[COM] Error updating tactical sector grid: " + ex.Message);
            }
        }

        private void UpdateTacticalVectors()
        {
            if (dynamicMap == null)
            {
                HideAllVectors();
                return;
            }

            FactionHQ localHq = dynamicMap.HQ;
            if (localHq == null) return;

            float mapFactor = dynamicMap.mapDisplayFactor;
            float inverseScale = 1f / Mathf.Max(0.01f, dynamicMap.mapImage.transform.localScale.x);

            // 1. AI Flight Sortie Vectors (CAP, Strike, CAS, RTB)
            if (ShowAiOrders)
            {
                int count = orderExtractor.ExtractActiveOrders(localHq, activeOrders);
                for (int i = 0; i < vectorPool.Count; i++)
                {
                    PooledVector vec = vectorPool[i];
                    if (i >= count)
                    {
                        vec.Root.SetActive(false);
                        continue;
                    }

                    AiTaskingOrder order = activeOrders[i];
                    Vector2 startMap = new Vector2(order.OriginWorld.x * mapFactor, order.OriginWorld.z * mapFactor);
                    Vector2 endMap = new Vector2(order.TargetWorld.x * mapFactor, order.TargetWorld.z * mapFactor);

                    Vector2 delta = endMap - startMap;
                    float distance = delta.magnitude;

                    if (distance < 3f)
                    {
                        vec.Root.SetActive(false);
                        continue;
                    }

                    float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                    float thickness = 2.0f * inverseScale;

                    vec.Rect.localPosition = new Vector3(startMap.x, startMap.y, 0f);
                    vec.Rect.sizeDelta = new Vector2(distance, thickness);
                    vec.Rect.localEulerAngles = new Vector3(0f, 0f, angle);
                    vec.Line.color = order.MissionColor;

                    vec.Root.SetActive(true);
                }
            }
            else
            {
                for (int i = 0; i < vectorPool.Count; i++)
                {
                    if (vectorPool[i].Root != null) vectorPool[i].Root.SetActive(false);
                }
            }

            // 2. Attack Thrust Arrows (From friendly forward nodes to contested enemy sectors)
            if (ShowAttackRoutes)
            {
                var thrusts = sectorGrid.GetAttackThrusts();
                for (int i = 0; i < thrustPool.Count; i++)
                {
                    PooledVector thrust = thrustPool[i];
                    if (i >= thrusts.Count)
                    {
                        thrust.Root.SetActive(false);
                        continue;
                    }

                    TacticalSectorGrid.AttackThrust t = thrusts[i];
                    Vector2 startMap = new Vector2(t.OriginX * mapFactor, t.OriginZ * mapFactor);
                    Vector2 endMap = new Vector2(t.TargetX * mapFactor, t.TargetZ * mapFactor);

                    Vector2 delta = endMap - startMap;
                    float distance = delta.magnitude;

                    if (distance < 5f)
                    {
                        thrust.Root.SetActive(false);
                        continue;
                    }

                    float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
                    float thickness = 3.5f * inverseScale;

                    thrust.Rect.localPosition = new Vector3(startMap.x, startMap.y, 0f);
                    thrust.Rect.sizeDelta = new Vector2(distance, thickness);
                    thrust.Rect.localEulerAngles = new Vector3(0f, 0f, angle);
                    thrust.Line.color = new Color(0.2f, 0.9f, 0.5f, 0.85f); // Tactical green attack arrow

                    thrust.Root.SetActive(true);
                }
            }
            else
            {
                for (int i = 0; i < thrustPool.Count; i++)
                {
                    if (thrustPool[i].Root != null) thrustPool[i].Root.SetActive(false);
                }
            }

            // 3. Combat Clash Hotspot Markers (Crossed / diamond contact bursts)
            if (ShowClashes)
            {
                var clashes = sectorGrid.GetClashes();
                for (int i = 0; i < clashPool.Count; i++)
                {
                    PooledMarker marker = clashPool[i];
                    if (i >= clashes.Count)
                    {
                        marker.Root.SetActive(false);
                        continue;
                    }

                    TacticalSectorGrid.TacticalClash clash = clashes[i];
                    Vector2 mapPos = new Vector2(clash.WorldX * mapFactor, clash.WorldZ * mapFactor);
                    float size = 16f * inverseScale;

                    marker.Rect.localPosition = new Vector3(mapPos.x, mapPos.y, 0f);
                    marker.Rect.sizeDelta = new Vector2(size, size);
                    marker.Rect.localEulerAngles = new Vector3(0f, 0f, 45f); // Rotated diamond

                    // Pulsing clash color
                    float pulse = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 5f);
                    marker.Graphic.color = new Color(1.0f, 0.35f, 0.1f, pulse);

                    marker.Root.SetActive(true);
                }
            }
            else
            {
                for (int i = 0; i < clashPool.Count; i++)
                {
                    if (clashPool[i].Root != null) clashPool[i].Root.SetActive(false);
                }
            }

            // 4. Contested Strategic Node Rings
            if (ShowNodes)
            {
                var nodes = sectorGrid.GetNodes();
                int ringIdx = 0;

                for (int i = 0; i < nodes.Count && ringIdx < nodeRingPool.Count; i++)
                {
                    TacticalSectorGrid.TacticalNode node = nodes[i];
                    if (!node.IsContested) continue;

                    PooledMarker ring = nodeRingPool[ringIdx++];
                    Vector2 mapPos = new Vector2(node.X * mapFactor, node.Z * mapFactor);
                    float size = 26f * inverseScale;

                    ring.Rect.localPosition = new Vector3(mapPos.x, mapPos.y, 0f);
                    ring.Rect.sizeDelta = new Vector2(size, size);
                    ring.Rect.localEulerAngles = Vector3.zero;

                    float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 4f);
                    ring.Graphic.color = new Color(1.0f, 0.8f, 0.2f, pulse);

                    ring.Root.SetActive(true);
                }

                for (int i = ringIdx; i < nodeRingPool.Count; i++)
                {
                    nodeRingPool[i].Root.SetActive(false);
                }
            }
            else
            {
                for (int i = 0; i < nodeRingPool.Count; i++)
                {
                    if (nodeRingPool[i].Root != null) nodeRingPool[i].Root.SetActive(false);
                }
            }
        }

        private void HideAllVectors()
        {
            for (int i = 0; i < vectorPool.Count; i++)
            {
                if (vectorPool[i].Root != null) vectorPool[i].Root.SetActive(false);
            }
            for (int i = 0; i < thrustPool.Count; i++)
            {
                if (thrustPool[i].Root != null) thrustPool[i].Root.SetActive(false);
            }
            for (int i = 0; i < clashPool.Count; i++)
            {
                if (clashPool[i].Root != null) clashPool[i].Root.SetActive(false);
            }
            for (int i = 0; i < nodeRingPool.Count; i++)
            {
                if (nodeRingPool[i].Root != null) nodeRingPool[i].Root.SetActive(false);
            }
        }
    }
}
