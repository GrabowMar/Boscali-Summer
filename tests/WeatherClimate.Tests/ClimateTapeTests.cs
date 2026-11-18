using System;
using BoscaliSummer.Modules.Weather.Domain;

namespace WeatherClimateTests
{
    /// <summary>The climate byte in the weather key, on the wire and in the tape. Climate 0 is the weather of before: the tape identity, the tape and
    /// the field's air are pinned to values printed (<c>dotnet run -- --golden</c>) by the code as it was before the byte existed (commit 5f99de79).</summary>
    internal static class ClimateTapeTests
    {
        // seed, class, story, pin, salt, opening base
        private static readonly (uint Seed, byte Class, byte Story, byte Pin, byte Salt, float Base)[] Keys =
        {
            (1u, 1, 255, 255, 0, 0f), (1337u, 3, 255, 255, 0, 1450f), (4000000007u, 4, 255, 255, 7, 350f), (77u, 6, 255, 255, 0, 600f),
            (2026u, 0, 255, 255, 3, 2400f), (9u, 255, 255, 255, 0, 0f), (51u, 2, 1, 255, 0, 900f), (51u, 5, 3, 255, 9, 0f), (8u, 1, 255, 21, 0, 1200f),
        };

        // TapeId, tape digest, then the bits of Temperature, VisibilityKm and Dewpoint at (1000, -2000): the output of the old code.
        private static readonly (uint TapeId, uint Digest, int Temperature, int Visibility, int Dewpoint)[] Before =
        {
            (887363917u, 1964727122u, 1105199104, 1108869120, 1102699692),
            (716963936u, 678934189u, 1103626240, 1101004800, 1099512480),
            (2728024367u, 1638269087u, 1102053376, 1094713344, 1099844852),
            (2927811441u, 2385264474u, 1101004800, 1068342907, 1100742656),
            (3250982652u, 1366069152u, 1105723392, 1110704128, 1101021762),
            (743335593u, 364859759u, 1105199104, 1108869120, 1100546530),
            (3870960966u, 1233275983u, 1105199104, 1108869120, 1102005786),
            (3167492977u, 778094896u, 1105723392, 1110704128, 1101004800),
            (3671268705u, 449548355u, 1103626240, 1101004800, 1100763435),
        };

        internal static WeatherKey Key(int i, byte climate = 0) => new WeatherKey(Keys[i].Seed, 10f, false, Keys[i].Class, layoutSalt: Keys[i].Salt,
            story: Keys[i].Story, skyPin: Keys[i].Pin, openingBase: Keys[i].Base, headingDeg: 250f, climate: climate);

        internal static uint Digest(WeatherTape tape)
        {
            uint h = 2166136261u;
            void Mix(int v) { unchecked { h = (h ^ (uint)v) * 16777619u; } }
            foreach (TapeKeyframe k in tape.Keyframes) { Mix(k.SkyIndex); Mix(BitConverter.SingleToInt32Bits(k.S)); Mix(BitConverter.SingleToInt32Bits(k.Width)); Mix(k.Segment); Mix(BitConverter.SingleToInt32Bits(k.Sky.CloudBase)); }
            foreach (TapeSegment s in tape.Segments) { Mix(s.Story); Mix((int)s.Class); Mix(BitConverter.SingleToInt32Bits(s.S0)); }
            foreach (TapeFront f in tape.Fronts) { Mix(f.Story); Mix(f.Marker); Mix(BitConverter.SingleToInt32Bits(f.SMain)); }
            Mix(tape.Markers.Count); Mix((int)tape.OpeningClass); Mix(BitConverter.SingleToInt32Bits(tape.OpeningEnd));
            return h;
        }

        private static (int Temperature, int Visibility, int Dewpoint) Air(WeatherKey key)
        {
            var field = new WeatherField();
            field.Build(key, 10f, 40000f, 40000f, 15f);
            WeatherPoint p = field.Sample(1000f, -2000f);
            return (BitConverter.SingleToInt32Bits(p.Temperature), BitConverter.SingleToInt32Bits(p.VisibilityKm), BitConverter.SingleToInt32Bits(p.Dewpoint));
        }

        internal static void PrintGolden()
        {
            for (int i = 0; i < Keys.Length; i++)
            {
                WeatherKey key = Key(i);
                var air = Air(key);
                Console.WriteLine("            (" + key.TapeId + "u, " + Digest(WeatherTape.Generate(TapeInputs.From(key))) + "u, " + air.Temperature + ", " + air.Visibility + ", " + air.Dewpoint + "),");
            }
        }

        private static bool Holds(WeatherTape tape, WeatherRegimeType cls)
        {
            foreach (TapeKeyframe k in tape.Keyframes) if (SkyLibrary.ClassOf(k.SkyIndex) == cls) return true;
            return false;
        }

        private static bool Tells(WeatherTape tape, StoryKind story)
        {
            foreach (TapeSegment s in tape.Segments) if (s.Story == (int)story) return true;
            return false;
        }

        public static void Run()
        {
            WeatherClimate.Local = null;

            // Climate 0: the identity, the tape and the air of before, bit for bit.
            for (int i = 0; i < Keys.Length; i++)
            {
                WeatherKey key = Key(i);
                TestAssert.Eq(key.Climate, (byte)0, "key " + i + " has no climate");
                TestAssert.Eq(key.TapeId, Before[i].TapeId, "key " + i + " keeps its TapeId");
                TestAssert.Eq(Digest(WeatherTape.Generate(TapeInputs.From(key))), Before[i].Digest, "key " + i + " keeps its tape");
                TestAssert.Eq(Air(key), (Before[i].Temperature, Before[i].Visibility, Before[i].Dewpoint), "key " + i + " keeps its air");
            }

            // A climate is another tape of the same seed: Hash(old, climate, 0xC11A), a different one per climate.
            for (int i = 0; i < Keys.Length; i++)
                for (byte c = 1; c < WeatherClimate.Count; c++)
                {
                    WeatherKey key = Key(i, c);
                    TestAssert.Eq(key.Climate, c, "the constructor keeps the climate");
                    TestAssert.Eq(key.TapeId, BoscaliSummer.Core.Math.Deterministic.Hash((int)Before[i].TapeId, c, 0xC11A), "TapeId of climate " + c);
                    TestAssert.That(key.TapeId != Before[i].TapeId && key.TapeId != Key(i, (byte)(c % 3 + 1)).TapeId, "climates have their own tapes");
                    TestAssert.That(!key.Equals(Key(i)) && !key.SameTape(Key(i)), "a climate key is not the vanilla key");
                    TestAssert.That(key.Equals(Key(i, c)) && key.GetHashCode() == Key(i, c).GetHashCode(), "equal keys, equal hashes");
                    TestAssert.Eq(TapeInputs.From(key).Climate, c, "TapeInputs carries the climate");
                }
            TestAssert.Eq(Key(0, 9).Climate, (byte)0, "an unknown climate is no climate");
            TestAssert.Eq(Key(0, 9).TapeId, Before[0].TapeId, "an unknown climate keeps the vanilla tape");

            // The byte survives every edit of a key and the wire.
            WeatherKey arctic = Key(2, WeatherClimate.Arctic);
            WeatherKey[] edits =
            {
                arctic.WithSets(Superstructures.FogBankSet), arctic.WithOffset(1234f), arctic.WithLayoutSalt(5), arctic.WithAnchor(10f, 20f),
                arctic.WithAnchor(10f, 20f).WithoutAnchor(), arctic.WithSkyPin(4), arctic.Rebased(60f, true, 31f), arctic.AfterClockReset(90.0, 5f),
                arctic.With(1, 12, Superstructures.LenticularSet), arctic.WithStory(3),
                ForceWeatherMap.Key(arctic, 20f, WeatherRegimeType.Fair, null, 900f, 77u, 600f),
            };
            for (int i = 0; i < edits.Length; i++) TestAssert.Eq(edits[i].Climate, WeatherClimate.Arctic, "edit " + i + " keeps the climate");
            TestAssert.That(edits[9].TapeId != arctic.TapeId, "a story is still a cut");

            WeatherKeyWire.Fields wire = WeatherKeyWire.FromKey(arctic);
            TestAssert.Eq(wire.Climate, WeatherClimate.Arctic, "the wire carries the climate");
            WeatherKey back = WeatherKeyWire.ToKey(wire);
            TestAssert.That(back.Equals(arctic) && back.TapeId == arctic.TapeId, "wire round trip");
            wire.Climate = 200;
            TestAssert.Eq(WeatherKeyWire.ToKey(wire).Climate, (byte)0, "a climate this build does not know arrives as none");
            TestAssert.That(WeatherKeyWire.ToKey(WeatherKeyWire.FromKey(Key(1))).Equals(Key(1)), "vanilla wire round trip");

            // The tape: an arctic one holds no storm sky and tells no convective day, whatever class it opens in; the same seeds do hold storms without a climate.
            int vanillaStorms = 0, vanillaConvective = 0, tropicalConvective = 0, vanillaOccluded = 0, arcticOccluded = 0, tapes = 0;
            for (uint seed = 1; seed <= 150; seed++)
                for (byte cls = 0; cls < WeatherKey.ClassCount; cls++)
                {
                    WeatherTape Tape(byte climate) => WeatherTape.Generate(TapeInputs.From(new WeatherKey(seed, 0f, true, cls, climate: climate)));
                    WeatherTape plain = Tape(0), cold = Tape(WeatherClimate.Arctic), warm = Tape(WeatherClimate.Tropical);
                    tapes++;
                    if (Holds(plain, WeatherRegimeType.Storm)) vanillaStorms++;
                    if (Tells(plain, StoryKind.ConvectiveDay)) vanillaConvective++;
                    if (Tells(warm, StoryKind.ConvectiveDay)) tropicalConvective++;
                    if (Tells(plain, StoryKind.Occluded)) vanillaOccluded++;
                    if (Tells(cold, StoryKind.Occluded)) arcticOccluded++;
                    TestAssert.That(!Holds(cold, WeatherRegimeType.Storm), "arctic tape of seed " + seed + " class " + cls + " holds a Storm sky");
                    TestAssert.That(!Tells(cold, StoryKind.ConvectiveDay), "arctic tape of seed " + seed + " class " + cls + " tells a convective day");
                    TestAssert.That(cold.OpeningClass != WeatherRegimeType.Storm, "arctic never opens on a storm");
                    TestAssert.That(cold.Keyframes.Length > 8 && warm.Keyframes.Length > 8, "climate tapes are whole tapes");
                }
            TestAssert.That(vanillaStorms > tapes / 2, "the same seeds hold storms without a climate (" + vanillaStorms + " of " + tapes + ")");
            TestAssert.That(tropicalConvective > vanillaConvective, "the tropics tell more convective days (" + tropicalConvective + " against " + vanillaConvective + ")");
            TestAssert.That(arcticOccluded > vanillaOccluded, "the arctic tells more occlusions (" + arcticOccluded + " against " + vanillaOccluded + ")");

            // A pinned storm sky is the host's own choice and stays, as a pin always has.
            TestAssert.That(Holds(WeatherTape.Generate(TapeInputs.From(new WeatherKey(3u, 0f, false, 6, skyPin: 44, climate: WeatherClimate.Arctic))), WeatherRegimeType.Storm), "a pin is kept");

            // The story pick: a zero bias removes a story, all zeros give High pressure, and no climate is the old pick for every roll.
            TestAssert.Eq(WeatherStories.Pick(new[] { (int)StoryKind.ConvectiveDay }, WeatherRegimeType.Scattered, 0.5f, WeatherClimate.Arctic), (int)StoryKind.HighPressure, "every candidate removed");
            for (int r = 0; r < 1000; r++)
            {
                float roll = r / 1000f;
                foreach (WeatherRegimeType cls in new[] { WeatherRegimeType.Clear, WeatherRegimeType.Broken, WeatherRegimeType.Storm })
                {
                    TestAssert.That(WeatherStories.Pick(WeatherStories.AllStories, cls, roll, WeatherClimate.Arctic) != (int)StoryKind.ConvectiveDay, "arctic never picks the convective day");
                    TestAssert.Eq(WeatherStories.Pick(WeatherStories.AllStories, cls, roll), OldPick(WeatherStories.AllStories, cls, roll), "the pick without a climate, roll " + roll);
                    TestAssert.Eq(WeatherStories.Pick(WeatherStories.FrontStories, cls, roll), OldPick(WeatherStories.FrontStories, cls, roll), "the front pick without a climate, roll " + roll);
                }
            }
        }

        /// <summary>WeatherStories.Pick as it was before the climate argument.</summary>
        private static int OldPick(int[] candidates, WeatherRegimeType cls, float roll)
        {
            float total = 0f;
            foreach (int s in candidates) total += WeatherStories.Weight(s, cls);
            float at = roll * total;
            foreach (int s in candidates)
            {
                at -= WeatherStories.Weight(s, cls);
                if (at < 0f) return s;
            }
            return candidates[candidates.Length - 1];
        }
    }
}
