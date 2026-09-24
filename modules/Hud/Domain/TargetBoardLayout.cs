using System;
using BoscaliSummer.Framework.Contracts;
namespace BoscaliSummer.Features.Hud.Domain
{
    internal static class TargetBoardLayout
    {
        // Input/output are screen-independent canvas units. Missing data never enters layout.
        public static HudBounds Place(HudBounds safe, float width, float height, int corner, float insetX, float insetY, float topInset)
        {
            if (!Finite(safe.X) || !Finite(safe.Y) || !Finite(safe.Width) || !Finite(safe.Height) || !Finite(width) || !Finite(height) ||
                !Finite(insetX) || !Finite(insetY) || !Finite(topInset) || width <= 0 || height <= 0 || safe.Width < width + 32 || safe.Height < height + 32) return default;
            float minX = safe.X + 16, maxX = safe.X + safe.Width - width - 16;
            float minY = safe.Y + 16, maxY = safe.Y + safe.Height - height - 16;
            bool left = corner == 1 || corner == 3, top = corner >= 2;
            return new HudBounds
            {
                X = Clamp(left ? minX + insetX : maxX - insetX, minX, maxX),
                Y = Clamp(top ? maxY - topInset - insetY : minY + insetY, minY, maxY), Width = width, Height = height
            };
        }
        private static float Clamp(float value, float min, float max) => Math.Max(min, Math.Min(max, value));
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
