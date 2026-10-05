using BoscaliSummer.Modules.Command.Presentation.MapUi;

namespace BoscaliSummer.Tests.Features.Command
{
    internal static class SettingsTests
    {
        public static void Run()
        {
            // Procedural patterns were retired. The saved custom id (3) remains valid.
            for (int preset = 0; preset < 3; preset++)
            {
                TestAssert.That(SettingsChoices.BackgroundName(true, true, true, preset) == "MATTE",
                    "Retired layered backgrounds resolve to matte");
            }
            foreach (int direction in new[] { -1, 1 })
            {
                TestAssert.That(SettingsChoices.CycleBackground(false, false, false, 0, direction) == 1,
                    "Both directions from matte reach custom");
                TestAssert.That(SettingsChoices.CycleBackground(false, false, true, 3, direction) == 0,
                    "Both directions from custom reach matte");
            }
            TestAssert.That(SettingsChoices.BackgroundName(true, true, true, 3) == "CUSTOM" &&
                SettingsChoices.BackgroundName(false, false, false, 3) == "MATTE",
                "Only an enabled custom image uses the persisted custom id");
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
