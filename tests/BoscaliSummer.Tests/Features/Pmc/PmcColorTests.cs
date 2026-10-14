using System.Globalization;
using System.Threading;
using BoscaliSummer.Modules.Pmc.Runtime;

namespace BoscaliSummer.Tests.Features.Pmc
{
    internal static class PmcColorTests
    {
        public static void Run()
        {
            CultureInfo before = Thread.CurrentThread.CurrentCulture;
            try
            {
                // A decimal-comma locale must not change how the setting reads.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("pl-PL");
                TestAssert.That(PmcColor.TryParse("0.2,0.8,0.2,1", out float r, out float g, out float b, out float a)
                    && r == 0.2f && g == 0.8f && b == 0.2f && a == 1f, "RGBA reads the same on a Polish Windows");
                TestAssert.That(PmcColor.TryParse(" 1, 0.5 ,0", out _, out _, out _, out float alpha) && alpha == 1f, "alpha defaults to 1, spaces are fine");
                TestAssert.That(!PmcColor.TryParse("green", out _, out _, out _, out _), "words are refused");
                TestAssert.That(!PmcColor.TryParse("1,2", out _, out _, out _, out _), "two numbers are refused");
            }
            finally { Thread.CurrentThread.CurrentCulture = before; }
        }
    }
}
