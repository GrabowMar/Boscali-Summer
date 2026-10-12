using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The INFO tab's org chart: where each officer's card stands and which lines join it to the office it answers to.</summary>
    internal static class StrOrgLayoutTests
    {
        private const float Width = 458f, CardH = 72f, Gap = 8f, VGap = 24f, MaxW = 220f;

        public static void Run()
        {
            ASixOfficerStaffIsOneOverTwoOverThree();
            EveryShapeStaysInsideTheWidthAndNeverOverlaps();
            EveryChildIsJoinedToItsOwnParentAndToNoOtherOffice();
            AComponentWithNoBasesStandsAlone();
            AMissingOfficeIsAnEmptyCardInItsPlace();
            AnOfficerWhoseParentIsMissingHangsUnderTheRoot();
            ABadParentChainNeverLosesAnOfficer();
            NothingBuildsNothing();
        }

        private static StrOfficerRow Row(int id, int parent, int tier) => new StrOfficerRow { Id = id, ParentId = parent, Tier = tier };

        private static StrOrgPlan Build(params StrOfficerRow[] rows)
        {
            var plan = new StrOrgPlan();
            plan.Build(rows, rows.Length, Width, CardH, Gap, VGap, MaxW);
            return plan;
        }

        private static StrOfficerRow[] Six() => new[] { Row(0, -1, 0), Row(1, 0, 1), Row(2, 0, 1), Row(3, 2, 2), Row(4, 1, 2), Row(5, 2, 2) };

        private static int CardOf(StrOrgPlan p, int row)
        {
            for (int i = 0; i < p.CardCount; i++) if (p.Cards[i].Row == row) return i;
            return -1;
        }

        private static int Level(StrOrgPlan p, int depth)
        {
            int n = 0;
            for (int i = 0; i < p.CardCount; i++) if (System.Math.Abs(p.Cards[i].Y - depth * (CardH + VGap)) < 0.01f) n++;
            return n;
        }

        private static void ASixOfficerStaffIsOneOverTwoOverThree()
        {
            StrOrgPlan p = Build(Six());
            TestAssert.That(p.CardCount == 6, "six cards");
            TestAssert.That(Level(p, 0) == 1 && Level(p, 1) == 2 && Level(p, 2) == 3, "one over two over three");
            StrOrgCard root = p.Cards[CardOf(p, 0)];
            TestAssert.That(System.Math.Abs(root.X + root.W * 0.5f - Width * 0.5f) < 0.01f, "the theater commander is centred");
            TestAssert.That(System.Math.Abs(p.Height - (3 * CardH + 2 * VGap)) < 0.01f, "the chart is three cards and two gaps tall");
            StrOrgCard only = p.Cards[CardOf(p, 4)], air = p.Cards[CardOf(p, 1)];
            TestAssert.That(System.Math.Abs(only.X + only.W * 0.5f - (air.X + air.W * 0.5f)) < 0.01f, "an only child stands straight under its parent");
        }

        private static void EveryShapeStaysInsideTheWidthAndNeverOverlaps()
        {
            var seven = new[] { Row(0, -1, 0), Row(1, 0, 1), Row(2, 0, 1), Row(3, 1, 2), Row(4, 1, 2), Row(5, 2, 2), Row(6, 2, 2) };
            var one = new[] { Row(0, -1, 0) };
            var two = new[] { Row(0, -1, 0), Row(1, 0, 1) };
            var three = new[] { Row(0, -1, 0), Row(1, 0, 1), Row(2, 0, 1) };
            foreach (StrOfficerRow[] rows in new[] { one, two, three, Six(), seven })
                foreach (float w in new[] { 458f, 300f })
                {
                    var p = new StrOrgPlan();
                    p.Build(rows, rows.Length, w, CardH, Gap, VGap, MaxW);
                    TestAssert.That(p.CardCount == rows.Length, rows.Length + " officers, " + rows.Length + " cards");
                    for (int i = 0; i < p.CardCount; i++)
                    {
                        StrOrgCard c = p.Cards[i];
                        TestAssert.That(c.X >= -0.01f && c.X + c.W <= w + 0.01f && c.W > 0f && c.Y >= 0f && c.Y + c.H <= p.Height + 0.01f, "card " + i + " of " + rows.Length + " at " + w + " is inside the chart");
                        for (int j = i + 1; j < p.CardCount; j++)
                        {
                            StrOrgCard d = p.Cards[j];
                            bool apart = c.X + c.W <= d.X || d.X + d.W <= c.X || c.Y + c.H <= d.Y || d.Y + d.H <= c.Y;
                            TestAssert.That(apart, "cards " + i + " and " + j + " of " + rows.Length + " at " + w + " do not overlap");
                        }
                    }
                    for (int s = 0; s < p.SegCount; s++)
                        TestAssert.That(p.Segs[s].X >= -1f && p.Segs[s].X + p.Segs[s].W <= w + 1f, "line " + s + " stays inside the width");
                }
        }

        private static bool Touch(StrOrgSeg a, StrOrgSeg b) => Touch(a.X, a.Y, a.W, a.H, b.X, b.Y, b.W, b.H);

        private static bool Touch(float ax, float ay, float aw, float ah, float bx, float by, float bw, float bh) =>
            ax <= bx + bw + 0.01f && bx <= ax + aw + 0.01f && ay <= by + bh + 0.01f && by <= ay + ah + 0.01f;

        private static bool Touch(StrOrgSeg s, StrOrgCard c) => Touch(s.X, s.Y, s.W, s.H, c.X, c.Y, c.W, c.H);

        // The line coming into a card, followed through touching lines, reaches its parent and its siblings and no other card.
        private static void EveryChildIsJoinedToItsOwnParentAndToNoOtherOffice()
        {
            StrOrgPlan p = Build(Six());
            for (int c = 0; c < p.CardCount; c++)
            {
                int parent = p.Cards[c].Parent;
                if (parent < 0) continue;
                var seen = new bool[p.SegCount];
                var stack = new System.Collections.Generic.Stack<int>();
                for (int s = 0; s < p.SegCount; s++)
                    if (Touch(p.Segs[s], p.Cards[c]) && p.Segs[s].Y < p.Cards[c].Y) { seen[s] = true; stack.Push(s); }   // the line coming in from above
                while (stack.Count > 0)
                {
                    StrOrgSeg a = p.Segs[stack.Pop()];
                    for (int s = 0; s < p.SegCount; s++)
                        if (!seen[s] && Touch(a, p.Segs[s])) { seen[s] = true; stack.Push(s); }
                }
                for (int o = 0; o < p.CardCount; o++)
                {
                    bool joined = false;
                    for (int s = 0; s < p.SegCount && !joined; s++) joined = seen[s] && Touch(p.Segs[s], p.Cards[o]);
                    bool expected = o == c || o == parent || p.Cards[o].Parent == parent;
                    TestAssert.That(joined == expected, "the line of card " + c + " meets exactly its parent and siblings; card " + o + (joined ? " is joined" : " is not joined"));
                }
            }
        }

        private static void AComponentWithNoBasesStandsAlone()
        {
            StrOrgPlan p = Build(Row(0, -1, 0), Row(1, 0, 1), Row(2, 0, 1), Row(3, 2, 2));
            TestAssert.That(p.CardCount == 4 && Level(p, 0) == 1 && Level(p, 1) == 2 && Level(p, 2) == 1, "the base stands under the ground command only");
            TestAssert.That(p.Cards[CardOf(p, 3)].Parent == CardOf(p, 2) && p.Cards[CardOf(p, 1)].Parent == CardOf(p, 0), "parentage follows the ids");
            StrOrgCard air = p.Cards[CardOf(p, 1)];
            for (int s = 0; s < p.SegCount; s++)
                TestAssert.That(!(Touch(p.Segs[s], air) && p.Segs[s].Y > air.Y + air.H + 1f), "no line runs down from a component with no bases");
        }

        private static void AMissingOfficeIsAnEmptyCardInItsPlace()
        {
            // A base whose component nobody confirmed: the component is drawn as an empty card, and so is the theater above it.
            StrOrgPlan p = Build(Row(5, 12, 2));
            TestAssert.That(p.CardCount == 3, "an empty theater card, an empty component card, the base");
            StrOrgCard b = p.Cards[CardOf(p, 0)];
            StrOrgCard comp = p.Cards[b.Parent];
            TestAssert.That(comp.Row < 0 && comp.Tier == 1 && p.Cards[comp.Parent].Row < 0 && p.Cards[comp.Parent].Tier == 0, "the base hangs under the empty component under the empty theater");

            // A known component with no theater above it: one empty theater card.
            p = Build(Row(12, 14, 1));
            TestAssert.That(p.CardCount == 2 && p.Cards[0].Row == 0 && p.Cards[1].Row == -1 && p.Cards[0].Parent == 1 && p.Cards[1].Tier == 0, "the theater office is empty above the component");

            // Two bases of the same missing component share one empty card.
            p = Build(Row(0, -1, 0), Row(3, 9, 2), Row(4, 9, 2));
            TestAssert.That(p.CardCount == 4 && p.Cards[CardOf(p, 1)].Parent == p.Cards[CardOf(p, 2)].Parent, "one empty component card for both bases");
        }

        private static void AnOfficerWhoseParentIsMissingHangsUnderTheRoot()
        {
            StrOrgPlan p = Build(Row(0, -1, 0), Row(1, 0, 1), Row(2, -1, 1), Row(3, 77, 1));
            TestAssert.That(p.CardCount == 4, "nobody vanishes");
            int root = CardOf(p, 0);
            TestAssert.That(p.Cards[CardOf(p, 2)].Parent == root && p.Cards[CardOf(p, 3)].Parent == root, "a component with no parent id or an unknown one hangs under the theater commander");
        }

        private static void ABadParentChainNeverLosesAnOfficer()
        {
            StrOrgPlan p = Build(Row(0, -1, 0), Row(1, 2, 1), Row(2, 1, 1), Row(3, 3, 2), Row(4, 0, 0));
            TestAssert.That(p.CardCount == 5, "a cycle and a second theater commander lose nobody");
            for (int i = 0; i < p.CardCount; i++)
            {
                int hops = 0, cur = i;
                while (p.Cards[cur].Parent >= 0 && hops++ < 20) cur = p.Cards[cur].Parent;
                TestAssert.That(hops < 20 && p.Cards[cur].Row == 0, "card " + i + " ends at the theater commander");
            }
        }

        private static void NothingBuildsNothing()
        {
            var p = new StrOrgPlan();
            p.Build(null, 0, Width, CardH, Gap, VGap, MaxW);
            TestAssert.That(p.CardCount == 0 && p.SegCount == 0 && p.Height == 0f, "no officers, no chart");
            p.Build(Six(), 0, Width, CardH, Gap, VGap, MaxW);
            TestAssert.That(p.CardCount == 0, "a count of zero builds nothing");
        }
    }
}
