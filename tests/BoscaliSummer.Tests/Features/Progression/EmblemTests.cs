using System;
using System.Collections.Generic;
using BoscaliSummer.Modules.Progression.Runtime;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Progression
{
    /// <summary>Squadron insignia are local cosmetics, but their encoding round-trips through the
    /// BepInEx config and their drawing is pure arithmetic, so both get pure coverage.</summary>
    internal static class EmblemTests
    {
        private const int Size = 64;

        public static void Run()
        {
            RoundTrip();
            LegacyConfigStillParses();
            Bounds();
            LayersStepAndWrap();
            RollsAreDeterministicAndLegible();
            HostileIsDeterministic();
            BlazonReads();
            EveryShapeIsASolidBadge();
            EveryChargeDrawsItsOwnMark();
            NumberAndAccentsChangeTheDrawing();
            LowVisIsGrey();
            PackedLiveryKeepsItsTexels();
        }

        // ---- Packing a painted livery ---------------------------------------------------------

        private static void PackedLiveryKeepsItsTexels()
        {
            // The sizes the game's own 2048 atlases report: DXT1 and DXT5 with twelve mips.
            TestAssert.That(LiveryCodec.Size(2048, 2048, false) == 2796216 && LiveryCodec.Size(2048, 2048, true) == 5592432,
                "a packed livery is not the size of a stock one");
            TestAssert.That(LiveryCodec.Size(1, 1, false) == 8 && LiveryCodec.Size(4, 4, true) == 48, "small mip chains are mis-sized");

            // Two paints in one block come back as themselves (to 5:6:5), whichever way the colour diagonal runs.
            var pair = new byte[4 * 4 * 4];
            for (int i = 0; i < 16; i++)
            {
                bool left = (i & 3) < 2;
                pair[i * 4] = (byte)(left ? 184 : 16); pair[i * 4 + 1] = (byte)(left ? 24 : 68); pair[i * 4 + 2] = (byte)(left ? 24 : 152); pair[i * 4 + 3] = 255;
            }
            byte[] paired = Unpack(LiveryCodec.Encode(pair, 4, 4, false), 4, 4);
            for (int i = 0; i < pair.Length; i++)
                TestAssert.That(Math.Abs(pair[i] - paired[i]) <= 4, "a two-paint block lost a paint: byte " + i + " " + pair[i] + " -> " + paired[i]);

            // A painted emblem over flat grey, as the bake leaves it, at about the size it has on an atlas.
            const int size = 256;
            var pixels = new byte[size * size * 4];
            EmblemRaster.Render(new EmblemDesign(1, 3, 1, 3, 2, 5, 0, 42), EmblemFinish.Colour, size, false, pixels);
            for (int i = 0; i < pixels.Length; i += 4)
            {
                float a = pixels[i + 3] / 255f;
                for (int c = 0; c < 3; c++) pixels[i + c] = (byte)(pixels[i + c] * a + 118 * (1f - a));
                pixels[i + 3] = 255;
            }
            byte[] packed = LiveryCodec.Encode(pixels, size, size, false);
            TestAssert.That(packed.Length == LiveryCodec.Size(size, size, false), "the packed chain is not the promised size");
            byte[] back = Unpack(packed, size, size);
            long error = 0; int worst = 0;
            for (int i = 0; i < pixels.Length; i++)
            {
                int d = Math.Abs(pixels[i] - back[i]);
                error += d; worst = Math.Max(worst, d);
            }
            // Flat areas survive to within 5:6:5 rounding; only blocks mixing three paints lose more.
            TestAssert.That(error / (double)pixels.Length < 2.5, "packing blurs the livery: mean error " + error / (double)pixels.Length);
            TestAssert.That(back[0] > 110 && back[0] < 126 && back[1] > 110 && back[1] < 126, "flat livery grey shifted: " + back[0]);

            // What came out of a packed texture survives a second packing: the stock livery around the
            // marking is already DXT, and must not visibly degrade by passing through here.
            byte[] again = Unpack(LiveryCodec.Encode(back, size, size, false), size, size);
            int drift = 0, moved = 0;
            for (int i = 0; i < back.Length; i++)
            {
                int d = Math.Abs(back[i] - again[i]);
                drift = Math.Max(drift, d);
                if (d > 8) moved++;
            }
            // A block where three of the marking's paints meet may settle a step further on the second pass;
            // nothing else moves, and a painted block is never packed twice in the game (a repaint starts from the stock atlas).
            TestAssert.That(drift <= 24 && moved <= 16,
                "re-packing a packed livery moved " + moved + " bytes by up to " + drift + " (first pass worst " + worst + ")");

            // Rows keep their order: a top band stays at the top.
            var bands = new byte[8 * 8 * 4];
            for (int i = 0; i < bands.Length; i += 4) { bands[i] = (byte)(i / 4 / 8 < 4 ? 255 : 0); bands[i + 3] = 255; }
            byte[] banded = Unpack(LiveryCodec.Encode(bands, 8, 8, false), 8, 8);
            TestAssert.That(banded[0] > 240 && banded[(7 * 8) * 4] < 16, "packing reordered the rows");

            // Alpha rides along when asked for.
            var veil = new byte[4 * 4 * 4];
            for (int i = 0; i < 16; i++) { veil[i * 4] = 200; veil[i * 4 + 3] = (byte)(i < 8 ? 255 : 193); }
            byte[] veiled = LiveryCodec.Encode(veil, 4, 4, true);
            TestAssert.That(veiled.Length == 48 && veiled[0] == 255 && veiled[1] == 193, "alpha endpoints are wrong");
            TestAssert.That((veiled[2] & 7) == 0 && (veiled[7] >> 5 & 7) == 1, "alpha codes do not pick their endpoints");
        }

        /// <summary>Reference DXT1 decode of the first mip level.</summary>
        private static byte[] Unpack(byte[] packed, int width, int height)
        {
            var pixels = new byte[width * height * 4];
            int o = 0;
            for (int by = 0; by < height; by += 4)
                for (int bx = 0; bx < width; bx += 4, o += 8)
                {
                    int c0 = packed[o] | packed[o + 1] << 8, c1 = packed[o + 2] | packed[o + 3] << 8;
                    uint indices = (uint)(packed[o + 4] | packed[o + 5] << 8 | packed[o + 6] << 16 | packed[o + 7] << 24);
                    var palette = new int[4, 3];
                    for (int c = 0; c < 3; c++)
                    {
                        int e0 = c == 0 ? c0 >> 11 : c == 1 ? c0 >> 5 & 63 : c0 & 31, e1 = c == 0 ? c1 >> 11 : c == 1 ? c1 >> 5 & 63 : c1 & 31;
                        int v0 = c == 1 ? e0 << 2 | e0 >> 4 : e0 << 3 | e0 >> 2, v1 = c == 1 ? e1 << 2 | e1 >> 4 : e1 << 3 | e1 >> 2;
                        palette[0, c] = v0; palette[1, c] = v1;
                        palette[2, c] = c0 > c1 ? (2 * v0 + v1) / 3 : (v0 + v1) / 2;
                        palette[3, c] = c0 > c1 ? (v0 + 2 * v1) / 3 : 0;
                    }
                    for (int i = 0; i < 16; i++)
                    {
                        int x = bx + (i & 3), y = by + (i >> 2);
                        if (x >= width || y >= height) continue;
                        int pick = (int)(indices >> (2 * i) & 3), p = (y * width + x) * 4;
                        pixels[p] = (byte)palette[pick, 0]; pixels[p + 1] = (byte)palette[pick, 1];
                        pixels[p + 2] = (byte)palette[pick, 2]; pixels[p + 3] = 255;
                    }
                }
            return pixels;
        }

        private static void RoundTrip()
        {
            var design = new EmblemDesign(7, 9, 15, 4, 11, 10, 9, 99);
            TestAssert.That(design.Encode() == "7.9.15.4.11.10.9.99", "emblem encoding changed: " + design.Encode());
            TestAssert.That(EmblemDesign.TryParse(design.Encode(), out EmblemDesign parsed) && parsed == design,
                "an encoded emblem did not round-trip");
            TestAssert.That(EmblemDesign.Parse(EmblemDesign.DefaultText, default) == EmblemDesign.Default,
                "the default text is not the default emblem");
            TestAssert.That(EmblemDesign.Default.Legible, "the default emblem must be legible");
        }

        private static void LegacyConfigStillParses()
        {
            // The pre-layer default was shield / wings / red-on-white, stored as "0.2.3".
            TestAssert.That(EmblemDesign.TryParse("0.2.3", out EmblemDesign legacy), "a pre-layer emblem no longer parses");
            TestAssert.That(legacy.Shape == 0 && legacy.Charge == 2 && legacy.Division == 0 && legacy.Accent == 0 &&
                legacy.Number == 0 && legacy.Legible, "a pre-layer emblem lost its shape, mark or contrast");
            for (int shape = 0; shape < 4; shape++)
                for (int charge = 0; charge < 5; charge++)
                    for (int palette = 0; palette < 6; palette++)
                        TestAssert.That(EmblemDesign.TryParse(shape + "." + charge + "." + palette, out EmblemDesign old) &&
                            old.Shape == shape && old.Charge == charge && old.Legible,
                            "pre-layer emblem " + shape + "." + charge + "." + palette + " did not carry over");
            TestAssert.That(!EmblemDesign.TryParse("4.0.0", out _) && !EmblemDesign.TryParse("0.5.0", out _) &&
                !EmblemDesign.TryParse("0.0.6", out _), "an out-of-range pre-layer emblem parsed");
        }

        private static void Bounds()
        {
            var wrapped = new EmblemDesign(EmblemDesign.ShapeCount + 1, 11, EmblemDesign.ChargeCount + 1, 6, 13, -1, 12, 100);
            TestAssert.That(wrapped.Shape == 1 && wrapped.Division == 1 && wrapped.Charge == 1 && wrapped.Accent == 1 &&
                wrapped.Field == 1 && wrapped.Second == 11 && wrapped.Ink == 0 && wrapped.Number == 0,
                "emblem indices must wrap into range");
            for (int layer = 0; layer < EmblemDesign.LayerCount; layer++)
            {
                var parts = new string[EmblemDesign.LayerCount];
                for (int i = 0; i < parts.Length; i++) parts[i] = "0";
                parts[layer] = EmblemDesign.Count((EmblemLayer)layer).ToString();
                TestAssert.That(!EmblemDesign.TryParse(string.Join(".", parts), out _),
                    "an out-of-range " + (EmblemLayer)layer + " parsed");
            }
            foreach (string bad in new[] { "", "0.0", "0.0.0.0", "a.b.c", "0.0.0.0.0.0.0.x", "0.0.0.0.0.0.0.0.0", "-1.0.0.0.0.0.0.0" })
                TestAssert.That(!EmblemDesign.TryParse(bad, out _), "a malformed emblem parsed: '" + bad + "'");
            TestAssert.That(EmblemDesign.ShapeNames.Length == EmblemDesign.ShapeCount &&
                EmblemDesign.DivisionNames.Length == EmblemDesign.DivisionCount &&
                EmblemDesign.ChargeNames.Length == EmblemDesign.ChargeCount &&
                EmblemDesign.AccentNames.Length == EmblemDesign.AccentCount &&
                EmblemDesign.TinctureNames.Length == EmblemDesign.TinctureCount, "an emblem catalogue and its count disagree");
        }

        private static void LayersStepAndWrap()
        {
            EmblemDesign start = EmblemDesign.Default;
            for (int i = 0; i < EmblemDesign.LayerCount; i++)
            {
                var layer = (EmblemLayer)i;
                EmblemDesign stepped = start.With(layer, 1);
                TestAssert.That(stepped != start && stepped.With(layer, -1) == start, layer + " does not step there and back");
                TestAssert.That(start.With(layer, EmblemDesign.Count(layer)) == start, layer + " does not wrap");
                for (int other = 0; other < EmblemDesign.LayerCount; other++)
                    if (other != i)
                        TestAssert.That(stepped.Value((EmblemLayer)other) == start.Value((EmblemLayer)other),
                            "stepping " + layer + " moved " + (EmblemLayer)other);
                TestAssert.That(!string.IsNullOrEmpty(stepped.Name(layer)), layer + " has no stepper name");
            }
            TestAssert.That(new EmblemDesign(0, 0, 0, 0, 0, 0, 5, 0).Name(EmblemLayer.Number) == "NONE" &&
                new EmblemDesign(0, 0, 0, 0, 0, 0, 5, 7).Name(EmblemLayer.Number) == "NO. 7", "the number stepper reads wrong");
        }

        private static void RollsAreDeterministicAndLegible()
        {
            TestAssert.That(EmblemDesign.Random(12345) == EmblemDesign.Random(12345), "emblem rolls are not seed-stable");
            var seen = new HashSet<EmblemDesign>();
            for (int seed = 0; seed < 2000; seed++)
            {
                EmblemDesign design = EmblemDesign.Random(seed);
                seen.Add(design);
                TestAssert.That(design.Legible, "roll " + seed + " is not legible: " + design.Blazon());
                TestAssert.That(EmblemDesign.TryParse(design.Encode(), out EmblemDesign parsed) && parsed == design,
                    "roll " + seed + " escaped its catalogue");
            }
            TestAssert.That(seen.Count > 1900, "the generator repeats itself: " + seen.Count + " designs in 2000 rolls");
        }

        private static void HostileIsDeterministic()
        {
            for (int i = 0; i < 128; i++)
            {
                string identity = "[+]|CROWN " + i;
                EmblemDesign first = EmblemDesign.Hostile(identity);
                TestAssert.That(first == EmblemDesign.Hostile(identity), "a hostile crest is not identity-stable");
                TestAssert.That(EmblemDesign.IsHostileTincture(first.Field) &&
                    (first.Division == 0 || EmblemDesign.IsHostileTincture(first.Second)),
                    "a hostile crest escaped the caution/danger band: " + first.Blazon());
                TestAssert.That(first.Legible, "a hostile crest is not legible: " + first.Blazon());
                TestAssert.That(first.Charge < EmblemDesign.NoveltyCharge, "an enemy wing flies a joke or nose art: " + first.Blazon());
            }
            // The stored form is the index, so the catalogues only ever grow at the end.
            TestAssert.That(EmblemDesign.ShapeNames[7] == "ARCH" && EmblemDesign.ChargeNames[15] == "ANCHOR" &&
                EmblemDesign.ChargeNames[EmblemDesign.NoveltyCharge] == "COOL S", "an emblem catalogue was reordered");
            bool novelty = false;
            for (int seed = 0; seed < 200 && !novelty; seed++) novelty = EmblemDesign.Random(seed).Charge >= EmblemDesign.NoveltyCharge;
            TestAssert.That(novelty, "a player's roll never comes up with a joke or nose art");
            TestAssert.That(EmblemDesign.Hostile("") == EmblemDesign.Hostile(null),
                "an empty wing identity is not safe to hash");
        }

        private static void BlazonReads()
        {
            TestAssert.That(EmblemDesign.Default.Blazon() == "AZURE, WINGS OR WITHIN AN ANNULET",
                "the default blazon changed: " + EmblemDesign.Default.Blazon());
            TestAssert.That(new EmblemDesign(1, 3, 13, 2, 0, 3, 5, 7).Blazon() ==
                "PER BEND ARGENT AND AZURE, AN ARROW SABLE, IN CHIEF THREE MULLETS · NO. 7", "a full blazon reads wrong");
            TestAssert.That(new EmblemDesign(0, 0, 3, 0, 2, 2, 0, 0).Blazon() == "GULES, A CROSS ARGENT", "a singular charge lost its article");
            TestAssert.That(new EmblemDesign(0, 0, 24, 0, 5, 5, 0, 0).Blazon() == "SABLE, A LOSS ARGENT" &&
                new EmblemDesign(0, 0, 19, 0, 5, 5, 0, 0).Blazon() == "SABLE, AXES ARGENT", "the new charges read wrong");
        }

        // ---- Drawing -----------------------------------------------------------------------

        private static byte[] Draw(EmblemDesign design, EmblemFinish finish = EmblemFinish.Colour)
        {
            var pixels = new byte[Size * Size * 4];
            EmblemRaster.Render(design, finish, Size, false, pixels);
            return pixels;
        }

        /// <summary>Opaque pixels of one paint inside the centred square of half-width <paramref name="reach"/> (0..1).</summary>
        private static int Near(byte[] pixels, Rgba paint, float reach = 1f)
        {
            int count = 0, margin = (int)(Size * 0.5f * (1f - reach));
            for (int y = margin; y < Size - margin; y++)
                for (int x = margin; x < Size - margin; x++)
                {
                    int i = (y * Size + x) * 4;
                    if (pixels[i + 3] == 255 && Math.Abs(pixels[i] - paint.R * 255f) < 6f &&
                        Math.Abs(pixels[i + 1] - paint.G * 255f) < 6f && Math.Abs(pixels[i + 2] - paint.B * 255f) < 6f) count++;
                }
            return count;
        }

        private static void EveryShapeIsASolidBadge()
        {
            for (int shape = 0; shape < EmblemDesign.ShapeCount; shape++)
            {
                byte[] pixels = Draw(new EmblemDesign(shape, 0, 0, 0, 3, 3, 0, 0));
                string name = EmblemDesign.ShapeNames[shape];
                int opaque = 0;
                for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] == 255) opaque++;
                TestAssert.That(pixels[3] == 0 && pixels[(Size - 1) * 4 + 3] == 0, name + " paints its bottom corners");
                TestAssert.That(pixels[(Size / 2 * Size + Size / 2) * 4 + 3] == 255, name + " has a hole at its centre");
                // The patch nearly fills its square; the delta and the diamond are the leanest.
                TestAssert.That(opaque > Size * Size * 0.30f && opaque < Size * Size * 0.80f,
                    name + " covers an implausible share of its square: " + opaque);
                TestAssert.That(Near(pixels, EmblemDesign.Paint(3, EmblemFinish.Colour)) > Size * Size * 0.08f,
                    name + " shows almost no field");
            }
        }

        private static void EveryChargeDrawsItsOwnMark()
        {
            Rgba ink = EmblemDesign.Paint(0, EmblemFinish.Colour);
            var drawings = new List<byte[]>();
            for (int charge = 0; charge < EmblemDesign.ChargeCount; charge++)
            {
                // White on a black roundel; the rim is ink too, so only the middle of the field is counted.
                byte[] pixels = Draw(new EmblemDesign(1, 0, charge, 0, 5, 5, 0, 0));
                int marked = Near(pixels, ink, 0.62f);
                TestAssert.That(marked > 40, EmblemDesign.ChargeNames[charge] + " draws no visible mark (" + marked + " px)");
                foreach (byte[] earlier in drawings)
                    TestAssert.That(!Same(earlier, pixels), EmblemDesign.ChargeNames[charge] + " draws the same as another charge");
                drawings.Add(pixels);
            }
        }

        private static void NumberAndAccentsChangeTheDrawing()
        {
            var plain = new EmblemDesign(0, 0, 0, 0, 3, 3, 0, 0);
            byte[] baseline = Draw(plain);
            for (int accent = 1; accent < EmblemDesign.AccentCount; accent++)
                TestAssert.That(!Same(baseline, Draw(plain.With(EmblemLayer.Accent, accent))),
                    EmblemDesign.AccentNames[accent] + " draws nothing");
            for (int division = 1; division < EmblemDesign.DivisionCount; division++)
            {
                byte[] split = Draw(new EmblemDesign(0, division, 0, 0, 3, 1, 5, 0));
                TestAssert.That(Near(split, EmblemDesign.Paint(3, EmblemFinish.Colour)) > 60 &&
                    Near(split, EmblemDesign.Paint(1, EmblemFinish.Colour)) > 60,
                    EmblemDesign.DivisionNames[division] + " does not show both tinctures");
            }
            var numbers = new List<byte[]>();
            foreach (int number in new[] { 1, 7, 8, 10, 42, 99 })
            {
                byte[] pixels = Draw(plain.With(EmblemLayer.Number, number));
                TestAssert.That(!Same(baseline, pixels), "squadron number " + number + " draws nothing");
                foreach (byte[] earlier in numbers) TestAssert.That(!Same(earlier, pixels), "two squadron numbers draw the same");
                numbers.Add(pixels);
            }
        }

        private static void LowVisIsGrey()
        {
            for (int seed = 0; seed < 24; seed++)
            {
                byte[] pixels = Draw(EmblemDesign.Random(seed), EmblemFinish.LowVis);
                int min = 255, max = 0;
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i + 3] != 255) continue;
                    TestAssert.That(pixels[i] == pixels[i + 1] && pixels[i + 1] == pixels[i + 2], "a low-vis emblem carries colour");
                    min = Math.Min(min, pixels[i]); max = Math.Max(max, pixels[i]);
                }
                // Tone has to carry the whole design, but never as stark as black on white.
                TestAssert.That(max - min >= 30 && min >= 40 && max <= 170, "low-vis tones fall outside the grey ramp: " + min + ".." + max);
            }
        }

        private static bool Same(byte[] a, byte[] b)
        {
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
    }
}
