using BoscaliSummer.Features.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    internal static class StaffLogTests
    {
        public static void Run()
        {
            NewestFirstBoundedAndCut();
            ARepeatedLineIsNotLoggedTwice();
        }

        private static void ARepeatedLineIsNotLoggedTwice()
        {
            var log = new StaffLog();
            TestAssert.That(log.Add("DEFENDING AZALEA"), "a first line lands");
            TestAssert.That(!log.Add("DEFENDING AZALEA"), "an identical newest line is refused");
            TestAssert.That(log.Count == 1, "the refusal leaves the ring alone");
            TestAssert.That(log.Add("OPENING OUTPOST: 2 WAVES"), "a new line still lands");
            TestAssert.That(log.Add("DEFENDING AZALEA"), "a non-consecutive repeat is news again");
        }

        private static void NewestFirstBoundedAndCut()
        {
            var log = new StaffLog();
            TestAssert.That(log.Count == 0, "a new log is empty");
            log.Add("");
            log.Add(null);
            TestAssert.That(log.Count == 0, "emptiness is never logged");
            for (int i = 0; i < StaffLog.MaximumEntries + 3; i++) log.Add("LINE " + i);
            TestAssert.That(log.Count == StaffLog.MaximumEntries, "the ring stops at its ceiling");
            TestAssert.That(log.Entries[0] == "LINE " + (StaffLog.MaximumEntries + 2), "the newest line reads first");
            log.Add(new string('X', StaffLog.MaximumTextLength + 20));
            TestAssert.That(log.Entries[0].Length == StaffLog.MaximumTextLength, "a long line is cut, never wrapped");
            log.Clear();
            TestAssert.That(log.Count == 0, "clearing empties the ring");
        }
    }
}
