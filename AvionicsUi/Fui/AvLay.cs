using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics
{
    /// <summary>Top-left placement in panel units, and the canvas split that keeps text rebuilds local (spec §8).</summary>
    public static class AvLay
    {
        public static RectTransform Child(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var t = (RectTransform)go.transform;
            t.SetParent(parent, false);
            t.anchorMin = t.anchorMax = t.pivot = new Vector2(0f, 1f);
            return t;
        }

        public static void Place(RectTransform t, AvSlot s) => Place(t, s.X, s.Y, s.W, s.H);

        public static void Place(RectTransform t, float x, float y, float w, float h)
        {
            t.anchorMin = t.anchorMax = t.pivot = new Vector2(0f, 1f);
            t.anchoredPosition = new Vector2(x, -y);
            t.sizeDelta = new Vector2(w, h);
            t.localScale = Vector3.one;
        }

        public static void Fill(RectTransform t, float inset = 0f)
        {
            t.anchorMin = Vector2.zero; t.anchorMax = Vector2.one; t.pivot = new Vector2(0.5f, 0.5f);
            t.offsetMin = new Vector2(inset, inset); t.offsetMax = new Vector2(-inset, -inset);
            t.localScale = Vector3.one;
        }

        public static Canvas Nest(RectTransform t, bool interactive)
        {
            Canvas c = t.GetComponent<Canvas>();
            if (c == null) c = t.gameObject.AddComponent<Canvas>();
            c.overrideSorting = false;
            c.pixelPerfect = false;
            c.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            Canvas root = c.rootCanvas;
            if (root != null) root.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            if (interactive && t.GetComponent<GraphicRaycaster>() == null) t.gameObject.AddComponent<GraphicRaycaster>();
            return c;
        }

        /// <summary>Height available to a console mounted under <paramref name="parent"/> (walks up past zero-height anchors).</summary>
        public static float ResolveHeight(RectTransform parent, float min, float max)
        {
            if (max < min) max = min;
            if (parent == null) return min;
            float available = parent.rect.height;
            RectTransform cursor = parent;
            for (int i = 0; i < 4 && available <= 1f && cursor != null; i++)
            {
                cursor = cursor.parent as RectTransform;
                if (cursor != null) available = cursor.rect.height;
            }
            if (available <= 1f) return min;
            return Mathf.Clamp(Mathf.Floor(available), min, max);
        }

        /// <summary>Nudge a panel back inside its root canvas (leading edge pinned when it is larger than the canvas).</summary>
        public static void ClampIntoCanvas(RectTransform panel, float margin = 8f)
        {
            if (panel == null || panel.parent == null) return;
            Canvas canvas = panel.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            var canvasRt = canvas.rootCanvas.transform as RectTransform;
            if (canvasRt == null) return;
            panel.GetWorldCorners(Corners);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 local = canvasRt.InverseTransformPoint(Corners[i]);
                minX = Mathf.Min(minX, local.x); maxX = Mathf.Max(maxX, local.x);
                minY = Mathf.Min(minY, local.y); maxY = Mathf.Max(maxY, local.y);
            }
            Rect bounds = canvasRt.rect;
            float dx = minX < bounds.xMin + margin ? bounds.xMin + margin - minX : maxX > bounds.xMax - margin ? bounds.xMax - margin - maxX : 0f;
            float dy = maxY > bounds.yMax - margin ? bounds.yMax - margin - maxY : minY < bounds.yMin + margin ? bounds.yMin + margin - minY : 0f;
            if (Mathf.Approximately(dx, 0f) && Mathf.Approximately(dy, 0f)) return;
            Vector3 world = canvasRt.TransformVector(new Vector3(dx, dy, 0f));
            Vector3 local2 = panel.parent.InverseTransformVector(world);
            panel.anchoredPosition += new Vector2(local2.x, local2.y);
        }

        private static readonly Vector3[] Corners = new Vector3[4];

        public static Image Solid(RectTransform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }
    }
}
