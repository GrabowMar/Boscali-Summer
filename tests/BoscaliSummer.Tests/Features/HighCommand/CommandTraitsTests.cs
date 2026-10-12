using System.Globalization;
using BoscaliSummer.Modules.HighCommand.Domain;

namespace BoscaliSummer.Tests.Features.HighCommand
{
    /// <summary>Annex 04 H6: what killing an enemy commander means, said from the attacker's side (the friendly line is BonusLine).</summary>
    internal static class CommandTraitsTests
    {
        // Written out independently of the rule so a drift in either one fails.
        private static readonly (CommandTrait Trait, string Text)[] Expected =
        {
            (CommandTrait.Logistician, "THEIR STIPEND +15%"),
            (CommandTrait.Beloved, "LOSS HURTS THEM 30 S LONGER"),
            (CommandTrait.Zealot, "+15% OF THEIR STAFF"),
            (CommandTrait.Veteran, "BOUNTY +20% TO YOU"),
            (CommandTrait.Recluse, "HARD TO SPOT (1.8 KM)"),
        };

        public static void Run()
        {
            NoTraitSaysSo();
            EveryTraitStatesItsEffect();
            EveryMaskFitsAndOneAndTwoTraitMasksStateEveryTrait();
            TheStrikeLineIsNeverTheOwnersBonusLine();
            TheRecluseDistanceIsTheRealOne();
        }

        private static void NoTraitSaysSo()
        {
            TestAssert.Eq(CommandTraits.StrikeLine(CommandTrait.None), "NO STAFF BONUS", "a commander with nothing notable");
            TestAssert.Eq(CommandTraits.StrikeLine((CommandTrait)(1 << 5)), "NO STAFF BONUS", "bits that are not traits count for nothing");
        }

        private static void EveryTraitStatesItsEffect()
        {
            foreach (var (trait, text) in Expected)
            {
                TestAssert.Eq(CommandTraits.StrikeLine(trait), text, trait + " on its own");
                TestAssert.Eq(CommandTraits.StrikeEffect(trait), text, trait + " effect");
            }
            TestAssert.Eq(CommandTraits.StrikeEffect(CommandTrait.None), "", "no trait, no effect");
        }

        private static void EveryMaskFitsAndOneAndTwoTraitMasksStateEveryTrait()
        {
            for (int mask = 0; mask <= CommandTraits.All; mask++)
            {
                var traits = (CommandTrait)mask;
                string line = CommandTraits.StrikeLine(traits);
                TestAssert.That(line.Length > 0 && line.Length <= CommandTraits.MaximumStrikeLine, "mask " + mask + " fits (" + line.Length + ")");
                int count = 0;
                foreach (var (trait, _) in Expected) if (CommandTraits.Has(traits, trait)) count++;
                if (count > 2) continue;
                foreach (var (trait, text) in Expected)
                    TestAssert.That(CommandTraits.Has(traits, trait) == line.Contains(text), "mask " + mask + " states " + trait + " exactly when it has it");
                if (count == 2)
                    TestAssert.That(line.Contains(" · "), "two traits are joined by a middle dot");
            }
        }

        private static void TheStrikeLineIsNeverTheOwnersBonusLine()
        {
            foreach (var (trait, _) in Expected)
                TestAssert.That(CommandTraits.StrikeLine(trait) != CommandTraits.BonusLine(trait),
                    trait + ": an enemy post is described by what killing it means, not by what it pays its owner");
        }

        private static void TheRecluseDistanceIsTheRealOne()
        {
            float metres = StaffWords.PostSightMetres * CommandTraits.IntelRadiusMultiplier(CommandTrait.Recluse);
            string km = (metres / 1000f).ToString("0.0", CultureInfo.InvariantCulture);
            TestAssert.That(CommandTraits.StrikeLine(CommandTrait.Recluse).Contains("(" + km + " KM)"),
                "the recluse line names the radius the sensing really uses (" + km + " km)");
        }
    }
}
