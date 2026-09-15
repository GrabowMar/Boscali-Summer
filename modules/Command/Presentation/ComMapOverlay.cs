using System;
using BepInEx.Logging;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Patches;
using BoscaliSummer.Features.Command.Presentation.MapUi;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Features;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Command.Presentation
{
    internal sealed class ComMapOverlay : MonoBehaviour, ISceneService
    {
        private const int BaseTextureWidth = 1024;

        private CommandSettings settings;
        private CommandManager command;
        private ManualLogSource logger;

        private DynamicMap dynamicMap;
        private GameObject overlayObj;
        private RawImage overlayImage;
        private FrontlineGraphic frontlineGraphic;
        private Texture2D overlayTexture;
        private TacticalSectorGrid sectorGrid;
        private MissionMapCompatibilityEngine compatibilityEngine;
        private TerritoryControlView territory;

        private float nextGridUpdate;
        private FactionHQ gridHq;
        private bool isMapMaximized;
        private bool initialized;

        /// <summary>
        /// The faction control field shared with host ingress queries.
        /// </summary>
        public TacticalSectorGrid Grid => sectorGrid;

        // ------------------------------------------------------------ map layers
        // The MAP bezel switches these two layers for the session; the config entries stay
        // the authority for their starting state and for the SET screen.

        /// <summary>True once the map is on screen and the overlay can be drawn at all.</summary>
        public bool LayersAvailable => initialized && dynamicMap != null;

        /// <summary>Whether the last pass resolved a faction headquarters to derive control from.</summary>
        public bool HasControlData => gridHq != null;

        /// <summary>The cell size actually in use, after any theater-driven coarsening.</summary>
        public float CellMetres => sectorGrid != null ? sectorGrid.CellSize : 0f;

        public bool ControlFieldVisible => settings != null && settings.FrontlinesOverlay.Value;

        public bool FrontLineVisible => ControlFieldVisible && settings != null && settings.FrontlineTrace.Value;

        public void SetControlFieldVisible(bool visible)
        {
            if (settings == null || settings.FrontlinesOverlay.Value == visible) return;
            settings.FrontlinesOverlay.Value = visible;
            SyncSettings();
        }

        public void SetFrontLineVisible(bool visible)
        {
            if (settings == null || settings.FrontlineTrace.Value == visible) return;
            settings.FrontlineTrace.Value = visible;
            SyncSettings();
        }

        public void Configure(CommandSettings config, CommandManager manager, MissionMapCompatibilityEngine compat, ManualLogSource log, TerritoryControlView control)
        {
            settings = config;
            command = manager;
            compatibilityEngine = compat;
            territory = control;
            logger = log;

            float cell = settings.GridCellSizeMetres.Value;
            sectorGrid = new TacticalSectorGrid(cell > 0f ? cell : TacticalSectorGrid.DefaultCellSize, 100000f);

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
            if (frontlineGraphic != null)
            {
                Destroy(frontlineGraphic.gameObject);
                frontlineGraphic = null;
            }

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
            TheaterFrame.Invalidate();
        }

        public void SyncSettings()
        {
            nextGridUpdate = 0f;
            if (initialized && isMapMaximized)
            {
                UpdateSectorGrid();
            }
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
            SetOverlayVisible(true);
        }

        private void HandleMapMinimized()
        {
            isMapMaximized = false;
            SetOverlayVisible(false);
        }

        private void SetOverlayVisible(bool visible)
        {
            if (overlayObj != null) overlayObj.SetActive(visible);
            if (frontlineGraphic != null) frontlineGraphic.gameObject.SetActive(visible);
        }

        private void Update()
        {
            if (settings == null || !settings.Enabled.Value)
            {
                SetOverlayVisible(false);
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
                SetOverlayVisible(isMapMaximized);
            }

            if (!isMapMaximized) return;

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
                nextGridUpdate = now + Math.Max(0.2f, settings.GridRefreshInterval.Value);
                UpdateSectorGrid();
            }
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
                : TheaterFrame.Resolve(dynamicMap);
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
            EnsureFrontlineGraphic(mapImageRect);
            SetOverlayVisible(DynamicMap.mapMaximized);

            // Draw the tint directly above the terrain image; the front line goes last so it
            // reads over the trench trace that the Trenches overlay draws between the two.
            overlayObj.transform.SetAsFirstSibling();

            initialized = true;
            logger?.LogInfo("[COM] Dynamic frontline overlay initialized (" + sectorGrid.ResolutionX + "x" +
                sectorGrid.ResolutionY + " sectors of " + sectorGrid.CellSize + "m, " +
                mapSize.x + "x" + mapSize.y + "m theater).");
        }

        private void EnsureFrontlineGraphic(RectTransform mapImageRect)
        {
            if (frontlineGraphic != null || mapImageRect == null || dynamicMap == null || dynamicMap.mapImage == null)
                return;

            var go = new GameObject("ComFrontlineLine", typeof(RectTransform), typeof(FrontlineGraphic));
            go.transform.SetParent(dynamicMap.mapImage.transform, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = mapImageRect.pivot;
            rect.localScale = Vector3.one;
            rect.localPosition = Vector3.zero;

            frontlineGraphic = go.GetComponent<FrontlineGraphic>();
            frontlineGraphic.raycastTarget = false;
            go.SetActive(false);
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
                    if (frontlineGraphic != null) frontlineGraphic.SetSource(null);
                    command?.SyncSectorTelemetry(sectorGrid);
                    return;
                }

                TacticalSectorGrid current = territory.Read(localHq);
                if (current == null)
                {
                    if (frontlineGraphic != null) frontlineGraphic.SetSource(null);
                    return;
                }
                sectorGrid = current;
                GetTextureSize(out int texW, out int texH);
                EnsureTexture(texW, texH);
                command?.SyncSectorTelemetry(sectorGrid);
                bool showSectors = ControlFieldVisible;
                bool showFrontlines = FrontLineVisible;
                float overlayAlpha = settings != null ? settings.OverlayOpacity.Value : 0.35f;

                if (overlayImage != null)
                {
                    overlayImage.enabled = localHq != null && showSectors;
                }

                // 4. Fast Procedural Texture Bake
                Color32[] pixels = sectorGrid.BakeTexture(
                    texW,
                    texH,
                    showSectors,
                    overlayAlpha);

                overlayTexture.SetPixels32(pixels);
                overlayTexture.Apply(false);

                if (frontlineGraphic != null)
                {
                    // The line is the front: it must sit above the trench trace, which is
                    // initialised last in its own scene pass.
                    Transform parent = frontlineGraphic.transform.parent;
                    if (parent != null && frontlineGraphic.transform.GetSiblingIndex() != parent.childCount - 1)
                        frontlineGraphic.transform.SetAsLastSibling();
                    frontlineGraphic.SetSource(showFrontlines ? sectorGrid : null);
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning("[COM] Error updating tactical sector grid: " + ex.Message);
            }
        }
    }
}
