using System;

namespace NOAvionics.Tests
{
    /// <summary>
    /// Engine-free WCAG AA contrast and state separation tests for AvionicsTokens.
    /// Executed by both Wing Command and Boscali Summer test runners without Unity.
    /// </summary>
    public static class AvionicsTokenTests
    {
        private static Rgba Accent => new Rgba(0.30f, 1f, 0.35f);
        private static Rgba Dim => AvTokens.TextDim;
        private static Rgba Disabled => AvTokens.TextMuted;

        private static Rgba GroundOverDarkMap =>
            AvTokens.Ground.Over(new Rgba(0.05f, 0.06f, 0.07f));

        private static Rgba GroundOverBrightMap =>
            AvTokens.Ground.Over(new Rgba(0.85f, 0.85f, 0.85f));

        public static void Run(Action<bool, string> assert)
        {
            if (assert == null) throw new ArgumentNullException(nameof(assert));

            TestBodyText(assert);
            TestDisabledText(assert);
            TestGroundDrift(assert);
            TestHudToast(assert);
            TestSecondaryTextOnSurfaces(assert);
        }

        private static void TestSecondaryTextOnSurfaces(Action<bool, string> assert)
        {
            foreach (Rgba surface in new[] { AvTokens.Ground, AvTokens.Surface, AvTokens.SurfaceRaised })
            {
                float ratio = Rgba.Contrast(AvTokens.TextMuted, surface.Over(GroundOverBrightMap));
                assert(ratio >= 4.5f, $"secondary text is {ratio:F2}:1 on a shared surface; must be at least 4.5:1");
            }
        }

        private static void TestBodyText(Action<bool, string> assert)
        {
            var colours = new (string Name, Rgba Colour)[]
            {
                ("Dim", Dim),
                ("Accent", Accent),
                ("White", Rgba.White),
            };

            foreach ((string name, Rgba text) in colours)
            foreach (Rgba ground in new[] { GroundOverDarkMap, GroundOverBrightMap })
            {
                float ratio = Rgba.Contrast(text, ground);
                assert(ratio >= 4.5f,
                    $"{name} text measures {ratio:F2}:1 against panel ground; 4.5:1 is the floor.");
            }
        }

        private static void TestDisabledText(Action<bool, string> assert)
        {
            Rgba ground = GroundOverDarkMap;
            Rgba flattened = Disabled.Over(ground);

            float disabled = Rgba.Contrast(flattened, ground);
            float live = Rgba.Contrast(Dim, ground);

            assert(disabled >= 4.5f,
                $"disabled text at {disabled:F2}:1 misses the 4.5:1 readability floor");
            assert(disabled < live * 0.7f,
                $"disabled text at {disabled:F2}:1 is not clearly weaker than live text at {live:F2}:1");
        }

        private static void TestGroundDrift(Action<bool, string> assert)
        {
            float drift = Rgba.Contrast(GroundOverBrightMap, GroundOverDarkMap);
            assert(drift <= 1.6f, $"ground drift is {drift:F2}:1 (must be ≤1.6:1)");
        }

        private static void TestHudToast(Action<bool, string> assert)
        {
            Rgba flattened = AvTokens.HudPanel.Over(new Rgba(0f, 0f, 0f));
            float contrast = Rgba.Contrast(Rgba.White, flattened);
            assert(contrast >= 4.5f, $"toast text measures {contrast:F2}:1 on HUD panel");
        }
    }
}
