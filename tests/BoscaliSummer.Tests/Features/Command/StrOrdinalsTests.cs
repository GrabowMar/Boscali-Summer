using System;
using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>First-seen platform and sortie numbers (annex 05 P2 test 18).</summary>
    internal static class StrOrdinalsTests
    {
        public static void Run()
        {
            NumbersAreAssignedOnFirstSight();
            TheTableHoldsNinetyNineAndThenSaysZero();
            ResetStartsOver();
            TagsAreLabels();
            SteadyLookupsAllocateNothing();
        }

        private static void NumbersAreAssignedOnFirstSight()
        {
            var p = new StrOrdinals("P");
            TestAssert.That(p.Of("@3,-2") == 1 && p.Of("#base") == 2 && p.Of("@3,-2") == 1, "a key keeps the number it was given first");
            TestAssert.That(p.Of("") == 3 && p.Of("") == 3, "an empty key is still a key");
            TestAssert.That(p.Of((string)null) == 0, "no key, no number, and nothing is consumed");
            TestAssert.That(p.Of("x") == 4, "a null key consumed no number");
            var s = new StrOrdinals("S");
            TestAssert.That(s.Of(77) == 1 && s.Of(12) == 2 && s.Of(77) == 1, "integer keys number the same way");
            TestAssert.That(s.Of("77") == 3, "a text key and an integer key never share a number");
        }

        private static void TheTableHoldsNinetyNineAndThenSaysZero()
        {
            var p = new StrOrdinals("P");
            for (int i = 1; i <= StrOrdinals.Max; i++) TestAssert.That(p.Of("k" + i) == i, "key " + i + " gets number " + i);
            TestAssert.That(p.Of("k100") == 0 && p.Of("k100") == 0, "number 100 is not given out, and asking again does not change that");
            TestAssert.That(p.Of("k7") == 7, "a key already in the table still answers when the table is full");
            var s = new StrOrdinals("S");
            for (int i = 1; i <= StrOrdinals.Max; i++) s.Of(i + 1000);
            TestAssert.That(s.Of(5) == 0 && s.Of(1001) == 1, "the integer table is bounded the same way");
        }

        private static void ResetStartsOver()
        {
            var p = new StrOrdinals("P");
            p.Of("a"); p.Of("b");
            p.Reset();
            TestAssert.That(p.Of("b") == 1 && p.Of("a") == 2, "after a reset the numbering restarts at 1");
        }

        private static void TagsAreLabels()
        {
            var p = new StrOrdinals("P");
            TestAssert.That(p.Tag(2) == "P2" && p.Tag(99) == "P99", "a tag is the prefix and the number");
            TestAssert.That(p.Tag(0) == "" && p.Tag(100) == "" && p.Tag(-1) == "", "no number, no tag");
        }

        private static void SteadyLookupsAllocateNothing()
        {
            var p = new StrOrdinals("P");
            p.Of("a"); p.Of(5); p.Tag(1);
            long before = GC.GetAllocatedBytesForCurrentThread();
            int sum = 0;
            for (int i = 0; i < 100; i++) sum += p.Of("a") + p.Of(5) + p.Tag(1).Length;
            long after = GC.GetAllocatedBytesForCurrentThread();
            TestAssert.That(sum > 0 && after - before == 0, "looking up a known key and its tag allocates nothing");
        }
    }
}
