using UnityEngine;

namespace BoscaliSummer.Core.Ui
{
    /// <summary>
    /// Floating chrome over the big map (the STR card and plate, the event dispatch alert...) registers its rect here, so wheel,
    /// drag, clicks and armed picks over it stay off the map behind it. Command's map pointer test reads it; any module may
    /// register. At most <see cref="Capacity"/> rects; a destroyed one is dropped on the way.
    /// </summary>
    internal static class MapControlRects
    {
        private const int Capacity = 16;
        private static readonly RectTransform[] rects = new RectTransform[Capacity];
        private static readonly Canvas[] canvases = new Canvas[Capacity];
        private static int count;

        internal static void Register(RectTransform rect)
        {
            if (rect == null) return;
            for (int i = 0; i < count; i++) if (ReferenceEquals(rects[i], rect)) return;
            if (count >= Capacity) return;
            rects[count] = rect;
            canvases[count++] = rect.GetComponentInParent<Canvas>();
        }

        internal static void Unregister(RectTransform rect)
        {
            for (int i = 0; i < count; i++)
            {
                if (!ReferenceEquals(rects[i], rect)) continue;
                RemoveAt(i);
                return;
            }
        }

        /// <summary>Scene reset: forget every rect.</summary>
        internal static void Reset()
        {
            for (int i = 0; i < count; i++) { rects[i] = null; canvases[i] = null; }
            count = 0;
        }

        internal static bool Contains(Vector2 screenPoint)
        {
            for (int i = count - 1; i >= 0; i--)
            {
                RectTransform rect = rects[i];
                if (rect == null) { RemoveAt(i); continue; }           // destroyed: Unity's null check
                if (!rect.gameObject.activeInHierarchy) continue;
                Canvas canvas = canvases[i] != null ? canvases[i] : canvases[i] = rect.GetComponentInParent<Canvas>();
                Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
                if (RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, camera)) return true;
            }
            return false;
        }

        private static void RemoveAt(int index)
        {
            rects[index] = rects[--count];
            canvases[index] = canvases[count];
            rects[count] = null;
            canvases[count] = null;
        }
    }
}
