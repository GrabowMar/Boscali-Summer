using System;
using System.Globalization;
using System.Reflection;
using BoscaliSummer.Modules.HighCommand.Domain;

namespace BoscaliSummer.Tests.Features.HighCommand
{
    /// <summary>Annex 04 H8: every sentence the staff board says on its own fits the 96-character wire clip.</summary>
    internal static class StaffWordsTests
    {
        private const int Clip = 96;

        public static void Run()
        {
            EveryConstantFitsTheClip();
            StaleReadsWholeSecondsAndAdmitsWhenItCannotTell();
            TheBuildersReadAsTheAnnexExamples();
            TheBuildersFitTheClipForTheWorstInputs();
            TheSightRadiusSentenceFollowsTheConstant();
        }

        private static void EveryConstantFitsTheClip()
        {
            int seen = 0;
            foreach (FieldInfo field in typeof(StaffWords).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.FieldType != typeof(string)) continue;
                string text = (string)field.GetValue(null);
                TestAssert.That(!string.IsNullOrEmpty(text), field.Name + " says something");
                TestAssert.That(text.Length <= 83, field.Name + " is at most 83 characters (" + text.Length + ")");
                seen++;
            }
            TestAssert.That(seen == 7, "the seven fixed sentences are all covered (found " + seen + ")");
        }

        private static void StaleReadsWholeSecondsAndAdmitsWhenItCannotTell()
        {
            TestAssert.Eq(StaffWords.Stale(42.4f), "Staff board is 42 s old: waiting for the host.", "a stale board names its age");
            TestAssert.That(StaffWords.Stale(999f).Length <= Clip && StaffWords.Stale(999f).Contains("999"), "Stale(999) fits and says 999");
            TestAssert.That(StaffWords.Stale(float.MaxValue).Length <= Clip, "an absurd age still fits");
            TestAssert.That(StaffWords.Stale(-3f).Contains(" 0 s "), "a negative age reads 0");
            string unknown = StaffWords.Stale(float.NaN);
            TestAssert.That(unknown == StaffWords.Stale(float.PositiveInfinity) && unknown.Length > 0 && unknown.Length <= Clip && !unknown.Contains(" 0 s"),
                "an age that cannot be read says so instead of printing a number");
        }

        private static void TheBuildersReadAsTheAnnexExamples()
        {
            TestAssert.Eq(StaffWords.KillLine(3000, "MAJ GEN", "Mira Keller", "KILLED AT Sandrift Airbase"),
                "BOUNTY +3000 · MAJ GEN Mira Keller KILLED AT Sandrift Airbase", "the bounty line");
            TestAssert.Eq(StaffWords.ClaimedLine("MAJ GEN", "Mira Keller"),
                "NO BOUNTY, OFFICE ALREADY CLAIMED · MAJ GEN Mira Keller", "the already-claimed line");
            TestAssert.Eq(StaffWords.AfieldLine("MAJ GEN", "Mira Keller"),
                "MAJ GEN Mira Keller WAS AFIELD · POST DESTROYED, NO BOUNTY", "the afield line");
        }

        private static void TheBuildersFitTheClipForTheWorstInputs()
        {
            string rank = new string('R', 8), name = new string('N', 24), cause = new string('C', 43);
            TestAssert.That(StaffWords.KillLine(100000, rank, name, cause).Length <= Clip, "kill line at the documented worst case");
            TestAssert.That(StaffWords.ClaimedLine(rank, name).Length <= Clip, "claimed line at the documented worst case");
            TestAssert.That(StaffWords.AfieldLine(rank, name).Length <= Clip, "afield line at the documented worst case");

            string longRank = new string('R', 40), longName = new string('N', 60), longCause = new string('C', 120);
            foreach (int funds in new[] { int.MinValue, -1, 0, 100000, 100001, int.MaxValue })
                TestAssert.That(StaffWords.KillLine(funds, longRank, longName, longCause).Length <= Clip, "kill line with funds " + funds + " and long inputs fits");
            TestAssert.That(StaffWords.ClaimedLine(longRank, longName).Length <= Clip, "claimed line with long inputs fits");
            TestAssert.That(StaffWords.AfieldLine(longRank, longName).Length <= Clip, "afield line with long inputs fits");

            TestAssert.That(StaffWords.KillLine(5, null, null, null).Length > 0 && StaffWords.ClaimedLine(null, null).Length > 0 &&
                            StaffWords.AfieldLine(null, null).Length > 0, "null names never throw");
            TestAssert.That(StaffWords.KillLine(-9, rank, name, cause).Contains("+0"), "a negative bounty is never printed as money");
        }

        private static void TheSightRadiusSentenceFollowsTheConstant()
        {
            TestAssert.That(StaffWords.PostSightMetres == 2600f, "the post sight radius is 2.6 km");
            string km = (StaffWords.PostSightMetres / 1000f).ToString("0.0", CultureInfo.InvariantCulture);
            TestAssert.That(StaffWords.NoEnemyPost.Contains(km + " km"), "the no-enemy-post sentence names the same radius (" + km + " km)");
        }
    }
}
