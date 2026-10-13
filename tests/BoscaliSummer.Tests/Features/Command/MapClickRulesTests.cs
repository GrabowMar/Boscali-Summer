using BoscaliSummer.Modules.Command.Domain;

namespace BoscaliSummer.Tests.Features.Command
{
    /// <summary>The plain-click rule of the map: a click that another map control took must not clear the unit selection (annex 05 P5).</summary>
    internal static class MapClickRulesTests
    {
        public static void Run()
        {
            TheTruthTable();
            AControlThatTookTheClickKeepsTheSelection();
        }

        private static void TheTruthTable()
        {
            foreach (bool open in new[] { false, true })
                foreach (bool sameFrame in new[] { false, true })
                    foreach (bool shift in new[] { false, true })
                    {
                        bool want = open && !sameFrame && !shift;
                        bool got = MapClickRules.ShouldClearSelection(open, sameFrame ? 41 : 7, 41, shift);
                        TestAssert.That(got == want, "open " + open + ", click taken this frame " + sameFrame + ", shift " + shift + " clears " + want + ", got " + got);
                    }
        }

        private static void AControlThatTookTheClickKeepsTheSelection()
        {
            TestAssert.That(MapClickRules.ShouldClearSelection(true, -1, 0, false), "no control ever took a click (frame -1): bare map clears the selection");
            TestAssert.That(MapClickRules.BoxTakes(MapBoxSide.All, true, true) && MapClickRules.BoxTakes(MapBoxSide.All, true, false) && MapClickRules.BoxTakes(MapBoxSide.All, false, false),
                "a box set to ALL takes both sides and the factionless");
            TestAssert.That(MapClickRules.BoxTakes(MapBoxSide.Hostile, true, false) && !MapClickRules.BoxTakes(MapBoxSide.Hostile, true, true) && !MapClickRules.BoxTakes(MapBoxSide.Hostile, false, false),
                "a box set to HOSTILE takes the other side only");
            TestAssert.That(MapClickRules.BoxTakes(MapBoxSide.Friendly, true, true) && !MapClickRules.BoxTakes(MapBoxSide.Friendly, true, false) && !MapClickRules.BoxTakes(MapBoxSide.Friendly, false, false),
                "a box set to OWN takes the player's side only");
            TestAssert.That(MapClickRules.ShouldClearSelection(true, 0, 1, false), "a click taken on the previous frame does not shield this one");
            TestAssert.That(!MapClickRules.ShouldClearSelection(true, 1, 1, false), "a click taken this frame does");
            TestAssert.That(!MapClickRules.ShouldClearSelection(false, 0, 1, false), "a closed map clears nothing");
            TestAssert.That(MapClickRules.ShouldClearSelection(true, int.MinValue, int.MaxValue, false), "extreme frame numbers differ and clear");
        }
    }
}
