using System;
using System.Linq;
using System.Reflection;

namespace BoscaliSummer.Tests
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // Discover suite entry points so restored/new suites cannot silently go unrun.
            var suites = typeof(Program).Assembly.GetTypes()
                .Where(type => type.Namespace?.StartsWith("BoscaliSummer.Tests", StringComparison.Ordinal) == true ||
                               type.Namespace == "NOAvionics.Tests" ||
                               (type.Namespace == null && type.Name.EndsWith("Tests", StringComparison.Ordinal)))
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Where(method => method.Name == "Run" && method.ReturnType == typeof(void) &&
                    (method.GetParameters().Length == 0 ||
                     (method.GetParameters().Length == 1 && method.GetParameters()[0].ParameterType == typeof(Action<bool, string>))))
                .Where(method => args.Length == 0 || args.Any(filter => method.DeclaringType.FullName.Contains(filter, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(method => method.DeclaringType.FullName).ToArray();
            if (suites.Length == 0) { Console.Error.WriteLine("No matching test suites."); return 2; }
            int failed = 0;
            foreach (var suite in suites)
            {
                try
                {
                    suite.Invoke(null, suite.GetParameters().Length == 0 ? null : new object[] { (Action<bool, string>)TestAssert.That });
                    Console.WriteLine("PASS " + suite.DeclaringType.FullName);
                }
                catch (TargetInvocationException error)
                {
                    failed++;
                    Console.Error.WriteLine("FAIL " + suite.DeclaringType.FullName + ": " + error.InnerException);
                }
            }
            Console.WriteLine($"BoscaliSummer.Tests: {suites.Length - failed} passed, {failed} failed; {TestAssert.Count} assertions.");
            return failed == 0 ? 0 : 1;
        }
    }
}
