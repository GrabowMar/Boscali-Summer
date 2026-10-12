using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>Slice 4a holds D: the capture key is Dynamic (TapeMoves defaults to true) but the field is built with moving:false, so every manager re-key keeps the offset as it stands.</summary>
    internal static class TapeHeldTests
    {
        public static void Run()
        {
            var key = new WeatherKey(5u, 0f, true, 2, offset: 300000f, systemSpeed: 40f);
            WeatherKey hold = TapeSwitch.Held(key, TapeSwitch.Plan(key, false, 1800f, 40f).Key);
            WeatherKey resume = TapeSwitch.Held(hold, TapeSwitch.Plan(hold, true, 3600f, 40f).Key);
            WeatherKey paced = TapeSwitch.Held(resume, TapeSwitch.Pace(resume, 5400f, 45f));
            WeatherKey reset = TapeSwitch.Held(paced, paced.AfterClockReset(7000.0, 100f));
            foreach (WeatherKey k in new[] { key, hold, resume, paced, reset })
            {
                var field = new WeatherField();
                field.Build(k, 9000f, 81920f, 40960f);
                TestAssert.That(field.Frame.Offset == 300000f, "the held field stays at the offset the opening gave after Hold, Resume, Pace and a clock reset");
            }
            TestAssert.That(key.Rebased(3600f, false).Offset != 300000f, "a plain Rebased of a Dynamic key moves the offset: the reason for Held");
            TestAssert.That(!hold.Dynamic && resume.Dynamic && paced.SystemSpeed == 45f, "Held changes the offset only: the hold state and the speed go through");
        }
    }
}
