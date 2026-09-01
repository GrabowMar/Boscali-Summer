using System;
using System.IO;
using BoscaliSummer.Features.Radio.Presentation;
using BoscaliSummer.Features.Radio.Runtime;

namespace BoscaliSummer.Tests.Features.Radio
{
    internal static class RadioTests
    {
        public static void Run()
        {
            string root = Path.Combine(
                Path.GetTempPath(), "BoscaliSummer.RadioTests." + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                Directory.CreateDirectory(Path.Combine(root, "02 Night Ops"));
                Directory.CreateDirectory(Path.Combine(root, "01 Day Ops"));
                Directory.CreateDirectory(Path.Combine(root, "01 Day Ops", "Nested"));
                Directory.CreateDirectory(Path.Combine(root, "Empty Starter"));
                File.WriteAllBytes(Path.Combine(root, "Root Track.ogg"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(root, "Ignored.mp3"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(root, "01 Day Ops", "Bravo.wav"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(root, "01 Day Ops", "Alpha.ogg"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(root, "01 Day Ops", "Nested", "Too Deep.ogg"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(root, "02 Night Ops", "Night.OGG"), new byte[] { 1 });
                File.WriteAllBytes(Path.Combine(root, "Empty Starter", "station.png"), new byte[] { 1 });

                RadioLibrary library = RadioLibrary.Scan(root);
                TestAssert.That(library.TrackCount == 4, "radio scan accepted an unsupported or nested track");
                TestAssert.That(library.Channels.Length == 3, "radio scan produced the wrong station count");
                TestAssert.That(library.Channels[0].Name == "LOCAL", "root tracks did not become LOCAL");
                TestAssert.That(library.Channels[1].Name == "01 Day Ops", "stations were not sorted");
                TestAssert.That(library.Channels[1].Tracks[0].Title == "Alpha", "tracks were not sorted");
                TestAssert.That(RadioLibrary.IsSupportedExtension(".WAV"), "WAV extension was rejected");
                TestAssert.That(!RadioLibrary.IsSupportedExtension(".mp3"), "unprobed MP3 extension was accepted");

                TestAssert.That(BuiltInStationRules.ImportFolderNames.Length == 2 &&
                    Array.IndexOf(BuiltInStationRules.ImportFolderNames, "Agrapol FM") >= 0 &&
                    Array.IndexOf(BuiltInStationRules.ImportFolderNames, "Maris Network") >= 0 &&
                    Array.IndexOf(BuiltInStationRules.ImportFolderNames, "Base Broadcast") < 0,
                    "starter folders did not preserve the immutable Base station boundary");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }

            byte[] valid = MakePngHeader(256, 128);
            TestAssert.That(PngIconHeader.IsSupported(valid, out int width, out int height),
                "valid bounded PNG header was rejected");
            TestAssert.That(width == 256 && height == 128, "PNG dimensions were read incorrectly");
            TestAssert.That(!PngIconHeader.IsSupported(MakePngHeader(257, 128), out _, out _),
                "oversized PNG width was accepted");
            TestAssert.That(!PngIconHeader.IsSupported(MakePngHeader(128, 0), out _, out _),
                "zero-height PNG was accepted");
            valid[1] = 0;
            TestAssert.That(!PngIconHeader.IsSupported(valid, out _, out _),
                "invalid PNG signature was accepted");

            TestAssert.That(BuiltInStationRules.AcceptsLocalTracks(BuiltInStationRules.AgrapolId),
                "Agrapol station rejected local replacement tracks");
            TestAssert.That(BuiltInStationRules.AcceptsLocalTracks(BuiltInStationRules.MarisId),
                "Maris station rejected local replacement tracks");
            TestAssert.That(!BuiltInStationRules.AcceptsLocalTracks(BuiltInStationRules.BaseId),
                "Base station accepted local tracks");
            TestAssert.That(BuiltInStationRules.UsesVanillaTracks(BuiltInStationRules.AgrapolId, 0) &&
                !BuiltInStationRules.UsesVanillaTracks(BuiltInStationRules.AgrapolId, 1),
                "Agrapol fallback was not replaced by local tracks");
            TestAssert.That(BuiltInStationRules.UsesVanillaTracks(BuiltInStationRules.BaseId, 1),
                "Base station stopped using the original soundtrack when local files existed");

            CheckPalette();
        }

        private static void CheckPalette()
        {
            var darkMap = new RadioRgba(0.05f, 0.06f, 0.07f);
            var brightMap = new RadioRgba(0.85f, 0.85f, 0.85f);
            RadioRgba darkGround = RadioUiPalette.PanelGround.Over(darkMap);
            RadioRgba brightGround = RadioUiPalette.PanelGround.Over(brightMap);
            var accent = new RadioRgba(0.30f, 1f, 0.35f);

            TestAssert.That(RadioRgba.Contrast(RadioUiPalette.Dim, darkGround) >= 4.5f,
                "radio secondary text is unreadable over a dark map");
            TestAssert.That(RadioRgba.Contrast(RadioUiPalette.Dim, brightGround) >= 4.5f,
                "radio secondary text is unreadable over a bright map");
            TestAssert.That(RadioRgba.Contrast(darkGround, brightGround) <= 1.5f,
                "radio panel ground changes too much with the map beneath it");

            RadioUiPaint resting = RadioUiPalette.Paint(
                RadioButtonStyle.Toggle, accent, true, false, false, false);
            RadioUiPaint hovered = RadioUiPalette.Paint(
                RadioButtonStyle.Toggle, accent, true, false, true, false);
            RadioUiPaint selected = RadioUiPalette.Paint(
                RadioButtonStyle.Toggle, accent, true, true, false, false);
            RadioUiPaint pressed = RadioUiPalette.Paint(
                RadioButtonStyle.Toggle, accent, true, false, true, true);

            RadioRgba restFill = resting.Fill.Over(darkGround);
            RadioRgba hoverFill = hovered.Fill.Over(darkGround);
            RadioRgba selectedFill = selected.Fill.Over(darkGround);
            RadioRgba pressedFill = pressed.Fill.Over(darkGround);
            TestAssert.That(RadioRgba.Contrast(hoverFill, restFill) >= 1.25f,
                "radio button hover is indistinguishable from rest");
            TestAssert.That(RadioRgba.Contrast(selectedFill, hoverFill) >= 1.25f,
                "radio button selection is indistinguishable from hover");
            TestAssert.That(pressedFill.RelativeLuminance > selectedFill.RelativeLuminance,
                "radio button press is weaker than selection");

            RadioButtonStyle[] styles =
            {
                RadioButtonStyle.Default,
                RadioButtonStyle.Primary,
                RadioButtonStyle.Quiet,
                RadioButtonStyle.Toggle
            };
            foreach (RadioButtonStyle style in styles)
            foreach (bool latched in new[] { false, true })
            foreach (bool hover in new[] { false, true })
            foreach (bool isPressed in new[] { false, true })
            {
                RadioUiPaint paint = RadioUiPalette.Paint(
                    style, accent, true, latched, hover, isPressed);
                float contrast = RadioRgba.Contrast(
                    paint.Text, paint.Fill.Over(brightGround));
                TestAssert.That(contrast >= 4.5f,
                    "radio button label falls below readable contrast in style " + style);
            }
        }

        private static byte[] MakePngHeader(uint width, uint height)
        {
            byte[] data = new byte[24];
            byte[] signature = { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a };
            Array.Copy(signature, data, signature.Length);
            data[12] = (byte)'I';
            data[13] = (byte)'H';
            data[14] = (byte)'D';
            data[15] = (byte)'R';
            WriteBigEndian(data, 16, width);
            WriteBigEndian(data, 20, height);
            return data;
        }

        private static void WriteBigEndian(byte[] data, int offset, uint value)
        {
            data[offset] = (byte)(value >> 24);
            data[offset + 1] = (byte)(value >> 16);
            data[offset + 2] = (byte)(value >> 8);
            data[offset + 3] = (byte)value;
        }
    }
}
