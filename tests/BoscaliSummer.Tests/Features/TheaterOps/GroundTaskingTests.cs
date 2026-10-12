using System;
using System.Collections.Generic;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    /// <summary>Which ground groups carry the main effort and how a new vehicle is grouped (annex 02 G1 and G2, tables of 6.6). Distances are metres from a fix at the origin unless a test says otherwise.</summary>
    internal static class GroundTaskingTests
    {
        public static void Run()
        {
            ConstantsAreTheSpecifiedOnes();
            QuotaTable();
            QuotaHasNoOverflow();
            ChooseTakesTheNearestFourOfSix();
            ChooseIsIndependentOfInputOrder();
            ChooseTieGoesToTheLowerId();
            AHeldGroupBeatsNearerNewcomers();
            TheHeldSetStaysOnASecondPass();
            AShrinkingQuotaDropsTheFarthestHeld();
            AnUnusableGroupIsNeitherCountedNorChosen();
            ReachBoundariesAreInclusiveAndHeldReachesFarther();
            AGroupTwelveAndAHalfKilometresOutIsNowInReach();
            AWaveGroupIsChosenAtAnyDistanceOutsideTheQuota();
            ChooseClearsStaleMarksAndSurvivesBadInput();
            NearestFrontRules();
            PickRules();
            FreeLaneRules();
        }

        private static GroupFacts G(int id, float x, bool usable = true, bool held = false, bool wave = false) =>
            new GroupFacts(id, x, 0f, usable, held, wave);

        private static string Ids(GroupFacts[] groups, bool[] chosen)
        {
            var ids = new List<int>();
            for (int i = 0; i < groups.Length; i++) if (chosen[i]) ids.Add(groups[i].Id);
            ids.Sort();
            return string.Join(",", ids);
        }

        private static int Pick(GroupFacts[] groups, TheaterOpKind kind, out string ids)
        {
            var chosen = new bool[groups.Length];
            int taken = GroundTasking.Choose(groups, groups.Length, 0f, 0f, kind, chosen);
            ids = Ids(groups, chosen);
            return taken;
        }

        private static void ConstantsAreTheSpecifiedOnes()
        {
            TestAssert.Near(GroundTasking.ReachMeters, 20000f, "a group joins the effort from 20 km");
            TestAssert.Near(GroundTasking.HeldReachMeters, 25000f, "a group already on the effort is kept to 25 km");
            TestAssert.Near(GroundTasking.FrontReachMeters, 12000f, "an off-effort group holds a front within 12 km");
            TestAssert.Near(GroundTasking.WaveSeconds, 120f, "a depot-born group is a wave group for 120 s");
            TestAssert.Near(GroupFormation.JoinSeconds, 20f, "a group forms around its leader for 20 s");
            TestAssert.Near(GroupFormation.JoinRadiusMeters, 800f, "a young group takes a vehicle within 800 m");
            TestAssert.Near(GroupFormation.StragglerRadiusMeters, 3000f, "an older group takes a straggler within 3 km");
        }

        private static void QuotaTable()
        {
            int[] want = { 0, 1, 2, 2, 3, 4, 4, 5, 6 };
            for (int n = 0; n < want.Length; n++)
            {
                TestAssert.Eq(GroundTasking.Quota(TheaterOpKind.Assault, n), want[n], "assault quota of " + n + " usable groups");
                TestAssert.Eq(GroundTasking.Quota(TheaterOpKind.Defend, n), want[n], "defend quota of " + n + " usable groups");
            }
            TestAssert.Eq(GroundTasking.Quota(TheaterOpKind.Assault, -3), 0, "a negative count sends nothing");
            TestAssert.Eq(GroundTasking.Quota(TheaterOpKind.Recon, -3), 0, "a negative count sends no scout either");
            TestAssert.Eq(GroundTasking.Quota(TheaterOpKind.Recon, 0), 0, "a reconnaissance needs a group to send");
            for (int n = 1; n <= 8; n++)
                TestAssert.Eq(GroundTasking.Quota(TheaterOpKind.Recon, n), 1, "a reconnaissance takes exactly one of " + n);
            // Slice 0 force sizes: Terminal Control opened with 3 Boscali and 5 Primeva groups, Escalation peaked at 15.
            TestAssert.Eq(GroundTasking.Quota(TheaterOpKind.Assault, 3), 2, "three groups send two and keep a front");
            TestAssert.Eq(GroundTasking.Quota(TheaterOpKind.Assault, 5), 4, "five groups send four");
            TestAssert.Eq(GroundTasking.Quota(TheaterOpKind.Assault, 15), 10, "fifteen groups send ten");
        }

        private static void QuotaHasNoOverflow()
        {
            TestAssert.Eq(GroundTasking.Quota(TheaterOpKind.Assault, int.MaxValue), 1431655765, "two thirds of a huge count does not wrap negative");
        }

        private static void ChooseTakesTheNearestFourOfSix()
        {
            var groups = new[] { G(1, 6000f), G(2, 1000f), G(3, 5000f), G(4, 2000f), G(5, 4000f), G(6, 3000f) };
            TestAssert.Eq(Pick(groups, TheaterOpKind.Assault, out string ids), 4, "six usable groups send four");
            TestAssert.Eq(ids, "2,4,5,6", "the four nearest to the fix go");
        }

        private static void ChooseIsIndependentOfInputOrder()
        {
            // One held group far out, one near, two tied farther: quota 3 of 4 takes held, nearest, then the lower id of the tie.
            var set = new[] { G(7, 9000f, held: true), G(3, 1000f), G(5, 4000f), G(2, 4000f) };
            int orderings = 0;
            foreach (var order in Permutations(4))
            {
                var groups = new GroupFacts[4];
                for (int i = 0; i < 4; i++) groups[i] = set[order[i]];
                TestAssert.Eq(Pick(groups, TheaterOpKind.Assault, out string ids), 3, "four usable groups send three");
                TestAssert.Eq(ids, "2,3,7", "every input order chooses the same ids");
                orderings++;
            }
            TestAssert.Eq(orderings, 24, "all 24 orderings of four groups were tried");
        }

        private static void ChooseTieGoesToTheLowerId()
        {
            var forward = new[] { G(9, 2000f), G(4, 2000f) };
            var reverse = new[] { G(4, 2000f), G(9, 2000f) };
            Pick(forward, TheaterOpKind.Recon, out string a);
            Pick(reverse, TheaterOpKind.Recon, out string b);
            TestAssert.Eq(a, "4", "equal distance picks the lower id, listed last");
            TestAssert.Eq(b, "4", "equal distance picks the lower id, listed first");
        }

        private static void AHeldGroupBeatsNearerNewcomers()
        {
            var groups = new[] { G(1, 15000f, held: true), G(2, 2000f), G(3, 3000f) };
            TestAssert.Eq(Pick(groups, TheaterOpKind.Assault, out string ids), 2, "quota 2 of 3");
            TestAssert.Eq(ids, "1,2", "the held group at 15 km keeps its place, the nearest newcomer fills the other");
            Pick(groups, TheaterOpKind.Recon, out ids);
            TestAssert.Eq(ids, "1", "a single place goes to the held group, not to the group at 2 km");
        }

        private static void TheHeldSetStaysOnASecondPass()
        {
            var first = new[] { G(1, 3000f), G(2, 4000f), G(3, 5000f), G(4, 6000f), G(5, 7000f), G(6, 8000f) };
            TestAssert.Eq(Pick(first, TheaterOpKind.Assault, out string ids), 4, "six send four");
            TestAssert.Eq(ids, "1,2,3,4", "first pass takes the four nearest");
            // The other two now stand nearer than three of the chosen groups, and the four chosen groups are held.
            var second = new[] { G(1, 3000f, held: true), G(2, 4000f, held: true), G(3, 5000f, held: true), G(4, 6000f, held: true), G(5, 500f), G(6, 900f) };
            TestAssert.Eq(Pick(second, TheaterOpKind.Assault, out ids), 4, "still four");
            TestAssert.Eq(ids, "1,2,3,4", "a held group is never swapped for a nearer newcomer");
        }

        private static void AShrinkingQuotaDropsTheFarthestHeld()
        {
            var four = new[] { G(1, 1000f, held: true), G(2, 2000f, held: true), G(3, 3000f, held: true), G(4, 4000f) };
            Pick(four, TheaterOpKind.Assault, out string ids);
            TestAssert.Eq(ids, "1,2,3", "four usable groups hold three on the effort");
            var three = new[] { G(1, 1000f, held: true), G(2, 2000f, held: true), G(3, 3000f, held: true), G(4, 4000f, usable: false) };
            Pick(three, TheaterOpKind.Assault, out ids);
            TestAssert.Eq(ids, "1,2", "with one fewer usable group the quota falls to two and the farthest held group is released");
            var lostHeld = new[] { G(1, 1000f, held: true, usable: false), G(2, 2000f, held: true), G(3, 3000f, held: true), G(4, 4000f, held: true) };
            Pick(lostHeld, TheaterOpKind.Assault, out ids);
            TestAssert.Eq(ids, "2,3", "when a held group stalls the other held groups keep two places, the farthest one goes");
        }

        private static void AnUnusableGroupIsNeitherCountedNorChosen()
        {
            // Counting the nearest, unusable group would make the quota 3 (of four) instead of 2 (of three usable).
            var groups = new[] { G(1, 100f, usable: false), G(2, 1000f), G(3, 2000f), G(4, 3000f) };
            TestAssert.Eq(Pick(groups, TheaterOpKind.Assault, out string ids), 2, "three usable groups send two");
            TestAssert.Eq(ids, "2,3", "the unusable group is skipped even though it is nearest");
        }

        private static void ReachBoundariesAreInclusiveAndHeldReachesFarther()
        {
            TestAssert.Eq(Pick(new[] { G(1, 20000f) }, TheaterOpKind.Assault, out _), 1, "an unheld group at exactly 20 km is in");
            TestAssert.Eq(Pick(new[] { G(1, 20001f) }, TheaterOpKind.Assault, out _), 0, "an unheld group at 20,001 m is out");
            TestAssert.Eq(Pick(new[] { G(1, 24999f, held: true) }, TheaterOpKind.Assault, out _), 1, "a held group at 24,999 m is kept");
            TestAssert.Eq(Pick(new[] { G(1, 25000f, held: true) }, TheaterOpKind.Assault, out _), 1, "a held group at exactly 25 km is kept");
            TestAssert.Eq(Pick(new[] { G(1, 25001f, held: true) }, TheaterOpKind.Assault, out _), 0, "a held group at 25,001 m is released");
            TestAssert.Eq(Pick(new[] { G(1, 22000f) }, TheaterOpKind.Assault, out _), 0, "an unheld group between the two reaches does not join");
            // A group the other side of the fix is as far as one on this side (the distance is a distance, not an axis).
            TestAssert.Eq(Pick(new[] { G(1, -19999f) }, TheaterOpKind.Assault, out _), 1, "reach is symmetric about the fix");
        }

        private static void AGroupTwelveAndAHalfKilometresOutIsNowInReach()
        {
            // Slice 0, Escalation: Boscali g3 stood 12.5 km from the fix, outside the old 12 km probe, so it was never assigned.
            var groups = new[] { G(3, 12500f), G(10, 21000f) };
            TestAssert.Eq(Pick(groups, TheaterOpKind.Assault, out string ids), 1, "one group in reach, one out");
            TestAssert.Eq(ids, "3", "the group at 12.5 km joins the effort");
        }

        private static void AWaveGroupIsChosenAtAnyDistanceOutsideTheQuota()
        {
            var groups = new[] { G(1, 1000f), G(2, 2000f), G(3, 3000f), G(9, 200000f, wave: true) };
            TestAssert.Eq(Pick(groups, TheaterOpKind.Assault, out string ids), 3, "two of three plus the wave group");
            TestAssert.Eq(ids, "1,2,9", "the wave group at 200 km is chosen and the quota of the rest is unchanged");
            var near = new[] { G(1, 1000f), G(2, 2000f), G(3, 3000f), G(9, 500f, wave: true) };
            Pick(near, TheaterOpKind.Assault, out ids);
            TestAssert.Eq(ids, "1,2,9", "a wave group inside reach does not use up a place either");
            var gone = new[] { G(1, 1000f), G(2, 2000f), G(3, 3000f), G(9, 200000f, usable: false, wave: true) };
            Pick(gone, TheaterOpKind.Assault, out ids);
            TestAssert.Eq(ids, "1,2", "an unusable wave group is not chosen");
            TestAssert.Eq(Pick(new[] { G(9, 200000f, wave: true) }, TheaterOpKind.Recon, out ids), 1, "a wave group alone is chosen");
            Pick(new[] { G(1, 1000f), G(2, 2000f), G(9, 200000f, wave: true) }, TheaterOpKind.Recon, out ids);
            TestAssert.Eq(ids, "1,9", "a reconnaissance still takes its one group beside the wave group");
        }

        private static void ChooseClearsStaleMarksAndSurvivesBadInput()
        {
            var groups = new[] { G(1, 100f, usable: false), G(2, 90000f), G(3, 1000f) };
            var chosen = new[] { true, true, true };
            TestAssert.Eq(GroundTasking.Choose(groups, 3, 0f, 0f, TheaterOpKind.Assault, chosen), 1, "one group in reach");
            TestAssert.That(!chosen[0] && !chosen[1] && chosen[2], "stale marks are cleared before choosing");

            TestAssert.Eq(GroundTasking.Choose(null, 0, 0f, 0f, TheaterOpKind.Assault, new bool[1]), 0, "null groups");
            TestAssert.Eq(GroundTasking.Choose(groups, 3, 0f, 0f, TheaterOpKind.Assault, null), 0, "null result array");
            TestAssert.Eq(GroundTasking.Choose(new GroupFacts[0], 0, 0f, 0f, TheaterOpKind.Assault, new bool[0]), 0, "empty input");
            TestAssert.Eq(GroundTasking.Choose(groups, 0, 0f, 0f, TheaterOpKind.Assault, new bool[3]), 0, "a zero count");
            TestAssert.Eq(GroundTasking.Choose(groups, -1, 0f, 0f, TheaterOpKind.Assault, new bool[3]), 0, "a negative count");
            TestAssert.Eq(GroundTasking.Choose(groups, 4, 0f, 0f, TheaterOpKind.Assault, new bool[4]), 0, "a count past the groups array");
            TestAssert.Eq(GroundTasking.Choose(groups, 3, 0f, 0f, TheaterOpKind.Assault, new bool[2]), 0, "a result array that is too short");

            var withWave = new[] { G(1, 1000f), G(2, 2000f, wave: true) };
            var marks = new bool[2];
            TestAssert.Eq(GroundTasking.Choose(withWave, 2, float.NaN, 0f, TheaterOpKind.Assault, marks), 1, "an unknown fix reaches no ordinary group");
            TestAssert.That(!marks[0] && marks[1], "only the wave group is chosen when the fix is NaN");
            var nan = new[] { new GroupFacts(1, float.NaN, 0f, true, false, false), G(2, 1000f) };
            TestAssert.Eq(Pick(nan, TheaterOpKind.Assault, out string ids), 1, "a group at an unknown position is never in reach");
            TestAssert.Eq(ids, "2", "the group with a known position is chosen");
        }

        private static void NearestFrontRules()
        {
            float[] fx = { 5000f, 3000f, -2000f }, fz = { 0f, 4000f, 0f };
            TestAssert.Eq(GroundTasking.NearestFront(0f, 0f, fx, fz, 3), 2, "the nearest front within 12 km");
            float[] tx = { 3000f, -3000f, 0f }, tz = { 0f, 0f, 3000f };
            TestAssert.Eq(GroundTasking.NearestFront(0f, 0f, tx, tz, 3), 0, "a tie goes to the lowest index");
            float[] rx = { 0f, -3000f, 3000f }, rz = { 3000f, 0f, 0f };
            TestAssert.Eq(GroundTasking.NearestFront(0f, 0f, rx, rz, 3), 0, "a tie goes to the lowest index in any order");
            TestAssert.Eq(GroundTasking.NearestFront(0f, 0f, new[] { 12000f }, new[] { 0f }, 1), 0, "a front at exactly 12 km is in");
            TestAssert.Eq(GroundTasking.NearestFront(0f, 0f, new[] { 12001f }, new[] { 0f }, 1), -1, "a front at 12,001 m is out");
            TestAssert.Eq(GroundTasking.NearestFront(0f, 0f, new[] { 20000f, 30000f }, new[] { 0f, 0f }, 2), -1, "beyond 12 km returns -1");
            TestAssert.Eq(GroundTasking.NearestFront(0f, 0f, fx, fz, 0), -1, "a zero count returns -1");
            TestAssert.Eq(GroundTasking.NearestFront(0f, 0f, fx, fz, 4), -1, "a count past the arrays returns -1");
            TestAssert.Eq(GroundTasking.NearestFront(0f, 0f, null, fz, 3), -1, "null x returns -1");
            TestAssert.Eq(GroundTasking.NearestFront(0f, 0f, fx, null, 3), -1, "null z returns -1");
            TestAssert.Eq(GroundTasking.NearestFront(0f, 0f, new float[0], new float[0], 0), -1, "empty returns -1");
            TestAssert.Eq(GroundTasking.NearestFront(0f, 0f, new[] { float.NaN, 4000f }, new[] { 0f, 0f }, 2), 1, "a front at an unknown position is skipped");
            TestAssert.Eq(GroundTasking.NearestFront(float.NaN, 0f, fx, fz, 3), -1, "a group at an unknown position holds no front");
            TestAssert.Eq(GroundTasking.NearestFront(3000f, 3000f, fx, fz, 3), 1, "distance is measured from the group, not the origin");
        }

        private static void PickRules()
        {
            TestAssert.Eq(GroupFormation.Pick(new[] { new OpenGroup(1, 800f, 0f, 3, 5f) }, 1, 0f, 0f), 0, "a young group takes a vehicle at 800 m");
            TestAssert.Eq(GroupFormation.Pick(new[] { new OpenGroup(1, 801f, 0f, 3, 5f) }, 1, 0f, 0f), -1, "not at 801 m");
            // A group seals at 20 s: 1.5 km is too far for a 19.9 s group and fine for a 20.0 s one.
            TestAssert.Eq(GroupFormation.Pick(new[] { new OpenGroup(1, 1500f, 0f, 3, 19.9f) }, 1, 0f, 0f), -1, "a group still forming does not take a straggler");
            TestAssert.Eq(GroupFormation.Pick(new[] { new OpenGroup(1, 1500f, 0f, 3, 20f) }, 1, 0f, 0f), 0, "a group at 20 s takes a straggler at 1.5 km");
            TestAssert.Eq(GroupFormation.Pick(new[] { new OpenGroup(1, 2999f, 0f, 3, 60f) }, 1, 0f, 0f), 0, "a straggler at 2,999 m joins an older group");
            TestAssert.Eq(GroupFormation.Pick(new[] { new OpenGroup(1, 3000f, 0f, 3, 60f) }, 1, 0f, 0f), 0, "a straggler at exactly 3 km joins");
            TestAssert.Eq(GroupFormation.Pick(new[] { new OpenGroup(1, 3001f, 0f, 3, 60f) }, 1, 0f, 0f), -1, "not at 3,001 m");
            // A full group is skipped, a group with room for one is not.
            var full = new[] { new OpenGroup(1, 100f, 0f, 6, 5f), new OpenGroup(2, 700f, 0f, 5, 5f) };
            TestAssert.Eq(GroupFormation.Pick(full, 2, 0f, 0f), 1, "the full group is skipped for the one with room");
            TestAssert.Eq(GroupFormation.Pick(new[] { new OpenGroup(1, 100f, 0f, 6, 5f) }, 1, 0f, 0f), -1, "a full group takes nobody");
            // The nearest leader wins; equal distances go to the lower id whatever the order.
            var near = new[] { new OpenGroup(1, 700f, 0f, 2, 5f), new OpenGroup(2, 300f, 0f, 2, 5f) };
            TestAssert.Eq(GroupFormation.Pick(near, 2, 0f, 0f), 1, "the nearer leader wins over the lower id");
            var tieForward = new[] { new OpenGroup(8, 500f, 0f, 2, 5f), new OpenGroup(3, 0f, 500f, 2, 5f) };
            var tieReverse = new[] { tieForward[1], tieForward[0] };
            TestAssert.Eq(GroupFormation.Pick(tieForward, 2, 0f, 0f), 1, "a tie goes to the lower id (listed last)");
            TestAssert.Eq(GroupFormation.Pick(tieReverse, 2, 0f, 0f), 0, "a tie goes to the lower id (listed first)");
            // Slice 0: lone convoy vehicles crossing a boundary made groups of one or two. A sealed one-vehicle group still takes a straggler.
            TestAssert.Eq(GroupFormation.Pick(new[] { new OpenGroup(4, 2000f, 0f, 1, 300f) }, 1, 0f, 0f), 0, "a sealed one-vehicle group takes a straggler 2 km behind");
            TestAssert.Eq(GroupFormation.Pick(null, 0, 0f, 0f), -1, "null returns -1");
            TestAssert.Eq(GroupFormation.Pick(new OpenGroup[0], 0, 0f, 0f), -1, "empty returns -1");
            TestAssert.Eq(GroupFormation.Pick(full, 3, 0f, 0f), -1, "a count past the array returns -1");
            TestAssert.Eq(GroupFormation.Pick(full, 0, 0f, 0f), -1, "a zero count returns -1");
            TestAssert.Eq(GroupFormation.Pick(new[] { new OpenGroup(1, float.NaN, 0f, 2, 5f) }, 1, 0f, 0f), -1, "a leader at an unknown position takes nobody");
        }

        private static void FreeLaneRules()
        {
            TestAssert.Eq(GroupFormation.FreeLane(0), 0, "an empty group starts at lane 0");
            TestAssert.Eq(GroupFormation.FreeLane(0b001), 1, "lane 0 taken");
            TestAssert.Eq(GroupFormation.FreeLane(0b101), 1, "lanes 0 and 2 taken, 1 is free");
            TestAssert.Eq(GroupFormation.FreeLane(0b101111), 4, "the lowest free lane, not the member count");
            TestAssert.Eq(GroupFormation.FreeLane(0b111110), 0, "lane 0 free again after a loss");
            TestAssert.Eq(GroupFormation.FreeLane(0b111111), -1, "six lanes taken leaves none");
            TestAssert.Eq(GroupFormation.FreeLane(unchecked((int)0xFFFFFFC0)), 0, "bits above the sixth lane are not lanes");
            TestAssert.Eq(GroupFormation.FreeLane(-1), -1, "every bit set leaves none");
        }

        private static IEnumerable<int[]> Permutations(int n)
        {
            var items = new int[n];
            for (int i = 0; i < n; i++) items[i] = i;
            return Permute(items, 0);
        }

        private static IEnumerable<int[]> Permute(int[] items, int start)
        {
            if (start == items.Length - 1) { yield return (int[])items.Clone(); yield break; }
            for (int i = start; i < items.Length; i++)
            {
                (items[start], items[i]) = (items[i], items[start]);
                foreach (var order in Permute(items, start + 1)) yield return order;
                (items[start], items[i]) = (items[i], items[start]);
            }
        }
    }
}
