using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Weather.Domain;

namespace BoscaliSummer.Tests.Features.Weather
{
    /// <summary>The key on the wire (spec 9.1): exact round trips, float32 bits, sentinels and the adoption rule.</summary>
    internal static class WeatherKeyWireTests
    {
        public static void Run()
        {
            RoundTripIsExact();
            Float32RoundingIsStable();
            SentinelTable();
            MasksAndRanges();
            AdoptionRule();
            ReuseKeepsTheInstance();
            TwoPeersAgree();
        }

        private static WeatherKeyWire.Fields Fields(byte story = 255, byte pin = 255, byte cls = 255, byte sets = 0) =>
            new WeatherKeyWire.Fields
            {
                Seed = 4242u, Story = story, SkyPin = pin, LayoutSalt = 9, MissionClass = cls, OpeningBase = 2400f,
                HeadingDeg = 271.5f, Epoch = 120f, Offset = -45000.25f, SystemSpeed = 27f, Dynamic = true, Sets = sets,
                HasAnchor = true, AnchorX = 1000f, AnchorZ = -2000f, SetOriginSquall = 3f, SetOriginSupercell = 4f, SetOriginCell = 5f,
            };

        private static void RoundTripIsExact()
        {
            var rng = new Random(20261009);
            for (int i = 0; i < 400; i++)
            {
                var key = new WeatherKey((uint)rng.Next(1, int.MaxValue), (float)(rng.NextDouble() * 100000), rng.Next(2) == 0,
                    (byte)(rng.Next(8) == 0 ? 255 : rng.Next(WeatherKey.ClassCount)), (byte)rng.Next(64), (byte)rng.Next(256),
                    rng.Next(2) == 0, (float)(rng.NextDouble() * 160000 - 80000), (float)(rng.NextDouble() * 80000 - 40000),
                    (byte)(rng.Next(6) == 0 ? 255 : rng.Next(WeatherKey.StoryCount)), (byte)(rng.Next(6) == 0 ? 255 : rng.Next(WeatherKey.SkyCount)),
                    rng.Next(3) == 0 ? 0f : rng.Next(60, 800) * 10f, (float)(rng.NextDouble() * 359.9999),
                    (float)(rng.NextDouble() * 6_000_000 - 3_000_000), 18f + (float)(rng.NextDouble() * 27),
                    (float)(rng.NextDouble() * 1e6), (float)(rng.NextDouble() * 1e6), (float)(rng.NextDouble() * 1e6));
                var warnings = new List<string>();
                WeatherKey back = WeatherKeyWire.ToKey(WeatherKeyWire.FromKey(key), warnings.Add);
                TestAssert.That(back.Equals(key) && warnings.Count == 0, "a key survives the wire unchanged (#" + i + ")");
                TestAssert.That(BitConverter.SingleToInt32Bits(back.Offset) == BitConverter.SingleToInt32Bits(key.Offset) &&
                    BitConverter.SingleToInt32Bits(back.HeadingDeg) == BitConverter.SingleToInt32Bits(key.HeadingDeg), "the float bits are identical");
                foreach (double t in new[] { key.Epoch, key.Epoch + 60.0, key.Epoch + 3600.0 })
                    TestAssert.That(Math.Abs(back.OffsetAt(t) - key.OffsetAt(t)) < 1.0, "OffsetAt survives the wire within 1 m");
                TestAssert.That(back.TapeId == key.TapeId && back.SameTape(key), "and so does the tape identity");
            }
        }

        private static void Float32RoundingIsStable()
        {
            // The host computes in double and stores float32; every peer then holds the same bits.
            double exact = 3_000_000.123456789;
            var key = new WeatherKey(1u, 0f, true, offset: (float)exact);
            TestAssert.That(key.Offset == (float)exact && (double)key.Offset != exact, "the key holds the rounded value");
            WeatherKey back = WeatherKeyWire.ToKey(WeatherKeyWire.FromKey(key));
            TestAssert.That(back.Offset == key.Offset && back.Equals(key), "rounding twice changes nothing");
            WeatherKey rebased = new WeatherKey(1u, 10f, true, offset: 2_999_000.5f, systemSpeed: 45f).Rebased(100000f);
            TestAssert.That(WeatherKeyWire.ToKey(WeatherKeyWire.FromKey(rebased)).Equals(rebased), "a rebased key is already float32");
        }

        private static void SentinelTable()
        {
            var warnings = new List<string>();
            WeatherKey passed = WeatherKeyWire.ToKey(Fields(255, 255, 255), warnings.Add);
            TestAssert.That(passed.Story == 255 && passed.SkyPin == 255 && passed.MissionClass == 255 && warnings.Count == 0,
                "255 passes through every sentinel field");

            WeatherKey edge = WeatherKeyWire.ToKey(Fields((byte)(WeatherKey.StoryCount - 1), (byte)(WeatherKey.SkyCount - 1),
                (byte)(WeatherKey.ClassCount - 1)), warnings.Add);
            TestAssert.That(edge.Story == WeatherKey.StoryCount - 1 && edge.SkyPin == WeatherKey.SkyCount - 1 &&
                edge.MissionClass == WeatherKey.ClassCount - 1 && warnings.Count == 0, "Count-1 is valid");

            warnings.Clear();
            WeatherKey over = WeatherKeyWire.ToKey(Fields((byte)WeatherKey.StoryCount, (byte)WeatherKey.SkyCount,
                (byte)WeatherKey.ClassCount), warnings.Add);
            TestAssert.That(over.Story == 255 && over.SkyPin == 255 && over.MissionClass == 255, "Count falls back to auto/none, never Storm");
            TestAssert.That(warnings.Count == 3, "each fallback is reported, got " + warnings.Count);

            warnings.Clear();
            WeatherKey junk = WeatherKeyWire.ToKey(Fields(254, 200, 7), warnings.Add);
            TestAssert.That(junk.Story == 255 && junk.SkyPin == 255 && junk.MissionClass == 255, "254, 200 and 7 are not table values and fall back to auto");
        }

        private static void MasksAndRanges()
        {
            TestAssert.That(WeatherKeyWire.ToKey(Fields(sets: 0xFF)).Sets == Superstructures.AllSets, "Sets are masked to the six console bits");
            for (int salt = 0; salt < 256; salt++)
            {
                WeatherKeyWire.Fields f = Fields();
                f.LayoutSalt = (byte)salt;
                TestAssert.That(WeatherKeyWire.ToKey(f).LayoutSalt == salt, "every layout salt passes");
            }
            WeatherKeyWire.Fields negative = Fields();
            negative.Offset = -3_000_000f;
            negative.HeadingDeg = 359.99997f;
            WeatherKey k = WeatherKeyWire.ToKey(negative);
            TestAssert.That(k.Offset == -3_000_000f && k.HeadingDeg == 359.99997f, "negative offsets and a heading near 360 survive");
            negative.HeadingDeg = 360f;
            TestAssert.That(WeatherKeyWire.ToKey(negative).HeadingDeg == 0f, "a heading of 360 wraps to 0");
            negative.HeadingDeg = float.NaN;
            TestAssert.That(WeatherKeyWire.ToKey(negative).HeadingDeg == WeatherKey.SeededHeading(negative.Seed), "a NaN heading takes the seeded one");
        }

        private static void AdoptionRule()
        {
            TestAssert.That(WeatherKeyWire.CanAdopt(false, false, 0d, 99999u), "a held key needs no clock check");
            TestAssert.That(WeatherKeyWire.CanAdopt(false, true, 5.0, 5000u), "a held key ignores clock skew");
            TestAssert.That(!WeatherKeyWire.CanAdopt(true, false, 0d, 100u), "a moving key waits for a local clock");
            TestAssert.That(WeatherKeyWire.CanAdopt(true, true, 1000.0, 1000u), "equal clocks adopt");
            TestAssert.That(WeatherKeyWire.CanAdopt(true, true, 1010.0, 1000u) && WeatherKeyWire.CanAdopt(true, true, 990.0, 1000u), "10 s apart adopts");
            TestAssert.That(!WeatherKeyWire.CanAdopt(true, true, 1010.5, 1000u) && !WeatherKeyWire.CanAdopt(true, true, 989.5, 1000u), "10.5 s apart waits");
            TestAssert.That(!WeatherKeyWire.CanAdopt(true, true, 1000.0 + 1e4, 1000u) && !WeatherKeyWire.CanAdopt(true, true, 1000.0 - 1e4, 1000u),
                "a client 10 000 s off keeps its previous key");
        }

        private static void ReuseKeepsTheInstance()
        {
            var a = new WeatherKey(7u, 100f, true, 3, headingDeg: 45f, offset: 1000f);
            WeatherKey copy = WeatherKeyWire.ToKey(WeatherKeyWire.FromKey(a));
            TestAssert.That(!ReferenceEquals(a, copy) && a.Equals(copy), "the wire builds a new but value-equal key");
            TestAssert.That(ReferenceEquals(WeatherKeyWire.Reuse(a, copy), a), "a value-equal resend returns the current instance");
            WeatherKey moved = a.Rebased(500f);
            TestAssert.That(ReferenceEquals(WeatherKeyWire.Reuse(a, moved), moved), "a changed key replaces it");
            TestAssert.That(ReferenceEquals(WeatherKeyWire.Reuse(null, a), a), "the first key is adopted");
        }

        private static void TwoPeersAgree()
        {
            // The host key and the key a client builds from the message fields alone, with no local LevelInfo.
            // The shared pure function of the key is the tape and the field.
            var hostField = new WeatherField();
            var clientField = new WeatherField();
            for (uint seed = 1; seed <= 500; seed++)
                for (byte cls = 0; cls < 7; cls++)
                    foreach (float openingBase in new[] { 0f, 2400f })
                    {
                        var host = new WeatherKey(seed, 100f, true, cls, openingBase: openingBase,
                            headingDeg: WeatherKey.SeededHeading(seed), systemSpeed: 31f, offset: 1234f);
                        WeatherKey client = WeatherKeyWire.ToKey(WeatherKeyWire.FromKey(host));
                        TestAssert.That(client.Equals(host) && client.TapeId == host.TapeId, "peers hold the same key and tape");
                        TestAssert.That(host.TapeId == client.TapeId && TapeGolden.Hash(WeatherTape.For(host)) == TapeGolden.Hash(WeatherTape.For(client)),
                            "both peers derive the same tape");
                        if (seed % 25 == 0)
                        {
                            hostField.Build(host, 900f, 40960f, 40960f);
                            clientField.Build(client, 900f, 40960f, 40960f);
                            WeatherPoint p = hostField.Sample(1500f, -800f), q = clientField.Sample(1500f, -800f);
                            TestAssert.That(p.Cover == q.Cover && p.RainRate == q.RainRate && p.WindX == q.WindX &&
                                hostField.PrevailingHeading == clientField.PrevailingHeading, "both peers derive the same sky and wind direction");
                        }
                    }
        }
    }
}
