using NOAvionics;
using UnityEngine;
using UnityEngine.UI;

namespace NOAvionics.Ui
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
