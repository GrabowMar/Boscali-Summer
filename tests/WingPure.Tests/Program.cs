using System;
using BoscaliSummer.Tests;
using System.Linq;
using System.Reflection;

namespace WingPure.Tests
{
    /// <summary>Runs every static Run() in a *Tests type, optionally filtered by name (same pattern as BoscaliSummer.Tests).</summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
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
            Console.WriteLine($"WingPure.Tests: {suites.Length - failed} passed, {failed} failed; {TestAssert.Count} assertions.");
            return failed == 0 ? 0 : 1;
        }
    }
}
