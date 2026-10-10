using System;
using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using BoscaliSummer.Modules.Wing.Domain;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>Burn rate, endurance and BINGO / JOKER, and that they match the Wing module's rule exactly.</summary>
    internal static class FuelEnduranceTests
    {
        public static void Run()
        {
            Endurance();
            Flags();
            ParityWithWing();
            Words();
        }

        private static void Endurance()
        {
            var fuel = new FuelEndurance();
            TestAssert.That(float.IsInfinity(fuel.EnduranceSeconds) && float.IsNaN(fuel.Level), "Before any reading nothing is known");
            fuel.Update(1.0f, float.NaN, 200f, 1f);
            TestAssert.That(float.IsInfinity(fuel.EnduranceSeconds), "One reading has no burn rate yet");
            // 0.0005 of the tanks per second: 2000 s to dry from full.
            float level = 1f;
            for (int i = 0; i < 600; i++) { level -= 0.0005f; fuel.Update(level, float.NaN, 200f, 1f); }
            TestAssert.Near(fuel.RatePerSecond * 1000f, 0.5f, "The burn rate is learned (fraction per second)");
            TestAssert.That(Math.Abs(fuel.EnduranceSeconds - level / 0.0005f) < 5f, "Endurance is the level over the rate: " + fuel.EnduranceSeconds);
            TestAssert.That(!fuel.Bingo && !fuel.Joker, "With no field known neither flag can be set");

            float before = fuel.RatePerSecond;
            fuel.Update(level - 0.05f, float.NaN, 200f, 1f, afterburner: true);
            TestAssert.Near(fuel.RatePerSecond, before, "A burst of afterburner does not skew the learned rate");

            fuel.Reset();
            TestAssert.That(float.IsNaN(fuel.Level) && float.IsNaN(fuel.RatePerSecond) && !fuel.Bingo, "A new aircraft forgets everything");
        }

        private static void Flags()
        {
            var fuel = new FuelEndurance();
            // 0.001 per second, a field 100 km away at 200 m/s: 500 s home = 0.5 of the tanks + 0.1 reserve = 0.6 needed.
            float level = 1f;
            bool tripped = false, sawJoker = false;
            for (int i = 0; i < 600; i++)
            {
                level -= 0.001f;
                if (fuel.Update(level, 100000f, 200f, 1f)) tripped = true;
                if (fuel.JokerNow) sawJoker = true;
                if (level > 0.75f) TestAssert.That(!fuel.Joker && !fuel.Bingo, "Plenty of fuel: no flag at " + level);
            }
            TestAssert.That(sawJoker, "JOKER is announced on the way down");
            TestAssert.That(tripped && fuel.Bingo, "BINGO trips once the fuel no longer covers the way home");
            TestAssert.That(level < 0.6f, "...which is when the tanks hold less than the need");

            // The same fuel nearer a field is comfortable again; refuelling clears both flags.
            fuel.Update(level, 5000f, 200f, 1f);
            TestAssert.That(!fuel.Bingo, "Close to a field the flight home is cheap: BINGO clears");
            fuel.Update(1f, 100000f, 200f, 1f);
            TestAssert.That(!fuel.Bingo && !fuel.Joker, "A refuel clears both");

            TestAssert.That(FuelEndurance.Tone(0.8f, false, false) == HudTone.Info, "Plenty is routine");
            TestAssert.That(FuelEndurance.Tone(0.2f, false, false) == HudTone.Caution && FuelEndurance.Tone(0.9f, false, true) == HudTone.Caution, "Low or JOKER cautions");
            TestAssert.That(FuelEndurance.Tone(0.05f, false, false) == HudTone.Warning && FuelEndurance.Tone(0.9f, true, true) == HudTone.Warning, "Nearly empty or BINGO warns");
            TestAssert.That(FuelEndurance.CautionLevel == 0.25f && FuelEndurance.WarningLevel == 0.10f, "The thresholds are the fuel line's own");
        }

        /// <summary>The same sequences through the Wing module's BingoMonitor and ours: every flag, every step.</summary>
        private static void ParityWithWing()
        {
            for (int seed = 1; seed <= 40; seed++)
            {
                uint state = (uint)seed * 2654435761u;
                float Next() { state = state * 1664525u + 1013904223u; return (state >> 8) / (float)(1 << 24); }
                var wing = new BingoMonitor();
                var ours = new FuelEndurance();
                float fuel = 1f, burn = 0.0003f + 0.0017f * Next(), distance = 5000f + 120000f * Next(), cruise = 120f + 130f * Next();
                for (int step = 0; step < 700; step++)
                {
                    if (step % 50 == 0) { burn = 0.0003f + 0.0017f * Next(); distance = 5000f + 120000f * Next(); }
                    bool afterburner = Next() < 0.1f;
                    fuel = Math.Max(0f, fuel - burn * (afterburner ? 3f : 1f));
                    if (step == 600) fuel = 1f;   // a refuel
                    bool w = wing.Update(fuel, distance, cruise, 1f, afterburner);
                    bool o = ours.Update(fuel, distance, cruise, 1f, afterburner);
                    string at = " (seed " + seed + ", step " + step + ")";
                    TestAssert.That(w == o && wing.Bingo == ours.Bingo && wing.Joker == ours.Joker && wing.JokerNow == ours.JokerNow,
                        "BINGO/JOKER match the Wing rule" + at);
                    TestAssert.That(wing.SecondsToBingo == ours.SecondsToBingo || (float.IsInfinity(wing.SecondsToBingo) && float.IsInfinity(ours.SecondsToBingo)),
                        "Seconds to bingo match" + at);
                }
            }
        }

        private static void Words()
        {
            TestAssert.That(FuelText.Endurance(2520f) == "42MIN" && FuelText.Endurance(30f) == "<1MIN", "Endurance reads in minutes");
            TestAssert.That(FuelText.Endurance(6300f) == "1H45" && FuelText.Endurance(7500f) == "2H05", "...and in hours past a hundred minutes");
            TestAssert.That(FuelText.Endurance(float.PositiveInfinity) == "--" && FuelText.Endurance(float.NaN) == "--", "Unknown endurance reads --");
            TestAssert.That(FuelText.Level(0.38f, 2520f) == "38%  END 42MIN", "The level and what it buys: " + FuelText.Level(0.38f, 2520f));
            TestAssert.That(FuelText.Home("highwaystrip2", 23000f, AvUnits.Metric) == "RTB HIGHWAYSTRIP2 23KM", "Home reads the field and its range: " + FuelText.Home("highwaystrip2", 23000f, AvUnits.Metric));
            TestAssert.That(FuelText.Home(null, float.NaN, AvUnits.Metric) == "RTB --" && FuelText.Home("ALPHA", float.NaN, AvUnits.Metric) == "RTB --", "No field, no range");
            TestAssert.That(FuelText.State(true, true) == "BINGO" && FuelText.State(false, true) == "JOKER" && FuelText.State(false, false) == "", "BINGO outranks JOKER");
            TestAssert.That(FuelText.Legend(true, false, HudTone.Warning) == "BINGO" && FuelText.Legend(false, true, HudTone.Caution) == "JOKER", "The third-person fuel scale says BINGO and JOKER as the board does");
            TestAssert.That(FuelText.Legend(false, false, HudTone.Caution) == "LOW" && FuelText.Legend(false, false, HudTone.Info) == "FUEL", "...LOW when only the tank is low, else FUEL");
        }
    }
}
