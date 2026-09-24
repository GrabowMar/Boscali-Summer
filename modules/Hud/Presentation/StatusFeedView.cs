using BoscaliSummer.Features.Hud.Configuration;
using BoscaliSummer.Features.Hud.Domain;
using BoscaliSummer.Framework.Contracts;
using NOAvionics.Ui;
using UnityEngine;

namespace BoscaliSummer.Features.Hud.Presentation
{
    internal sealed class StatusFeedView
    {
        private readonly HudSurface surface;
        private readonly RectTransform panel;
        private readonly HudPanel backdrop;
                private readonly HudRow[] rows = new HudRow[HudLayout.MaxRows];
        public StatusFeedView(Transform parent)
        {
            surface = new HudSurface("Boscali / status feed", parent);
            panel = HudSurface.Rect("Status", surface.Transform);
            backdrop = panel.gameObject.AddComponent<HudPanel>(); backdrop.raycastTarget = false;
            for (int i = 0; i < rows.Length; i++) rows[i] = new HudRow(panel, i);
        }
        public void Present(HudMessage[] messages, int count, HudSettings config, HudBounds avoid)
        {
            surface.Resize(HudLayout.Scale(config.ScaleStep.Value));
            Rect safe = surface.Safe;
            float width = Mathf.Min(300, safe.width - 32);
            float pitch = config.ShowDetails.Value ? 36 : 24;
            count = Mathf.Min(count, Mathf.Max(0, Mathf.FloorToInt((safe.height - 60) / pitch)));
            if (count == 0) { Hide(); return; }
            float height = 8;
            for (int i = 0; i < count; i++) height += HudRow.Height(messages[i], config.ShowDetails.Value);
            HudPlacement anchor = HudLayout.Place(config.ResolvedAnchor());
            float x = Mathf.Lerp(safe.xMin + 16, safe.xMax - width - 16, anchor.AnchorX) + config.OffsetX.Value;
            float y = Mathf.Lerp(safe.yMin + 16, safe.yMax - height - 16, anchor.AnchorY) + config.OffsetY.Value;
            if (config.ResolvedAnchor() == HudAnchor.UnderWeapons)
            {
                y = safe.yMax - 168 / surface.Scale - height + config.OffsetY.Value;
            }
            x = Mathf.Clamp(x, safe.xMin + 16, safe.xMax - width - 16);
            y = Mathf.Clamp(y, safe.yMin + 16, safe.yMax - height - 16);
            Rect occupied = new Rect(avoid.X / surface.Scale, avoid.Y / surface.Scale, avoid.Width / surface.Scale, avoid.Height / surface.Scale);
            Rect proposed = new Rect(x, y, width, height);
            if (avoid.Visible && proposed.Overlaps(occupied))
            {
                if (occupied.yMax + 12 + height <= safe.yMax - 16) y = occupied.yMax + 12;
                else if (occupied.yMin - 12 - height >= safe.yMin + 16) y = occupied.yMin - 12 - height;
                else x = occupied.center.x > safe.center.x ? occupied.xMin - width - 12 : occupied.xMax + 12;
                x = Mathf.Clamp(x, safe.xMin + 16, safe.xMax - width - 16);
                // No readable space beats painting two instruments on top of one another.
                if (new Rect(x, y, width, height).Overlaps(occupied)) { Hide(); return; }
            }
            // Screen anchors must not inherit transient floating-origin movement from native canvases.
            HudSurface.Place(panel, Mathf.Round(x * surface.Scale) / surface.Scale, Mathf.Round(y * surface.Scale) / surface.Scale, width, height);
            backdrop.Style(config.Contrast.Value, anchor.AnchorX < .5f, false);
            float cursor = height - 4;
            for (int i = 0; i < rows.Length; i++)
            {
                if (i >= count) { rows[i].Hide(); continue; }
                float rowHeight = HudRow.Height(messages[i], config.ShowDetails.Value);
                cursor -= rowHeight;
                HudSurface.Place(rows[i].Rect, 0, cursor, width, rowHeight);
                rows[i].Present(messages[i], width, config.ShowDetails.Value);
            }
            surface.Group.alpha = HudLayout.Opacity(config.OpacityStep.Value); surface.Show(true);
        }
        public void Hide() => surface.Show(false);
        public void Destroy() => surface.Destroy();
    }
}
