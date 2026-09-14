using BoscaliSummer.Features.Progression.Runtime;

namespace BoscaliSummer.Tests.Features.Progression
{
    /// <summary>Squadron emblems are local cosmetics, but their encoding and bounds still
    /// round-trip through the BepInEx config and therefore get pure coverage.</summary>
    internal static class EmblemTests
    {
        public static void Run()
        {
            RoundTrip();
            Bounds();
            RandomIsDeterministic();
            PaletteAccessors();
            HostileIsDeterministic();
        }

        private static void RoundTrip()
        {
            var design = new EmblemDesign(2, 4, 5);
            TestAssert.That(design.Encode() == "2.4.5", "emblem encoding changed");
            TestAssert.That(EmblemDesign.TryParse(design.Encode(), out EmblemDesign parsed) && parsed == design,
                "an encoded emblem did not round-trip");
            TestAssert.That(EmblemDesign.Default == new EmblemDesign(0, 2, 3), "the default emblem changed");
        }

        private static void Bounds()
        {
            var wrapped = new EmblemDesign(9, 9, 9);
            TestAssert.That(wrapped.Shape == 1 && wrapped.Charge == 4 && wrapped.Palette == 3,
                "emblem indices must wrap into range");
            TestAssert.That(!EmblemDesign.TryParse("9.0.0", out _), "an out-of-range shape parsed");
            TestAssert.That(!EmblemDesign.TryParse("0.9.0", out _), "an out-of-range charge parsed");
            TestAssert.That(!EmblemDesign.TryParse("0.0.9", out _), "an out-of-range palette parsed");
            TestAssert.That(!EmblemDesign.TryParse("0.0", out _), "a truncated emblem parsed");
            TestAssert.That(!EmblemDesign.TryParse("a.b.c", out _), "a non-numeric emblem parsed");
            TestAssert.That(!EmblemDesign.TryParse("", out _), "an empty emblem parsed");
        }

        private static void RandomIsDeterministic()
        {
            EmblemDesign first = EmblemDesign.Random(12345);
            EmblemDesign second = EmblemDesign.Random(12345);
            TestAssert.That(first == second, "emblem randomisation is not seed-stable");
            for (int seed = 0; seed < 256; seed++)
            {
                EmblemDesign design = EmblemDesign.Random(seed);
                TestAssert.That(design.Shape < EmblemDesign.ShapeCount &&
                    design.Charge < EmblemDesign.ChargeCount &&
                    design.Palette < EmblemDesign.PaletteCount,
                    "random emblem indices escaped their catalogue");
            }
        }

        private static void PaletteAccessors()
        {
            for (byte palette = 0; palette < EmblemDesign.PaletteCount; palette++)
            {
                TestAssert.That(EmblemDesign.Primary(palette).A > 0f, "emblem primary colour is invisible");
                TestAssert.That(EmblemDesign.Secondary(palette).A > 0f, "emblem secondary colour is invisible");
            }
        }

        private static void HostileIsDeterministic()
        {
            for (int i = 0; i < 128; i++)
            {
                string identity = "[+]|CROWN " + i;
                EmblemDesign first = EmblemDesign.Hostile(identity);
                TestAssert.That(first == EmblemDesign.Hostile(identity), "a hostile crest is not identity-stable");
                TestAssert.That(first.Palette == 2 || first.Palette == 3,
                    "a hostile crest escaped the caution/danger palette band");
                TestAssert.That(first.Shape < EmblemDesign.ShapeCount && first.Charge < EmblemDesign.ChargeCount,
                    "a hostile crest escaped its catalogue");
            }
            TestAssert.That(EmblemDesign.Hostile("") == EmblemDesign.Hostile(null),
                "an empty wing identity is not safe to hash");
        }
    }
}
