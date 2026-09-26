using BoscaliSummer.Features.Squad.Domain;

namespace BoscaliSummer.Tests.Features.Squad
{
    internal static class SnapshotRevisionTests
    {
        public static void Run()
        {
            EqualContentHasEqualRevision();
            EveryFieldChangesTheRevision();
            RevisionIsNeverZero();
        }

        private static SnapshotRevision Sample(int alive = 3, string status = "HUNTING", string target = "YOU")
        {
            var revision = new SnapshotRevision();
            revision.Add(7); revision.Add("CALDER"); revision.Add(true);
            revision.Add(status); revision.Add(alive); revision.Add(target);
            return revision;
        }

        private static void EqualContentHasEqualRevision()
        {
            TestAssert.That(Sample().Value == Sample().Value,
                "identical snapshots must answer the same revision so an idle poll stays tiny");
        }

        private static void EveryFieldChangesTheRevision()
        {
            uint baseline = Sample().Value;
            TestAssert.That(Sample(alive: 2).Value != baseline, "a lost wingman must change the revision");
            TestAssert.That(Sample(status: "ACE DEFEATED").Value != baseline, "a status change must change the revision");
            TestAssert.That(Sample(target: "").Value != baseline, "a cleared target must change the revision");

            var joined = new SnapshotRevision(); joined.Add("ab"); joined.Add("c");
            var split = new SnapshotRevision(); split.Add("a"); split.Add("bc");
            TestAssert.That(joined.Value != split.Value, "adjacent strings must not alias across a boundary");

            var nothing = new SnapshotRevision(); nothing.Add((string)null);
            var empty = new SnapshotRevision(); empty.Add("");
            TestAssert.That(nothing.Value == empty.Value, "null and empty text travel identically, so they share a revision");
        }

        private static void RevisionIsNeverZero()
        {
            // Zero is the client's "nothing applied yet" query value; a zero revision could never be refreshed.
            TestAssert.That(new SnapshotRevision().Value != 0u, "an empty snapshot must still have a non-zero revision");
            for (int i = 0; i < 20000; i++)
            {
                var revision = new SnapshotRevision(); revision.Add(i);
                TestAssert.That(revision.Value != 0u, "a revision must never be zero");
            }
        }
    }
}
