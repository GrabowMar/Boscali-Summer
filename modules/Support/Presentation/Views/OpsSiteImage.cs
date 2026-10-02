using BoscaliSummer.Modules.Support.Presentation.Window;
using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Modules.Support.Presentation.Views
{
    /// <summary>Georegistered native terrain crop, never a fabricated photograph of a target.</summary>
    internal sealed class OpsSiteImage
    {
        private readonly RawImage image;
        private readonly Image crossH, crossV;
        private readonly Rect area;
        public OpsSiteImage(RectTransform parent, Rect at, bool warm = false)
        {
            area = at;
            Chrome.Panel(parent, at, AvTheme.Ground);
            var go = new GameObject("AreaImagery", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            image = go.GetComponent<RawImage>();
            Chrome.Place(image.rectTransform, at);
            image.raycastTarget = false; image.enabled = false;
            image.material = OpsArtwork.TerrainMaterial(warm);
            Chrome.Outline(parent, at, RoomPaint.Instrument.WithAlpha(0.6f));
            crossH = Chrome.Panel(parent, new Rect(at.x + at.width / 2f - 8f, at.y - at.height / 2f, 16f, 1f), RoomPaint.Command);
            crossV = Chrome.Panel(parent, new Rect(at.x + at.width / 2f, at.y - at.height / 2f + 8f, 1f, 16f), RoomPaint.Command);
            crossH.raycastTarget = crossV.raycastTarget = false;
        }
        public void Set(Sprite source, Vector2 metres, float x, float z)
        {
            image.enabled = source != null && metres.x > 0 && metres.y > 0;
            crossH.enabled = crossV.enabled = image.enabled;
            if (!image.enabled) return;
            Rect pixel = source.textureRect;
            float span = 12000f;
            float u = Mathf.Clamp01(0.5f + x / metres.x), v = Mathf.Clamp01(0.5f + z / metres.y);
            float width = Mathf.Min(1f, span / metres.x), height = Mathf.Min(1f, span * area.height / area.width / metres.y);
            float left = Mathf.Clamp(u - width / 2f, 0f, 1f - width);
            float bottom = Mathf.Clamp(v - height / 2f, 0f, 1f - height);
            float markX = Mathf.Clamp((u - left) / width * area.width, 8f, area.width - 8f);
            float markY = Mathf.Clamp((1f - (v - bottom) / height) * area.height, 8f, area.height - 8f);
            Chrome.Place(crossH.rectTransform, new Rect(area.x + markX - 8f, area.y - markY, 16f, 1f));
            Chrome.Place(crossV.rectTransform, new Rect(area.x + markX, area.y - markY + 8f, 1f, 16f));
            image.texture = source.texture;
            image.uvRect = new Rect((pixel.x + left * pixel.width) / source.texture.width,
                (pixel.y + bottom * pixel.height) / source.texture.height,
                width * pixel.width / source.texture.width, height * pixel.height / source.texture.height);
        }
    }
}
