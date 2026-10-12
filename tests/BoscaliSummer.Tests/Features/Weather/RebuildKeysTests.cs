using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>Spec 9.2: rebuilds compare TapeId plus HeadingDeg; kinematic edits never rebuild the tape.</summary>
    internal static class RebuildKeysTests
    {
        public static void Run()
        {
            var key = new WeatherKey(7u, 0f, true, 2, headingDeg: 120f);
            var moved = new WeatherKey(7u, 900f, false, 2, 63, 0, true, 5000f, 6000f, headingDeg: 120f, offset: 300000f, systemSpeed: 40f);
            TestAssert.That(key.SameTape(moved) && key.TapeId == moved.TapeId, "Epoch, Offset, Dynamic, SystemSpeed, Sets and Anchor are kinematics");
            TestAssert.That(!key.SameTape(key.WithLayoutSalt(1)), "Re-roll rebuilds");
            TestAssert.That(!key.SameTape(new WeatherKey(7u, 0f, true, 2, headingDeg: 121f)), "a new frame heading rebuilds");
            TestAssert.That(!key.SameTape(key.WithSkyPin(3)) && !key.SameTape(key.WithStory(1)), "a pin or a story is a new tape");
            // The dressing and the cookie compare D beside SameTape: a held offset edit must be visible there.
            var field = new WeatherField(); field.Build(key, 600f, 40960f, 40960f, 12f);
            var shifted = new WeatherField(); shifted.Build(key.WithOffset(120000f), 600f, 40960f, 40960f, 12f);
            TestAssert.That(key.SameTape(key.WithOffset(120000f)) && field.Frame.Offset != shifted.Frame.Offset,
                "SameTape ignores D, and the field's frame offset carries it");
        }
    }
}
