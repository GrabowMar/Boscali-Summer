using BoscaliSummer.Features.TheaterOps.Domain;

namespace BoscaliSummer.Tests.Features.TheaterOps
{
    internal static class NavalTaskTests
    {
        public static void Run()
        {
            TestAssert.That(!NavalTask.CanRedirect(false, false, false, false, true) &&
                !NavalTask.CanRedirect(true, true, false, false, true) &&
                !NavalTask.CanRedirect(true, false, true, false, true) &&
                !NavalTask.CanRedirect(true, false, false, true, true) &&
                !NavalTask.CanRedirect(true, false, false, false, false) &&
                NavalTask.CanRedirect(true, false, false, false, true),
                "naval tasking yields to authority, commands, holds, and combat");

            NavalTask.Position(NavalRole.Patrol, 100f, 200f, 2, 0,
                out float firstX, out float firstZ);
            NavalTask.Position(NavalRole.Patrol, 100f, 200f, 3, 0,
                out float secondX, out float secondZ);
            NavalTask.Position(NavalRole.Patrol, 100f, 200f, 2, 1,
                out float nextX, out float nextZ);
            TestAssert.That(firstX > 100f && secondX < 100f &&
                firstZ > 200f && secondZ > 200f && nextX == firstX && nextZ < 200f,
                "patrol ships separate and reverse their legs");
        }
    }
}
