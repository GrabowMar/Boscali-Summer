using BoscaliSummer.Core.Contracts;
using BoscaliSummer.Modules.Hud.Domain;
using NOAvionics;

namespace BoscaliSummer.Tests.Features.Hud
{
    /// <summary>The words of the missile-cam readout.</summary>
    internal static class MissileCamHudTests
    {
        public static void Run()
        {
            TestAssert.That(MissileCamText.StateOf(1) == SeekerState.Locked && MissileCamText.StateOf(2) == SeekerState.Search &&
                MissileCamText.StateOf(3) == SeekerState.Passive && MissileCamText.StateOf(0) == SeekerState.Unknown, "Seeker modes map one to one");

            var d = new MissileCamData
            {
                Valid = true, Index = 0, Count = 2, Weapon = "AIM-120", Seeker = "SARH", State = SeekerState.Locked,
                HasTarget = true, Target = "MiG-29", RangeM = 2400f, ClosureMps = 233.3f, TtiS = 3.4f,
            };
            TestAssert.That(MissileCamText.Header(d) == "MSL 1/2 · AIM-120", "Header: place in the list and the weapon: " + MissileCamText.Header(d));
            TestAssert.That(MissileCamText.Seeker(d) == "SARH · LOCKED", "A locked seeker reads LOCKED: " + MissileCamText.Seeker(d));
            TestAssert.That(MissileCamText.Range(d, AvUnits.Metric) == "2.4KM", "Range to go: " + MissileCamText.Range(d, AvUnits.Metric));
            TestAssert.That(MissileCamText.Closure(d, AvUnits.Metric) == "840KM/H", "Closure in the player's units: " + MissileCamText.Closure(d, AvUnits.Metric));
            TestAssert.That(MissileCamText.Tti(d) == "3S", "Time to impact: " + MissileCamText.Tti(d));
            TestAssert.That(MissileCamText.Tone(d) == HudTone.Info, "A locked seeker is routine");

            d.State = SeekerState.Search;
            TestAssert.That(MissileCamText.Seeker(d) == "SARH · SEARCH" && MissileCamText.Tone(d) == HudTone.Caution, "A searching seeker cautions");
            d.State = SeekerState.Passive;
            TestAssert.That(MissileCamText.Seeker(d) == "SARH · PASSIVE", "A passive seeker says so");

            d.HasTarget = false; d.Seeker = "INS"; d.RangeM = float.NaN; d.ClosureMps = float.NaN; d.TtiS = float.NaN;
            TestAssert.That(MissileCamText.Seeker(d) == "INS · NO TGT" && MissileCamText.Tone(d) == HudTone.Caution, "A blind missile says NO TGT");
            TestAssert.That(MissileCamText.Range(d, AvUnits.Metric) == "--" && MissileCamText.Closure(d, AvUnits.Metric) == "--" && MissileCamText.Tti(d) == "--",
                "Unknown numbers read --");

            d.ClosureMps = -40f;
            TestAssert.That(MissileCamText.Closure(d, AvUnits.Metric) == "OPENING", "A target pulling away is OPENING");
            d.TtiS = 0.2f;
            TestAssert.That(MissileCamText.Tti(d) == "1S", "The last instant reads 1 s");
            d.TtiS = 99.4f;
            TestAssert.That(MissileCamText.Tti(d) == "99S", "Under 100 s it reads seconds");
            d.TtiS = 1200f;
            TestAssert.That(MissileCamText.Tti(d) == "20:00", "A cruise missile 20 minutes out reads M:SS: " + MissileCamText.Tti(d));
            d.TtiS = 100f;
            TestAssert.That(MissileCamText.Tti(d) == "1:40", "...from 100 s: " + MissileCamText.Tti(d));
            d.TtiS = float.PositiveInfinity;
            TestAssert.That(MissileCamText.Tti(d) == "--", "An infinite time reads --");
            // The time to go has no 99 s cap (a threat row does), and a missile barely closing has none at all.
            d.TtiS = MissileCamText.TimeToGo(300000f, 250f);
            TestAssert.That(MissileCamText.Tti(d) == "20:00", "300 km at 250 m/s is 20:00: " + MissileCamText.Tti(d));
            d.TtiS = MissileCamText.TimeToGo(5000f, 20f);
            TestAssert.That(MissileCamText.Tti(d) == "4:10", "Closing slowly but closing still counts: " + MissileCamText.Tti(d));
            d.TtiS = MissileCamText.TimeToGo(5000f, 10f);
            TestAssert.That(MissileCamText.Tti(d) == "--" && float.IsNaN(MissileCamText.TimeToGo(5000f, -40f)), "Barely or not closing: --");
            d.Weapon = null; d.Seeker = null; d.HasTarget = true; d.State = SeekerState.Unknown;
            TestAssert.That(MissileCamText.Header(d) == "MSL 1/2" && MissileCamText.Seeker(d) == "SEEKER · TRACK", "Missing names degrade to plain words");
            Words();
        }

        private static void Words()
        {
            TestAssert.That(MissileCamMath.ModeWord(CamMode.Blind, true) == "BLIND" && MissileCamMath.ModeWord(CamMode.Blind, false) == "NO TGT", "Blind is BLIND only for a weapon that had a target");
            TestAssert.That(MissileCamMath.ModeWord(CamMode.DataLink, false) == "DLINK" && MissileCamMath.ModeWord(CamMode.Locked, true) == "LOCKED" &&
                MissileCamMath.ModeWord(CamMode.Search, true) == "SEARCH" && MissileCamMath.ModeWord(CamMode.Lost, false) == "LOST", "The other modes keep their words");
            TestAssert.That(MissileCamMath.Caution(CamMode.Blind, true) && MissileCamMath.Caution(CamMode.Search, true), "Blind or searching on a given target cautions");
            TestAssert.That(!MissileCamMath.Caution(CamMode.Blind, false) && !MissileCamMath.Caution(CamMode.Search, false), "A weapon that never had a target gives no caution");
            TestAssert.That(!MissileCamMath.Caution(CamMode.Locked, true) && !MissileCamMath.Caution(CamMode.DataLink, true) && !MissileCamMath.Caution(CamMode.Lost, true), "Locked, data-link and lost are not cautions");
            TestAssert.That(MissileCamMath.ResultWord(ShotState.Hit) == "HIT" && MissileCamMath.ResultWord(ShotState.Miss) == "MISS" && MissileCamMath.ResultWord(ShotState.Defeated) == "DEFEATED", "The result is the shot's");
            TestAssert.That(MissileCamMath.ResultWord(ShotState.InFlight) == "LOST" && MissileCamMath.ResultWord(ShotState.Empty) == "LOST", "No result from the game (or a decoy) reads LOST");
            TestAssert.Near(MissileCamMath.TapeStep(12000f, 1000f), 1000f, "A 12 km tape ticks every km");
            TestAssert.Near(MissileCamMath.TapeStep(14000f, 1000f), 1000f, "Fourteen ticks still fit");
            TestAssert.Near(MissileCamMath.TapeStep(15000f, 1000f), 2000f, "Fifteen do not: 2 km");
            TestAssert.Near(MissileCamMath.TapeStep(40000f, 1000f), 5000f, "A 40 km tape ticks every 5 km");
            TestAssert.Near(MissileCamMath.TapeStep(300f, 1000f), 100f, "A short tape ticks every tenth of the unit");
            TestAssert.Near(MissileCamMath.TapeStep(20000f, 1852f), 1852f, "A 20 km tape in nautical miles ticks every nm");
        }
    }
}
