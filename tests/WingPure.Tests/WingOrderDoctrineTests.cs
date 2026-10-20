using BoscaliSummer.Modules.Wing.Domain;
using BoscaliSummer.Tests;

// WingOrder's task field is unused here (WingTask.cs clashes with GroundDepartureTests' TaskKind seam).
namespace BoscaliSummer.Modules.Wing.Domain
{
    internal sealed class WingTask { }
}

namespace WingPure.Tests
{
    /// <summary>Wing order and doctrine gaps (audit 2026-10-19): members detached out of one element into another keep its
    /// settings, and an aircraft's own doctrine can be cleared.</summary>
    internal static class WingOrderDoctrineTests
    {
        public static void Run()
        {
            WingDoctrine wing = WingDoctrine.Sweep, hold = WingDoctrine.Reserve;
            var roster = new ElementRoster();
            for (uint id = 1; id <= 4; id++) roster.Add(id);
            var settings = new ElementSettings();

            // Out of A: the new element follows the wing.
            ScopeTarget b = roster.Resolve(WingScope.OfMembers(3, 4));
            TestAssert.That(b.Detached && b.Element == 1 && b.From == 0, "3 and 4 detach out of A into B");
            settings.Copy(b.From, b.Element);
            TestAssert.That(!settings.HasDoctrine(1), "an element detached out of A follows the wing");
            settings.SetDoctrine(1, hold);
            settings.SetShape(1, "trail");

            // Out of B (HOLD FIRE, its own shape) into C: C keeps B's, not the wing's long reach.
            ScopeTarget c = roster.Resolve(WingScope.OfMembers(4));
            TestAssert.That(c.Detached && c.Element == 2 && c.From == 1, "4 detaches out of B into C");
            settings.Copy(c.From, c.Element);
            TestAssert.Eq(settings.DoctrineOf(2, wing), hold, "C flies B's doctrine");
            TestAssert.Eq(settings.ShapeOf(2, "wedge"), "trail", "C flies B's shape");

            // An aircraft's own doctrine goes: it follows its element again.
            var own = new MemberDoctrines();
            own.SetOverride(7, DoctrineAxis.Weapons, (byte)WeaponsPolicy.Missiles);
            WingDoctrine guns = wing.With(DoctrineAxis.Weapons, (byte)WeaponsPolicy.Guns);
            TestAssert.That(own.Has(7) && own.Resolve(7, guns).Weapons == WeaponsPolicy.Missiles, "#7 flies its own MISSILES");
            TestAssert.That(own.Forget(7) && !own.Has(7), "#7's own doctrine clears");
            TestAssert.Eq(own.Resolve(7, guns), guns, "#7 flies its element's doctrine again");
            TestAssert.That(!own.Forget(7), "nothing of its own left to clear");
        }
    }
}
