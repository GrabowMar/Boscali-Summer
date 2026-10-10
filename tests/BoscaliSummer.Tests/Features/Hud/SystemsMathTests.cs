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
            int[][] dims = { new[] { 48, 12, 4 }, new[] { 30, 10, 3 }, new[] { 72, 12, 6 }, new[] { 1, 10, 1 }, new[] { 0, 10, 1 } };
            foreach (int[] d in dims)
            {
                SystemsMath.GridDims(d[0], out int cols, out int rows);
                TestAssert.That(cols == d[1] && rows == d[2], "A magazine of " + d[0] + " is " + cols + "x" + rows);
            }
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

        private static void Palette()
        {
            var pal = new ScopePalette(new Rgba(0.22f, 0.87f, 0.32f), new Rgba(0.95f, 0.85f, 0.29f), new Rgba(1f, 0.31f, 0.27f));
            Rgba ink = pal.Of(ScopeTone.Ink), hi = pal.Of(ScopeTone.Hi), dim = pal.Of(ScopeTone.Dim), faint = pal.Of(ScopeTone.Faint);
            TestAssert.That(hi.R > ink.R && hi.B > ink.B, "Hi is the ink pushed toward white");
            TestAssert.That(dim.G < ink.G && dim.G > faint.G, "Dim and faint step down from the ink");
            TestAssert.That(pal.Of(ScopeTone.Amber).R > 0.9f && pal.Of(ScopeTone.Red).R > 0.9f && pal.Of(ScopeTone.Red).G < 0.4f, "Amber and red are the theme's caution and warning");
            TestAssert.Near(pal.Of(ScopeTone.Ink, 0.5f).A, 0.5f, "A token takes an alpha");
            TestAssert.That(pal.Of(ScopeTone.Bg).G < pal.Of(ScopeTone.Plate).G, "The glass is darker than the plate");
        }
    }
}
