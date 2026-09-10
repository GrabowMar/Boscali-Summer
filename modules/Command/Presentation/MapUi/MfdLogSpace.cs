using System;

namespace BoscaliSummer.Features.Command.Presentation.MapUi
{
    internal static class MfdLogSpace
    {
        public static float Remaining(float height, float x, float y, float width,
            float left, float right, float bottom, float top, float gutter)
        {
            if (right <= x || left >= x + width || bottom >= y || top <= y - height)
                return height;
            return Math.Max(0f, Math.Min(height, y - top - gutter));
        }
    }
}
