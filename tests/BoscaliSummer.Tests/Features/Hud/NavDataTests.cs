using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The NAV DATA block (bottom right, cockpit view): which rows exist, the words in them, and the box they stand in.</summary>
    internal static class NavDataTests
    {
        public static void Run()
        {
            Bearings();
            Distances();
            Endurances();
            Clocks();
            Names();
            Rows();
            Fit();
            Layout();
        }

        private static string Text(char[] b, int len) => new string(b, 0, len);

        private static string Row(NavRow r) => r.Label + " " + Text(r.Value, r.Length) + (r.Flag != null ? " " + r.Flag : "");

        private static NavInput Full() => new NavInput
        {
            Units = AvUnits.Metric,
            HasHome = true, HomeName = "ANNEX CLIFF", HomeBearingDeg = 97f, HomeM = 23400f,
            HasTarget = true, TargetName = "FS-20 VORTEX", TargetBearingDeg = 271.4f, TargetM = 8700f, ClosureMps = 250f,
            HasFuel = true, EnduranceS = 3900f, Bingo = false, Joker = false,
            HasClock = true, ClockS = 745f,
        };

        private static void Bearings()
        {
            TestAssert.Eq(NavBlock.Bearing(0f), 0, "Bearing 0");
            TestAssert.Eq(NavBlock.Bearing(359.4f), 359, "Bearing 359.4 reads 359");
            TestAssert.Eq(NavBlock.Bearing(359.6f), 0, "Bearing 359.6 wraps to 0, never 360");
            TestAssert.Eq(NavBlock.Bearing(360f), 0, "Bearing 360 wraps to 0");
            TestAssert.Eq(NavBlock.Bearing(-1f), 359, "Bearing -1 wraps to 359");
            TestAssert.Eq(NavBlock.Bearing(-0.4f), 0, "Bearing -0.4 rounds to 0");
            TestAssert.Eq(NavBlock.Bearing(-359f), 1, "Bearing -359 is 1");
            TestAssert.Eq(NavBlock.Bearing(725f), 5, "Bearing 725 wraps twice to 5");
            TestAssert.Eq(NavBlock.Bearing(90.5f), 91, "Half rounds away from zero");
            var b = new char[8];
            TestAssert.Eq(Text(b, NavBlock.WriteBearing(b, 0, 7f)), "007", "A bearing is three digits: 007");
            TestAssert.Eq(Text(b, NavBlock.WriteBearing(b, 0, 97.2f)), "097", "...097");
            TestAssert.Eq(Text(b, NavBlock.WriteBearing(b, 0, 359.6f)), "000", "...359.6 is 000");
            TestAssert.Eq(Text(b, NavBlock.WriteBearing(b, 0, 180f)), "180", "...180");
        }

        private static void Distances()
        {
            var b = new char[16];
            string D(float m, AvUnits u) => Text(b, NavBlock.WriteDistance(b, 0, m, u));
            TestAssert.Eq(D(900f, AvUnits.Metric), "900M", "Metric under a kilometre is metres, in capitals");
            TestAssert.Eq(D(1500f, AvUnits.Metric), "1.5KM", "...to ten kilometres one decimal");
            TestAssert.Eq(D(23400f, AvUnits.Metric), "23KM", "...past ten whole kilometres");
            TestAssert.Eq(D(500f, AvUnits.Imperial), "547YD", "Imperial under a thousand yards is yards");
            TestAssert.Eq(D(20000f, AvUnits.Imperial), "10.8NM", "...then nautical miles, one decimal");
            // The words are the game's own readings (the same breakpoints as every other HUD distance), upper-cased.
            foreach (float m in new[] { 0f, 55f, 999f, 1001f, 9999f, 10001f, 123456f })
                foreach (AvUnits u in new[] { AvUnits.Metric, AvUnits.Imperial })
                {
                    int n = AvUnitTable.DistanceReading(b, 0, m, u);
                    TestAssert.Eq(D(m, u), Text(b, n).ToUpperInvariant(), "Distance " + m + " in " + u + " reads as the game's reading in capitals");
                }
        }

        private static void Endurances()
        {
            var b = new char[16];
            string E(float s) => Text(b, NavBlock.WriteEndurance(b, 0, s));
            // The same words as the board's fuel row (FuelText.Endurance), for every kind of value.
            foreach (float s in new[] { float.NaN, float.PositiveInfinity, -5f, 0f, 30f, 59.9f, 60f, 119f, 2520f, 5999f, 6000f, 6060f, 3900f, 7200f, 35940f, 359999f, 360000f })
                TestAssert.Eq(E(s), FuelText.Endurance(s), "Endurance " + s + " reads as the fuel row's");
            TestAssert.Eq(E(2520f), "42MIN", "42 minutes");
            TestAssert.Eq(E(3900f), "65MIN", "65 minutes (under 100)");
            TestAssert.Eq(E(6300f), "1H45", "105 minutes is 1H45");
            TestAssert.Eq(E(30f), "<1MIN", "Under a minute");
            TestAssert.Eq(E(float.NaN), "--", "Unknown");
            TestAssert.That(NavBlock.HasEndurance(2520f) && !NavBlock.HasEndurance(float.PositiveInfinity) && !NavBlock.HasEndurance(float.NaN) && !NavBlock.HasEndurance(360000f), "A parked aircraft's endless endurance is no data");
        }

        private static void Clocks()
        {
            var b = new char[16];
            string C(float s) => Text(b, NavBlock.WriteClock(b, 0, s));
            TestAssert.Eq(C(0f), "0:00:00", "The clock starts at zero");
            TestAssert.Eq(C(745f), "0:12:25", "12 minutes 25 seconds");
            TestAssert.Eq(C(3725.9f), "1:02:05", "Seconds are floored");
            TestAssert.Eq(C(36000f), "10:00:00", "Hours are not capped");
        }

        private static void Names()
        {
            var b = new char[48];
            string N(string name, int room) => Text(b, NavBlock.FitWords(name, room, b, 0));
            TestAssert.Eq(N("ANNEX CLIFF", 16), "ANNEX CLIFF", "A name that fits stays whole");
            TestAssert.Eq(N("WESTERN HIGHWAY STRIP", 16), "WESTERN HIGHWAY", "Whole words go from the end, never half a word");
            TestAssert.Eq(N("WESTERN HIGHWAY STRIP", 14), "WESTERN", "...and again");
            TestAssert.Eq(N("HYPERION", 5), "HYPER", "One word too long for the room is cut to it");
            TestAssert.Eq(N("HYPERION CLASS CARRIER", 12), "HYPERION", "Whole words of a long name (the radar names' rule)");
            TestAssert.Eq(N("  A   B  ", 8), "A B", "Spaces are single");
            TestAssert.Eq(N("ANNEX CLIFF", 0), "", "No room: nothing");
            TestAssert.Eq(N("", 8), "", "No name: nothing");
            TestAssert.Eq(N(null, 8), "", "A null name: nothing");
            TestAssert.Eq(N("HIGHWAY STRIP 02", 16), "HIGHWAY STRIP 02", "A name exactly the room fits");
            for (int room = 1; room < 20; room++)
                TestAssert.That(N("WESTERN HIGHWAY STRIP", room).IndexOfAny(new[] { (char)46, (char)0x2026 }) < 0 && N("WESTERN HIGHWAY STRIP", room).Length <= room, "Room " + room + ": no dots and no overrun");
        }

        private static void Rows()
        {
            var block = new NavBlock();
            TestAssert.Eq(block.Compose(default), 0, "Nothing known: no rows, no block");
            NavInput all = Full();
            TestAssert.Eq(block.Compose(all), 5, "Everything known: five rows");
            TestAssert.Eq(Row(block.Rows[0]), "HOME ANNEX CLIFF 097 / 23KM", "HOME: name, three digit bearing, distance");
            TestAssert.Eq(Row(block.Rows[1]), "TGT FS-20 VORTEX 271 / 8.7KM", "TGT: name, bearing, range");
            TestAssert.Eq(Row(block.Rows[2]), "CLS CLOSING 900KM/H", "CLS: the closing speed in the card's words (250 m/s)");
            TestAssert.Eq(Row(block.Rows[3]), "FUEL 65MIN", "FUEL: the endurance");
            TestAssert.Eq(Row(block.Rows[4]), "CLOCK 0:12:25", "CLOCK: the mission time");
            TestAssert.That(block.Rows[0].Kind == NavRowKind.Home && block.Rows[1].Kind == NavRowKind.Target && block.Rows[2].Kind == NavRowKind.Closure && block.Rows[3].Kind == NavRowKind.Fuel && block.Rows[4].Kind == NavRowKind.Clock, "The rows come in that order");
            TestAssert.That(block.Rows[1].Tone == ScopeTone.Hi && block.Rows[0].Tone == ScopeTone.Ink, "The selected target is BRIGHT, the rest INK");

            // Home alone.
            NavInput home = default;
            home.Units = AvUnits.Imperial; home.HasHome = true; home.HomeName = "MARIS AIRPORT"; home.HomeBearingDeg = 5f; home.HomeM = 18000f;
            TestAssert.Eq(block.Compose(home), 1, "Home only: one row");
            TestAssert.Eq(Row(block.Rows[0]), "HOME MARIS AIRPORT 005 / 9.7NM", "...in the player's imperial units");

            // A row exists only while it has data.
            NavInput noHome = all; noHome.HasHome = false;
            TestAssert.Eq(block.Compose(noHome), 4, "No field: no HOME row");
            TestAssert.That(block.Rows[0].Kind == NavRowKind.Target, "...and the rest move up");
            NavInput nan = all; nan.HomeM = float.NaN;
            TestAssert.That(block.Compose(nan) == 4 && block.Rows[0].Kind == NavRowKind.Target, "A field with no distance is no row");
            NavInput noTarget = all; noTarget.HasTarget = false;
            TestAssert.That(block.Compose(noTarget) == 3 && block.Rows[1].Kind == NavRowKind.Fuel, "No target: neither TGT nor CLS");
            NavInput noClosure = all; noClosure.ClosureMps = float.NaN;
            TestAssert.That(block.Compose(noClosure) == 4 && block.Rows[1].Kind == NavRowKind.Target && block.Rows[2].Kind == NavRowKind.Fuel, "A target with no closure yet: TGT alone");
            NavInput noClock = all; noClock.ClockS = float.NaN;
            TestAssert.That(block.Compose(noClock) == 4 && block.Rows[3].Kind == NavRowKind.Fuel, "No clock: no CLOCK row");
            noClock.ClockS = -1f;
            TestAssert.Eq(block.Compose(noClock), 4, "A negative clock is none");
            NavInput unread = all; unread.HasClock = false;
            TestAssert.Eq(block.Compose(unread), 4, "A clock nobody read is none (a zeroed input has no clock row)");

            // Closure words.
            NavInput open = all; open.ClosureMps = -100f;
            block.Compose(open);
            TestAssert.Eq(Row(block.Rows[2]), "CLS OPENING 360KM/H", "Opening");
            open.ClosureMps = 3f;
            block.Compose(open);
            TestAssert.Eq(Row(block.Rows[2]), "CLS STEADY", "Under 5 m/s either way is steady");
            open.ClosureMps = -4.9f;
            block.Compose(open);
            TestAssert.Eq(Row(block.Rows[2]), "CLS STEADY", "...also opening slowly");
            open.ClosureMps = 250f; open.Units = AvUnits.Imperial;
            block.Compose(open);
            TestAssert.Eq(Row(block.Rows[2]), "CLS CLOSING 486KT", "Knots in imperial, capitals");

            // Fuel: BINGO and JOKER, and the unknown endurance.
            NavInput bingo = all; bingo.Bingo = true;
            block.Compose(bingo);
            TestAssert.Eq(Row(block.Rows[3]), "FUEL 65MIN BINGO", "BINGO follows the endurance");
            bingo.Bingo = false; bingo.Joker = true;
            block.Compose(bingo);
            TestAssert.Eq(Row(block.Rows[3]), "FUEL 65MIN JOKER", "JOKER the same way");
            bingo.Joker = true; bingo.Bingo = true;
            block.Compose(bingo);
            TestAssert.Eq(Row(block.Rows[3]), "FUEL 65MIN BINGO", "Both: BINGO is the louder word");
            NavInput parked = all; parked.EnduranceS = float.PositiveInfinity;
            TestAssert.That(block.Compose(parked) == 4 && block.Rows[3].Kind == NavRowKind.Clock, "A parked aircraft (endless endurance) with no flag: no FUEL row");
            parked.Bingo = true;
            block.Compose(parked);
            TestAssert.Eq(Row(block.Rows[3]), "FUEL -- BINGO", "...but BINGO is shown with the endurance unknown");
            NavInput noFuel = all; noFuel.HasFuel = false;
            TestAssert.That(block.Compose(noFuel) == 4 && block.Rows[3].Kind == NavRowKind.Clock, "No fuel reading: no FUEL row");
        }

        /// <summary>Every row, whatever the names, is no more than <see cref="NavBlock.MaxChars"/> characters: 260 units at the 12 unit face's 7.8 a character.</summary>
        private static void Fit()
        {
            TestAssert.That(NavBlock.MaxChars * 7.8f <= NavBlock.Width && (NavBlock.MaxChars + 1) * 7.8f > NavBlock.Width, "33 characters are what 260 units hold");
            var block = new NavBlock();
            string[] names = { "", "A", "ANNEX CLIFF", "WESTERN HIGHWAY STRIP", "HYPERION CLASS CARRIER", "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", "HIGHWAY STRIP 02", "K92 HIGHWAY STRIP WEST" };
            foreach (AvUnits units in new[] { AvUnits.Metric, AvUnits.Imperial })
                foreach (string name in names)
                    foreach (float metres in new[] { 5f, 950f, 1500f, 9000f, 55000f, 190000f })
                    {
                        NavInput input = Full();
                        input.Units = units; input.HomeName = name; input.TargetName = name; input.HomeM = metres; input.TargetM = metres; input.Bingo = true;
                        int n = block.Compose(input);
                        for (int i = 0; i < n; i++)
                        {
                            NavRow r = block.Rows[i];
                            TestAssert.That(r.Chars <= NavBlock.MaxChars, "'" + Row(r) + "' is " + r.Chars + " characters, over " + NavBlock.MaxChars + " (" + name + ", " + metres + " m, " + units + ")");
                            TestAssert.That(Row(r).IndexOf((char)0x2026) < 0 && !Row(r).Contains("..."), "'" + Row(r) + "' is cut with no ellipsis");
                        }
                    }
            // The name is the whole words that fit, and the bearing and distance always survive.
            NavInput long1 = Full(); long1.HomeName = "WESTERN HIGHWAY STRIP";
            block.Compose(long1);
            TestAssert.That(Row(block.Rows[0]).EndsWith("097 / 23KM"), "The bearing and distance are never cut: " + Row(block.Rows[0]));
            TestAssert.Eq(Row(block.Rows[0]), "HOME WESTERN HIGHWAY 097 / 23KM", "A long base name loses its last words, not its numbers");
        }

        private static void At(float actual, float expected, string what) => TestAssert.That(Math.Abs(actual - expected) < 0.01f, what + ": " + actual + ", want " + expected);

        /// <summary>The block's box: 260 wide, 24 in from the screen's right and bottom edges, rows 18 apart, growing upwards, its caption row a shelf 6 clear of the first row.</summary>
        private static void Layout()
        {
            CornerLayout.NavPlan p = CornerLayout.Nav(1920f, 1080f, 4, true);
            At(p.Right, 1896f, "The block's right edge is 24 from the screen's");
            At(p.Left, 1636f, "...260 wide");
            At(p.Bottom, 1056f, "...and its bottom 24 from the screen's");
            At(p.Height, 24f + 18f * 3f + 6f, "Four rows and a shelf: 84 high");
            At(p.RowY(3) + 6f, p.Bottom, "The last row's glyphs end at the block's bottom");
            At(p.FirstRowY - (p.Top + 6f) - 12f, 6f, "The first row's glyphs (12 high) stand 6 under the caption's");
            At(p.RowY(1) - p.RowY(0), 18f, "Rows are 18 apart: 6 clear between 12 high glyphs");
            TestAssert.That(p.Shelf && p.Present && p.Rows == 4, "A plan with its shelf");

            CornerLayout.NavPlan lean = CornerLayout.Nav(1920f, 1080f, 4, false);
            At(lean.Bottom, 1056f, "Lean: the same bottom");
            At(lean.Height, 6f + 18f * 3f + 6f, "...no caption row: 66 high");
            At(lean.FirstRowY, lean.Top + 6f, "...the first row 6 under the top");
            At(lean.RowY(3) + 6f, lean.Bottom, "...and the last still ends at the bottom margin");

            CornerLayout.NavPlan one = CornerLayout.Nav(1920f, 1080f, 1, true);
            At(one.Height, 30f, "One row and a shelf: 30 high");
            TestAssert.That(!CornerLayout.Nav(1920f, 1080f, 0, true).Present, "No rows: no plan");

            // At 1280 x 720 the unit is 0.85 of a pixel: the screen is 1505.9 x 847.1 design units, the margins stay 24 units.
            CornerLayout.NavPlan small = CornerLayout.Nav(1280f / 0.85f, 720f / 0.85f, 3, true);
            At(small.Right, 1280f / 0.85f - 24f, "720p: the right edge is still 24 units in");
            At(small.Bottom, 720f / 0.85f - 24f, "...and the bottom");
            TestAssert.That(small.Left > 0f && small.Top > 0f, "...and it is on the screen");

            // The cloud's rectangle: as wide as the block, its height up to the next step of 16 from the bottom.
            BoardRect shade = CornerLayout.NavShade(p);
            At(shade.W, 260f, "The cloud's rectangle is the block's width");
            At(shade.H, 96f, "...its height rounded up to the next 16 (84 -> 96)");
            At(shade.Bottom, p.Bottom, "...from the bottom margin up");
            TestAssert.That(!CornerLayout.NavShade(default).Present, "No block, no cloud");
            // A row coming or going inside a step does not move the cloud's rectangle.
            CornerLayout.NavPlan five = CornerLayout.Nav(1920f, 1080f, 5, true);
            TestAssert.That(CornerLayout.NavShade(five).H > shade.H && CornerLayout.NavShade(CornerLayout.Nav(1920f, 1080f, 3, true)).H < shade.H, "Taller blocks have taller clouds");
        }
    }
}
