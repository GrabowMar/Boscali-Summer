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
