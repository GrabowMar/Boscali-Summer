using BoscaliSummer.Core.Game;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The shared mission-clock gate (spec 9.5): what counts as a valid read, and how a
    /// clock that starts late, glitches or resets behaves.</summary>
    internal static class WeatherClockTests
    {
        public static void Run()
        {
            GateTable();
            ClockRampsHoldsAndResets();
            SameFrameReadsShareOneCap();
        }

        private static void GateTable()
        {
            TestAssert.That(MissionClockGate.IsValid(true, false, 0d, 12f), "single player with a manager reads");
            TestAssert.That(MissionClockGate.IsValid(true, false, 0d, 0f), "zero is a valid mission start");
            TestAssert.That(!MissionClockGate.IsValid(false, false, 0d, 12f), "no clock source, no clock");
            TestAssert.That(!MissionClockGate.IsValid(true, false, 0d, float.NaN), "NaN is refused");
            TestAssert.That(!MissionClockGate.IsValid(true, false, 0d, float.PositiveInfinity), "+infinity is refused");
            TestAssert.That(!MissionClockGate.IsValid(true, false, 0d, float.NegativeInfinity), "-infinity is refused");
            TestAssert.That(!MissionClockGate.IsValid(true, false, 0d, -0.5f), "negative time is refused");
            TestAssert.That(!MissionClockGate.IsValid(true, true, 0d, 40f), "multiplayer before the shared start is lobby time");
            TestAssert.That(!MissionClockGate.IsValid(true, true, -1d, 40f), "a negative shared start is refused");
            TestAssert.That(MissionClockGate.IsValid(true, true, 3.5d, 40f), "multiplayer after the shared start reads");
            TestAssert.That(MissionClockGate.Converged(5000f, 5000f) && MissionClockGate.Converged(100f, 99f) &&
                !MissionClockGate.Converged(600f, 5000f), "a held value below the raw read has not converged");
        }

        private static void ClockRampsHoldsAndResets()
        {
            var clock = new MissionClock();
            int frame = 0;

            // A clock that starts at 5000 s is eased in at MaxStep per frame; the gate keeps Weather from
            // building anything until the held value has caught up with the raw read.
            float held = clock.Read(5000f, true, ++frame);
            TestAssert.That(held == 600f && !MissionClockGate.Converged(held, 5000f), "first read is capped at one step");
            int frames = 1;
            while (!MissionClockGate.Converged(held, 5000f) && frames < 30)
            {
                held = clock.Read(5000f, true, ++frame);
                frames++;
            }
            TestAssert.That(held == 5000f && frames == 9, "5000 s is reached after 9 frames, got " + held + " in " + frames);

            TestAssert.That(clock.Read(float.NaN, true, ++frame) == 5000f, "NaN holds the last good value");
            TestAssert.That(clock.Read(-3f, true, ++frame) == 5000f, "a negative read holds");
            TestAssert.That(clock.Read(9000f, false, ++frame) == 5000f, "an invalid read holds");
            TestAssert.That(clock.Read(4996f, true, ++frame) == 5000f, "a 4 s step back is a latched transient and holds");

            // The mission restarts: a raw value far below the latched one starts a new timeline.
            TestAssert.That(clock.Read(0f, true, ++frame) == 0f, "a reset to zero re-baselines");
            TestAssert.That(clock.Read(2f, true, ++frame) == 2f, "and keeps advancing");
            clock.Reset();
            TestAssert.That(clock.Last == 0f, "a scene reset clears the clock");
        }

        private static void SameFrameReadsShareOneCap()
        {
            var clock = new MissionClock();
            float a = clock.Read(100000f, true, 1);
            float b = clock.Read(100000f, true, 1);
            TestAssert.That(a == 600f && b == 600f, "two reads in one frame cannot add two steps");
            TestAssert.That(clock.Read(100000f, true, 2) == 1200f, "the next frame adds one");
        }
    }
}
