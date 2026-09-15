using System;
using BepInEx.Logging;
using BoscaliSummer.Features.Trenches.Configuration;
using BoscaliSummer.Features.Trenches.Runtime;
using BoscaliSummer.Framework.Lifecycle;
using BoscaliSummer.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Trenches.Presentation
{
    /// <summary>
    /// Renders dynamic NATO-standard entrenchment symbology onto the tactical theater map
    /// (DynamicMap): the fire line as one solid crenellated trace following the real front
    /// contour, support and redoubt traces dimmer, native strongpoints marked, and the
    /// stage of each position ticked. Host-local; never pollutes flight HUDs.
    /// </summary>
    internal sealed class TrenchMapOverlay : MonoBehaviour, ISceneService
    {
        private const int BaseTextureSize = 1536;
        private const float ToothSpacing = 11f;

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
                trenchManager.OnLinesChanged += HandleLinesChanged;
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
            TheaterFrame.Invalidate();
        }

        private void OnDestroy()
        {
            if (trenchManager != null)
            {
                trenchManager.OnLinesChanged -= HandleLinesChanged;
            }
            ResetForScene();
        }

        private void HandleLinesChanged()
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

            theaterDimensions = TheaterFrame.Resolve();

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

            var lines = trenchManager.Lines;
            for (int n = 0; n < lines.Count; n++)
            {
                TrenchLine line = lines[n];
                Color32 factionColor = line.OwnerHq == dynamicMap?.HQ
                    ? new Color32(65, 210, 255, 255) : new Color32(255, 100, 80, 255);
                if (line.Overrun) factionColor = new Color32(130, 130, 130, 180);
                else if (line.Suppressed) factionColor = new Color32(255, 195, 65, 255);
                Color32 rearColor = factionColor;
                rearColor.a = 110;

                // 1. The fire line solid with crenellations facing the threat, spaced on the map
                // rather than on the ditch's own station spacing.
                DrawTrace(pixels, w, h, line.Curve, line.Threat, factionColor, 1, true, crenellationColor);
                // 2. Support, redoubt, communication and sap traces dimmer.
                DrawTrace(pixels, w, h, line.Support, line.Threat, rearColor, 1, false, crenellationColor);
                DrawTrace(pixels, w, h, line.Redoubt, line.Threat, rearColor, 1, false, crenellationColor);
                DrawTraces(pixels, w, h, line.Links, rearColor);
                DrawTraces(pixels, w, h, line.Spurs, rearColor);
                // 3. Native strongpoints: the bays the emplacements actually hold.
                DrawStrongpoints(pixels, w, h, line, factionColor);

                // Stage ticks and a crossed-out neutralized position remain legible
                // without relying only on red/blue/amber colour differences.
                Vector2 center = WorldToTex(line.Center, w, h);
                int cx = (int)center.x, cy = (int)center.y;
                if (line.Overrun)
                {
                    DrawLine(pixels, w, h, cx - 4, cy - 4, cx + 4, cy + 4, factionColor, 1);
                    DrawLine(pixels, w, h, cx - 4, cy + 4, cx + 4, cy - 4, factionColor, 1);
                }
                else for (int tick = 0; tick < (int)line.Stage; tick++)
                    DrawLine(pixels, w, h, cx - 6 + tick * 4, cy + 6, cx - 6 + tick * 4, cy + 9, factionColor, 1);
            }

            overlayTexture.SetPixels32(pixels);
            overlayTexture.Apply();
        }

        private void DrawStrongpoints(Color32[] pixels, int w, int h, TrenchLine line, Color32 color)
        {
            DrawMarks(pixels, w, h, line.Anchors, 0.15f, color);
            DrawMarks(pixels, w, h, line.Anchors, 0.5f, color);
            DrawMarks(pixels, w, h, line.Anchors, 0.85f, color);
            DrawMarks(pixels, w, h, line.SupportAnchors, 0.5f, color);
        }

        private void DrawMarks(Color32[] pixels, int w, int h, Vector3[] anchors, float fraction, Color32 color)
        {
            if (anchors == null || anchors.Length == 0) return;
            Vector3 anchor = anchors[Mathf.Clamp(Mathf.RoundToInt(fraction * (anchors.Length - 1)), 0, anchors.Length - 1)];
            Vector2 at = WorldToTex(anchor, w, h);
            DrawFilledSquare(pixels, w, h, (int)at.x, (int)at.y, 1, color);
        }

        private void DrawTrace(Color32[] pixels, int w, int h, Vector3[] trace, Vector3[] threat,
            Color32 color, int thickness, bool crenellate, Color32 tickColor)
        {
            if (trace == null || trace.Length < 2) return;
            float sinceTooth = 0f;
            for (int p = 0; p < trace.Length - 1; p++)
            {
                Vector2 p0 = WorldToTex(trace[p], w, h);
                Vector2 p1 = WorldToTex(trace[p + 1], w, h);
                DrawLine(pixels, w, h, (int)p0.x, (int)p0.y, (int)p1.x, (int)p1.y, color, thickness);
                if (!crenellate) continue;

                // One tooth every ToothSpacing map pixels, not one per path station: the ditch
                // is laid out every few metres, so a tooth per station merged into a solid bar.
                sinceTooth += Vector2.Distance(p0, p1);
                if (sinceTooth < ToothSpacing) continue;
                sinceTooth = 0f;
                Vector3 direction = p + 1 < threat.Length ? threat[p] : threat[0];
                DrawCrenellations(pixels, w, h, p0, p1, new Vector2(direction.x, direction.z), tickColor, 1);
            }
        }

        private void DrawTraces(Color32[] pixels, int w, int h, Vector3[][] traces, Color32 color)
        {
            if (traces == null) return;
            for (int t = 0; t < traces.Length; t++)
            {
                Vector3[] trace = traces[t];
                for (int p = 0; p + 1 < trace.Length; p++)
                {
                    Vector2 p0 = WorldToTex(trace[p], w, h);
                    Vector2 p1 = WorldToTex(trace[p + 1], w, h);
                    DrawLine(pixels, w, h, (int)p0.x, (int)p0.y, (int)p1.x, (int)p1.y, color, 1);
                }
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
