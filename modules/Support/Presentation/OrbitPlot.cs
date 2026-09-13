using System;
using BoscaliSummer.Features.Support.Runtime;
using NOAvionics;
using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation
{
    /// <summary>
    /// Baked station schematic for the SPACE page: map bounds, shaded coverage, swath rims,
    /// transfer paths and satellite glyphs. One texture, repainted on demand. Panel labels
    /// are projected over it separately.
    /// </summary>
    internal sealed class OrbitPlot
    {
        private GameObject root;
        private RawImage view;
        private Texture2D texture;
        private Color32[] pixels;
        private int width = 240;
        private int height = 160;

        public void Build(RectTransform parent, Rect area)
        {
            width = Mathf.Max(64, Mathf.RoundToInt(area.width));
            height = Mathf.Max(64, Mathf.RoundToInt(area.height));
            pixels = new Color32[width * height];

            root = new GameObject("StationPlot", typeof(RectTransform), typeof(RawImage));
            var rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            AvKit.Place(rect, area);
            view = root.GetComponent<RawImage>();
            view.raycastTarget = false;

            texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            view.texture = texture;
            Clear(new Color32(7, 12, 15, 255));
            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        public void Destroy()
        {
            if (view != null) view.texture = null;
            if (texture != null) UnityEngine.Object.Destroy(texture);
            if (root != null) UnityEngine.Object.Destroy(root);
            root = null;
            view = null;
            texture = null;
            pixels = null;
        }

        /// <summary>Shared world→plot scale, so panel labels project onto the same frame.</summary>
        public static float Scale(float plotWidth, float plotHeight, float mapRadius) =>
            (Mathf.Min(plotWidth, plotHeight) * 0.42f) / Mathf.Max(1f, mapRadius);

        /// <summary>Project a world point into panel coordinates using the plot's area rect.</summary>
        public static void Project(Constellation constellation, float worldX, float worldZ,
                                   Rect plotArea, float mapFactor, out float x, out float y)
        {
            float scale = Scale(plotArea.width, plotArea.height, constellation.MapRadius) * mapFactor;
            x = plotArea.x + plotArea.width * 0.5f + worldX * scale;
            y = plotArea.y - plotArea.height * 0.5f + worldZ * scale;
        }

        /// <summary>Convert a world point to texture pixels; used by Render internally.</summary>
        private void ToPlot(Constellation constellation, float worldX, float worldZ, out float x, out float y)
        {
            float scale = Scale(width, height, constellation.MapRadius);
            x = width * 0.5f + worldX * scale;
            y = height * 0.5f - worldZ * scale;
        }

        public void Render(Constellation constellation, int selectedId, bool hasCursor,
                           float cursorX, float cursorZ, Color selectedColor)
        {
            if (texture == null || constellation == null) return;

            Clear(new Color32(7, 12, 15, 255));

            OrbitalBounds.Extents(out float halfWidth, out float halfHeight);
            if (halfWidth > 0f && halfHeight > 0f)
            {
                float scale = Scale(width, height, constellation.MapRadius);
                Rect bounds = new Rect(
                    width * 0.5f - halfWidth * scale, height * 0.5f - halfHeight * scale,
                    halfWidth * 2f * scale, halfHeight * 2f * scale);
                DrawRectFill(bounds, new Color32(13, 23, 25, 255));
                DrawRectGrid(bounds, new Color32(24, 40, 39, 255), 6);
                DrawRect(bounds, new Color32(72, 122, 106, 255));
                DrawRect(new Rect(bounds.x + 1f, bounds.y + 1f, bounds.width - 2f, bounds.height - 2f),
                         new Color32(72, 122, 106, 190));
                DrawCornerTicks(bounds, new Color32(126, 186, 162, 255), 9f);
            }

            // Coverage shading first, so rims and glyphs read over it.
            for (int i = 0; i < constellation.Satellites.Count; i++)
            {
                Satellite satellite = constellation.Satellites[i];
                if (satellite.State != SatelliteState.Stationed) continue;
                bool isSelected = satellite.Id == selectedId;
                Color role = RoleColor(satellite.Role);
                ToPlot(constellation, satellite.StationX, satellite.StationZ, out float sx, out float sy);
                float radius = satellite.Orbit.Swath * Scale(width, height, constellation.MapRadius);
                DrawDiscGradient(sx, sy, radius, role, isSelected ? 0.30f : 0.18f);
            }

            // Transfer paths under the glyphs.
            for (int i = 0; i < constellation.Satellites.Count; i++)
            {
                Satellite satellite = constellation.Satellites[i];
                if (satellite.State != SatelliteState.Transit) continue;
                Color role = RoleColor(satellite.Role);
                ToPlot(constellation, satellite.OriginX, satellite.OriginZ, out float ox, out float oy);
                ToPlot(constellation, satellite.StationX, satellite.StationZ, out float dx, out float dy);
                DrawDashedLine(ox, oy, dx, dy, To32(role, 150));
                DrawCircleAA(dx, dy, 7f, To32(role, 220), 1.1f);
                Put(Mathf.RoundToInt(dx), Mathf.RoundToInt(dy), To32(role, 255));
            }

            // Swath rims and satellite glyphs.
            for (int i = 0; i < constellation.Satellites.Count; i++)
            {
                Satellite satellite = constellation.Satellites[i];
                bool isSelected = satellite.Id == selectedId;
                Color role = RoleColor(satellite.Role);
                float radius = satellite.Orbit.Swath * Scale(width, height, constellation.MapRadius);

                constellation.Position(satellite, out float px, out float pz);
                ToPlot(constellation, px, pz, out float fx, out float fy);

                if (isSelected)
                {
                    DrawCircleAA(fx, fy, radius, To32(role, 230), 1.3f);
                    DrawCircleAA(fx, fy, 9f, To32(selectedColor, 235), 1.3f);
                }
                else if (satellite.State == SatelliteState.Stationed)
                {
                    DrawCircleAA(fx, fy, radius, To32(role, 110), 1.0f);
                }
                DrawSatGlyph(fx, fy, role);
            }

            if (hasCursor)
            {
                ToPlot(constellation, cursorX, cursorZ, out float cx, out float cy);
                Color32 mark = new Color32(226, 244, 226, 235);
                int ix = Mathf.RoundToInt(cx);
                int iy = Mathf.RoundToInt(cy);
                Put(ix, iy, mark);
                for (int d = 3; d <= 7; d++)
                {
                    Put(ix + d, iy, To32(Color.white, 210));
                    Put(ix - d, iy, To32(Color.white, 210));
                    Put(ix, iy + d, To32(Color.white, 210));
                    Put(ix, iy - d, To32(Color.white, 210));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false);
        }

        private static Color RoleColor(SatelliteRole role) =>
            role == SatelliteRole.Recon ? AvTheme.Friendly :
            role == SatelliteRole.Strike ? AvTheme.Alert : AvTheme.Warning;

        private static Color32 To32(Color color, byte alpha) =>
            new Color32((byte)(color.r * 255f), (byte)(color.g * 255f), (byte)(color.b * 255f), alpha);

        private void Clear(Color32 color)
        {
            for (int i = 0; i < pixels.Length; i++) pixels[i] = color;
        }

        private void Put(int x, int y, Color32 color)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return;
            int index = y * width + x;
            Color32 under = pixels[index];
            int alpha = color.a;
            if (alpha >= 255)
            {
                pixels[index] = color;
                return;
            }
            int inverse = 255 - alpha;
            pixels[index] = new Color32(
                (byte)((color.r * alpha + under.r * inverse) / 255),
                (byte)((color.g * alpha + under.g * inverse) / 255),
                (byte)((color.b * alpha + under.b * inverse) / 255),
                255);
        }

        private void DrawDiscGradient(float cx, float cy, float radius, Color color, float peakAlpha)
        {
            if (radius <= 0f) return;
            int r = Mathf.CeilToInt(radius);
            int centerX = Mathf.RoundToInt(cx);
            int centerY = Mathf.RoundToInt(cy);
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    float distance = Mathf.Sqrt(x * x + y * y);
                    if (distance > radius) continue;
                    float falloff = 1f - distance / radius;
                    float alpha = peakAlpha * Mathf.Pow(falloff, 1.5f) * 255f;
                    if (alpha < 2f) continue;
                    Put(centerX + x, centerY + y, To32(color, (byte)Mathf.Clamp(alpha, 0f, 255f)));
                }
            }
        }

        private void DrawCircleAA(float cx, float cy, float radius, Color32 color, float thickness)
        {
            if (radius <= 1f) return;
            int r = Mathf.CeilToInt(radius + thickness);
            int centerX = Mathf.RoundToInt(cx);
            int centerY = Mathf.RoundToInt(cy);
            float half = Mathf.Max(0.6f, thickness * 0.5f);
            for (int y = -r; y <= r; y++)
            {
                for (int x = -r; x <= r; x++)
                {
                    float distance = Mathf.Sqrt(x * x + y * y);
                    float cover = Mathf.Clamp01(half + 0.6f - Mathf.Abs(distance - radius));
                    if (cover <= 0.02f) continue;
                    byte alpha = (byte)Mathf.Clamp(color.a * cover, 0f, 255f);
                    Put(centerX + x, centerY + y, new Color32(color.r, color.g, color.b, alpha));
                }
            }
        }

        private void DrawDashedLine(float x1, float y1, float x2, float y2, Color32 color)
        {
            float dx = x2 - x1, dy = y2 - y1;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            if (length < 1f) return;
            for (float t = 0f; t <= length; t += 6f)
            {
                float dashEnd = Mathf.Min(t + 3.5f, length);
                for (float s = t; s <= dashEnd; s += 1f)
                {
                    float u = s / length;
                    Put(Mathf.RoundToInt(x1 + dx * u), Mathf.RoundToInt(y1 + dy * u),
                        t < length * 0.35f ? To32(Color.white, (byte)(color.a * 0.45f)) : color);
                }
            }
        }

        private void DrawSatGlyph(float cx, float cy, Color color)
        {
            int x = Mathf.RoundToInt(cx);
            int y = Mathf.RoundToInt(cy);
            Color32 body = To32(color, 255);
            Color32 panel = To32(color, 205);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -2; dy <= 2; dy++)
                    Put(x + dx, y + dy, body);
            for (int dx = -6; dx <= -3; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    Put(x + dx, y + dy, panel);
            for (int dx = 3; dx <= 6; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    Put(x + dx, y + dy, panel);
            Put(x, y - 4, To32(color, 170));
        }

        private void DrawRectFill(Rect rect, Color32 color)
        {
            int left = Mathf.RoundToInt(rect.xMin);
            int right = Mathf.RoundToInt(rect.xMax);
            int top = Mathf.RoundToInt(rect.yMin);
            int bottom = Mathf.RoundToInt(rect.yMax);
            for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++)
                    Put(x, y, color);
        }

        private void DrawRectGrid(Rect rect, Color32 color, int divisions)
        {
            for (int i = 1; i < divisions; i++)
            {
                float x = Mathf.Lerp(rect.xMin, rect.xMax, i / (float)divisions);
                float y = Mathf.Lerp(rect.yMin, rect.yMax, i / (float)divisions);
                for (int py = Mathf.RoundToInt(rect.yMin); py <= Mathf.RoundToInt(rect.yMax); py++)
                    Put(Mathf.RoundToInt(x), py, color);
                for (int px = Mathf.RoundToInt(rect.xMin); px <= Mathf.RoundToInt(rect.xMax); px++)
                    Put(px, Mathf.RoundToInt(y), color);
            }
        }

        private void DrawRect(Rect rect, Color32 color)
        {
            int left = Mathf.RoundToInt(rect.xMin);
            int right = Mathf.RoundToInt(rect.xMax);
            int top = Mathf.RoundToInt(rect.yMin);
            int bottom = Mathf.RoundToInt(rect.yMax);
            for (int x = left; x <= right; x++)
            {
                Put(x, top, color);
                Put(x, bottom, color);
            }
            for (int y = top; y <= bottom; y++)
            {
                Put(left, y, color);
                Put(right, y, color);
            }
        }

        private void DrawCornerTicks(Rect rect, Color32 color, float length)
        {
            int left = Mathf.RoundToInt(rect.xMin);
            int right = Mathf.RoundToInt(rect.xMax);
            int top = Mathf.RoundToInt(rect.yMin);
            int bottom = Mathf.RoundToInt(rect.yMax);
            int len = Mathf.RoundToInt(length);
            for (int i = 0; i <= len; i++)
            {
                Put(left + i, top, color);
                Put(right - i, top, color);
                Put(left + i, bottom, color);
                Put(right - i, bottom, color);
                Put(left, top + i, color);
                Put(right, top + i, color);
                Put(left, bottom - i, color);
                Put(right, bottom - i, color);
            }
        }
    }
}
