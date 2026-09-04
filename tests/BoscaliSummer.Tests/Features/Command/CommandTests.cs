using BoscaliSummer.Features.Command.Domain;
using NOAvionics.Tests;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class CommandTests
    {
        public static void Run()
        {
            AvionicsProtocolTests.Run(TestAssert.That);
            AvionicsTokenTests.Run(TestAssert.That);
            AvBoxTests.Run(TestAssert.That);
            AvGridTests.Run(TestAssert.That);
            AvStyleTests.Run(TestAssert.That);

            TestAssert.That(CommandDoctrineHelper.CanSetDoctrine(0),
                "Doctrine is host-always-on; vanilla rank is not a lock");
            TestAssert.That(CommandDoctrineHelper.MaxPriorityTargets(0) == 3,
                "Priority marks are not rank-gated");
            TestAssert.That(!CommandDoctrineHelper.CanOrderSectorStrike(5),
                "Unwired sector strike stays unavailable");
            TestAssert.That(!CommandDoctrineHelper.CanOrderScramble(5),
                "Unwired scramble stays unavailable");

            CommandDoctrine[] doctrines = (CommandDoctrine[])System.Enum.GetValues(typeof(CommandDoctrine));
            TestAssert.That(doctrines.Length == 5, "Must have exactly 5 strategic doctrines");
            for (int i = 0; i < doctrines.Length; i++)
            {
                TestAssert.That(!string.IsNullOrEmpty(CommandDoctrineHelper.GetName(doctrines[i])),
                    "Doctrine name must not be empty for " + doctrines[i]);
                TestAssert.That(!string.IsNullOrEmpty(CommandDoctrineHelper.GetDescription(doctrines[i])),
                    "Doctrine description must not be empty for " + doctrines[i]);
            }
        }
    }
}
