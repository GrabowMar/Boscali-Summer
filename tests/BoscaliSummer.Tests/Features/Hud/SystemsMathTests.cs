using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The SYSTEMS page's rules, the scope's words and its palette.</summary>
    internal static class SystemsMathTests
    {
        public static void Run()
        {
            Headlines();
            Fuel();
            Zones();
            Words();
            GaugeHold();
            BaseLine();
            Palette();
        }

        private static DamageState Dmg(DamageFlags flags = DamageFlags.None, int engines = 2, int out_ = 0, float detached = 0f) =>
            new DamageState { Flags = flags, Engines = engines, EnginesOut = out_, DetachedRatio = detached };

        private static FuelState Fuel(float level, bool bingo = false, bool joker = false) =>
            new FuelState { Valid = true, Level = level, Bingo = bingo, Joker = joker, HasNeed = true, Need = 0.14f, EnduranceS = 840f };

        private static void Headlines()
        {
            TestAssert.Eq(SystemsMath.Headline(Dmg(), Fuel(0.5f)), SystemsHeadline.Nominal, "Cruise reads NOMINAL");
            TestAssert.Eq(SystemsMath.Headline(Dmg(DamageFlags.Fire | DamageFlags.FuelLeak, out_: 1), Fuel(0.1f, true, true)), SystemsHeadline.Fire, "Fire outranks everything");
            TestAssert.Eq(SystemsMath.Headline(Dmg(out_: 1), Fuel(0.1f, true, true)), SystemsHeadline.Bingo, "BINGO outranks a dead engine");
            TestAssert.Eq(SystemsMath.Headline(Dmg(DamageFlags.FuelLeak, out_: 1), Fuel(0.5f)), SystemsHeadline.EngineOut, "A dead engine outranks a leak");
            TestAssert.Eq(SystemsMath.Headline(Dmg(DamageFlags.FuelLeak), Fuel(0.25f, false, true)), SystemsHeadline.FuelLeak, "A leak outranks JOKER");
            TestAssert.Eq(SystemsMath.Headline(Dmg(), Fuel(0.25f, false, true)), SystemsHeadline.Joker, "JOKER");
            TestAssert.Eq(SystemsMath.Headline(Dmg(detached: 0.12f), Fuel(0.5f)), SystemsHeadline.Structure, "Lost structure shows once it cautions");
            TestAssert.Eq(SystemsMath.Headline(Dmg(detached: 0.05f), Fuel(0.5f)), SystemsHeadline.Nominal, "A scratch is nominal");

            TestAssert.That(SystemsMath.HeadlineTone(SystemsHeadline.Fire, Dmg()) == ScopeTone.Red && SystemsMath.HeadlineTone(SystemsHeadline.Bingo, Dmg()) == ScopeTone.Red && SystemsMath.HeadlineTone(SystemsHeadline.EngineOut, Dmg()) == ScopeTone.Red, "Fire, BINGO and a dead engine are warnings");
            TestAssert.That(SystemsMath.HeadlineTone(SystemsHeadline.FuelLeak, Dmg()) == ScopeTone.Amber && SystemsMath.HeadlineTone(SystemsHeadline.Joker, Dmg()) == ScopeTone.Amber, "A leak and JOKER are cautions");
            TestAssert.That(SystemsMath.HeadlineTone(SystemsHeadline.Nominal, Dmg()) == ScopeTone.Dim, "Nominal is a quiet word");
            TestAssert.That(SystemsMath.HeadlineTone(SystemsHeadline.Structure, Dmg(detached: 0.3f)) == ScopeTone.Red && SystemsMath.HeadlineTone(SystemsHeadline.Structure, Dmg(detached: 0.15f)) == ScopeTone.Amber, "Structure warns past 25 percent, cautions past 10");
            TestAssert.That(SystemsMath.HeadlineText(SystemsHeadline.EngineOut, Dmg(out_: 1)) == "ENG 1 OUT" && SystemsMath.HeadlineText(SystemsHeadline.EngineOut, Dmg(engines: 1, out_: 1)) == "ENG OUT", "The engine words match the damage lamps");
            TestAssert.That(SystemsMath.HeadlineText(SystemsHeadline.Structure, Dmg(detached: 0.3f)) == "STR 70%", "Structure says what is left");
            TestAssert.That(SystemsMath.Abnormal(Dmg(), Fuel(0.5f)) == false && SystemsMath.Abnormal(Dmg(), Fuel(0.2f, false, true)), "The page is live only while something is wrong");
        }

        private static void Fuel()
        {
            FuelState f = Fuel(0.30f);
            TestAssert.Eq(SystemsMath.Percent(0.299f), 30, "Percent rounds");
            TestAssert.That(SystemsMath.EnduranceMinutes(840f, out int min) && min == 14, "Endurance in whole minutes");
            TestAssert.That(!SystemsMath.EnduranceMinutes(float.PositiveInfinity, out _) && !SystemsMath.EnduranceMinutes(float.NaN, out _) && !SystemsMath.EnduranceMinutes(400000f, out _), "Unknown endurance has no words");
            TestAssert.That(SystemsMath.EndTone(200f) == ScopeTone.Red && SystemsMath.EndTone(500f) == ScopeTone.Amber && SystemsMath.EndTone(900f) == ScopeTone.Dim && SystemsMath.EndTone(float.PositiveInfinity) == ScopeTone.Dim, "The endurance reads red under 5 minutes, amber under 10");
            TestAssert.Eq(SystemsMath.MarginPoints(f), 16, "Margin over BINGO in points");
            TestAssert.That(SystemsMath.MarginTone(16) == ScopeTone.Ink && SystemsMath.MarginTone(15) == ScopeTone.Amber && SystemsMath.MarginTone(0) == ScopeTone.Red && SystemsMath.MarginTone(-3) == ScopeTone.Red, "Margin tones");
            TestAssert.That(SystemsMath.BingoMarkTone(Fuel(0.30f)) == ScopeTone.Dim && SystemsMath.BingoMarkTone(Fuel(0.19f)) == ScopeTone.Red, "The BINGO mark stays dim until it matters");
            TestAssert.That(SystemsMath.JokerMarkTone(Fuel(0.40f)) == ScopeTone.Dim && SystemsMath.JokerMarkTone(Fuel(0.34f)) == ScopeTone.Amber, "...and so does JOKER");
            TestAssert.Near(SystemsMath.BurnPointsPerMinute(f), 30f / 14f, "Burn rate in points per minute");
            TestAssert.Near(SystemsMath.BurnPointsPerMinute(new FuelState { Level = 0.3f, EnduranceS = float.PositiveInfinity }), 0f, "No endurance, no rate");
            TestAssert.That(SystemsMath.ToneOf(HudTone.Warning) == ScopeTone.Red && SystemsMath.ToneOf(HudTone.Caution) == ScopeTone.Amber && SystemsMath.ToneOf(HudTone.Info) == ScopeTone.Ink, "Tones map to the scope's");
            TestAssert.That(SystemsMath.ToneOf(LampState.Warning) == ScopeTone.Red && SystemsMath.ToneOf(LampState.Caution) == ScopeTone.Amber && SystemsMath.ToneOf(LampState.On) == ScopeTone.Ink && SystemsMath.ToneOf(LampState.Dark) == ScopeTone.Ink, "Lamp states map to the scope's");
        }

        private static void Zones()
        {
            TestAssert.That(SystemsMath.SideOf(-2f, 1f) == (byte)ZoneSide.Left && SystemsMath.SideOf(2f, 1f) == (byte)ZoneSide.Right && SystemsMath.SideOf(0.3f, 1f) == (byte)ZoneSide.Centre, "A part sits left, right or on the centreline");
            TestAssert.That(SystemsMath.SideOf(-1f, 1f) == (byte)ZoneSide.Centre, "The band edge is the centreline");
            TestAssert.That(SystemsMath.JamWord(new CmStation { Active = true }) == "ON" && SystemsMath.JamWord(new CmStation { Charge = 1f }) == "RDY" && SystemsMath.JamWord(new CmStation { Charge = 0.5f }) == "CHG", "The jammer says ready, on or charging");
            DamageState a = Dmg(), b = Dmg();
            TestAssert.That(SystemsMath.SameDamage(a, b), "Equal readings are the same");
            b.Leak = 1;
            TestAssert.That(!SystemsMath.SameDamage(a, b), "A new leak zone is a change");
            b = Dmg(detached: 0.001f);
            TestAssert.That(SystemsMath.SameDamage(a, b), "A change under one percent of structure is not");
        }

        private static void Words()
        {
            TestAssert.That(ScopeStr.Int(7) == "7" && ReferenceEquals(ScopeStr.Int(7), ScopeStr.Int(7)) && ScopeStr.Int(5000) == "5000", "Integers are cached");
            TestAssert.That(ScopeStr.Pct(30) == "30%" && ReferenceEquals(ScopeStr.Pct(30), ScopeStr.Pct(30)) && ScopeStr.Pct(-4) == "0%" && ScopeStr.Pct(130) == "100%", "Percents are cached and clamped");
            TestAssert.That(ScopeStr.Cat("END ", 14, " MIN").ToString() == "END 14 MIN", "Cat composes text and an integer");
            TestAssert.That(ScopeStr.Cat("BINGO ", 14, "%  JOKER ", 29, "%").ToString() == "BINGO 14%  JOKER 29%", "Cat composes two integers");
            TestAssert.That(ScopeStr.Cat("LEAK -", 4.62, 1, "%/M").ToString() == "LEAK -4.6%/M", "Cat composes a decimal");
            TestAssert.That(ScopeStr.Cat("", -3, null).ToString() == "-3", "Cat writes a negative integer");
            TestAssert.That(ScopeStr.Distance("k92", 22000f, AvUnits.Metric, 20).ToString() == "K92 22KM", "A distance reads in the player's units, upper case: " + ScopeStr.Distance("k92", 22000f, AvUnits.Metric, 20).ToString());
            string cut = ScopeStr.Distance("HighwayStrip2", 23000f, AvUnits.Metric, 10).ToString();
            TestAssert.That(cut.Length <= 10 && cut.EndsWith("23KM") && cut.Contains("…"), "A long name is cut so the distance always shows: " + cut);
            TestAssert.Eq(SystemsMath.WindFromDegrees(0f), 360, "North reads 360");
            TestAssert.Eq(SystemsMath.WindFromDegrees(270f), 270, "270 reads 270");
            TestAssert.Eq(SystemsMath.WindFromDegrees(-90f), 270, "A negative bearing wraps");
        }

        /// <summary>The G gauge is live from 2.5 G and for 3 s after the load last was that high; level cruise has none.</summary>
        private static void GaugeHold()
        {
            float at = float.NegativeInfinity;
            TestAssert.That(!SystemsMath.GaugeLive(1f, 100f, ref at), "Level cruise: no G gauge");
            TestAssert.That(!SystemsMath.GaugeLive(2.4f, 100.1f, ref at), "Under 2.5 G: none");
            TestAssert.That(SystemsMath.GaugeLive(2.5f, 100.2f, ref at), "At 2.5 G it is live");
            TestAssert.That(SystemsMath.GaugeLive(1f, 101f, ref at) && SystemsMath.GaugeLive(1f, 102.9f, ref at), "Back to 1 G it stays for the hold");
            TestAssert.That(!SystemsMath.GaugeLive(1f, 103.3f, ref at), "...3 s after the load last was high, it goes");
            TestAssert.That(!SystemsMath.GaugeLive(1f, 110f, ref at), "...and stays gone");
            TestAssert.That(SystemsMath.GaugeLive(-3f, 111f, ref at), "A negative load counts by its size");
            TestAssert.That(SystemsMath.GaugeLive(1f, 113.5f, ref at), "A new pull restarts the hold: 2.5 s after it, still live");
            TestAssert.That(SystemsMath.GaugeLive(6f, 120f, ref at) && SystemsMath.GaugeLive(5f, 120.1f, ref at) && SystemsMath.GaugeLive(1f, 122.5f, ref at), "A hard pull keeps it up, and its hold runs from the last high tick");
            TestAssert.That(!SystemsMath.GaugeLive(float.NaN, 130f, ref at), "A load that is not a number is not a pull");
        }

        /// <summary>The home base on the compact strip: the name has the line, the margin figure gives way, and the two never touch.</summary>
        private static void BaseLine()
        {
            const int Line = 17;       // the strip's base line holds 17 characters of the board's face
            string[] names = { "K92", "ANNEX CLIFF", "AIRBASE CITY NORTH", "AIRBASE CITY NORTH SECTOR 12" };
            TestAssert.Eq(names[3].Length, 28, "The long test name is 28 characters");
            foreach (AvUnits units in new[] { AvUnits.Metric, AvUnits.Imperial })
                foreach (float metres in new[] { 97f, 800f, 4500f, 19000f, 123000f })
                    foreach (int margin in new[] { 90, 100, 15, 0, -5 })
                        foreach (string name in names)
                        {
                            int full = SystemsMath.MarginWords(margin, false).Length, shortChars = SystemsMath.MarginWords(margin, true).Length;
                            int form = ScopeStr.FitMargin(name, metres, units, Line, full, shortChars, out int room);
                            string line = ScopeStr.Distance(name, metres, units, room).ToString();
                            string tag = name + " " + metres + " " + units + " margin " + margin + " form " + form + ": [" + line + "]";
                            string distance = line.Substring(line.LastIndexOf(' ') + 1);
                            int marginChars = form == 0 ? full : form == 1 ? shortChars : 0;
                            TestAssert.That(line.Length + (form < 2 ? ScopeStr.MarginGap + marginChars : 0) <= Line, "The base line, a gap and the margin fit the line: " + tag);
                            TestAssert.That(form == 2 || line.Length + ScopeStr.MarginGap + marginChars <= Line, "A margin shown always leaves its blank: " + tag);
                            TestAssert.That(distance.Length > 0 && char.IsDigit(distance[0]), "The distance is never cut: " + tag);
                            bool cut = line.Contains("…");
                            // Name priority: a name is only cut when it cannot fit the line even with no margin at all.
                            int whole = name.Length + 1 + distance.Length;
                            TestAssert.That(cut == (whole > Line && line.Length > distance.Length), "A name is cut only when it cannot fit without the margin: " + tag);
                            if (whole <= Line) TestAssert.That(line == name + " " + distance, "A name that fits is whole: " + tag);
                            if (cut) TestAssert.That(form == 2, "A cut name has no margin beside it: " + tag);
                        }

            // The strip as the pilot sees it: 97 m from home, the margin 90 points.
            int f0 = ScopeStr.FitMargin("ANNEX CLIFF", 97f, AvUnits.Metric, Line, 4, 3, out int r0);
            TestAssert.That(f0 == 2 && ScopeStr.Distance("ANNEX CLIFF", 97f, AvUnits.Metric, r0).ToString() == "ANNEX CLIFF 97M", "ANNEX CLIFF keeps its name and gives up the margin");
            int f1 = ScopeStr.FitMargin("K92", 19000f, AvUnits.Metric, Line, 4, 3, out int r1);
            TestAssert.That(f1 == 0 && ScopeStr.Distance("K92", 19000f, AvUnits.Metric, r1).ToString() == "K92 19KM", "A short name has room for both");
            int f2 = ScopeStr.FitMargin("AIRBASE CITY NORTH", 97f, AvUnits.Metric, Line, 4, 3, out int r2);
            string north = ScopeStr.Distance("AIRBASE CITY NORTH", 97f, AvUnits.Metric, r2).ToString();
            TestAssert.That(f2 == 2 && north.Length == Line && north.EndsWith(" 97M") && north.Contains("…"), "AIRBASE CITY NORTH takes the whole line, cut once: " + north);
            int f3 = ScopeStr.FitMargin("K92", 800f, AvUnits.Metric, Line, SystemsMath.MarginWords(0, false).Length, SystemsMath.MarginWords(0, true).Length, out _);
            TestAssert.Eq(f3, 1, "NO MARGIN gives way to NO MGN before it goes: the warning stays beside a short name");
            TestAssert.That(SystemsMath.MarginWords(90, false).ToString() == "+90%" && SystemsMath.MarginWords(90, true).ToString() == "+90" &&
                SystemsMath.MarginWords(0, false).ToString() == "NO MARGIN" && SystemsMath.MarginWords(-4, true).ToString() == "NO MGN", "The margin's words, long and short");
        }

        private static void Palette()
        {
            var pal = new ScopePalette(new Rgba(0.22f, 0.87f, 0.32f), new Rgba(0.95f, 0.85f, 0.29f), new Rgba(1f, 0.31f, 0.27f));
            Rgba ink = pal.Of(ScopeTone.Ink), hi = pal.Of(ScopeTone.Hi), dim = pal.Of(ScopeTone.Dim), faint = pal.Of(ScopeTone.Faint);
            TestAssert.That(hi.R > ink.R && hi.B > ink.B, "Hi is the ink pushed toward white");
            TestAssert.That(dim.G == ink.G && Math.Abs(dim.A - 0.7f) < 1e-5f && faint.G < ink.G, "Dim is the ink at 0.70 (there is no plate to mix it with), faint steps down from it");
            TestAssert.Near(pal.Of(ScopeTone.Dim, 0.5f).A, 0.35f, "A dim token's alpha is multiplied by the alpha asked for");
            TestAssert.That(pal.Of(ScopeTone.Amber).R > 0.9f && pal.Of(ScopeTone.Red).R > 0.9f && pal.Of(ScopeTone.Red).G < 0.4f, "Amber and red are the theme's caution and warning");
            TestAssert.Near(pal.Of(ScopeTone.Ink, 0.5f).A, 0.5f, "A token takes an alpha");
            TestAssert.That(pal.Of(ScopeTone.Bg).G < pal.Of(ScopeTone.Plate).G, "The glass is darker than the plate");
        }
    }
}
