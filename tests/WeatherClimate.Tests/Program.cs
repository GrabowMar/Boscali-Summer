using System;
using System.Linq;
using System.Reflection;

namespace WeatherClimateTests
{
    /// <summary>Runs every static Run() in a *Tests type, optionally filtered by name (the WingPure.Tests pattern).
    /// <c>--golden</c> prints the climate-0 pins of <see cref="ClimateTapeTests"/> instead.</summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--golden") { ClimateTapeTests.PrintGolden(); return 0; }
            var suites = typeof(Program).Assembly.GetTypes()
                .Where(t => t.Name.EndsWith("Tests", StringComparison.Ordinal))
                .Select(t => t.GetMethod("Run", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, Type.EmptyTypes, null))
                .Where(m => m != null && (args.Length == 0 || args.Any(f => m.DeclaringType.FullName.Contains(f, StringComparison.OrdinalIgnoreCase))))
                .OrderBy(m => m.DeclaringType.FullName).ToArray();
            if (suites.Length == 0) { Console.Error.WriteLine("No matching test suites."); return 2; }
            int failed = 0;
            foreach (MethodInfo suite in suites)
            {
                try { suite.Invoke(null, null); Console.WriteLine("PASS " + suite.DeclaringType.FullName); }
                catch (TargetInvocationException e) { failed++; Console.Error.WriteLine("FAIL " + suite.DeclaringType.FullName + ": " + e.InnerException.Message); }
            }
            Console.WriteLine($"WeatherClimate.Tests: {suites.Length - failed} passed, {failed} failed; {TestAssert.Count} assertions.");
            return failed == 0 ? 0 : 1;
        }
    }

    /// <summary>The asserts of BoscaliSummer.Tests, kept here so this project builds from a clean checkout.</summary>
    internal static class TestAssert
    {
        public static int Count { get; private set; }

        public static void That(bool condition, string message)
        {
            Count++;
            if (!condition) throw new InvalidOperationException(message);
        }

        public static void Eq<T>(T actual, T expected, string message) =>
            That(Equals(actual, expected), message + " (got " + actual + ", want " + expected + ")");

        public static void Near(float actual, float expected, string message) =>
            That(Math.Abs(actual - expected) < 0.01f, message + " (got " + actual + ", want " + expected + ")");
    }
}
