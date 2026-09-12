using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Patches;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Lifecycle;
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
        private TerritoryControlView territory;


        private float nextGridUpdate;
        private FactionHQ gridHq;
        private bool isMapMaximized;
        private bool initialized;

        public bool ShowSectors = true;
        public bool ShowFrontlines = true;

        /// <summary>
        /// The faction control field shared with host ingress queries.
        /// </summary>
        public TacticalSectorGrid Grid => sectorGrid;

        public void Configure(CommandSettings config, CommandManager manager, MissionMapCompatibilityEngine compat, ManualLogSource log, TerritoryControlView control)
        {
            settings = config;
            command = manager;
            compatibilityEngine = compat;
            territory = control;
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
            gridHq = null;
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
            if (settings == null || !settings.Enabled.Value)
            {
                if (overlayObj != null) overlayObj.SetActive(false);
                return;
            }

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
                nextGridUpdate = now + Math.Max(0.5f, settings.GridRefreshInterval.Value);
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

            GetTextureSize(out int texW, out int texH);
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
            logger?.LogInfo("[COM] Dynamic frontline overlay initialized (" + sectorGrid.ResolutionX + "x" +
                sectorGrid.ResolutionY + " sectors, " + mapSize.x + "x" + mapSize.y + "m theater).");
        }

        private void GetTextureSize(out int width, out int height)
        {
            float longest = sectorGrid.WorldSize;
            width = Math.Clamp((int)Math.Round(BaseTextureWidth * sectorGrid.WorldSizeX / longest), 1, BaseTextureWidth);
            height = Math.Clamp((int)Math.Round(BaseTextureWidth * sectorGrid.WorldSizeY / longest), 1, BaseTextureWidth);
        }

        private void UpdateSectorGrid()
        {
            if (dynamicMap == null) return;

            try
            {
                FactionHQ localHq = (compatibilityEngine != null)
                    ? compatibilityEngine.ResolvePlayerHq(dynamicMap)
                    : dynamicMap.HQ;
                if (localHq != gridHq)
                {
                    gridHq = localHq;
                }
                if (overlayImage != null) overlayImage.enabled = localHq != null;
                if (localHq == null)
                {
                    command?.SyncSectorTelemetry(sectorGrid);
                    return;
                }

                TacticalSectorGrid current = territory.Read(localHq);
                if (current == null) return;
                sectorGrid = current;
                GetTextureSize(out int texW, out int texH);
                EnsureTexture(texW, texH);
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
