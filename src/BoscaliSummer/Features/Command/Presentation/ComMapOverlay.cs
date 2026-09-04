using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Command.Configuration;
using BoscaliSummer.Features.Command.Domain;
using BoscaliSummer.Features.Command.Patches;
using BoscaliSummer.Features.Command.Runtime;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using NOAvionics;
using NOAvionics.Ui;

namespace BoscaliSummer.Features.Command.Presentation
{
    internal sealed class ComMapOverlay : MonoBehaviour, ISceneService
    {
        private CommandSettings settings;
        private CommandManager command;
        private ManualLogSource logger;

        private DynamicMap dynamicMap;
        private RawImage overlayImage;
        private Texture2D overlayTexture;
        private InfluenceGridCalculator gridCalc;
        private AiOrderExtractor orderExtractor;

        // Pooled Vector Objects
        private sealed class PooledVector
        {
            public GameObject Root;
            public RectTransform Rect;
            public Image Line;
            public TMP_Text Label;
        }

        private readonly List<PooledVector> vectorPool = new List<PooledVector>(48);
        private readonly List<AiTaskingOrder> activeOrders = new List<AiTaskingOrder>(48);
        private readonly List<InfluenceGridCalculator.InfluenceSource> influenceSources =
            new List<InfluenceGridCalculator.InfluenceSource>(64);
        private readonly List<InfluenceGridCalculator.RadarSource> radarSources =
            new List<InfluenceGridCalculator.RadarSource>(32);

        private float nextGridUpdate;
        private float nextVectorUpdate;
        private bool isMapMaximized;
        private bool initialized;
        private Coroutine gridComputeCoroutine;
        private bool isComputingGrid;

        public bool ShowFrontlines = true;
        public bool ShowRadar = true;
        public bool ShowRecon = true;
        public bool ShowAiOrders = true;

        public void Configure(CommandSettings config, CommandManager manager, ManualLogSource log)
        {
            settings = config;
            command = manager;
            logger = log;

            ShowFrontlines = settings.FrontlinesOverlay.Value;
            ShowRadar = settings.RadarCoverageOverlay.Value;
            ShowRecon = settings.VisibilityOverlay.Value;
            ShowAiOrders = settings.AiOrdersOverlay.Value;

            gridCalc = new InfluenceGridCalculator(settings.GridResolution.Value, 81920f);
            orderExtractor = new AiOrderExtractor(settings.MaxOrderVectors.Value);

            DynamicMapMaximizePatch.OnMaximized += HandleMapMaximized;
            DynamicMapMinimizePatch.OnMinimized += HandleMapMinimized;
        }

        public void ResetForScene()
        {
            if (gridComputeCoroutine != null)
            {
                StopCoroutine(gridComputeCoroutine);
                gridComputeCoroutine = null;
            }
            isComputingGrid = false;

            if (overlayImage != null && overlayImage.gameObject != null)
            {
                UnityEngine.Object.Destroy(overlayImage.gameObject);
            }
            overlayImage = null;

            if (overlayTexture != null)
            {
                UnityEngine.Object.Destroy(overlayTexture);
            }
            overlayTexture = null;

            for (int i = 0; i < vectorPool.Count; i++)
            {
                if (vectorPool[i].Root != null) UnityEngine.Object.Destroy(vectorPool[i].Root);
            }
            vectorPool.Clear();

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
            if (overlayImage != null) overlayImage.enabled = true;
        }

        private void HandleMapMinimized()
        {
            if (gridComputeCoroutine != null)
            {
                StopCoroutine(gridComputeCoroutine);
                gridComputeCoroutine = null;
            }
            isComputingGrid = false;

            isMapMaximized = false;
            if (overlayImage != null) overlayImage.enabled = false;
            HideAllVectors();
        }

        private void Update()
        {
            if (!settings.Enabled.Value) return;

            if (!initialized)
            {
                TryInitialize();
                return;
            }

            if (dynamicMap == null) return;
            isMapMaximized = DynamicMap.mapMaximized;

            // Performance-first: ZERO CPU work while the map is minimized during flight!
            if (!isMapMaximized) return;

            float now = Time.unscaledTime;

            if (now >= nextGridUpdate && !isComputingGrid)
            {
                nextGridUpdate = now + settings.GridRefreshInterval.Value;
                gridComputeCoroutine = StartCoroutine(SpreadInfluenceGridComputation());
            }

            if (now >= nextVectorUpdate)
            {
                nextVectorUpdate = now + settings.VectorRefreshInterval.Value;
                UpdateOrderVectors();
            }
        }

        private void TryInitialize()
        {
            dynamicMap = SceneSingleton<DynamicMap>.i;
            if (dynamicMap == null || dynamicMap.mapImage == null) return;

            RectTransform mapImageRect = dynamicMap.mapImage.GetComponent<RectTransform>();
            if (mapImageRect == null) return;

            int res = settings.GridResolution.Value;
            overlayTexture = new Texture2D(res, res, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            GameObject overlayObj = new GameObject("ComMapOverlay", typeof(RectTransform), typeof(RawImage));
            overlayObj.transform.SetParent(dynamicMap.mapImage.transform, false);

            RectTransform overlayRect = overlayObj.GetComponent<RectTransform>();
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            overlayRect.pivot = mapImageRect.pivot;

            overlayImage = overlayObj.GetComponent<RawImage>();
            overlayImage.texture = overlayTexture;
            overlayImage.raycastTarget = false;
            overlayImage.enabled = DynamicMap.mapMaximized;

            // Ensure overlay draws above map background but below iconLayer
            overlayObj.transform.SetAsFirstSibling();

            // Initialize Vector Pool under iconLayer
            Transform vectorParent = dynamicMap.iconLayer != null
                ? dynamicMap.iconLayer.transform
                : dynamicMap.mapTransform;

            int poolSize = settings.MaxOrderVectors.Value;
            for (int i = 0; i < poolSize; i++)
            {
                GameObject vecObj = new GameObject("ComOrderVec_" + i, typeof(RectTransform), typeof(Image));
                vecObj.transform.SetParent(vectorParent, false);

                RectTransform r = vecObj.GetComponent<RectTransform>();
                r.pivot = new Vector2(0f, 0.5f);
                r.anchorMin = new Vector2(0.5f, 0.5f);
                r.anchorMax = new Vector2(0.5f, 0.5f);

                Image img = vecObj.GetComponent<Image>();
                img.raycastTarget = false;

                GameObject labelObj = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                labelObj.transform.SetParent(vecObj.transform, false);
                TMP_Text lbl = labelObj.GetComponent<TextMeshProUGUI>();
                lbl.fontSize = AvTokens.FontMicro;
                lbl.color = AvTheme.TextPrimary;
                if (AvFont.Font != null) lbl.font = AvFont.Font;
                lbl.alignment = TextAlignmentOptions.MidlineLeft;
                lbl.raycastTarget = false;
                lbl.rectTransform.sizeDelta = new Vector2(160f, 20f);
                lbl.rectTransform.localPosition = new Vector3(8f, 12f, 0f);

                vecObj.SetActive(false);
                vectorPool.Add(new PooledVector { Root = vecObj, Rect = r, Line = img, Label = lbl });
            }

            initialized = true;
            logger?.LogInfo("[COM] Tactical Map Overlay initialized (" + res + "x" + res + " grid, " +
                poolSize + " vector pool).");
        }

        private System.Collections.IEnumerator SpreadInfluenceGridComputation()
        {
            if (dynamicMap == null || overlayTexture == null) yield break;
            FactionHQ localHq = dynamicMap.HQ;
            if (localHq == null) yield break;

            isComputingGrid = true;
            gridCalc.Clear();
            influenceSources.Clear();
            radarSources.Clear();

            // 1. Airbases
            IEnumerable<Airbase> airbases = localHq.GetAirbases();
            if (airbases != null)
            {
                foreach (Airbase ab in airbases)
                {
                    if (ab == null || ab.UnitDestroyed()) continue;
                    Vector3 pos = ab.transform.position;
                    bool hostile = ab.CurrentHQ != localHq;
                    influenceSources.Add(new InfluenceGridCalculator.InfluenceSource(
                        pos.x, pos.z, 14000f, 2.5f, hostile));
                }
            }

            // Yield to next frame to keep frame rate silky smooth
            yield return null;
            if (!isMapMaximized || dynamicMap == null) { isComputingGrid = false; yield break; }

            // 2. Active Units & Garrisons (aircraft + ground combat vehicles)
            IReadOnlyList<Aircraft> allAircraft = UnitRegistry.allAircraft;
            if (allAircraft != null)
            {
                for (int i = 0; i < allAircraft.Count; i++)
                {
                    Aircraft ac = allAircraft[i];
                    if (ac == null || ac.disabled) continue;
                    Vector3 pos = ac.transform.position;
                    bool hostile = ac.NetworkHQ != localHq;
                    if (hostile && !localHq.IsTargetBeingTracked(ac)) continue;

                    influenceSources.Add(new InfluenceGridCalculator.InfluenceSource(
                        pos.x, pos.z, 6000f, 0.8f, hostile));
                }
            }

            List<Unit> allUnits = UnitRegistry.allUnits;
            if (allUnits != null)
            {
                for (int i = 0; i < allUnits.Count; i++)
                {
                    Unit u = allUnits[i];
                    if (u == null || u.disabled || u is Aircraft || u is Missile) continue;
                    if (u is GroundVehicle || u is Ship)
                    {
                        Vector3 pos = u.transform.position;
                        bool hostile = u.NetworkHQ != localHq;
                        if (hostile && !localHq.IsTargetBeingTracked(u)) continue;

                        influenceSources.Add(new InfluenceGridCalculator.InfluenceSource(
                            pos.x, pos.z, 5000f, 1.2f, hostile));
                    }
                }
            }

            gridCalc.AddInfluence(influenceSources);

            // Yield to next frame
            yield return null;
            if (!isMapMaximized || dynamicMap == null) { isComputingGrid = false; yield break; }

            // 3. Radars & SAMs
            if (GameAccess.HqSensorsAvailable)
            {
                List<Radar> radars = GameAccess.GetHqRadars(localHq);
                if (radars != null)
                {
                    for (int i = 0; i < radars.Count; i++)
                    {
                        Radar r = radars[i];
                        if (r == null) continue;
                        Vector3 pos = r.transform.position;
                        float range = r.GetRadarRange();
                        radarSources.Add(new InfluenceGridCalculator.RadarSource(
                            pos.x, pos.z, range > 1000f ? range : 25000f, false, false));
                    }
                }
            }

            gridCalc.AddRadars(radarSources);

            // Yield to next frame
            yield return null;
            if (!isMapMaximized || dynamicMap == null || overlayTexture == null) { isComputingGrid = false; yield break; }

            // 4. Bake Texture & Apply
            Color32[] pixels = gridCalc.BakeTexture(
                ShowFrontlines, ShowRadar, ShowRecon, settings.OverlayOpacity.Value);
            overlayTexture.SetPixels32(pixels);
            overlayTexture.Apply(false);

            isComputingGrid = false;
        }

        private void UpdateOrderVectors()
        {
            if (!ShowAiOrders || dynamicMap == null)
            {
                HideAllVectors();
                return;
            }

            FactionHQ localHq = dynamicMap.HQ;
            if (localHq == null) return;

            int count = orderExtractor.ExtractActiveOrders(localHq, activeOrders);
            float mapFactor = dynamicMap.mapDisplayFactor;

            for (int i = 0; i < vectorPool.Count; i++)
            {
                PooledVector vec = vectorPool[i];
                if (i >= count)
                {
                    vec.Root.SetActive(false);
                    continue;
                }

                AiTaskingOrder order = activeOrders[i];

                // Convert world coords to map local coords
                Vector2 startMap = new Vector2(order.OriginWorld.x * mapFactor, order.OriginWorld.z * mapFactor);
                Vector2 endMap = new Vector2(order.TargetWorld.x * mapFactor, order.TargetWorld.z * mapFactor);

                Vector2 delta = endMap - startMap;
                float distance = delta.magnitude;

                if (distance < 2f)
                {
                    vec.Root.SetActive(false);
                    continue;
                }

                float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;

                vec.Rect.localPosition = new Vector3(startMap.x, startMap.y, 0f);
                vec.Rect.sizeDelta = new Vector2(distance, 2.5f);
                vec.Rect.localEulerAngles = new Vector3(0f, 0f, angle);

                vec.Line.color = order.MissionColor;
                vec.Label.text = order.Callsign + " > " + order.TargetName;
                vec.Label.color = order.MissionColor.WithAlpha(0.9f);
                vec.Label.rectTransform.localEulerAngles = new Vector3(0f, 0f, -angle); // Keep text horizontal

                vec.Root.SetActive(true);
            }
        }

        private void HideAllVectors()
        {
            for (int i = 0; i < vectorPool.Count; i++)
            {
                if (vectorPool[i].Root != null) vectorPool[i].Root.SetActive(false);
            }
        }
    }
}
