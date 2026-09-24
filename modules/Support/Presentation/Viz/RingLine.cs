using UnityEngine;
using UnityEngine.UI;

namespace BoscaliSummer.Features.Support.Presentation.Viz
{
    /// <summary>
    /// A circle drawn as pooled strokes, so its line keeps the same width at any radius (a scaled ring
    /// sprite fattens as it grows). Dashed draws every other segment. The look is the caller's.
    /// </summary>
    internal sealed class RingLine
    {
        private readonly Image[] segments;
        private readonly bool dashed;

        public RingLine(RectTransform parent, int count, Color color, bool dashed)
        {
            this.dashed = dashed;
            segments = new Image[Mathf.Clamp(count, 8, 96)];
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = Lines.Make(parent, color, null, "Ring");
                segments[i].enabled = false;
            }
        }

        public void Set(Vector2 centre, float radius, float width, Color color)
        {
            bool show = radius > 2f && !float.IsNaN(radius);
            for (int i = 0; i < segments.Length; i++)
            {
                Image segment = segments[i];
                if (!show || (dashed && i % 2 == 1))
                {
                    segment.enabled = false;
                    continue;
                }
                float a0 = i * Mathf.PI * 2f / segments.Length;
                float a1 = (i + 1) * Mathf.PI * 2f / segments.Length;
                Lines.Set(segment, centre.x + Mathf.Cos(a0) * radius, centre.y + Mathf.Sin(a0) * radius,
                    centre.x + Mathf.Cos(a1) * radius, centre.y + Mathf.Sin(a1) * radius, width);
                segment.color = color;
            }
        }

        public void Hide()
        {
            for (int i = 0; i < segments.Length; i++) segments[i].enabled = false;
        }
    }
}
