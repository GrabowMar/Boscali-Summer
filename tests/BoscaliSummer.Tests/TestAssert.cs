using System;

namespace BoscaliSummer.Tests
{
    internal static class TestAssert
    {
        public static void That(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        public static void Throws<TException>(Action action, string message)
            where TException : Exception
        {
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
