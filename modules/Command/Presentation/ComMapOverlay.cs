using System;
using System.Threading.Tasks;
using BoscaliSummer.Modules.Command.Configuration;
using BoscaliSummer.Modules.Command.Patches;
using BoscaliSummer.Modules.Command.Presentation.MapUi;
using BoscaliSummer.Modules.Command.Runtime;
using BoscaliSummer.Core.Lifecycle;
using BoscaliSummer.Core.Game;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Command.Presentation
{
    internal sealed class ComMapOverlay : MonoBehaviour, ISceneService
    {
        private const int BaseTextureWidth = TacticalSectorGrid.MaximumTextureSize;

        private CommandSettings settings;

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

        private const float BakeMinInterval = 3f;
        private float lastBakeTime = -999f;
        private ulong lastBakeDisplayHash;
        private bool lastBakeShowSectors;
        private float lastBakeOverlayAlpha = -1f;
        private int lastBakeTexW;
        private int lastBakeTexH;
        private bool hasBakedTexture;

        // The raster bake is plain array work, so it runs on a worker over this private copy of
        // the published grid (which the main thread keeps changing). The lastBake* fields above
        // describe the bake that was last requested, whether or not it has landed yet.
        private sealed class BakeJob
        {
            internal Task Task;
            internal Color32[] Pixels;
            internal int Width;
            internal int Height;
            internal Exception Error;
            internal bool Discard;
        }

        private TacticalSectorGrid bakeGrid;
        private BakeJob pendingBake;
        private bool bakeFaultLogged;
        private bool textureClear;

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

        public bool TerrainImageVisible => settings == null || settings.MapTerrainImage.Value;

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

        public void SetTerrainImageVisible(bool visible)
        {
            if (settings == null || settings.MapTerrainImage.Value == visible) return;
            settings.MapTerrainImage.Value = visible;
            MfdMapDeck.ApplyAppearance(settings);
        }

        public void Configure(CommandSettings config, MissionMapCompatibilityEngine compat, TerritoryControlView control)
        {
            settings = config;
            compatibilityEngine = compat;
            territory = control;

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

            // A territory view may share this grid with another reader. Drop our reference
            // instead of clearing a field owned by that service.
            sectorGrid = FreshGrid();

            // A bake still running keeps its own copy and pixels; nobody will read the result.
            if (pendingBake != null) pendingBake.Discard = true;
            pendingBake = null;
            bakeGrid = null;
            bakeFaultLogged = false;
            textureClear = false;

            dynamicMap = null;
            initialized = false;
            isMapMaximized = false;
            nextGridUpdate = 0f;
            gridHq = null;
            hasBakedTexture = false;
            lastBakeDisplayHash = 0UL;
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
            lastBakeTime = -999f;
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
            PollBake();

            if (settings == null || !settings.Enabled.Value)
            {
                SetOverlayVisible(false);
                return;
            }

            if (dynamicMap == null) dynamicMap = SceneSingleton<DynamicMap>.i;
            if (dynamicMap == null) return;
            isMapMaximized = DynamicMap.mapMaximized;

            // Build the raster and vector map surfaces only when the large map opens.
            // The sector counts the board and the ticker read come from CommandManager's census, so nothing here needs to run while the map is closed.
            if (isMapMaximized && !initialized) TryInitialize();

            // Strict visibility control: Overlay is strictly for the Maximized Theater Map!
            // When minimized (cockpit flight HUD), overlay MUST stay disabled so it never pollutes the cockpit MFD.
            if (overlayObj != null && overlayObj.activeSelf != isMapMaximized)
            {
                SetOverlayVisible(isMapMaximized);
            }

            // Ensure overlay stays parented to mapImage with stretch anchors
            if (isMapMaximized && overlayObj != null && dynamicMap.mapImage != null && overlayObj.transform.parent != dynamicMap.mapImage.transform)
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
                UpdateSectorGrid(isMapMaximized && initialized);
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
            textureClear = false;
            ClearTexture(texW, texH);

            if (overlayImage != null)
            {
                overlayImage.texture = overlayTexture;
            }
        }

        private void ClearTexture(int texW, int texH)
        {
            if (textureClear) return;
            Color32[] transparentPixels = new Color32[texW * texH];
            overlayTexture.SetPixels32(transparentPixels);
            overlayTexture.Apply(false);
            textureClear = true;
        }

        /// <summary>
        /// Uploads a finished bake. Runs on the main thread from Update; never waits for a bake
        /// that is still running.
        /// </summary>
        private void PollBake()
        {
            BakeJob job = pendingBake;
            if (job == null || !job.Task.IsCompleted) return;
            pendingBake = null;
            // The front may have moved while the worker ran: look again at once.
            nextGridUpdate = 0f;
            if (job.Task.IsFaulted && job.Error == null) job.Error = job.Task.Exception?.GetBaseException();
            if (job.Error != null)
            {
                if (!bakeFaultLogged)
                {
                    bakeFaultLogged = true;
                    Plugin.Logger?.LogWarning("[COM] Tactical overlay bake failed: " + job.Error.Message);
                }
                return;
            }
            if (job.Discard || job.Pixels == null) return;
            if (overlayTexture == null || overlayTexture.width != job.Width || overlayTexture.height != job.Height)
            {
                // The texture was replaced while the worker ran: this picture no longer fits.
                hasBakedTexture = false;
                return;
            }
            overlayTexture.SetPixels32(job.Pixels);
            overlayTexture.Apply(false);
            textureClear = false;
        }

        /// <summary>Snapshots the shown grid into the private bake grid and bakes it on a worker.</summary>
        private void StartBake(int texW, int texH, float overlayAlpha)
        {
            if (bakeGrid == null || !bakeGrid.SharesBakeLayout(sectorGrid)) bakeGrid = sectorGrid.NewBakeTwin();
            bakeGrid.CopyBakeStateFrom(sectorGrid);
            TacticalSectorGrid grid = bakeGrid;
            var job = new BakeJob { Width = texW, Height = texH };
            job.Task = Task.Run(() =>
            {
                try { job.Pixels = grid.BakeTexture(texW, texH, true, overlayAlpha); }
                catch (Exception ex) { job.Error = ex; }
            });
            pendingBake = job;
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
            overlayImage.enabled = false;
            overlayImage.raycastTarget = false;
            EnsureFrontlineGraphic(mapImageRect);
            SetOverlayVisible(DynamicMap.mapMaximized);

            // Draw the tint directly above the terrain image; the front line goes last so it
            // reads over the trench trace that the Trenches overlay draws between the two.
            overlayObj.transform.SetAsFirstSibling();

            initialized = true;
            nextGridUpdate = 0f;
            Plugin.Logger?.LogInfo("[COM] Dynamic frontline overlay initialized (" + sectorGrid.ResolutionX + "x" +
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

        private void UpdateSectorGrid(bool paintMap = true)
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
                    return;
                }

                TacticalSectorGrid current = territory.Read(localHq);
                if (current == null)
                {
                    if (frontlineGraphic != null) frontlineGraphic.SetSource(null);
                    return;
                }
                sectorGrid = current;
                if (!paintMap) return;
                GetTextureSize(out int texW, out int texH);
                EnsureTexture(texW, texH);
                bool showSectors = ControlFieldVisible;
                bool showFrontlines = FrontLineVisible;
                float overlayAlpha = settings != null ? settings.OverlayOpacity.Value : 0.35f;

                if (overlayImage != null)
                {
                    overlayImage.enabled = localHq != null && showSectors;
                }

                // 4. Fast Procedural Texture Bake (dirty-checked)
                // A moving front changes the hash every refresh; hash-only re-bakes wait out BakeMinInterval (the change stays pending).
                bool bakeDirty = !hasBakedTexture
                    || (lastBakeDisplayHash != sectorGrid.DisplayHash && Time.unscaledTime - lastBakeTime >= BakeMinInterval)
                    || lastBakeShowSectors != showSectors
                    || !Mathf.Approximately(lastBakeOverlayAlpha, overlayAlpha)
                    || lastBakeTexW != texW
                    || lastBakeTexH != texH;

                // Nothing to draw (hidden, or fully transparent): clearing is cheap, so it stays here,
                // and any bake still running holds a picture that must not land over it.
                bool clearOnly = !showSectors || !float.IsFinite(overlayAlpha) || overlayAlpha <= 0f;
                bool requested = false;
                if (bakeDirty && clearOnly)
                {
                    if (pendingBake != null) pendingBake.Discard = true;
                    ClearTexture(texW, texH);
                    requested = true;
                }
                // One bake at a time: a request that arrives while the worker is busy simply stays
                // dirty and is asked again once the running bake has landed (newest state wins).
                else if (bakeDirty && pendingBake == null)
                {
                    StartBake(texW, texH, overlayAlpha);
                    requested = true;
                }

                if (requested)
                {
                    lastBakeDisplayHash = sectorGrid.DisplayHash;
                    lastBakeShowSectors = showSectors;
                    lastBakeOverlayAlpha = overlayAlpha;
                    lastBakeTexW = texW;
                    lastBakeTexH = texH;
                    hasBakedTexture = true;
                    lastBakeTime = Time.unscaledTime;
                }

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
                Plugin.Logger?.LogWarning("[COM] Error updating tactical sector grid: " + ex.Message);
            }
        }

        private TacticalSectorGrid FreshGrid()
        {
            float cell = settings != null ? settings.GridCellSizeMetres.Value : 0f;
            return new TacticalSectorGrid(cell > 0f ? cell : TacticalSectorGrid.DefaultCellSize, 100000f);
        }
    }
}
