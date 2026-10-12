using System;
using NOAvionics;
using NOAvionics.Tests;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>
    /// The amber "effort" ink role (annex 05 P7): every shipped palette defines it, with the value of its own caution line so the look is
    /// unchanged until a theme separates them, and a sheet without it resolves to the caller's fallback (AvInk.Effort passes the caution hue).
    /// </summary>
    internal static class StrInkTests
    {
        private static readonly Rgba Sentinel = new Rgba(1f, 0.0123f, 1f);

        public static void Run()
        {
            EveryPaletteDefinesEffortAsItsCaution();
            APortalEffortIsTheMockupAmber();
            ASheetWithoutTheRoleFallsBack();
        }

        private static AvStyleSheet Composed(AvThemeId id) =>
            AvStyleSheet.Parse(AvThemes.Compose(id == AvThemeId.Custom ? AvThemes.CustomPalette(190f, 200f, 0.6f)
                : AvFuiTests.Sheet("avionics." + id.ToString().ToLowerInvariant() + ".avss"), AvFuiTests.Sheet("avionics.fui.avss")));

        private static bool IsSentinel(AvPaint p) => p.Kind == AvColorRef.Fixed && Math.Abs(p.Value.G - 0.0123f) < 1e-5f;

        private static void EveryPaletteDefinesEffortAsItsCaution()
        {
            foreach (AvThemeId id in (AvThemeId[])Enum.GetValues(typeof(AvThemeId)))
            {
                AvStyleSheet sheet = Composed(id);
                AvPaint effort = sheet.Paint("effort", Sentinel), caution = sheet.Paint("caution", Sentinel);
                TestAssert.That(!IsSentinel(effort), id + " defines the effort role");
                TestAssert.That(effort.Kind == caution.Kind && effort.Alpha.Equals(caution.Alpha) && effort.Value.Equals(caution.Value),
                    id + " effort is its caution line (the look is unchanged until a theme separates them)");
            }
        }

        private static void APortalEffortIsTheMockupAmber()
        {
            Rgba effort = Composed(AvThemeId.Portal).Paint("effort", Sentinel).Value;   // #F5BA56
            TestAssert.That(Math.Abs(effort.R - 0xF5 / 255f) < 0.003f && Math.Abs(effort.G - 0xBA / 255f) < 0.003f && Math.Abs(effort.B - 0x56 / 255f) < 0.003f,
                "Portal effort is the mockup amber #F5BA56");
        }

        private static void ASheetWithoutTheRoleFallsBack()
        {
            AvStyleSheet sheet = AvStyleSheet.Parse(":root { caution: #112233; }");
            TestAssert.That(IsSentinel(sheet.Paint("effort", Sentinel)), "a sheet lacking effort answers with the caller's fallback, which AvInk.Effort sets to the caution hue");
            TestAssert.That(!IsSentinel(sheet.Paint("caution", Sentinel)), "the same sheet still resolves the roles it has");
        }
    }
}
