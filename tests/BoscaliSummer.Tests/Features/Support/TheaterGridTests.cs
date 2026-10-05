using System.Globalization;
using BoscaliSummer.Modules.Support.Domain;

namespace BoscaliSummer.Tests.Features.Support
{
    internal static class TheaterGridTests
    {
        public static void Run()
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
                TestAssert.That(TheaterGrid.Kilometres(12400.0, -3100.0) == "12.4 / -3.1 KM",
                    "grid uses invariant kilometres even when the player's locale uses a decimal comma");
                TestAssert.That(TheaterGrid.Km(100000.0) == "100" && TheaterGrid.Km(-100000.0) == "-100",
                    "large map distances use whole kilometres");
                TestAssert.That(TheaterGrid.Km(double.NaN) == "—" && TheaterGrid.Km(double.PositiveInfinity) == "—",
                    "unknown coordinates never print non-finite values");
                TestAssert.That(TheaterGrid.Clock(125.4) == "02:05" && TheaterGrid.Clock(3725.0) == "1:02:05",
                    "countdowns use minutes and then hours");
                TestAssert.That(TheaterGrid.Clock(3599.99) == "59:59" && TheaterGrid.Clock(3600) == "1:00:00",
                    "clock changes format at exactly one hour and floors fractional seconds");
                TestAssert.That(TheaterGrid.Elapsed(393856.0) == "109:24:16" && TheaterGrid.Elapsed(0) == "000:00:00",
                    "elapsed mission time uses the three-digit hour format");
                foreach (double bad in new[] { -1.0, double.NaN, double.PositiveInfinity })
                    TestAssert.That(TheaterGrid.Clock(bad) == "--:--" && TheaterGrid.Elapsed(bad) == "---:--:--",
                        "invalid clocks show an unavailable value");
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
    }
}
