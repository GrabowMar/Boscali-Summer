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
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }
    }
}
