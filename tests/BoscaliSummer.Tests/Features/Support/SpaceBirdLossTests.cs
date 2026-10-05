using BoscaliSummer.Modules.Support.Domain.Space;

namespace BoscaliSummer.Tests.Features.Support
{
    /// <summary>M6a: an ASAT kills one bird; its tasks stop, a reservation in flight is voided, and the bird returns through the 400 CR bar and a 6 minute build (space spec 5.2).</summary>
    internal static class SpaceBirdLossTests
    {
        public static void Run()
        {
            var s = new SpaceState(2);
            TestAssert.That(s.HasBird(BirdKind.Optical) && s.HasBird(BirdKind.Radar) && s.HasBird(BirdKind.Kinetic), "all three birds start up");
            TestAssert.That(s.CanStart(BirdTask.Camera, 10f) && s.CanStart(BirdTask.Scan, 10f), "tasks start while the birds are up");

            // A task reserved on the OPTICAL bird when the strike lands can never commit.
            TestAssert.That(s.TryReserve(BirdTask.Camera, 10f, out SpaceTaskReservation reservation), "reserve the camera");
            TestAssert.That(s.KillBird(BirdKind.Optical, 20f), "the ASAT kills the OPTICAL bird");
            TestAssert.That(!s.KillBird(BirdKind.Optical, 21f), "a dead bird cannot die twice");
            TestAssert.That(!s.CanCommit(reservation, 22f, 5f) && !s.Commit(reservation, 22f, 5f), "the reservation in flight is void");
            TestAssert.That(!s.HasBird(BirdKind.Optical) && s.BirdDown(BirdKind.Optical), "the bird is gone");
            TestAssert.That(s.DownMask == 1, "bit 0 marks OPTICAL");
            TestAssert.That(!s.CanStart(BirdTask.Camera, 30f) && float.IsPositiveInfinity(s.ReadyIn(BirdTask.Camera, 30f)), "the camera cannot start and has no ETA");
            TestAssert.That(s.CanStart(BirdTask.Scan, 30f) && s.CanStart(BirdTask.Rod, 30f), "the other birds are untouched");
            TestAssert.That(s.Family(30f) == SpaceFamilyState.Normal, "uplinks decide the family, not the birds");
            TestAssert.That(!s.KillBird((BirdKind)9, 30f) && !s.KillBird(BirdKind.Radar, float.NaN), "a bad bird or time kills nothing");

            // Rebuild: the bar (400) first, then six minutes.
            TestAssert.That(s.TickBirds(40f) == 0, "nothing returns without a full bar");
            TestAssert.That(s.RebuildPercent(BirdKind.Optical, 40f) == 0, "0 % to start");
            s.BirdBar(BirdKind.Optical).Fund(200f);
            TestAssert.That(s.RebuildPercent(BirdKind.Optical, 40f) == 25, "half the bar reads 25 %");
            s.BirdBar(BirdKind.Optical).Fund(300f);
            TestAssert.That(s.BirdBar(BirdKind.Optical).Complete, "the bar is full at 400");
            TestAssert.That(s.TickBirds(100f) == 0, "a full bar starts the build, it does not finish it");
            TestAssert.That(s.RebuildPercent(BirdKind.Optical, 100f) == 50, "the build starts at 50 %");
            TestAssert.That(s.RebuildPercent(BirdKind.Optical, 280f) == 75, "three minutes into the build: 75 %");
            TestAssert.That(s.TickBirds(459f) == 0, "still building at 359 s");
            TestAssert.That(s.TickBirds(460.5f) == 1, "back after 360 s: OPTICAL restored");
            TestAssert.That(s.HasBird(BirdKind.Optical) && s.DownMask == 0, "the bird is up again");
            TestAssert.That(s.CanStart(BirdTask.Camera, 461f), "and its task starts");
            TestAssert.That(s.BirdBar(BirdKind.Optical).Value == 0f, "the bar is reset for a next loss");
            TestAssert.That(s.KillBird(BirdKind.Optical, 470f), "it can be shot down again");
        }
    }
}
