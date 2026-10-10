using BoscaliSummer.Modules.Wing.Domain.Pure;
using BoscaliSummer.Tests;

namespace WingPure.Tests
{
    internal static class ReviewFix2Tests
    {
        public static void Run()
        {
            TestAssert.That(BadgeGlyphs.Label("B4", ' ', false, false, false) == "B4", "an unmatched stance (space) adds nothing to the badge");
            TestAssert.That(BadgeGlyphs.Label("B4", 'H', false, false, false).StartsWith("B4 "), "a matched stance still adds its letter");
            // DEFAULT FOR: order kind + task shape -> the editor's keys (TaskKind: 0 Form 1 Move 2 Route 3 Patrol 4 Orbit 5 Hold)
            TestAssert.That(StanceDefaults.Key("Attack", 0, false, false) == "attack", "attack");
            TestAssert.That(StanceDefaults.Key("EscortMe", 0, false, false) == "escort" && StanceDefaults.Key("EscortTarget", 0, false, false) == "escort", "escort");
            TestAssert.That(StanceDefaults.Key("Task", 4, true, false) == "cap" && StanceDefaults.Key("Task", 3, true, false) == "sweep", "cap and sweep are guarded tasks");
            TestAssert.That(StanceDefaults.Key("Task", 4, false, true) == "scout", "scout wins over the orbit it ends in");
            TestAssert.That(StanceDefaults.Key("Task", 1, false, false) == "move" && StanceDefaults.Key("Task", 2, false, false) == "move", "move and route");
            TestAssert.That(StanceDefaults.Key("Task", 5, false, false) == "orbit" && StanceDefaults.Key("Task", 3, false, false) == "patrol", "hold reads orbit; patrol");
            TestAssert.That(StanceDefaults.Key("Task", 0, false, false) == null && StanceDefaults.Key("Rtb", 0, false, false) == null, "everything else has no default");
        }
    }
}
