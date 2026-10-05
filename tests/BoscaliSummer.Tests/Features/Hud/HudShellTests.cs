using BoscaliSummer.Modules.Hud.Domain;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>
    /// What is left of the pre-2026-09-28 FUI shell tests: the channel-mute parser is the one
    /// piece of that Domain surface the minimal native-parented status panel still uses. The
    /// declutter matrix, field/keep-out geometry, stack placement, hide ledger and frame-timing
    /// ring all tested Domain types retired with the FUI shell.
    /// </summary>
    internal static class HudShellTests
    {
        public static void Run()
        {
            Channels();
        }

        private static void Channels()
        {
            ChannelFilter filter = new ChannelFilter();
            filter.Parse("fuel, Weather,,radio");

            TestAssert.That(!filter.Enabled("weather"),
                "The historic 'Weather' token must mute the renamed 'weather' channel");
            TestAssert.That(!filter.Enabled("fuel"), "A parsed channel must read as disabled");
            TestAssert.That(filter.Enabled("support"), "An unparsed channel must read as enabled");
            TestAssert.That(filter.Enabled(null) && filter.Enabled(""),
                "A blank channel key must always read as enabled");

            string disabledSupport = filter.With("support", false);
            TestAssert.That(disabledSupport.EndsWith(",support"),
                "Disabling a new channel must append it to the CSV");
            TestAssert.That(filter.Enabled("support"), "With must not mutate the filter's own state");

            string enabledFuel = filter.With("fuel", true);
            TestAssert.That(!enabledFuel.Contains("fuel"),
                "Re-enabling a channel must drop it from the CSV");
        }
    }
}
