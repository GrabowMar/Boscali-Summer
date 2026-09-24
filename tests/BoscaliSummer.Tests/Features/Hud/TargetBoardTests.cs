using BoscaliSummer.Features.Hud.Domain;
using BoscaliSummer.Framework.Contracts;
namespace BoscaliSummer.Tests.Features.Hud
{
    internal static class TargetBoardTests
    {
        public static void Run()
        {
            foreach (float width in new[] { 1280f, 1920f, 2560f, 3440f })
            foreach (float height in new[] { 720f, 1080f, 1440f })
            for (int scale = 0; scale < 4; scale++)
            for (int corner = 0; corner < 4; corner++)
            for (int inset = 0; inset <= 600; inset += 200)
            {
                float factor = System.Math.Min(width / 1920, height / 1080) * HudLayout.Scale(scale);
                var safe = new HudBounds { X = 12 / factor, Y = 8 / factor, Width = (width - 24) / factor, Height = (height - 16) / factor };
                HudBounds p = TargetBoardLayout.Place(safe, 352, 416, corner, inset, inset, 160 / factor);
                if (!p.Visible) continue;
                TestAssert.That(p.X >= safe.X && p.Y >= safe.Y && p.X + p.Width <= safe.X + safe.Width && p.Y + p.Height <= safe.Y + safe.Height,
                    "The whole dock must fit its safe area at every corner and scale");
            }
            TestAssert.That(!TargetBoardLayout.Place(new HudBounds { Width = 300, Height = 300 }, 352, 416, 0, 0, 0, 0).Visible,
                "Unavailable space must not produce clipped instruments");
            TestAssert.That(!TargetBoardLayout.Place(new HudBounds { Width = 1920, Height = 1080 }, float.NaN, 416, 0, 0, 0, 0).Visible,
                "Invalid layout values must fail closed");
        }
    }
}
