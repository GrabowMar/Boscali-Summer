using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>HOLD / RESUME and PACE as pure key edits, and the window's column budgets.</summary>
    internal static class MetOfficeTapeTests
    {
        public static void Run()
        {
            HoldKeepsPinAndPosition();
            ResumeKeepsPin();
            UnpinnedHoldKeepsTheClassOnScreen();
            PaceNeverTouchesHoldOrPin();
            PaceStepClamps();
            EveryColumnFits();
        }

        private static WeatherKey Moving(byte pin = WeatherKey.None) =>
            new WeatherKey(1234u, 100f, true, (byte)WeatherRegimeType.Fair, skyPin: pin, systemSpeed: 27f, offset: 5000f);

        private static void HoldKeepsPinAndPosition()
        {
            WeatherKey key = Moving(26);
            TapeSwitchPlan plan = TapeSwitch.Plan(key, false, 400f, 27f, (byte)WeatherRegimeType.Broken);
            TestAssert.That(!plan.Key.Dynamic && plan.Key.SkyPin == 26, "holding a pinned sky keeps the pin");
            TestAssert.Near((float)plan.Key.OffsetAt(9999.0), (float)key.OffsetAt(400.0), "the tape position is continuous across a hold");
            TestAssert.That(!plan.TapeMoves && !plan.ConsoleAuto && plan.ManualOverride, "a held tape is a console hold: fog, light and wind keep following the field");
            TestAssert.That(plan.Key.TapeId == key.TapeId && plan.Key.SameTape(key), "a hold is a rebase, never a cut");
        }

        private static void ResumeKeepsPin()
        {
            WeatherKey held = Moving(26).Rebased(400f, false);
            TapeSwitchPlan plan = TapeSwitch.Plan(held, true, 500f, 34f, (byte)WeatherRegimeType.Broken);
            TestAssert.That(plan.Key.Dynamic && plan.Key.SkyPin == 26 && plan.Key.SystemSpeed == 34f, "resuming a pinned sky keeps the pin and takes the new speed");
            TestAssert.That(plan.TapeMoves && plan.ConsoleAuto && plan.ManualOverride, "a pinned sky stays a manual override even while the tape runs");
            TestAssert.Near((float)plan.Key.OffsetAt(500.0), (float)held.OffsetAt(500.0), "resume does not move the tape");
        }

        private static void UnpinnedHoldKeepsTheClassOnScreen()
        {
            WeatherKey key = Moving();
            TapeSwitchPlan hold = TapeSwitch.Plan(key, false, 300f, 27f, (byte)WeatherRegimeType.Overcast);
            TestAssert.That(hold.Key.MissionClass == (byte)WeatherRegimeType.Overcast && hold.ManualOverride, "an unpinned hold opens on the class showing (interim)");
            TapeSwitchPlan resume = TapeSwitch.Plan(hold.Key, true, 600f, 27f, (byte)WeatherRegimeType.Overcast);
            TestAssert.That(resume.Key.Dynamic && !resume.ManualOverride && resume.ConsoleAuto && resume.Key.SkyPin == WeatherKey.None, "an unpinned resume hands the sky back to the tape");
        }

        private static void PaceNeverTouchesHoldOrPin()
        {
            WeatherKey pinnedHeld = Moving(26).Rebased(200f, false);
            WeatherKey faster = TapeSwitch.Pace(pinnedHeld, 300f, 40f, (byte)WeatherRegimeType.Storm);
            TestAssert.That(!faster.Dynamic && faster.SkyPin == 26 && faster.SystemSpeed == 40f && faster.MissionClass == pinnedHeld.MissionClass, "pace changes only the speed");
            WeatherKey running = Moving();
            WeatherKey slower = TapeSwitch.Pace(running, 300f, 18f, (byte)WeatherRegimeType.Overcast);
            TestAssert.That(slower.Dynamic && slower.MissionClass == (byte)WeatherRegimeType.Overcast, "a running tape keeps running at a new pace and keeps the class on screen (interim)");
            TestAssert.Near((float)slower.OffsetAt(300.0), (float)running.OffsetAt(300.0), "pace is SMOOTH: the tape does not jump");
        }

        private static void PaceStepClamps()
        {
            TestAssert.That(TapeSwitch.Step(1, -1) == 1 && TapeSwitch.Step(6, 1) == 6 && TapeSwitch.Step(3, 1) == 4 && TapeSwitch.Step(3, -1) == 2, "pace steps one and stops at 1 and 6");
        }

        private static void EveryColumnFits()
        {
            TestAssert.That(MetOfficeLayout.Height <= 700f && MetOfficeLayout.Width <= 1260f, "the window fits the 1280 x 720 reference canvas");
            TestAssert.Near(MetOfficeLayout.Col23, 334f, "the two instrument columns are 334 wide");
            for (int c = 1; c <= 3; c++)
                TestAssert.That(MetOfficeLayout.ColumnHeight(c) <= MetOfficeLayout.BodyHeight,
                    "column " + c + " needs " + MetOfficeLayout.ColumnHeight(c) + " of " + MetOfficeLayout.BodyHeight);
            TestAssert.That(MetOfficeLayout.CommandTop + MetOfficeLayout.Command + MetOfficeLayout.Gap + MetOfficeLayout.Footer <= MetOfficeLayout.Height + 0.01f, "command bar and footer sit inside the window");
            TestAssert.That(MetOfficeLayout.BodyTop + MetOfficeLayout.BodyHeight <= MetOfficeLayout.CommandTop, "the body ends above the command bar");
        }
    }
}
