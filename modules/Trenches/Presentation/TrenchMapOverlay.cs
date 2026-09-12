using System;
using System.Collections.Generic;
using BepInEx.Logging;
using BoscaliSummer.Features.Trenches.Configuration;
using BoscaliSummer.Features.Trenches.Runtime;
using BoscaliSummer.Framework.Lifecycle;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Trenches.Presentation
{
    /// <summary>
    /// Renders dynamic NATO-standard entrenchment symbology onto the tactical theater map (DynamicMap).
    /// Features continuous trench traces, perpendicular crenellated teeth facing threat directions,
    /// and fortified strongpoint markers.
    /// </summary>
    internal sealed class TrenchMapOverlay : MonoBehaviour, ISceneService
    {
        private const int BaseTextureSize = 1024;

        private TrenchesSettings settings;
        private TrenchManager trenchManager;
        private ManualLogSource logger;

        private DynamicMap dynamicMap;
        private GameObject overlayObj;
        private RawImage overlayImage;
        private Texture2D overlayTexture;

        private bool initialized;
        private bool isMapMaximized;
        private Vector2 theaterDimensions = new Vector2(81920f, 81920f);

        public void Configure(TrenchesSettings config, TrenchManager manager, ManualLogSource log)
        {
            settings = config;
            trenchManager = manager;
            logger = log;

            if (trenchManager != null)
            {
                trenchManager.OnNetworksChanged += HandleNetworksChanged;
            }
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

            dynamicMap = null;
            initialized = false;
            isMapMaximized = false;
        }

        private void OnDestroy()
        {
            if (trenchManager != null)
            {
                trenchManager.OnNetworksChanged -= HandleNetworksChanged;
            }
            ResetForScene();
        }

        private void HandleNetworksChanged()
        {
            if (isMapMaximized)
            {
                BakeTrenchMapTexture();
            }
        }

        private void Update()
        {
            if (settings == null || !settings.Enabled.Value || !settings.ShowOnTacticalMap.Value)
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

            bool currentMaximized = DynamicMap.mapMaximized;
            if (overlayObj != null && overlayObj.activeSelf != currentMaximized)
            {
                overlayObj.SetActive(currentMaximized);
                if (currentMaximized)
                {
                    BakeTrenchMapTexture();
                }
            }

            isMapMaximized = currentMaximized;
        }

        private void TryInitialize()
        {
            dynamicMap = SceneSingleton<DynamicMap>.i;
            if (dynamicMap == null || dynamicMap.mapImage == null) return;

            RectTransform mapImageRect = dynamicMap.mapImage.GetComponent<RectTransform>();
            if (mapImageRect == null) return;

            ResolveTheaterDimensions();

            EnsureTexture(BaseTextureSize, BaseTextureSize);

            overlayObj = new GameObject("TrenchMapOverlay", typeof(RectTransform), typeof(RawImage));
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

            // Draw above the terrain image
            overlayObj.transform.SetAsLastSibling();

            initialized = true;
            BakeTrenchMapTexture();
            logger?.LogInfo("[TRENCHES] Tactical map overlay initialized.");
        }

        private void ResolveTheaterDimensions()
        {
            try
            {
                MapSettings ms = FindObjectOfType<MapSettings>();
                if (ms != null && ms.MapSize.x > 1000f && ms.MapSize.y > 1000f)
                {
                    theaterDimensions = ms.MapSize;
                    return;
                }
            }
            catch { }

            theaterDimensions = new Vector2(81920f, 81920f);
        }

        private void EnsureTexture(int w, int h)
        {
            if (overlayTexture != null && overlayTexture.width == w && overlayTexture.height == h)
                return;

            if (overlayTexture != null) Destroy(overlayTexture);

            overlayTexture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "TrenchMapTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            if (overlayImage != null) overlayImage.texture = overlayTexture;
        }

        private void BakeTrenchMapTexture()
        {
            if (overlayTexture == null || trenchManager == null) return;

            int w = overlayTexture.width;
            int h = overlayTexture.height;
            Color32[] pixels = new Color32[w * h]; // Initialized to (0,0,0,0)

            Color32 trenchColor = new Color32(45, 38, 28, 240);       // Dark earth brown
            Color32 crenellationColor = new Color32(20, 18, 14, 255); // Sharp charcoal
            Color32 bunkerColor = new Color32(210, 175, 80, 255);     // Tactical amber strongpoint

            var networks = trenchManager.Networks;
            for (int n = 0; n < networks.Count; n++)
            {
                TrenchNetwork net = networks[n];

                // 1. Draw Edges (Continuous trench polyline)
                foreach (var edge in net.Edges)
                {
                    if (edge.PathPoints == null || edge.PathPoints.Length < 2) continue;

                    for (int p = 0; p < edge.PathPoints.Length - 1; p++)
                    {
                        Vector2 p0 = WorldToTex(edge.PathPoints[p], w, h);
                        Vector2 p1 = WorldToTex(edge.PathPoints[p + 1], w, h);

                        DrawLine(pixels, w, h, (int)p0.x, (int)p0.y, (int)p1.x, (int)p1.y, trenchColor, 2);

                        // Draw perpendicular NATO crenellations / tick marks on fire trenches
                        if (edge.Type == TrenchEdgeType.ZigzagFireTrench)
                        {
                            Vector2 dir = (p1 - p0).normalized;
                            Vector2 side = new Vector2(-dir.y, dir.x); // 90-degree normal

                            Vector2 mid = (p0 + p1) * 0.5f;
                            Vector2 tickEnd = mid + side * 5f; // 5-pixel tick mark
                            DrawLine(pixels, w, h, (int)mid.x, (int)mid.y, (int)tickEnd.x, (int)tickEnd.y, crenellationColor, 1);
                        }
                    }
                }

                // 2. Draw Nodes (Bunkers, Heavy Weapon Pits)
                foreach (var node in net.Nodes)
                {
                    if (node.Type == TrenchNodeType.BunkerBlindage || node.Type == TrenchNodeType.HeavyWeaponPit)
                    {
                        Vector2 nodePos = WorldToTex(node.Position, w, h);
                        DrawFilledSquare(pixels, w, h, (int)nodePos.x, (int)nodePos.y, 4, bunkerColor);
                    }
                }
            }

            overlayTexture.SetPixels32(pixels);
            overlayTexture.Apply();
        }

        private Vector2 WorldToTex(Vector3 worldPos, int texW, int texH)
        {
            // Center is (0,0) in world space, spanning +/- half dimension
            float normX = (worldPos.x + theaterDimensions.x * 0.5f) / theaterDimensions.x;
            float normY = (worldPos.z + theaterDimensions.y * 0.5f) / theaterDimensions.y;

            return new Vector2(
                Math.Clamp(normX * (texW - 1), 0, texW - 1),
                Math.Clamp(normY * (texH - 1), 0, texH - 1)
            );
        }

        private static void DrawLine(Color32[] pixels, int w, int h, int x0, int y0, int x1, int y1, Color32 color, int thickness)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                DrawBrush(pixels, w, h, x0, y0, thickness, color);

                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private static void DrawBrush(Color32[] pixels, int w, int h, int cx, int cy, int radius, Color32 color)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                int py = cy + dy;
                if (py < 0 || py >= h) continue;

                for (int dx = -radius; dx <= radius; dx++)
                {
                    int px = cx + dx;
                    if (px < 0 || px >= w) continue;

                    pixels[py * w + px] = color;
                }
            }
        }

        private static void DrawFilledSquare(Color32[] pixels, int w, int h, int cx, int cy, int halfSize, Color32 color)
        {
            for (int y = cy - halfSize; y <= cy + halfSize; y++)
            {
                if (y < 0 || y >= h) continue;
                for (int x = cx - halfSize; x <= cx + halfSize; x++)
                {
                    if (x < 0 || x >= w) continue;
                    pixels[y * w + x] = color;
                }
            }
        }
    }
}
