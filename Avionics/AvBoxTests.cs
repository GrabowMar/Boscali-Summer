using System;

namespace NOAvionics.Tests
{
    /// <summary>
    /// Engine-free geometry tests for the <see cref="AvNode"/> layout pass.
    ///
    /// These exist because the two failures this layer was built to end — text clipped
    /// because its box was a fixed height, and dead space because nothing claimed the
    /// remainder — are both arithmetic, and arithmetic that was previously only checkable
    /// by launching a mission and looking at a panel. Both mods run this suite.
    /// </summary>
    public static class AvBoxTests
    {
        /// <summary>The panel geometry the real screens use, so the numbers here mean something.</summary>
        private static AvRect Panel =>
            new AvRect(0f, 0f, AvTokens.PanelWidth, AvTokens.PanelHeight);

        /// <summary>A stub text engine: every leaf is 20px tall and 60px wide.</summary>
        private static float Stub(AvNode node, float available) => available <= 0f ? 60f : 20f;

        public static void Run(Action<bool, string> assert)
        {
            if (assert == null) throw new ArgumentNullException(nameof(assert));

            TestFixedStack(assert);
            TestGrowAbsorbsRemainder(assert);
            TestGrowSplitsByWeight(assert);
            TestAutoFollowsContent(assert);
            TestGapsCountOnlyBetween(assert);
            TestPaddingInsets(assert);
            TestRowDistributesWidth(assert);
            TestGridWrapsAndAlignsRows(assert);
            TestNestingComposes(assert);
            TestOverflowClampsRatherThanInverts(assert);
            TestLookupByPath(assert);
            TestEmptyContainerIsHarmless(assert);
        }

        private static void TestFixedStack(Action<bool, string> assert)
        {
            AvNode root = AvLayout.Column("root").Gaps(0f)
                .Add(AvLayout.Cell("a").Height(30f))
                .Add(AvLayout.Cell("b").Height(58f))
                .Add(AvLayout.Cell("c").Height(44f));
            root.Arrange(Panel, Stub);

            Near(assert, root["a"].Y, 0f, "first child starts at the top");
            Near(assert, root["b"].Y, -30f, "second child starts below the first");
            Near(assert, root["c"].Y, -88f, "third child accumulates both siblings");
            Near(assert, root["b"].Height, 58f, "a fixed child keeps its stated height");
            Near(assert, root["a"].Width, AvTokens.PanelWidth, "a column child fills the cross axis");
        }

        private static void TestGrowAbsorbsRemainder(Action<bool, string> assert)
        {
            // The dead-space case: a short page must stretch, not leave a void.
            AvNode root = AvLayout.Column("root").Gaps(0f)
                .Add(AvLayout.Cell("bar").Height(30f))
                .Add(AvLayout.Cell("body").Grow())
                .Add(AvLayout.Cell("strip").Height(44f));
            root.Arrange(Panel, Stub);

            float expected = AvTokens.PanelHeight - 30f - 44f;
            Near(assert, root["body"].Height, expected, "grow takes everything the fixed rows left");
            Near(assert, root["strip"].Bottom, -AvTokens.PanelHeight,
                 "the pinned strip lands exactly on the panel's bottom edge");
        }

        private static void TestGrowSplitsByWeight(Action<bool, string> assert)
        {
            AvNode root = AvLayout.Column("root").Gaps(0f)
                .Add(AvLayout.Cell("one").Grow(1f))
                .Add(AvLayout.Cell("three").Grow(3f));
            root.Arrange(new AvRect(0f, 0f, 100f, 400f), Stub);

            Near(assert, root["one"].Height, 100f, "weight 1 of 4 takes a quarter");
            Near(assert, root["three"].Height, 300f, "weight 3 of 4 takes three quarters");
        }

        private static void TestAutoFollowsContent(Action<bool, string> assert)
        {
            // The truncation case: a row is as tall as its copy, not a hardcoded 40f.
            AvNode tall = AvLayout.Cell("tall");
            AvNode root = AvLayout.Column("root").Gaps(0f).Add(tall);
            root.Arrange(Panel, (node, avail) => avail <= 0f ? 60f : 137f);

            Near(assert, root["tall"].Height, 137f, "an auto leaf is exactly its measured content");

            // And it must include its own padding, or padded text overflows its box.
            AvNode padded = AvLayout.Column("root2").Gaps(0f)
                .Add(AvLayout.Cell("p").Pad(6f, 4f, 6f, 4f));
            padded.Arrange(Panel, Stub);
            Near(assert, padded["p"].Height, 28f, "an auto leaf adds its vertical padding to its content");
        }

        private static void TestGapsCountOnlyBetween(Action<bool, string> assert)
        {
            AvNode root = AvLayout.Column("root").Gaps(10f)
                .Add(AvLayout.Cell("a").Height(20f))
                .Add(AvLayout.Cell("b").Height(20f))
                .Add(AvLayout.Cell("c").Grow());
            root.Arrange(new AvRect(0f, 0f, 100f, 100f), Stub);

            Near(assert, root["b"].Y, -30f, "one gap sits between the first and second child");
            // 100 total - 20 - 20 - two gaps of 10 = 40.
            Near(assert, root["c"].Height, 40f, "gaps are charged before grow is distributed");
            Near(assert, root["c"].Bottom, -100f, "the last child still reaches the bottom edge");
        }

        private static void TestPaddingInsets(Action<bool, string> assert)
        {
            AvNode root = AvLayout.Column("root").Pad(14f).Gaps(0f)
                .Add(AvLayout.Cell("a").Grow());
            root.Arrange(new AvRect(0f, 0f, 470f, 200f), Stub);

            Near(assert, root["a"].X, 14f, "padding moves the child right");
            Near(assert, root["a"].Y, -14f, "padding moves the child down");
            Near(assert, root["a"].Width, 442f, "padding narrows the child on both sides");
            Near(assert, root["a"].Height, 172f, "padding shortens the child top and bottom");
        }

        private static void TestRowDistributesWidth(Action<bool, string> assert)
        {
            AvNode root = AvLayout.Row("bar").Gaps(0f).Height(30f)
                .Add(AvLayout.Cell("id").Width(60f))
                .Add(AvLayout.Cell("state").Grow())
                .Add(AvLayout.Cell("chips").Width(140f));
            AvLayout.Column("outer").Gaps(0f).Add(root).Arrange(new AvRect(0f, 0f, 470f, 300f), Stub);

            Near(assert, root["id"].Width, 60f, "a fixed cell keeps its width in a row");
            Near(assert, root["state"].Width, 270f, "the grow cell takes the middle");
            Near(assert, root["chips"].X, 330f, "later cells start after their predecessors");
            Near(assert, root["state"].Height, 30f, "a row child fills the row height");
        }

        private static void TestGridWrapsAndAlignsRows(Action<bool, string> assert)
        {
            AvNode grid = AvLayout.Grid("g", 3).Gaps(4f);
            for (int i = 0; i < 5; i++) grid.Add(AvLayout.Cell("c" + i).Height(20f));
            AvLayout.Column("outer").Gaps(0f).Add(grid).Arrange(new AvRect(0f, 0f, 308f, 300f), Stub);

            // (308 - 2 gaps of 4) / 3 = 100.
            Near(assert, grid["c0"].Width, 100f, "grid cells split the width evenly");
            Near(assert, grid["c1"].X, 104f, "the second column clears the first plus a gap");
            Near(assert, grid["c0"].Y, 0f, "the first grid row starts at the top");
            Near(assert, grid["c3"].Y, -24f, "the fourth item wraps onto the second row");
            Near(assert, grid["c3"].X, 0f, "a wrapped item returns to the first column");
            Near(assert, grid["c4"].X, 104f, "a partial last row still steps across columns");
        }

        private static void TestNestingComposes(Action<bool, string> assert)
        {
            AvNode root = AvLayout.Column("panel").Pad(14f).Gaps(8f)
                .Add(AvLayout.Row("bar").Height(30f)
                    .Add(AvLayout.Cell("id").Width(50f))
                    .Add(AvLayout.Cell("rest").Grow()))
                .Add(AvLayout.Stack("body").Grow()
                    .Add(AvLayout.Cell("one").Height(40f))
                    .Add(AvLayout.Cell("two").Grow()));
            root.Arrange(new AvRect(0f, 0f, 470f, 300f), Stub);

            Near(assert, root["id"].X, 14f, "a nested row inherits its parent's padding");
            Near(assert, root["rest"].Width, 392f, "442 inner minus the 50px id cell");
            // 300 - 28 padding - 30 bar - 8 gap = 234.
            Near(assert, root["body"].Height, 234f, "the body grows inside the padded column");
            Near(assert, root["two"].Height, 194f, "grow nests: the inner body absorbs its own remainder");
            Near(assert, root["two"].Bottom, root["body"].Bottom,
                 "the innermost grow child reaches its parent's bottom");
        }

        private static void TestOverflowClampsRatherThanInverts(Action<bool, string> assert)
        {
            // More fixed content than the panel holds. It must overflow downward, never
            // produce a negative height that flips a widget inside out.
            AvNode root = AvLayout.Column("root").Gaps(0f)
                .Add(AvLayout.Cell("a").Height(200f))
                .Add(AvLayout.Cell("b").Height(200f))
                .Add(AvLayout.Cell("c").Grow());
            root.Arrange(new AvRect(0f, 0f, 100f, 300f), Stub);

            assert(root["c"].Height >= 0f, "an over-subscribed grow child clamps to zero, never negative");
            Near(assert, root["b"].Y, -200f, "overflowing children still stack in order");

            AvRect squeezed = new AvRect(0f, 0f, 10f, 10f).Inset(20f, 20f, 20f, 20f);
            assert(squeezed.Width >= 0f && squeezed.Height >= 0f,
                   "padding larger than the box clamps to zero rather than inverting");
        }

        private static void TestLookupByPath(Action<bool, string> assert)
        {
            AvNode root = AvLayout.Column("panel").Gaps(0f)
                .Add(AvLayout.Stack("strikes").Gaps(0f)
                    .Add(AvLayout.Row("row0").Height(24f)
                        .Add(AvLayout.Cell("name").Grow()))
                    .Add(AvLayout.Row("row1").Height(24f)
                        .Add(AvLayout.Cell("name").Grow())));
            root.Arrange(Panel, Stub);

            Near(assert, root["strikes.row1.name"].Y, -24f, "a dotted path resolves through the tree");
            assert(root.Find("strikes.row1") != null, "Find returns the node itself");
            assert(root.Find("nope.at.all") == null, "a missing path resolves to null, not an exception");

            AvRect missing = root["nope"];
            assert(missing.Width == 0f && missing.Height == 0f,
                   "an unknown path yields an empty rect rather than throwing");
        }

        private static void TestEmptyContainerIsHarmless(Action<bool, string> assert)
        {
            AvNode empty = AvLayout.Column("empty").Pad(8f).Gaps(6f);
            empty.Arrange(Panel, Stub);
            Near(assert, empty.Rect.Height, AvTokens.PanelHeight, "an empty container keeps the area it was given");

            AvNode grid = AvLayout.Grid("g", 0).Gaps(4f).Add(AvLayout.Cell("only").Height(10f));
            AvLayout.Column("o").Add(grid).Arrange(Panel, Stub);
            assert(grid["only"].Width > 0f, "a grid declared with zero columns falls back to one");
        }

        private static void Near(Action<bool, string> assert, float actual, float expected, string what)
        {
            assert(Math.Abs(actual - expected) < 0.01f,
                   what + " (expected " + expected.ToString("0.##") + ", got " + actual.ToString("0.##") + ")");
        }
    }
}
