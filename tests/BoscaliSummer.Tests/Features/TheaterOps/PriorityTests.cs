using BoscaliSummer.Features.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    internal static class PriorityTests
    {
        public static void Run()
        {
            var table = new PriorityTable();
            var directive = new PriorityDirective("obj-north", "Northern Corridor", 1200f, 30f, -800f);

            TestAssert.That(!table.TryGet("Coalition", out _), "an unset faction has no priority");
            TestAssert.That(table.TrySet("Coalition", directive), "a valid directive is stored");
            TestAssert.That(
                table.TryGet("Coalition", out PriorityDirective stored) &&
                stored.Key == "obj-north" && stored.Label == "Northern Corridor" &&
                stored.X == 1200f && stored.Y == 30f && stored.Z == -800f,
                "a stored directive round-trips");
            TestAssert.That(!table.TryGet("PALA", out _), "a priority is faction scoped");

            TestAssert.That(!table.TrySet("", directive), "an empty faction is rejected");
            TestAssert.That(!table.TrySet("Coalition", default(PriorityDirective)),
                "a default directive is rejected");
            TestAssert.That(
                !table.TrySet("PALA", new PriorityDirective("k", "l", float.NaN, 0f, 0f)),
                "a non-finite position is rejected");
            TestAssert.That(
                !table.TrySet("PALA", new PriorityDirective("", "l", 1f, 2f, 3f)),
                "an empty objective key is rejected");

            string longKey = new string('k', PriorityDirective.MaximumKeyLength + 20);
            string longLabel = new string('l', PriorityDirective.MaximumLabelLength + 20);
            PriorityDirective bounded = new PriorityDirective(longKey, longLabel, 0f, 0f, 0f);
            TestAssert.That(
                bounded.Key.Length == PriorityDirective.MaximumKeyLength &&
                bounded.Label.Length == PriorityDirective.MaximumLabelLength,
                "identity and label are bounded to the table's ceiling");

            for (int i = 1; i < PriorityTable.MaximumFactions; i++)
                TestAssert.That(table.TrySet("faction-" + i, directive),
                    "faction " + i + " fits under the ceiling");
            TestAssert.That(table.Count == PriorityTable.MaximumFactions,
                "the table holds exactly the faction ceiling");
            TestAssert.That(!table.TrySet("faction-overflow", directive),
                "a ninth faction is rejected instead of growing the table");
            TestAssert.That(
                table.TrySet("faction-1", new PriorityDirective("obj-south", "Southern Reach", 5f, 6f, 7f)),
                "an existing faction updates at the ceiling");

            TestAssert.That(table.TryClear("faction-1"), "a set faction clears");
            TestAssert.That(!table.TryClear("faction-1"), "clearing twice is not a success");
            table.Clear();
            TestAssert.That(table.Count == 0, "scene reset drops every faction");
        }
    }
}
