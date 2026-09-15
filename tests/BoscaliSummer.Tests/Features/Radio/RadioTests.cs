using System;
using System.Collections.Generic;
using System.IO;
using BoscaliSummer.Features.Radio.Presentation;
using BoscaliSummer.Features.Radio.Runtime;

namespace BoscaliSummer.Tests.Features.Radio
{
    internal static class RadioTests
    {
        public static void Run()
        {
            HuntMusicTransitions();
            DialAndProgramming();
            TuningMath();
            PropagationMath();
            SpectrumRows();
            VanillaHoldRules();
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
        }

        private static void TuningMath()
        {
            TestAssert.That(RadioDialTuning.Step(RadioDial.Fm(88500), -1).Equals(RadioDial.Fm(88400)),
                "an FM tuning step did not move one 100 kHz increment");
            TestAssert.That(RadioDialTuning.Step(RadioDial.Air(121500), 1).Equals(RadioDial.Air(121525)),
                "the VHF air band did not step by its 25 kHz channel");
            TestAssert.That(RadioDialTuning.FineStep(RadioDial.Air(121500), 1, 5).Equals(RadioDial.Air(121505)),
                "a fine step did not divide the air band channel by five");
            TestAssert.That(RadioDialTuning.Step(RadioDial.Fm(RadioDial.FmMinKilohertz), -1)
                .Equals(RadioDial.Fm(RadioDial.FmMinKilohertz)),
                "the FM dial stepped below its band edge");
            TestAssert.That(RadioDialTuning.Step(RadioDial.Fm(RadioDial.FmMaxKilohertz), 1)
                .Equals(RadioDial.Fm(RadioDial.FmMaxKilohertz)),
                "the FM dial stepped above its band edge");
            TestAssert.That(RadioDialTuning.Step(RadioDial.Mw(780), 1).Equals(RadioDial.Mw(790)) &&
                RadioDialTuning.Step(RadioDial.Mw(780), -1).Equals(RadioDial.Mw(770)),
                "the MW dial stepped by the wrong increment");

            TestAssert.That(RadioBands.Next(RadioBand.Fm) == RadioBand.Air &&
                RadioBands.Next(RadioBand.Air) == RadioBand.Mw &&
                RadioBands.Next(RadioBand.Mw) == RadioBand.Fm,
                "the band knob did not cycle FM → VHF → MW");
            TestAssert.That(RadioDial.Air(118000).FrequencyText == "118.000" &&
                RadioDial.Air(118000).UnitText == "MHz" &&
                RadioDial.Mw(780).FrequencyText == "780",
                "air band or MW formatting lost its precision");
            TestAssert.That(RadioBands.Modulation(RadioBand.Fm) == RadioModulation.Fm &&
                RadioBands.Modulation(RadioBand.Air) == RadioModulation.Am &&
                RadioBands.Modulation(RadioBand.Mw) == RadioModulation.Am,
                "a band reported the wrong modulation");

            var dials = new[] { RadioDial.Fm(88500), RadioDial.Fm(101900), RadioDial.Mw(780) };
            TestAssert.That(RadioDialTuning.IndexAt(dials, RadioDial.Fm(101900)) == 1 &&
                RadioDialTuning.IndexAt(dials, RadioDial.Fm(90000)) == -1,
                "dial locking did not match exact station frequencies");
            TestAssert.That(RadioDialTuning.IndexAt(dials, RadioDial.Fm(88550)) == -1,
                "a half-step off a station still locked onto it");
            TestAssert.That(RadioDialTuning.Seek(dials, RadioDial.Fm(88500), 1) == 1,
                "seek did not find the next FM station up");
            TestAssert.That(RadioDialTuning.Seek(dials, RadioDial.Fm(101900), 1) == 0,
                "seek did not wrap to the low end of the band");
            TestAssert.That(RadioDialTuning.Seek(dials, RadioDial.Fm(101900), -1) == 0,
                "seek down did not find the previous FM station");
            TestAssert.That(RadioDialTuning.Seek(dials, RadioDial.Mw(780), 1) == 2,
                "seek left the current band");
            TestAssert.That(RadioDialTuning.Seek(dials, RadioDial.Fm(90000), 1) == 1 &&
                RadioDialTuning.Seek(dials, RadioDial.Fm(90000), -1) == 0,
                "seek from between stations did not pick the nearest station each way");
            TestAssert.That(RadioDialTuning.FirstInBand(dials, RadioBand.Mw) == 2 &&
                RadioDialTuning.FirstInBand(dials, RadioBand.Fm) == 0,
                "first-in-band did not find the lowest station of the band");
        }

        private static void PropagationMath()
        {
            TestAssert.That(Math.Abs(RadioPropagation.HorizonKilometres(100f, 100f) - 82.4f) < 0.1f,
                "the radio horizon did not use 4.12*(sqrt(tx)+sqrt(rx))");
            TestAssert.That(RadioPropagation.HorizonKilometres(2500f, 100f) > 240f &&
                RadioPropagation.HorizonKilometres(2500f, 100f) < 250f,
                "altitude did not extend the radio horizon");

            TestAssert.That(RadioPropagation.FreeSpaceLossDb(10f) < RadioPropagation.FreeSpaceLossDb(100f) &&
                Math.Abs(RadioPropagation.FreeSpaceLossDb(10f) - RadioPropagation.FreeSpaceLossDb(1f) - 20f) < 0.01f,
                "free-space loss was not 20 dB per decade");

            RadioReception near = RadioPropagation.Evaluate(
                10f, 40f, 3000f, true, RadioModulation.Fm, RadioModulation.Fm);
            RadioReception far = RadioPropagation.Evaluate(
                180f, 40f, 100f, true, RadioModulation.Fm, RadioModulation.Fm);
            TestAssert.That(near.Quality > 0.9f, "a nearby station did not read as a strong signal");
            TestAssert.That(far.Quality < near.Quality && far.BeyondHorizon,
                "distance or the horizon did not attenuate reception");

            RadioReception blocked = RadioPropagation.Evaluate(
                30f, 40f, 500f, false, RadioModulation.Am, RadioModulation.Am);
            RadioReception clear = RadioPropagation.Evaluate(
                30f, 40f, 500f, true, RadioModulation.Am, RadioModulation.Am);
            TestAssert.That(blocked.Quality < clear.Quality && blocked.PenaltyDb > 0f,
                "terrain line of sight did not cost signal");

            RadioReception amBlocked = RadioPropagation.Evaluate(
                30f, 40f, 500f, false, RadioModulation.Am, RadioModulation.Am);
            RadioReception fmBlocked = RadioPropagation.Evaluate(
                30f, 40f, 500f, false, RadioModulation.Fm, RadioModulation.Fm);
            TestAssert.That(fmBlocked.Quality > amBlocked.Quality,
                "FM did not tolerate terrain blockage better than AM");

            RadioReception garble = RadioPropagation.Evaluate(
                10f, 40f, 3000f, true, RadioModulation.Fm, RadioModulation.Am);
            TestAssert.That(garble.Quality < near.Quality && garble.PenaltyDb >= RadioPropagation.ModeMismatchPenaltyDb,
                "a mode mismatch did not garble an otherwise strong signal");

            TestAssert.That(near.Open(0.4f) && !far.Open(0.4f),
                "squelch opened on a weak channel");
            TestAssert.That(!garble.Open(0.6f),
                "squelch stayed open on a heavily degraded signal");
            TestAssert.That(RadioPropagation.StaticFor(1f) == 0f &&
                RadioPropagation.StaticFor(0f) > 0.5f,
                "the static curve did not quiet a strong signal");
            TestAssert.That(RadioPropagation.Evaluate(200f, 40f, 30f, true,
                RadioModulation.Am, RadioModulation.Am).SUnits < 1f,
                "a dead frequency still reported a strong S-meter");
            TestAssert.That(near.SReport.StartsWith("S", StringComparison.Ordinal),
                "the S-meter report lost its unit");
        }

        private static void SpectrumRows()
        {
            const int bins = RadioSpectrum.DefaultBins;
            var row = new float[bins];
            var signals = new List<RadioSignal>
            {
                new RadioSignal(88500f, 1f),
                new RadioSignal(101900f, 0.6f)
            };

            RadioSpectrum.Fill(row, signals, RadioBands.FmMinKilohertz, RadioBands.FmMaxKilohertz, 0.07f, 12.5f);
            float carrier = RadioSpectrum.Level(row, 88500f, RadioBands.FmMinKilohertz, RadioBands.FmMaxKilohertz);
            float weakCarrier = RadioSpectrum.Level(row, 101900f, RadioBands.FmMinKilohertz, RadioBands.FmMaxKilohertz);
            float silence = RadioSpectrum.Level(row, 96000f, RadioBands.FmMinKilohertz, RadioBands.FmMaxKilohertz);
            TestAssert.That(carrier > 0.8f && carrier > weakCarrier && weakCarrier > silence,
                "a station carrier did not stand above the noise floor");
            TestAssert.That(silence <= 0.07f, "the noise floor exceeded its ceiling");

            var repeat = new float[bins];
            RadioSpectrum.Fill(repeat, signals, RadioBands.FmMinKilohertz, RadioBands.FmMaxKilohertz, 0.07f, 12.5f);
            TestAssert.That(Math.Abs(repeat[10] - row[10]) < 0.0001f,
                "the same frame and time produced a different spectrum");

            var shifted = new float[bins];
            RadioSpectrum.Fill(shifted, signals, RadioBands.FmMinKilohertz, RadioBands.FmMaxKilohertz, 0.07f, 13.5f);
            TestAssert.That(Math.Abs(shifted[10] - row[10]) > 0.0001f,
                "the noise floor never moved between frames");

            var outOfBand = new List<RadioSignal> { new RadioSignal(780f, 1f) };
            var air = new float[bins];
            RadioSpectrum.Fill(air, outOfBand, RadioBands.AirMinKilohertz, RadioBands.AirMaxKilohertz, 0.05f, 1f);
            float airLevel = RadioSpectrum.Level(air, 121500f,
                RadioBands.AirMinKilohertz, RadioBands.AirMaxKilohertz);
            TestAssert.That(airLevel <= 0.05f, "an MW carrier leaked into the VHF spectrum");

            RadioSpectrum.Fill(row, null, RadioBands.FmMinKilohertz, RadioBands.FmMaxKilohertz, 0.07f, 2f);
            TestAssert.That(RadioSpectrum.Level(row, 88500f, RadioBands.FmMinKilohertz, RadioBands.FmMaxKilohertz) <= 0.07f,
                "an empty band still showed a carrier");
        }

        private static void VanillaHoldRules()
        {
            var hold = new VanillaMusicHold();
            TestAssert.That(!hold.Held, "an untouched receiver must not hold the soundtrack");

            hold.EngageReceiver();
            TestAssert.That(hold.Held, "an on-air receiver did not hold the soundtrack");
            hold.ReleaseReceiver();
            TestAssert.That(!hold.Held, "STOP did not hand the soundtrack back");

            hold.SetDeckEngaged(true);
            hold.EngageReceiver();
            hold.ReleaseReceiver();
            TestAssert.That(hold.Held, "stopping the receiver released the deck's hold");
            hold.SetDeckEngaged(false);
            TestAssert.That(!hold.Held, "the deck kept the soundtrack after it stopped");

            hold.EngageReceiver();
            hold.Reset();
            TestAssert.That(!hold.Held, "a scene reset kept the previous hold");
        }

        private static void DialAndProgramming()
        {
            TestAssert.That(RadioDialAllocation.TryBuiltIn(BuiltInStationRules.AgrapolId, out RadioDial agrapol) &&
                agrapol.IsFm && agrapol.FullText == "88.5 MHz",
                "Agrapol dial was not the canonical FM frequency");
            TestAssert.That(RadioDialAllocation.TryBuiltIn(BuiltInStationRules.BaseId, out RadioDial baseDial) &&
                !baseDial.IsFm && baseDial.FullText == "780 kHz",
                "Base dial was not the canonical MW frequency");
            TestAssert.That(RadioDial.Fm(101900).FrequencyText == "101.9" &&
                RadioDial.Fm(100000).FrequencyText == "100.0",
                "FM frequency formatting lost a decimal");
            TestAssert.That(!RadioDialAllocation.TryBuiltIn("user-thing", out _),
                "a user station id claimed a built-in dial");

            var used = new HashSet<int>();
            RadioDial first = RadioDialAllocation.Allocate("Alpha Network", used);
            RadioDial repeated = RadioDialAllocation.Allocate("Alpha Network", new HashSet<int>());
            TestAssert.That(first.Equals(repeated), "dial allocation was not deterministic");
            TestAssert.That(first.IsFm && first.Fraction >= 0f && first.Fraction <= 1f,
                "allocated dial left the FM band");
            TestAssert.That(RadioDialAllocation.TryFmSlot(first, out int slot) &&
                slot >= 0 && slot < RadioDialAllocation.FmSlotCount,
                "allocated dial was not on the FM slot grid");
            TestAssert.That(!RadioDialAllocation.Allocate("Bravo Network", used).Equals(first),
                "colliding stations were stacked on one frequency");
            TestAssert.That(!RadioDialAllocation.TryFmSlot(baseDial, out _),
                "an MW dial claimed an FM slot");

            var full = new HashSet<int>();
            for (int i = 0; i < RadioDialAllocation.FmSlotCount; i++)
                RadioDialAllocation.Allocate("Station " + i, full);
            TestAssert.That(full.Count == RadioDialAllocation.FmSlotCount,
                "the dial allocator reused a slot before the band was full");

            DateTime morning = new DateTime(2026, 1, 1, 8, 0, 0);
            string morningShow = RadioProgramming.ProgramName(BuiltInStationRules.AgrapolId, morning);
            TestAssert.That(morningShow == RadioProgramming.ProgramName(
                BuiltInStationRules.AgrapolId, morning.AddHours(1)),
                "one daypart chose two different programmes");
            TestAssert.That(morningShow != RadioProgramming.ProgramName(
                BuiltInStationRules.AgrapolId, new DateTime(2026, 1, 1, 2, 0, 0)),
                "the night programme matched the morning programme");
            TestAssert.That(
                RadioProgramming.DaypartAt(new DateTime(2026, 1, 1, 4, 59, 0)) == RadioDaypart.Night &&
                RadioProgramming.DaypartAt(new DateTime(2026, 1, 1, 5, 0, 0)) == RadioDaypart.Morning &&
                RadioProgramming.DaypartAt(new DateTime(2026, 1, 1, 21, 59, 0)) == RadioDaypart.Evening &&
                RadioProgramming.DaypartAt(new DateTime(2026, 1, 1, 22, 0, 0)) == RadioDaypart.Night,
                "daypart boundaries drifted");

            int genericCount = RadioProgramming.BulletinCount("user-unknown");
            TestAssert.That(genericCount > 0 && genericCount <= RadioProgramming.MaximumBulletins &&
                RadioProgramming.BulletinCount(BuiltInStationRules.MarisId) <= RadioProgramming.MaximumBulletins,
                "bulletin tables were empty or exceeded their bound");
            TestAssert.That(RadioProgramming.Bulletin("user-unknown", 999) ==
                RadioProgramming.Bulletin("user-unknown", 999 % genericCount),
                "bulletin rotation did not wrap within its table");
        }

        private static void HuntMusicTransitions()
        {
            var gate = new HuntMusicGate();
            TestAssert.That(!gate.Begin(false, 0) && !gate.Begin(true, 0) && gate.Begin(true, 1) && !gate.Begin(true, 1),
                "hunt music must start exactly once on the active transition");
            gate.Suppress(1);
            TestAssert.That(!gate.Begin(true, 1) && !gate.Begin(true, 1),
                "active hunt polling restarted manually stopped music");
            TestAssert.That(!gate.Begin(false, 0) && !gate.Begin(true, 1),
                "recovering the same hunt after a timeout must not replay its music");
            TestAssert.That(!gate.Begin(false, 0) && gate.Begin(true, 2),
                "manual stop prevented the next distinct hunt from changing music");
            gate.Reset();
            gate.Suppress(3);
            TestAssert.That(!gate.Begin(true, 3),
                "manual stop just before the first hunt poll lost to automatic playback");
            gate.Reset();
            TestAssert.That(gate.Begin(true, 3), "scene reset retained the previous hunt latch");
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
