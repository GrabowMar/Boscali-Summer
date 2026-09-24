using NOAvionics.Ui;
using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    /// <summary>
    /// A straight stroke as one rotated image, so traces, links and routes can be pooled and moved
    /// without rebuilding meshes. Coordinates are <see cref="AvKit"/> coordinates: top-left origin,
    /// Y negative downward. The stroke's look (colour, sprite, dashes) is the caller's.
    /// </summary>
    internal static class Lines
    {
        public static Image Make(RectTransform parent, Color color, Sprite sprite = null, string name = "Stroke")
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.sizeDelta = new Vector2(0f, 1f);
            Image image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = color;
            if (sprite != null)
            {
                image.sprite = sprite;
                image.type = Image.Type.Tiled;
            }
            return image;
        }

        /// <summary>Stretch the stroke from (x1, y1) to (x2, y2). A zero-length stroke is hidden.</summary>
        public static void Set(Image line, float x1, float y1, float x2, float y2, float width)
        {
            float dx = x2 - x1;
            float dy = y2 - y1;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            bool show = length > 0.5f && !float.IsNaN(length);
            if (line.enabled != show) line.enabled = show;
            if (!show) return;
            RectTransform rect = line.rectTransform;
            rect.anchoredPosition = new Vector2(x1, y1);
            rect.sizeDelta = new Vector2(length, width);
            rect.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(dy, dx) * Mathf.Rad2Deg);
        }

        /// <summary>Place a square marker centred on (x, y).</summary>
        public static void Centre(RectTransform rect, float x, float y, float size) =>
            Centre(rect, x, y, size, size);

        public static void Centre(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
