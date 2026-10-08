using System;

namespace BoscaliSummer.Tests
{
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

        public static void Throws<TException>(Action action, string message)
            where TException : Exception
        {
            Count++;
            try
            {
                action();
            }
            catch (TException)
            {
                return;
            }
            throw new InvalidOperationException(message);
        }
    }
}
