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
        private Color32[] bakeBuffer;

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
            bakeBuffer = null;

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
            if (currentMaximized && trenchManager != null && trenchManager.RefreshPlannedSites())
            {
                BakeTrenchMapTexture();
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
                MapSettings ms = NetworkSceneSingleton<LevelInfo>.i?.LoadedMapSettings;
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
            if (bakeBuffer == null || bakeBuffer.Length != w * h) bakeBuffer = new Color32[w * h];
            Color32[] pixels = bakeBuffer;
            Array.Clear(pixels, 0, pixels.Length);

            Color32 crenellationColor = new Color32(20, 18, 14, 255); // Sharp charcoal

            // Projected entrenchments: a dim dashed trace of the contested line where
            // sectors are queued for digging. Drawn first so real networks read on top.
            var planned = trenchManager.PlannedSites;
            for (int i = 0; i < planned.Count; i++)
            {
                PlannedEntrenchment plan = planned[i];
                Color32 ghost = plan.Owner == dynamicMap?.HQ
                    ? new Color32(65, 210, 255, 110) : new Color32(255, 100, 80, 110);
                Vector2 lateral = new Vector2(-plan.Threat.z, plan.Threat.x).normalized;
                Vector3 offset = new Vector3(lateral.x, 0f, lateral.y) * plan.HalfSpan;
                Vector2 a = WorldToTex(plan.Position - offset, w, h);
                Vector2 b = WorldToTex(plan.Position + offset, w, h);
                DrawDashedLine(pixels, w, h, a, b, ghost);
                DrawCrenellations(pixels, w, h, a, b, new Vector2(plan.Threat.x, plan.Threat.z),
                    new Color32(20, 18, 14, 200), Mathf.Clamp((int)(Vector2.Distance(a, b) / 6f), 2, 12));
            }

            var networks = trenchManager.Networks;
            for (int n = 0; n < networks.Count; n++)
            {
                TrenchNetwork net = networks[n];
                Color32 factionColor = net.OwnerHq == dynamicMap?.HQ
                    ? new Color32(65, 210, 255, 255) : new Color32(255, 100, 80, 255);
                if (net.Overrun) factionColor = new Color32(130, 130, 130, 180);
                else if (net.Suppressed) factionColor = new Color32(255, 195, 65, 255);
                Color32 rearColor = factionColor;
                rearColor.a = 110;
                Vector2 threat2D = new Vector2(net.ThreatDirection.x, net.ThreatDirection.z);

                // 1. Draw Edges: the fire line solid, support/rear/communication traces dimmer.
                foreach (var edge in net.Edges)
                {
                    if (edge.PathPoints == null || edge.PathPoints.Length < 2) continue;
                    bool frontLine = Vector3.Dot(edge.PathPoints[0] - net.SeedCenter, net.ThreatDirection) >= -30f;
                    Color32 color = frontLine ? factionColor : rearColor;
                    int thickness = frontLine ? 2 : 1;

                    for (int p = 0; p < edge.PathPoints.Length - 1; p++)
                    {
                        Vector2 p0 = WorldToTex(edge.PathPoints[p], w, h);
                        Vector2 p1 = WorldToTex(edge.PathPoints[p + 1], w, h);
                        DrawLine(pixels, w, h, (int)p0.x, (int)p0.y, (int)p1.x, (int)p1.y, color, thickness);

                        // Half-density crenellations keep the fire line readable, not blocky.
                        if (frontLine && p % 2 == 0)
                            DrawCrenellations(pixels, w, h, p0, p1, threat2D, crenellationColor, 1);
                    }
                }

                // 2. Strongpoints (dugouts, weapon pits) only; rifle bays stay off the map.
                foreach (var node in net.Nodes)
                {
                    if (node.Type != TrenchNodeType.BunkerBlindage && node.Type != TrenchNodeType.HeavyWeaponPit) continue;
                    Vector2 nodePos = WorldToTex(node.Position, w, h);
                    DrawFilledSquare(pixels, w, h, (int)nodePos.x, (int)nodePos.y, 1, factionColor);
                }
                // Stage ticks and a crossed-out neutralized position remain legible
                // without relying only on red/blue/amber colour differences.
                Vector2 center = WorldToTex(net.Center, w, h);
                int cx = (int)center.x, cy = (int)center.y;
                if (net.Overrun)
                {
                    DrawLine(pixels, w, h, cx - 4, cy - 4, cx + 4, cy + 4, factionColor, 1);
                    DrawLine(pixels, w, h, cx - 4, cy + 4, cx + 4, cy - 4, factionColor, 1);
                }
                else for (int tick = 0; tick < (int)net.Stage; tick++)
                    DrawLine(pixels, w, h, cx - 6 + tick * 4, cy + 6, cx - 6 + tick * 4, cy + 9, factionColor, 1);
            }

            overlayTexture.SetPixels32(pixels);
            overlayTexture.Apply();
        }

        private static void DrawDashedLine(Color32[] pixels, int w, int h, Vector2 a, Vector2 b, Color32 color)
        {
            float length = Vector2.Distance(a, b);
            if (length < 1f)
            {
                DrawLine(pixels, w, h, (int)a.x, (int)a.y, (int)b.x, (int)b.y, color, 1);
                return;
            }
            const float dash = 3f, gap = 3f;
            Vector2 dir = (b - a) / length;
            for (float d = 0f; d < length; d += dash + gap)
            {
                Vector2 s = a + dir * d;
                Vector2 e = a + dir * Mathf.Min(length, d + dash);
                DrawLine(pixels, w, h, (int)s.x, (int)s.y, (int)e.x, (int)e.y, color, 1);
            }
        }

        /// <summary>NATO crenellation ticks perpendicular to the trace, facing the threat.</summary>
        private static void DrawCrenellations(Color32[] pixels, int w, int h, Vector2 a, Vector2 b, Vector2 threat,
            Color32 tickColor, int count)
        {
            Vector2 delta = b - a;
            float length = delta.magnitude;
            if (length < 0.5f || count < 1) return;
            Vector2 side = new Vector2(-delta.y, delta.x) / length;
            if (threat.sqrMagnitude > 0.001f && Vector2.Dot(side, threat) < 0f) side = -side;

            for (int i = 0; i < count; i++)
            {
                Vector2 at = Vector2.Lerp(a, b, (i + 0.5f) / count);
                DrawLine(pixels, w, h, (int)at.x, (int)at.y,
                    (int)(at.x + side.x * 4f), (int)(at.y + side.y * 4f), tickColor, 1);
            }
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
