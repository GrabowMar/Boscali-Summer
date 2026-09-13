using BoscaliSummer.Features.Command.Presentation.MapUi;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class SettingsTests
    {
        public static void Run()
        {
            TestAssert.That(SettingsChoices.BackgroundName(true, false, true, 0) == "MIXED",
                "Existing layered backgrounds must be described honestly without changing their values");
            TestAssert.That(SettingsChoices.CycleBackground(true, false, true, 0, 1) == 0,
                "Replacing a mixed background starts with plain");
            TestAssert.That(SettingsChoices.CycleBackground(false, false, false, 0, -1) == 6,
                "Previous from plain wraps to custom");
            for (int mode = 0; mode < 7; mode++)
            {
                TestAssert.That(SettingsChoices.CycleBackground(mode == 1, mode == 2, mode >= 3, mode - 3, 1) == (mode + 1) % 7,
                    "Every background choice is reachable, including wraparound");
            }
            byte[] png = { 137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82,
                0, 0, 16, 0, 0, 0, 8, 0 };
            TestAssert.That(SettingsChoices.SupportedImage(png), "4096x2048 PNG header fits the decode budget");
            png[19] = 1;
            TestAssert.That(!SettingsChoices.SupportedImage(png), "Oversized PNG is rejected before decoding");
            png[18] = png[19] = 0;
            TestAssert.That(!SettingsChoices.SupportedImage(png), "Zero-width PNG is rejected");
            TestAssert.That(!SettingsChoices.SupportedImage(null), "Missing bytes are rejected");
            var jpeg = new byte[24];
            byte[] header = { 255, 216, 255, 192, 0, 17, 8, 4, 56, 7, 128, 3 };
            header.CopyTo(jpeg, 0);
            TestAssert.That(SettingsChoices.SupportedImage(jpeg), "1920x1080 JPEG header fits the decode budget");
            jpeg[9] = 32;
            TestAssert.That(!SettingsChoices.SupportedImage(jpeg), "Oversized JPEG is rejected before decoding");
            jpeg[5] = 255;
            TestAssert.That(!SettingsChoices.SupportedImage(jpeg), "Truncated JPEG segment cannot read outside the buffer");
        }
    }
}
