using System;

namespace NOAvionics.Tests
{
    public static class AvGridTests
    {
        public static void Run(Action<bool, string> assert)
        {
            if (assert == null) throw new ArgumentNullException(nameof(assert));

            TestSnapRoundsToModule(assert);
            TestSnapRectIsStable(assert);
            TestResolvedEdgesAreMultiplesOfModule(assert);
            TestRegionsNeverOverlap(assert);
            TestReservesAreRespected(assert);
            TestConstrainedCanvasDegradesGracefully(assert);
            TestWidePanelExpandsColumn(assert);
        }

        private static void Near(Action<bool, string> assert, float actual, float expected, string message)
        {
            assert(Math.Abs(actual - expected) < 0.001f,
                   $"{message}: expected {expected:F2}, got {actual:F2}");
        }

        private static void TestSnapRoundsToModule(Action<bool, string> assert)
        {
            Near(assert, AvGrid.Snap(0f, 8f), 0f, "0 snaps to 0");
            Near(assert, AvGrid.Snap(4f, 8f), 8f, "4 snaps to 8");
            Near(assert, AvGrid.Snap(7.9f, 8f), 8f, "7.9 snaps to 8");
            Near(assert, AvGrid.Snap(11.9f, 8f), 8f, "11.9 snaps to 8");
            Near(assert, AvGrid.Snap(12.0f, 8f), 16f, "12.0 snaps to 16");
            Near(assert, AvGrid.Snap(-4f, 8f), -8f, "-4 snaps to -8");
            Near(assert, AvGrid.Snap(-8.1f, 8f), -8f, "-8.1 snaps to -8");
            Near(assert, AvGrid.Snap(-12.1f, 8f), -16f, "-12.1 snaps to -16");
        }

        private static void TestSnapRectIsStable(Action<bool, string> assert)
        {
            var r = new AvRect(13.4f, 55.1f, 102.7f, 89.3f);
            var s1 = AvGrid.SnapRect(r, 8f);
            var s2 = AvGrid.SnapRect(s1, 8f);

            Near(assert, s1.X, s2.X, "X is stable across snaps");
            Near(assert, s1.Y, s2.Y, "Y is stable across snaps");
            Near(assert, s1.Width, s2.Width, "Width is stable across snaps");
            Near(assert, s1.Height, s2.Height, "Height is stable across snaps");
            Near(assert, s1.Right, s2.Right, "Right is stable across snaps");
            Near(assert, s1.Bottom, s2.Bottom, "Bottom is stable across snaps");
        }

        private static void TestResolvedEdgesAreMultiplesOfModule(Action<bool, string> assert)
        {
            float[] canvasWidths = { 1920f, 2560f, 1366f, 1280f, 3440f };
            float[] canvasHeights = { 1080f, 1440f, 768f, 720f, 1440f };
            var spec = AvGridSpec.Default;

            for (int i = 0; i < canvasWidths.Length; i++)
            {
                var regions = AvGrid.Resolve(canvasWidths[i], canvasHeights[i], 470f, 96f, spec);

                void CheckRect(AvRect r, string name)
                {
                    assert(Math.Abs(r.X % spec.Module) < 0.001f, $"{name}.X ({r.X}) must be multiple of {spec.Module}");
                    assert(Math.Abs(r.Right % spec.Module) < 0.001f, $"{name}.Right ({r.Right}) must be multiple of {spec.Module}");
                    assert(Math.Abs(r.Y % spec.Module) < 0.001f, $"{name}.Y ({r.Y}) must be multiple of {spec.Module}");
                    assert(Math.Abs(r.Bottom % spec.Module) < 0.001f, $"{name}.Bottom ({r.Bottom}) must be multiple of {spec.Module}");
                    assert(Math.Abs(r.Width % spec.Module) < 0.001f, $"{name}.Width ({r.Width}) must be multiple of {spec.Module}");
                    assert(Math.Abs(r.Height % spec.Module) < 0.001f, $"{name}.Height ({r.Height}) must be multiple of {spec.Module}");
                }

                CheckRect(regions.Panel, $"Panel[{i}]");
                CheckRect(regions.Map, $"Map[{i}]");
                CheckRect(regions.Rail, $"Rail[{i}]");
                CheckRect(regions.Content, $"Content[{i}]");
            }
        }

        private static void TestRegionsNeverOverlap(Action<bool, string> assert)
        {
            var spec = AvGridSpec.Default;
            var regions = AvGrid.Resolve(1920f, 1080f, 470f, 96f, spec);

            assert(regions.Panel.Right <= regions.Map.X,
                   $"Panel right ({regions.Panel.Right}) must not overlap Map left ({regions.Map.X})");
            assert(regions.Map.Right <= regions.Rail.X,
                   $"Map right ({regions.Map.Right}) must not overlap Rail left ({regions.Rail.X})");
            assert(regions.Panel.X >= regions.Content.X,
                   "Panel must sit inside content left");
            assert(regions.Rail.Right <= regions.Content.Right,
                   "Rail must sit inside content right");
        }

        private static void TestReservesAreRespected(Action<bool, string> assert)
        {
            float canvasH = 1080f;
            float halfH = canvasH * 0.5f;
            var spec = AvGridSpec.Default;
            spec.TopReserve = 26f;
            spec.BottomReserve = 120f;
            var regions = AvGrid.Resolve(1920f, canvasH, 470f, 96f, spec);

            assert(regions.Panel.Y <= halfH - spec.TopReserve,
                   $"Panel top ({regions.Panel.Y}) must clear top reserve ({halfH - spec.TopReserve})");
            assert(regions.Panel.Bottom >= -halfH + spec.BottomReserve,
                   $"Panel bottom ({regions.Panel.Bottom}) must clear bottom reserve ({-halfH + spec.BottomReserve})");

            assert(regions.Map.Y <= halfH - spec.TopReserve,
                   "Map top must clear top reserve");
            assert(regions.Map.Bottom >= -halfH + spec.BottomReserve,
                   "Map bottom must clear bottom reserve");

            assert(regions.Rail.Y <= halfH - spec.TopReserve,
                   "Rail top must clear top reserve");
            assert(regions.Rail.Bottom >= -halfH + spec.BottomReserve,
                   "Rail bottom must clear bottom reserve");
        }

        private static void TestConstrainedCanvasDegradesGracefully(Action<bool, string> assert)
        {
            var spec = AvGridSpec.Default;
            var regions = AvGrid.Resolve(300f, 200f, 470f, 96f, spec);

            assert(regions.Panel.Width >= 0f, "Panel width never negative");
            assert(regions.Panel.Height >= 0f, "Panel height never negative");
            assert(regions.Map.Width >= 0f, "Map width never negative");
            assert(regions.Map.Height >= 0f, "Map height never negative");
            assert(regions.Rail.Width >= 0f, "Rail width never negative");
            assert(regions.Rail.Height >= 0f, "Rail height never negative");
        }

        private static void TestWidePanelExpandsColumn(Action<bool, string> assert)
        {
            var spec = AvGridSpec.Default;
            var narrow = AvGrid.Resolve(1920f, 1080f, 470f, 96f, spec);
            var wide = AvGrid.Resolve(1920f, 1080f, 560f, 96f, spec);

            assert(wide.Panel.Width > narrow.Panel.Width, "Wide panel width is larger");
            assert(wide.Map.X > narrow.Map.X, "Map moves right when panel widens");
            assert(wide.Map.Width < narrow.Map.Width, "Map shrinks when panel widens");
            assert(wide.Panel.Right <= wide.Map.X, "Wide panel does not overlap map");
        }
    }
}
