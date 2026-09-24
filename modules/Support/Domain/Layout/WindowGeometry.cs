using System;

namespace BoscaliSummer.Features.Support.Domain.Layout
{
    /// <summary>The popup rect. Its top margin clears the map's theater wire and the 32px room notches.</summary>
    internal static class WindowGeometry
    {
        public const float SideMargin = 48f;
        public const float TopMargin = 72f;
        public const float BottomMargin = 40f;

        /// <summary>The window on a 1920×1080 canvas. Also the result of a non-finite input.</summary>
        public static Box Default => new Box(SideMargin, TopMargin, 1824f, 968f);

        public static Box Window(float canvasW, float canvasH)
        {
            if (!float.IsFinite(canvasW) || !float.IsFinite(canvasH) || canvasW <= 0f || canvasH <= 0f)
                return Default;
            float width = Clamp(canvasW - SideMargin * 2f, 1280f, 1840f);
            float height = Clamp(canvasH - TopMargin - BottomMargin, 720f, 968f);
            if (width > canvasW) width = canvasW;
            if (height > canvasH) height = canvasH;
            float available = canvasH - TopMargin - BottomMargin;
            float y = available >= height ? TopMargin + (available - height) * 0.5f
                : (canvasH - height) * 0.5f;
            return new Box((canvasW - width) * 0.5f, y, width, height);
        }

        public static Box Lerp(Box from, Box to, float t)
        {
            if (!float.IsFinite(t)) return Default;
            if (t <= 0f) return from;
            if (t >= 1f) return to;
            return new Box(
                from.X + (to.X - from.X) * t,
                from.Y + (to.Y - from.Y) * t,
                from.Width + (to.Width - from.Width) * t,
                from.Height + (to.Height - from.Height) * t);
        }

        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
    }
}
