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
        private MissionMapCompatibilityEngine compatibilityEngine;

        private float nextGridUpdate;
        private bool isMapMaximized;
        private bool initialized;

        public bool ShowSectors = true;
        public bool ShowFrontlines = true;

        public void Configure(CommandSettings config, CommandManager manager, MissionMapCompatibilityEngine compat, ManualLogSource log)
        {
            settings = config;
            command = manager;
            compatibilityEngine = compat;
            logger = log;

            ShowSectors = settings.FrontlinesOverlay.Value;
            ShowFrontlines = settings.FrontlinesOverlay.Value;

            int res = settings.GridResolution.Value;
            sectorGrid = new TacticalSectorGrid(res > 0 ? res : TacticalSectorGrid.DefaultResolution, 100000f);

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

            if (sectorGrid != null)
            {
                sectorGrid.ResetAll();
            }

            dynamicMap = null;
            initialized = false;
            isMapMaximized = false;
            nextGridUpdate = 0f;
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
            if (overlayObj != null) overlayObj.SetActive(true);
        }

        private void HandleMapMinimized()
        {
            isMapMaximized = false;
            if (overlayObj != null) overlayObj.SetActive(false);
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
                    }
                }

                // 3. Evaluate Sectors (Wavefront Growth + 66% Superiority Rule + Frontlines)
                sectorGrid.EvaluateSectors();
                command?.SyncSectorTelemetry(sectorGrid);

                // 4. Fast Procedural Texture Bake
                Color32[] pixels = sectorGrid.BakeTexture(
                    texW,
                    texH,
                    ShowSectors,
                    ShowFrontlines,
                    settings.OverlayOpacity.Value);

                overlayTexture.SetPixels32(pixels);
                overlayTexture.Apply(false);
            }
            catch (Exception ex)
            {
                logger?.LogWarning("[COM] Error updating tactical sector grid: " + ex.Message);
            }
        }
    }
}
