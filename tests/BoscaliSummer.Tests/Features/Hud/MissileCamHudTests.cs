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
            Geometry();
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

        /// <summary>The page's pure rules: the seeker's mode, its gauge, the tape, the chain, the profile's apex, the angles off the boresight, the target box and the cut at the view's frame.</summary>
        private static void Geometry()
        {
            TestAssert.That(MissileCamMath.ModeOf(true, false, false, false) == CamMode.Blind && MissileCamMath.ModeOf(false, false, true, false) == CamMode.Blind, "Nothing to guide on is blind, whatever the seeker says");
            TestAssert.That(MissileCamMath.ModeOf(true, false, true, true) == CamMode.Locked && MissileCamMath.ModeOf(false, false, true, true) == CamMode.Locked, "A seeker on its target is locked");
            TestAssert.That(MissileCamMath.ModeOf(true, false, false, true) == CamMode.DataLink, "An active radar before its pitbull flies on the data-link");
            TestAssert.That(MissileCamMath.ModeOf(true, true, false, true) == CamMode.Blind, "An active radar that lost its lock after the pitbull is blind");
            TestAssert.That(MissileCamMath.ModeOf(false, false, false, true) == CamMode.Search, "A semi-active seeker without a lock searches");

            TestAssert.Near(MissileCamMath.Strength(CamMode.Locked, 0f, 35f), 1f, "On the boresight the seeker gauge is full");
            TestAssert.Near(MissileCamMath.Strength(CamMode.Locked, 35f, 35f), 0.15f, "At the cone's edge it is at its floor");
            TestAssert.Near(MissileCamMath.Strength(CamMode.Locked, 20.125f, 35f), 0.5f, "Half way down in between");
            TestAssert.Near(MissileCamMath.Strength(CamMode.Locked, 60f, 0f), 1f, "A seeker with no cone is full while locked");
            TestAssert.That(MissileCamMath.Strength(CamMode.DataLink, 0f, 35f) == 0f && MissileCamMath.Strength(CamMode.Blind, 0f, 35f) == 0f, "No lock, no strength");

            TestAssert.Near(MissileCamMath.TapeFrac(6200f, 12400f), 0.5f, "Half the tape's range is half way down the tape");
            TestAssert.That(MissileCamMath.TapeFrac(20000f, 12400f) == 1f && MissileCamMath.TapeFrac(0f, 12400f) == 0f, "The tape ends at its top range and at the target");
            TestAssert.That(MissileCamMath.TapeFrac(float.NaN, 12400f) == 0f && MissileCamMath.TapeFrac(500f, 0f) == 0f, "An unknown range or an empty tape puts the marker on the target");

            TestAssert.That(MissileCamMath.ChainIndex(ShotPhase.Boost) == 0 && MissileCamMath.ChainIndex(ShotPhase.Mid) == 1 && MissileCamMath.ChainIndex(ShotPhase.Active) == 2 &&
                MissileCamMath.ChainIndex(ShotPhase.Term) == 3, "The chain follows the phases");
            TestAssert.That(MissileCamMath.ChainIndex(ShotPhase.Fall) == -1, "A bomb has no chain");

            int at = MissileCamMath.Apex(new[] { 7000f, 10100f, 9000f, 99999f }, 3, out float apex);
            TestAssert.That(at == 1 && apex == 10100f, "The apex is the highest of the samples in use (got " + at + ", " + apex + ")");
            TestAssert.That(MissileCamMath.Apex(new[] { -40f, -90f }, 2, out apex) == 0 && apex == -40f, "Below sea level still has an apex");
            TestAssert.That(MissileCamMath.Apex(null, 0, out apex) == -1 && apex == 0f, "No path, no apex");

            TestAssert.Near(MissileCamMath.OffBoresight(3f, -4f), 5f, "The angle off the boresight is the length of the two error angles");
            MissileCamMath.Error(0f, 0f, 1f, out float ex, out float ey);
            TestAssert.That(ex == 0f && ey == 0f, "Dead ahead is on the boresight");
            MissileCamMath.Error(1f, 0f, 1f, out ex, out ey);
            TestAssert.Near(ex, 45f, "A target to the right is right");
            TestAssert.Near(ey, 0f, "...and level");
            MissileCamMath.Error(0f, 1f, 1f, out ex, out ey);
            TestAssert.Near(ey, -45f, "A target above is up (y runs down)");
            MissileCamMath.Error(1f, 0f, -1f, out ex, out ey);
            TestAssert.Near(ex, 135f, "Behind the missile reads past 90");

            TestAssert.Near(MissileCamMath.BoxHalf(12400f), 16f, "A far target wears the smallest box");
            TestAssert.Near(MissileCamMath.BoxHalf(2000f), 27f, "At 2 km the box is 18 + 9 units a side from its middle");
            TestAssert.Near(MissileCamMath.BoxHalf(100f), 69f, "It stops growing before it fills the view");
            TestAssert.That(MissileCamMath.BoxHalf(float.NaN) == 16f && MissileCamMath.BoxHalf(0f) == 16f, "No range: the smallest box");

            float x0 = -10f, y0 = 5f, x1 = 30f, y1 = 5f;
            TestAssert.That(MissileCamMath.ClipLine(ref x0, ref y0, ref x1, ref y1, 0f, 0f, 20f, 10f), "A line across the frame is drawn");
            TestAssert.That(x0 == 0f && x1 == 20f && y0 == 5f && y1 == 5f, "...cut at both edges (" + x0 + " to " + x1 + ")");
            x0 = 5f; y0 = 2f; x1 = 15f; y1 = 8f;
            TestAssert.That(MissileCamMath.ClipLine(ref x0, ref y0, ref x1, ref y1, 0f, 0f, 20f, 10f) && x0 == 5f && y0 == 2f && x1 == 15f && y1 == 8f, "A line inside the frame is left alone");
            x0 = 0f; y0 = -5f; x1 = 20f; y1 = 15f;
            TestAssert.That(MissileCamMath.ClipLine(ref x0, ref y0, ref x1, ref y1, 0f, 0f, 20f, 10f), "A slanted line (a rolled horizon) is drawn");
            TestAssert.Near(x0, 5f, "...from where it enters at the top");
            TestAssert.Near(y0, 0f, "...on the edge");
            TestAssert.Near(x1, 15f, "...to where it leaves at the bottom");
            TestAssert.Near(y1, 10f, "...on the edge");
            x0 = -5f; y0 = 20f; x1 = 25f; y1 = 20f;
            TestAssert.That(!MissileCamMath.ClipLine(ref x0, ref y0, ref x1, ref y1, 0f, 0f, 20f, 10f), "A line past the frame is not drawn");
            x0 = 30f; y0 = 0f; x1 = 30f; y1 = 10f;
            TestAssert.That(!MissileCamMath.ClipLine(ref x0, ref y0, ref x1, ref y1, 0f, 0f, 20f, 10f), "Nor is one parallel to an edge outside it");
        }
    }
}
